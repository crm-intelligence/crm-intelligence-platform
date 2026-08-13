# SQL Üretim Katmanı — Kapsam Dökümü

**Bileşen:** `Crm.Analytics.Sql` — Semantic Catalog, Talep Ayrıştırma, Deterministic Query Builder, SQL Guardrail
**Durum:** 482 test geçiyor (414 birim + 68 entegrasyon). Guardrail 16/16 kontrol bağlı.
**Backend entegrasyonu:** `ENTEGRASYON.md` · **Demo:** `DEMO.md`

Bu doküman, sistemin **hangi soruları yanıtlayabildiğini ve hangilerini yanıtlayamadığını**
kaydeder. Amaç, demo ve UAT sırasında sürpriz yaşanmaması: yanıtlanamayan bir soru tipinin
gerekçesi burada yazılı olmalı.

---

## 1. Desteklenen soru tipleri

| Soru tipi | Örnek | Yol |
|---|---|---|
| Tek değer | "Toplam satış tutarı ne?" | Query Builder → KPI kartı |
| Kategorik kırılım | "Eyalete göre satış" | Query Builder → bar |
| Zaman serisi (yıl/çeyrek/ay) | "Aylık satış trendi" | Query Builder → çizgi |
| İki boyutlu kırılım | "Eyalet ve kategoriye göre satış" | Query Builder → matrix |
| Filtreli talep | "Elektronik kategorisinde satış" | Query Builder, değer parametrelenir |
| Tarih aralıklı talep | "1 Ocak – 31 Mart arası satış" | Query Builder, ISO 8601 parametre |
| Çoklu ölçüm | "Satış tutarı ve sipariş sayısı" | Aynı kaynaktan olmak koşuluyla |
| Takip sorusu / revizyon | "Satış adedi yerine sipariş sayısı" | `CanonicalRequestReviser` (5 senaryo test edildi) |
| Katalog dışı kavram | Desteklenmeyen metric/dimension | Unsupported veya dinamik netleştirme; SQL üretilmez |

Serbest metin girişi `CatalogTermRequestParser` ile çözümlenir: metinde **yalnızca katalog
alias'ları** aranır. Çekim eki toleransı vardır ("eyaletlere" → `customer_state`). Tanınan
tarih ifadeleri: `bu/geçen/son yıl·çeyrek·ay·hafta`, `son N gün/ay/yıl/hafta`, `bugün`,
`dün`, `Ocak 2018`, `2018`, ISO tarih çifti (`2018-01-01 ile 2018-03-31`).

---

## 2. Desteklenmeyen soru tipleri ve gerekçeleri

### 2.1 Veri eksikliğinden desteklenmeyenler

| Soru tipi | Gerekçe | Ne gerekiyor |
|---|---|---|
| **Kampanya performansı** | Olist veri setinde kampanya tablosu ve `campaign_id` kolonu **yok**. Siparişi kampanyaya bağlayan hiçbir alan bulunmuyor. | DE'den kampanya köprü görünümü |
| **Satıcı bazlı analiz** | `order_items`'ta `seller_id` yok (temizlenmiş dataset). | DE'den `seller_id` kolonu |
| **RFM'in Recency bacağı** | `recency_days` hesaplanabilir ama **referans tarih kararı yok**. Veri 2018'de bitiyor; `GETDATE()` kullanılırsa tüm müşteriler ~8 yıl önce alışveriş yapmış görünür ve segmentasyon anlamsızlaşır. | İş biriminden referans tarih kararı |
| **"Net satış"** | `price` (kargo hariç) ve `price + freight_value` (kargo dahil) iki aday olarak tanımlı; hangisinin "net satış" olduğu **iş kararı**. Hiçbiri `net_sales` anahtarını almadı. | BI'dan adlandırma onayı |
| **Ürün adı / müşteri adı** | Dataset'te yok (yalnızca `product_id`, `customer_id`). | — |

### 2.2 Tasarım kararıyla desteklenmeyenler

