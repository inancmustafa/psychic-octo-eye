# Usage Widget

Windows için yerel Claude + Codex kota göstergesi. C#/WPF uygulaması ve bağımlılıksız Node.js veri okuyucusu. Mevcut Windows çalışma ortamını kullanır; .NET SDK kurulumu gerektirmez.

## Başlatma

Gereksinimler: Windows, .NET Framework 4.8, Windows PowerShell, Node.js 18 veya üzeri, Codex CLI ve Claude Code. `node.exe`, `codex.exe` ve `claude.exe` PATH üzerinden bulunabilmelidir. Ayrı npm paketi veya .NET SDK kurulumu gerekmez.

1. Depoyu indirin veya klonlayın.
2. Uygulama klasöründe aşağıdaki komutları çalıştırın:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Setup.ps1
```

3. Oluşturulan `UsageWidget.exe` veya `Usage Widget.lnk` dosyasına çift tıklayın.

XAML ve Node.js dosyaları aynı klasörde kalmalıdır. Kullanıcı kılavuzu: [Kullanim.html](Kullanim.html).

Kaynak deposu derlenmiş uygulamayı veya bilgisayara özel `runtime.json` dosyasını içermez. Bunlar yukarıdaki adımlarda yerel olarak oluşturulur.

`Setup.ps1` mevcut `node.exe`, `codex.exe`, `claude.exe` yollarını bulur ve kısayolu oluşturur. Kaynak değişirse `Build.ps1` Windows ile gelen .NET Framework derleyicisiyle uygulamayı derler.

## Veri akışı

- Codex: `initialize` → `initialized` → `account/rateLimits/read`. Model veya görev başlatılmaz. Sunucunun döndürdüğü kota süreleri kullanılır.
- Claude: `~/.claude/.credentials.json` içindeki mevcut Claude Code OAuth oturumu, yalnızca `https://api.anthropic.com/api/oauth/usage` için bellekte kullanılır. Yönlendirmeler reddedilir. Oturum yenileme veya dosyaya anahtar yazma yapılmaz. Bu uç nokta resmî olarak garantili bir genel API değildir.
- Masaüstünde kullanılan Claude hesabıyla Code oturumunun eşleşmesi kullanıcı tarafından sağlanır. Giriş düğmesi resmî `claude auth login --claudeai` akışını açar.
- `%LOCALAPPDATA%/UsageWidget/usage.json`: yalnızca filtrelenmiş kullanım bilgileri. Kimlik bilgileri, hesap e-postaları, konuşmalar ve ham sunucu yanıtları kaydedilmez.
- Beş dakika aralık, 60 saniye elle yenileme sınırı, HTTP 429 geri çekilmesi, 15/20 saniye bağlantı zaman aşımı.
- Ağ hatasında eski ölçüm zaman damgasıyla korunur. Kimlik doğrulama hatasında eski hesap değerleri temizlenir. Kota süresi dolduğunda otomatik olarak %0 varsayılmaz.

## Kontroller

`node --test providers.test.mjs`

`node collect.mjs --summary`

`UsageWidget.exe --snapshot <tam-png-yolu>` arayüzü gerçek önbellekten dosyaya render eder (önce çalışan göstergeyi kapatın). `--expanded` ayrıntıları açık başlatır.

Doğrulama: Altı veri normalizasyonu/hata durumu testi geçti. Canlı Codex kotası okundu, derlenen arayüzde gösterildi ve görsel olarak kontrol edildi. Claude canlı bağlantısı, kullanıcı girişi gerektiği için doğrulanamadı.

## Sınırlar

Bu ilk sürümde kota uyarı bildirimleri, açılışta otomatik başlatma ve her ekran kenarına otomatik yapışma yok. Pencere üstte kalır, taşınabilir ve fareyle açılır. Claude oturumu yoksa giriş gerekir; desteklenmeyen hesapta kota uydurulmaz. Codex güncellemeleri çalıştırılabilir dosya yolunu değiştirirse Setup.ps1 yeniden çalıştırılmalıdır.
