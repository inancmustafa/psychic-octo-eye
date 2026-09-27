$ErrorActionPreference='Stop'
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$refs=@('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll','WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll')
$compileArgs=@('/nologo','/target:winexe','/platform:anycpu','/optimize+',('/out:'+(Join-Path $PSScriptRoot 'UsageWidget.exe')))
foreach($ref in $refs) { $compileArgs += '/reference:'+(Join-Path $framework $ref) }
$compileArgs += Join-Path $PSScriptRoot 'Widget.cs'
& (Join-Path $framework 'csc.exe') @compileArgs
if($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
