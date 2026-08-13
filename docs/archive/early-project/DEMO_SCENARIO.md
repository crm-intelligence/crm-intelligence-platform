# Hafta 1 İç Demo Senaryosu

## Demo Amacı

Yetkili bir kullanıcının satış talebinin API tarafından güvenli şekilde alınması, doğrulanması, loglanması ve yanıt üretilmesi sürecini göstermek.

## Demo Akışı

1. API çalıştırılır.
2. Swagger açılır.
3. Microsoft Entra ID üzerinden JWT token alınır.
4. Swagger Authorize alanına token girilir.
5. Aşağıdaki istek gönderilir:

```json
{
  "requestId": "demo-001",
  "prompt": "Son çeyrek satışlarını bölgeye göre göster",
  "useCase": "quarterly-sales-by-region",
  "parameters": {},
  "userId": "demo-user",
  "source": "Teams",
  "targetTable": "sales",
  "region": "TR"
}