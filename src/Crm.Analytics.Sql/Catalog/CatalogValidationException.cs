namespace Crm.Analytics.Sql.Catalog;

/// <summary>
/// Allow-list veya Metric Catalog gecersiz oldugunda atilir.
/// </summary>
/// <remarks>
/// Bu hata <b>kullaniciya gosterilmez</b>; uygulamanin baslamasini engellemesi beklenir.
/// Gecersiz bir allow-list ile calismaya devam etmek, guardrail'in dayandigi tek gercek
/// kaynagin bozuk olmasi demektir — fail-closed davranis burada "hic baslamamak"tir.
/// </remarks>
public sealed class CatalogValidationException(string message) : Exception(message);
