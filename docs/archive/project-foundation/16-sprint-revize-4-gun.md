# AI-Powered CRM Analytics Platform — Revize Sprint Planı (4 İş Günü)

> **Not:** Bu doküman `11-sprint-11-gun.md`'nin Gün 3 ve sonrasının yerini alır; eski doküman referans olarak korunur.
>
> **Kapsam güncellemesi:** Doküman artık sprintin **tamamını (Gün 1–6)** kapsıyor. Gün 1 ve Gün 2 yalnızca "tamamlandı" notuyla geçilmiyor; sprint panosuna işlendikleri hâliyle **geçmiş kayıt** olarak aşağıda gerçek görev tablolarıyla korunuyor. Böylece hangi işin fiilen kapandığı ve hangisinin Gün 3'e taşındığı tek dokümandan izlenebiliyor.

**Süre:** 4 iş günü + 1 tampon gün (Cuma 31 Temmuz → Çarşamba 5 Ağustos 2026)
**Sprint bitişi:** Salı 4 Ağustos 2026 — demo günü
**Takım:** 5 kişi / 4 track

---

## Sprint Hedefi (Sprint Goal)

Teams'ten gelen bir doğal dil talebinin; **kimlik doğrulama → kontrollü SQL üretimi → read-only veri erişimi → Fabric analitik sonucu → Power BI raporu → Teams özeti** akışını uçtan uca çalışır hale getirmek ve üç demo senaryosunu gösterilebilir kalitede tamamlamak:

1. **Satış analizi**
2. **Müşteri segmentasyonu**
3. **Kategori performansı**

Üçüncü senaryo eski planda "kampanya performansı" olarak geçiyordu; Olist veri setinde kampanya verisi bulunmadığı için **kategori performansı** olarak düzeltildi (bkz. "Bilinen Tutarsızlıklar").

---

## Neden Revize Edildi

1. **Takvim daraldı.** Sprint Salı 4 Ağustos'ta bitmek zorunda. Elimizde 11 iş günü değil, **4 iş günü** var (Cuma 31 Tem, Pazartesi 3 Ağu, Salı 4 Ağu + Çarşamba 5 Ağu tampon). Cumartesi ve Pazar çalışma günü değil.
2. **Blokaj tek bir yerde toplandı.** Yapay Zekâ track'i 11 günlük işini **%100 tamamladı** — guardrail, Query Builder, NL2SQL, katalog, audit ve üç teknik doküman teslim edildi. Buna karşın sprintin tek bloker'ı Veri & BI tarafında: `vw_sales`, `vw_customer_rfm` ve `vw_payment` görünümleri henüz yok. Görünümler olmadan üretilen SQL gerçek veri üzerinde koşamıyor, semantic model kurulamıyor, uçtan uca akış bağlanamıyor.
3. **Rol ayrımı gecikme üretiyordu.** Veri Mühendisi ile BI Geliştiricisi ayrı roller olduğu için *görünüm → Fabric ingest → semantic model → rapor* zincirinde her devir teslim noktası bekleme yaratıyordu. 4 günlük bir sprintte bu devir teslimlerin maliyeti kabul edilemez.
4. **Gün 1–2 "tamamlandı" sayılıp geçilmişti.** İlk iki gün tek satırlık bir özetle kapatılmıştı; işler sprint panosuna işlenirken **Gün 2'nin iki maddesinin fiilen kapanmadığı** ortaya çıktı (`G2-DB-01` görünüm sözleşmesi yazıldı ama görünümler oluşturulmadı; `G2-OPS-01` CI dosyası açıldı ama gerçek build/test koşmuyor). Gün 3'ün kritik yolunun görünümlerle başlamasının sebebi tam olarak bu. Bu yüzden Gün 1–2 artık gerçek görev tablolarıyla dokümana alındı ve iki madde açık olarak işaretlendi.

---

## Track'ler ve Sahiplik

| Track | Kısaltma | Kim | Ana Sorumluluk Alanı | Durum |
|-------|----------|-----|----------------------|-------|
| **VERİ & BI** | `DATA_BI` | **Sümeyye + Sıla** (ortak sahiplik) | Read-only görünümler, Fabric Lakehouse ingest, semantic model, DAX KPI'lar, Power BI raporları, RLS | 🔴 Kritik yol |
| **YAPAY ZEKÂ** | `AI` | **Mehmet Efe** (ekip lideri) | Canonical request, allow-list & metric catalog, Query Builder, SQL guardrail, NL2SQL, karar audit'i | ✅ 11 günlük iş tamamlandı |
| **BACKEND** | `BE` | **Ramazan** | ASP.NET Core API, durum modeli, Teams entegrasyonu, bağlam ve revizyon akışı | Devam ediyor |
| **DEVOPS / PM / QA** | `OPS` | **Lokman** | CI/CD, audit log, request ID korelasyonu, RLS ve yetki testleri, test planı, sprint yönetimi | Devam ediyor |

