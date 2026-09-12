param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (!$OutputPath) { $OutputPath = Join-Path $repoRoot 'dist\EdgeReminder.exe' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath)) | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler was not found.' }
$sourceRoot = Join-Path $repoRoot 'src'
$source = @('Json.cs','Core.cs','Discovery.cs','App.cs') | ForEach-Object { Join-Path $sourceRoot $_ }
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/debug-','/utf8output',
    "/win32icon:$(Join-Path $sourceRoot 'app.ico')", "/win32manifest:$(Join-Path $sourceRoot 'app.manifest')", "/out:$OutputPath",
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',
    '/reference:System.Management.dll','/reference:System.Web.Extensions.dll') + $source
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
if ((Get-Item -LiteralPath $OutputPath).Length -gt 5MB) { throw 'The EXE exceeds 5 MB.' }
Get-Item -LiteralPath $OutputPath | Select-Object FullName,Length
