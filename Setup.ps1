$ErrorActionPreference='Stop'
$runtime = @{}
# Node zorunlu; Claude ve Codex CLI'den en az biri yeterli (diğeri Ayarlar > Sağlayıcılar'dan kapatılabilir).
foreach ($name in @('node','codex','claude')) {
    $found = Get-Command ($name+'.exe') -ErrorAction SilentlyContinue
    if ($found) { $runtime[$name+'Path']=$found.Source; continue }
    if ($name -eq 'node') { throw 'node bulunamadı.' }
    Write-Warning "$name bulunamadı; bu sağlayıcı bağlanamaz."
}
if (-not $runtime.ContainsKey('codexPath') -and -not $runtime.ContainsKey('claudePath')) { throw 'Ne claude ne codex bulundu.' }
$runtime | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime.json') -Encoding UTF8
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $PSScriptRoot 'Usage Widget.lnk'))
$shortcut.TargetPath = Join-Path $PSScriptRoot 'UsageWidget.exe'
$shortcut.Arguments = ''
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.WindowStyle = 1
$shortcut.Description = 'Usage Notch · Claude ve Codex kullanım kotaları'
$shortcut.IconLocation = (Join-Path $env:SystemRoot 'System32/shell32.dll')+',21'
$shortcut.Save()
Write-Output 'Usage Widget.lnk hazır.'
