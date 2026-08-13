using CrmAnalytics.Api.Integrations.CopilotStudio;
using CrmAnalytics.Contracts.CopilotStudio;

namespace CrmAnalytics.UnitTests;

public sealed class CopilotPlannedReportRequestNormalizerTests
{
    [Fact]
    public void Outcome_PreservesContextIntentAndControlsClarification()
    {
        var accepted = CopilotPlannedReportRequestNormalizer.Normalize(
            Request(
                outcome: "accepted",
                clarification: new CopilotClarification { Kind = "metric" }));

        Assert.NotNull(accepted.SemanticIntent);
        Assert.Null(accepted.Clarification);

        var needsClarification =
            CopilotPlannedReportRequestNormalizer.Normalize(
                Request(
                    outcome: "needs_clarification",
                    clarification: new CopilotClarification
                    {
                        Kind = "metric"
                    }));

        Assert.NotNull(needsClarification.SemanticIntent);
        Assert.Equal("metric", needsClarification.Clarification?.Kind);

        var unsupported = CopilotPlannedReportRequestNormalizer.Normalize(
            Request(
                outcome: "unsupported",
                unresolvedConcepts:
                [new CopilotUnresolvedConcept { Kind = "dimension" }],
                clarification: new CopilotClarification { Kind = "metric" }));

        Assert.NotNull(unsupported.SemanticIntent);
        Assert.Null(unsupported.Clarification);
        Assert.Equal(
            "dimension",
            Assert.Single(unsupported.UnresolvedConcepts).Kind);
    }

    [Fact]
    public void EmptyValuesAndIncompleteRanking_AreNormalizedAtBoundary()
    {
        var result = CopilotPlannedReportRequestNormalizer.Normalize(Request());
        var intent = Assert.IsType<
            CrmAnalytics.Contracts.Integrations.SubmittedSemanticIntent>(
                result.SemanticIntent);
        Assert.Null(intent.Ranking);
        Assert.Null(intent.Date.RelativeExpression);
        Assert.Null(intent.Date.From);
        Assert.Null(intent.Date.To);
        Assert.Null(intent.Date.Count);
    }

    [Fact]
    public void CompletePositiveRanking_IsCreated()
    {
        var result = CopilotPlannedReportRequestNormalizer.Normalize(
            Request(intent: Intent(
                ranking: new CopilotRankingIntent
                {
                    TopN = 10,
                    OrderBy = " order_count ",
                    Direction = " desc "
                })));

        Assert.NotNull(result.SemanticIntent);
        Assert.Equal(10, result.SemanticIntent.Ranking?.TopN);
        Assert.Equal("order_count", result.SemanticIntent.Ranking?.OrderBy);
        Assert.Equal("desc", result.SemanticIntent.Ranking?.Direction);
    }

    [Theory]
    [InlineData(-1, "order_count", "desc")]
    [InlineData(0, "order_count", "desc")]
    [InlineData(10, "", "desc")]
    [InlineData(10, "order_count", "")]
    public void IncompleteOrNonPositiveRanking_IsOmitted(
        int topN,
        string orderBy,
        string direction)
    {
        var result = CopilotPlannedReportRequestNormalizer.Normalize(
            Request(intent: Intent(
                ranking: new CopilotRankingIntent
                {
                    TopN = topN,
                    OrderBy = orderBy,
                    Direction = direction
                })));

        Assert.Null(result.SemanticIntent?.Ranking);
    }

    [Fact]
    public void DateCount_ZeroIsRetainedOnlyForLastNExpressions()
    {
        var lastN = CopilotPlannedReportRequestNormalizer.Normalize(
            Request(Intent(
                    date: new CopilotDateIntent
                    {
                        Kind = "relative",
                        RelativeExpression = "last_n_days",
                        Count = 0,
                        From = "",
                        To = "",
                        Grain = "none"
                    })));
        Assert.Equal(0, lastN.SemanticIntent!.Date.Count);

        var previousMonth = CopilotPlannedReportRequestNormalizer.Normalize(
            Request(Intent(
                    date: new CopilotDateIntent
                    {
                        Kind = "relative",
                        RelativeExpression = "previous_month",
                        Count = 0,
                        From = "",
                        To = "",
                        Grain = "none"
                    })));
        Assert.Null(previousMonth.SemanticIntent!.Date.Count);
    }

    private static CopilotPlannedReportRequest Request(
        CopilotSemanticIntent? intent = null,
        CopilotClarification? clarification = null,
        string outcome = "accepted",
        IReadOnlyList<CopilotUnresolvedConcept>? unresolvedConcepts = null) =>
        new()
    {
        Prompt = "2018 siparis sayisini goster",
        ConversationId = "conversation-1",
        Outcome = outcome,
        SemanticIntent = intent ?? Intent(),
        UnresolvedConcepts = unresolvedConcepts ?? [],
        Clarification = clarification ?? new CopilotClarification
        {
            Kind = ""
        }
    };

    private static CopilotSemanticIntent Intent(
        CopilotDateIntent? date = null,
        CopilotRankingIntent? ranking = null) => new()
    {
        Metric = "order_count",
        GroupBy = [],
        Filters = [],
        Date = date ?? new CopilotDateIntent
        {
            Kind = "unspecified",
            RelativeExpression = "",
            Count = 0,
            From = "",
            To = "",
            Grain = "none"
        },
        Ranking = ranking ?? new CopilotRankingIntent
        {
            TopN = 0,
            OrderBy = "",
            Direction = ""
        }
    };
}
