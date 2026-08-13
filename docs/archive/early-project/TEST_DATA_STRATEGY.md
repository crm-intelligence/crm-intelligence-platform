# TEST DATA STRATEGY

## Amaç

Bu dokümanın amacı CRM API uygulamasında kullanılacak test verilerinin nasıl hazırlanacağını açıklamaktır.

---

## Test Veri Türleri

### 1. Geçerli Veriler (Valid Data)

Sistemin beklediği doğru formatta oluşturulan verilerdir.

Örnek:

- Kullanıcı Adı: Ahmet
- E-posta: ahmet@test.com
- Şifre: Test123!

---

### 2. Geçersiz Veriler (Invalid Data)

Kurallara uymayan verilerdir.

Örnek:

- Boş kullanıcı adı
- Hatalı e-posta
- Çok kısa şifre

---

### 3. Sınır Değerleri (Boundary Values)

Minimum ve maksimum uzunluklar test edilir.

Örnek:

- 1 karakter
- 50 karakter
- 255 karakter

---

### 4. Boş Veri Testleri

Alanların boş gönderilmesi test edilir.

Örnek:

- ""
- null

---

### 5. Güvenlik Test Verileri

Sistemin zararlı girişlere karşı dayanıklılığı test edilir.

Örnek:

SQL Injection

```
' OR 1=1 --
```

Cross Site Scripting (XSS)

```html
<script>alert('XSS')</script>
```

---

## Beklenen Sonuç

- Geçerli veriler kabul edilmelidir.
- Geçersiz veriler reddedilmelidir.
- Boş alanlarda uygun hata mesajı verilmelidir.
- Güvenlik saldırıları engellenmelidir.