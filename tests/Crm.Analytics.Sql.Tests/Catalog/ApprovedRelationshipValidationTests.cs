using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Tests.Catalog;

public sealed class ApprovedRelationshipValidationTests
{
    [Fact]
    public void ValidRelationship_IsLoadedIntoRegistryGraph()
    {
        var registry = Registry(AllowList(
            ("source_a", Object("source_a", "a_id", Path("a.b", "source_b", "a_id", "b_id"))),
            ("source_b", Object("source_b", "b_id"))));

        var relationship = Assert.Single(
            registry.GetApprovedJoinGraph(DataSource.Dwh).Relationships);
        Assert.Equal("a.b", relationship.Id);
        Assert.Equal("mart.source_a", relationship.LeftPhysicalObject);
        Assert.Equal("mart.source_b", relationship.RightPhysicalObject);
    }

    [Fact]
    public void UnknownSource_IsRejected()
    {
        var exception = Assert.Throws<CatalogValidationException>(() => Registry(AllowList(
            ("source_a", Object("source_a", "a_id", Path("a.missing", "missing", "a_id", "id"))))));

        Assert.Contains("logical/fiziksel obje", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownColumn_IsRejected()
    {
        var exception = Assert.Throws<CatalogValidationException>(() => Registry(AllowList(
            ("source_a", Object("source_a", "a_id", Path("a.b", "source_b", "unknown", "b_id"))),
            ("source_b", Object("source_b", "b_id")))));

        Assert.Contains("JOIN kolonu", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateRelationshipId_IsRejected()
    {
        var exception = Assert.Throws<CatalogValidationException>(() => Registry(AllowList(
            ("source_a", Object("source_a", "id",
                Path("duplicate", "source_b", "id", "id"),
                Path("duplicate", "source_c", "id", "id"))),
            ("source_b", Object("source_b", "id")),
            ("source_c", Object("source_c", "id")))));

        Assert.Contains("benzersiz degil", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CrossRuntimeRelationship_IsRejected()
    {
        var relationship = Path("a.b", "source_b", "id", "id") with
        {
            RightRuntime = DataSource.Oltp
        };
        var exception = Assert.Throws<CatalogValidationException>(() => Registry(AllowList(
            ("source_a", Object("source_a", "id", relationship)),
            ("source_b", Object("source_b", "id")))));

        Assert.Contains("registry sinirini asiyor", exception.Message, StringComparison.Ordinal);
    }

    private static SemanticCatalogRegistry Registry(AllowListDocument allowList) => new(
        new Dictionary<DataSource, SemanticCatalogSource>
        {
            [DataSource.Dwh] = new(
                new MetricCatalogDocument
                {
                    Metrics = new Dictionary<string, MetricDefinition>(),
                    Dimensions = new Dictionary<string, DimensionDefinition>()
                },
                allowList)
        });

    private static AllowListDocument AllowList(
        params (string Name, AllowedObject Object)[] objects) => new()
        {
            Objects = objects.ToDictionary(item => item.Name, item => item.Object),
            MaxJoins = 0
        };

    private static AllowedObject Object(
        string logicalName,
        string joinColumn,
        params JoinPath[] paths) => new()
    {
        PhysicalName = $"mart.{logicalName}",
        Columns = [joinColumn, "scope"],
        ScopeColumn = "scope",
        JoinPaths = paths
    };

    private static JoinPath Path(
        string id,
        string to,
        string leftColumn,
        string rightColumn) => new()
        {
            Id = id,
            To = to,
            LeftColumn = leftColumn,
            RightColumn = rightColumn,
            Cardinality = RelationshipCardinality.ManyToOne,
            LeftRuntime = DataSource.Dwh,
            RightRuntime = DataSource.Dwh,
            AllowedJoinTypes = [ApprovedJoinType.Inner]
        };
}
