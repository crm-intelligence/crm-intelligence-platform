# 7. Microsoft Fabric Analitik Kapsamı

## 7.1 Demo Analitik Flow'ları

| Analitik Flow | Temel Amaç | Örnek Çıktılar |
|---------------|------------|----------------|
| **Müşteri Segmentasyonu** | Benzer davranış ve değer özelliklerine sahip müşteri grupları oluşturmak | Yüksek değerli, yeni, uyuyan, kampanyaya duyarlı segmentler |
| **Satış Analizi** | Satış performansını bölge, ürün, kategori ve dönem bazında incelemek | Net satış, satış adedi, büyüme, ortalama sepet |
| **Kampanya Performansı** | Kampanyanın satış ve müşteri etkileşimine etkisini değerlendirmek | Dönüşüm, katılım, satış etkisi, segment bazlı sonuç |

## 7.2 Veri Hazırlama Katmanı

- Temizleme: eksik değer, tekrar eden kayıt, tip uyumsuzluğu
- Dönüştürme: tarih normalizasyonu, para birimi, ölçü birleştirme
- Standardizasyon: kurumsal metric tanımlarına uygun hesaplama
- Sonuç tabloları: `requestId` ile ilişkilendirilir, rapor metadata'sı taşır

> **Adlandırma sözleşmesi.** Aynı değer iki katmanda iki farklı yazımla geçer ve bu kasıtlıdır:
> API, JSON sözleşmeleri ve audit kaydı **camelCase** (`requestId`, `conversationId`) kullanır;
> Fabric sonuç tabloları ve SQL kolonları **snake_case** (`request_id`) kullanır.
> Dönüşüm Fabric pipeline'ının giriş adımında yapılır — değer birebir taşınır, yalnızca yazım değişir.
> Yeni bir alan eklenirken bu ayrım korunmalıdır; iki katmanda aynı yazımı kullanmak sözleşmeyi bozar.

## 7.3 İleri Analitik ve Asenkron İşler (ileri faz)

Uzun sürebilecek işlemler arka planda çalışan asenkron işler olarak tasarlanır. Kullanıcı talebi başlatır, Teams'te "hazırlanıyor" bilgisi gösterilir, analiz tamamlandığında bildirim iletilir.

- **Satış tahmini:** ürün, kategori veya bölge bazında gelecek dönem öngörüleri
- **Churn analizi:** müşteri kaybetme riski ve risk skoru
- **Kök neden analizi:** satış düşüşü veya performans sapmasının olası sürücüleri
- **Proaktif uyarılar:** belirlenen eşik ve anomali koşullarında kullanıcı bildirimi
