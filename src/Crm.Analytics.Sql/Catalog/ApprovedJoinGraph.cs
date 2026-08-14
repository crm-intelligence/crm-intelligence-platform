using System.Collections.Immutable;
using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Catalog;

/// <summary>
/// Immutable projection of the reviewed relationship contracts in an allow-list.
/// It never infers relationships from object or column names.
/// </summary>
internal sealed class ApprovedJoinGraph
{
    public const int MaximumHopBudget = 8;

    private readonly AllowListDocument allowList;
    private readonly ImmutableArray<ApprovedRelationshipDefinition> relationships;
    private readonly ImmutableDictionary<string, ApprovedRelationshipDefinition> byId;

    private ApprovedJoinGraph(
        DataSource runtime,
        AllowListDocument allowList,
        ImmutableArray<ApprovedRelationshipDefinition> relationships)
    {
        Runtime = runtime;
        this.allowList = allowList;
        this.relationships = relationships;
        byId = relationships.ToImmutableDictionary(
            relationship => relationship.Id,
            StringComparer.OrdinalIgnoreCase);
    }

    public DataSource Runtime { get; }

    public ImmutableArray<ApprovedRelationshipDefinition> Relationships => relationships;

    public static ApprovedJoinGraph Create(DataSource runtime, AllowListDocument allowList)
    {
        ArgumentNullException.ThrowIfNull(allowList);
        var errors = new List<string>();
        var definitions = ImmutableArray.CreateBuilder<ApprovedRelationshipDefinition>();
        var relationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (leftSource, allowedObject) in allowList.Objects)
        {
            foreach (var path in allowedObject.JoinPaths)
            {
                if (!AllowListLoader.IsValidRelationshipId(path.Id))
                {
                    errors.Add($"'{leftSource}' relationship id gecersiz: '{path.Id}'.");
                    continue;
                }

                if (!relationshipIds.Add(path.Id))
                {
                    errors.Add($"Relationship id benzersiz degil: '{path.Id}'.");
                    continue;
                }

                var rightObject = allowList.FindObject(path.To);
                var leftPhysicalObject = allowList.ResolvePhysicalObject(leftSource);
                var rightPhysicalObject = allowList.ResolvePhysicalObject(path.To);
                if (rightObject is null || leftPhysicalObject is null || rightPhysicalObject is null)
                {
                    errors.Add(
                        $"'{path.Id}' relationship allow-listed logical/fiziksel obje referansi gecersiz.");
                    continue;
                }

                if (!allowedObject.HasColumn(path.LeftColumn)
                    || !rightObject.HasColumn(path.RightColumn))
                {
                    errors.Add($"'{path.Id}' relationship allow-listed olmayan JOIN kolonu kullaniyor.");
                    continue;
                }

                if (path.LeftRuntime != runtime || path.RightRuntime != runtime)
                {
                    errors.Add(
                        $"'{path.Id}' relationship '{runtime}' registry sinirini asiyor: " +
                        $"'{path.LeftRuntime}' -> '{path.RightRuntime}'.");
                    continue;
                }

                if (path.AllowedJoinTypes.Count == 0
                    || path.AllowedJoinTypes.Distinct().Count() != path.AllowedJoinTypes.Count
                    || path.AllowedJoinTypes.Any(joinType => !Enum.IsDefined(joinType))
                    || !Enum.IsDefined(path.Cardinality))
                {
                    errors.Add($"'{path.Id}' relationship JOIN tipi contract'i gecersiz.");
                    continue;
                }

                definitions.Add(new ApprovedRelationshipDefinition(
                    path.Id,
                    runtime,
                    leftSource,
                    leftPhysicalObject,
                    path.LeftColumn,
                    path.To,
                    rightPhysicalObject,
                    path.RightColumn,
                    path.Cardinality,
                    path.AllowedJoinTypes.ToImmutableArray(),
                    path.Enabled));
            }
        }

        if (errors.Count > 0)
        {
            throw new CatalogValidationException(
                "Approved relationship graph gecersiz:" + Environment.NewLine
                + string.Join(Environment.NewLine, errors.Select(error => "  - " + error)));
        }

