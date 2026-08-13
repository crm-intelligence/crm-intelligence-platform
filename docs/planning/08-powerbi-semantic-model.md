# 8. Power BI ve Semantic Model Stratejisi

## 8.1 Demo Yaklaşımı

Demo aşamasında **sabit bir semantic model** kullanılır. Satış analizi, müşteri segmentasyonu ve kategori performansı için metrikler, ilişkiler ve rapor sayfaları önceden tanımlanır. Bu yaklaşım tutarlı KPI hesaplaması, kolay test ve yönetilebilir performans sağlar.

> Üçüncü senaryo eski planda "kampanya performansı"ydı. Olist veri setinde kampanya verisi ve `campaign_id` kolonu bulunmadığı için senaryo **kategori performansı** olarak düzeltildi.

## 8.2 İleri Faz Yaklaşımı

Metadata destekli ve genişletilebilir bir semantic katman değerlendirilir. Amaç; yeni metrik ve boyutların kolay eklenmesi, generic rapor ekranlarının kullanılması ve sonuç yapısına göre görsel tipinin esnek belirlenmesidir.

> Bu yapı, her soru için yeni bir PBIX oluşturmak anlamına gelmez.

## 8.3 Sonuç Yapısı — Görsel Eşlemesi

| Sonuç Yapısı | Önerilen Görselleştirme |
|--------------|-------------------------|
| Tek değer | KPI kartı |
| Tarih + metrik | Çizgi grafik |
| Kategori + metrik | Bar grafik |
| Birden fazla boyut | Matrix |
| Karmaşık veya detaylı sonuç | Tablo |

## 8.4 RLS Yaklaşımı

- Rol tanımları kullanıcının veri kapsamıyla (şirket, bölge, mağaza) hizalanır
- SQL katmanındaki zorunlu yetki filtresi ile Power BI RLS **birbirinin yedeği**, alternatifi değildir
- Her yetki senaryosu farklı test kullanıcılarıyla doğrulanır
