# Veri Mühendisi'ne Görünüm Talebi

> Bu metin Slack/Teams'e olduğu gibi yapıştırılabilir. Teknik sözleşme:
> `Catalog/olist_views.contract.sql`

---

Selam,

SQL guardrail katmanı hazır (16 kontrol, 482 test geçiyor) ama **SQL Server üzerinde
kullanılabilir hâle gelmesi için üç görünüme ihtiyacım var**. Görünümlerin SQLite
eşdeğerini yerel test fixture'ı olarak yazdım (`Crm.Analytics.Sql.DevData/sqlite_views.sql`)
ve üretilen SQL orada gerçek veriyle koşuyor; ama fixture bir test aracı, üretim hedefi değil. Ham tabloları allow-list'e koyamıyorum —
nedenini aşağıda açıkladım, çünkü bu bir tercih değil güvenlik kısıtı.

## Neden ham tablolar allow-list'e konamıyor

Yetki filtresi (`customer_state`) **yalnızca `customers` tablosunda**, tutar (`price`) ise
**`order_items` tablosunda**. Ham tabloları allow-list'e koyarsam:

- `order_items`'ı kapsam filtresinden **muaf** işaretlemem gerekir (`scopeExempt`), çünkü o
  tabloda `customer_state` kolonu yok.
- Bu, tutar içeren en hassas tabloyu **kapsam filtresiz** bırakmak demektir. Yetkisi
  yalnızca SP olan bir kullanıcı tüm Brezilya'nın satış tutarını görebilirdi.

Görünüm katmanı bu problemi kaynağında çözüyor: JOIN bir kez, doğru şekilde, DE tarafında
yapılıyor ve guardrail tek objeye kapsam filtresi uygulayabiliyor.

## İhtiyacım olan üç görünüm

| Görünüm | Ne içeriyor | Kapsam kolonu |
|---|---|---|
| `vw_sales` | Sipariş kalemi düzeyinde satış (orders × order_items × customers × products) | `customer_state` |
| `vw_customer_rfm` | Müşteri düzeyinde toplam (`customer_unique_id` bazında) | `customer_state` |
| `vw_payment` | Ödeme kaydı düzeyinde (order_payments × orders × customers) | `customer_state` |

Kolon listeleri ve gerekçeleri `olist_views.contract.sql` dosyasında. Özellikle rica
ettiğim üç nokta:

1. **`customer_unique_id` kullanılmalı, `customer_id` değil.** Olist'te `customer_id`
   sipariş başına üretiliyor (`customers` ve `orders` aynı satır sayısına sahip); gerçek
   kişiyi `customer_unique_id` temsil ediyor. RFM'de bunu karıştırmak her müşteriyi tek
   siparişli gösterirdi.
2. **`product_category` için `COALESCE(..., 'uncategorized')`.** Kategorisi olmayan
   kalemler var; sessiz bir NULL grubu raporda "boş" satır olarak görünür.
3. **`vw_sales` ile `vw_payment` aynı sorguda birleştirilmemeli.** Bir siparişin birden
   fazla ödeme kaydı olabiliyor (103.886 ödeme / 99.441 sipariş) — tutarlar çoklanır.
   Allow-list'te `maxJoins = 0` yaptım, guardrail bunu zaten reddediyor; sizin tarafta da
   ayrı görünümler olarak kalması yeterli.

## Ayrıca ihtiyacım olan iki şey

**1. Row-Level Security (ikinci savunma katmanı).**

Guardrail şu an **tek** savunma katmanı. AST gezinmesinde bir boşluk gözden kaçarsa (CTE,
derived table, UNION kolu) veritabanı satır sızıntısını yine engellemeli:

- `CREATE SECURITY POLICY` + `FILTER PREDICATE`, `WITH SCHEMABINDING = ON`
- Bağlantı açılışında `EXEC sp_set_session_context @key=N'UserState', @value=..., @read_only=1`
  — `@read_only=1` kritik: bağlantı havuza dönene kadar kimlik değiştirilemez
- Predicate çağıran principal'i de doğrulamalı (`DATABASE_PRINCIPAL_ID()`)

Guardrail tarafında `sp_set_session_context` çağrısı **ayrıca yasaklı** (negatif testi var),
yani üretilen SQL kimliği değiştirmeye çalışamaz.

**2. Base tablolara `GRANT SELECT` verilmemesi.** Uygulama kullanıcısı yalnızca
allow-list'teki görünümleri görebilmeli. Guardrail allow-list dışı objeyi reddediyor ama
ikinci katman olarak izinlerin de kapalı olması gerekiyor.

## İsteğe bağlı: boyut değer listesi

Ayrıştırıcı şu an metinden **filtre çıkarmıyor**, çünkü "SP" ifadesinin bir eyalet kodu mu
yoksa anlaşılmayan bir kelime mi olduğunu bilemiyor. Boyut başına değer listesi (örnek:
27 eyalet kodu, kategori adları) verirseniz "SP ve RJ'yi karşılaştır" gibi talepleri de
deterministik olarak çözebilirim. Uydurma eşleme yapmak istemedim.

## Ne zaman bloke oluyorum

Görünümler olmadan `allowlist.olist.json` ve `metric_catalog.olist.json` kullanılamaz —
guardrail çalışır ama gerçek veri dönmez. Demo guardrail davranışını gösterebiliyor, uçtan
uca sonuç gösteremiyor.

Teşekkürler.
