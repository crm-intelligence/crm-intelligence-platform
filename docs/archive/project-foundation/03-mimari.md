# 3. Hedef Cloud-First Mimari

Veri kaynakları ve uygulama bileşenleri Azure üzerinde bulunur. Müşteri sunucusuna özel bir sorgu ajanı kurulmaz. Teams, API, kimlik doğrulama, veri erişimi, Fabric analitiği ve Power BI raporlaması merkezi biçimde yönetilir.

## 3.1 Uçtan Uca Akış

```
Microsoft Teams
      ↓  doğal dilde talep
ASP.NET Core API           → kimlik, bağlam, orkestrasyon
      ↓
Talep Analizi              → metric, boyut, filtre, tarih aralığı
      ↓
Query Builder / NL2SQL     → hibrit SQL üretimi
      ↓
SQL Guardrail              → doğrulama ve limitler
      ↓
Kaynak Seçimi              → DWH / Fabric  |  Azure OLTP
      ↓
Microsoft Fabric           → veri hazırlama ve analitik flow
      ↓
Power BI Semantic Model    → KPI, ilişki, RLS
      ↓
Teams Yanıtı               → kısa özet + rapor bağlantısı
```

**Ortak güvenlik katmanı:** Entra ID · rol ve veri kapsamı · read-only erişim · allow-list · RLS · audit · izleme

## 3.2 Bileşen Rolleri

| Bileşen | Temel Rol | Kullanıcıya Etkisi |
|---------|-----------|--------------------|
| Microsoft Teams | Kullanıcı talebini ve takip sorularını almak | Mevcut çalışma ortamından ayrılmama |
| ASP.NET Core API | Kimlik, bağlam, orkestrasyon, hata ve sonuç yönetimi | Tutarlı ve izlenebilir talep akışı |
| Query Builder / NL2SQL | Tanımlı ve ad hoc talepler için SQL üretmek | Standart raporlarda güvenilirlik, yeni sorularda esneklik |
| DWH / OLTP | Tarihsel analitik ve güncel operasyonel veriyi sağlamak | İhtiyaca uygun veri güncelliği |
| Microsoft Fabric | Veri hazırlama ve analitik flow'ları yürütmek | Segmentasyon, satış ve kampanya analizleri |
| Power BI | Semantic model, RLS ve görselleştirme | Detaylı, filtrelenebilir ve yetkili rapor deneyimi |
