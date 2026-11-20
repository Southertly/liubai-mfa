param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$CertificateThumbprint,
    [string]$SdkBin = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64"
)

$ErrorActionPreference = 'Stop'
$desktop = Split-Path -Parent $PSScriptRoot
$windows = Split-Path -Parent $desktop
$makeAppx = Join-Path $SdkBin 'makeappx.exe'
$signTool = Join-Path $SdkBin 'signtool.exe'
$manifest = Join-Path $PSScriptRoot 'AppxManifest.xml'
$packageVersion = [version]([xml](Get-Content -LiteralPath $manifest -Raw)).Package.Identity.Version
$packageName = "LiubaiMfa-Preview-$packageVersion-x64.msix"
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
if ($certificate.Subject -ne 'CN=LiubaiMFA-Development' -or -not $certificate.HasPrivateKey) {
    throw '请选择具有私钥的留白 MFA 开发签名证书。'
}
foreach ($requiredFile in @($makeAppx, $signTool)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "缺少打包工具：$requiredFile"
    }
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$staging = Join-Path $tempRoot ('mfa-msix-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    $layout = Join-Path $staging 'layout'
    & (Join-Path $PSScriptRoot 'stage-desktop.ps1') -Staging $staging -Version $packageVersion -Channel preview

    Copy-Item -LiteralPath $manifest -Destination (Join-Path $layout 'AppxManifest.xml')
    $assets = Join-Path $layout 'Assets'
    New-Item -ItemType Directory -Path $assets | Out-Null
    foreach ($asset in @('StoreLogo.png', 'Square150x150Logo.png', 'Square44x44Logo.png')) {
        Copy-Item -LiteralPath (Join-Path $windows "LiubaiMfa.Windows\Assets\$asset") -Destination $assets
    }
    $package = Join-Path $staging $packageName
    & $makeAppx pack /d $layout /p $package
    if ($LASTEXITCODE -ne 0) { throw 'MSIX 打包失败。' }
    & $signTool sign /fd SHA256 /sha1 $CertificateThumbprint $package
    if ($LASTEXITCODE -ne 0) { throw 'MSIX 签名失败。' }
    & $signTool verify /pa $package
    if ($LASTEXITCODE -ne 0) { throw 'MSIX 签名验证失败。' }

    $output = Join-Path $windows 'AppPackages\DesktopPreview'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $destination = Join-Path $output $packageName
    Move-Item -LiteralPath $package -Destination $destination -Force
    Get-Item -LiteralPath $destination
} finally {
    $resolved = (Resolve-Path -LiteralPath $staging).Path
    if ($resolved -eq $staging -and $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
