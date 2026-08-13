using System.Globalization;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Parsing;
using Microsoft.SqlServer.TransactSql.ScriptDom;

// ScriptDom'un FunctionCall tipi, bir SQL fonksiyon cagrisi AST dugumudur. Alias, hem
// niyeti okunur kilar hem de statik analiz araclarinin bunu JavaScript'in
// "new Function()" kod-uretim kalibiyla karistirmasini onler.
using SqlFunctionCall = Microsoft.SqlServer.TransactSql.ScriptDom.FunctionCall;

namespace Crm.Analytics.Sql.QueryBuilder;

/// <summary>
/// Canonical Request'ten deterministik, parametreli SELECT uretir.
/// </summary>
/// <remarks>
/// <para>
/// SQL <b>AST olarak kurulur</b>, metin birlestirmeyle degil. Katalogdaki metrik ifadeleri
/// bile metne gomulmez; parse edilip AST dugumu olarak yerlestirilir. Uretilen metin yalnizca
/// son adimda <c>SqlScriptGenerator</c> tarafindan olusturulur.
/// </para>
/// <para>
/// Kullanici degerleri hicbir zaman metne girmez: her filtre degeri bir parametreye baglanir
/// ve <see cref="SqlParameterSpec"/> olarak tasinir.
/// </para>
/// <para>
/// Uretilen SQL <b>onaylanmis degildir</b> — guardrail hattindan gecmesi zorunludur.
/// </para>
/// </remarks>
public sealed class DeterministicQueryBuilder(
    TSqlParserFactory parserFactory,
    MetricCatalogDocument catalog,
    AllowListDocument allowList)
{
    /// <summary>Parametre oneki. Guardrail'in urettikleriyle (@p, @scope) cakismaz.</summary>
    private const string ParameterPrefix = "@f";

    public QueryBuildResult Build(CanonicalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var metricsResult = ResolveMetrics(request);

        if (metricsResult.Failure is not null)
        {
            return metricsResult.Failure;
        }

        var dimensionsResult = ResolveDimensions(request);

        if (dimensionsResult.Failure is not null)
        {
            return dimensionsResult.Failure;
        }

        var metrics = metricsResult.Items;
        var dimensions = dimensionsResult.Items;

        if (metrics.Count == 0 && dimensions.Count == 0)
        {
            return QueryBuildResult.Failure(ReasonCode.CL001, "Talep hicbir metrik veya boyut icermiyor.");
        }

        var (source, sourceFailure) = ResolveSingleSource(metrics, dimensions);

        if (sourceFailure is not null)
        {
            return sourceFailure;
        }

        // Savunma katmani: kaynak objenin allow-list'te oldugu CatalogValidator tarafindan
        // baslangicta dogrulanmis olmali. Yine de burada tekrar bakiyoruz — katalog
        // dogrulanmadan yuklenmis olsa dahi Query Builder izinsiz bir objeye SQL uretmemeli.
        if (!allowList.HasObject(source!))
        {
            return QueryBuildResult.Failure(ReasonCode.GR003,
                $"Kaynak obje allow-list'te yok: '{source}'. Katalog dogrulanmamis olabilir.");
        }

        var physicalSource = allowList.ResolvePhysicalObject(source!);
        if (physicalSource is null)
        {
            return QueryBuildResult.Failure(ReasonCode.GR003,
                $"Kaynak obje fiziksel ada cozumlenemedi: '{source}'.");
        }

        var compatibilityFailure = ValidateSemanticCompatibility(
            request, metrics, dimensions);
        if (compatibilityFailure is not null)
        {
            return compatibilityFailure;
        }

        var state = new BuildState();
        var block = new QuerySpecification { FromClause = BuildFromClause(physicalSource) };

        var selectFailure = AddSelectElements(block, metrics, dimensions, request);

        if (selectFailure is not null)
        {
            return selectFailure;
        }

        var whereFailure = AddWhereClause(block, request, source!, state);

        if (whereFailure is not null)
        {
            return whereFailure;
        }

        var groupingFailure = AddGroupByClause(block, metrics, dimensions);
        if (groupingFailure is not null)
        {
            return groupingFailure;
        }

        var orderingFailure = AddOrderByClause(block, request, source!);
        if (orderingFailure is not null)
        {
            return orderingFailure;
        }
        AddTopRowFilter(block, request);

        var sql = parserFactory.GenerateScript(BuildScript(block), out var versioningErrors);

        if (versioningErrors.Count > 0)
        {
            return QueryBuildResult.Failure(ReasonCode.GR014,
                $"Uretilen sorgu hedef SQL surumu icin gecersiz: {versioningErrors[0].Message}");
        }

        return QueryBuildResult.Success(sql, state.Parameters, source!);
    }

    private (List<(string Key, MetricDefinition Definition)> Items, QueryBuildResult? Failure) ResolveMetrics(
        CanonicalRequest request)
    {
        var metrics = new List<(string, MetricDefinition)>();

        foreach (var key in request.Metrics)
        {
            var metric = catalog.FindMetric(key);

            if (metric is null)
            {
                return (metrics, QueryBuildResult.Failure(ReasonCode.CL001, $"Tanimsiz metrik: '{key}'."));
            }

            if (!metric.IsUsable)
            {
                // designPending: ifadesi henuz kararlastirilmadi. Uydurma bir ifade uretmek
                // yerine netlestirme istenir.
                return (metrics, QueryBuildResult.Failure(ReasonCode.CL001,
                    $"'{key}' metriginin tanimi henuz kesinlesmedi ({metric.ApprovalStatus})."));
            }

            metrics.Add((key, metric));
        }

        return (metrics, null);
    }

    private (List<(string Key, DimensionDefinition Definition)> Items, QueryBuildResult? Failure) ResolveDimensions(
        CanonicalRequest request)
    {
        var dimensions = new List<(string, DimensionDefinition)>();

        foreach (var key in request.Dimensions)
        {
            var dimension = catalog.FindDimension(key);

            if (dimension is null)
            {
                return (dimensions, QueryBuildResult.Failure(ReasonCode.CL001, $"Tanimsiz boyut: '{key}'."));
            }

            if (!dimension.Selectable)
            {
                return (dimensions, QueryBuildResult.Failure(
                    ReasonCode.CL001, $"Secilemeyen katalog alani: '{key}'."));
            }

            dimensions.Add((key, dimension));
        }

        return (dimensions, null);
    }

    /// <summary>
    /// Tum metrik ve boyutlarin ayni objeden gelmesi zorunlu.
    /// </summary>
    /// <remarks>
    /// Farkli objeler JOIN gerektirir; JOIN yolu allow-list'te tanimli olmak zorundadir ve
    /// Query Builder kendi basina JOIN kurmaz. Olist allow-list'inde <c>maxJoins = 0</c> ve
    /// gorunumler arasinda ortak anahtar bulunmadigi icin bu birlesim teknik olarak da
    /// mumkun degil.
    /// </remarks>
    private static (string? Source, QueryBuildResult? Failure) ResolveSingleSource(
        List<(string Key, MetricDefinition Definition)> metrics,
        List<(string Key, DimensionDefinition Definition)> dimensions)
    {
        var sources = metrics.Select(metric => metric.Definition.Source)
            .Concat(dimensions.Select(dimension => dimension.Definition.Source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sources.Length > 1)
        {
            return (null, QueryBuildResult.Failure(ReasonCode.GR006,
                $"Talep birden fazla kaynagi birlestirmeyi gerektiriyor: {string.Join(", ", sources)}. " +
                "Tanimli bir JOIN yolu yok."));
        }

        return (sources[0], null);
    }

    private static FromClause BuildFromClause(string source)
    {
        var fromClause = new FromClause();
        fromClause.TableReferences.Add(new NamedTableReference
        {
            SchemaObject = BuildSchemaObjectName(source)
        });

        return fromClause;
    }

    /// <summary>
    /// Obje adini AST dugumu olarak kurar. Ad allow-list'ten gelir ve yukleme aninda dar bir
    /// desene karsi dogrulanmistir; yine de metin birlestirme yerine Identifier dugumleri
    /// kullanilir.
    /// </summary>
    private static SchemaObjectName BuildSchemaObjectName(string name)
    {
        var schemaObject = new SchemaObjectName();

        foreach (var part in name.Split('.'))
        {
            schemaObject.Identifiers.Add(new Identifier { Value = part });
        }

        return schemaObject;
    }

    private QueryBuildResult? AddSelectElements(
        QuerySpecification block,
        List<(string Key, MetricDefinition Definition)> metrics,
        List<(string Key, DimensionDefinition Definition)> dimensions,
        CanonicalRequest request)
    {
        // Boyutlar once gelir: sonuc seti "kirilim + olcum" duzeninde okunur.
        foreach (var (key, dimension) in dimensions)
        {
            var expression = BuildGrainExpression(dimension, request.Grain, out var grainFailure);

            if (grainFailure is not null)
            {
                return grainFailure;
            }

            block.SelectElements.Add(BuildSelectElement(expression, key));
        }

        foreach (var (key, metric) in metrics)
        {
            var parsed = parserFactory.TryParseExpression(metric.Expression!, out var errors);

            if (errors.Count > 0 || parsed is null)
            {
                // CatalogValidator bunu baslangicta yakalamis olmali; buraya dusmesi katalogun
                // dogrulanmadan yuklendigini gosterir.
                return QueryBuildResult.Failure(ReasonCode.GR014,
                    $"'{key}' metrik ifadesi parse edilemedi. Katalog dogrulanmamis olabilir.");
            }

            block.SelectElements.Add(BuildSelectElement(parsed, key));
        }

        return null;
    }

    private static SelectScalarExpression BuildSelectElement(ScalarExpression expression, string alias) => new()
    {
        Expression = expression,
        ColumnName = new IdentifierOrValueExpression { Identifier = new Identifier { Value = alias } }
    };

    /// <summary>
    /// Zaman boyutuna kirilim uygular.
    /// </summary>
    /// <remarks>
    /// Yalnizca yil, ceyrek ve ay desteklenir. Gun kirilimi <c>CAST</c> gerektirir (izinli
    /// fonksiyon listesinde degil), hafta kirilimi ise <c>DATEPART(week, ...)</c> ile
    /// hesaplanir ve hafta baslangici sunucu ayarina (<c>DATEFIRST</c>) bagli oldugu icin
    /// DETERMINISTIK DEGILDIR. Desteklenmeyen kirilim icin uydurma bir karsilik uretmek yerine
    /// netlestirme istenir.
    /// </remarks>
    private static ScalarExpression BuildGrainExpression(
        DimensionDefinition dimension,
        TimeGrain grain,
        out QueryBuildResult? failure)
    {
        failure = null;
        var column = BuildColumnReference(dimension.Column);

        if (!dimension.IsTimeDimension || grain == TimeGrain.None)
        {
            return column;
        }

        switch (grain)
        {
            case TimeGrain.Year:
                return BuildCall("YEAR", column);

            case TimeGrain.Month:
                return BuildCall("MONTH", column);

            case TimeGrain.Quarter:
                return BuildCall("DATEPART", BuildColumnReference("quarter"), column);

            default:
                failure = QueryBuildResult.Failure(ReasonCode.CL002,
                    $"Desteklenmeyen zaman kirilimi: {grain}. Yil, ceyrek ve ay desteklenir.");
                return column;
        }
    }

    private static ColumnReferenceExpression BuildColumnReference(string column)
    {
        var identifier = new MultiPartIdentifier();
        identifier.Identifiers.Add(new Identifier { Value = column });

        return new ColumnReferenceExpression { MultiPartIdentifier = identifier };
    }

    private static SqlFunctionCall BuildCall(string name, params ScalarExpression[] arguments)
    {
        var call = new SqlFunctionCall { FunctionName = new Identifier { Value = name } };

        foreach (var argument in arguments)
        {
            call.Parameters.Add(argument);
        }

        return call;
    }

    private QueryBuildResult? AddWhereClause(
        QuerySpecification block,
        CanonicalRequest request,
        string source,
        BuildState state)
    {
        var conditions = new List<BooleanExpression>();

        foreach (var filter in request.Filters)
        {
            var dimension = catalog.FindDimension(filter.Field);

            if (dimension is null)
            {
                return QueryBuildResult.Failure(ReasonCode.CL001, $"Tanimsiz filtre alani: '{filter.Field}'.");
            }

            if (!dimension.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
            {
                return QueryBuildResult.Failure(ReasonCode.GR006,
                    $"'{filter.Field}' filtresi baska bir kaynaga ait: '{dimension.Source}'.");
            }

            if (!dimension.Filterable)
            {
                return QueryBuildResult.Failure(
                    ReasonCode.CL001,
                    $"'{filter.Field}' alani icin filtre capability'si etkin degil.");
            }

            var condition = BuildFilterCondition(dimension, filter, state, out var failure);

            if (failure is not null)
            {
                return failure;
            }

            conditions.Add(condition!);
        }

        var dateCondition = BuildDateRangeCondition(request, source, state, out var dateFailure);

        if (dateFailure is not null)
        {
            return dateFailure;
        }

        if (dateCondition is not null)
        {
            conditions.Add(dateCondition);
        }

        if (conditions.Count > 0)
        {
            block.WhereClause = new WhereClause
            {
                SearchCondition = conditions.Aggregate(CombineWithAnd)
            };
        }

        return null;
    }

    private BooleanExpression? BuildFilterCondition(
        DimensionDefinition dimension,
        RequestFilter filter,
        BuildState state,
        out QueryBuildResult? failure)
    {
        failure = null;

        var expectedCount = filter.Op switch
        {
            FilterOperator.In or FilterOperator.NotIn => -1,
            FilterOperator.Between => 2,
            _ => 1
        };

        if (filter.Values.Count == 0)
        {
            failure = QueryBuildResult.Failure(ReasonCode.CL001, $"'{filter.Field}' filtresi degersiz.");
            return null;
        }

        if (expectedCount > 0 && filter.Values.Count != expectedCount)
        {
            failure = QueryBuildResult.Failure(ReasonCode.CL001,
                $"'{filter.Field}' filtresi {filter.Op} icin {expectedCount} deger bekliyor, " +
                $"{filter.Values.Count} geldi.");
            return null;
        }

        foreach (var value in filter.Values)
        {
            if (!IsValidFilterLiteral(dimension, value))
            {
                failure = QueryBuildResult.Failure(ReasonCode.CL001,
                    $"'{filter.Field}' filtresi catalog value type sozlesmesine uymuyor.");
                return null;
            }
        }

        var parameterNames = filter.Values
            .Select(value => state.AddParameter(MapValueKind(dimension, value), value.Raw))
            .ToArray();

        // Kosul, sabit ve guvenli bir metinden parse edilir: metne yalnizca katalogdan gelen
        // kolon adi ve kendi urettigimiz parametre adlari girer. Kullanici DEGERI asla girmez.
        var text = filter.Op switch
        {
            FilterOperator.Eq => $"{dimension.Column} = {parameterNames[0]}",
            FilterOperator.NotEq => $"{dimension.Column} <> {parameterNames[0]}",
            FilterOperator.Gt => $"{dimension.Column} > {parameterNames[0]}",
            FilterOperator.Gte => $"{dimension.Column} >= {parameterNames[0]}",
            FilterOperator.Lt => $"{dimension.Column} < {parameterNames[0]}",
            FilterOperator.Lte => $"{dimension.Column} <= {parameterNames[0]}",
            FilterOperator.Between =>
                $"{dimension.Column} BETWEEN {parameterNames[0]} AND {parameterNames[1]}",
            FilterOperator.In => $"{dimension.Column} IN ({string.Join(", ", parameterNames)})",
            FilterOperator.NotIn => $"{dimension.Column} NOT IN ({string.Join(", ", parameterNames)})",
            _ => throw new InvalidOperationException($"Desteklenmeyen operator: {filter.Op}")
        };

        return parserFactory.ParseBooleanExpression(text);
    }

    /// <summary>
    /// Tarih araligi kosulunu kurar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aralik, kaynakta zaman boyutu varsa <b>zorunludur</b>. Onceki surumde eksik aralik
    /// sessizce atlanip kosulsuz bir sorgu uretiliyordu; bu, <c>maxDateRangeDays</c> butcesini
    /// tamamen etkisiz kiliyordu — <c>DateRangeBudget</c> kontrolu var olmayan bir araligi
    /// denetleyemez. Donen satiri sinirlamak (TOP) taranan satiri sinirlamaz.
    /// </para>
    /// <para>
    /// Ters yon de sessiz gecilmez: kullanici tarih verdiyse ama kaynakta zaman boyutu yoksa
    /// filtre uygulanamaz. Bunu yok saymak, "son ceyrek" isteyen kullaniciya tum zamanlarin
    /// sonucunu dogru gibi gostermek olurdu.
    /// </para>
    /// </remarks>
    private BooleanExpression? BuildDateRangeCondition(
        CanonicalRequest request,
        string source,
        BuildState state,
        out QueryBuildResult? failure)
    {
        failure = null;

        var timeDimension = catalog.Dimensions.Values.FirstOrDefault(dimension =>
            dimension.IsTimeDimension
            && dimension.Source.Equals(source, StringComparison.OrdinalIgnoreCase));

        if (request.DateRange.From is not { } from || request.DateRange.To is not { } to)
        {
            if (timeDimension is not null)
            {
                failure = QueryBuildResult.Failure(ReasonCode.CL002,
                    $"'{source}' kaynagi zaman boyutu tasiyor, bu yuzden tarih araligi zorunludur. " +
                    "Aralik olmadan sorgu tum donemi tarar ve tarih butcesi denetlenemez.");
            }

            return null;
        }

        if (timeDimension is null)
        {
            failure = QueryBuildResult.Failure(ReasonCode.CL002,
                $"Talep tarih araligi iceriyor ama '{source}' kaynaginda zaman boyutu tanimli degil; " +
                "aralik uygulanamaz.");

            return null;
        }

        // ISO 8601 (yyyy-MM-dd) bilincli: sunucu DATEFORMAT/LANGUAGE ayarindan bagimsiz
        // yorumlanir. Yerel bicim kullanmak, ayni sorgunun farkli ortamlarda FARKLI SATIR
        // KUMESI dondurmesine yol acardi.
        var fromParameter = state.AddParameter(FilterValueKind.Date, from.ToString("yyyy-MM-dd"));
        var exclusiveTo = to.AddDays(1);
        var toParameter = state.AddParameter(
            FilterValueKind.Date, exclusiveTo.ToString("yyyy-MM-dd"));

        return parserFactory.ParseBooleanExpression(
            $"{timeDimension.Column} >= {fromParameter} AND {timeDimension.Column} < {toParameter}");
    }

    private static FilterValueKind MapValueKind(DimensionDefinition dimension, FilterLiteral value) =>
        // Tip ONCELIKLE katalogdan okunur: '2026-04-01' hem metin hem tarih olabilir ve
        // JSON'dan tahmin etmek yanlis parametre baglamaya yol acardi.
        dimension.ValueType switch
        {
            "text" => FilterValueKind.Text,
            "integer" => FilterValueKind.Integer,
            "decimal" => FilterValueKind.Decimal,
            "boolean" => FilterValueKind.Boolean,
            "date" => FilterValueKind.Date,
            _ => value.Kind
        };

    private static bool IsValidFilterLiteral(
        DimensionDefinition dimension,
        FilterLiteral value)
    {
        if (string.IsNullOrWhiteSpace(value.Raw) || value.Raw.Length > 512
            || value.Raw.Any(char.IsControl))
        {
            return false;
        }

        return dimension.ValueType switch
        {
            "text" => true,
            "integer" => int.TryParse(value.Raw, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out _),
            "decimal" => decimal.TryParse(value.Raw, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out _),
            "boolean" => bool.TryParse(value.Raw, out _),
            "date" => DateOnly.TryParseExact(value.Raw, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            _ => false
        };
    }

    private static QueryBuildResult? AddGroupByClause(
        QuerySpecification block,
        List<(string Key, MetricDefinition Definition)> metrics,
        List<(string Key, DimensionDefinition Definition)> dimensions)
    {
        // GROUP BY yalnizca hem olcum hem kirilim varsa gerekir.
        if (metrics.Count == 0 || dimensions.Count == 0)
        {
            return null;
        }

        var nonGroupable = dimensions.FirstOrDefault(item => !item.Definition.Groupable);
        if (nonGroupable.Definition is not null)
        {
            return QueryBuildResult.Failure(
                ReasonCode.CL001,
                $"'{nonGroupable.Key}' alani icin group-by capability'si etkin degil.");
        }

        var groupBy = new GroupByClause();

        // SELECT'te uretilen ifadenin AYNISI GROUP BY'a konur; farkli bir ifade yazmak
        // (ornek: kolonun kendisi yerine YEAR(kolon)) T-SQL hatasi uretir.
        foreach (var element in block.SelectElements
            .OfType<SelectScalarExpression>()
            .Take(dimensions.Count))
        {
            groupBy.GroupingSpecifications.Add(new ExpressionGroupingSpecification
            {
                Expression = element.Expression
            });
        }

        block.GroupByClause = groupBy;
        return null;
    }

    private QueryBuildResult? ValidateSemanticCompatibility(
        CanonicalRequest request,
        IReadOnlyList<(string Key, MetricDefinition Definition)> metrics,
        IReadOnlyList<(string Key, DimensionDefinition Definition)> dimensions)
    {
        foreach (var metric in metrics)
        {
            var incompatibleDimension = dimensions.FirstOrDefault(dimension =>
                !catalog.IsMetricDimensionCompatible(metric.Key, dimension.Key));
            if (incompatibleDimension.Definition is not null)
            {
                return QueryBuildResult.Failure(ReasonCode.CL001,
                    $"'{metric.Key}' metrigi '{incompatibleDimension.Key}' dimension'i ile uyumlu degil.");
            }

            var incompatibleFilter = request.Filters.FirstOrDefault(filter =>
                !catalog.IsMetricFilterCompatible(metric.Key, filter.Field));
            if (incompatibleFilter is not null)
            {
                return QueryBuildResult.Failure(ReasonCode.CL001,
                    $"'{metric.Key}' metrigi '{incompatibleFilter.Field}' filtresi ile uyumlu degil.");
            }
        }

        return null;
    }

    private QueryBuildResult? AddOrderByClause(
        QuerySpecification block,
        CanonicalRequest request,
        string source)
    {
        var allowedObject = allowList.FindObject(source)
            ?? throw new InvalidOperationException($"Taninmayan catalog source: '{source}'.");
        var expressions = new List<(string Column, SortDirection Direction)>();

        if (!string.IsNullOrWhiteSpace(request.OrderBy))
        {
            var dimension = catalog.FindDimension(request.OrderBy);
            if (dimension is null
                || !dimension.Source.Equals(source, StringComparison.OrdinalIgnoreCase)
                || !dimension.Sortable)
            {
                return QueryBuildResult.Failure(
                    ReasonCode.CL002,
                    "Istenen siralama alani secili source catalog'unda sortable degil.");
            }

            expressions.Add((dimension.Column, request.OrderDirection));
        }

        foreach (var stableColumn in allowedObject.StableOrderColumns)
        {
            if (!expressions.Any(item => item.Column.Equals(
                    stableColumn, StringComparison.OrdinalIgnoreCase)))
            {
                expressions.Add((stableColumn, request.OrderDirection));
            }
        }

        if (expressions.Count == 0)
        {
            if (request.Limit is not null && request.OrderBy is not null)
            {
                return QueryBuildResult.Failure(
                    ReasonCode.CL002,
                    "Top N talebi icin deterministik tie-breaker kanitlanmadi.");
            }

            return null;
        }

        var clause = new OrderByClause();
        foreach (var (column, direction) in expressions)
        {
            clause.OrderByElements.Add(new ExpressionWithSortOrder
            {
                Expression = BuildColumnReference(column),
                SortOrder = direction == SortDirection.Desc
                    ? SortOrder.Descending
                    : SortOrder.Ascending
            });
        }

        block.OrderByClause = clause;
        return null;
    }

    private static void AddTopRowFilter(QuerySpecification block, CanonicalRequest request)
    {
        // Talebin istedigi limit yalnizca DUSURUCU olabilir; ust siniri guardrail'daki
        // RowLimit kontrolu ayrica zorlar.
        if (request.Limit is not { } limit || limit <= 0)
        {
            return;
        }

        block.TopRowFilter = new TopRowFilter
        {
            Expression = new IntegerLiteral { Value = limit.ToString() },
            Percent = false,
            WithTies = false
        };
    }

    private static BooleanExpression CombineWithAnd(BooleanExpression left, BooleanExpression right) =>
        new BooleanBinaryExpression
        {
            BinaryExpressionType = BooleanBinaryExpressionType.And,
            FirstExpression = left,
            SecondExpression = right
        };

    private static TSqlScript BuildScript(QuerySpecification block)
    {
        var batch = new TSqlBatch();
        batch.Statements.Add(new SelectStatement { QueryExpression = block });

        var script = new TSqlScript();
        script.Batches.Add(batch);

        return script;
    }

    private sealed class BuildState
    {
        private readonly List<SqlParameterSpec> parameters = [];
        private int counter;

        public IReadOnlyList<SqlParameterSpec> Parameters => parameters;

        public string AddParameter(FilterValueKind kind, string raw)
        {
            var name = $"{ParameterPrefix}{counter++}";
            parameters.Add(new SqlParameterSpec(name, kind, raw));
            return name;
        }
    }
}
