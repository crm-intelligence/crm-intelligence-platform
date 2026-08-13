using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail.Visitors;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Mutation;

/// <summary>
/// Kullanicinin veri kapsami filtresini AST uzerinde <b>zorla</b> enjekte eder.
/// </summary>
/// <remarks>
/// <para>
/// Sorguda kapsam filtresi olup olmadigina bakilmaz; filtre her zaman guardrail tarafindan
/// eklenir. Kullanicinin yazdigi bir <c>region = 'Ege'</c> kosulu guvenlik acisindan hicbir
/// anlam tasimaz.
/// </para>
/// <para>
/// Filtre <b>agactaki her sorgu blokuna</b> ayri ayri eklenir. Dokumandaki "WHERE'e ekle"
/// ifadesi tek bir WHERE oldugunu varsayar; CTE, turetilmis tablo ve UNION kollari bu
/// varsayimi bozar ve tek noktaya eklenen filtre bu yapilarda veri sizdirir.
/// </para>
/// </remarks>
public sealed class ScopeFilterInjector
{
    /// <summary>Kapsam parametrelerinin adlandirma oneki. Diger parametrelerden ayrilir.</summary>
    private const string ParameterPrefix = "scope";

    public sealed record Outcome(
        int InjectedBlockCount,
        int InjectedPredicateCount,
        int ExemptTableCount,
        IReadOnlyList<string> Description)
    {
        public bool AnythingInjected => InjectedPredicateCount > 0;
    }

    /// <summary>
    /// Kapsam filtresini uygular. Cagiran, kapsamin cozumlenebilir ve sinirli oldugunu
    /// (<see cref="UserDataScope.IsResolvable"/>, <see cref="UserDataScope.IsUnrestricted"/>)
    /// onceden dogrulamis olmalidir.
    /// </summary>
    public Outcome Inject(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fragment = context.RequireFragment();
        var cteNames = CteNameCollector.CollectFrom(fragment);
        var blocks = QuerySpecificationCollector.CollectFrom(fragment);

        var parameterNames = CreateScopeParameters(context);
        var descriptions = new List<string>();
        var injectedBlocks = 0;
        var injectedPredicates = 0;
        var exemptTables = 0;

        foreach (var block in blocks)
        {
            var predicates = new List<BooleanExpression>();
            var tables = DirectTableReferenceReader.Read(block);

            foreach (var table in tables.Tables)
            {
                // CTE referansi bir obje degildir; filtre govdesine eklenir, referansina degil.
                if (cteNames.Contains(table.ObjectName))
                {
                    continue;
                }

                var allowed = context.AllowList.FindSqlObject(table.ObjectName);

                if (allowed is null)
                {
                    // AllowListObjects kontrolu bu kontrolden ONCE calisir, dolayisiyla buraya
                    // dusmemesi gerekir. Dustuyse siralama bozulmus demektir; sessizce atlamak
                    // filtresiz sorgu uretmek olurdu.
                    throw new InvalidOperationException(
                        $"'{table.ObjectName}' allow-list'te yok ancak kapsam enjeksiyonuna kadar gelmis. " +
                        "Kontrol sirasi bozulmus olabilir.");
                }

                if (allowed.IsScopeExempt)
                {
                    exemptTables++;
                    continue;
                }

                var scopeColumn = allowed.ScopeColumn
                    ?? throw new InvalidOperationException(
                        $"'{table.ObjectName}' icin scopeColumn tanimli degil. " +
                        "Allow-list yuklemesi bunu engellemeliydi.");

                predicates.Add(BuildScopePredicate(context, scopeColumn, table.Qualifier, parameterNames));
                injectedPredicates++;
                descriptions.Add($"{table.Qualifier.Value}.{scopeColumn} IN ({string.Join(", ", parameterNames)})");
            }

            if (predicates.Count == 0)
            {
                continue;
            }

            ApplyToWhereClause(block, predicates);
            injectedBlocks++;
        }

        return new Outcome(injectedBlocks, injectedPredicates, exemptTables, descriptions);
    }