        return new ApprovedJoinGraph(
            runtime,
            allowList,
            definitions.Where(definition => definition.Enabled).ToImmutableArray());
    }

    public JoinPathResolutionResult FindApprovedJoinPaths(
        string fromSource,
        string toSource,
        int maxHops)
    {
        if (string.IsNullOrWhiteSpace(fromSource)
            || string.IsNullOrWhiteSpace(toSource)
            || maxHops is < 1 or > MaximumHopBudget
            || !allowList.HasObject(fromSource)
            || !allowList.HasObject(toSource))
        {
            return JoinPathResolutionResult.Invalid(
                $"Source'lar allow-listed olmali ve maxHops 1-{MaximumHopBudget} araliginda olmali.");
        }

        if (fromSource.Equals(toSource, StringComparison.OrdinalIgnoreCase))
        {
            return JoinPathResolutionResult.Found(
                [new ApprovedJoinPath(fromSource, toSource, [])]);
        }

        var candidates = ImmutableArray.CreateBuilder<ApprovedJoinPath>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { fromSource };
        FindPaths(fromSource, toSource, maxHops, visited, [], candidates);

        var ordered = candidates
            .OrderBy(path => path.Steps.Length)
            .ThenBy(path => string.Join('|', path.Steps.Select(step => step.Relationship.Id)),
                StringComparer.Ordinal)
            .ToImmutableArray();
        return ordered.Length switch
        {
            0 => JoinPathResolutionResult.NotFound(),
            1 => JoinPathResolutionResult.Found(ordered),
            _ => JoinPathResolutionResult.Ambiguous(ordered)
        };
    }

    public JoinPathValidationResult ValidateApprovedJoinPath(JoinPathProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (!allowList.HasObject(proposal.FromSource)
            || !allowList.HasObject(proposal.ToSource)
            || proposal.Steps.Length > MaximumHopBudget)
        {
            return JoinPathValidationResult.Invalid(
                JoinPathValidationCode.InvalidProposal,
                "Proposed path source veya hop budget contract'i gecersiz.");
        }

        var current = proposal.FromSource;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { current };
        foreach (var step in proposal.Steps)
        {
            if (!step.FromSource.Equals(current, StringComparison.OrdinalIgnoreCase)
                || !byId.TryGetValue(step.RelationshipId, out var relationship))
            {
                return JoinPathValidationResult.Invalid(
                    JoinPathValidationCode.UnknownOrDisconnectedRelationship,
                    $"Relationship bilinmiyor veya path baglantisi kopuk: '{step.RelationshipId}'.");
            }

            var forward = relationship.LeftSource.Equals(step.FromSource,
                    StringComparison.OrdinalIgnoreCase)
                && relationship.RightSource.Equals(step.ToSource,
                    StringComparison.OrdinalIgnoreCase);
            var reverse = relationship.RightSource.Equals(step.FromSource,
                    StringComparison.OrdinalIgnoreCase)
                && relationship.LeftSource.Equals(step.ToSource,
                    StringComparison.OrdinalIgnoreCase);
            if (!forward && !reverse)
            {
                return JoinPathValidationResult.Invalid(
                    JoinPathValidationCode.UnknownOrDisconnectedRelationship,
                    $"'{step.RelationshipId}' proposed source ciftini baglamiyor.");
            }

            if (!relationship.AllowedJoinTypes.Contains(step.JoinType)
                || reverse && step.JoinType != ApprovedJoinType.Inner)
            {
                return JoinPathValidationResult.Invalid(
                    JoinPathValidationCode.JoinTypeNotApproved,
                    $"'{step.RelationshipId}' icin proposed JOIN tipi/yonu onayli degil.");
            }

            if (!visited.Add(step.ToSource))
            {
                return JoinPathValidationResult.Invalid(
                    JoinPathValidationCode.CycleDetected,
                    "Proposed path cycle iceriyor.");
            }

            current = step.ToSource;
        }

        return current.Equals(proposal.ToSource, StringComparison.OrdinalIgnoreCase)
            ? JoinPathValidationResult.Valid()
            : JoinPathValidationResult.Invalid(
                JoinPathValidationCode.TargetNotReached,
                "Proposed path hedef source'a ulasmiyor.");
    }

    private void FindPaths(
        string current,
        string target,
        int remainingHops,
        HashSet<string> visited,
        ImmutableArray<ApprovedJoinStep> steps,
        ImmutableArray<ApprovedJoinPath>.Builder candidates)
    {
        if (remainingHops == 0)
        {
            return;
        }

        foreach (var relationship in relationships.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var next = relationship.LeftSource.Equals(current, StringComparison.OrdinalIgnoreCase)
                ? relationship.RightSource
                : relationship.RightSource.Equals(current, StringComparison.OrdinalIgnoreCase)
                    ? relationship.LeftSource
                    : null;
            if (next is null || visited.Contains(next))
            {
                continue;
            }

            var nextSteps = steps.Add(new ApprovedJoinStep(relationship, current, next));
            if (next.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(new ApprovedJoinPath(
                    steps.Length == 0 ? current : steps[0].FromSource,
                    target,
                    nextSteps));
                continue;
            }

            visited.Add(next);
            FindPaths(next, target, remainingHops - 1, visited, nextSteps, candidates);
            visited.Remove(next);
        }
    }
}

