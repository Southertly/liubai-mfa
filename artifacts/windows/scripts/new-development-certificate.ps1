$ErrorActionPreference = "Stop"

$certificate = New-SelfSignedCertificate `
    -Type Custom `
    -Subject "CN=LiubaiMFA-Development" `
    -FriendlyName "留白 MFA Windows 开发签名" `
    -KeyUsage DigitalSignature `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")

$outputDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) "local-signing"
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$publicCertificatePath = Join-Path $outputDirectory "LiubaiMFA-Development.cer"
Export-Certificate -Cert $certificate -FilePath $publicCertificatePath | Out-Null

Write-Host "开发证书已创建。"
Write-Host "Thumbprint: $($certificate.Thumbprint)"
Write-Host "公开证书: $publicCertificatePath"
Write-Host "安装测试 MSIX 前，请在测试机上手动将公开证书导入受信任人存储。"
