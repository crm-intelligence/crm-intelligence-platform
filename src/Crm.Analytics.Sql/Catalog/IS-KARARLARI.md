# Bekleyen İş Kararları

**Kime:** Fabric/BI Geliştiricisi + iş birimi
**Kimden:** AI Engineer

Bu kararların hiçbirini kendim vermedim. Her biri raporun **sayısını değiştirir**; sessiz
bir varsayım yapmak, yanlış sayıyı doğru görünen bir raporla sunmak olurdu. Kararlar gelene
kadar ilgili metrikler `businessApprovalPending` / `designPending` durumunda duruyor ve
ayrıştırıcı bunları kullanıcıya önermiyor.

---

## Karar 1 — "Net satış" kargo dahil mi?

**Neden karar gerekiyor:** Olist verisinde iki aday var ve ikisi de savunulabilir. Hangisinin
"net satış" olduğu muhasebe/BI tanımına bağlı.

| Aday | İfade | Anlamı |
|---|---|---|
| `item_sales` | `SUM(price)` | Ürün tutarı, **kargo hariç** |
| `customer_paid_total` | `SUM(price + freight_value)` | Müşterinin ödediği, **kargo dahil** |

**Şu anki durum:** İkisi de tanımlı ve kullanılabilir, ama **hiçbiri `net_sales` anahtarını
almadı**. Yani "net satış" diye sorulduğunda sistem bir metriğe bağlanmıyor.

**Kararın etkisi:**
- `avg_basket` (ortalama sepet) ifadesi de değişir: payda `item_sales` kullanılıyor,
  `customer_paid_total` seçilirse o da güncellenmeli.
- Power BI semantic model'de KPI adı buna göre belirlenir.
- Kargo Brezilya'da toplam tutarın önemli bir kısmı; iki aday arasındaki fark küçük değil.

**İhtiyacım olan:** Hangisi `net_sales` anahtarını alacak? Diğeri hangi adla kalacak?

---

## Karar 2 — RFM'de "recency" referans tarihi

**Neden karar gerekiyor:** Olist verisi **2018'de bitiyor**. `GETDATE()` kullanırsam bugün
(2026) tüm müşteriler ~8 yıl önce alışveriş yapmış görünür ve RFM segmentasyonu anlamsızlaşır
— herkes "kayıp müşteri" olur.

**Seçenekler:**

| Seçenek | Sonuç |
|---|---|
| Veri setindeki en son sipariş tarihi | Segmentasyon anlamlı olur; veri güncellenince referans kayar |
| Sabit bir tarih (örnek: `2018-10-17`) | Tekrarlanabilir, ama elle güncellenmesi gerekir |
| `GETDATE()` | Bu veri setiyle **anlamsız** — önerilmiyor |

**Şu anki durum:** `recency_days` metriğinin `expression` alanı **null**; karar gelmeden
ifade yazılmadı. Bu yüzden `customer_segmentation` use case'i `partial` durumunda: Frequency
ve Monetary çalışıyor, Recency eksik.

**İhtiyacım olan:** Referans tarih hangisi olacak? Bir de RFM skor eşikleri ve segment
adları (örnek: "Şampiyon", "Riskli", "Kayıp") — bunlar da iş birimi kararı.

---

## Karar 3 — İptal edilen siparişler sayılacak mı?

**Neden karar gerekiyor:** `order_status` kolonunda `canceled` ve `unavailable` gibi
değerler var. Şu an **hiçbir filtre uygulanmıyor**: tüm siparişler tutara giriyor.

**Şu anki durum:** `order_status` bir boyut olarak tanımlı, yani kullanıcı isterse kırılım
yapabiliyor veya filtreleyebiliyor. Ama **varsayılan davranış** belirlenmedi.

**Etkinin büyüklüğü (yerel fixture üzerinde ölçüldü):**

| `order_status` | Sipariş |
|---|---|
| `delivered` | 96.478 |
| `shipped` | 1.107 |
| `canceled` | **625** |
| `unavailable` | **609** |
| `invoiced` | 314 |
| `processing` | 301 |
| `created` | 5 |
| `approved` | 2 |

Yani karar **1.234 siparişi (~%1,2)** etkiliyor. Karar hâlâ iş tarafında, ama artık
"küçük bir ayrıntı mı, ciddi bir sapma mı" sorusu cevaplanabilir durumda.

**İlgili ikinci bulgu:** 775 siparişin `order_items` tablosunda **hiç kalemi yok**.
Bu yüzden `vw_sales` 2018-09-03'te biterken ham `orders` tablosu 2018-10-17'ye kadar
gidiyor. "Sipariş sayısı" metriği hangi kaynaktan hesaplanırsa 775 fark çıkar — BI
tarafındaki KPI mutabakatı için bu farkın bilinmesi gerekiyor.

**İhtiyacım olan:** "Satış tutarı" sorulduğunda iptal edilen siparişler dahil mi? Eğer
hariç olacaksa bunu görünüm katmanında mı (DE), metrik ifadesinde mi uygulayalım? Görünümde
uygulamak daha güvenli — o zaman hiçbir sorgu yanlışlıkla iptal edilenleri sayamaz.

---

## Karar 4 — Belirsizlik eşiği (teknik ama iş etkisi var)

Talebin kelimelerinin **en az %60'ı** katalog terimlerine bağlanmazsa sistem netleştirme
sorusu soruyor. Bu eşik konfigürasyona açık (`SqlProductionOptions.ConfidenceThreshold`).

- Eşik **yüksek** → sistem sık sık "ne demek istediniz?" sorar, kullanıcı sıkılır.
- Eşik **düşük** → sistem anlamadığı talebe rapor üretir, kullanıcı yanlış rapora güvenir.

Doğru değeri ancak gerçek kullanıcı talepleriyle ölçebiliriz. **UAT sırasında** netleştirme
oranını izleyip birlikte ayarlamayı öneriyorum. Varsayılan 0.60 bir başlangıç noktası, bir
gerçek değil.

---

## Kararların gelmemesi ne anlama geliyor

| Karar | Gelmezse |
|---|---|
| 1 — net satış | "Net satış" sorusu netleştirmeye gider; iki metrik ayrı adlarla kullanılabilir |
| 2 — recency | RFM segmentasyonu eksik kalır (F ve M çalışır, R yok) |
| 3 — iptal siparişler | Tutarlar iptal edilenleri **içerir**; rapor ile muhasebe ayrışabilir |
| 4 — eşik | Varsayılan 0.60 ile devam; UAT'ta ayarlanır |

Kararlar `metric_catalog.olist.json` içindeki `approvalNote` alanlarında da kayıtlı, böylece
kod tarafında da hangi metriğin neyi beklediği görünüyor.
