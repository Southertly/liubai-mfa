param(
    [Parameter(Mandatory = $true)][ValidatePattern('^(0|[1-9]\d{0,2})\.(0|[1-9]\d{0,2})\.(0|[1-9]\d{0,4})$')][string]$Version,
    [ValidatePattern('^[0-9A-Fa-f]{40}$')][string]$CertificateThumbprint,
    [string]$SdkBin = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64"
)

$ErrorActionPreference = 'Stop'
$packageVersion = [version]$Version
if ($packageVersion.Major -gt 255 -or $packageVersion.Minor -gt 255 -or $packageVersion.Build -gt 65535) {
    throw '版本号超出 Windows Installer 支持范围。'
}
$signTool = Join-Path $SdkBin 'signtool.exe'
if ($CertificateThumbprint) {
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
    if (!$certificate.HasPrivateKey -or $certificate.Subject -eq $certificate.Issuer -or
        !(Test-Path -LiteralPath $signTool -PathType Leaf)) {
        throw '正式发布需要受信任的代码签名证书和 SignTool。'
    }
}

$desktop = Split-Path -Parent $PSScriptRoot
$windows = Split-Path -Parent $desktop
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$staging = Join-Path $tempRoot ('mfa-msi-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    & (Join-Path $PSScriptRoot 'stage-desktop.ps1') -Staging $staging -Version $packageVersion -Channel stable
    $layout = Join-Path $staging 'layout'
    $output = Join-Path $staging 'installer'
    dotnet build (Join-Path $PSScriptRoot 'msi\LiubaiMfa.wixproj') -c Release -o $output `
        "-p:ApplicationVersion=$Version" "-p:PayloadDirectory=$layout"
    if ($LASTEXITCODE -ne 0) { throw 'MSI 打包失败。' }
    $packages = @(Get-ChildItem -LiteralPath $output -File -Filter 'LiubaiMfa.msi' -Recurse)
    if ($packages.Count -ne 1) { throw '未生成唯一的 MSI。' }
    $package = $packages[0].FullName
    $suffix = '-unsigned'
    if ($CertificateThumbprint) {
        & $signTool sign /fd SHA256 /sha1 $CertificateThumbprint /tr 'http://timestamp.digicert.com' /td SHA256 $package
        if ($LASTEXITCODE -ne 0) { throw 'MSI 签名失败。' }
        & $signTool verify /pa $package
        if ($LASTEXITCODE -ne 0) { throw 'MSI 签名验证失败。' }
        $suffix = ''
    }

    $destination = Join-Path $windows 'AppPackages\Desktop'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $packagePath = Join-Path $destination "LiubaiMfa-$Version-x64$suffix.msi"
    Move-Item -LiteralPath $package -Destination $packagePath -Force
    Get-Item -LiteralPath $packagePath
} finally {
    $resolved = (Resolve-Path -LiteralPath $staging).Path
    if ($resolved -eq $staging -and $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
