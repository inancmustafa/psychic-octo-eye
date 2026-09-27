$ErrorActionPreference='Stop'
$runtime = @{}
foreach ($name in @('node','codex','claude')) {
    $found = Get-Command ($name+'.exe') -ErrorAction SilentlyContinue
    if (-not $found) { throw "$name bulunamadı." }
    $runtime[$name+'Path']=$found.Source
}
$runtime | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime.json') -Encoding UTF8
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $PSScriptRoot 'Usage Widget.lnk'))
$shortcut.TargetPath = Join-Path $PSScriptRoot 'UsageWidget.exe'
$shortcut.Arguments = ''
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.WindowStyle = 1
$shortcut.Description = 'Claude ve Codex kullanım kotaları'
$shortcut.IconLocation = (Join-Path $env:SystemRoot 'System32/shell32.dll')+',21'
$shortcut.Save()
Write-Output 'Usage Widget.lnk hazır.'
