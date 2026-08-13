using crm_project.Models;

namespace crm_project.Services;

public class SecurityPolicyService
{
    private static readonly HashSet<string> AllowedTables =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "customers",
            "sales",
            "products"
        };

    private static readonly HashSet<string> AllowedRegions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "TR",
            "EU"
        };

    public SecurityDecision Evaluate(CanonicalRequest request)
    {
        if (request.Prompt.Contains("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            return SecurityDecision.Deny(
                "DELETE işlemi güvenlik politikası gereği yasaktır.");
        }

        if (!AllowedTables.Contains(request.TargetTable))
        {
            return SecurityDecision.Deny(
                $"'{request.TargetTable}' tablosuna erişim yetkisi yoktur.");
        }

        if (!AllowedRegions.Contains(request.Region))
        {
            return SecurityDecision.Deny(
                $"'{request.Region}' bölgesi izin verilen kapsam dışındadır.");
        }

        return SecurityDecision.Allow();
    }
}

public class SecurityDecision
{
    public bool IsAllowed { get; init; }

    public string Decision { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public static SecurityDecision Allow()
    {
        return new SecurityDecision
        {
            IsAllowed = true,
            Decision = "Allowed",
            Reason = "Talep güvenlik politikalarına uygundur."
        };
    }

    public static SecurityDecision Deny(string reason)
    {
        return new SecurityDecision
        {
            IsAllowed = false,
            Decision = "Denied",
            Reason = reason
        };
    }
}