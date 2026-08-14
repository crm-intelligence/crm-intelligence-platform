using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Nlu;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;

namespace Crm.Analytics.Sql.Routing;

/// <summary>
/// Canonical talebi secilen SQL query strategy'sine yonlendirir ve ciktiyi ortak
/// guardrail hattindan gecirir.
/// </summary>
/// <remarks>
/// <para>
/// Bu sinif, sistemde SQL uretmenin TEK giris noktasidir. Guardrail'i atlayan bir yol
/// bilincli olarak birakilmamistir — "test amacli bile" birakilmaz.
/// </para>
/// <para>
/// Strategy tarafindan uretilen SQL onaylanmis degildir. Strategy talebi karsilayamiyorsa
/// sistem fail-closed davranir; basarili her cikti ortak guardrail hattina girer.
/// </para>
/// </remarks>
public sealed class SqlProductionRouter
{
    private readonly QueryStrategyRouter queryStrategyRouter;
    private readonly AllowListDocument allowList;
    private readonly TSqlParserFactory parserFactory;
    private readonly IDecisionAuditWriter auditWriter;
    private readonly AmbiguityGate ambiguityGate;
    private readonly DataSource source;

    internal SqlProductionRouter(
        QueryStrategyRouter queryStrategyRouter,
        AllowListDocument allowList,
        TSqlParserFactory parserFactory,
        IDecisionAuditWriter auditWriter,
        AmbiguityGate? ambiguityGate = null,
        DataSource source = DataSource.Dwh)
    {
        this.queryStrategyRouter = queryStrategyRouter;
        this.allowList = allowList;
        this.parserFactory = parserFactory;
        this.auditWriter = auditWriter;
        this.ambiguityGate = ambiguityGate ?? new AmbiguityGate();
        this.source = source;
    }

    /// <summary>
    /// Mevcut dogrudan cagrilar icin deterministic Fast Path uyumluluk constructor'i.
    /// SQL yine QueryStrategyRouter uzerinden uretilir.
    /// </summary>
    public SqlProductionRouter(
        DeterministicQueryBuilder queryBuilder,
        AllowListDocument allowList,
        TSqlParserFactory parserFactory,
        IDecisionAuditWriter auditWriter,
        AmbiguityGate? ambiguityGate = null,
        DataSource source = DataSource.Dwh)
        : this(
            new QueryStrategyRouter(
                [new DeterministicSqlQueryStrategy(queryBuilder)],
                queryBuilder),
            allowList,
            parserFactory,
            auditWriter,
            ambiguityGate,
            source)
    {
    }

    public sealed record RoutingResult(
        GuardrailResult Guardrail,
        ProductionPath Path,
        string? BuilderDetail);