| Soru tipi | Gerekçe |
|---|---|
| **Gün bazında kırılım** | `CAST` gerektiriyor; izinli fonksiyon listesinde değil. |
| **Hafta bazında kırılım** | `DATEPART(week, ...)` hafta başlangıcının sunucu `DATEFIRST` ayarına bağlı olması nedeniyle **deterministik değil**. Aynı sorgu farklı ortamlarda farklı sonuç verirdi. |
| **Kişi/kayıt seviyesinde detay** | `customer_id`, `customer_unique_id`, `order_id` kırılımda kullanılamaz (`GR012`). Agregasyon içinde ve filtrede serbest. |
| **Küçük grup seçimi** | `HAVING COUNT(*) = 1` gibi ifadeler reddedilir; tek kişiyi tespit etmenin klasik yolu. |
| **Farklı görünümleri birleştiren talep** | `maxJoins = 0`. `vw_sales` ile `vw_customer_rfm` arasında ortak anahtar yok (`customer_id` vs `customer_unique_id`); teknik olarak da birleştirilemezler. |
| **3 yıldan geniş tarih aralığı** | `maxDateRangeDays = 1100`. `TOP` dönen satırı sınırlar, taranan satırı sınırlamaz. |
| **`SELECT *`** | Yıldız çalışma anında kolonlara genişler ve allow-list kontrolünü etkisiz kılar; görünüme yarın eklenen bir PII kolonu sessizce döner. Nitelendirilmiş `v.*` de reddedilir. |
| **Sıralama önerisi** | `ORDER BY` olmayan bir sorguda `TOP` hangi satırların döneceğini belirsiz bırakır. Guardrail sıralama **eklemez** — hangi sıralamanın doğru olduğu iş kararı, varsayım yapmak yanlış veri göstermek olurdu. |
| **Tarihsiz talep** (zaman boyutu olan kaynakta) | Aralık olmadan sorgu tüm dönemi tarar ve tarih bütçesi denetlenemez: `DateRangeBudget` var olmayan bir aralığı denetleyemez, `TOP` ise dönen satırı sınırlar, **taranan** satırı sınırlamaz. Varsayılan aralık uydurulmaz → `CL002`. Zaman boyutu taşımayan kaynaklarda (`vw_customer_rfm`) aralık istenmez. |
| **Metinden filtre çıkarımı** | Filtre kurmak boyut **değerlerini** bilmeyi gerektirir: "SP" bir eyalet kodu mu, anlaşılmayan bir kelime mi? Katalog bugün değer sözlüğü taşımıyor; uydurma değer eşlemesi yerine filtre üretimi kapsam dışı bırakıldı. Filtreler yapılandırılmış olarak (UI seçimi / takip sorusu) gelir. **DE'den boyut değer listesi gelirse açılabilir.** |
| **Tek başına "satış"** | Katalogda `satis` tek kelimelik alias **kasten** yok: "satış" kargo dahil mi hariç mi belirsiz (bkz. 2.1 "net satış"). Belirsiz terimi bir metriğe bağlamak, kullanıcının sormadığı şeyi doğru görünen bir raporla yanıtlamak olurdu. |

### 2.3 Güvenlik gereği reddedilenler

Veri değiştirme (`INSERT`/`UPDATE`/`DELETE`/`MERGE`), şema değiştirme (`DROP`/`ALTER`/`CREATE`/
`TRUNCATE`), izin verme (`GRANT`), dinamik SQL (`EXEC`, `sp_executesql`), oturum bağlamı
değiştirme (`sp_set_session_context`), dosya/dış kaynak okuma (`OPENROWSET`, `OPENJSON`,
`OPENQUERY`), kullanıcı tanımlı fonksiyon çağrısı ve `APPLY`, ifade zinciri (`;`), allow-list
dışı obje/kolon, yasaklı PII kolonu, tanımsız JOIN yolu, yetki kapsamı dışı bölge talebi.

Tam liste ve gerekçe kodları: `Contracts/ReasonCode.cs`.
Kanıt paketi: `Crm.Analytics.Sql.Tests/Evidence/NegativeTestEvidenceTests.cs`.

---

## 3. Dokümandan sapmalar

Proje dokümanı 12 guardrail kontrolü tanımlıyordu. Uygulanan set **16 kontrol** içeriyor ve
üç yerde dokümandan bilinçli olarak ayrılıyor:

| # | Sapma | Gerekçe |
|---|---|---|
| 1 | **4 yeni kontrol** eklendi: `InputLimits` (GR015), `NodeTypeWhitelist` (GR011), `MinCellSize` (GR012), `DateRangeBudget` (GR013) | Dokümandaki set; APPLY/fonksiyon çağrısı, kimlik seviyesinde detay, küçük grup seçimi ve girdi boyutu boşluklarını kapatmıyordu. `APPLY` boşluğu test ile kanıtlandı: sorgu diğer tüm kontrolleri geçip **kabul ediliyordu**. |
| 2 | **`RegenerateAndRevalidate`** (kontrol 15) eklendi | Mutasyon yapan kontroller (kapsam enjeksiyonu, parametreleme, limit) kendi açığını yaratabilir; değişiklikten sonra hiçbir kontrol tekrar koşmuyordu. |
| 3 | **Kontrol sırası değişti:** `ParseToAst` artık `SingleStatement`'tan önce | Parse edilmemiş metinde `;` saymak ancak regex ile yapılabilirdi ve bu "kontroller regex ile yapılmaz" kırmızı çizgisini ihlal ederdi. Ayrıca `'a;b'` literali yanlış pozitif üretirdi. |
| 4 | **Kontrol sırası değişti:** `NoDeniedPiiColumns` artık `AllowListColumns`'tan önce | PII kolonları allow-list'te bulunmadığı için allow-list kontrolü önce koşarsa PII kontrolü **ölü kod** olur ve bir PII erişim denemesi audit'e "bilinmeyen kolon" (`GR004`) olarak, yani yazım hatasıyla aynı şekilde yazılırdı. |
| 5 | `NoStarSelect` kapsamı genişletildi: nitelendirilmiş `v.*` de reddedilir | Doküman yalnızca `SELECT *` yazıyordu. |
| 6 | `NoDeniedPiiColumns` **tüm ağaçta** denetleniyor | Doküman "deniedColumns'dan hiçbiri **seçilmemiş**" diyordu, yani yalnızca SELECT listesi. `WHERE email LIKE 'a%'` PII'yi seçmiyor ama sızdırıyor. |
| 7 | `Timeout` bir kontrol değil, sözleşme alanı | Uygulaması execution katmanına ait; kontrol listesinde durması sahte güvenlik hissi verirdi. |
| 8 | Metric expression'larında **tablo alias'ı kullanılmıyor** | Doküman `SUM(f.net_amount)`, negatif test örnekleri `vw_sales v` kullanıyordu — çelişkili. Alias artık AST üzerinde Query Builder tarafından nitelendiriliyor. |
| 9 | `IN` listeleri **n adet ayrı parametre** | Dokümandaki `@regions: "Marmara,Ege"` sözleşmesi T-SQL'de çalışmaz (Microsoft bunu "antipattern" olarak adlandırıyor). `STRING_SPLIT` compat-level bağımlılığı ve sıra garantisizliği nedeniyle kullanılmadı. |
| 10 | Üçüncü demo senaryosu **kategori performansı** | Kampanya performansı bu veriyle kurulamıyor (bkz. 2.1). |

