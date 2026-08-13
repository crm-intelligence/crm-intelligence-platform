using System.Reflection;
using Crm.Analytics.Sql.IntegrationTests.Sqlite;
using Crm.Analytics.Sql.Parsing;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.IntegrationTests.Contract;

/// <summary>
/// Sozlesme, SQLite DDL'i ve allow-list arasindaki sapmayi yakalar.
/// Veritabani gerektirmez.
/// </summary>
/// <remarks>
/// <para>
/// Bu test <c>Crm.Analytics.Sql/Catalog/olist_views.contract.sql</c> dosyasina
/// ILK OTOMATIK TUKETICISINI kazandirir. Bugune kadar o dosyaya hicbir kod
/// bakmiyordu — yalnizca prose icinden referans veriliyordu. Sapmayi yakalayacak
/// hicbir mekanizma yoktu.
/// </para>
/// <para>
/// Sapmanin somut sonucu gorulduу: elle paylasilan <c>crm_dev.db</c> dosyasinda
/// gorunumler <c>vw_customers</c>/<c>vw_payments</c> adiyla, RFM toplamlari
/// olmadan ve <c>product_category</c> yerine <c>product_category_name_english</c>
/// ile olusturulmustu. Guardrail'in urettigi hicbir sorgu o dosyada
/// calisamazdi ve bunu soyleyen tek bir test yoktu.
/// </para>
/// </remarks>
public sealed class ViewContractDriftTests
{
    private static readonly string[] BeklenenGorunumler =
        ["vw_sales", "vw_customer_rfm", "vw_payment"];

    [Fact]
    public void Sozlesme_ve_sqlite_ddl_ayni_gorunumleri_tanimliyor()
    {
        var sozlesme = ViewColumns("olist_views.contract.sql");
        var sqlite = ViewColumns("sqlite_views.sql");

        Assert.Equal(
            BeklenenGorunumler.OrderBy(n => n, StringComparer.Ordinal),
            sozlesme.Keys.OrderBy(n => n, StringComparer.Ordinal));

        Assert.Equal(
            sozlesme.Keys.OrderBy(n => n, StringComparer.Ordinal),
            sqlite.Keys.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("vw_sales")]
    [InlineData("vw_customer_rfm")]
    [InlineData("vw_payment")]
    public void Kolon_listeleri_sozlesme_sqlite_ve_allow_list_arasinda_ayni(string gorunum)
    {
        var sozlesme = ViewColumns("olist_views.contract.sql")[gorunum];
        var sqlite = ViewColumns("sqlite_views.sql")[gorunum];
        var allowList = OlistCatalog.AllowList.Objects[gorunum].Columns;

        Assert.Equal(sozlesme, sqlite);
        Assert.Equal(
            sozlesme.OrderBy(c => c, StringComparer.Ordinal),
            allowList.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("vw_sales")]
    [InlineData("vw_customer_rfm")]
    [InlineData("vw_payment")]
    public void Her_gorunum_kapsam_kolonunu_iceriyor(string gorunum)
    {
        var kolonlar = ViewColumns("sqlite_views.sql")[gorunum];
        var kapsamKolonu = OlistCatalog.AllowList.Objects[gorunum].ScopeColumn;

        // Kapsam kolonu gorunumde YOKSA guardrail kapsam filtresini enjekte
        // edemez ve sorgu filtresiz kalir.
        Assert.Equal("customer_state", kapsamKolonu);
        Assert.Contains(kapsamKolonu, kolonlar);
    }

    [OlistDatabaseFact]
    public void Canli_veritabanindaki_kolonlar_sozlesmeyle_ayni()
    {
        // Statik dosyalar tutarli olsa bile fixture eski olabilir. Bu test
        // zinciri canli veritabanina kadar uzatir.
        using var connection = OlistTestDatabase.OpenReadOnly();

        foreach (var gorunum in BeklenenGorunumler)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({gorunum});";

            var canli = new List<string>();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                canli.Add(reader.GetString(1));
            }

            Assert.Equal(ViewColumns("sqlite_views.sql")[gorunum], canli);
        }
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Gomulu bir SQL dosyasindaki <c>CREATE VIEW</c> ifadelerini ayristirir ve
    /// gorunum adi -> cikti kolon listesi eslemesi dondurur.
    /// </summary>
    /// <remarks>
    /// Iki dosya da gecerli T-SQL sozdiziminde oldugu icin kutuphanenin kendi
    /// <see cref="TSqlParserFactory"/>'si kullaniliyor — ayri bir ayristirici
    /// yazilmiyor.
    /// </remarks>
    private static Dictionary<string, List<string>> ViewColumns(string resourceName)
    {
        // Iki dosya da GO ile ayrilmis batch'ler iceriyor, bu yuzden ikisi de
        // butun halinde ayristirilabilir. ScriptDom yorumlari ve metin
        // sabitlerini dogru ele alir — elle bolucu yazmak gereksiz ve kirilgan
        // olurdu (blok yorumlari icindeki noktali virguller yuzunden).
        var sql = ReadEmbedded(resourceName);
        var parsed = new TSqlParserFactory().Parse(sql);

        Assert.True(
            parsed.Errors.Count == 0,
            $"{resourceName} T-SQL olarak ayristirilamadi: " +
            $"{(parsed.Errors.Count > 0 ? parsed.Errors[0].Message : string.Empty)}");

        var collector = new ViewCollector();
        parsed.Fragment!.Accept(collector);

        return collector.Views;
    }

    private static string ReadEmbedded(string logicalName)
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException(
                $"Gomulu kaynak bulunamadi: {logicalName}. " +
                $"Mevcut: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class ViewCollector : TSqlFragmentVisitor
    {
        public Dictionary<string, List<string>> Views { get; } = new(StringComparer.Ordinal);

        public override void Visit(CreateViewStatement node)
        {
            var name = node.SchemaObjectName.Identifiers[^1].Value;

            if (node.SelectStatement.QueryExpression is QuerySpecification block)
            {
                Views[name] = block.SelectElements
                    .OfType<SelectScalarExpression>()
                    .Select(ColumnNameOf)
                    .ToList();
            }

            base.Visit(node);
        }

        /// <summary>
        /// Bir SELECT ogesinin cikti kolon adini bulur: varsa takma ad, yoksa
        /// kolon referansinin son parcasi.
        /// </summary>
        private static string ColumnNameOf(SelectScalarExpression element)
        {
            if (element.ColumnName?.Value is { Length: > 0 } alias)
            {
                return alias;
            }

            if (element.Expression is ColumnReferenceExpression column)
            {
                return column.MultiPartIdentifier.Identifiers[^1].Value;
            }

            throw new InvalidOperationException(
                "Cikti kolon adi belirlenemedi: takma adi olmayan bir ifade var. " +
                "Gorunum tanimlarinda her hesaplanan kolon AS ile adlandirilmalidir.");
        }
    }
}
