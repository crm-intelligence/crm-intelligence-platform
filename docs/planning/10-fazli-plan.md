# 10. Fazlı Geliştirme Planı

Proje, her aşamada test edilebilir ve gösterilebilir somut bir çıktı üretecek şekilde fazlara ayrılmıştır. Faz süreleri ekip kapasitesi, veri erişimi, Azure hazırlığı ve kurum onaylarına göre netleşir.

| Faz | Ana Çalışmalar | Somut Çıktı | Çıkış Kriteri |
|-----|----------------|-------------|---------------|
| **Faz 0 — Keşif** | Veri kaynakları, KPI'lar, use case ve güvenlik gereksinimleri | Onaylı kapsam ve veri sözlüğü | Öncelikli 3 use case ve erişim yöntemi net |
| **Faz 1 — Cloud Temel** | Azure ortamı, Entra ID, ağ, Key Vault, izleme | Çalışan güvenli geliştirme ortamı | Kimlik ve bağlantı testleri başarılı |
| **Faz 2 — Çekirdek Ürün** | Teams, API, bağlam, request ve durum yönetimi | Uçtan uca mock rapor talebi | Teams'ten talep alınıp sonuç dönebiliyor |
| **Faz 3 — SQL ve Kaynak** | Query Builder, NL2SQL PoC, guardrail, DWH/OLTP seçimi | Kontrollü SQL üretim hattı | Tanımlı güvenlik testleri geçiliyor |
| **Faz 4 — Fabric Analitiği** | Segmentasyon, satış ve kampanya flow'ları | Demo veri ürünleri ve sonuç tabloları | Üç analitik senaryo yeniden çalıştırılabilir |
| **Faz 5 — Power BI** | Semantic model, RLS, raporlar, Teams linki | Entegre demo rapor deneyimi | Yetkili kullanıcı doğru raporu görüyor |
| **Faz 6 — Pilot** | UAT, performans, güvenlik, dokümantasyon | Pilot sürüm ve kapanış raporu | Kabul kriterleri karşılanıyor |

## 10.1 Rol Bazlı Faz Sorumlulukları

| Rol | Faz 0-1 | Faz 2-3 | Faz 4-5 | Faz 6 |
|-----|---------|---------|---------|-------|
| **Backend ve Teams** | API sözleşmeleri, Teams yaklaşımı, Entra ID entegrasyon gereksinimleri | ASP.NET Core API, conversation context, request durumu, rapor revizyonu, hata ve log akışı | Fabric ve Power BI sonuçlarını Teams'e bağlama, asenkron durum akışı | Uçtan uca UAT, hata senaryoları, operasyon dokümantasyonu |
| **AI, NL2SQL ve Query Builder** | Use case, metric, dimension, metadata ve izinli şema kapsamı | Query Builder, guarded NL2SQL, canonical request, SQL guardrail | Analitik flow ve semantic model ile sorgu sonuç sözleşmeleri, negatif SQL testleri | Doğruluk, güvenlik ve açıklanabilirlik testleriyle teslim |
| **Data Engineering ve Fabric** | DWH/OLTP kaynak analizi, veri kalitesi, erişim, veri sözlüğü | Read-only bağlantılar, kaynak seçim kuralları, veri sözleşmeleri | Segmentasyon, satış ve kampanya flow'ları; temizleme, dönüşüm, sonuç tabloları | Yeniden çalıştırılabilir pipeline'lar, veri kalite kontrolleri, işletim dokümanı |
| **Power BI ve Semantic Model** | KPI, metric, dimension, ilişki ve RLS gereksinimleri | Sabit demo semantic modeli tasarımı, rapor veri sözleşmeleri | Satış, segmentasyon ve kampanya raporları; RLS ve Teams bağlantısı | Yetki senaryoları test edilmiş rapor paketi, ileri semantic katman taslağı |
| **Cloud, Güvenlik ve DevOps** | Azure ortamı, Entra ID, Key Vault, ağ, erişim, izleme | CI/CD, yapılandırma, read-only erişim, secret yönetimi, monitoring, audit | Fabric ve Power BI ortam güvenliği, deployment, uçtan uca telemetri | Doğrulanmış güvenlik kontrolleri, izlenebilir ve tekrarlanabilir pilot ortam |

> **Rol bazlı teslim ilkesi:** Her rol; çalışan kod veya yapılandırma, test kanıtı, kısa teknik dokümantasyon ve entegrasyon desteği üretir. Faz kapanışları bireysel görev tamamlanmasına değil, uçtan uca entegrasyonun doğrulanmasına dayanır.
