using Crm.Analytics.Sql.Contracts;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Mutation;

/// <summary>
/// Filtre literal'lerini parametreye cevirir.
/// </summary>
/// <remarks>
/// <para>
/// Savunma katmani, kendisine ulasan literal'i AST uzerinde
/// <see cref="VariableReference"/> ile degistirir
/// ve degeri <see cref="SqlParameterSpec"/> olarak tasir; deger hicbir asamada SQL metnine
/// gomulmez.
/// </para>
/// <para>
/// Yalnizca <b>karsilastirma ve uyelik yuklemleri</b> icindeki literal'ler donusturulur
/// (<c>=</c>, <c>IN</c>, <c>BETWEEN</c>). Sebep: T-SQL bazi konumlarda degisken kabul etmez
/// (<c>TOP</c> parantez gerektirir, <c>CAST</c>'in tip argumani sabit olmak zorundadir,
/// <c>ORDER BY</c> ordinal'leri sabittir). Bu konumlardaki literal'lere dokunmak gecersiz SQL
/// uretirdi.
/// </para>
/// <para>
/// <see cref="NullLiteral"/>, <see cref="DefaultLiteral"/> ve <see cref="MaxLiteral"/>
/// donusturulmez: <c>x = NULL</c> ile <c>x = @p</c> semantik olarak ayni degildir
/// (ANSI_NULLS), <c>DEFAULT</c> ve <c>MAX</c> ise deger degil anahtar kelimedir.
/// </para>
/// </remarks>
public sealed class LiteralParameterizer
{
    public sealed record Outcome(int ParameterizedCount, int SkippedCount);

    public Outcome Parameterize(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var visitor = new FilterLiteralVisitor(context);
        context.RequireFragment().Accept(visitor);

        return new Outcome(visitor.ParameterizedCount, visitor.SkippedCount);
    }

    private sealed class FilterLiteralVisitor(GuardrailContext context) : TSqlFragmentVisitor
    {
        public int ParameterizedCount { get; private set; }

        public int SkippedCount { get; private set; }

        public override void Visit(BooleanComparisonExpression node)
        {
            node.FirstExpression = Convert(node.FirstExpression);
            node.SecondExpression = Convert(node.SecondExpression);
        }

        public override void Visit(InPredicate node)
        {
            for (var index = 0; index < node.Values.Count; index++)
            {
                node.Values[index] = Convert(node.Values[index]);
            }
        }

        public override void Visit(BooleanTernaryExpression node)
        {
            // BETWEEN: ilk ifade kolon, diger ikisi sinirlar.
            node.SecondExpression = Convert(node.SecondExpression);
            node.ThirdExpression = Convert(node.ThirdExpression);
        }

        public override void Visit(LikePredicate node)
        {
            node.SecondExpression = Convert(node.SecondExpression);
        }

        private ScalarExpression Convert(ScalarExpression expression)
        {
            if (expression is not Literal literal)
            {
                return expression;
            }

            var kind = MapKind(literal);

            if (kind is null)
            {
                // NULL / DEFAULT / MAX ve taninmayan tipler: dokunulmaz.
                SkippedCount++;
                return expression;
            }

            var name = context.NextParameterName();
            context.AddParameter(new SqlParameterSpec(name, kind.Value, literal.Value));
            ParameterizedCount++;

            return new VariableReference { Name = name };
        }

        /// <summary>
        /// Literal tipini parametre tipine esler.
        /// </summary>
        /// <remarks>
        /// <see cref="StringLiteral.IsNational"/> ayrica okunmaz: metin parametreleri her zaman
        /// unicode baglanir (<see cref="SqlParameterSpec.IsUnicode"/>). Sebep, <c>N</c> onekini
        /// kaybetmenin collation'a bagli olarak FARKLI SATIR KUMESI dondurebilmesi ve Turkce
        /// I/i ciftinin bu yolla filtre atlatmaya zemin hazirlamasidir.
        /// </remarks>
        private static FilterValueKind? MapKind(Literal literal) => literal switch
        {
            StringLiteral => FilterValueKind.Text,
            IntegerLiteral => FilterValueKind.Integer,
            NumericLiteral => FilterValueKind.Decimal,
            RealLiteral => FilterValueKind.Decimal,
            MoneyLiteral => FilterValueKind.Decimal,
            _ => null
        };
    }
}
