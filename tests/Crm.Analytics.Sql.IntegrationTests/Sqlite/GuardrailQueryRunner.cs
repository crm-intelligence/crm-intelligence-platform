using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.Service;
using Microsoft.Data.Sqlite;

namespace Crm.Analytics.Sql.IntegrationTests.Sqlite;

/// <summary>
/// Guardrail'in ONAYLADIGI sorguyu yerel fixture'da calistirir.
/// </summary>
/// <remarks>
/// <para><b>Bu sinifin uc katı kurali var:</b></para>
/// <list type="number">
///   <item>
///     <b>Yalnizca <c>Accepted</c> calistirilir.</b> Reddedilen veya
///     netlestirme isteyen bir sonucla cagrilmak istisna atar — bir test
///     kazara reddedilmis bir taslagi calistiramaz.
///   </item>
///   <item>
///     <b>Yeniden dogrulama YOK.</b> Kosucu guardrail'i tekrar calistirmaz ve
///     SQL'i "onarmaz". Onarirsa, uretimde gecmeyecek bir sey testte gecer.
///   </item>
///   <item>
///     <b>Yakalama YOK.</b> Ceviri veya SQLite hatasi yutulmaz, orijinal SQL'e
///     geri donulmez. Musamahakar bir harness, "bu yapi cevrilemiyor" durumunu
///     "bu metrik biraz yanlis" durumuna cevirir.
///   </item>
/// </list>
/// </remarks>
internal sealed class GuardrailQueryRunner(SqliteConnection connection, TSqlParserFactory parserFactory)
{
    private readonly SqliteDialectTranslator translator = new(parserFactory);

    /// <summary>
    /// Son calistirilan SQLite metni. Testler bunun icinde literal deger
    /// bulunmadigini dogrulayabilsin diye aciliyor.
    /// </summary>
    public string? LastExecutedSql { get; private set; }

    /// <summary>
    /// Onaylanmis sorguyu calistirir ve satirlari dondurur.
    /// </summary>
    public List<Dictionary<string, object?>> Execute(GuardrailResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Decision != GuardrailDecision.Accepted)
        {
            throw new InvalidOperationException(
                $"Yalnizca Accepted sonuclar calistirilabilir. Gelen: {result.Decision} " +
                $"({result.ReasonCode}). Reddedilmis bir sorgunun veritabanina " +
                "ulasmasi harness'in sozlesmesini ihlal eder.");
        }

        var sql = result.Sql
            ?? throw new InvalidOperationException(
                "Accepted sonucta Sql bos olamaz.");

        return Execute(sql, result.Parameters, result.QueryTimeoutSeconds);
    }

    /// <summary>
    /// Uretim servisinin onayladigi yaniti calistirir.
    /// </summary>
    /// <remarks>
    /// Ayri bir asiri yukleme olarak duruyor cunku harness'in sahte bir
    /// <see cref="GuardrailResult"/> KURMASI yanlis olurdu: onay uretmek
    /// guardrail'in isi, harness'in degil.
    /// </remarks>
    public List<Dictionary<string, object?>> Execute(SqlProductionResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Decision != GuardrailDecision.Accepted)
        {
            throw new InvalidOperationException(
                $"Yalnizca Accepted yanitlar calistirilabilir. Gelen: {response.Decision} " +
                $"({response.ReasonCode}).");
        }

        var sql = response.Sql
            ?? throw new InvalidOperationException("Accepted yanitta Sql bos olamaz.");

        // Kapsam filtresinin uygulandiginin kaniti; bos olmasi sozlesme ihlali.
        if (string.IsNullOrWhiteSpace(response.AppliedScopeFilter))
        {
            throw new InvalidOperationException(
                "Accepted yanitta AppliedScopeFilter bos olamaz.");
        }

        return Execute(sql, response.Parameters, response.CommandTimeoutSeconds);
    }

    /// <summary>
    /// T-SQL metnini cevirir, parametreleri baglar ve calistirir.
    /// </summary>
    public List<Dictionary<string, object?>> Execute(
        string tsql,
        IReadOnlyList<SqlParameterSpec> parameters,
        int? timeoutSeconds = null)
    {
        var translated = translator.Translate(tsql);
        LastExecutedSql = translated;

        using var command = connection.CreateCommand();
        command.CommandText = translated;

        if (timeoutSeconds is { } timeout)
        {
            // Sozlesme alaninin baglandigini gostermek icin uygulaniyor.
            // NOT: SQLite'ta bu bir KILIT BEKLEME suresi, sorgu iptali degil —
            // dolayisiyla bu bir timeout testi degildir.
            command.CommandTimeout = timeout;
        }

        SqliteParameterBinder.Bind(command, parameters);

        // Prepare(): SQLite ifadeyi derler. Boylece yalnizca satir sayan bir
        // test bile artik kalinti bir dialekt hatasini yakalar — hata, sorunlu
        // token ile birlikte burada yuzeye cikar.
        command.Prepare();

        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();

        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);

            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }
}
