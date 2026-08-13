# Smoke Test Raporu

## Proje Bilgileri

**Proje:** CRM Project<br>
**Test Tarihi:** 31.07.2026<br>
**Test Eden:** Lokman Önal<br>
**Branch:** devops<br>

---

## Amaç

Bu smoke testin amacı, sistemin kritik bileşenlerinin temel seviyede çalıştığını doğrulamaktır.

Test kapsamında aşağıdaki bileşenler kontrol edilmiştir:

- API erişimi
- Microsoft Entra ID JWT doğrulaması
- Yetkisiz istek kontrolü
- Güvenlik politikaları
- Audit log kayıtları
- Request ID korelasyonu
- Application Insights telemetrisi

---

## Test Sonuçları

| ID | Test Senaryosu | Beklenen Sonuç | Gerçekleşen Sonuç | Durum |
|----|----------------|----------------|-------------------|-------|
| ST-01 | API sağlık kontrolü | API başarıyla yanıt vermeli | API yanıt verdi | PASS |
| ST-02 | Geçerli JWT token ile istek | İstek kabul edilmeli | İstek kabul edildi | PASS |
| ST-03 | Token olmadan istek | 401 Unauthorized dönmeli | 401 Unauthorized döndü | PASS |
| ST-04 | DELETE sorgusu gönderme | 403 Forbidden dönmeli | 403 Forbidden döndü | PASS |
| ST-05 | Yetkisiz tablo talebi | 403 Forbidden dönmeli | 403 Forbidden döndü | PASS |
| ST-06 | Kapsam dışı bölge talebi | 403 Forbidden dönmeli | 403 Forbidden döndü | PASS |
| ST-07 | Audit log kontrolü | Kullanıcı, karar, kaynak ve sorgu loglanmalı | Audit log oluştu | PASS |
| ST-08 | Request ID kontrolü | Her istekte Request ID bulunmalı | Request ID üretildi | PASS |
| ST-09 | Application Insights kontrolü | İstek telemetride görünmeli | İstek telemetride görüntülendi | PASS |

---

## Test Özeti

**Toplam Test:** 9<br>
**Başarılı Test:** 9<br>
**Başarısız Test:** 0<br>

**Genel Sonuç:** Smoke test başarıyla tamamlandı.

---

## Bulgular

Kritik seviyede herhangi bir hata tespit edilmemiştir.

Aşağıdaki iyileştirme alanları belirlenmiştir:

- Teams entegrasyonunun gerçek ortamda doğrulanması
- Satış verisinin gerçek veri kaynağından alınması
- Power BI rapor linkinin otomatik oluşturulması
- Smoke testlerin otomatik hale getirilmesi

---

## Hafta 1 Çıkış Durumu

Aşağıdaki teknik bileşenler hazır durumdadır:

- Yetkili kullanıcı doğrulaması
- API güvenlik kontrolleri
- Request ID ile istek takibi
- Audit log altyapısı
- Application Insights telemetrisi

Teams üzerinden talep alınması, gerçek satış özetinin oluşturulması ve rapor linkinin kullanıcıya gönderilmesi için entegrasyon çalışmaları devam etmektedir.
