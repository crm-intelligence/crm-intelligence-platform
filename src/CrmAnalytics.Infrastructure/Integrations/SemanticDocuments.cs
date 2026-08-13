using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;

namespace CrmAnalytics.Infrastructure.Integrations;

public enum SemanticSlotKind
{
    Metric,
    Dimension
}

public sealed record SemanticDocument(
    DataSource Source,
    SemanticSlotKind SlotKind,
    string Key,
    string Text);

/// <summary>
/// Creates the model-facing embedding corpus from the authoritative runtime registry.
/// Physical mappings and SQL-bearing metadata are deliberately never projected.
/// </summary>
public sealed class SemanticDocumentFactory(SemanticCatalogRegistry registry)
{
    public IReadOnlyList<SemanticDocument> CreateDocuments()
    {
        var documents = new List<SemanticDocument>();
        foreach (var source in registry.Sources.OrderBy(item => item.Key))
        {
            documents.AddRange(source.Value.Catalog.Metrics
                .Where(item => item.Value.IsUsable)
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new SemanticDocument(
                    source.Key,
                    SemanticSlotKind.Metric,
                    item.Key,
                    JsonSerializer.Serialize(new
                    {
                        semanticType = "metric",
                        semanticKey = item.Key,
                        businessName = item.Value.Label,
                        businessDescription = item.Value.Description,
                        conceptualAliases = item.Value.Aliases,
                        aggregationSemantics = item.Value.Kind,
                        valueType = item.Value.ValueType,
                        unit = item.Value.Unit
                    }))));

            documents.AddRange(source.Value.Catalog.Dimensions
                .Where(item => item.Value.Selectable || item.Value.Filterable
                    || item.Value.Sortable)
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new SemanticDocument(
                    source.Key,
                    SemanticSlotKind.Dimension,
                    item.Key,
                    JsonSerializer.Serialize(new
                    {
                        semanticType = "dimension",
                        semanticKey = item.Key,
                        businessName = item.Value.Label,
                        businessDescription = item.Value.Description,
                        conceptualAliases = item.Value.Aliases,
                        valueType = item.Value.ValueType,
                        supportedOperations = new
                        {
                            select = item.Value.Selectable,
                            groupBy = item.Value.Groupable,
                            filter = item.Value.Filterable,
                            sort = item.Value.Sortable
                        }
                    }))));
        }

        return documents;
    }

    public string CreateFingerprint()
    {
        var canonical = string.Join('\n', CreateDocuments().Select(document =>
            $"{document.Source}|{document.SlotKind}|{document.Key}|{document.Text}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
