param(
    [Parameter(Mandatory = $true)]
    [string]$CertificateThumbprint
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "LiubaiMfa.Windows\LiubaiMfa.Windows.csproj"
$certificate = Get-ChildItem "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction Stop

if ($certificate.Subject -ne "CN=LiubaiMFA-Development") {
    throw "证书主题必须与 Package.appxmanifest 的 Publisher 一致：CN=LiubaiMFA-Development"
}

dotnet run --project (Join-Path $projectRoot "LiubaiMfa.Core.Regression\LiubaiMfa.Core.Regression.csproj") -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $project `
    -c Release `
    -p:Platform=x64 `
    -p:RuntimeIdentifier=win-x64 `
    -p:AppxPackageDir="$projectRoot\AppPackages\" `
    -p:UapAppxPackageBuildMode=SideloadOnly `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=true `
    -p:PackageCertificateThumbprint=$CertificateThumbprint `
    -p:AppxBundle=Never
exit $LASTEXITCODE
