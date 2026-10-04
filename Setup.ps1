$ErrorActionPreference='Stop'
$runtime = @{}
# claude CLI yalnızca karttaki "Claude'u bağla" girişi için kullanılır; kota okuma için gerekmez.
$found = Get-Command 'claude.exe' -ErrorAction SilentlyContinue
if ($found) { $runtime['claudePath']=$found.Source }
else { Write-Warning 'claude bulunamadı; karttaki bağla düğmesi çalışmaz (kota okuma etkilenmez).' }
$runtime | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime.json') -Encoding UTF8
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $PSScriptRoot 'Usage Widget.lnk'))
$shortcut.TargetPath = Join-Path $PSScriptRoot 'UsageWidget.exe'
$shortcut.Arguments = ''
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.WindowStyle = 1
$shortcut.Description = 'Usage Notch · Claude kullanım kotaları'
$shortcut.IconLocation = (Join-Path $env:SystemRoot 'System32/shell32.dll')+',21'
$shortcut.Save()
Write-Output 'Usage Widget.lnk hazır.'
