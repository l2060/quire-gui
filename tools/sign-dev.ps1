<#
.SYNOPSIS
    为 Quire Studio 生成开发用代码签名并签名可执行文件。

.DESCRIPTION
    Windows 对没有有效签名的可执行文件可能弹出 SmartScreen「Windows 已保护你的电脑」。
    本脚本在当前用户范围内：

      1. 创建（或复用）一张自签名代码签名证书；
      2. 把证书装入「受信任的根证书颁发机构」与「受信任的发布者」；
      3. 用它对 QuireStudio.exe 签名。

    证书只装进当前用户，不影响其他账户；用 -Remove 可以整体撤销。

.PARAMETER Exe
    要签名的可执行文件，默认 Release 输出。

.PARAMETER Remove
    撤销：删除证书存储中的条目并移除本地证书。

.EXAMPLE
    pwsh -File tools/sign-dev.ps1
    pwsh -File tools/sign-dev.ps1 -Exe src/QuireStudio/bin/Debug/net10.0/QuireStudio.exe
#>
[CmdletBinding()]
param(
    [string]$Exe = "src/QuireStudio/bin/Release/net10.0/QuireStudio.exe",
    [string]$Subject = "CN=Quire Studio Dev, O=Quire Studio, C=CN",
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'

if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') {
    throw "代码签名仅适用于 Windows。"
}

$storeNames = @('Root', 'TrustedPublisher', 'My')

function Get-DevelopmentCertificate {
    param([string]$CertificateSubject)

    return Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $CertificateSubject } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
}

function Uninstall-Certificate {
    param([string]$CertificateSubject)

    $certificate = Get-DevelopmentCertificate -CertificateSubject $CertificateSubject
    if (-not $certificate) {
        Write-Host "未找到开发证书，无需清理。"
        return
    }

    foreach ($name in $storeNames) {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($name, 'CurrentUser')
        $store.Open('ReadWrite')
        foreach ($item in @($store.Certificates | Where-Object { $_.Thumbprint -eq $certificate.Thumbprint })) {
            $store.Remove($item)
        }
        $store.Close()
    }

    Write-Host "已移除证书 $($certificate.Thumbprint)。"
}

if ($Remove) {
    Uninstall-Certificate -CertificateSubject $Subject
    return
}

if (-not (Test-Path $Exe)) {
    throw "找不到可执行文件：$Exe。请先执行 dotnet build -c Release。"
}

$Exe = (Resolve-Path $Exe).Path

$certificate = Get-DevelopmentCertificate -CertificateSubject $Subject
if (-not $certificate) {
    Write-Host "创建自签名代码签名证书…"

    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -FriendlyName "Quire Studio Development Signing" `
        -KeyUsage DigitalSignature `
        -KeyExportPolicy Exportable `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(5) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')
}

foreach ($name in @('Root', 'TrustedPublisher')) {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($name, 'CurrentUser')
    $store.Open('ReadWrite')

    if (-not ($store.Certificates | Where-Object { $_.Thumbprint -eq $certificate.Thumbprint })) {
        $store.Add($certificate)
        Write-Host "已将证书加入当前用户的 $name 存储。"
    }

    $store.Close()
}

Write-Host "正在签名 $Exe …"

try {
    $signature = Set-AuthenticodeSignature -FilePath $Exe -Certificate $certificate `
        -TimestampServer "http://timestamp.digicert.com" -ErrorAction Stop
}
catch {
    # 无网络时退化为不带时间戳的签名，本机验证仍然有效。
    $signature = Set-AuthenticodeSignature -FilePath $Exe -Certificate $certificate
}

Write-Host "签名状态：$($signature.Status)"
Write-Host "证书指纹：$($certificate.Thumbprint)"

if ($signature.Status -ne 'Valid') {
    Write-Warning "签名未通过验证：$($signature.StatusMessage)"
    exit 1
}
