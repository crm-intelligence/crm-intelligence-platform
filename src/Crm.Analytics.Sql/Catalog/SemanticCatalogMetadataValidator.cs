namespace Crm.Analytics.Sql.Catalog;

/// <summary>Validates the semantic/model-facing half of a catalog.</summary>
public static class SemanticCatalogMetadataValidator
{
    private static readonly char[] SentencePunctuation = ['.', '!', '?', ';'];

    public static void Validate(MetricCatalogDocument catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var errors = new List<string>();
        var mappingReferences = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, metric) in catalog.Metrics)
        {
            RequireSemanticText("metric", key, metric.Description,
                metric.QueryMappingReference, metric.Aliases, errors);
            if (!mappingReferences.Add(metric.QueryMappingReference))
            {
                errors.Add($"'{key}' metric query mapping reference'i benzersiz degil.");
            }

            foreach (var dimensionKey in metric.CompatibleDimensions
                .Concat(metric.CompatibleFilters).Distinct(StringComparer.Ordinal))
            {
                var dimension = catalog.FindDimension(dimensionKey);
                if (dimension is null)
                {
                    errors.Add($"'{key}' metrigi tanimsiz dimension ile uyumlu: '{dimensionKey}'.");
                    continue;
                }

                if (!dimension.CompatibleMetrics.Contains(key, StringComparer.Ordinal))
                {
                    errors.Add($"'{key}' ile '{dimensionKey}' compatibility tanimi simetrik degil.");
                }

                if (!metric.Source.Equals(dimension.Source, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"'{key}' ile '{dimensionKey}' farkli backend mapping source'larina ait.");
                }
            }

            foreach (var dimensionKey in metric.CompatibleFilters)
            {
                var dimension = catalog.FindDimension(dimensionKey);
                if (dimension is not null && !dimension.Filterable)
                {
                    errors.Add($"'{key}' filtresi filterable degil: '{dimensionKey}'.");
                }
            }

            var sourceHasTime = catalog.Dimensions.Values.Any(dimension =>
                dimension.IsTimeDimension
                && dimension.Source.Equals(metric.Source, StringComparison.OrdinalIgnoreCase));
            if (metric.IsUsable && metric.RequiresDateRange != sourceHasTime)
            {
                errors.Add($"'{key}' requiresDateRange degeri backend time mapping ile uyusmuyor.");
            }
        }

        foreach (var (key, dimension) in catalog.Dimensions)
        {
            RequireSemanticText("dimension", key, dimension.Description,
                dimension.QueryMappingReference, dimension.Aliases, errors);
            if (!mappingReferences.Add(dimension.QueryMappingReference))
            {
                errors.Add($"'{key}' dimension query mapping reference'i benzersiz degil.");
            }

            foreach (var metricKey in dimension.CompatibleMetrics)
            {
                var metric = catalog.FindMetric(metricKey);
                if (metric is null)
                {
                    errors.Add($"'{key}' dimension'i tanimsiz metric ile uyumlu: '{metricKey}'.");
                }
                else if (!metric.CompatibleDimensions.Contains(key, StringComparer.Ordinal)
                    && !metric.CompatibleFilters.Contains(key, StringComparer.Ordinal))
                {
                    errors.Add($"'{key}' ile '{metricKey}' compatibility tanimi simetrik degil.");
                }
            }
        }

        ValidateAliasCollisions(
            catalog.Metrics.SelectMany(item => item.Value.Aliases
                .Select(alias => (item.Key, Alias: alias))), "metric", errors);
        ValidateAliasCollisions(
            catalog.Dimensions.SelectMany(item => item.Value.Aliases
                .Select(alias => (item.Key, Alias: alias))), "dimension", errors);

        if (errors.Count > 0)
        {
            throw new CatalogValidationException(
                "Semantic Catalog metadata gecersiz:" + Environment.NewLine
                + string.Join(Environment.NewLine, errors.Select(error => "  - " + error)));
        }
    }

    private static void ValidateAliasCollisions(
        IEnumerable<(string Key, string Alias)> entries,
        string kind,
        List<string> errors)
    {
        foreach (var collision in entries
            .GroupBy(entry => entry.Alias.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(entry => entry.Key)
                .Distinct(StringComparer.Ordinal).Count() > 1))
        {
            errors.Add($"'{collision.Key}' alias'i birden fazla {kind} key'ine bagli.");
        }
    }

    private static void RequireSemanticText(
        string kind,
        string key,
        string description,
        string mappingReference,
        IReadOnlyList<string> aliases,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            errors.Add($"'{key}' {kind} description alani bos.");
        }

        if (string.IsNullOrWhiteSpace(mappingReference))
        {
            errors.Add($"'{key}' {kind} queryMappingReference alani bos.");
        }

        foreach (var alias in aliases)
        {
            var wordCount = alias.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount is 0 or > 6 || alias.IndexOfAny(SentencePunctuation) >= 0)
            {
                errors.Add($"'{key}' {kind} alias'i kavram duzeyinde degil: '{alias}'.");
            }
        }
    }
}
