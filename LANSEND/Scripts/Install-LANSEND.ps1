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

Write-Host "LANSEND Send to entegrasyonu kuruldu."
Write-Host "Uygulama: $AppPath"
Write-Host "Hedef bilgisayarlarda bir kez yönetici PowerShell ile Enable-DirectTarget.ps1 çalıştırın."
