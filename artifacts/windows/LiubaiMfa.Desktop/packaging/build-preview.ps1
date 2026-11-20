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
$electron = Join-Path $desktop 'node_modules\electron\dist'
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

$electronExecutable = Join-Path $electron 'electron.exe'
if (-not (Test-Path -LiteralPath $electronExecutable -PathType Leaf)) {
    $installElectron = Join-Path $desktop 'node_modules\electron\install.js'
    if (-not (Test-Path -LiteralPath $installElectron -PathType Leaf)) {
        throw '请先运行 npm ci 安装 Electron 开发依赖。'
    }
    node $installElectron
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $electronExecutable -PathType Leaf)) {
        throw '未能安装 Electron 运行时。'
    }
}

Push-Location $desktop
try {
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Electron 构建失败。' }
} finally {
    Pop-Location
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$staging = Join-Path $tempRoot ('mfa-msix-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    $layout = Join-Path $staging 'layout'
    New-Item -ItemType Directory -Path $layout | Out-Null
    Copy-Item -Path (Join-Path $electron '*') -Destination $layout -Recurse
    Rename-Item -LiteralPath (Join-Path $layout 'electron.exe') -NewName 'LiubaiMfaPreview.exe'
    Rename-Item -LiteralPath (Join-Path $layout 'electron_wer.dll') -NewName 'LiubaiMfaPreview_wer.dll'
    Remove-Item -LiteralPath (Join-Path $layout 'resources\default_app.asar')

    $application = Join-Path $layout 'resources\app'
    $main = Join-Path $application 'dist\main'
    New-Item -ItemType Directory -Path $main | Out-Null
    $metadata = @{ name = 'LiubaiMfaDesktopPreview'; version = $packageVersion.ToString(); main = 'dist/main/main.js' }
    $metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $application 'package.json') -Encoding ascii
    foreach ($file in @('main.js', 'core-bridge.js', 'qr-import.js', 'auto-lock.js', 'windows-updates.js')) {
        Copy-Item -LiteralPath (Join-Path $desktop "dist\main\$file") -Destination $main
    }
    foreach ($folder in @('preload', 'renderer')) {
        Copy-Item -LiteralPath (Join-Path $desktop "dist\$folder") -Destination (Join-Path $application 'dist') -Recurse
    }

    $core = Join-Path $layout 'resources\core'
    # 用 .NET 8 SDK 自带的运行时版本，避免覆盖 Windows SDK 的框架引用版本。
    '{"sdk":{"version":"8.0.100","rollForward":"latestFeature"}}' |
        Set-Content -LiteralPath (Join-Path $staging 'global.json') -Encoding ascii
    Push-Location $staging
    try {
        dotnet publish (Join-Path $windows 'LiubaiMfa.CoreHost\LiubaiMfa.CoreHost.csproj') -c Release -r win-x64 --self-contained true `
            -p:DisableTransitiveFrameworkReferenceDownloads=true -p:WindowsSdkPackageVersion=10.0.19041.57 `
            -p:DebugType=None -p:DebugSymbols=false -o $core
        if ($LASTEXITCODE -ne 0) { throw '.NET 自包含宿主发布失败。' }
    } finally {
        Pop-Location
    }

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