**Veri Mühendisi ve BI rollerinin birleştirilmesi neden gerekliydi:** Görünümlerin yazılması (veri mühendisliği) ile o görünümlerin üzerine semantic model ve rapor kurulması (BI) tek bir kesintisiz zincir. Ayrı roller olarak yürütüldüğünde her adım "diğerinin çıktısını bekleme" durumuna düşüyordu: görünüm kolon adı değiştiğinde semantic model kırılıyor, DAX ölçüsü bir kırılım istediğinde görünüme geri dönülüyordu. Dört günde bu gidiş-gelişlere yer yok. Bu yüzden iki rol **VERİ & BI** adı altında tek track'e indirildi ve Sümeyye ile Sıla'ya ortak sahiplik verildi — ikisi de zincirin tamamına dokunabiliyor, aralarındaki iş bölümünü gün içinde kendileri yapıyor, kimse kimseyi devir teslim için beklemiyor.

---

## Sprint Takvimi Özeti

| Gün | Tarih | Tema | Kilometre Taşı |
|-----|-------|------|----------------|
| 1 | Pazartesi 27 Temmuz | Kickoff ve temel kurulum | ⚠️ Yalnızca AI çıktısı doğrulandı; 4 madde açık |
| 2 | Salı 28 Temmuz | Sözleşmeler günü — contract first | ⚠️ Kısmen (1 tamam · 1 bloke · 1 devam · 2 açık) |
| **3** | Cuma 31 Temmuz | Blokajı kaldır — görünümler canlı | Üç görünüm sorgulanabilir, katalog doğrulaması yeşil |
| **4** | Pazartesi 3 Ağustos | Uçtan uca akış + güvenlik | Bir talep Teams'ten uçtan uca çalışıyor |
| **5** | Salı 4 Ağustos | 🎯 Demo, kabul ve kapanış | **Sprint bitişi — üç senaryo canlı** |
| 6 | Çarşamba 5 Ağustos | Tampon — taşan işler ve doküman | Kalan bulgular kapalı, dokümanlar teslim |

Cumartesi 1 Ağustos ve Pazar 2 Ağustos çalışma günü değildir. Gün 6 bir **tampon**tur: Salı hedefi tuttuysa bu gün büyük ölçüde boş kalır.

Sprint panosundaki toplam iş kalemi **55 görev**tir: Gün 1 → 5, Gün 2 → 5, Gün 3 → 22, Gün 4 → 13, Gün 5 → 6, Gün 6 → 4.

---

## Gün 1 — Pazartesi 27 Temmuz · Kickoff (kısmen doğrulandı)

| Track | Kişi | İş | Çıktı | Durum |
|-------|------|------------|-------|-------|
| **VERİ & BI** · `G1-DB-01` | Sümeyye | **DWH ve OLTP kaynak envanterini çıkar.** Satış senaryosu için gereken tablo ve kolonları listele; bağlantı bilgisi ve erişim talebini aç. | Kaynak envanteri + erişim talebi kaydı | ⬜ Yapılacak |
| **VERİ & BI** · `G1-DB-02` | Sıla | **Fabric ve Power BI çalışma alanlarını kur, KPI listesini onayla.** Net satış, satış adedi, büyüme ve ortalama sepet tanımlarını iş birimiyle teyit et. | Çalışma alanları + onaylı KPI listesi v1 | ⬜ Yapılacak |
| **YAPAY ZEKÂ** · `G1-AI-01` | Mehmet Efe | **Canonical request şeması ve sözleşme tipleri.** `canonical_request.schema.json`, `CanonicalRequest`, `DateRangeSpec`, `RequestFilter`, `UserDataScope`, `ReasonCode`. | Sözleşme tipleri + JSON şeması | ✅ Tamamlandı |
| **BACKEND** · `G1-BE-01` | Ramazan | **ASP.NET Core solution iskeleti ve repo yapısı.** API + Service + Domain katmanları, health endpoint, branch yapısı. | Çalışan `/health` endpoint'i + repo | ⬜ Yapılacak |
| **DEVOPS/PM/QA** · `G1-OPS-01` | Lokman | **Sprint kickoff, DoD ve Azure temel kaynakları.** Backlog ve DoD netleştirildi; Azure resource group, Entra ID app registration ve Key Vault oluşturuldu. | Sprint backlog + Azure temel kaynakları | ⬜ Yapılacak |

**Gün sonu durumu:** Beş maddeden yalnızca `G1-AI-01`'in çıktısı repoda doğrulanabiliyor (sözleşme tipleri ve JSON şeması `Crm.Analytics.Sql/Contracts/` altında duruyor). Diğer dördünün çıktıları Azure, Fabric, Power BI ve ASP.NET tarafında yaşıyor ve depoda iz bırakmıyor; bu yüzden panoda **açık** bırakıldılar. Yapıldıysa sahibi panodan işaretler — bkz. "Gün 1–2 durumları neden ekip beyanına bırakıldı".

---

## Gün 2 — Salı 28 Temmuz · Sözleşmeler (kısmen tamamlandı)

