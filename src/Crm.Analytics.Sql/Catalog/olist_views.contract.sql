/* ===========================================================================
   GORUNUM SOZLESMESI — Veri Muhendisi'nden talep
   ===========================================================================

   Bu dosya CALISTIRILMAK icin degil, DE'ye verilecek SOZLESME olarak yazildi.
   AI Engineer'in guardrail'i yalnizca burada tanimlanan gorunumleri tanir.

   NEDEN GORUNUM ZORUNLU (ham tablolar allow-list'e alinamaz):

   Olist semasinda kapsam (yetki) kolonu YALNIZCA musteri tablosunda var
   (customer_state). Satis tutarinin bulundugu order_items tablosunda hicbir
   cografi kolon yok. Sonuc:

     - order_items'i allow-list'e koyup "scopeExempt" isaretlemek zorunda
       kalirdik. Bu, tutar iceren en hassas tablonun kapsam filtresi olmadan
       sorgulanabilmesi demektir — dogrudan bir sizinti.
     - Alternatif olarak her sorguda order_items -> orders -> customers cift
       JOIN'ine bel baglamak gerekirdi. Guardrail JOIN'i zorlayamaz; JOIN
       yapmayan bir sorgu filtresiz kalirdi.

   Bu yuzden kapsam kolonu, tutar ile AYNI SATIRDA bulunmalidir. Asagidaki
   gorunumler bunu sagliyor.

   Kaynak tablolar (dogrulanmis, data/fixtures/olist/ klasoru):
     cleaned_olist_customers_dataset(customer_id, customer_unique_id,
                                     customer_state, customer_city)          99.441
     cleaned_olist_orders_dataset(order_id, customer_id, order_status,
                                  order_purchase_timestamp)                   99.441
     cleaned_olist_order_items_dataset(order_id, order_item_id, product_id,
                                       price, freight_value)                 112.650
     cleaned_olist_order_payments_dataset(order_id, payment_type,
                                          payment_installments,
                                          payment_value)                     103.886
     cleaned_olist_products_dataset(product_id,
                                    product_category_name_english)            32.951
   =========================================================================== */


/* ---------------------------------------------------------------------------
   1) vw_sales — satis analizi + kategori performansi
   ---------------------------------------------------------------------------
   Kapsam kolonu: customer_state (tutar ile ayni satirda).

   DIKKAT — products LEFT JOIN ile baglanmali: order_items'taki bazi urunler
   products tablosunda bulunmayabilir (112.650 kalem, 32.951 urun). INNER JOIN
   kullanilirsa bu kalemler SESSIZCE dusher ve satis toplami eksik cikar;
   BI'in KPI mutabakati tutmaz. Kategorisi olmayan kalemler icin NULL yerine
   acik bir etiket kullanilmasi onerilir.
   --------------------------------------------------------------------------- */
CREATE VIEW vw_sales AS
SELECT
    oi.order_id,
    oi.order_item_id,
    oi.product_id,
    oi.price,
    oi.freight_value,
    o.customer_id,
    o.order_status,
    o.order_purchase_timestamp,
    c.customer_state,
    c.customer_city,
    COALESCE(p.product_category_name_english, 'uncategorized') AS product_category
FROM cleaned_olist_order_items_dataset  AS oi
JOIN cleaned_olist_orders_dataset       AS o ON oi.order_id   = o.order_id
JOIN cleaned_olist_customers_dataset    AS c ON o.customer_id = c.customer_id
LEFT JOIN cleaned_olist_products_dataset AS p ON oi.product_id = p.product_id;
GO


/* ---------------------------------------------------------------------------
   2) vw_customer_rfm — musteri segmentasyonu (RFM)
   ---------------------------------------------------------------------------
   Kapsam kolonu: customer_state.

   DIKKAT — Olist'te customer_id GERCEK MUSTERI DEGILDIR:
   customers ve orders tablolarinin satir sayilari birebir ayni (99.441/99.441).
   Bu, customer_id'nin siparis basina uretildigini gosterir; gercek kisiyi
   customer_unique_id temsil eder. Musteri bazli her metrik (RFM dahil)
   customer_unique_id uzerinden hesaplanmalidir, aksi halde her musteri "tek
   siparis vermis" gibi gorunur ve Frequency metrigi anlamsizlasir.
   TEYIT EDILDI (yerel SQLite fixture'i uzerinde olculdu, bkz. asagidaki not):
   99.441 customer_id karsiliginda 96.096 tekil customer_unique_id var ve
   2.997 kisi birden fazla siparis vermis. Yani customer_id gercekten siparis
   basina uretiliyor; RFM'in customer_unique_id uzerinden hesaplanmasi zorunlu.

   DIKKAT — vw_customer_rfm 95.539 satir dondurur, 96.096 DEGIL. Fark,
   order_items'ta karsiligi olmayan siparislerin musterilerinden gelir. Iki sayi
   ayri seylerdir, karistirilmamalidir.

   customer_unique_id gorunumde yer alir ancak allow-list'te identityColumns
   listesine girer: filtrede kullanilabilir, KIRILIMDA kullanilamaz. Boylece
   segment bazli sorgular calisir, kisi bazli detay reddedilir.
   --------------------------------------------------------------------------- */
