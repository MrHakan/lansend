$ErrorActionPreference = "Stop"

$sendToShortcut = Join-Path $env:APPDATA "Microsoft\Windows\SendTo\LANSEND.lnk"
$startupShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\LANSEND.lnk"

foreach ($path in @($sendToShortcut, $startupShortcut)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

Remove-NetFirewallRule -DisplayName "LANSEND Discovery" -ErrorAction SilentlyContinue
Remove-NetFirewallRule -DisplayName "LANSEND Transfers" -ErrorAction SilentlyContinue

Write-Host "LANSEND Send to ve başlangıç kısayolları kaldırıldı."
