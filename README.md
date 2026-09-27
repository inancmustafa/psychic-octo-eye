# Usage Notch (usage-widget · supernotchy-style)

Windows için yerel Claude + Codex kota göstergesi; görünüm ve kullanım [Super Notchy](https://supernotchy.com/) (macOS) örnek alınarak yeniden yazıldı. C#/WPF arayüz (.NET Framework 4.8, C# 5) ve bağımlılıksız Node.js veri okuyucusu. Yönetici yetkisi, .NET SDK veya npm paketi gerekmez.

## Ne değişti

- **Çentik (notch):** Ekranın bir kenarına (üst/alt/sol/sağ) yapışan küçük kapsül. Dinlenmede sağlayıcı başına mini halka + yüzde; üzerine gelince yaylanarak açılır, her sağlayıcı için büyük halka gösterir. Fare ayrılınca katlanır.
- **Renk durumdur, sayı kullanılandır:** Halka vurgu renginde; %75+ sarı, %90+ kırmızı, eski/dolmuş veri gri. Halka köşesindeki nokta ajan oturumunun durumunu gösterir (çalışıyor: vurgu, nabız atar; seni bekliyor: sarı; bitti: gri).
- **Kart:** Halkanın üzerinde kısa süre durunca açılır: tüm kota pencereleri, kullanılan yüzde, sıfırlanma zamanı (göreli + saat), son ölçüm yaşı, **AJAN OTURUMLARI** listesi (Çalışıyor / Seni bekliyor / Bitti). Halkaya tıklamak kartı sabitler (SABİT); tekrar tıklamak çözer. ↗ sağlayıcının kullanım sayfasını açar.
- **Ton rafı (◐):** Üç hazır ton (Mürekkep, Grafit, Kağıt — Kağıt paleti açık renge çevirir), özel vurgu rengi, yenile, ayarlar.
- **Ayarlar:** Kenar, yüzey (Düz siyah / Koyu cam / Cam), ton, vurgu rengi (6 hazır + özel), boyut (Küçük/Orta/Büyük), görünürlük (Her zaman / Üzerine gelince — kenarda ince şerit), tam ekranda gizlenme, bildirim (oturum bitince/onay beklerken çentik kendiliğinden açılır, isteğe bağlı ses), sağlayıcıları aç/kapa.
- **Alt + sürükle:** Çentiği başka kenara veya monitöre taşır; bırakılan yere en yakın kenara yapışır.
- **Sağlayıcı kapatma:** Kapatılan sağlayıcı sorgulanmaz, oturumları taranmaz, `usage.json` içindeki ölçümü silinir.
- Sistem tepsisi simgesi (vurgu renginde halka): Göster / Ayarlar… / Şimdi yenile / Kullanım kılavuzu / Çıkış. Görev çubuğunda pencere yoktur, çentik odağı çalmaz.

## Kurulum

Gereksinimler: Windows 10/11, .NET Framework 4.8, Windows PowerShell, Node.js 18+, Claude Code ve/veya Codex CLI (`node.exe` zorunlu; `claude.exe`/`codex.exe`'den en az biri PATH'te).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Setup.ps1
```

Ardından `Usage Widget.lnk` veya `UsageWidget.exe`. `.cs`, `.mjs` ve `Kullanim.html` dosyaları exe ile aynı klasörde kalmalıdır (XAML dosyaları artık yok; arayüz tamamen kodda kurulur).

## Komut satırı

| Bayrak | Açıklama |
|---|---|
| `--edge top\|bottom\|left\|right` | Kenar (kaydedilen tercihi bu çalıştırma için ezer) |
| `--tone ink\|graphite\|paper` | Ton |
| `--surface solid\|dark\|glass` | Yüzey |
| `--size s\|m\|l` | Boyut |
| `--reveal always\|hover` | Görünürlük |
| `--accent #RRGGBB` | Vurgu rengi |
| `--demo` | Örnek veriyle çalışır (gerçek sorgu yapmaz) |
| `--snapshot <tam-png-yolu>` | Açık çentiği PNG'ye çizip çıkar (çalışan göstergeyle çakışmaz) |
| `--card claude\|codex` | Snapshot'ta ilgili kartı da aç |
| `--backdrop` | Snapshot'a masaüstü benzeri arka plan ekle |

Örnek: `UsageWidget.exe --demo --snapshot "%TEMP%\notch.png" --card claude --backdrop`

## Tercihler

`%LOCALAPPDATA%\UsageWidget\preferences.json` — anahtarlar: `edge, offset, monitor, surface, tone, accent, size, reveal, foldFullscreen, notifyPeek, notifySound, providers{claude,codex}`. `collect.mjs` yalnızca `providers` anahtarını okur.

## Veri akışı

- Codex: `codex app-server` üzerinden `initialize` → `initialized` → `account/rateLimits/read`. Model veya görev başlatılmaz.
- Claude: `~/.claude/.credentials.json` içindeki mevcut Claude Code OAuth oturumu yalnızca `https://api.anthropic.com/api/oauth/usage` için bellekte kullanılır. Oturum yenilenmez, dosyaya anahtar yazılmaz. Bu uç nokta resmî, garantili bir genel API değildir.
- `%LOCALAPPDATA%\UsageWidget\usage.json`: yalnızca filtrelenmiş kota bilgisi. 5 dk aralık, sağlayıcı başına 60 sn tekrar sınırı, HTTP 429 geri çekilmesi.
- **Ajan oturumları:** `~/.claude/projects/*/*.jsonl` (`CLAUDE_CONFIG_DIR`) ve `~/.codex/sessions/YYYY/MM/DD/*.jsonl` (`CODEX_HOME`) dosyalarının yalnızca son satırları 4 sn'de bir, salt okunur ve paylaşımlı kipte okunur (son 3 saat, sağlayıcı başına en çok 8 dosya). Yalnızca kayıt türü, çalışma klasörünün adı ve zaman kullanılır; konuşma içeriği saklanmaz, ağa gönderilmez.

## Sınırlar ve belirsizlikler

- **Oturum durumu sezgiseldir.** Kayıt dosyası izin bekleyen bir araç çağrısı ile uzun süren bir komutu ayırt etmez: araç çağrısından 30 sn sonra hâlâ yazma yoksa "Seni bekliyor" gösterilir. Kesin sonuç için Claude Code *hooks* ile olay dosyası yazmak ileride eklenebilir. Codex rollout biçimi sürümler arasında değişebilir.
- **Gerçek bulanıklık (blur) yok.** WPF katmanlı saydam pencerede arka plan bulanıklaştırılamaz; "Cam" yarı saydam katman + ışık kenarıyla taklit edilir.
- Karışık DPI'lı çoklu monitörde konum, birincil ekranın ölçeğiyle hesaplanır; ikincil ekranda birkaç piksel kayma olabilir.
- Tam ekran algılama `SHQueryUserNotificationState` ve ön plan penceresinin monitörü tam kaplamasına dayanır.
- Codex kullanım sayfası adresi (`https://chatgpt.com/codex/settings/usage`) değişirse `Widget.cs` içindeki `UsagePages` güncellenmelidir.
- Windows açılışında otomatik başlatma eklenmedi (kısayolu `shell:startup` klasörüne kopyalamak yeterli).

## Kontroller

```powershell
node --test providers.test.mjs
node collect.mjs --summary
.\UsageWidget.exe --demo --snapshot "$env:TEMP\notch.png" --card claude --backdrop
```

Doğrulama durumu: 8 Node testi geçti; C# kaynakları `-langversion:5` ile WPF/WinForms derlemelerine karşı hatasız derlendi; oturum tarayıcı sahte JSONL kayıtlarıyla test edildi. Arayüz Windows üzerinde **henüz çalıştırılıp görsel olarak kontrol edilmedi**.
