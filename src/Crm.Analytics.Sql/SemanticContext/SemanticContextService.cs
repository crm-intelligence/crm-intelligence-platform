using System.Collections.Immutable;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;
using Crm.Analytics.Sql.Parsing;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.SemanticContext;

/// <summary>
/// Produces minimum, read-only semantic/schema grounding from the authoritative registry.
/// No database discovery, model call or SQL execution occurs here.
/// </summary>
internal sealed class SemanticContextService : ISqlSemanticTools
{
    private const int DefaultRelationshipHopBudget = 3;
    private readonly SemanticCatalogRegistry registry;
    private readonly TSqlParserFactory parserFactory;

    public SemanticContextService(
        SemanticCatalogRegistry registry,
        TSqlParserFactory parserFactory)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(parserFactory);
        this.registry = registry;
        this.parserFactory = parserFactory;

        // Context must never project an unvalidated catalog/physical mapping pair.
        foreach (var source in registry.Sources.Values)
        {
            new CatalogValidator(parserFactory).Validate(source.Catalog, source.AllowList);
        }
    }

    public SemanticContextResult<SemanticMetricContext> GetMetricContext(
        DataSource runtime,
        string metricKey)
    {
        var source = registry.GetRequired(runtime);
        var metric = source.Catalog.FindMetric(metricKey);
        if (metric is null)
        {
            return SemanticContextResult<SemanticMetricContext>.Fail(
                SemanticContextFailureCode.UnknownMetric,
                metricKey,
                $"Taninmayan metric semantic key: '{metricKey}'.");
        }

        if (!metric.IsUsable)
        {
            return SemanticContextResult<SemanticMetricContext>.Fail(
                SemanticContextFailureCode.UnusableMetric,
                metricKey,
                $"Metric approved expression tasimiyor: '{metricKey}'.");
        }

        return SemanticContextResult<SemanticMetricContext>.Success(
            ProjectMetric(metricKey, metric));
    }

    public SemanticContextResult<SemanticDimensionContext> GetDimensionContext(
        DataSource runtime,
        string dimensionKey)
    {
        var dimension = registry.GetRequired(runtime).Catalog.FindDimension(dimensionKey);
        return dimension is null
            ? SemanticContextResult<SemanticDimensionContext>.Fail(
                SemanticContextFailureCode.UnknownDimension,
                dimensionKey,
                $"Taninmayan dimension semantic key: '{dimensionKey}'.")
            : SemanticContextResult<SemanticDimensionContext>.Success(
                ProjectDimension(dimensionKey, dimension));
    }

    public SemanticContextResult<SemanticSourceContext> GetSourceContext(
        DataSource runtime,
        string logicalSource)
    {
        var allowList = registry.GetRequired(runtime).AllowList;
        var allowedObject = allowList.FindObject(logicalSource);
        var physicalObject = allowList.ResolvePhysicalObject(logicalSource);
        return allowedObject is null || physicalObject is null
            ? SemanticContextResult<SemanticSourceContext>.Fail(
                SemanticContextFailureCode.UnknownSource,
                logicalSource,
                $"Taninmayan approved logical source: '{logicalSource}'.")
            : SemanticContextResult<SemanticSourceContext>.Success(
                new SemanticSourceContext(
                    logicalSource,
                    physicalObject,
                    runtime,
                    allowedObject.Columns.Order(StringComparer.Ordinal).ToImmutableArray()));
    }

    public SemanticContextResult<SemanticQueryContext> GetQuerySemanticContext(
        DataSource runtime,
        CanonicalQuery query,
        int relationshipHopBudget = DefaultRelationshipHopBudget)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Version != CanonicalQuery.CurrentVersion)
        {
            return SemanticContextResult<SemanticQueryContext>.Fail(
                SemanticContextFailureCode.InvalidCanonicalVersion,
                query.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Desteklenmeyen internal canonical version: '{query.Version}'.");
        }

        var source = registry.GetRequired(runtime);
        var metrics = ImmutableArray.CreateBuilder<SemanticMetricContext>();
        var dimensions = ImmutableArray.CreateBuilder<SemanticDimensionContext>();
        var logicalSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var relevantColumns = new Dictionary<string, HashSet<string>>(
            StringComparer.OrdinalIgnoreCase);

        var metricKeys = query.Metrics
            .Concat(query.Calculations.SelectMany(calculation => calculation.MetricKeys))
            .Concat(query.Ordering
                .Where(ordering => ordering.TargetKind == CanonicalOrderingTargetKind.Metric)
                .Select(ordering => ordering.TargetKey))
            .Distinct(StringComparer.Ordinal);
        foreach (var key in metricKeys)
        {
            var result = GetMetricContext(runtime, key);
            if (!result.IsSuccessful)
            {
                return SemanticContextResult<SemanticQueryContext>.Fail(
                    result.Failure!.Code, result.Failure.SemanticKey, result.Failure.Detail);
            }

            var context = result.Value!;
            metrics.Add(context);
            logicalSources.Add(context.LogicalSource);
            AddColumns(relevantColumns, context.LogicalSource, context.ApprovedExpressionColumns);
        }

        var dimensionKeys = query.Dimensions
            .Concat(query.Filters.Select(filter => filter.Field))
            .Concat(query.Ordering
                .Where(ordering => ordering.TargetKind == CanonicalOrderingTargetKind.Dimension)
                .Select(ordering => ordering.TargetKey))
            .Distinct(StringComparer.Ordinal);
        foreach (var key in dimensionKeys)
        {
            var result = GetDimensionContext(runtime, key);
            if (!result.IsSuccessful)
            {
                return SemanticContextResult<SemanticQueryContext>.Fail(
                    result.Failure!.Code, result.Failure.SemanticKey, result.Failure.Detail);
            }

            var context = result.Value!;
            dimensions.Add(context);
            logicalSources.Add(context.LogicalSource);
            AddColumns(relevantColumns, context.LogicalSource, [context.ApprovedPhysicalColumn]);
        }

        var timeDimensions = source.Catalog.Dimensions
            .Where(item => item.Value.IsTimeDimension
                && logicalSources.Contains(item.Value.Source))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => ProjectDimension(item.Key, item.Value))
            .ToImmutableArray();
        foreach (var timeDimension in timeDimensions)
        {
            AddColumns(relevantColumns, timeDimension.LogicalSource,
                [timeDimension.ApprovedPhysicalColumn]);
        }

        var relationshipsResult = ResolveRelevantRelationships(
            runtime, logicalSources, relationshipHopBudget);
        if (relationshipsResult.Failure is not null)
        {
            return SemanticContextResult<SemanticQueryContext>.Fail(
                relationshipsResult.Failure.Code,
                relationshipsResult.Failure.SemanticKey,
                relationshipsResult.Failure.Detail);
        }

        foreach (var relationship in relationshipsResult.Value!.Items)
        {
            logicalSources.Add(relationship.LeftSource);
            logicalSources.Add(relationship.RightSource);
            AddColumns(relevantColumns, relationship.LeftSource, [relationship.LeftColumn]);
            AddColumns(relevantColumns, relationship.RightSource, [relationship.RightColumn]);
        }

        var sourceContexts = ImmutableArray.CreateBuilder<SemanticSourceContext>();
        foreach (var logicalSource in logicalSources.Order(StringComparer.Ordinal))
        {
            var physicalObject = source.AllowList.ResolvePhysicalObject(logicalSource);
            var allowedObject = source.AllowList.FindObject(logicalSource);
            if (physicalObject is null || allowedObject is null)
            {
                return SemanticContextResult<SemanticQueryContext>.Fail(
                    SemanticContextFailureCode.UnknownSource,
                    logicalSource,
                    $"Semantic mapping approved physical object'e cozumlenemedi: '{logicalSource}'.");
            }

            var columns = relevantColumns.GetValueOrDefault(logicalSource) ?? [];
            if (columns.Any(column => !allowedObject.HasColumn(column)))
            {
                return SemanticContextResult<SemanticQueryContext>.Fail(
                    SemanticContextFailureCode.UnknownSource,
                    logicalSource,
                    $"Semantic mapping allow-listed olmayan kolon iceriyor: '{logicalSource}'.");
            }

            sourceContexts.Add(new SemanticSourceContext(
                logicalSource,
                physicalObject,
                runtime,
                columns.Order(StringComparer.Ordinal).ToImmutableArray()));
        }

        var graph = registry.GetApprovedJoinGraph(runtime);
        return SemanticContextResult<SemanticQueryContext>.Success(
            new SemanticQueryContext(
                runtime,
                metrics.ToImmutable(),
                dimensions.ToImmutable(),
                timeDimensions,
                sourceContexts.ToImmutable(),
                relationshipsResult.Value!.Items,
                new SemanticContextCapabilities(
                    source.AllowList.MaxJoins,
                    graph.Relationships.Length,
                    source.AllowList.MaxJoins > 0,
                    graph.Relationships.Length > 0)));
    }

    public SemanticContextResult<ImmutableRelationshipSet> GetRelationships(
        DataSource runtime,
        IReadOnlyCollection<string> sourceSet)
    {
        ArgumentNullException.ThrowIfNull(sourceSet);
        var allowList = registry.GetRequired(runtime).AllowList;
        var unknown = sourceSet.FirstOrDefault(source => !allowList.HasObject(source));
        if (unknown is not null)
        {
            return SemanticContextResult<ImmutableRelationshipSet>.Fail(
                SemanticContextFailureCode.UnknownSource,
                unknown,
                $"Relationship lookup source allow-listed degil: '{unknown}'.");
        }

        var selected = sourceSet.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationships = registry.GetApprovedJoinGraph(runtime).Relationships
            .Where(relationship => selected.Contains(relationship.LeftSource)
                || selected.Contains(relationship.RightSource))
            .ToImmutableArray();
        return SemanticContextResult<ImmutableRelationshipSet>.Success(
            new ImmutableRelationshipSet(relationships));
    }

    public JoinPathResolutionResult FindApprovedJoinPaths(
        DataSource runtime,
        string fromSource,
        string toSource,
        int maxHops) => registry.GetApprovedJoinGraph(runtime)
            .FindApprovedJoinPaths(fromSource, toSource, maxHops);

    public JoinPathValidationResult ValidateApprovedJoinPath(
        DataSource runtime,
        JoinPathProposal proposal) => registry.GetApprovedJoinGraph(runtime)
            .ValidateApprovedJoinPath(proposal);

    private SemanticMetricContext ProjectMetric(string key, MetricDefinition metric)
    {
        var expression = parserFactory.TryParseExpression(metric.Expression!, out var errors);
        if (expression is null || errors.Count > 0)
        {
            throw new CatalogValidationException(
                $"'{key}' approved expression context projection sirasinda parse edilemedi.");
        }

        var collector = new ExpressionColumnCollector();
        expression.Accept(collector);
        return new SemanticMetricContext(
            key,
            metric.Label,
            metric.Description,
            metric.Aliases.ToImmutableArray(),
            metric.Kind,
            metric.ValueType,
            metric.Unit,
            metric.Expression!,
            collector.Columns.Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            metric.Source,
            metric.CompatibleDimensions.ToImmutableArray(),
            metric.CompatibleFilters.ToImmutableArray(),
            metric.RequiresDateRange);
    }

    private static SemanticDimensionContext ProjectDimension(
        string key,
        DimensionDefinition dimension) => new(
            key,
            dimension.Label,
            dimension.Description,
            dimension.Aliases.ToImmutableArray(),
            dimension.Source,
            dimension.Column,
            dimension.ValueType,
            dimension.Selectable,
            dimension.Groupable,
            dimension.Filterable,
            dimension.Sortable,
            dimension.IsTimeDimension,
            dimension.SemanticRole);

    private SemanticContextResult<ImmutableRelationshipSet> ResolveRelevantRelationships(
        DataSource runtime,
        IReadOnlyCollection<string> logicalSources,
        int maxHops)
    {
        if (maxHops is < 1 or > ApprovedJoinGraph.MaximumHopBudget)
        {
            return SemanticContextResult<ImmutableRelationshipSet>.Fail(
                SemanticContextFailureCode.InvalidRelationshipRequest,
                maxHops.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Relationship hop budget 1-{ApprovedJoinGraph.MaximumHopBudget} araliginda olmali.");
        }

        var graph = registry.GetApprovedJoinGraph(runtime);
        var selected = logicalSources.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var left = 0; left < selected.Length; left++)
        {
            for (var right = left + 1; right < selected.Length; right++)
            {
                var result = graph.FindApprovedJoinPaths(selected[left], selected[right], maxHops);
                if (result.Status == JoinPathResolutionStatus.InvalidRequest)
                {
                    return SemanticContextResult<ImmutableRelationshipSet>.Fail(
                        SemanticContextFailureCode.InvalidRelationshipRequest,
                        $"{selected[left]}->{selected[right]}",
                        result.Detail ?? "Approved join path request gecersiz.");
                }

                foreach (var relationship in result.Paths
                    .SelectMany(path => path.Steps)
                    .Select(step => step.Relationship))
                {
                    ids.Add(relationship.Id);
                }
            }
        }

        return SemanticContextResult<ImmutableRelationshipSet>.Success(
            new ImmutableRelationshipSet(graph.Relationships
                .Where(relationship => ids.Contains(relationship.Id))
                .OrderBy(relationship => relationship.Id, StringComparer.Ordinal)
                .ToImmutableArray()));
    }

    private static void AddColumns(
        IDictionary<string, HashSet<string>> relevantColumns,
        string logicalSource,
        IEnumerable<string> columns)
    {
        if (!relevantColumns.TryGetValue(logicalSource, out var selected))
        {
            selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            relevantColumns[logicalSource] = selected;
        }

        selected.UnionWith(columns);
    }

    private sealed class ExpressionColumnCollector : TSqlFragmentVisitor
    {
        private readonly HashSet<string> columns = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<string> Columns => columns;

        public override void Visit(ColumnReferenceExpression node)
        {
            var identifiers = node.MultiPartIdentifier?.Identifiers;
            if (identifiers is { Count: > 0 })
            {
                columns.Add(identifiers[^1].Value);
            }
        }
    }
}

internal sealed record ImmutableRelationshipSet(
    ImmutableArray<ApprovedRelationshipDefinition> Items);
