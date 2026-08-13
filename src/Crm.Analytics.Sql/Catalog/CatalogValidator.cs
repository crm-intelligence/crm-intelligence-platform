using Crm.Analytics.Sql.Parsing;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Catalog;

/// <summary>
/// Metric Catalog'u allow-list'e karsi dogrular: ifadeler parse edilir, kolonlar ve
/// fonksiyonlar izinli mi diye denetlenir.
/// </summary>
/// <remarks>
/// <para>
/// Katalog bir enjeksiyon yuzeyidir: <c>expression</c> alanlari serbest SQL metni tasir.
/// Dogrulanmazsa katalog'a yazilan tek bir satir tum guardrail'i baypas edebilir — cunku
/// Query Builder katalogdan gelen ifadeye guvenir.
/// </para>
/// <para>
/// Bu dogrulama <b>uygulama baslangicinda</b> kosar ve basarisiz olursa uygulama baslamaz.
/// Gecersiz bir katalogla calismaya devam etmek, guardrail'in dayandigi tanimin bozuk
/// olmasi demektir.
/// </para>
/// </remarks>
public sealed class CatalogValidator(TSqlParserFactory parserFactory)
{
    public void Validate(MetricCatalogDocument catalog, AllowListDocument allowList)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(allowList);

        var errors = new List<string>();

        foreach (var (key, metric) in catalog.Metrics)
        {
            ValidateMetric(key, metric, allowList, errors);
        }

        foreach (var (key, dimension) in catalog.Dimensions)
        {
            ValidateDimension(key, dimension, allowList, errors);
        }

        ValidateScenarioObjects(catalog, allowList, errors);

        if (errors.Count > 0)
        {
            throw new CatalogValidationException(
                "Metric Catalog allow-list ile uyumsuz:" + System.Environment.NewLine +
                string.Join(System.Environment.NewLine, errors.Select(error => "  - " + error)));
        }
    }

    private static void ValidateScenarioObjects(
        MetricCatalogDocument catalog,
        AllowListDocument allowList,
        List<string> errors)
    {
        foreach (var (objectName, scenarioObject) in allowList.ScenarioObjects)
        {
            foreach (var contract in scenarioObject.Contracts)
            {
                var parts = contract.Split(':', 2);
                var exists = parts.Length == 2 && parts[0] switch
                {
                    "useCase" => catalog.UseCases.ContainsKey(parts[1]),
                    "metric" => catalog.Metrics.ContainsKey(parts[1]),
                    _ => false
                };

                if (!exists)
                {
                    errors.Add(
                        $"'{objectName}' ozel view contract'i mevcut use-case/metric katalogunda yok: '{contract}'.");
                }
            }
        }
    }

    private void ValidateMetric(
        string key,
        MetricDefinition metric,
        AllowListDocument allowList,
        List<string> errors)
    {
        var source = allowList.FindObject(metric.Source);

        if (source is null)
        {
            errors.Add($"'{key}' metriginin kaynagi allow-list'te yok: '{metric.Source}'.");
            return;
        }

        if (!metric.IsUsable)
        {
            // Ifadesi olmayan metrik (designPending) dogrulanamaz; yukleyici bunu zaten
            // designPending olmaya zorluyor.
            return;
        }

        var parsed = parserFactory.TryParseExpression(metric.Expression!, out var parseErrors);

        if (parseErrors.Count > 0 || parsed is null)
        {
            errors.Add(
                $"'{key}' metriginin ifadesi parse edilemedi: '{metric.Expression}'. " +
                $"Ilk hata: {parseErrors.FirstOrDefault()?.Message ?? "(bilinmiyor)"}");
            return;
        }

        var inspector = new ExpressionInspector();
        parsed.Accept(inspector);

        foreach (var column in inspector.Columns)
        {
            // Alias kullanimi katalog sozlesmesini bozar: nitelendirme Query Builder'in isidir.
            if (column.Contains('.', StringComparison.Ordinal))
            {
                errors.Add($"'{key}' ifadesinde tablo alias'i kullanilmis: '{column}'.");
                continue;
            }

            if (!source.HasColumn(column))
            {
                errors.Add($"'{key}' ifadesindeki '{column}' kolonu '{metric.Source}' icinde izinli degil.");
            }

            if (allowList.IsDeniedColumn(column))
            {
                errors.Add($"'{key}' ifadesi yasakli kisisel veri kolonu kullaniyor: '{column}'.");
            }
        }

        foreach (var function in inspector.Functions.Where(function => !allowList.IsAllowedFunction(function)))
        {
            errors.Add($"'{key}' ifadesi izinli olmayan fonksiyon kullaniyor: '{function}'.");
        }
    }

    private static void ValidateDimension(
        string key,
        DimensionDefinition dimension,
        AllowListDocument allowList,
        List<string> errors)
    {
        var source = allowList.FindObject(dimension.Source);

        if (source is null)
        {
            errors.Add($"'{key}' boyutunun kaynagi allow-list'te yok: '{dimension.Source}'.");
            return;
        }

        if (!source.HasColumn(dimension.Column))
        {
            errors.Add($"'{key}' boyutunun kolonu '{dimension.Source}.{dimension.Column}' izinli degil.");
        }

        if (allowList.IsDeniedColumn(dimension.Column))
        {
            errors.Add($"'{key}' boyutu yasakli kisisel veri kolonu kullaniyor: '{dimension.Column}'.");
        }

        // Kapsam boyutu, kaynak objenin gercek kapsam kolonuyla ayni olmak zorunda; aksi halde
        // "kapsam boyutu" olarak isaretlenmis ama filtre uygulanmayan bir kolon olusur.
        if (dimension.IsScopeDimension
            && !dimension.Column.Equals(source.ScopeColumn, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                $"'{key}' kapsam boyutu olarak isaretli ancak kolonu '{dimension.Column}', " +
                $"'{dimension.Source}' objesinin scopeColumn'u ise '{source.ScopeColumn}'.");
        }
    }

    private sealed class ExpressionInspector : TSqlFragmentVisitor
    {
        private readonly List<string> columns = [];
        private readonly List<string> functions = [];

        public IReadOnlyList<string> Columns => columns;

        public IReadOnlyList<string> Functions => functions;

        public override void Visit(ColumnReferenceExpression node)
        {
            var identifiers = node.MultiPartIdentifier?.Identifiers;

            if (identifiers is not { Count: > 0 })
            {
                return;
            }

            // Alias kullanimini tespit edebilmek icin tam ad korunur.
            columns.Add(string.Join('.', identifiers.Select(identifier => identifier.Value)));
        }

        public override void Visit(FunctionCall node)
        {
            if (!string.IsNullOrWhiteSpace(node.FunctionName?.Value))
            {
                functions.Add(node.FunctionName.Value);
            }
        }
    }
}
