# 6. Güvenlik, Yetkilendirme ve İzlenebilirlik

| Katman | Kontrol | Örnek Uygulama | Amaç |
|--------|---------|----------------|------|
| Kimlik | Entra ID | Token doğrulama, kullanıcı kimliği | Talebin gerçek kullanıcıya bağlanması |
| Yetki | Rol ve veri kapsamı | Şirket, bölge, mağaza, departman | Kullanıcının yalnızca yetkili veriyi görmesi |
| API | Girdi doğrulama ve loglama | Request ID, hata yönetimi, rate limit | Merkezi kontrol ve izlenebilirlik |
| SQL | Allow-list ve parser | SELECT only, tablo/kolon limiti | Riskli sorguların engellenmesi |
| Veri | Read-only ve minimum veri | Maskeli görünüm, PII azaltma | Veri değişikliğini ve gereksiz erişimi önleme |
| Rapor | Power BI RLS | Kullanıcı bazlı satır güvenliği | Görselleştirme katmanında ek koruma |
| Audit | Uçtan uca kayıt | Kullanıcı, karar, kaynak, rapor erişimi | Denetim ve hata analizi |

## Audit Kaydında Bulunması Gerekenler

- `requestId`, `conversationId`, `previousRequestId`
- Kullanıcı kimliği ve etkin veri kapsamı
- Ham doğal dil talebi
- Canonical request (ayrıştırılmış hali)
- Üretim yolu: `QueryBuilder` | `NL2SQL`
- Üretilen SQL ve guardrail kararı (`Accepted` / `Rejected` + gerekçe kodu)
- Seçilen veri kaynağı (DWH / OLTP)
- Süre, dönen satır sayısı, rapor bağlantısı

## Not

Güvenlik kontrollerinin ayrıntılı kapsamı; kullanılacak Azure servisleri, kurum politikaları, ağ topolojisi ve veri sınıflandırmasına göre uyarlanacaktır. Bu doküman hedef kontrol yaklaşımını tanımlar; nihai konfigürasyon teknik keşif ve kurum onayıyla kesinleşir.
