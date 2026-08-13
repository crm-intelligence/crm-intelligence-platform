using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Guardrail;

/// <summary>
/// Tek bir guardrail kontrolu.
/// </summary>
/// <remarks>
/// Kontroller <see cref="GuardrailCheckName"/> sirasina gore calisir. Bir kontrol yalnizca
/// kendisinden ONCE gelen kontrollerin gectigini varsayabilir; ornegin AST'ye erisen her
/// kontrol <see cref="GuardrailCheckName.ParseToAst"/> sonrasinda yer almalidir.
/// </remarks>
public interface IGuardrailCheck
{
    GuardrailCheckName Name { get; }

    /// <summary>
    /// Kontrolu uygular. Basarisizlik icin istisna FIRLATMAZ, <see cref="CheckResult.Fail"/>
    /// dondurur. Istisna yalnizca beklenmeyen durumlar icindir ve pipeline tarafindan
    /// ret olarak ele alinir.
    /// </summary>
    CheckResult Execute(GuardrailContext context);
}
