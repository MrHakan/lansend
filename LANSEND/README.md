# LANSEND

Windows üzerinde aynı yerel ağdaki bilgisayarlar arasında, hedefte LANSEND açık olmadan hızlı dosya paylaşımı.

## Kullanım

1. Dosya alacak her Windows bilgisayarda `Scripts\Enable-DirectTarget.ps1` script'ini yönetici PowerShell ile bir kez çalıştırın.
2. Dosya göndereceğiniz bilgisayarda LANSEND'i çalıştırın; `Send to → LANSEND` kısayolu otomatik oluşturulur.
3. Dosya Gezgini'nde bir dosya veya klasöre sağ tıklayın.
4. `Send to` → `LANSEND` seçin.
5. LANSEND, yerel ağı ARP, Ping ve yaygın servis portlarıyla tarar. Bütün aktif cihazlar listelenir; SMB hazır olanlar ayrıca belirtilir.
6. Hedefi seçip gönderin. Dosyalar doğrudan hedefteki `Documents\LANSEND` klasörüne yazılır; hedef bilgisayarda LANSEND'in açık olması gerekmez.

Otomatik taramada görünmeyen bir cihaz için `IP ile ekle` düğmesiyle IP adresini, cihaz adını ve gerekirse Windows kullanıcı adını kaydedebilirsiniz. `Bu PC'yi alıcı yap` düğmesi mevcut bilgisayardaki paylaşımı tek seferde kurar. İlk erişimde Windows kimlik bilgileri sorulabilir; parola kaydedilmez.

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

Hedef paylaşımını hazırlamak için:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Scripts\Enable-DirectTarget.ps1
```

## Tasarım notları

- Cihaz keşfi ARP tablosu, ICMP Ping ve yaygın TCP servis portlarıyla yapılır; cihaz adı DNS üzerinden çözülür.
- Dosya aktarımı hedefteki standart Windows SMB paylaşımına doğrudan yapılır; hedefte LANSEND alıcı servisi yoktur.
- Geçici `.part` dosyası tamamlanınca hedef ada taşınır.
- Aynı isimde dosya varsa mevcut dosya ezilmez; `(1)`, `(2)` biçiminde yeni ad verilir.
- Cihaz profilleri `%APPDATA%\LANSEND\devices.json` içinde tutulur; parola saklanmaz.
- Uygulama tek örnek çalışır; Send to kısayolu açık örneğe dosya yollarını named pipe üzerinden iletir.
