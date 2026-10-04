# Usage Notch'u tek adımda hazırlar ve başlatır; Baslat.bat bu betiği çağırır.
# Hiçbir şey indirmez; derleme için Windows'taki .NET Framework 4.8, giriş için Claude masaüstü uygulamasındaki Claude Code kullanılır.
# Giriş süresi dolduğunda (kartta "giriş gerekli") yeniden çalıştırmak yeterlidir; hazır olan adımlar atlanır.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe = Join-Path $root 'UsageWidget.exe'
$stateDir = Join-Path $env:LOCALAPPDATA 'UsageWidget'
$claudeHome = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
$credentials = Join-Path $claudeHome '.credentials.json'
New-Item -ItemType Directory -Force $stateDir | Out-Null
# Son çalıştırmanın çıktısı; anahtar yazdırılmadığı için log'da gizli bilgi yoktur.
$logging = $false
try { Start-Transcript -LiteralPath (Join-Path $stateDir 'baslat.log') | Out-Null; $logging = $true } catch { }

function Step([string]$text) { Write-Host ''; Write-Host "==> $text" -ForegroundColor Cyan }
function Ok([string]$text) { Write-Host "    $text" -ForegroundColor Green }
function Warn([string]$text) { Write-Host "    $text" -ForegroundColor Yellow }
function Info([string]$text) { Write-Host "    $text" }

# PATH'te claude.exe yoksa Claude masaüstü uygulamasının içindeki en yeni Claude Code kullanılır.
function FindClaude {
    $onPath = Get-Command claude.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $bundled = Join-Path $env:APPDATA 'Claude\claude-code'
    if (-not (Test-Path $bundled)) { return $null }
    $best = Get-ChildItem $bundled -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'claude.exe') } |
        Sort-Object { $v = $null; if ([version]::TryParse($_.Name, [ref]$v)) { $v } else { [version]'0.0' } } -Descending |
        Select-Object -First 1
    if ($best) { return Join-Path $best.FullName 'claude.exe' }
    return $null
}

# Yalnızca bitiş zamanı okunur; anahtarın kendisi ekrana yazılmaz.
function TokenExpiry {
    try {
        $oauth = (Get-Content -Raw -LiteralPath $credentials | ConvertFrom-Json).claudeAiOauth
        if ($oauth -and $oauth.accessToken -and $oauth.expiresAt) { return [DateTimeOffset]::FromUnixTimeMilliseconds([int64]$oauth.expiresAt).LocalDateTime }
    } catch { }
    return $null
}

# Widget son ölçümde 401 aldıysa ve anahtar o zamandan beri değişmediyse bitiş zamanı ileride olsa da giriş gerekir;
# yoksa widget'ın açtığı otomatik Baslat girişi atlar ve widget bir sonraki ölçümde yine reddedilir.
function TokenRejected {
    try {
        $last = (Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $stateDir 'usage.json') | ConvertFrom-Json).claude
        if ($last.status -ne 'login_required') { return $false }
        $attempted = [DateTimeOffset]::FromUnixTimeMilliseconds([int64]$last.attemptedAt).UtcDateTime
        return (Get-Item -LiteralPath $credentials).LastWriteTimeUtc -lt $attempted
    } catch { return $false }
}

# Önce nazikçe kapatılır (tercihler kaydedilsin), 5 sn içinde kapanmazsa zorla.
function StopWidget {
    $running = @(Get-Process UsageWidget -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) { Ok 'Çalışan widget yok.' }
    foreach ($p in $running) {
        if ($p.CloseMainWindow()) { $null = $p.WaitForExit(5000) }
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; $null = $p.WaitForExit(5000) }
        Ok "Çalışan widget kapatıldı (PID $($p.Id))."
    }
}

