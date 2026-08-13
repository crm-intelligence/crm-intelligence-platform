# AI-Powered CRM Analytics Platform — 11 Günlük Sprint Planı (Rol Bazlı)

**Süre:** 2 hafta / 11 iş günü
**Sprint Hedefi (Sprint Goal):** Teams'ten gelen bir doğal dil talebinin; kimlik doğrulama → kontrollü SQL üretimi → read-only veri erişimi → Fabric analitik sonucu → Power BI raporu → Teams özeti akışını **uçtan uca çalışır** hale getirmek ve üç demo senaryosunu (satış analizi, müşteri segmentasyonu, kampanya performansı) gösterilebilir kalitede tamamlamak.

> **Düzeltme (üçüncü senaryo):** Yukarıdaki hedefte ve aşağıdaki BI satırlarında geçen
> "kampanya performansı", Olist veri setinde kampanya verisi ve `campaign_id` kolonu
> bulunmadığı için **kategori performansı** olarak değişti. Orijinal metin tarihsel kayıt
> olarak korundu; geçerli senaryo listesi satış analizi, müşteri segmentasyonu ve
> **kategori performansı**dır.

**Kapsam Kararı:** Faz 0-1 (keşif + cloud temel) bu sprintin ilk 2 gününde "hızlandırılmış" biçimde yapılır; Faz 2-5 sprint boyunca dikey dilim (vertical slice) yaklaşımıyla paralel yürütülür. Faz 6 (pilot/UAT) son 2 günde daraltılmış kapsamda ele alınır.

**Kritik Kural:** Gün 5 sonunda mock/örnek veriyle de olsa uçtan uca akış çalışmalıdır. Gerçek veri erişimi gecikirse ekip beklemez, mock veri sözleşmesiyle devam eder.

---

## Roller ve Kısaltmalar

| # | Rol | Kısaltma | Ana Sorumluluk Alanı |
|---|-----|----------|----------------------|
| 1 | **Veri Mühendisi** (Data Engineer) | **DE** | DWH/OLTP bağlantıları, read-only görünümler, veri sözlüğü, veri kalitesi |
| 2 | **Microsoft Fabric ve Business Intelligence Geliştiricisi** (Fabric / BI Developer) | **BI** | Fabric pipeline & analitik flow, semantic model, Power BI raporları, RLS |
| 3 | **Yapay Zekâ Mühendisi** (AI Engineer — NL2SQL & Query Builder) | **AI** | Talep ayrıştırma, Query Builder, guarded NL2SQL, SQL guardrail |
| 4 | **Backend Geliştirici** (Backend Developer — ASP.NET Core & Microsoft Teams) | **BE** | ASP.NET Core API, Teams entegrasyonu, bağlam ve durum yönetimi |
| 5 | **DevOps, Proje Yönetimi ve Kalite Güvence** (DevOps / PM / QA) | **OPS** | Azure ortamı, Entra ID, CI/CD, izleme, test planı, sprint yönetimi |

---

## Günlük Ritüel (her gün sabit)

- **12:00 – 12:15 Daily standup:** dün / bugün / engel (DevOps-PM yürütür)
- **16:30 – 17:00 Entegrasyon checkpoint:** o gün üretilen çıktının bir sonraki rolde çalıştığının doğrulanması
- Her gün sonunda kod/konfigürasyon commit edilir; "bilgisayarımda çalışıyor" kabul edilmez

---

## Sprint Takvimi Özeti

| Gün | Tema | Günün Kilometre Taşı |
|-----|------|----------------------|
| 1 | Kickoff & Temel | Azure kaynakları + repo + use case kapsamı net |
| 2 | Sözleşmeler | API, veri ve metric sözleşmeleri yazılı |
| 3 | İlk Dikey Dilim | Teams → API → mock sonuç çalışıyor |
| 4 | Güvenlik & Guardrail | Entra ID + SQL guardrail devrede |
| 5 | **E2E Milestone 1** | Satış analizi uçtan uca (gerçek/mock veri) |
| 6 | Segmentasyon & Kampanya | 2. ve 3. senaryonun veri/analitik tarafı |
| 7 | Raporlama Derinliği | Power BI 3 rapor sayfası + RLS |
| 8 | Bağlam, Async & Hata | Takip sorusu + durum modeli + audit |
| 9 | Entegrasyon Testi | 3 senaryo E2E + bug bash |
| 10 | UAT & Freeze | Kabul kriterleri + kod freeze + demo provası |
| 11 | **Demo & Kapanış** | Demo, kapanış raporu, retro, sonraki sprint backlog |