CREATE VIEW vw_customer_rfm AS
SELECT
    c.customer_unique_id,
    c.customer_state,
    c.customer_city,
    MAX(o.order_purchase_timestamp) AS last_order_timestamp,
    MIN(o.order_purchase_timestamp) AS first_order_timestamp,
    COUNT(DISTINCT o.order_id)      AS order_count,
    SUM(oi.price)                   AS total_price,
    SUM(oi.freight_value)           AS total_freight
FROM cleaned_olist_customers_dataset    AS c
JOIN cleaned_olist_orders_dataset       AS o  ON c.customer_id = o.customer_id
JOIN cleaned_olist_order_items_dataset  AS oi ON o.order_id    = oi.order_id
GROUP BY c.customer_unique_id, c.customer_state, c.customer_city;
GO


/* ---------------------------------------------------------------------------
   3) vw_payment — odeme kanali kirilimi
   ---------------------------------------------------------------------------
   Kapsam kolonu: customer_state.

   Bir siparisin birden fazla odeme kaydi olabilir (103.886 odeme / 99.441
   siparis). Bu yuzden odeme tutari ile order_items tutari AYNI gorunumde
   birlestirilmemelidir: birlestirilirse tutarlar coklanir. Odeme analizi
   ayri gorunum uzerinden yapilir.
   --------------------------------------------------------------------------- */
CREATE VIEW vw_payment AS
SELECT
    pay.order_id,
    pay.payment_type,
    pay.payment_installments,
    pay.payment_value,
    o.order_status,
    o.order_purchase_timestamp,
    c.customer_state,
    c.customer_city
FROM cleaned_olist_order_payments_dataset AS pay
JOIN cleaned_olist_orders_dataset         AS o ON pay.order_id  = o.order_id
JOIN cleaned_olist_customers_dataset      AS c ON o.customer_id = c.customer_id;
GO


/* ===========================================================================
   DE'DEN EK TALEPLER

   1) READ-ONLY ERISIM: uygulama kullanicisina yalnizca bu gorunumlere
      GRANT SELECT verilmeli; ham tablolara SELECT izni VERILMEMELIDIR.
      Guardrail'de bir AST gezinme boslugu kalsa dahi erisilebilir yuzey
      bu gorunumlerle sinirli kalir.

   2) IKINCI SAVUNMA KATMANI (onaylandi): gorunumlerin altindaki tablolara
      CREATE SECURITY POLICY ile FILTER PREDICATE tanimlanmasi ve baglanti
      acilisinda
          EXEC sp_set_session_context @key=N'UserState', @value=..., @read_only=1
      cagrilmasi. @read_only=1 kritik: baglanti havuza donene kadar kimlik
      degistirilemez. WITH SCHEMABINDING = ON kullanilmali.

   3) NULL customer_state: kapsam filtresi "customer_state IN (...)" bicimindedir
      ve NULL degerli satirlari DISARIDA BIRAKIR.
      KAPANDI — olculdu: NULL/bos customer_state sayisi 0, tekil eyalet sayisi 27.
      Sessiz veri kaybi YOK. (Bu degismez FixtureInvariantTests tarafindan
      surekli dogrulaniyor; bir veri seti yenilemesi bozarsa test kirilir.)

   4) order_status: iptal/iade edilmis siparislerin (canceled, unavailable)
      satis metriklerine dahil edilip edilmeyecegi bir IS KARARIDIR. Karar
      gelene kadar gorunume filtre KONULMADI — sessiz varsayim yapmamak icin.
      NICELENDIRILDI: canceled 625 + unavailable 609 = 1.234 siparis (~%1,2).
      Karar hala is tarafinda, ama artik etkisinin buyuklugu biliniyor.

   =========================================================================== */

/* ---------------------------------------------------------------------------
   YEREL DOGRULAMA NOTU

   Yukaridaki "olculdu / teyit edildi" ifadeleri, Azure ortami hazir olana kadar
   kullanilan yerel SQLite fixture'i uzerinde uretildi:

       dotnet run --project Crm.Analytics.Sql.DevData

   Fixture, data/fixtures/olist/ altindaki CSV'lerden her seferinde SIFIRDAN uretilir ve
   satir sayilari bu dosyadaki beklenen degerlere karsi dogrulanir. Bu, elle
   paylasilan bir kopyada bulunan sessiz bir hatadan sonra eklendi: orders tablosu
   iki kez yuklenmis (198.882 satir / 99.441 tekil order_id), bu yuzden vw_sales
   225.300 satir donuyor ve TUM satis metrikleri tam iki katina cikiyordu. Hicbir
   hata mesaji yoktu.

   Bu gorunumlerin SQLite karsiligi: Crm.Analytics.Sql.DevData/sqlite_views.sql
   Iki dosya arasindaki sapmayi Crm.Analytics.Sql.IntegrationTests icindeki
   ViewContractDriftTests yakalar (kolon listelerini allowlist.olist.json ile de
   karsilastirir).

   ONEMLI: yerel fixture Azure SQL'in YERINE GECMEZ. Read-only erisim, RLS ve
   sp_set_session_context taklidi YAPMAZ; yukaridaki 1. ve 2. talepler hala
   gecerlidir.
   --------------------------------------------------------------------------- */
