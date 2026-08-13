# TEST PLAN v1

## 1. Amaç

Bu test planının amacı CRM Project API uygulamasının doğru çalıştığını doğrulamaktır.

---

## 2. Test Kapsamı

Test edilecek bileşenler:

- API Build
- Unit Test
- REST API Endpointleri
- GitHub CI Pipeline

---

## 3. Test Türleri

- Build Test
- Unit Test
- API Test
- Manuel Test

---

## 4. Test Ortamı

- .NET 8
- GitHub Actions
- Windows 11
- Visual Studio Code

---

## 5. Test Senaryoları

### Senaryo 1

**Amaç:** Proje başarılı şekilde derlenmelidir.

**Komut:**
```bash
dotnet build
```

**Beklenen Sonuç:**
Build işlemi başarılı olmalıdır.

---

### Senaryo 2

**Amaç:** Unit testler çalışmalıdır.

**Komut:**
```bash
dotnet test
```

**Beklenen Sonuç:**
Tüm testler başarılı olmalıdır.

---

### Senaryo 3

**Amaç:** GitHub Actions CI Pipeline çalışmalıdır.

**Beklenen Sonuç:**
CI Pipeline yeşil (Success) durumunda olmalıdır.

---

## 6. Başarı Kriterleri

- Build başarılı
- Unit testler başarılı
- GitHub Actions başarılı