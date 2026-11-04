param([ValidateSet('2022', '2023', '2024')][string]$RevitVersion = '2023', [string]$RevitApiPath = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $project = 'src/KetCauViet.Revit/KetCauViet.Revit.csproj'
    $properties = @("-p:RevitVersion=$RevitVersion")
    if ($RevitApiPath) { $properties += "-p:RevitApiPath=$RevitApiPath" }
    if ($RevitApiPath) { & dotnet restore $project @properties }
    else { & dotnet restore $project @properties --locked-mode }
    if ($LASTEXITCODE -ne 0) { throw 'Khôi phục dependency thất bại.' }
    & dotnet build $project -c Release --no-restore @properties
    if ($LASTEXITCODE -ne 0) { throw 'Build plugin thất bại.' }
    & dotnet test tests/KetCauViet.Core.Tests/KetCauViet.Core.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Kiểm thử thất bại.' }
    Set-Content -Path "artifacts/$RevitVersion/revit-version.txt" -Value $RevitVersion -Encoding UTF8
    $package = "artifacts/package-$RevitVersion"
    New-Item -ItemType Directory -Force "$package/KetCauViet" | Out-Null
    Copy-Item "artifacts/$RevitVersion/*.dll", "artifacts/$RevitVersion/HUONG-DAN.md", "artifacts/$RevitVersion/revit-version.txt" "$package/KetCauViet/" -Force
    Copy-Item 'docs' "$package/KetCauViet/" -Recurse -Force
    Copy-Item 'scripts/Install.ps1' "$package/Install.ps1" -Force
    Copy-Item 'installer/KetCauViet.addin' "$package/KetCauViet.addin" -Force
    Copy-Item 'HUONG-DAN.md' "$package/HUONG-DAN.md" -Force
    Copy-Item 'docs' "$package/" -Recurse -Force
    Compress-Archive -Path "$package/*" -DestinationPath "artifacts/KetCauViet-Revit$RevitVersion.zip" -Force
    Write-Host "Gói cài: artifacts/KetCauViet-Revit$RevitVersion.zip"
} finally { Pop-Location }