---

# HAFTA 1

## Gün 1 — Kickoff ve Temel Kurulum

| Rol | Bugün Ne Yapacak | Günün Çıktısı |
|-----|------------------|---------------|
| **DE** | DWH ve OLTP kaynaklarının envanterini çıkarır; satış senaryosu için gereken tablo/kolonları listeler; bağlantı bilgisi ve erişim talebini açar. | Kaynak envanter listesi + erişim talebi kaydı |
| **BI** | Fabric workspace ve Power BI workspace'i oluşturur; KPI listesini (net satış, satış adedi, büyüme, ortalama sepet) iş tanımlarıyla teyit eder. | Workspace'ler hazır + onaylı KPI listesi v1 |
| **AI** | Üç use case için canonical request şemasını (metric, dimension, filter, date range) taslaklar; metric/dimension catalog iskeletini açar. | `canonical_request.json` taslağı |
| **BE** | ASP.NET Core solution iskeletini kurar (API + Service + Domain katmanları), health endpoint'i ayağa kaldırır, repo ve branch yapısını oluşturur. | Çalışan `/health` endpoint'i, repo hazır |
| **OPS** | Sprint kickoff'u yapar, backlog ve DoD'yi netleştirir; Azure resource group, Entra ID app registration ve Key Vault'u oluşturur. | Sprint backlog + Azure temel kaynaklar |

**Gün sonu kontrolü:** Herkes Azure ortamına ve repoya erişebiliyor mu?

---

## Gün 2 — Sözleşmeler Günü (Contract First)

| Rol | Bugün Ne Yapacak | Günün Çıktısı |
|-----|------------------|---------------|
| **DE** | Read-only DB kullanıcısı/rolünü tanımlar; satış senaryosu için allow-list'e girecek görünümleri (`vw_sales`, `vw_customer`, `vw_campaign`) oluşturur. | Read-only erişim + görünüm v1 |
| **BI** | Fabric Lakehouse/Warehouse yapısını kurar; örnek satış verisini ingest eder (gerçek veri yoksa sentetik veri üretir). | Fabric'te sorgulanabilir satış tablosu |
| **AI** | Allow-list (tablo/kolon/JOIN yolu) ve metric catalog dosyalarını yazar; satış analizi için Query Builder şablon tasarımını yapar. | `allowlist.json` + `metric_catalog.json` |
| **BE** | API sözleşmesini yazar: `POST /api/report/request`, `GET /api/report/status/{requestId}`, `POST /api/report/{requestId}/revise`; DTO'lar, Conversation ID ve Previous Request ID modeli, geçici in-memory store. Kanonik endpoint listesi: § 9.3. | OpenAPI/Swagger dokümanı + DTO'lar |
| **OPS** | CI pipeline'ı (build + unit test) kurar; secret'ları Key Vault'a taşır; test planı taslağını ve test veri stratejisini yazar. | Yeşil CI build + test planı v1 |

**Gün sonu kontrolü:** API sözleşmesi ile AI'ın canonical request şeması birebir uyuşuyor mu?

---

## Gün 3 — İlk Dikey Dilim (Teams → API → Mock Sonuç)

| Rol | Bugün Ne Yapacak | Günün Çıktısı |
|-----|------------------|---------------|
| **DE** | Tarih boyutu ve temel veri kalite kontrollerini ekler; OLTP tarafında sıkı timeout ve satır limitli sınırlı görünümü hazırlar. | Date dimension + OLTP limitli görünüm |
| **BI** | Satış verisi için temizleme/dönüştürme pipeline'ı v1'i geliştirir; sonuç tablosu şemasını (request_id ile) tanımlar. | Çalışan satış pipeline'ı + sonuç tablosu |
| **AI** | Query Builder v1'i geliştirir: canonical request → parametreli SELECT (satış analizi senaryosu). | Deterministik SQL üreten Query Builder v1 |
| **BE** | Teams bot/app kaydını yapar; Teams'ten mesaj alıp API'ye taşıyan akışı ve durum modelini (Received/Processing/Completed/NeedsClarification/Rejected/Failed) kurar. | Teams'ten mesaj alıp mock cevap dönen akış |
| **OPS** | API'ye Entra ID token doğrulamasını ekler; Application Insights'ı bağlar; request ID korelasyonunu tanımlar. | Kimlik doğrulamalı API + telemetri |

