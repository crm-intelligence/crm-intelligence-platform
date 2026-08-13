# 2. Ürün Deneyimi ve Fonksiyonel Kapsam

## 2.1 Temel Kullanıcı Akışı

`1. Sor` → `2. Anla` → `3. Güvenli Sorgula` → `4. Analiz Et` → `5. Raporla`

**Örnek talep:** "Marmara ve Ege bölgelerinin son çeyrek satışlarını karşılaştır."

**Örnek takip soruları:**
- "Satış adedi yerine net kârı göster"
- "Bölge yerine mağaza bazında grupla"
- "Sadece kampanyalı siparişleri dahil et"

Takip sorusu yeni bir sorgu değil, **mevcut raporun revizyonudur.** Conversation ID ve Previous Request ID ile önceki talep referans alınır.

## 2.2 Demo Kapsamı (bu fazda yapılacak)

- Microsoft Teams üzerinden rapor talebi alma
- Konuşma kimliği ve önceki talep referansı ile bağlam takibi
- Tanımlı rapor senaryolarında Query Builder kullanımı
- Sınırlı ve izinli ad hoc taleplerde NL2SQL PoC
- DWH ve OLTP kaynaklarından read-only veri erişimi
- Fabric üzerinde müşteri segmentasyonu, satış analizi ve kategori performansı flow'ları
  (üçüncü senaryo eski planda "kampanya performansı"ydı; Olist veri setinde kampanya
  verisi ve `campaign_id` kolonu yok, senaryo **kategori performansı** olarak düzeltildi)
- Power BI üzerinde sabit semantic model ve hazır rapor sayfaları
- Teams içinde özet sonuç ve Power BI bağlantısı

## 2.3 İleri Faz Kapsamı (bu fazda yapılmayacak)

- Satış tahmini ve churn analizi gibi uzun süren asenkron analitik işler
- Kök neden analizi ve proaktif bildirimler
- Metadata destekli genişletilebilir semantic katman
- Generic rapor ekranları ve daha dinamik görsel seçimleri
- Sık kullanılan ad hoc analizlerin zamanla ürünleştirilmesi
