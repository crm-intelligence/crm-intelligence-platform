using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Routing;

namespace Crm.Analytics.Sql.Service;

/// <summary>
/// Backend'in SQL uretim servisine verdigi istek.
/// </summary>
/// <remarks>
/// <para>
/// <b>Servis durumsuzdur.</b> Takip sorusu icin onceki talebin <i>kimligi</i> degil kendisi
/// tasinir: konusma durumunu saklamak Backend'in isidir (bkz. 09-backend-teams.md, Conversation
/// ID / Previous Request ID yonetimi). Bu bilesene bir durum deposu koymak, olceklenmeyi
/// bozar ve iki farkli yerde ayni gercegin tutulmasina yol acardi.
/// </para>
/// <para>
/// <b>Veri kapsami cagirandan gelir.</b> Kullanici -> kapsam eslemesi Backend ve Veri Muhendisi'nin
/// sorumlulugunda; bu servis kapsami <i>uretmez</i>, aldigi kapsami SQL'e zorla uygular.
/// Cozumlenemeyen kapsam (<see cref="UserDataScope.Unresolved"/>) sinirsiz anlamina gelmez,
/// ret sebebidir.
/// </para>
/// </remarks>
public sealed record SqlProductionRequest
{
    /// <summary>Kullanicinin serbest metni.</summary>
    public required string Prompt { get; init; }

    /// <summary>Bu talebe atanan kimlik. Audit kaydi ve Backend durum sorgusu buna baglanir.</summary>
    public required string RequestId { get; init; }

    public required string ConversationId { get; init; }

    /// <summary>Kullanicinin gorebilecegi veri kapsami.</summary>
    public required UserDataScope Scope { get; init; }

    /// <summary>
    /// Gorece tarih ifadelerinin cozumlendigi referans gun. Cagirandan gelmesi bilincli:
    /// sistem saatine bagli bir cozumleme audit'te yeniden uretilemezdi.
    /// </summary>
    public required DateOnly Today { get; init; }

    /// <summary>
    /// Takip sorusuysa revize edilecek onceki talep. Dolu ise metin <b>delta</b> olarak
    /// cozumlenir: belirtilmeyen alanlar oncekinden korunur.
    /// </summary>
    public CanonicalRequest? PreviousRequest { get; init; }

    public string? UserId { get; init; }

    /// <summary>
    /// Acik source secimi. Null ise servis iki catalog'u deterministik olarak cozer;
    /// null hicbir zaman DWH varsayimi degildir.
    /// </summary>
    public DataSource? Source { get; init; }
}

/// <summary>
/// Backend'e donen sonuc.
/// </summary>
/// <remarks>
/// Backend durum modeline (09-backend-teams.md 9.2) eslemesi:
/// <list type="bullet">
/// <item><see cref="GuardrailDecision.Accepted"/> -> sorgu calistirilir, sonra <c>Completed</c>.</item>
/// <item><see cref="GuardrailDecision.NeedsClarification"/> -> <c>NeedsClarification</c>, <see cref="UserMessage"/> gosterilir.</item>
/// <item><see cref="GuardrailDecision.Rejected"/> -> <c>Rejected</c>, <see cref="UserMessage"/> gosterilir.</item>
/// </list>
/// <c>Failed</c> durumu bu servisin uretmedigi bir durumdur: teknik hata calistirma
/// katmaninda olusur. Guardrail'in kendi hatasi <c>GR014</c> ile <see cref="GuardrailDecision.Rejected"/>
/// olarak doner — fail-closed.
/// </remarks>
public sealed record SqlProductionResponse
{
    public required string RequestId { get; init; }

    public required GuardrailDecision Decision { get; init; }

    /// <summary>
    /// Calistirilmaya hazir SQL. <b>Yalnizca</b> <see cref="GuardrailDecision.Accepted"/>
    /// durumunda dolu. Reddedilen sorgunun metni disa verilmez.
    /// </summary>
    public string? Sql { get; init; }

    /// <summary>
    /// Baglanacak parametreler. Degerler SQL metnine hicbir asamada gomulmez;
    /// calistirma katmani bunlari <c>SqlParameter</c> olarak baglar.
    /// </summary>
    public IReadOnlyList<SqlParameterSpec> Parameters { get; init; } = [];

    /// <summary>Guardrail'in enjekte ettigi kapsam filtresi. Accepted durumunda bos olamaz.</summary>
    public string? AppliedScopeFilter { get; init; }

    /// <summary>Komut timeout'u. Uygulamasi calistirma katmanina aittir.</summary>
    public int? CommandTimeoutSeconds { get; init; }

    /// <summary>Secili source contract'inin izin verdigi azami sonuc satiri.</summary>
    public int? RowLimit { get; init; }

    /// <summary>Guardrail AST/allow-list kontrolunden gecmis fiziksel okuma objesi.</summary>
    public string? PhysicalObject { get; init; }

    public DataSource? Source { get; init; }

    public ReasonCode ReasonCode { get; init; }

    /// <summary>
    /// Kullaniciya gosterilecek mesaj. Sema bilgisi (gorunum adi, kolon adi, SQL parcasi)
    /// icermez: ret mesaji bir sema kesif araci olmamalidir.
    /// </summary>
    public string? UserMessage { get; init; }

    /// <summary>
    /// Sonuc yapisi ve onerilen gorsel. BI tarafi oneriyi kabul etmek zorunda degildir.
    /// </summary>
    public ResultShape? ResultShape { get; init; }

    /// <summary>
    /// Cozumlenmis talep. <b>Backend bunu saklamalidir</b>: bir sonraki takip sorusunda
    /// <see cref="SqlProductionRequest.PreviousRequest"/> olarak geri verilir.
    /// </summary>
    public CanonicalRequest? CanonicalRequest { get; init; }

    /// <summary>SQL'in hangi yoldan uretildigi. Audit ve teshis icin.</summary>
    public ProductionPath Path { get; init; }

    /// <summary>Kosulan, basarisiz olan ve atlanan tum kontroller.</summary>
    public IReadOnlyList<CheckResult> Checks { get; init; } = [];

    /// <summary>Cozumlenemeyen terimler. Netlestirme sorusunu zenginlestirmek icin kullanilabilir.</summary>
    public IReadOnlyList<string> UnresolvedTerms { get; init; } = [];
}
