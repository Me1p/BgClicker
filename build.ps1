param(
    [string]$CertificatePath = '',
    [string]$CertificatePassword = ''
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'

$cscCandidates = @(
    'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $csc) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' 2>$null |
            Select-Object -First 1
        if ($found -and (Test-Path $found)) { $csc = $found }
    }
}

if (-not $csc) {
    $patterns = @(
        'C:\Program Files\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\2019\*\MSBuild\Current\Bin\Roslyn\csc.exe'
    )
    foreach ($pattern in $patterns) {
        $found = Get-ChildItem $pattern -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { $csc = $found.FullName; break }
    }
}

if (-not $csc) { throw 'Roslyn csc.exe not found (Visual Studio Build Tools are required).' }

$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $framework 'System.dll'))) {
    $framework = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319'
}

$references = @(
    'System.dll',
    'System.Core.dll',
    'System.Windows.Forms.dll',
    'System.Drawing.dll',
    'System.Net.Http.dll',
    'System.Web.Extensions.dll'
) | ForEach-Object { '/r:' + (Join-Path $framework $_) }

$sources = Get-ChildItem (Join-Path $src '*.cs') | ForEach-Object { $_.FullName }

New-Item -ItemType Directory -Force $dist | Out-Null

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/debug-',
    '/codepage:65001',
    ('/win32manifest:' + (Join-Path $root 'app.manifest')),
    ('/win32icon:' + (Join-Path $root 'assets\icon.ico')),
    ('/out:' + (Join-Path $dist 'BgClicker.exe'))
) + $references + $sources

& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

Copy-Item (Join-Path $root 'app.config') (Join-Path $dist 'BgClicker.exe.config') -Force
Copy-Item (Join-Path $root 'README.md') (Join-Path $dist 'README.md') -Force
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $dist 'LICENSE') -Force
Copy-Item (Join-Path $root 'docs') (Join-Path $dist 'docs') -Recurse -Force
Copy-Item (Join-Path $root 'assets') (Join-Path $dist 'assets') -Recurse -Force

$exePath = Join-Path $dist 'BgClicker.exe'

if ($CertificatePath) {
    $signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe not found (Windows SDK is required).' }
    & $signtool.FullName sign /fd SHA256 /f $CertificatePath /p $CertificatePassword /tr http://timestamp.digicert.com /td SHA256 $exePath
    if ($LASTEXITCODE -ne 0) { throw "Signing failed with exit code $LASTEXITCODE" }
    Write-Host 'Signed.'
}

$hash = (Get-FileHash $exePath -Algorithm SHA256).Hash
"$hash  BgClicker.exe" | Set-Content (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII

$exe = Get-Item $exePath
Write-Host ("OK: {0} ({1:N0} bytes)" -f $exe.FullName, $exe.Length)
