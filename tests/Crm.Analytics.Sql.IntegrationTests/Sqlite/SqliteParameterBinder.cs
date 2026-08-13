using System.Globalization;
using Crm.Analytics.Sql.Contracts;
using Microsoft.Data.Sqlite;

namespace Crm.Analytics.Sql.IntegrationTests.Sqlite;

/// <summary>
/// Guardrail'in dondurdugu <see cref="SqlParameterSpec"/> listesini SQLite
/// parametrelerine baglar.
/// </summary>
/// <remarks>
/// <para>
/// <c>ENTEGRASYON.md</c> (satir 118-138) baglamayi <c>SqlDbType</c> uzerinden
/// tanimlar; <c>Microsoft.Data.Sqlite</c>'ta ise yalnizca
/// <c>Text / Integer / Real / Blob</c> vardir — <c>Date</c>, <c>Decimal</c> ve
/// <c>Bit</c> karsiligi yok. Bu yuzden paralel bir esleme gerekiyor:
/// </para>
/// <list type="table">
///   <item><term>Text</term><description>-> Text</description></item>
///   <item><term>Integer</term><description>-> Integer</description></item>
///   <item><term>Decimal</term><description>-> Real</description></item>
///   <item><term>Boolean</term><description>-> Integer (0/1)</description></item>
///   <item><term>Date</term><description>-> Text (ISO 8601)</description></item>
/// </list>
/// <para>
/// <c>Date -> Text</c> secimi keyfi degil: fixture'da
/// <c>order_purchase_timestamp</c> ISO bicimli TEXT olarak saklanir ve bu
/// bicimde sozluksel karsilastirma kronolojik karsilastirmayla ayni sonucu
/// verir. Boylece <c>WHERE ts &gt;= @f0</c> uretimdeki T-SQL <c>datetime</c>
/// karsilastirmasiyla ayni satirlari secer.
/// </para>
/// <para>
/// <c>default</c> dalinin hata atmasi, <c>ENTEGRASYON.md:137</c>'deki bilincli
/// tasarim karari: yeni bir tip eklendiginde sessizce yanlis baglamak, farkli
/// bir satir kumesi dondurebilir.
/// </para>
/// </remarks>
internal static class SqliteParameterBinder
{
    public static void Bind(SqliteCommand command, IReadOnlyList<SqlParameterSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(specs);

        foreach (var spec in specs)
        {
            var parameter = command.Parameters.Add(spec.Name, MapType(spec.Kind));
            parameter.Value = MapValue(spec);
        }
    }

    private static SqliteType MapType(FilterValueKind kind) => kind switch
    {
        FilterValueKind.Text => SqliteType.Text,
        FilterValueKind.Integer => SqliteType.Integer,
        FilterValueKind.Decimal => SqliteType.Real,
        FilterValueKind.Boolean => SqliteType.Integer,
        FilterValueKind.Date => SqliteType.Text,
        _ => throw new NotSupportedException(
            $"Bilinmeyen parametre tipi: {kind}. Sessizce baglamak yerine " +
            "duruldu (bkz. ENTEGRASYON.md:137).")
    };

    private static object MapValue(SqlParameterSpec spec) => spec.Kind switch
    {
        FilterValueKind.Text or FilterValueKind.Date => spec.Raw,

        FilterValueKind.Integer => long.Parse(spec.Raw, CultureInfo.InvariantCulture),

        FilterValueKind.Decimal => double.Parse(spec.Raw, CultureInfo.InvariantCulture),

        // SQLite'ta boolean yok; 0/1 olarak saklanir.
        FilterValueKind.Boolean => bool.Parse(spec.Raw) ? 1L : 0L,

        _ => throw new NotSupportedException(
            $"Bilinmeyen parametre tipi: {spec.Kind}.")
    };
}