| Track | Kişi | İş | Çıktı | Durum |
|-------|------|------------|-------|-------|
| **VERİ & BI** · `G2-DB-01` | Sümeyye | **Read-only erişim ve allow-list görünüm sözleşmesi.** Read-only rol tanımlandı ve görünüm sözleşmesi (`olist_views.contract.sql`) yazıldı; ancak görünümlerin kendisi fiilen oluşturulmadı — bu iş Gün 3'e (`G3-DB-01..03`) bloker olarak taşındı. | Read-only erişim + görünüm sözleşmesi | 🔴 Bloke |
| **VERİ & BI** · `G2-DB-02` | Sıla | **Fabric Lakehouse kurulumu ve örnek satış verisi.** Lakehouse/Warehouse yapısı kuruldu; Olist satış verisi yüklendi. | Sorgulanabilir satış tablosu | ⬜ Yapılacak |
| **YAPAY ZEKÂ** · `G2-AI-01` | Mehmet Efe | **Allow-list ve metric catalog.** `allowlist.olist.json`, `metric_catalog.olist.json`, `AllowListLoader`, `MetricCatalogLoader`, `CatalogValidator`. | Katalog dosyaları + yükleyiciler | ✅ Tamamlandı |
| **BACKEND** · `G2-BE-01` | Ramazan | **API sözleşmesi ve DTO'lar.** `POST /api/report/request`, `GET /api/report/status/{id}`, `POST /api/report/{id}/revise`; Conversation ID ve Previous Request ID modeli. | Swagger dokümanı + DTO'lar | ⬜ Yapılacak |
| **DEVOPS/PM/QA** · `G2-OPS-01` | Lokman | **CI pipeline iskeleti ve test planı.** Test planı ve test veri stratejisi yazıldı, secret'lar Key Vault'a taşındı. CI dosyası açıldı ancak yalnızca placeholder echo çalıştırıyor — gerçek build/test Gün 3'te (`G3-OPS-01`) tamamlanacak. | CI iskeleti + test planı v1 | 🟡 Devam ediyor |

### Gün 2'nin kapanmayan iki maddesi — Gün 3 blokajının kaynağı

Gün 2 bir bütün olarak "tamamlandı" sayılamaz. İki madde açık kaldı ve **Gün 3'ün kritik yolunu doğuran sebep tam olarak bunlar**:

- **`G2-DB-01` — 🔴 Bloke.** Read-only rol ve görünüm sözleşmesi (`olist_views.contract.sql`) yazıldı, ama `vw_sales` / `vw_customer_rfm` / `vw_payment` görünümlerinin **kendisi fiilen oluşturulmadı**. Sözleşme bir niyet beyanıdır; sorgulanabilir bir nesne değildir. Bu yüzden Gün 3'te `G3-DB-01..03` bloker olarak duruyor ve guardrail'ın ürettiği SQL hâlâ gerçek veri üzerinde koşamıyor.
- **`G2-OPS-01` — 🟡 Devam ediyor.** Test planı yazıldı ve secret'lar Key Vault'a taşındı, CI dosyası da açıldı; ancak pipeline yalnızca `echo "CI başarıyla çalıştı!"` çalıştırıyor — hiçbir şey doğrulamıyor. Gerçek `dotnet build` + `dotnet test` adımı Gün 3'e (`G3-OPS-01`) kaldı.

**Bu iki madde neden "tamamlandı" sayılmadı:** Aksi hâlde aynı iş hem Gün 2'de kapanmış hem Gün 3'te bloker olarak görünürdü — doküman kendi içinde tutarsız olurdu ve sprintin tek gerçek blokajı kayıtta kaybolurdu. Kısmen yapılmış iş, yapılmış iş değildir; açık kalan kısım sahibinin üzerinde ve taşındığı günün task kodunda izlenir.

---

## Gün 1–2 durumları neden ekip beyanına bırakıldı

Panoda Gün 1 ve Gün 2'nin yalnızca **iki** maddesi "Tamamlandı" işaretli: `G1-AI-01` ve `G2-AI-01`. Sebep, bir değer yargısı değil **doğrulanabilirlik**:

| Doğrulanabilen | Doğrulanamayan |
|----------------|----------------|
| Yapay Zekâ çıktıları depoda duruyor — `Contracts/`, `Catalog/`, `Guardrail/` klasörleri ve ~400 test somut kanıt. | Azure resource group, Entra ID app registration, Key Vault, Fabric Lakehouse, Power BI çalışma alanı, ASP.NET solution ve Swagger sözleşmesi **depo dışında** yaşıyor. |

Bu maddeler yapılmış olabilir; ancak yapıldığını depodan teyit etmenin yolu yok. Panoya "tamamlandı" olarak işlemek, doğrulanmamış bir durumu doğrulanmış gibi kaydetmek olurdu — sprint panosunun tek işi kimin ne yaptığını dürüstçe göstermek olduğu için bu kabul edilemez.

**Nasıl kapanacak:** İşi yapan kişi panodan kendi görevini işaretler. Böylece kayıt "ekip beyanı" olarak değil, **sahibinin imzasıyla** ve zaman damgasıyla düşer; aktivite kaydında kimin ne zaman kapattığı görünür. Gün 1–2 maddeleri ilerleme yüzdesini düşürüyor gibi görünse de bu bilinçli: gerçek durumu olduğundan iyi göstermek, dört günlük bir sprintte en pahalı hatadır.

