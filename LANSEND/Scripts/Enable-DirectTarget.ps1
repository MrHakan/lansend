#requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

$folder = Join-Path $env:USERPROFILE "Documents\LANSEND"
$shareName = "LANSEND"
$accountName = "$env:USERDOMAIN\$env:USERNAME"

New-Item -ItemType Directory -Force -Path $folder | Out-Null

$share = Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue
if ($null -eq $share) {
    New-SmbShare -Name $shareName -Path $folder -ChangeAccess $accountName -Description "LANSEND direct file drop" | Out-Null
}
else {
    if ($share.Path -ne $folder) {
        throw "LANSEND paylaşımı başka bir klasöre bağlı: $($share.Path)"
    }

    Grant-SmbShareAccess -Name $shareName -AccountName $accountName -AccessRight Change -Force | Out-Null
}

$firewallRuleName = "LANSEND Direct SMB"
if ($null -eq (Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule `
        -DisplayName $firewallRuleName `
        -Description "Allow direct LANSEND file copies over Windows SMB" `
        -Direction Inbound `
        -Protocol TCP `
        -LocalPort 445 `
        -Profile Private `
        -Action Allow | Out-Null
}
else {
    Set-NetFirewallRule -DisplayName $firewallRuleName -Enabled True -Profile Private -Action Allow | Out-Null
}

Write-Host "Hazır: \\$env:COMPUTERNAME\$shareName"
Write-Host "Hedef klasör: $folder"
Write-Host "LANSEND uygulamasının bu bilgisayarda açık olması gerekmez."
