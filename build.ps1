param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'dist')
)

# Uses the C# compiler that ships with Windows (.NET Framework 4), no SDK needed.
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }

New-Item -ItemType Directory -Force $OutDir | Out-Null
$exe = Join-Path $OutDir 'RobloxMultiInstance.exe'

& $csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$exe" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    (Join-Path $PSScriptRoot 'src\RobloxMultiInstance.cs')
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Write-Host "Built $exe"
