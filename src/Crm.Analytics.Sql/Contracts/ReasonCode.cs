namespace Crm.Analytics.Sql.Contracts;

/// <summary>
/// Ret ve netlestirme gerekce kodlari. Backend bu kodu kullaniciya gosterilecek mesaja
/// cevirir; kod ile mesaj ayri tutulur cunku mesaj metni degisse dahi kodun anlami sabit
/// kalmali ve audit kayitlari karsilastirilabilir olmali.
/// </summary>
public enum ReasonCode
{
    /// <summary>Ret yok.</summary>
    None = 0,

    /// <summary>SELECT disi ifade tespit edildi (DML/DDL/EXEC).</summary>
    GR001,

    /// <summary>Coklu statement veya ';' zinciri.</summary>
    GR002,

    /// <summary>Allow-list disi tablo veya gorunum.</summary>
    GR003,

    /// <summary>Allow-list disi kolon.</summary>
    GR004,

    /// <summary>Yasakli kisisel veri kolonu.</summary>
    GR005,

    /// <summary>Izinsiz JOIN yolu veya JOIN sayisi asimi.</summary>
    GR006,

    /// <summary>Veri kapsami filtresi uygulanamadi (kullanicinin kapsami belirlenemedi).</summary>
    GR007,

    /// <summary>Kullanicinin veri kapsami disi talep.</summary>
    GR008,

    /// <summary>Satir veya sure limiti asimi.</summary>
    GR009,

    /// <summary>Parametreli olmayan sorgu.</summary>
    GR010,

    /// <summary>
    /// Desteklenmeyen SQL yapisi (OPENROWSET/OPENJSON/APPLY/inline TVF gibi beyaz liste disi dugum).
    /// Dokumandaki 12 kontrole EK olarak onerilmistir.
    /// </summary>
    GR011,

    /// <summary>
    /// Kimlik seviyesinde detay veya minimum hucre buyuklugu ihlali. Allow-list'i ve PII
    /// kontrolunu gecen ama tek bir kisiyi izole eden sorgular icin. EK kontrol.
    /// </summary>
    GR012,

    /// <summary>
    /// Tarih araligi butcesi asimi. TOP donen satiri sinirlar, taranan satiri sinirlamaz;
    /// aralik ayrica butcelenir. EK kontrol.
    /// </summary>
    GR013,

    /// <summary>
    /// Uretilen SQL yeniden dogrulamayi gecemedi. Mutasyon sonrasi (scope enjeksiyonu,
    /// parametreleme, limit) yeniden parse ve yeniden kontrol basarisiz oldu. EK kontrol.
    /// Bu kodun uretimde gorulmesi guardrail'in kendi hatasidir; alarm konusu.
    /// </summary>
    GR014,

    /// <summary>
    /// Girdi boyut siniri asildi. Parse maliyetine girmeden once uygulanan ilk bariyer.
    /// Dokumandaki 12 kontrole EK olarak onerilmistir.
    /// </summary>
    GR015,

    /// <summary>Metric veya boyut cozumlenemedi.</summary>
    CL001,

    /// <summary>Tarih araligi belirsiz.</summary>
    CL002
}

/// <summary>
/// Gerekce kodlarinin kullaniciya gosterilecek karsiliklari. Mesajlar bilincli olarak
/// <b>ic detay sizdirmaz</b>: hangi tablonun var oldugu, hangi kolonun yasakli oldugu veya
/// SQL'in neresinde hata bulundugu kullaniciya soylenmez. Aksi halde ret mesajlari sema
/// kesfi icin bir oracle'a donusur.
/// </summary>
public static class ReasonCodeMessages
{
    private static readonly Dictionary<ReasonCode, string> Messages = new()
    {
        [ReasonCode.GR001] = "Bu talep veri değiştirme içerdiği için çalıştırılamaz.",
        [ReasonCode.GR002] = "Talep güvenlik kuralları nedeniyle çalıştırılamadı.",
        [ReasonCode.GR003] = "Bu veri alanı rapor kapsamında tanımlı değil.",
        [ReasonCode.GR004] = "İstenen alan rapor kapsamında tanımlı değil.",
        [ReasonCode.GR005] = "Kişisel veri alanları raporlanamaz.",
        [ReasonCode.GR006] = "Bu iki veri alanı birlikte raporlanamıyor.",
        [ReasonCode.GR007] = "Yetki kapsamınız belirlenemedi.",
        [ReasonCode.GR008] = "Yalnızca yetkili olduğunuz bölgeleri görebilirsiniz.",
        [ReasonCode.GR009] = "Talep çok geniş; lütfen tarih aralığını daraltın.",
        [ReasonCode.GR010] = "Talep işlenemedi.",
        [ReasonCode.GR011] = "Bu talep desteklenmeyen bir sorgu yapısı içeriyor.",
        [ReasonCode.GR012] = "Talep kişi veya kayıt seviyesinde detay istediği için çalıştırılamadı.",
        [ReasonCode.GR013] = "Talep edilen tarih aralığı çok geniş; lütfen daraltın.",
        [ReasonCode.GR014] = "Talep işlenemedi.",
        [ReasonCode.GR015] = "Talep çok uzun; lütfen daha kısa bir istek gönderin.",
        [ReasonCode.CL001] = "Hangi metriği görmek istediğinizi belirtir misiniz?",
        [ReasonCode.CL002] = "Hangi dönemi karşılaştırmak istiyorsunuz?"
    };

    /// <summary>
    /// Koda karsilik gelen kullanici mesaji. Taninmayan kod icin genel mesaj doner —
    /// eksik eslesme kullaniciya ic detay sizdirmamali.
    /// </summary>
    public static string For(ReasonCode code) =>
        Messages.TryGetValue(code, out var message) ? message : "Talep işlenemedi.";

    /// <summary>Mesaj tanimli olan kodlar. Test, her ret kodunun mesajini zorunlu kilar.</summary>
    public static IReadOnlyCollection<ReasonCode> DefinedCodes => Messages.Keys;
}
