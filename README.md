# LANSEND

Fast Windows file sharing over a local network, using direct SMB access.

LANSEND adds a `Send to → LANSEND` entry to Windows File Explorer. The sender discovers normal LAN devices with ARP, ICMP and common TCP service probes, then identifies which devices are ready for direct SMB transfer. Selected files are copied to the target's `Documents\LANSEND` folder. The target computer does not need to keep LANSEND running.

## Workspace

- Solution: `LANSEND.sln`
- Desktop app: `LANSEND/LANSEND.csproj`
- Windows setup scripts: `LANSEND/Scripts/`
- CI build: `.github/workflows/dotnet.yml`

## Local build

Requirements: Windows and .NET 8 SDK.

```powershell
dotnet restore .\LANSEND.sln
dotnet build .\LANSEND.sln -c Release
dotnet publish .\LANSEND\LANSEND.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

For a ready-to-install package, run `LANSEND\Build-LANSEND.ps1` and then execute `Scripts\Install-LANSEND.ps1` from the generated `publish` folder.

On every target Windows computer, run `Scripts\Enable-DirectTarget.ps1` once from an elevated PowerShell, or click `Bu PC'yi alıcı yap` in the app. It creates the `LANSEND` SMB share for the current user's `Documents\LANSEND` folder and enables a private-network TCP `445` firewall rule. This is a one-time Windows configuration; LANSEND itself does not need to stay open on the target.

## Network behavior

- The sender scans local IPv4 networks using ARP, Ping and common service ports; devices no longer need to expose SMB just to appear in the list.
- Large subnets are scanned around the sender's local `/24` range to keep discovery fast.
- Devices can also be added manually by IP address or computer name.
- `Send to → LANSEND` is created automatically whenever the application starts.
- Files are copied to the target's `\\IP\LANSEND` SMB share.
- The sender first tries the current Windows credentials. If access is denied, it asks for SMB credentials for that transfer and does not save the password.
- Device profiles are stored in `%APPDATA%\LANSEND\devices.json` without passwords.
- Files are written below the target's `Documents\LANSEND` folder only when the target share is configured that way.

Use LANSEND only on a trusted private LAN. The first version is intentionally LAN-only and does not expose a cloud relay.
