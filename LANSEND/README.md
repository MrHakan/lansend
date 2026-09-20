# LANSEND

Windows üzerinde aynı yerel ağdaki bilgisayarlar arasında hızlı dosya paylaşımı.

## Kullanım

1. Her Windows cihazda LANSEND'i çalıştırın.
2. İlk kurulumdan sonra uygulama arka planda tepsi simgesi olarak çalışır.
3. Dosya Gezgini'nde bir dosya veya klasöre sağ tıklayın.
4. `Send to` → `LANSEND` seçin.
5. Açılan pencerede cihaz adı, Windows kullanıcı adı ve IP adresiyle hedef bilgisayarı seçin.
6. Alıcı onay verirse dosyalar alıcıdaki `Documents\LANSEND` klasörüne yazılır.

## Geliştirme ve yayınlama

Windows üzerinde .NET 8 SDK ile doğrudan proje klasöründe:

```powershell
dotnet build .\LANSEND\LANSEND.csproj -c Release
dotnet publish .\LANSEND\LANSEND.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

Alternatif olarak proje klasöründeki `Build-LANSEND.ps1` script'ini çalıştırabilirsiniz. Script `publish` klasörüne tek dosyalık `LANSEND.exe` ve kurulum script'lerini hazırlar.

`publish` klasöründeki `LANSEND.exe` ve `Scripts` klasörünü aynı klasörde tutup aşağıdaki komutla kurulumu yapın:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Scripts\Install-LANSEND.ps1 -AppPath .\LANSEND.exe
```

Kurulum script'i:

- `%APPDATA%\Microsoft\Windows\SendTo\LANSEND.lnk` kısayolunu oluşturur.
- Windows başlangıcına arka plan kısayolu ekler.
- Private network profili için UDP `43821` keşif ve TCP `43822` aktarım portlarını açmayı dener.

## Tasarım notları

- Cihaz keşfi UDP broadcast ile yapılır; listede cihaz adı, kullanıcı adı ve IP adresi gösterilir.
- Dosya aktarımı TCP üzerinden yapılır ve büyük dosyalarda akış halinde ilerler.
- Alınan dosyalar geçici `.part` dosyasına yazılır, tamamlanınca hedef ada taşınır.
- Aynı isimde dosya varsa mevcut dosya ezilmez; `(1)`, `(2)` biçiminde yeni ad verilir.
- Gelen aktarım varsayılan olarak karşı cihazın onayını gerektirir.
- Uygulama tek örnek çalışır; Send to kısayolu açık örneğe dosya yollarını named pipe üzerinden iletir.
- Gelen yollar temizlenir ve `Documents\LANSEND` dışına yazılmasına izin verilmez.

Bu sürüm aynı özel LAN üzerinde çalışan ilk Windows prototipidir. Sonraki aşamada cihaz güven eşleştirme, aktarım geçmişi, duraklat/devam et ve Windows paketleme eklenebilir.
