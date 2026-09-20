param(
    [string] $AppPath = (Join-Path $PSScriptRoot "LANSEND.exe")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $AppPath -PathType Leaf)) {
    throw "LANSEND.exe bulunamadı: $AppPath"
}

$sendToPath = Join-Path $env:APPDATA "Microsoft\Windows\SendTo"
$startupPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup"
New-Item -ItemType Directory -Force -Path $sendToPath | Out-Null
New-Item -ItemType Directory -Force -Path $startupPath | Out-Null

$shell = New-Object -ComObject WScript.Shell

function New-LANSENDShortcut {
    param(
        [string] $ShortcutPath,
        [string] $Arguments
    )

    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $AppPath
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = Split-Path -Parent $AppPath
    $shortcut.IconLocation = "$AppPath,0"
    $shortcut.Description = "Send files with LANSEND"
    $shortcut.Save()
}

New-LANSENDShortcut -ShortcutPath (Join-Path $sendToPath "LANSEND.lnk") -Arguments ""
New-LANSENDShortcut -ShortcutPath (Join-Path $startupPath "LANSEND.lnk") -Arguments "--background"

try {
    New-NetFirewallRule -DisplayName "LANSEND Discovery" -Direction Inbound -Protocol UDP -LocalPort 43821 -Profile Private -Action Allow -ErrorAction Stop | Out-Null
    New-NetFirewallRule -DisplayName "LANSEND Transfers" -Direction Inbound -Protocol TCP -LocalPort 43822 -Profile Private -Action Allow -ErrorAction Stop | Out-Null
    Write-Host "Windows Firewall kuralları eklendi (Private network)."
}
catch {
    Write-Warning "Firewall kuralları eklenemedi. Yönetici PowerShell ile tekrar çalıştırabilirsiniz."
}

Write-Host "LANSEND Send to entegrasyonu kuruldu."
Write-Host "Uygulama: $AppPath"
