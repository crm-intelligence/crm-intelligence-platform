using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;
using Crm.Analytics.Sql.Routing;

namespace Crm.Analytics.Sql.Service;

/// <summary>
/// Servisin kurulum ayarlari.
/// </summary>
/// <param name="ConfidenceThreshold">
/// Belirsizlik kapisinin kapsama esigi. Dogru deger ancak gercek kullanici talepleriyle
/// olculebilir; bu yuzden konfigurasyona acik.
/// </param>
/// <param name="SqlVersionName">
/// Hedef T-SQL surumu ("Sql150" = SQL Server 2019). Parser davranisi degisikligi sessiz
/// guvenlik regresyonu demektir; surum acik bir ayar olarak durur.
/// </param>
public sealed record SqlProductionOptions(
    double ConfidenceThreshold = AmbiguityGate.DefaultConfidenceThreshold,
    string SqlVersionName = "Sql150");

/// <summary>
/// SQL uretim servisini kurar.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden DI paketine bagimli degil:</b> bu kutuphane bir <c>IServiceCollection</c> uzantisi
/// sunmuyor. Boyle bir uzanti, kutuphaneyi Microsoft.Extensions.DependencyInjection'a baglardi;
/// oysa Backend'in hangi kapsayiciyi kullandigi benim kararim degil. Fabrika duz bir nesne
/// dondurur, Backend onu kendi kapsayicisina istedigi yasam suresiyle kaydeder:
/// </para>
/// <code>
/// // Program.cs (Backend)
/// builder.Services.AddSingleton&lt;IDecisionAuditWriter, LoggingDecisionAuditWriter&gt;();
/// builder.Services.AddSingleton&lt;ISqlProductionService&gt;(provider =&gt;
///     SqlProductionFactory.CreateForOlist(
///         provider.GetRequiredService&lt;IDecisionAuditWriter&gt;()));
/// </code>
/// <para>
/// Servis <b>durumsuz ve thread-safe</b>: singleton olarak kaydedilebilir. Katalog ve allow-list
/// yukleme aninda dogrulanip salt-okunur tutulur.
/// </para>
/// </remarks>
public static class SqlProductionFactory
{
    /// <summary>
    /// Assembly'ye gomulu Olist katalogu ve allow-list'i ile servis kurar.
    /// </summary>
    /// <remarks>
    /// Gomulu kaynak kullanilmasi bilincli: sema dosyasi surumle birlikte tasinir ve dosya
    /// yolu tahminine bagli bir kurulum hatasi olusmaz. Farkli bir katalog kullanmak icin
    /// <see cref="Create"/> asiri yuklemesine yuklenmis dokumanlar verilir.
    /// </remarks>
    public static ISqlProductionService CreateForOlist(
        IDecisionAuditWriter auditWriter,
        SqlProductionOptions? options = null,
        SemanticCatalogRegistry? semanticCatalogs = null)
    {
        ArgumentNullException.ThrowIfNull(auditWriter);
        var settings = options ?? new SqlProductionOptions();
        var registry = semanticCatalogs ?? SemanticCatalogRegistry.CreateDefault();
        var parserFactory = CreateParserFactory(settings);
        var runtimes = new Dictionary<DataSource, SqlSourceRuntime>
        {
            [DataSource.Dwh] = CreateRuntime(
                DataSource.Dwh,
                registry.GetRequired(DataSource.Dwh).AllowList,
                registry.GetRequired(DataSource.Dwh).Catalog,
                auditWriter, settings, parserFactory),
            [DataSource.Oltp] = CreateRuntime(
                DataSource.Oltp,
                registry.GetRequired(DataSource.Oltp).AllowList,
                registry.GetRequired(DataSource.Oltp).Catalog,
                auditWriter, settings, parserFactory)
        };

        return new SqlProductionService(runtimes, auditWriter);
    }

    /// <summary>
    /// Verilen katalog ve allow-list ile servis kurar.
    /// </summary>
    /// <remarks>Model-authored SQL is not accepted; Query Builder failure is fail-closed.</remarks>
    public static ISqlProductionService Create(
        AllowListDocument allowList,
        MetricCatalogDocument catalog,
        IDecisionAuditWriter auditWriter,
        SqlProductionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(allowList);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(auditWriter);

        var settings = options ?? new SqlProductionOptions();
        var parserFactory = CreateParserFactory(settings);
        var runtime = CreateRuntime(
            DataSource.Dwh, allowList, catalog, auditWriter,
            settings, parserFactory);
        return new SqlProductionService(
            new Dictionary<DataSource, SqlSourceRuntime>
            {
                [DataSource.Dwh] = runtime
            },
            auditWriter);
    }

    private static TSqlParserFactory CreateParserFactory(SqlProductionOptions settings)
    {
        if (!Enum.TryParse<Microsoft.SqlServer.TransactSql.ScriptDom.SqlVersion>(
                settings.SqlVersionName, ignoreCase: false, out var sqlVersion))
        {
            throw new ArgumentException(
                $"Taninmayan T-SQL surumu: '{settings.SqlVersionName}'. Ornek: 'Sql150'.",
                nameof(settings));
        }

        return new TSqlParserFactory(sqlVersion);
    }

    private static SqlSourceRuntime CreateRuntime(
        DataSource source,
        AllowListDocument allowList,
        MetricCatalogDocument catalog,
        IDecisionAuditWriter auditWriter,
        SqlProductionOptions settings,
        TSqlParserFactory parserFactory)
    {
        new CatalogValidator(parserFactory).Validate(catalog, allowList);
        var gate = new AmbiguityGate(settings.ConfidenceThreshold);
        var router = new SqlProductionRouter(
            new DeterministicQueryBuilder(parserFactory, catalog, allowList),
            allowList,
            parserFactory,
            auditWriter,
            gate,
            source);

        return new SqlSourceRuntime(
            source, new CatalogTermRequestParser(catalog), router, catalog, gate);
    }

    /// <summary>
    /// Dil modeli baglantisi tanimlanmadiginda kullanilan, <b>hicbir zaman taslak uretmeyen</b>
    /// saglayici. Fail-closed: model yoksa serbest analiz yolu kapalidir.
    /// </summary>
}
