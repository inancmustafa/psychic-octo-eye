# Usage Notch (usage-widget · supernotchy-style)

Windows için yerel Claude kota göstergesi; görünüm ve kullanım [Super Notchy](https://supernotchy.com/) (macOS) örnek alınarak yeniden yazıldı. Tamamen C#/WPF (.NET Framework 4.8, C# 5); kota okuma da widget'ın içinde yapılır. Yönetici yetkisi, .NET SDK, Node.js veya npm paketi gerekmez; hiçbir şey indirilmez.

## Ne değişti

- **Yalnızca Claude:** Codex desteği (App Server sorgusu, `~/.codex` oturum taraması, sağlayıcı aç/kapa ayarı) kaldırıldı.
- **Node.js yok:** Kurum güvenlik duvarı Node.js indirmesini engellediği için `collect.mjs`/`providers.mjs` aynı davranışla `Collector.cs`'e taşındı; widget kotayı arka planda kendisi okur.
- **Çentik (notch):** Ekranın bir kenarına (üst/alt/sol/sağ) yapışan küçük kapsül. Dinlenmede mini halka + yüzde; üzerine gelince yaylanarak açılır, büyük halkayı gösterir. Fare ayrılınca katlanır.
- **İç içe halkalar:** İç halka 5 saatlik oturum, dış halka en dolu haftalık kota (tüm modeller / Sonnet / Opus). Yüzde ikisinden yüksek olanıdır; `%100` yalnızca kota gerçekten dolunca yazılır (%99,6 → `%99`).
- **Kota dolunca açılmaya kalan süre:** Dolu kotalardan en geç yenileneninkine göre. Kapalı çentikte yüzdenin yerinde (`2:36:12`, bir günden uzunsa `3g 10sa`), açık çentikte halkanın altında saniyeli (`3g 10:05:12`); saniye saniye akar. Süre bitince 5 dk'lık kontrol beklenmeden ölçüm alınır. Tepsi ipucu: `Claude · 5 saat %100 · Haftalık %26 · açılır 2:36:12`.
- **Renk durumdur, sayı kullanılandır:** Her halka kendi kotasına göre vurgu renginde; %75+ sarı, %90+ kırmızı, eski/dolmuş veri gri. Halka köşesindeki nokta ajan oturumunun durumunu gösterir (çalışıyor: vurgu, nabız atar; seni bekliyor: sarı; bitti: gri).
- **Kart:** Halkanın üzerinde kısa süre durunca açılır: tüm kota pencereleri, kullanılan yüzde, yenilenme zamanı saniye hassasiyetinde (solda canlı geri sayım `Yenilenmeye 2:36:12`, sağda hedef an `bugün 23:59:59` / `Cum 02.10 07:59:59`), son ölçüm yaşı, **AJAN OTURUMLARI** listesi (Çalışıyor / Seni bekliyor / Bitti; Claude masaüstündeki oturum başlığıyla). Halkaya tıklamak kartı sabitler (SABİT); tekrar tıklamak çözer. Oturum satırına tıklamak Claude masaüstünde o oturumu açar ve çentiği katlar.
- **Yenile (↻):** Açık çentikte ◐'nin altında (dikey çentikte yanında); ölçüm sürerken "…" gösterir. Kartın altındaki eski "↻ Yenile" kaldırıldı.
- **Ton rafı (◐):** Üç hazır ton (Mürekkep, Grafit, Kağıt — Kağıt paleti açık renge çevirir), özel vurgu rengi, yenile, ayarlar.
- **Ayarlar:** Kenar, yüzey (Düz siyah / Koyu cam / Cam; Cam seçiliyken saydamlık %10 / %20 / %35 / %50 / %70), ton, vurgu rengi (6 hazır + özel), boyut (Küçük/Orta/Büyük), görünürlük (Her zaman / Üzerine gelince — kenarda ince şerit), tam ekranda gizlenme, bildirim (oturum bitince/onay beklerken çentik kendiliğinden açılır, isteğe bağlı ses).
- **Alt + sürükle:** Çentiği başka kenara veya monitöre taşır; bırakılan yere en yakın kenara yapışır.
- Sistem tepsisi simgesi (vurgu renginde halka): Göster / Ayarlar… / Şimdi yenile / Kullanım kılavuzu / Çıkış. Görev çubuğunda pencere yoktur, çentik odağı çalmaz.

## Kurulum

**Tek adımda:** `Baslat.bat` dosyasına çift tıkla. Sırasıyla: çalışan widget'ı kapatır; `.cs` dosyaları veya `Build.ps1` exe'den yeniyse derler; `Setup.ps1`'i çalıştırır; `~/.claude/.credentials.json` içindeki girişin süresi dolmuşsa (veya 30 dk içinde doluyorsa) `claude auth login --claudeai` açar; widget'ı `--refresh` ile başlatıp ilk ölçümün sonucunu (en çok 30 sn) yazar. `claude.exe` PATH'te yoksa Claude masaüstü uygulamasının içindeki en yeni Claude Code (`%APPDATA%\Claude\claude-code\<sürüm>\claude.exe`) kullanılır. Son çalıştırmanın çıktısı `%LOCALAPPDATA%\UsageWidget\baslat.log` dosyasındadır. Giriş anahtarı yaklaşık 8 saat geçerlidir ve widget onu kendisi yenilemez; anahtar reddedilince `Baslat.bat`'ı kendiliğinden açar (bkz. Sınırlar ve belirsizlikler → Otomatik giriş).

**Elle:** Gereksinimler: Windows 10/11, .NET Framework 4.8, Windows PowerShell, giriş yapılmış Claude Code (`~/.claude/.credentials.json`). `claude.exe` PATH'te yoksa yalnızca karttaki "Claude'u bağla" düğmesi çalışmaz.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Setup.ps1
```

Ardından `Usage Widget.lnk` veya `UsageWidget.exe`. `Kullanim.html` exe ile aynı klasörde kalmalıdır (XAML dosyaları artık yok; arayüz tamamen kodda kurulur).

## Komut satırı

| Bayrak | Açıklama |
|---|---|
| `--edge top\|bottom\|left\|right` | Kenar (kaydedilen tercihi bu çalıştırma için ezer) |
| `--tone ink\|graphite\|paper` | Ton |
| `--surface solid\|dark\|glass` | Yüzey |
| `--transparency 0..85` | Cam yüzeyin saydamlığı (%) |
| `--size s\|m\|l` | Boyut |
| `--reveal always\|hover` | Görünürlük |
| `--accent #RRGGBB` | Vurgu rengi |
| `--refresh` | Açılışta sıradaki 5 dk'lık kontrolü beklemeden ölçüm al (`Baslat.bat` kullanır) |
| `--demo` | Örnek veriyle çalışır (gerçek sorgu yapmaz) |
| `--snapshot <tam-png-yolu>` | Açık çentiği PNG'ye çizip çıkar (çalışan göstergeyle çakışmaz) |
| `--card` | Snapshot'ta kartı da aç |
| `--backdrop` | Snapshot'a masaüstü benzeri arka plan ekle |

Örnek: `UsageWidget.exe --demo --snapshot "%TEMP%\notch.png" --card --backdrop`

## Tercihler

`%LOCALAPPDATA%\UsageWidget\preferences.json` — anahtarlar: `edge, offset, monitor, surface, transparency, tone, accent, size, reveal, foldFullscreen, notifyPeek, notifySound`. Önceki sürümden kalan `providers` anahtarı yok sayılır ve ilk kayıtta silinir.

## Veri akışı

- Claude: `~/.claude/.credentials.json` içindeki mevcut Claude Code OAuth oturumu yalnızca `https://api.anthropic.com/api/oauth/usage` için bellekte kullanılır. Oturum yenilenmez, dosyaya anahtar yazılmaz. Bu uç nokta resmî, garantili bir genel API değildir.
- `Collector.cs` isteği `HttpWebRequest` ile atar (TLS 1.2, Windows proxy ayarları geçerli). HTML dönen 403 (kurum güvenlik duvarı engel sayfası) hesap izni sorunu değil bağlantı sorunu sayılır.
- `%LOCALAPPDATA%\UsageWidget\usage.json`: yalnızca filtrelenmiş kota bilgisi (`claude` anahtarı). 5 dk aralık, 60 sn tekrar sınırı, HTTP 429 geri çekilmesi.
- **Ajan oturumları:** `~/.claude/projects/*/*.jsonl` (`CLAUDE_CONFIG_DIR`) dosyalarının yalnızca son satırları 4 sn'de bir, salt okunur ve paylaşımlı kipte okunur (son 3 saat, en çok 8 dosya). Yalnızca kayıt türü, oturum başlığı (`custom-title` kaydındaki `customTitle`; Claude masaüstü kenar çubuğundaki ad), çalışma klasörünün adı ve zaman kullanılır; konuşma içeriği saklanmaz, ağa gönderilmez. Başlık önce son 64 KB'ta aranır, bulunamazsa dosyanın ilk 1 MB'ında bir kez; başlıksız oturumlarda klasör adı gösterilir.

## Sınırlar ve belirsizlikler

- **Oturum durumu sezgiseldir.** Kayıt dosyası izin bekleyen bir araç çağrısı ile uzun süren bir komutu ayırt etmez: araç çağrısından 30 sn sonra hâlâ yazma yoksa "Seni bekliyor" gösterilir. Kesin sonuç için Claude Code *hooks* ile olay dosyası yazmak ileride eklenebilir.
- **Gerçek bulanıklık (blur) yok.** WPF katmanlı saydam pencerede arka plan bulanıklaştırılamaz; "Cam" yarı saydam katman + ışık kenarıyla taklit edilir.
- Karışık DPI'lı çoklu monitörde konum, birincil ekranın ölçeğiyle hesaplanır; ikincil ekranda birkaç piksel kayma olabilir.
- Tam ekran algılama `SHQueryUserNotificationState` ve ön plan penceresinin monitörü tam kaplamasına dayanır.
- **Oturuma gitme belgelenmemiş iç yapılara dayanır:** `%APPDATA%\Claude\claude-code-sessions\…\local_*.json` dosyalarındaki `cliSessionId` → `local_…` eşleşmesi ve masaüstü uygulamasının `claude://code/continue?session=local_…` bağlantısı (Claude 2.9939.2.0 ile denendi). Uygulama güncellemesi bunları değiştirirse tıklama sessizce çalışmayı bırakabilir. Terminalden açılan (masaüstünde kaydı olmayan) oturumlar açılamaz; kart notunda bildirilir.
- Windows açılışında `W:\SMD\AHK_Live\Bat\Startup.bat` başlatır (2026-09-30): `UsageWidget.exe --refresh` doğrudan açılır, `Baslat.bat` değil (pause'da bekler, derler).
- **Otomatik giriş (2026-09-30):** Ölçüm `login_required` dönünce (anahtar yok ya da 401) widget `Baslat.bat oto`'yu kendisi açar; girişi yeniler ve widget'ı yeniden başlatır. `oto` ile başarıda pencere kendiliğinden kapanır, hata varsa `pause`'da bekler. Son açılış 1 saatten yeniyse ya da o `cmd` penceresi hâlâ açıksa yeniden açılmaz (`%LOCALAPPDATA%\UsageWidget\oto-baslat.txt`: `<utc ms> <pid>`). `Baslat.ps1`, bitiş zamanı ileride olsa bile widget'ın son ölçümü 401 ise ve `.credentials.json` o zamandan beri değişmediyse girişi atlamaz; atlasaydı widget ile Baslat arasında döngü oluşurdu.

## Kontroller

```powershell
.\Baslat.bat
.\UsageWidget.exe --demo --snapshot "$env:TEMP\notch.png" --card --backdrop
```

Doğrulama durumu: `Collector.cs` ile derlenen sürüm 28.09.2026'da gerçek hesapla kotayı okudu (`Baslat.bat` → `Kota okundu`). Cam saydamlığı ayarı **henüz derlenmedi ve görsel olarak denenmedi**. Otomatik test yok (eski Node testleri `collect.mjs` ile birlikte kaldırıldı).