---

## Gün 3 — Cuma 31 Temmuz · Blokajı Kaldır

> **Gün 3 ve sonrası artık kişi bazında atanmış.** Aşağıdaki tablolar sorumluluğu track seviyesinde tarif eder; panodaki (`sprint-board`) fiili sahiplik şöyledir: **Backend** işleri Ramazan'da, **DevOps/PM/QA** işleri Lokman'da, **Yapay Zekâ** işleri Mehmet Efe'de. Ortak sahiplikteki **VERİ & BI** track'i ikiye bölündü — SQL ve veri katmanı (`G3-DB-01…05`, `G3-DB-07`, `G4-DB-04`, `G6-DB-01`) **Sümeyye'de**; Fabric ve Power BI işleri (`G3-DB-06`, `G4-DB-01…03`, `G5-DB-01`) **Sıla'da**. Bu bölünme, "aynı görünüm iki kez yazılır veya hiç yazılmaz" riskini (bkz. Riskler) kapatır. Devralınabilir işler bilinçli olarak atanmamış durur.

| Track | Bugün Ne Yapacak | Günün Çıktısı |
|-------|------------------|---------------|
| **VERİ & BI** · `G3-DB-01` | 🔴 **`vw_sales` görünümünü oluştur.** `Crm.Analytics.Sql/Catalog/olist_views.contract.sql` sözleşmesine birebir uyan görünümü yaz. Kolon adları ve tipleri sözleşmeden sapmamalı; allow-list bu görünümü referans alıyor. | Sorgulanabilir `vw_sales` görünümü |
| **VERİ & BI** · `G3-DB-02` | 🔴 **`vw_customer_rfm` görünümünü oluştur.** RFM segmentasyonu için gereken görünüm. Not: Olist veri setinde Recency için referans tarih yok — sözleşmede belirtilen sabit referans tarih yaklaşımı kullanılacak. | Sorgulanabilir `vw_customer_rfm` görünümü |
| **VERİ & BI** · `G3-DB-03` | 🔴 **`vw_payment` görünümünü oluştur.** Ödeme kırılımı görünümü; kategori performansı senaryosunun ciro tarafını besler. | Sorgulanabilir `vw_payment` görünümü |
| **VERİ & BI** · `G3-DB-04` | **CatalogValidator uyum testini yeşile çevir.** Üç görünüm oluştuktan sonra `Crm.Analytics.Sql` katalog doğrulamasını koştur; allow-list ile fiili şema arasındaki her sapmayı kapat. | Geçen katalog doğrulaması |
| **VERİ & BI** · `G3-DB-05` | **Veri kapsamı (scope) kolonunu netleştir.** `customer_state` → bölge eşlemesini tanımla ve kullanıcı → veri kapsamı tablosunu hazırla. Guardrail zorunlu kapsam filtresini bu kolona enjekte ediyor. | Veri kapsamı eşleme tablosu |
| **VERİ & BI** · `G3-DB-06` | **Fabric Lakehouse ingest kararı ve uygulaması.** Görünümleri Fabric Lakehouse/Warehouse üzerine taşı. Fabric erişimi gecikirse yerel `crm_dev.db` ile devam kararını yaz ve ekibe duyur — bekleme yok. | Fabric tablosu veya yazılı devam kararı |
| **VERİ & BI** · `G3-DB-07` | **`Crm.Analytics.Sql.DevData` kırık build'ini onar.** Proje `.csproj` içinde `sqlite_views.sql` dosyasını EmbeddedResource olarak arıyor ama dosya repoda yok; bu yüzden çözüm derlenmiyor. Görünüm SQL'leri yazılırken bu dosya da üretilmeli. | Derlenen çözüm |
| **YAPAY ZEKÂ** · `G3-AI-01…06` | ✅ **11 günlük AI işi teslim edildi.** Canonical request şeması, allow-list + metric catalog, deterministik Query Builder, 16 kontrollü SQL guardrail, guarded NL2SQL + ambiguity gate + karar audit'i, üç teknik doküman. Ayrıntı: "Yapay Zekâ Track'i — Teslim Edilenler". | Tamamlanmış AI bileşenleri |
| **YAPAY ZEKÂ** · `G3-AI-07` | **Görünümler gelince gerçek DB smoke testi.** `SqlProductionFactory.CreateForOlist` ile üretilen SQL'i gerçek görünümler üzerinde koştur; şu ana kadar yalnızca sözleşme seviyesinde doğrulandı. `G3-DB-01..03` bitmeden başlayamaz. | Gerçek veri üzerinde geçen smoke testi |
| **BACKEND** · `G3-BE-01` | **ASP.NET Core API iskeleti ve `/health`.** API + Service + Domain katmanları, çalışan health endpoint'i, branch yapısı. | Ayakta `/health` endpoint'i |
| **BACKEND** · `G3-BE-02` | **Rapor talebi endpoint'i ve durum modeli.** `POST /api/report/request`, `GET /api/report/status/{id}`, `POST /api/report/{id}/revise`. Durum modeli: Received, Processing, Completed, NeedsClarification, Rejected, Failed. | Swagger dokümanı + DTO'lar |
| **BACKEND** · `G3-BE-03` | **`Crm.Analytics.Sql` DI entegrasyonu.** `ENTEGRASYON.md`'yi izleyerek `SqlProductionFactory.CreateForOlist`'i singleton olarak kaydet; `ISqlProductionService.Produce()` tek giriş noktası. | SQL üretim katmanı API içinden çağrılabilir |
| **BACKEND** · `G3-BE-04` | **Swagger üzerinden uçtan uca mock akış.** Teams bağlanmadan önce talep → SQL üretimi → mock sonuç zincirini Swagger'dan doğrula. | Mock cevap dönen çalışan akış |
| **DEVOPS/PM/QA** · `G3-OPS-01` | **CI pipeline'ını gerçek build ve teste çevir.** `.github/workflows/ci.yml` şu an yalnızca `echo "CI başarıyla çalıştı!"` çalıştırıyor — hiçbir şey doğrulamıyor. `dotnet build` + `dotnet test` ile değiştir. | Gerçekten doğrulayan yeşil CI |
| **DEVOPS/PM/QA** · `G3-OPS-02` | **Audit log şeması ve request ID korelasyonu.** Kullanıcı, karar, kaynak, sorgu alanlarını içeren standart log şeması; her talep request ID ile uçtan uca izlenebilir olmalı. | Audit log altyapısı |
| **DEVOPS/PM/QA** · `G3-OPS-03` | **Test kullanıcıları ve veri kapsamı matrisi.** Farklı bölge/veri kapsamına sahip test kullanıcıları oluştur; RLS ve yetki testlerinin girdisi olacak. | Test kullanıcı matrisi |

