# 9. Backend ve Microsoft Teams Entegrasyonu

ASP.NET Core API; Teams'ten gelen talebi alır, kullanıcı kimliğini doğrular, konuşma bağlamını ve önceki rapor referansını saklar, ilgili servisleri çağırır ve sonuçları kullanıcıya döndürür.

## 9.1 Temel Teknik Sorumluluklar

- ASP.NET Core API endpoint'leri ve servis katmanının geliştirilmesi
- Microsoft Teams uygulaması veya bot entegrasyonu
- Conversation ID ve Previous Request ID ile bağlam yönetimi
- Rapor revizyonu ve takip sorusu akışının yönetimi
- İstek durumu, hata yönetimi, retry ve loglama
- Teams'e kısa sonuç, durum bilgisi ve Power BI bağlantısı gönderimi

## 9.2 Durum Modeli

| Durum | Anlamı | Kullanıcı Mesajı |
|-------|--------|------------------|
| `Received` | Talep API tarafından alındı | Talebiniz alındı. |
| `Processing` | Sorgu ve analiz akışı devam ediyor | Rapor hazırlanıyor. |
| `Completed` | Sonuç üretildi | Özet ve Power BI bağlantısı |
| `NeedsClarification` | Talep belirsiz | Kullanıcıdan ek bilgi istenir |
| `Rejected` | Yetki veya güvenlik kuralı nedeniyle çalıştırılamadı | Açıklanabilir kontrollü mesaj |
| `Failed` | Teknik hata oluştu | Takip edilebilir hata ve yeniden deneme |

## 9.3 Endpoint Taslağı

```
POST /api/report/request
  body : { prompt, conversationId, previousRequestId? }
  → 202 { requestId, status: "Received" }

GET  /api/report/status/{requestId}
  → 200 { requestId, status, summary?, reportUrl?, rejectionReason? }

POST /api/report/{requestId}/revise
  body : { prompt }
  → 202 { requestId, status: "Received" }
```
