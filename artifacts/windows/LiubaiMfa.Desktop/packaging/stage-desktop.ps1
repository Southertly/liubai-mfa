param(
    [Parameter(Mandatory = $true)][string]$Staging,
    [Parameter(Mandatory = $true)][version]$Version,
    [Parameter(Mandatory = $true)][ValidateSet('preview', 'stable')][string]$Channel
)

$ErrorActionPreference = 'Stop'
$desktop = Split-Path -Parent $PSScriptRoot
$windows = Split-Path -Parent $desktop
$electron = Join-Path $desktop 'node_modules\electron\dist'
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

$layout = Join-Path $Staging 'layout'
if (Test-Path -LiteralPath $layout) { throw '构建目录必须为空。' }
New-Item -ItemType Directory -Path $layout | Out-Null
$executable = if ($Channel -eq 'stable') { 'LiubaiMfa' } else { 'LiubaiMfaPreview' }
$appName = if ($Channel -eq 'stable') { 'LiubaiMfa' } else { 'LiubaiMfaDesktopPreview' }
Copy-Item -Path (Join-Path $electron '*') -Destination $layout -Recurse
Rename-Item -LiteralPath (Join-Path $layout 'electron.exe') -NewName "$executable.exe"
$iconArguments = @((Join-Path $PSScriptRoot 'set-app-icon.mjs'), (Join-Path $layout "$executable.exe"))
if ($Channel -eq 'stable') { $iconArguments += (Join-Path $Staging 'LiubaiMfaShortcut.exe') }
node @iconArguments
if ($LASTEXITCODE -ne 0) { throw '无法嵌入留白 MFA 应用图标。' }
Rename-Item -LiteralPath (Join-Path $layout 'electron_wer.dll') -NewName "${executable}_wer.dll"
Remove-Item -LiteralPath (Join-Path $layout 'resources\default_app.asar')

$application = Join-Path $layout 'resources\app'
$main = Join-Path $application 'dist\main'
New-Item -ItemType Directory -Path $main | Out-Null
@{name = $appName; version = $Version.ToString(); main = 'dist/main/main.js'} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $application 'package.json') -Encoding ascii
foreach ($file in @('main.js', 'core-bridge.js', 'qr-import.js', 'auto-lock.js', 'windows-updates.js', 'windows-installer.js')) {
    Copy-Item -LiteralPath (Join-Path $desktop "dist\main\$file") -Destination $main
}
foreach ($folder in @('preload', 'renderer')) {
    Copy-Item -LiteralPath (Join-Path $desktop "dist\$folder") -Destination (Join-Path $application 'dist') -Recurse
}

$core = Join-Path $layout 'resources\core'
# 用 .NET 8 SDK 自带的运行时版本，避免覆盖 Windows SDK 的框架引用版本。
'{"sdk":{"version":"8.0.100","rollForward":"latestFeature"}}' |
    Set-Content -LiteralPath (Join-Path $Staging 'global.json') -Encoding ascii
Push-Location $Staging
try {
    dotnet publish (Join-Path $windows 'LiubaiMfa.CoreHost\LiubaiMfa.CoreHost.csproj') -c Release -r win-x64 --self-contained true `
        -p:DisableTransitiveFrameworkReferenceDownloads=true -p:WindowsSdkPackageVersion=10.0.19041.57 `
        -p:DebugType=None -p:DebugSymbols=false -o $core
    if ($LASTEXITCODE -ne 0) { throw '.NET 自包含宿主发布失败。' }
} finally {
    Pop-Location
}
