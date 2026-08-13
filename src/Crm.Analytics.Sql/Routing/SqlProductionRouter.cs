using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Nlu;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;

namespace Crm.Analytics.Sql.Routing;

/// <summary>
/// Canonical talebi yalnizca deterministik Query Builder'a yonlendirir ve ciktiyi
/// guardrail hattindan gecirir.
/// </summary>
/// <remarks>
/// <para>
/// Bu sinif, sistemde SQL uretmenin TEK giris noktasidir. Guardrail'i atlayan bir yol
/// bilincli olarak birakilmamistir — "test amacli bile" birakilmaz.
/// </para>
/// <para>
/// Model SQL'i guvenlik sinirinin disindadir. Query Builder talebi karsilayamiyorsa sistem
/// fail-closed davranir ve netlestirme veya ret sonucu dondurur.
/// </para>
/// </remarks>
public sealed class SqlProductionRouter(
    DeterministicQueryBuilder queryBuilder,
    AllowListDocument allowList,
    TSqlParserFactory parserFactory,
    IDecisionAuditWriter auditWriter,
    AmbiguityGate? ambiguityGate = null,
    DataSource source = DataSource.Dwh)
{
    private readonly AmbiguityGate ambiguityGate = ambiguityGate ?? new AmbiguityGate();

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
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scope);

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

        var build = queryBuilder.Build(request);

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
