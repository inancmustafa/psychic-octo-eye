$ErrorActionPreference='Stop'
# .NET Framework 4.8 ile gelen csc.exe (C# 5) kullanılır; yönetici yetkisi gerekmez.
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$refs=@('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll','WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll')
$sources=@('Widget.cs','WidgetView.cs','Settings.cs','Ui.cs','Sessions.cs','Native.cs')
$compileArgs=@('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',('/out:'+(Join-Path $PSScriptRoot 'UsageWidget.exe')))
foreach($ref in $refs) { $compileArgs += '/reference:'+(Join-Path $framework $ref) }
foreach($source in $sources) { $compileArgs += Join-Path $PSScriptRoot $source }
& (Join-Path $framework 'csc.exe') @compileArgs
if($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
Write-Output 'UsageWidget.exe hazır.'
