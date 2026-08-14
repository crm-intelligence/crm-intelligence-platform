using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Catalog;

/// <summary>
/// Authoritative runtime source for semantic catalogs and their backend allow-lists.
/// Parser, model contract, validator and Query Builder receive documents from this registry.
/// </summary>
public sealed class SemanticCatalogRegistry
{
    private readonly IReadOnlyDictionary<DataSource, SemanticCatalogSource> sources;
    private readonly IReadOnlyDictionary<DataSource, ApprovedJoinGraph> joinGraphs;

    public SemanticCatalogRegistry(
        IReadOnlyDictionary<DataSource, SemanticCatalogSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("En az bir semantic catalog source gereklidir.",
                nameof(sources));
        }

        var validatedGraphs = new Dictionary<DataSource, ApprovedJoinGraph>();
        foreach (var (runtime, source) in sources)
        {
            ArgumentNullException.ThrowIfNull(source.Catalog);
            ArgumentNullException.ThrowIfNull(source.AllowList);
            SemanticCatalogMetadataValidator.Validate(source.Catalog);
            validatedGraphs[runtime] = ApprovedJoinGraph.Create(runtime, source.AllowList);
        }

        this.sources = new Dictionary<DataSource, SemanticCatalogSource>(sources);
        joinGraphs = validatedGraphs;
    }

    public IReadOnlyDictionary<DataSource, SemanticCatalogSource> Sources => sources;

    public SemanticCatalogSource GetRequired(DataSource source) =>
        sources.TryGetValue(source, out var value)
            ? value
            : throw new KeyNotFoundException($"Semantic catalog source bulunamadi: '{source}'.");

    internal ApprovedJoinGraph GetApprovedJoinGraph(DataSource source) =>
        joinGraphs.TryGetValue(source, out var value)
            ? value
            : throw new KeyNotFoundException($"Approved join graph source bulunamadi: '{source}'.");

    public static SemanticCatalogRegistry CreateDefault() => new(
        new Dictionary<DataSource, SemanticCatalogSource>
        {
            [DataSource.Dwh] = new(
                MetricCatalogLoader.FromJson(ContractResources.ReadOlistMetricCatalog()),
                AllowListLoader.FromJson(ContractResources.ReadOlistAllowList())),
            [DataSource.Oltp] = new(
                MetricCatalogLoader.FromJson(ContractResources.ReadFabricOltpMetricCatalog()),
                AllowListLoader.FromJson(ContractResources.ReadFabricOltpAllowList()))
        });
}

public sealed record SemanticCatalogSource(
    MetricCatalogDocument Catalog,
    AllowListDocument AllowList);
