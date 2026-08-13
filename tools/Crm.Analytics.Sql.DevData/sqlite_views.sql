-- ===========================================================================
-- KANONIK GORUNUMLER — SQLite karsiligi
-- ===========================================================================
--
-- Kaynak sozlesme: Crm.Analytics.Sql/Catalog/olist_views.contract.sql
-- O dosya Veri Muhendisi'ne verilen T-SQL SOZLESMESIDIR ve calistirilmak icin
-- yazilmamistir (GO ayiricilari, Azure'a ozgu maddeler). Bu dosya ayni uc
-- gorunumun SQLite lehcesindeki calistirilabilir esdegeridir.
--
-- DRIFT UYARISI: iki dosya elle senkron tutulur. Sozlesme degisip burasi
-- guncellenmezse Crm.Analytics.Sql.IntegrationTests icindeki gorunum-sozlesme
-- uyum testi (kolonlari allowlist.olist.json ile karsilastirir) KIRILIR.
-- Testin amaci tam olarak bu sapmayi yakalamaktir.
--
-- NOT: Ifadeler GO ile ayrilmis. GO bir SQL komutu DEGIL, batch ayiricidir;
-- Crm.Analytics.Sql.DevData yuklerken bu satirlari atar. Buradaki amaci, dosyanin
-- sozlesme dosyasiyla AYNI bicimde T-SQL olarak ayristirilabilmesi: sapma testi
-- (ViewContractDriftTests) iki dosyayi da ayni ayristiriciyla okuyor ve T-SQL
-- CREATE VIEW'un bir batch'te yalniz olmasini sart kosuyor.
--
-- Sozlesmeye gore BILINCLI olarak burada YER ALMAYANLAR:
--   - WITH SCHEMABINDING            (SQLite'ta yok)
--   - CREATE SECURITY POLICY / RLS  (ikinci savunma katmani, Azure tarafinda)
--   - sp_set_session_context        (Azure tarafinda)
-- Bunlar sozlesmenin "DE'DEN EK TALEPLER" bolumunde, gorunum tanimlarinin
-- icinde degil. Yerel fixture read-only erisim ve RLS taklidi YAPMAZ.
-- ===========================================================================


-- ---------------------------------------------------------------------------
-- 1) vw_sales — satis analizi + kategori performansi
-- ---------------------------------------------------------------------------
-- Kapsam kolonu: customer_state (tutar ile ayni satirda; ham tablolarda
-- tutar ve cografya ayri tablolarda oldugu icin gorunum zorunlu).
--
-- products LEFT JOIN: order_items'taki bir urun products'ta bulunmayabilir.
-- INNER JOIN kullanilirsa kalemler SESSIZCE duser ve satis toplami eksik cikar.
--
-- Yerel fixture ile olculdu:
--   - products'ta karsiligi OLMAYAN kalem : 0     (LEFT JOIN bugun fark yaratmiyor,
--                                                  ileriye donuk koruma olarak duruyor)
--   - kategorisi BOS olan urun            : 610
--   - bu urunlerden gelen kalem           : 1.603 -> product_category='uncategorized'
--
-- Yani COALESCE savunmasi LATENT DEGIL, AKTIF olarak yuk tasiyor: 1.603 kalem
-- NULL kategori yerine acik 'uncategorized' etiketi aliyor. NULL birakilsa bu
-- kalemler kategori kirilimlarinda sessizce kaybolurdu.
-- ---------------------------------------------------------------------------
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
FROM cleaned_olist_order_items_dataset   AS oi
JOIN cleaned_olist_orders_dataset        AS o ON oi.order_id   = o.order_id
JOIN cleaned_olist_customers_dataset     AS c ON o.customer_id = c.customer_id
LEFT JOIN cleaned_olist_products_dataset AS p ON oi.product_id = p.product_id;
GO


-- ---------------------------------------------------------------------------
-- 2) vw_customer_rfm — musteri segmentasyonu (RFM)
-- ---------------------------------------------------------------------------
-- Kapsam kolonu: customer_state.
--
-- Olist'te customer_id GERCEK MUSTERI DEGILDIR; siparis basina uretilir.
-- Gercek kisiyi customer_unique_id temsil eder. Yerel fixture ile teyit edildi:
-- 99.441 customer_id karsiliginda customers tablosunda 96.096 tekil
-- customer_unique_id var ve 2.997 kisi birden fazla siparis vermis.
-- Bu yuzden musteri bazli her metrik customer_unique_id uzerinden hesaplanir.
--
-- DIKKAT — bu gorunum 95.539 satir dondurur, 96.096 DEGIL. Fark, siparisi
-- order_items'ta karsiligi olmayan musterilerden gelir (JOIN onlari duser).
-- Iki sayi ayri seylerdir; testlerde karistirilmamalidir.
--
-- customer_unique_id gorunumde yer alir ama allow-list'te identityColumns
-- listesindedir: filtrede kullanilabilir, KIRILIMDA kullanilamaz.
-- ---------------------------------------------------------------------------
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
FROM cleaned_olist_customers_dataset   AS c
JOIN cleaned_olist_orders_dataset      AS o  ON c.customer_id = o.customer_id
JOIN cleaned_olist_order_items_dataset AS oi ON o.order_id    = oi.order_id
GROUP BY c.customer_unique_id, c.customer_state, c.customer_city;
GO


-- ---------------------------------------------------------------------------
-- 3) vw_payment — odeme kanali kirilimi
-- ---------------------------------------------------------------------------
-- Kapsam kolonu: customer_state.
--
-- Bir siparisin birden fazla odeme kaydi olabilir (103.886 odeme / 99.441
-- siparis). Bu yuzden odeme tutari ile order_items tutari AYNI gorunumde
-- birlestirilmez: birlestirilirse tutarlar coklanir.
-- ---------------------------------------------------------------------------
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