**Gün sonu kontrolü:** Teams'e yazılan bir talep, API log'unda request ID ile görülebiliyor mu?

---

## Gün 4 — Güvenlik ve SQL Guardrail

| Rol | Bugün Ne Yapacak | Günün Çıktısı |
|-----|------------------|---------------|
| **DE** | Veri kapsamı (bölge/mağaza/şirket) kolonlarını netleştirir; kullanıcı → veri kapsamı eşleme tablosunu hazırlar. | Veri kapsamı mapping tablosu |
| **BI** | Semantic model v1'i kurar: satış ölçüleri, ilişkiler, tarih tablosu; KPI hesaplarını DAX ile tanımlar. | Power BI semantic model v1 |
| **AI** | SQL Guardrail'ı geliştirir: SELECT-only, allow-list kontrolü, parser/AST doğrulaması, zorunlu yetki filtresi, satır ve süre limiti. + negatif test seti. | Guardrail bileşeni + 10+ negatif test |
| **BE** | Entra ID kimliğinden kullanıcı rolü ve veri kapsamını çıkarır; bunu SQL servisine claim olarak taşır; hata yönetimi ve loglama akışını tamamlar. | Kimlik → veri kapsamı akışı çalışıyor |
| **OPS** | Log şemasını standartlaştırır; audit kaydı (kullanıcı, karar, kaynak, sorgu) altyapısını kurar; güvenlik test senaryolarını yazar. | Audit log + güvenlik test senaryoları |

**Gün sonu kontrolü:** `DELETE`, yetkisiz tablo ve kapsam dışı bölge talepleri reddediliyor mu?

---

## Gün 5 — 🎯 E2E Milestone 1: Satış Analizi Uçtan Uca

| Rol | Bugün Ne Yapacak | Günün Çıktısı |
|-----|------------------|---------------|
| **DE** | Sorgu performansını ölçer, gerekli index/partition düzenlemelerini yapar; sonuç setinin doğruluğunu manuel SQL ile karşılaştırır. | Doğrulanmış veri + performans notu |
| **BI** | "Satış Analizi" Power BI rapor sayfasını (KPI kartları, trend grafiği, bölge kırılımı) hazırlar ve deep link üretir. | Satış Analizi raporu v1 + link |
| **AI** | Query Builder + Guardrail hattını API'ye entegre eder; sonuç şeklini (tek değer / tarih+metric / kategori+metric) etiketler. | Entegre SQL üretim hattı |
| **BE** | Uçtan uca akışı bağlar: Teams talebi → SQL → veri → kısa KPI özeti + Power BI bağlantısı → Teams yanıtı. | **Çalışan uçtan uca satış senaryosu** |
| **OPS** | İlk E2E smoke testini koşar, bulguları issue'lara döker; hafta içi demo (internal review) yürütür. | Smoke test raporu + bug listesi |

**Hafta 1 çıkış kriteri:** Yetkili bir kullanıcı Teams'ten "Son çeyrek satışları bölgeye göre göster" diyor; özet + rapor linki geliyor.

---

# HAFTA 2

## Gün 6 — Segmentasyon ve Kampanya Senaryoları

| Rol | Bugün Ne Yapacak | Günün Çıktısı |
|-----|------------------|---------------|
| **DE** | Müşteri ve kampanya veri setlerini hazırlar; incremental refresh mantığını ve veri kalite kurallarını ekler. | Müşteri + kampanya veri setleri |
| **BI** | Fabric'te müşteri segmentasyonu (RFM benzeri) ve kampanya performansı flow'larını geliştirir. | 2 yeni analitik flow + sonuç tabloları |
| **AI** | Guarded NL2SQL PoC'yi geliştirir: yalnızca allow-list şema metadata'sıyla prompt, üretilen SQL guardrail'dan geçer; belirsiz talepte NeedsClarification üretir. | NL2SQL PoC + netleştirme/ret akışı |
| **BE** | Takip sorusu / rapor revizyonu akışını geliştirir (metrik değiştirme, kırılım değiştirme, filtre ekleme) — Previous Request ID üzerinden. | Çalışan takip sorusu akışı |
| **OPS** | CD pipeline'ı ile dev ortamına otomatik deployment kurar; negatif güvenlik test paketini otomatikleştirir. | Otomatik deployment + güvenlik test paketi |

**Gün sonu kontrolü:** "Satış adedi yerine net kârı göster" talebi önceki raporu doğru revize ediyor mu?