try {
    $claude = FindClaude
    # Setup.ps1 claude yolunu PATH'ten okur; yalnızca bu oturum için eklenir.
    if ($claude) { $env:Path = (Split-Path $claude) + ';' + $env:Path }

    Step '[1/5] Çalışan widget'
    StopWidget

    Step '[2/5] Derleme'
    $inputs = @(Get-ChildItem $root -Filter *.cs) + @(Get-Item (Join-Path $root 'Build.ps1'))
    $newest = $inputs | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ((Test-Path $exe) -and $newest.LastWriteTime -le (Get-Item $exe).LastWriteTime) { Ok 'UsageWidget.exe güncel, derleme atlandı.' }
    else { & (Join-Path $root 'Build.ps1') }

    Step '[3/5] Kurulum (runtime.json + kısayol)'
    & (Join-Path $root 'Setup.ps1')

    Step '[4/5] Claude girişi'
    $expiry = TokenExpiry
    $rejected = TokenRejected
    if ($expiry -and $expiry -gt (Get-Date).AddMinutes(30) -and -not $rejected) { Ok "Giriş geçerli (bitiş: $($expiry.ToString('dd.MM.yyyy HH:mm')))." }
    else {
        if (-not $claude) { throw 'claude.exe bulunamadı (ne PATH''te ne Claude masaüstü uygulamasında).' }
        if ($rejected) { Info 'Anthropic son ölçümde girişi reddetti; tarayıcıda Claude girişi açılacak. Masaüstünde kullandığın hesabı seç.' }
        else { Info 'Giriş süresi dolmuş; tarayıcıda Claude girişi açılacak. Masaüstünde kullandığın hesabı seç.' }
        & $claude auth login --claudeai
        $expiry = TokenExpiry
        if (-not $expiry -or $expiry -le (Get-Date)) { throw 'Giriş tamamlanmadı; Baslat.bat dosyasını yeniden çalıştır.' }
        Ok "Giriş yenilendi (bitiş: $($expiry.ToString('dd.MM.yyyy HH:mm')))."
    }

    Step '[5/5] Başlatma ve ilk ölçüm'
    $usagePath = Join-Path $stateDir 'usage.json'
    $errorLog = Join-Path $stateDir 'ui-error.log'
    $started = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $startedAt = Get-Date
    # --refresh: widget açılır açılmaz beklemeden ölçüm alır
    $proc = Start-Process -FilePath $exe -ArgumentList '--refresh' -WorkingDirectory $root -PassThru
    Info 'Widget açıldı; ilk ölçüm bekleniyor (en çok 30 sn)…'
    $usage = $null
    for ($i = 0; $i -lt 30 -and -not $usage -and -not $proc.HasExited; $i++) {
        Start-Sleep -Seconds 1
        try { $data = Get-Content -Raw -Encoding UTF8 -LiteralPath $usagePath | ConvertFrom-Json; if ([int64]$data.writtenAt -ge $started) { $usage = $data.claude } } catch { }
    }
    if ($proc.HasExited) {
        $detail = if ((Test-Path $errorLog) -and (Get-Item $errorLog).LastWriteTime -ge $startedAt) { ' ' + (Get-Content $errorLog -TotalCount 1) } else { '' }
        throw "Widget açılır açılmaz kapandı.$detail"
    }
    switch ($usage.status) {
        'ok' { Ok ('Kota okundu: ' + (($usage.windows | ForEach-Object { "$($_.label) %$([math]::Round($_.usedPercent))" }) -join ' · ')) }
        'login_required' { Warn 'Anthropic girişi kabul etmedi; Baslat.bat dosyasını yeniden çalıştırıp girişi tamamla.' }
        'forbidden' { Warn 'Bu hesabın kota okuma izni yok; Claude aboneliği olan hesapla giriş yap.' }
        'no_quota' { Warn 'Hesap için kota bilgisi dönmedi.' }
        'stale' {
            if ($usage.error -eq 'rate_limited') { Warn 'Anthropic çok sık istek uyarısı verdi; widget birkaç dakika sonra kendisi yeniden dener.' }
            else { Warn "api.anthropic.com'a bağlanılamadı ($($usage.error)); ağ veya güvenlik duvarını kontrol et." }
        }
        default { Warn 'Ölçüm 30 sn içinde gelmedi; açık çentikteki ↻ ile yenile.' }
    }
    Ok 'Usage Notch çalışıyor: ekranın üst kenarındaki çentik ve sistem tepsisindeki halka simgesi.'
}
catch {
    Write-Host ''
    Write-Host "HATA: $($_.Exception.Message)" -ForegroundColor Red
    $failed = $true
}
finally {
    if ($logging) { Stop-Transcript | Out-Null }
}
if ($failed) { exit 1 }
