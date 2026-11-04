param([ValidateSet('2022', '2023', '2024')][string]$RevitVersion = '2024')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$package = Join-Path $root "artifacts/package-$RevitVersion"
$installer = Join-Path $package 'Install.ps1'
if (-not (Test-Path $installer)) { throw 'Chạy Build.ps1 trước khi kiểm thử bộ cài.' }

Get-ChildItem (Join-Path $root 'scripts') -Filter '*.ps1' | ForEach-Object {
    $tokens = $null; $parseErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }
}
Write-Host 'PASS: PowerShell parser'

$sandbox = Join-Path $root ('artifacts/installer-test-' + [guid]::NewGuid().ToString('N'))
$originalOs = $env:OS; $originalAppData = $env:APPDATA
function Assert-True([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Expect-Failure([scriptblock]$Action, [string]$Pattern) {
    try { & $Action } catch { if ($_.Exception.Message -like $Pattern) { return }; throw }
    throw 'Expected operation to be rejected.'
}
try {
    New-Item -ItemType Directory -Force $sandbox | Out-Null
    # Model Windows environment paths in a private directory. This verifies files
    # and validation logic; it does not claim to run Revit or Windows itself.
    $env:OS = 'Windows_NT'; $env:APPDATA = Join-Path $sandbox 'profile'
    function Get-Process { [CmdletBinding()]param([string]$Name) return $null }
    & $installer -RevitVersion $RevitVersion
    $addinRoot = Join-Path $env:APPDATA "Autodesk/Revit/Addins/$RevitVersion"
    [xml]$manifest = Get-Content (Join-Path $addinRoot 'KetCauViet.addin') -Raw
    Assert-True ($manifest.RevitAddIns.AddIn.FullClassName -eq 'KetCauViet.Revit.Application') 'Incorrect entry point.'
    Assert-True ($manifest.RevitAddIns.AddIn.AddInId -eq '8B8BB7E9-5A18-473F-A86F-5B55E5949B52') 'Incorrect add-in ID.'
    Assert-True (Test-Path $manifest.RevitAddIns.AddIn.Assembly) 'Assembly path does not resolve.'
    foreach ($name in @('KetCauViet.Revit.dll', 'KetCauViet.Core.dll', 'HUONG-DAN.md', 'docs/KIEM-THU-REVIT.md')) {
        Assert-True (Test-Path (Join-Path $addinRoot "KetCauViet/$name")) "Missing $name"
    }
    Assert-True (-not (Test-Path (Join-Path $addinRoot 'KetCauViet/RevitAPI.dll'))) 'Must not install Autodesk API DLL.'
    $actual = (Get-FileHash $manifest.RevitAddIns.AddIn.Assembly -Algorithm SHA256).Hash
    $expected = (Get-FileHash (Join-Path $package 'KetCauViet/KetCauViet.Revit.dll') -Algorithm SHA256).Hash
    Assert-True ($actual -eq $expected) 'Installed assembly was changed.'
    Write-Host 'PASS: installation files, UTF-8 manifest, assembly identity and documentation'
    & $installer -RevitVersion $RevitVersion
    Assert-True ((Get-FileHash $manifest.RevitAddIns.AddIn.Assembly -Algorithm SHA256).Hash -eq $expected) 'Repeat install changed DLL.'
    Write-Host 'PASS: repeat installation'
    $wrongYear = if ($RevitVersion -eq '2022') { '2024' } else { '2022' }
    Expect-Failure { & $installer -RevitVersion $wrongYear } '*phiên bản Revit khác*'
    Write-Host 'PASS: reject wrong Revit version'
    function Get-Process { [CmdletBinding()]param([string]$Name) return [pscustomobject]@{ ProcessName = 'Revit' } }
    Expect-Failure { & $installer -RevitVersion $RevitVersion } '*đóng Revit*'
    Write-Host 'PASS: reject running Revit'
    $env:OS = 'Linux'
    Expect-Failure { & $installer -RevitVersion $RevitVersion } '*Windows*'
    Write-Host 'PASS: reject non-Windows host'
} finally {
    $env:OS = $originalOs; $env:APPDATA = $originalAppData
    Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
}