---

## Gün 7 — Raporlama Derinliği ve RLS

| Rol | Bugün Ne Yapacak | Günün Çıktısı |
|-----|------------------|---------------|
| **DE** | Fabric sonuç tablolarını request ID ile ilişkilendirir; rapor metadata'sını (kaynak, filtre, üretim zamanı) sonuç yapısına ekler. | Rapor metadata'lı sonuç tabloları |
| **BI** | Segmentasyon ve kampanya rapor sayfalarını tamamlar; RLS rollerini ve satır güvenliği kurallarını tanımlar. | 3 rapor sayfası + RLS rolleri |
| **AI** | Sonuç yapısı → görsel tipi eşlemesini uygular (tek değer → KPI kartı, tarih+metric → çizgi, kategori+metric → bar, çok boyut → matrix). | Görsel öneri mantığı |
| **BE** | Teams yanıt formatını iyileştirir: kısa KPI özeti, en önemli 3 bulgu, doğru rapor sayfasına derin bağlantı. | Zenginleştirilmiş Teams yanıtı |
| **OPS** | Farklı veri kapsamına sahip test kullanıcıları oluşturur; RLS ve yetki testlerini koşar; fonksiyonel test case'lerini tamamlar. | RLS test sonuçları + test case seti |

**Gün sonu kontrolü:** Bölge yöneticisi kendi bölgesi dışındaki veriyi hiçbir yolla göremiyor mu?

---

## Gün 8 — Bağlam, Asenkron İşler, Hata Yönetimi

| Rol | Bugün Ne Yapacak | Günün Çıktısı |
|-----|------------------|---------------|
| **DE** | Ağır sorguları optimize eder; OLTP sorgu limitlerini stres testiyle doğrular; veri kalite kontrol raporunu üretir. | Performans + veri kalite raporu |
| **BI** | Pipeline'ları parametrik ve yeniden çalıştırılabilir hale getirir; refresh takvimi ve hata bildirimi kurar. | Yeniden çalıştırılabilir pipeline'lar |
| **AI** | Guardrail ve prompt'u iyileştirir; belirsizlik tespitini güçlendirir; her SQL kararını (kabul/ret/gerekçe) audit'e yazar. | Açıklanabilir karar log'u |
| **BE** | Uzun süren analizler için asenkron iş akışını (Processing → bildirim), retry ve idempotency'yi ekler; Rejected/Failed kullanıcı mesajlarını netleştirir. | Asenkron durum akışı + hata mesajları |
| **OPS** | Monitoring dashboard'u ve alert kurallarını kurar; secret/erişim gözden geçirmesi yapar; UAT senaryolarını hazırlar. | Monitoring dashboard + UAT senaryoları |

**Gün sonu kontrolü:** Hata durumunda kullanıcı anlaşılır mesaj ve request ID görüyor mu?

---

## Gün 9 — Entegrasyon Testi ve Bug Bash

| Rol | Bugün Ne Yapacak | Günün Çıktısı |
|-----|------------------|---------------|
| **DE** | Test sırasında çıkan veri tutarsızlıklarını düzeltir; kaynak seçim kuralını (DWH öncelikli, OLTP sınırlı) doğrular. | Düzeltilmiş veri + kaynak seçim doğrulaması |
| **BI** | Rapor performansını ve görsel tutarlılığını iyileştirir; KPI değerlerini SQL sonuçlarıyla çapraz doğrular. | KPI mutabakat tablosu |
| **AI** | Negatif SQL test paketini genişletir; prompt injection ve kapsam kaçırma denemelerini test eder. | Genişletilmiş güvenlik test raporu |
| **BE** | Bug bash bulgularını önceliklendirip düzeltir; log ve durum akışındaki boşlukları kapatır. | Kritik/major bug'lar kapalı |
| **OPS** | Üç senaryonun tam E2E testini koşar, yanıt sürelerini ölçer; bug bash'i yönetir ve kalan riskleri raporlar. | E2E test raporu + performans ölçümü |

**Gün sonu kontrolü:** 3 senaryo da tekrar tekrar, aynı sonucu üreterek çalışıyor mu?

---

## Gün 10 — UAT, Dokümantasyon ve Kod Freeze

