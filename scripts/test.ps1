$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repoRoot ('artifacts\tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler was not found.' }
$sources = @('Json.cs','Core.cs','Discovery.cs','App.cs') | ForEach-Object { Join-Path (Join-Path $repoRoot 'src') $_ }
$sources += Join-Path $repoRoot 'tests\Tests.cs'
$testExe = Join-Path $testRoot 'Tests.exe'
$arguments = @('/nologo','/target:exe','/platform:anycpu','/optimize+','/utf8output','/main:EdgeReminder.Tests',"/out:$testExe",
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',
    '/reference:System.Management.dll','/reference:System.Web.Extensions.dll') + $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $testExe (Join-Path $testRoot 'fixtures')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
