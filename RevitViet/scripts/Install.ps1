param(
    [ValidateSet('2022', '2023', '2024')][string]$RevitVersion = '2024',
    [switch]$Build
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Plugin Revit phải cài trên Windows.' }
if (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Hãy lưu dự án và đóng Revit trước khi cài.' }
if ($Build) { & (Join-Path $PSScriptRoot 'Build.ps1') -RevitVersion $RevitVersion }

$payload = Join-Path $PSScriptRoot 'KetCauViet'
if (-not (Test-Path $payload)) { $payload = Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts/$RevitVersion" }
if (-not (Test-Path (Join-Path $payload 'KetCauViet.Revit.dll'))) { throw 'Thiếu DLL. Chạy Build.ps1 hoặc giải nén đầy đủ gói phát hành.' }
$versionFile = Join-Path $payload 'revit-version.txt'
if (-not (Test-Path $versionFile)) { throw 'Thiếu revit-version.txt; cần build lại bằng Build.ps1.' }
if ((Get-Content $versionFile -Raw).Trim() -ne $RevitVersion) { throw 'Gói này được build cho phiên bản Revit khác.' }

$addinRoot = Join-Path $env:APPDATA "Autodesk/Revit/Addins/$RevitVersion"
$target = Join-Path $addinRoot 'KetCauViet'
New-Item -ItemType Directory -Force $target | Out-Null
Get-ChildItem -File $payload | Where-Object { $_.Name -notin @('RevitAPI.dll', 'RevitAPIUI.dll') } | Copy-Item -Destination $target -Force
if (Test-Path (Join-Path $payload 'docs')) {
    $docsTarget = Join-Path $target 'docs'
    New-Item -ItemType Directory -Force $docsTarget | Out-Null
    Get-ChildItem -File (Join-Path $payload 'docs') | Copy-Item -Destination $docsTarget -Force
}

$manifest = New-Object System.Xml.XmlDocument
$null = $manifest.AppendChild($manifest.CreateXmlDeclaration('1.0', 'utf-8', $null))
$root = $manifest.CreateElement('RevitAddIns'); $null = $manifest.AppendChild($root)
$addin = $manifest.CreateElement('AddIn'); $addin.SetAttribute('Type', 'Application'); $null = $root.AppendChild($addin)
$values = [ordered]@{
    Name = 'Kết Cấu Việt'
    Assembly = (Join-Path $target 'KetCauViet.Revit.dll')
    AddInId = '8B8BB7E9-5A18-473F-A86F-5B55E5949B52'
    FullClassName = 'KetCauViet.Revit.Application'
    VendorId = 'KCVT'
    VendorDescription = 'Công cụ kết cấu và khối lượng tiếng Việt'
}
foreach ($entry in $values.GetEnumerator()) { $node = $manifest.CreateElement($entry.Key); $node.InnerText = $entry.Value; $null = $addin.AppendChild($node) }
$manifest.Save((Join-Path $addinRoot 'KetCauViet.addin'))
Write-Host "Đã cài cho Revit $RevitVersion. Mở Revit và chọn tab KẾT CẤU VIỆT." -ForegroundColor Green