**Gün sonu kontrolü:** Üç görünüm ayakta ve katalog doğrulaması geçiyor mu?

---

## Gün 4 — Pazartesi 3 Ağustos · Uçtan Uca Akış + Güvenlik

| Track | Bugün Ne Yapacak | Günün Çıktısı |
|-------|------------------|---------------|
| **VERİ & BI** · `G4-DB-01` | **Semantic model ve DAX KPI tanımları.** Satış ölçüleri, ilişkiler, tarih tablosu; net satış / satış adedi / büyüme / ortalama sepet DAX ile. | Power BI semantic model |
| **VERİ & BI** · `G4-DB-02` | **Üç rapor sayfasını tamamla.** Satış analizi, müşteri segmentasyonu ve kategori performansı sayfaları. Not: üçüncü senaryo kampanya değil **kategori** performansıdır — Olist veri setinde kampanya verisi yok. | 3 rapor sayfası + deep link |
| **VERİ & BI** · `G4-DB-03` | **RLS rollerini ve satır güvenliği kurallarını tanımla.** Bölge yöneticisi kendi bölgesi dışındaki veriyi hiçbir yolla görememeli. | RLS rolleri |
| **VERİ & BI** · `G4-DB-04` | **KPI değerlerini manuel SQL ile mutabakat et.** Rapordaki her KPI kartını elle yazılmış SQL sonucuyla karşılaştır; sapma varsa demo öncesi kapat. | KPI mutabakat tablosu |
| **YAPAY ZEKÂ** · `G4-AI-01` | **Sonuç şekli → görsel tipi eşlemesini API yanıtına bağla.** `ResultShapeClassifier` çıktısını (tek değer → KPI kartı, tarih+metric → çizgi, kategori+metric → bar, çok boyut → matrix) backend yanıtına taşı. | Görsel öneri alanı dolu API yanıtı |
| **YAPAY ZEKÂ** · `G4-AI-02` | **Demo için belirsiz ve reddedilen talep senaryolarını hazırla.** `AmbiguityGate` NeedsClarification üretecek bir talep + guardrail'ın reddettiği bir talep; ikisi de demoda canlı gösterilecek. | Üç demo talebi (geçerli / belirsiz / reddedilen) |
| **BACKEND** · `G4-BE-01` | **Uçtan uca akışı bağla.** Talep → SQL üretimi → veri → kısa KPI özeti + Power BI derin bağlantısı → yanıt. | Çalışan uçtan uca senaryo |
| **BACKEND** · `G4-BE-02` | **Takip sorusu ve rapor revizyonu akışı.** `CanonicalRequestDelta` + Previous Request ID üzerinden metrik değiştirme, kırılım değiştirme, filtre ekleme. | Çalışan takip sorusu akışı |
| **BACKEND** · `G4-BE-03` | **Teams entegrasyonu ve hata mesajları.** Teams bot kaydı + mesaj akışı; Rejected/Failed durumlarında kullanıcıya anlaşılır mesaj ve request ID göster. | Teams üzerinden çalışan akış |
| **DEVOPS/PM/QA** · `G4-OPS-01` | **RLS ve yetki testlerini koş.** `G3-OPS-03` test kullanıcılarıyla kapsam dışı erişim denemelerini yürüt; her biri reddedilmeli. | RLS test sonuçları |
| **DEVOPS/PM/QA** · `G4-OPS-02` | **Negatif güvenlik test paketini CI'a bağla.** Guardrail negatif testleri her push'ta otomatik koşsun; kırmızıysa merge engellensin. | Otomatik güvenlik test paketi |
| **DEVOPS/PM/QA** · `G4-OPS-03` | **E2E smoke testi ve bug listesi.** Üç senaryoyu baştan sona koş, yanıt sürelerini ölç, bulguları demo öncesi öncelik sırasıyla listele. | E2E test raporu + bug listesi |

