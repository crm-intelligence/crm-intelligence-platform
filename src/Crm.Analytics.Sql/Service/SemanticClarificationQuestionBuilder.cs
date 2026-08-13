using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Service;

/// <summary>Creates a question from missing semantic fields, never from sentence templates.</summary>
internal static class SemanticClarificationQuestionBuilder
{
    public static string Build(
        ReasonCode reasonCode,
        CanonicalRequest? request,
        IReadOnlyList<string> unresolvedTerms)
    {
        if (reasonCode == ReasonCode.CL002
            || request is not null
                && request.DateRange.Kind == DateRangeKind.NotApplicable
                && request.Metrics.Count > 0)
        {
            return "Hangi tarih araligini kullanmaliyim?";
        }

        if (request?.Source is null && request is not null)
        {
            return "Analiz DWH rapor verisinden mi, operasyonel siparis verisinden mi yapilmali?";
        }

        if (request is not null && request.Metrics.Count == 0
            && request.Dimensions.Count > 0)
        {
            return "Bu kirilim icin hangi metrigi olcmeliyim?";
        }

        if (request is not null && request.Metrics.Count > 0
            && request.Dimensions.Count == 0
            && request.Intent is RequestIntent.Breakdown or RequestIntent.Compare)
        {
            return "Sonucu hangi dimension ile gruplamaliyim?";
        }

        if (unresolvedTerms.Count > 0)
        {
            return "Istenen olcum veya kirilim semantic catalog'da bulunamadi; desteklenen kavramlardan hangisini kastettiniz?";
        }

        return "Hangi metrigi veya dimension'i kullanmaliyim?";
    }
}