---

| 11 | **Ayrıştırıcı deterministik**, dil modeli değil | Doküman ayrıştırıcının nasıl çalışacağını belirtmiyordu. Dil modeli tabanlı bir ayrıştırıcı prompt injection ile yönlendirilebilir; katalog alias'ı arayan bir ayrıştırıcının yönlendirilebilecek davranışı yoktur. Arayüz (`IRequestParser`) model tabanlı bir uygulamaya açık, ama o da aynı belirsizlik kapısından geçer. |
| 12 | **`confidence` kapsama oranı** olarak tanımlandı | Doküman bir olasılık ima ediyordu; deterministik ayrıştırıcının "ne kadar eminim" diye bir iç durumu yoktur. Ölçülebilir tanım: talebin kaç kelimesinin bir anlama bağlandığı. Eşik (varsayılan 0.60) konfigürasyona açık — doğru değer ancak gerçek taleplerle ölçülebilir. |
| 13 | **Yorumla filtre atlatma reddedilmez, etkisiz kalır** | Doküman bunu negatif test listesinde (6.2.10) "reddedilmeli" olarak yazıyordu. Gerçek davranış daha iyi: yorum AST'de yok, literal parametreye çevrilir, kapsam filtresi enjekte edilir → sorgu **düzeltilerek** kabul edilir. Ret, atlatmanın engellendiğinden daha zayıf bir güvence olurdu. |
| 14 | **Ölçüm/kırılım sırası metindeki sıra** | Eşleştirme sırası alias uzunluğuna bağlıdır; o sırayı korumak "sipariş sayısı ve satış tutarı" diyen kullanıcıya kolonları ters sırada göstermek olurdu. |

---

## 4. Bilinen sınırlamalar

1. **Parametre ile yazılmış kapsam koşulları değerlendirilemez.** `GR008` yalnızca sabit değerleri
   tespit eder; parametreli koşullarda koruma enjekte edilen filtreye kalır (veri sızmaz).
2. **JOIN `ON` koşulunun içeriği doğrulanmaz.** Hangi objelerin birleştirildiği denetlenir, koşulun
   kendisi denetlenmez. Yanlış `ON` kartezyen çarpım üretebilir — güvenlik değil performans sorunu.
3. **Görünüm katmanı henüz yok.** `allowlist.olist.json` ve `metric_catalog.olist.json`,
   `olist_views.contract.sql` içindeki görünümler oluşturulmadan kullanılamaz.
4. **DB tarafında RLS kurulmadı.** Guardrail tek savunma katmanı; ikinci katman DE'den bekleniyor.
5. **Gerçek veritabanı entegrasyon testi yok.** Üretilen SQL'in referans SQL ile aynı sonucu
   verdiği doğrulanmadı — bu bir SQL Server instance'ı gerektiriyor.
6. **Model SQL yolu yoktur.** Ollama yalnızca runtime catalog içinden semantic key seçer.
   Query Builder'ın karşılayamadığı talep fail-closed netleştirmeye veya rete gider.
7. **Ayrıştırıcının çekim eki toleransı morfolojik çözümleme değil.** Kök dört karakter veya
   daha uzunsa ön ek eşleşmesi yeterli sayılır. Doğruluğu, katalog alias'larının ayırt edici
   seçilmesine bağlı; alias eklerken bu gözetilmeli.
8. **Kullanıcı → veri kapsamı eşlemesi bu bileşende yok.** `UserDataScope` çağırandan gelir;
   Backend ve DE'nin sorumluluğunda. Çözümlenemeyen kapsam `GR007` ile reddedilir.