**Gün sonu kontrolü:** Bir talep uçtan uca çalışıyor ve request ID ile izlenebiliyor mu?

---

## Gün 5 — Salı 4 Ağustos · 🎯 Demo, Kabul ve Kapanış

| Track | Bugün Ne Yapacak | Günün Çıktısı |
|-------|------------------|---------------|
| **DEVOPS/PM/QA** · `G5-OPS-01` | **Bug bash (09:00–12:00).** Kritik ve major bug'ları demo öncesi kapat; kalanları backlog'a taşı. | Kritik/major bug'lar kapalı |
| **DEVOPS/PM/QA** · `G5-OPS-02` | **Demo provası (12:00).** Tam akışı sırayla prova et; kimin ne zaman ne göstereceği netleşsin. | Prova edilmiş demo akışı |
| **VERİ & BI** · `G5-DB-01` | **Demo: Fabric flow'ları, raporlar ve RLS farkı.** Analitik flow'ları ve üç rapor sayfasını sun; RLS farkını iki farklı kullanıcıyla canlı göster. | Analitik ve raporlama sunumu |
| **YAPAY ZEKÂ** · `G5-AI-01` | **Demo: kontrollü SQL üretimi.** Bir geçerli talep, bir belirsiz talep (NeedsClarification), bir reddedilen talep — guardrail kararlarını gerekçeleriyle göster. | Guardrail canlı gösterimi |
| **BACKEND** · `G5-BE-01` | **Demo: Teams üzerinden uçtan uca.** İlk talep + takip sorusu + rapor bağlantısı. | Uçtan uca canlı demo |
| **DEVOPS/PM/QA** · `G5-OPS-03` | **Kabul kriterleri, retrospektif ve yeni backlog (17:00).** Kabul kriteri tablosunu işaretle, kapanış raporunu yaz, retroyu yürüt, sonraki sprint backlog'unu oluştur. | Kapanış raporu + retro + backlog |

**Gün sonu kontrolü:** Üç senaryo da tekrar tekrar aynı sonucu üretiyor mu?

---

## Gün 6 — Çarşamba 5 Ağustos · Tampon

| Track | Bugün Ne Yapacak | Günün Çıktısı |
|-------|------------------|---------------|
| **DEVOPS/PM/QA** · `G6-OPS-01` | **Demoda çıkan kalan bulguları kapat.** Salı hedefi tuttuysa bu gün boş kalır. Taşan işler buraya düşer. | Kapatılmış bulgular |
| **VERİ & BI** · `G6-DB-01` | **Veri sözlüğü ve işletim notları.** Görünüm tanımları, yenileme prosedürü, erişim adımları. | Veri sözlüğü + işletim dokümanı |
| **BACKEND** · `G6-BE-01` | **API ve Teams entegrasyonu teknik dokümanı.** Endpoint listesi, durum modeli, hata kodları. | API dokümanı |

---

## Devralınabilir İşler — AI Kapasitesi

> Yapay Zekâ track'inin 11 günlük işi bittiği için Mehmet Efe'nin boşta kalan kapasitesi aşağıdaki işlere kaydırılabilir. Bu maddelerin hiçbiri şu anda kimseye atanmış değil — **her biri sahiplenilmeyi bekliyor ve board üzerinden devralınır.** Devralınmazsa işler geldikleri track'te kalır; devralınırsa ilgili kişi kendi kritik yolu üzerinde daha fazla nefes alanı kazanır.