    /// <summary>
    /// Kapsam degerlerini parametre olarak kaydeder. Degerler <b>tek bir kez</b> uretilir ve
    /// tum sorgu bloklari ayni parametreleri paylasir; blok basina yeni parametre uretmek
    /// gereksiz ve plan cache'i sisirici olurdu.
    /// </summary>
    private static IReadOnlyList<string> CreateScopeParameters(GuardrailContext context)
    {
        var values = context.Scope.ValuesFor(UserDataScope.RegionDimension);

        if (values.Count == 0)
        {
            throw new InvalidOperationException(
                "Kapsam degeri bos. Cagiran, IsResolvable kontrolunu yapmis olmaliydi.");
        }

        var names = new List<string>(values.Count);

        foreach (var value in values)
        {
            var name = context.NextParameterName(ParameterPrefix);
            // Kapsam degerleri metindir ve NVARCHAR olarak baglanir: Turkce karakter kaybi
            // collation'a bagli olarak farkli satir kumesi dondurebilir.
            context.AddParameter(new SqlParameterSpec(name, FilterValueKind.Text, value));
            names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// <c>qualifier.scopeColumn IN (@scope0, @scope1)</c> yukleminin AST'sini kurar.
    /// </summary>
    /// <remarks>
    /// Metne yalnizca allow-list'ten gelen kolon adi ve kendi urettigimiz parametre adlari
    /// girer. Nitelendirici (alias) kullanicidan geldigi icin metne DEGIL, AST dugumu olarak
    /// eklenir; koseli parantez kacisi barindiran bir alias metne gomulse guardrail kendi
    /// eliyle enjeksiyon uretmis olurdu.
    /// </remarks>
    private static BooleanExpression BuildScopePredicate(
        GuardrailContext context,
        string scopeColumn,
        Identifier qualifier,
        IReadOnlyList<string> parameterNames)
    {
        var predicate = context.ParserFactory.ParseBooleanExpression(
            $"{scopeColumn} IN ({string.Join(", ", parameterNames)})");

        QualifyColumnReferences(predicate, qualifier);
        return predicate;
    }

    private static void QualifyColumnReferences(BooleanExpression predicate, Identifier qualifier)
    {
        var qualifierVisitor = new ColumnQualifierVisitor(qualifier);
        predicate.Accept(qualifierVisitor);

        if (qualifierVisitor.QualifiedCount == 0)
        {
            throw new InvalidOperationException(
                "Kapsam yukleminde nitelendirilecek kolon referansi bulunamadi.");
        }
    }

    /// <summary>
    /// Mevcut WHERE ile kapsam yuklemini birlestirir.
    /// </summary>
    /// <remarks>
    /// Mevcut kosul <b>parantez icine alinir</b>. Bu kozmetik degil, dogruluk gereksinimidir:
    /// mevcut kosul <c>a OR b</c> ise, parantezsiz birlestirme <c>a OR b AND scope</c> uretir
    /// ve AND'in onceligi nedeniyle <c>a</c> dalindaki satirlar kapsam filtresinden KACAR.
    /// Dogru sonuc <c>(a OR b) AND scope</c> olmalidir.
    /// </remarks>
    private static void ApplyToWhereClause(QuerySpecification block, List<BooleanExpression> predicates)
    {
        var scopeCondition = predicates.Aggregate(Combine);

        if (block.WhereClause is null)
        {
            block.WhereClause = new WhereClause { SearchCondition = scopeCondition };
            return;
        }

        block.WhereClause.SearchCondition = Combine(
            new BooleanParenthesisExpression { Expression = block.WhereClause.SearchCondition },
            scopeCondition);
    }

    private static BooleanExpression Combine(BooleanExpression left, BooleanExpression right) =>
        new BooleanBinaryExpression
        {
            BinaryExpressionType = BooleanBinaryExpressionType.And,
            FirstExpression = left,
            SecondExpression = right
        };

    private sealed class ColumnQualifierVisitor(Identifier qualifier) : TSqlFragmentVisitor
    {
        public int QualifiedCount { get; private set; }

        public override void Visit(ColumnReferenceExpression node)
        {
            if (node.MultiPartIdentifier is null)
            {
                return;
            }

            // Yeni Identifier uretilir; kullanicidan gelen dugum agacin iki yerinde
            // paylasilmaz. QuoteType korunur, boylece uretici kacis karakterlerini
            // dogru sekilde yeniden yazar.
            node.MultiPartIdentifier.Identifiers.Insert(0, new Identifier
            {
                Value = qualifier.Value,
                QuoteType = qualifier.QuoteType
            });

            QualifiedCount++;
        }
    }
}
