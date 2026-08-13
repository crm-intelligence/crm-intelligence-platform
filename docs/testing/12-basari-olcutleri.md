# 12. Başarı Ölçütleri ve Kabul Kriterleri

Aşağıdaki kriterler başlangıç çerçevesidir; gerçek hedef değerler veri hacmi ve ortam performansı görüldükten sonra kesinleşir.

| Alan | Önerilen Kriter | Doğrulama Yöntemi |
|------|-----------------|-------------------|
| Fonksiyonel | Üç demo use case'inin uçtan uca çalışması | Senaryo bazlı demo ve UAT |
| Bağlam | Takip sorusunun önceki raporu doğru revize etmesi | Conversation ve previous request testi |
| Güvenlik | Yetkisiz tablo, kolon veya veri kapsamına erişilememesi | Negatif güvenlik testleri |
| SQL | Veri değiştiren sorguların reddedilmesi | Guardrail test paketi |
| Rapor | Power BI RLS ile doğru veri kapsamının gösterilmesi | Farklı rol testleri |
| İzlenebilirlik | Her talebin request ID ile uçtan uca izlenebilmesi | Audit ve Application Insights kaydı |
| Performans | Standart demo senaryolarında kabul edilebilir yanıt süresi | Ortam bazlı yük ve süre ölçümü |