| Kod | İş | Geldiği Track | Neden Mehmet Efe için doğal | Kazanç |
|-----|-----|---------------|------------------------------|--------|
| 🔴 `G3-AI-D01` | Üç görünümün SQL'ini yaz | VERİ & BI | Sprintin tek bloker'ı. `olist_views.contract.sql` sözleşmesini Efe yazdı — kolon adlarını ve tiplerini birebir biliyor, DevData aracı da onun tarafında. | Kritik yol en hızlı açılır; Sümeyye ile Sıla doğrudan Fabric ingest + semantic modele geçer. Çıktı: üç görünüm + `sqlite_views.sql` |
| `G3-AI-D02` | SQL üretim katmanının DI entegrasyonunu bizzat yap | BACKEND | `ENTEGRASYON.md`'nin yazarı Efe; kaydı kendisi yaparsa doğru şekli ilk denemede kurar. | Ramazan API sözleşmesi ve Teams tarafına odaklanır; entegrasyon kaynaklı gidiş-gelişler ortadan kalkar. Çıktı: çalışan DI kaydı |
| `G3-AI-D03` | CI'ı gerçek build + test koşacak hale getir | DEVOPS/PM/QA | Test altyapısını yazan kişi Efe olduğu için ~400 testi CI'da doğru filtrelerle koşturmak onun için en hızlı iş. | Lokman RLS ve yetki testlerine odaklanır. Çıktı: gerçekten doğrulayan CI |
| `G4-AI-D04` | Teams yanıt formatını zenginleştir | BACKEND | Sonuç şekli sınıflandırması Efe'nin bileşeni olduğu için özet üretimini de o kurabilir. | Kısa KPI özeti + en önemli üç bulgu + doğru rapor sayfasına derin bağlantı. Çıktı: zenginleştirilmiş yanıt formatı |
| `G6-AI-D05` | Doküman tutarsızlıklarını düzelt | DEVOPS/PM/QA | Sapmaların üçü de Efe'nin yazdığı bileşenlerin dokümantasyonunda; doğru sayıları o biliyor. | Tutarlı dokümanlar (bkz. "Bilinen Tutarsızlıklar") |

---

## Yapay Zekâ Track'i — Teslim Edilenler

11 günlük AI planının tamamı sprintin ilk iki gününde kapandı. Aşağıdaki maddeler **tamamlanmış** durumdadır:

- ✅ **Canonical request şeması ve sözleşme tipleri** — `canonical_request.schema.json`, `CanonicalRequest`, `DateRangeSpec`, `RequestFilter`, `UserDataScope`, `ReasonCode`.
- ✅ **Allow-list ve metric catalog** — `allowlist.olist.json`, `metric_catalog.olist.json`, `AllowListLoader`, `MetricCatalogLoader`, `CatalogValidator`.
- ✅ **Deterministik Query Builder** — canonical request girdisinden parametreli `SELECT` üretimi; string birleştirme yok.
- ✅ **SQL Guardrail — 16 kontrol ve negatif test paketi** — `InputLimits → ParseToAst → SingleStatement → SelectOnly → NoStarSelect → NodeTypeWhitelist → AllowListObjects → NoDeniedPiiColumns → AllowListColumns → JoinPathAllowed → MinCellSize → DateRangeBudget → ScopeFilterInjection → LiteralParameterization → RowLimit → RegenerateAndRevalidate`. Fail-closed.
- ✅ **Guarded NL2SQL, ambiguity gate ve karar audit'i** — `ScriptedNl2SqlDrafter` + `Nl2SqlPrompt` + `AmbiguityGate` + `DecisionAuditRecord`. Gerçek LLM sağlayıcısı bağlanmadığı için fail-closed davranıyor.
- ✅ **Üç teknik doküman** — `ENTEGRASYON.md` (backend DI rehberi), `KAPSAM.md` (desteklenen/desteklenmeyen soru tipleri), `DEMO.md`.

Açık kalan tek AI maddesi `G3-AI-07`: üretilen SQL'in gerçek görünümler üzerinde smoke testi. Bu iş görünümler oluşmadan başlayamaz.

---

## Bilinen Tutarsızlıklar

| # | Tutarsızlık | Doğrusu / Yapılacak |
|---|-------------|---------------------|
| a | Üçüncü demo senaryosu eski planda **"kampanya performansı"** olarak geçiyor, ancak Olist veri setinde kampanya verisi yok. | Senaryo **kategori performansı**dır. Tüm dokümanlarda düzeltilecek. |
| b | `05-sql-uretimi.md` guardrail kontrol sayısını **12** olarak veriyor; kodda **16** kontrol var. | Doküman 16'ya güncellenecek. |
| c | Test sayısı README ve `KAPSAM.md`'de **406**, `ENTEGRASYON.md`'de **393**. | `dotnet test` ile doğrulanıp tek sayıya çekilecek. |
| d | `Crm.Analytics.Sql.DevData` projesi `.csproj` içinde `sqlite_views.sql` dosyasını EmbeddedResource olarak arıyor; dosya repoda yok, çözüm derlenmiyor. | `G3-DB-07` kapsamında dosya üretilecek. |
| e | Gün 1–2 görev durumları **ekip beyanına** dayanıyor; Azure, Fabric ve Power BI çıktıları repo dışında olduğu için bağımsız doğrulanamıyor. Repoda fiilen doğrulanabilen tek track **Yapay Zekâ**'nın çıktılarıdır (`G1-AI-01`, `G2-AI-01` — dosyalar, testler ve katalog repoda mevcut). | Gün 1–2'nin altyapı çıktıları için Gün 6'da ekran görüntüsü / bağlantı kanıtı toplanacak; o zamana kadar bu satırlar "beyan" olarak okunmalı. |

Bu maddelerin toplu düzeltmesi `G6-AI-D05` devralma task'ı altında izleniyor.

---

## Kapsamdan Çıkarılanlar