    public RoutingResult Produce(
        CanonicalRequest request,
        UserDataScope scope,
        string? rawPrompt = null,
        string? userId = null)
    {
        return ProduceCoreAsync(
                request,
                query: null,
                scope,
                rawPrompt,
                userId,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Internal async V2 entry point for provider-backed strategies. Public production
    /// contracts remain unchanged until a real provider integration is explicitly enabled.
    /// </summary>
    internal Task<RoutingResult> ProduceAsync(
        CanonicalRequest request,
        CanonicalQuery query,
        UserDataScope scope,
        CancellationToken cancellationToken,
        string? rawPrompt = null,
        string? userId = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ProduceCoreAsync(request, query, scope, rawPrompt, userId, cancellationToken);
    }

    internal RoutingResult ProduceCandidate(
        CanonicalRequest request,
        UserDataScope scope,
        QueryBuildResult candidate,
        string? userId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(candidate);

        request = request.Source is null ? request with { Source = source } : request;
        if (request.Source != source)
        {
            return RejectWithoutSql(
                request, scope, ReasonCode.CL001, ProductionPath.QueryBuilder,
                rawPrompt: null, userId, "Canonical source ile runtime catalog source uyusmuyor.");
        }

        var verdict = ambiguityGate.Evaluate(request);
        if (verdict.NeedsClarification)
        {
            return RejectWithoutSql(
                request, scope, verdict.ReasonCode, ProductionPath.QueryBuilder,
                rawPrompt: null, userId, verdict.Detail);
        }

        return candidate.IsSuccessful
            ? Finish(
                request, scope, candidate.Sql!, candidate.Parameters,
                ProductionPath.QueryBuilder, rawPrompt: null, userId, source,
                candidate.Detail)
            : RejectWithoutSql(
                request, scope, candidate.ReasonCode, ProductionPath.QueryBuilder,
                rawPrompt: null, userId, candidate.Detail);
    }

    private async Task<RoutingResult> ProduceCoreAsync(
        CanonicalRequest request,
        CanonicalQuery? query,
        UserDataScope scope,
        string? rawPrompt,
        string? userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();

        // Backward-compatible direct-router callers enter an already selected immutable
        // source context. Production service always sets Source before reaching here.
        request = request.Source is null ? request with { Source = source } : request;

        if (request.Source != source)
        {
            return RejectWithoutSql(
                request, scope, ReasonCode.CL001, ProductionPath.QueryBuilder,
                rawPrompt, userId, "Canonical source ile runtime catalog source uyusmuyor.");
        }

        // Belirsizlik kapisi SQL uretiminden ONCE kosar. Sozlesmedeki confidence ve
        // unresolvedTerms alanlari burada karara donusur; okunmayan bir alan sozlesmede
        // durup hicbir sey yapmiyorsa yanlis bir guvence verir.
        var verdict = ambiguityGate.Evaluate(request);

        if (verdict.NeedsClarification)
        {
            return RejectWithoutSql(request, scope, verdict.ReasonCode,
                ProductionPath.QueryBuilder, rawPrompt, userId, verdict.Detail);
        }

        var build = query is null
            ? await queryStrategyRouter.ProduceAsync(request, cancellationToken)
                .ConfigureAwait(false)
            : await queryStrategyRouter.ProduceAsync(request, query, cancellationToken)
                .ConfigureAwait(false);

        if (build.IsSuccessful)
        {
            return Finish(request, scope, build.Sql!, build.Parameters,
                ProductionPath.QueryBuilder, rawPrompt, userId, source, build.Detail);
        }

        return RejectWithoutSql(request, scope, build.ReasonCode,
            ProductionPath.QueryBuilder, rawPrompt, userId, build.Detail);
    }

    private RoutingResult Finish(
        CanonicalRequest request,
        UserDataScope scope,
        string sql,
        IReadOnlyList<SqlParameterSpec> parameters,
        ProductionPath path,
        string? rawPrompt,
        string? userId,
        DataSource source,
        string? detail)
    {
        var context = new GuardrailContext(
            sql, allowList, scope, parserFactory, source, request, parameters);

        var result = GuardrailFactory.Create().Execute(context);

        auditWriter.Write(DecisionAuditRecordFactory.From(context, result, path, userId, rawPrompt));

        return new RoutingResult(result, path, detail);
    }

    /// <summary>
    /// SQL uretilemeden verilen ret/netlestirme karari. Guardrail kosmadigi icin kontrol
    /// listesi bostur; audit bunu oldugu gibi kaydeder — kosulmamis kontrolu "gecti" saymak
    /// guvenlik kanitini yaniltici hale getirirdi.
    /// </summary>
    private RoutingResult RejectWithoutSql(
        CanonicalRequest request,
        UserDataScope scope,
        ReasonCode reasonCode,
        ProductionPath path,
        string? rawPrompt,
        string? userId,
        string? detail)
    {
        var result = reasonCode is ReasonCode.CL001 or ReasonCode.CL002
            ? GuardrailResult.NeedsClarification(reasonCode, [], parserFactory.VersionName)
            : GuardrailResult.Rejected(reasonCode, [], parserFactory.VersionName);

        var context = new GuardrailContext(
            "(SQL uretilemedi)", allowList, scope, parserFactory, source, request);

        auditWriter.Write(DecisionAuditRecordFactory.From(context, result, path, userId, rawPrompt));

        return new RoutingResult(result, path, detail);
    }
}
