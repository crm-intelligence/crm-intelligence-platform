using System.Collections.Immutable;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Tests.Catalog;

public sealed class ApprovedJoinGraphTests
{
    [Fact]
    public void DirectRelationship_ReturnsDirectPath()
    {
        var graph = Graph(("a", "b"));

        var result = graph.FindApprovedJoinPaths("a", "b", 1);

        Assert.Equal(JoinPathResolutionStatus.Found, result.Status);
        var path = Assert.Single(result.Paths);
        Assert.True(path.IsDirect);
        Assert.Equal("a.b", Assert.Single(path.Steps).Relationship.Id);
    }

    [Fact]
    public void MultiHopRelationship_ReturnsConnectedPath()
    {
        var graph = Graph(("a", "b"), ("b", "c"));

        var result = graph.FindApprovedJoinPaths("a", "c", 2);

        Assert.Equal(JoinPathResolutionStatus.Found, result.Status);
        Assert.Equal(["a.b", "b.c"], Assert.Single(result.Paths).Steps
            .Select(step => step.Relationship.Id));
    }

    [Fact]
    public void DisconnectedSources_ReturnNoPath()
    {
        var graph = Graph(("a", "b"), ("c", "d"));

        var result = graph.FindApprovedJoinPaths("a", "d", 3);

        Assert.Equal(JoinPathResolutionStatus.NotFound, result.Status);
        Assert.Empty(result.Paths);
    }

    [Fact]
    public void CyclicGraph_IsCycleSafeAndReturnsSimpleCandidates()
    {
        var graph = Graph(("a", "b"), ("b", "c"), ("c", "a"));

        var result = graph.FindApprovedJoinPaths("a", "c", 3);

        Assert.Equal(JoinPathResolutionStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.Paths.Length);
        Assert.All(result.Paths, path =>
        {
            var sources = path.Steps.Select(step => step.FromSource)
                .Append(path.ToSource)
                .ToArray();
            Assert.Equal(sources.Length, sources.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        });
    }

    [Fact]
    public void MultipleApprovedPaths_ReturnAllCandidatesAsAmbiguous()
    {
        var graph = Graph(
            ("a", "b"), ("b", "d"),
            ("a", "c"), ("c", "d"));

        var result = graph.FindApprovedJoinPaths("a", "d", 2);

        Assert.Equal(JoinPathResolutionStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.Paths.Length);
        Assert.Contains(result.Paths, path =>
            path.Steps.Select(step => step.Relationship.Id)
                .SequenceEqual(["a.b", "b.d"]));
        Assert.Contains(result.Paths, path =>
            path.Steps.Select(step => step.Relationship.Id)
                .SequenceEqual(["a.c", "c.d"]));
    }

    [Fact]
    public void ProposedPath_IsValidatedWithoutSelectingForCaller()
    {
        var graph = Graph(("a", "b"), ("b", "c"));
        var proposal = new JoinPathProposal(
            "a",
            "c",
            ImmutableArray.Create(
                new JoinPathProposalStep("a.b", "a", "b", ApprovedJoinType.Inner),
                new JoinPathProposalStep("b.c", "b", "c", ApprovedJoinType.Inner)));

        var validation = graph.ValidateApprovedJoinPath(proposal);

        Assert.True(validation.IsValid);
        Assert.Equal(JoinPathValidationCode.Valid, validation.Code);
    }

    [Fact]
    public void MaximumHopBudget_IsEnforced()
    {
        var result = Graph(("a", "b")).FindApprovedJoinPaths(
            "a", "b", ApprovedJoinGraph.MaximumHopBudget + 1);

        Assert.Equal(JoinPathResolutionStatus.InvalidRequest, result.Status);
        Assert.Empty(result.Paths);
    }

    [Fact]
    public void DisabledRelationship_IsNotDiscoverable()
    {
        var allowList = AllowListForEdges(("a", "b"));
        var relationship = allowList.Objects["a"].JoinPaths[0] with { Enabled = false };
        allowList = new AllowListDocument
        {
            Objects = new Dictionary<string, AllowedObject>(allowList.Objects)
            {
                ["a"] = new AllowedObject
                {
                    PhysicalName = "mart.a",
                    Columns = ["join_id", "scope"],
                    ScopeColumn = "scope",
                    JoinPaths = [relationship]
                }
            },
            MaxJoins = 0
        };

        var result = ApprovedJoinGraph.Create(DataSource.Dwh, allowList)
            .FindApprovedJoinPaths("a", "b", 1);

        Assert.Equal(JoinPathResolutionStatus.NotFound, result.Status);
    }

    private static ApprovedJoinGraph Graph(params (string Left, string Right)[] edges)
        => ApprovedJoinGraph.Create(DataSource.Dwh, AllowListForEdges(edges));

    private static AllowListDocument AllowListForEdges(
        params (string Left, string Right)[] edges)
    {
        var sourceNames = edges.SelectMany(edge => new[] { edge.Left, edge.Right })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var pathsBySource = edges.GroupBy(edge => edge.Left, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => new JoinPath
                {
                    Id = $"{edge.Left}.{edge.Right}",
                    To = edge.Right,
                    LeftColumn = "join_id",
                    RightColumn = "join_id",
                    Cardinality = RelationshipCardinality.ManyToOne,
                    LeftRuntime = DataSource.Dwh,
                    RightRuntime = DataSource.Dwh,
                    AllowedJoinTypes = [ApprovedJoinType.Inner]
                }).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var objects = sourceNames.ToDictionary(
            source => source,
            source => new AllowedObject
            {
                PhysicalName = $"mart.{source}",
                Columns = ["join_id", "scope"],
                ScopeColumn = "scope",
                JoinPaths = pathsBySource.GetValueOrDefault(source) ?? []
            },
            StringComparer.OrdinalIgnoreCase);

        return new AllowListDocument { Objects = objects, MaxJoins = 0 };
    }
}