9 günlük iş 4 güne sıkıştırıldı. Bu, kapsamın bir kısmının düşmesi anlamına geliyor. **Aşağıdaki maddeler sessizce düşürülmedi** — bilerek, yazılı olarak kapsam dışına alındı ve sonraki sprint backlog'una geçiyor:

| Düşen madde | Neden düştü | Sonuç |
|-------------|-------------|-------|
| Asenkron iş akışı + retry / idempotency | Uçtan uca senkron akışı çalıştırmak önce gelir | Uzun süren talepler demo kapsamında senkron koşacak |
| Incremental refresh | Tam yenileme demo veri hacmi için yeterli | Üretim hacminde yenileme maliyeti ölçülmedi |
| Monitoring dashboard + alert kuralları | Audit log ve request ID korelasyonu asgari izlenebilirliği sağlıyor | Proaktif alarm yok; sorunlar log'dan elle bulunacak |
| Stres testi | Demo senaryolarında yanıt süresi ölçümü ile yetiniliyor | Yük altındaki davranış bilinmiyor |
| Otomatik CD deployment | CI (build + test) önceliklendi | Dağıtım elle yapılacak |
| Ayrı UAT oturumu | Demo günü ile birleştirildi | Kabul kriterleri demo sırasında işaretlenecek |

---

## Sprint Kabul Kriterleri (Definition of Done)

| Alan | Kriter |
|------|--------|
| Fonksiyonel | Üç demo senaryosu (satış analizi, müşteri segmentasyonu, kategori performansı) Teams'ten uçtan uca çalışıyor |
| Bağlam | Takip sorusu önceki raporu doğru revize ediyor (metrik / kırılım / filtre değişimi) |
| Güvenlik | Yetkisiz tablo, kolon ve veri kapsamına erişilemiyor; yazma sorguları reddediliyor |
| SQL | Guardrail'ın 16 kontrolü devrede ve negatif test paketi %100 geçiyor; katalog doğrulaması yeşil |
| Rapor | Power BI RLS iki farklı kullanıcıda doğru veri kapsamını gösteriyor; KPI'lar manuel SQL ile mutabık |
| İzlenebilirlik | Her talep request ID ile uçtan uca izlenebiliyor; her karar audit kaydına düşüyor |
| Doküman | Her track kendi bileşeni için kısa teknik doküman + test kanıtı teslim etti; bilinen tutarsızlıklar kapatıldı |

---

## Riskler ve Azaltma (4 güne özgü)

| Risk | Etki | Azaltma |
|------|------|---------|
| **Görünümler Gün 3'te bitmez** (en büyük risk) | Tüm zincir kayar: smoke testi, semantic model, raporlar ve uçtan uca akış hiçbiri başlamaz | `G3-AI-D01` devralma task'ı ile Mehmet Efe'nin kapasitesi devreye alınır (sözleşmeyi yazan kişi olduğu için en hızlı yol). Fabric erişimi gecikirse beklenmez, yerel `crm_dev.db` ile devam edilir ve karar yazılı olarak duyurulur (`G3-DB-06`). |
| Teams / Power BI lisans veya yetki onayı gecikmesi | RLS gösterimi ve Teams demosu yapılamaz | Talepler Gün 3 sabahı açılır; dev tenant ile paralel çalışılır. Teams gecikirse akış Swagger üzerinden gösterilir (`G3-BE-04`). |
| Demo günü tek deneme şansı var | Bir hata tüm sprint çıktısını zayıf gösterir | Gün 5 sabahı bug bash (`G5-OPS-01`), 12:00'de tam prova (`G5-OPS-02`). Demo senaryoları Gün 4'te sabitlenir (`G4-AI-02`), demo sırasında yeni senaryo denenmez. |
| **4 gün gerçekten yetmeyebilir** | Kabul kriterlerinin bir kısmı açık kalır | Zorunlu asgari kapsam: üç görünüm + guardrail'ın gerçek veride koşması + bir senaryonun uçtan uca çalışması. Diğer iki senaryo bu sıraya göre eklenir. Gün 6 tampon olarak ayrıldı; yine yetmezse açık maddeler kapanış raporunda **açık** olarak işaretlenir, tamamlanmış gibi gösterilmez. |
| Ortak sahiplik (Sümeyye + Sıla) koordinasyon maliyeti üretir | Aynı görünüm iki kez yazılır veya hiç yazılmaz | **Kapatıldı:** VERİ & BI görevleri panoda kişi bazında bölündü — SQL/veri katmanı Sümeyye'de, Fabric/Power BI Sıla'da (bkz. Gün 3 notu). |

---

## Kapsam Dışı (Bu Sprintte Yapılmayacak)

- Satış tahmini, churn ve kök neden analizi (ileri faz)
- Generic / dinamik rapor ekranları ve her soru için yeni PBIX
- Tam otonom aksiyon alan agent yapısı
- Yazma (`INSERT` / `UPDATE` / `DELETE`) işlemleri
- On-premises Query Agent kurulumu
- Production ortamına deployment (yalnızca dev/test ortamı hedeflenir)
