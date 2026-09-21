# LANSEND

Fast Windows file sharing over a local network, using direct SMB access.

LANSEND adds a `Send to → LANSEND` entry to Windows File Explorer. The sender scans the local IPv4 networks for devices with TCP port `445` open, then copies selected files directly to the target's `Documents\LANSEND` folder. The target computer does not need to run LANSEND.

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

On every target Windows computer, run `Scripts\Enable-DirectTarget.ps1` once from an elevated PowerShell. It creates the `LANSEND` SMB share for the current user's `Documents\LANSEND` folder and enables Windows file-sharing firewall rules. This is a one-time Windows configuration; LANSEND itself does not need to stay open on the target.

## Network behavior

- The sender probes TCP `445` on local IPv4 subnets (up to 512 hosts per subnet).
- Devices can also be added manually by IP address or computer name.
- Files are copied to the target's `\\IP\LANSEND` SMB share.
- The sender first tries the current Windows credentials. If access is denied, it asks for SMB credentials for that transfer and does not save the password.
- Device profiles are stored in `%APPDATA%\LANSEND\devices.json` without passwords.
- Files are written below the target's `Documents\LANSEND` folder only when the target share is configured that way.

Use LANSEND only on a trusted private LAN. The first version is intentionally LAN-only and does not expose a cloud relay.