internal sealed record ApprovedRelationshipDefinition(
    string Id,
    DataSource Runtime,
    string LeftSource,
    string LeftPhysicalObject,
    string LeftColumn,
    string RightSource,
    string RightPhysicalObject,
    string RightColumn,
    RelationshipCardinality Cardinality,
    ImmutableArray<ApprovedJoinType> AllowedJoinTypes,
    bool Enabled);

internal sealed record ApprovedJoinStep(
    ApprovedRelationshipDefinition Relationship,
    string FromSource,
    string ToSource);

internal sealed record ApprovedJoinPath(
    string FromSource,
    string ToSource,
    ImmutableArray<ApprovedJoinStep> Steps)
{
    public bool IsDirect => Steps.Length == 1;
}

internal enum JoinPathResolutionStatus
{
    Found,
    Ambiguous,
    NotFound,
    InvalidRequest
}

internal sealed record JoinPathResolutionResult(
    JoinPathResolutionStatus Status,
    ImmutableArray<ApprovedJoinPath> Paths,
    string? Detail)
{
    public static JoinPathResolutionResult Found(IEnumerable<ApprovedJoinPath> paths) =>
        new(JoinPathResolutionStatus.Found, paths.ToImmutableArray(), null);

    public static JoinPathResolutionResult Ambiguous(IEnumerable<ApprovedJoinPath> paths) =>
        new(JoinPathResolutionStatus.Ambiguous, paths.ToImmutableArray(),
            "Birden fazla approved join path adayi bulundu; secim backend tarafindan varsayilamaz.");

    public static JoinPathResolutionResult NotFound() =>
        new(JoinPathResolutionStatus.NotFound, [], null);

    public static JoinPathResolutionResult Invalid(string detail) =>
        new(JoinPathResolutionStatus.InvalidRequest, [], detail);
}

internal sealed record JoinPathProposal(
    string FromSource,
    string ToSource,
    ImmutableArray<JoinPathProposalStep> Steps);

internal sealed record JoinPathProposalStep(
    string RelationshipId,
    string FromSource,
    string ToSource,
    ApprovedJoinType JoinType);

internal enum JoinPathValidationCode
{
    Valid,
    InvalidProposal,
    UnknownOrDisconnectedRelationship,
    JoinTypeNotApproved,
    CycleDetected,
    TargetNotReached
}

internal sealed record JoinPathValidationResult(
    bool IsValid,
    JoinPathValidationCode Code,
    string? Detail)
{
    public static JoinPathValidationResult Valid() =>
        new(true, JoinPathValidationCode.Valid, null);

    public static JoinPathValidationResult Invalid(
        JoinPathValidationCode code,
        string detail) => new(false, code, detail);
}
