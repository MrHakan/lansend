# LANSEND

Fast Windows file sharing over a local network.

LANSEND adds a `Send to → LANSEND` entry to Windows File Explorer. After selecting a target device, files are transferred directly to the receiving device's `Documents\LANSEND` folder.

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

## Network behavior

- UDP `43821`: device discovery
- TCP `43822`: file transfers
- Files are written below `Documents\LANSEND` only.
- Incoming transfers require confirmation by default.

Use LANSEND only on a trusted private LAN. The first version is intentionally LAN-only and does not expose a cloud relay.