| Rol | Bugün Ne Yapacak | Günün Çıktısı |
|-----|------------------|---------------|
| **DE** | Veri sözlüğünü ve işletim (operations) notlarını tamamlar; erişim ve yenileme prosedürünü yazar. | Veri sözlüğü + işletim dokümanı |
| **BI** | Rapor paketini finalize eder; ileri faz için genişletilebilir semantic katman taslağını yazar. | Final rapor paketi + ileri faz taslağı |
| **AI** | Hibrit sorgu sisteminin doğruluk/güvenlik test kanıtlarını derler; desteklenen ve desteklenmeyen soru tiplerini dokümante eder. | Test kanıt dosyası + kapsam dokümanı |
| **BE** | API ve Teams entegrasyonunun teknik dokümanını yazar; kod freeze sonrası yalnızca kritik düzeltme yapar. | API dokümanı + freeze edilmiş sürüm |
| **OPS** | İş kullanıcısıyla UAT oturumunu yürütür; kabul kriterlerini işaretler; demo senaryosunu ve provasını yapar. | UAT tutanağı + demo script |

**Gün sonu kontrolü:** Kabul kriterleri tablosunda açık kalan madde var mı?

---

## Gün 11 — 🎯 Demo, Kapanış ve Retrospektif

| Rol | Bugün Ne Yapacak | Günün Çıktısı |
|-----|------------------|---------------|
| **DE** | Demo sırasında veri katmanını izler; veri ile ilgili soruları yanıtlar; kalan veri işlerini backlog'a taşır. | Veri backlog maddeleri |
| **BI** | Demo'da Fabric flow'larını ve Power BI raporlarını sunar; RLS farkını canlı gösterir. | Analitik & raporlama sunumu |
| **AI** | Demo'da kontrollü SQL üretimini gösterir: bir geçerli talep, bir belirsiz talep, bir reddedilen talep. | Guardrail canlı gösterimi |
| **BE** | Demo'yu Teams üzerinden uçtan uca yürütür (ilk talep + takip sorusu + rapor linki). | Uçtan uca canlı demo |
| **OPS** | Demo'yu organize eder; kapanış raporunu, kabul kriteri sonuçlarını ve retrospektifi yürütür; sonraki sprint backlog'unu oluşturur. | Kapanış raporu + retro + yeni backlog |

---

## Sprint Kabul Kriterleri (Definition of Done)

| Alan | Kriter |
|------|--------|
| Fonksiyonel | Üç demo use case'i Teams'ten uçtan uca çalışıyor |
| Bağlam | Takip sorusu önceki raporu doğru revize ediyor |
| Güvenlik | Yetkisiz tablo/kolon/veri kapsamına erişilemiyor; yazma sorguları reddediliyor |
| SQL | Guardrail negatif test paketi %100 geçiyor |
| Rapor | Power BI RLS doğru veri kapsamını gösteriyor |
| İzlenebilirlik | Her talep request ID ile uçtan uca izlenebiliyor |
| Performans | Standart demo senaryolarında kabul edilebilir yanıt süresi ölçülmüş |
| Doküman | Her rol kendi bileşeni için kısa teknik doküman + test kanıtı teslim etti |

---

## Sprint Riskleri ve Azaltma

| Risk | Etki | Azaltma |
|------|------|---------|
| Gerçek DWH/OLTP erişimi Gün 3'e kadar gelmezse | Tüm zincir kayar | Gün 2'de sentetik veri + veri sözleşmesi hazır; ekip mock ile devam eder |
| Entra ID / Power BI lisans onayı gecikmesi | RLS ve kimlik testleri yapılamaz | Gün 1'de talep açılır; geçici dev tenant ile paralel çalışılır |
| NL2SQL beklenen kalitede çıkmaz | Demo zayıflar | Demo Query Builder öncelikli; NL2SQL "PoC" olarak konumlandırılır |
| KPI tanımlarında iş birimi anlaşmazlığı | Yeniden geliştirme | Gün 1'de KPI listesi yazılı onaylanır, sprint içinde değiştirilmez |
| 11 gün için kapsam fazla | Yarım kalan işler | Zorunlu kapsam: satış senaryosu + guardrail + RLS. Diğerleri "stretch" |

---

## Kapsam Dışı (Bu Sprintte Yapılmayacak)

- Satış tahmini, churn ve kök neden analizi (ileri faz)
- Generic/dinamik rapor ekranları ve her soru için yeni PBIX
- Tam otonom aksiyon alan agent yapısı
- Yazma (INSERT/UPDATE/DELETE) işlemleri
- On-premises Query Agent kurulumu
- Production ortamına deployment (yalnızca dev/test ortamı hedeflenir)
