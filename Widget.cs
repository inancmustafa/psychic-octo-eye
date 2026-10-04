using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using IOPath = System.IO.Path;
using Shapes = System.Windows.Shapes;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool snapshot = args.Contains("--snapshot");
        bool created;
        using (var mutex = new Mutex(true, snapshot ? "Local\\UsageWidgetSnapshot" : "Local\\UsageWidgetNative", out created))
        {
            if (!created) return;
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                var widget = new Widget(args);
                application.Run(widget.Window);
            }
            catch (Exception error)
            {
                string folder = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageWidget");
                Directory.CreateDirectory(folder);
                File.WriteAllText(IOPath.Combine(folder, "ui-error.log"), error.ToString());
                if (!snapshot) MessageBox.Show("Gösterge açılamadı. UsageWidget klasöründeki ui-error.log dosyasını kontrol et.", "Usage Notch");
            }
            finally { mutex.ReleaseMutex(); }
        }
    }
}

// Claude kota verisinin ekrana hazır özeti
internal sealed class ProviderView
{
    public bool HasData;
    public string Status = "";
    public Dictionary<string, object>[] Windows = new Dictionary<string, object>[0];
    public bool Fresh;
    public double? Session;      // iç halka: 5 saatlik oturum kotası
    public double? Weekly;       // dış halka: en dolu haftalık kota (tüm modeller / Sonnet / Opus)
    public string WeeklyId;
    public double? Used;         // gösterilen yüzde: iki halkadan yüksek olanı
    public double? ReopenAt;     // bir kota dolduysa kullanımın yeniden açılacağı an (ms)
    public double? Age;
}

internal sealed partial class Widget
{
    public readonly Window Window;

    private const string Glyph = "✳";
    private static readonly CultureInfo Tr = new CultureInfo("tr-TR");
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string DefaultNote = "Yüzdeler kullanılan kotadır · 5 dk’da bir yenilenir.";

    private readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    private readonly string stateDir = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageWidget");
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
    private readonly SessionScanner scanner;
    private readonly bool demo, noAnim, backdrop, snapshotCard, refreshOnStart;
    private readonly string snapshot;

    private Prefs prefs;
    private Theme theme;
    private Dictionary<string, object> runtime;
    private Dictionary<string, object> latest = new Dictionary<string, object>();
    private List<SessionInfo> sessions = new List<SessionInfo>();
    private Dictionary<string, SessionState> lastStates;
    private string lastRead = "", note = DefaultNote;
    private int version, cardVersion = -1;
    private DateTime cardBuilt = DateTime.MinValue;

    // Arayüz öğeleri (tema veya kenar değişince yeniden kurulur)
    private Border notch, shelf, card, slot;
    private FrameworkElement sliverView, idleView, expandedView;
    private RingView ring, mini;
    private TextBlock miniText, refreshGlyph, ringLabel;
    private bool reopenPending;
    private readonly List<Countdown> countdowns = new List<Countdown>();

    private bool expanded, shelfOpen, cardOpen, cardPinned, dragging, peeking, hiddenForFullscreen, scanning, collecting;
    private double dpiScale = 1;
    private IntPtr hwnd = IntPtr.Zero;
    private Window settingsWindow;
    private readonly Forms.NotifyIcon tray;
    private IntPtr trayIconHandle = IntPtr.Zero;
    private readonly EventHandler displayChanged;

    private readonly DispatcherTimer clock = new DispatcherTimer();
    private readonly DispatcherTimer poll = new DispatcherTimer();
    private readonly DispatcherTimer fold = new DispatcherTimer();
    private readonly DispatcherTimer cardTimer = new DispatcherTimer();
    private readonly DispatcherTimer hoverTimer = new DispatcherTimer();
    private readonly DispatcherTimer peekTimer = new DispatcherTimer();
    private readonly DispatcherTimer sessionTimer = new DispatcherTimer();
    private readonly DispatcherTimer screenTimer = new DispatcherTimer();
    private readonly DispatcherTimer countdownTimer = new DispatcherTimer();

    private string PrefsPath { get { return IOPath.Combine(stateDir, "preferences.json"); } }
    private bool Vertical { get { return prefs.Edge == "left" || prefs.Edge == "right"; } }
    private bool FarSide { get { return prefs.Edge == "bottom" || prefs.Edge == "right"; } }
    private double Scale { get { return prefs.Size == "s" ? 0.86 : prefs.Size == "l" ? 1.18 : 1.0; } }
    private static double Now { get { return (DateTime.UtcNow - Epoch).TotalMilliseconds; } }
    private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public Widget(string[] args)
    {
        Directory.CreateDirectory(stateDir);
        snapshot = Arg(args, "--snapshot");
        snapshotCard = args.Contains("--card");
        demo = args.Contains("--demo");
        backdrop = args.Contains("--backdrop");
        refreshOnStart = args.Contains("--refresh");
        noAnim = snapshot != null;
        runtime = Read(IOPath.Combine(root, "runtime.json"));

        // Kalıcı tercihler + komut satırı geçersiz kılmaları (--edge, --tone, --surface, --transparency, --size, --reveal, --accent)
        var map = Prefs.From(Read(PrefsPath)).ToMap();
        foreach (string key in new[] { "edge", "tone", "surface", "transparency", "size", "reveal", "accent" })
        {
            string value = Arg(args, "--" + key);
            if (value != null) map[key] = value;
        }
        prefs = Prefs.From(map);

        try { using (var g = Drawing.Graphics.FromHwnd(IntPtr.Zero)) dpiScale = g.DpiX / 96.0; }
        catch { dpiScale = 1; }
        scanner = new SessionScanner(ParseJson, ClaudeHome());

        Window = new Window
        {
            Title = "Usage Notch", WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
            UseLayoutRounding = true, SnapsToDevicePixels = true, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI")
        };
        Window.SourceInitialized += delegate
        {
            hwnd = new WindowInteropHelper(Window).Handle;
            if (snapshot == null) Native.MakeToolWindow(hwnd);
        };

        // Üzerine gelince aç, ayrılınca kısa gecikmeyle katla
        Window.MouseEnter += delegate { fold.Stop(); if (!expanded) SetExpanded(true); };
        Window.MouseLeave += delegate { if (!dragging) fold.Start(); };
        // Alt + sürükle: çentiği başka bir kenara ya da kenar boyunca taşı
        Window.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (!Native.AltDown()) return;
            dragging = true;
            Window.CaptureMouse();
            e.Handled = true;
        };
        Window.PreviewMouseMove += delegate { if (dragging) DragTo(); };
        Window.PreviewMouseLeftButtonUp += (s, e) => { if (!dragging) return; EndDrag(); e.Handled = true; };
        Window.LostMouseCapture += delegate { if (dragging) EndDrag(); };

        fold.Interval = TimeSpan.FromMilliseconds(550);
        fold.Tick += delegate
        {
            fold.Stop();
            if (cardPinned || dragging || peeking || Window.IsMouseOver) return;
            Fold();
        };
        cardTimer.Interval = TimeSpan.FromMilliseconds(320);
        cardTimer.Tick += delegate
        {
            cardTimer.Stop();
            if (cardPinned || peeking || card == null || card.IsMouseOver) return;
            if (slot != null && slot.IsMouseOver) return;
            HideCard();
        };
        hoverTimer.Interval = TimeSpan.FromMilliseconds(200);
        hoverTimer.Tick += delegate { hoverTimer.Stop(); ShowCard(); };
        peekTimer.Interval = TimeSpan.FromSeconds(4.5);
        peekTimer.Tick += delegate
        {
            peekTimer.Stop();
            peeking = false;
            if (!Window.IsMouseOver && !cardPinned) Fold();
        };
        clock.Interval = TimeSpan.FromSeconds(2);
        clock.Tick += delegate { Tick(); };
        poll.Interval = TimeSpan.FromMinutes(5);
        poll.Tick += delegate { Refresh(false); };
        sessionTimer.Interval = TimeSpan.FromSeconds(4);
        sessionTimer.Tick += delegate { ScanSessions(); };
        screenTimer.Interval = TimeSpan.FromSeconds(1.5);
        screenTimer.Tick += delegate { CheckFullscreen(); };
        // Geri sayım saniye hassasiyetinde; 250 ms'de bir bakılır ki gösterilen saniye gerçek saatten geri kalmasın
        countdownTimer.Interval = TimeSpan.FromMilliseconds(250);
        countdownTimer.Tick += delegate { UpdateCountdowns(); };

        tray = new Forms.NotifyIcon { Text = "Usage Notch", Visible = false };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Göster", null, delegate { Peek(); });
        menu.Items.Add("Ayarlar…", null, delegate { OpenSettings(); });
        menu.Items.Add("Şimdi yenile", null, delegate { Refresh(true); });
        menu.Items.Add("Kullanım kılavuzu", null, delegate { OpenHelp(); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, delegate { Window.Close(); });
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) Peek(); };

        displayChanged = delegate { Window.Dispatcher.BeginInvoke(new Action(Place)); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += displayChanged;

        Window.Closed += delegate
        {
            SavePrefs();
            foreach (var timer in new[] { clock, poll, fold, cardTimer, hoverTimer, peekTimer, sessionTimer, screenTimer, countdownTimer }) timer.Stop();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= displayChanged;
            if (settingsWindow != null) settingsWindow.Close();
            tray.Visible = false;
            tray.Dispose();
            if (trayIconHandle != IntPtr.Zero) Native.DestroyIcon(trayIconHandle);
        };

        BuildUi();
        Place();
        if (demo) LoadDemo();
        else if (snapshot != null) { try { sessions = scanner.Scan(DateTime.UtcNow); } catch { } }
        Tick();
        UpdateTrayIcon();
        tray.Visible = snapshot == null;

        Window.ContentRendered += delegate
        {
            if (snapshot != null)
            {
                SetExpanded(true);
                if (snapshotCard) ShowCard();
                Window.Dispatcher.BeginInvoke(new Action(TakeSnapshot), DispatcherPriority.ApplicationIdle);
                return;
            }
            clock.Start(); poll.Start(); sessionTimer.Start(); screenTimer.Start(); countdownTimer.Start();
            ScanSessions();
            Refresh(refreshOnStart);
        };
    }

    // ───────────────────────── Yardımcılar ─────────────────────────

    private static string Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
    }

    private static Dictionary<string, object> ParseJson(string text)
    {
        // Arka plan iş parçacığında kullanıldığı için her çağrıda ayrı ayrıştırıcı
        return new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue, RecursionLimit = 256 }.Deserialize<Dictionary<string, object>>(text);
    }

    private static string ClaudeHome()
    {
        string custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return !String.IsNullOrEmpty(custom) ? custom : IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    }

    private Dictionary<string, object> Read(string path)
    {
        try { return json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path)) ?? new Dictionary<string, object>(); }
        catch { return new Dictionary<string, object>(); }
    }

    private static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
    private static object Get(Dictionary<string, object> map, string key) { object result; return map != null && map.TryGetValue(key, out result) ? result : null; }
    private static string Text(Dictionary<string, object> map, string key) { return Convert.ToString(Get(map, key), Inv) ?? ""; }

    private static double? Number(Dictionary<string, object> map, string key)
    {
        object value = Get(map, key);
        if (value == null) return null;
        double n;
        return Double.TryParse(Convert.ToString(value, Inv), NumberStyles.Any, Inv, out n) ? (double?)n : null;
    }

    private static bool Expired(Dictionary<string, object> quota)
    {
        double? reset = Number(quota, "resetsAt");
        return reset.HasValue && reset.Value <= Now;
    }

    private void SavePrefs()
    {
        if (snapshot != null) return;
        try { File.WriteAllText(PrefsPath, json.Serialize(prefs.ToMap())); }
        catch { }
    }

    // ───────────────────────── Veri ─────────────────────────

    private ProviderView Describe()
    {
        var view = new ProviderView();
        var data = Map(Get(latest, "claude"));
        view.HasData = data.Count > 0;
        view.Status = Text(data, "status");
        var sequence = Get(data, "windows") as System.Collections.IEnumerable;
        view.Windows = sequence == null ? new Dictionary<string, object>[0] : sequence.Cast<object>().Select(Map).ToArray();
        double? updated = Number(data, "updatedAt");
        view.Age = updated.HasValue ? (double?)((Now - updated.Value) / 1000) : null;
        view.Fresh = view.Status == "ok" && view.Age.HasValue && view.Age.Value <= 660 && !view.Windows.Any(Expired);
        foreach (var quota in view.Windows)
        {
            double? used = Number(quota, "usedPercent");
            if (Expired(quota) || !used.HasValue) continue;
            string id = Text(quota, "id");
            if (id == "five_hour") view.Session = used;
            else if (id.StartsWith("seven_day", StringComparison.Ordinal) && (!view.Weekly.HasValue || used.Value > view.Weekly.Value)) { view.Weekly = used; view.WeeklyId = id; }
            // Dolan her kota kullanımı kapatır; açılma anı dolu kotalardan en geç yenileneninkidir
            double? reset = Number(quota, "resetsAt");
            if (used.Value >= 100 && reset.HasValue && (!view.ReopenAt.HasValue || reset.Value > view.ReopenAt.Value)) view.ReopenAt = reset;
        }
        if (view.Session.HasValue || view.Weekly.HasValue) view.Used = Math.Max(view.Session ?? 0, view.Weekly ?? 0);
        return view;
    }

    private Color RingColor(ProviderView view, double? used)
    {
        if (!used.HasValue || !view.Fresh) return theme.Stale;
        return theme.UsageColor(used.Value);
    }

    private static string ValueText(ProviderView view)
    {
        if (view.Status == "login_required" || view.Status == "forbidden") return "!";
        return view.Used.HasValue ? PercentText(view.Used.Value) : "—";
    }

    // %100 yalnızca kota gerçekten dolunca yazılır; %99,6 "%99" görünür, yoksa dolu sanılıp açılma süresi aranırdı.
    private static string PercentText(double used)
    {
        return "%" + (used >= 100 ? 100 : Math.Min(99, Math.Round(used))).ToString(Inv);
    }

    // Kullanımın açılmasına kalan süre: "2:36:12", bir günden uzunsa "3g 10:05:12" (dar yerde "3g 10sa")
    private static string Remaining(double resetMs, bool compact)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling((resetMs - Now) / 1000)));
        if (span.Days > 0)
            return span.Days.ToString(Inv) + "g " + (compact ? span.Hours.ToString(Inv) + "sa" : String.Format(Inv, "{0:00}:{1:00}:{2:00}", span.Hours, span.Minutes, span.Seconds));
        return String.Format(Inv, "{0}:{1:00}:{2:00}", span.Hours, span.Minutes, span.Seconds);
    }

    private SessionState Activity()
    {
        if (sessions.Any(s => s.State == SessionState.Waiting)) return SessionState.Waiting;
        if (sessions.Any(s => s.State == SessionState.Working)) return SessionState.Working;
        if (sessions.Any(s => s.State == SessionState.Finished && (DateTime.UtcNow - s.LastWriteUtc).TotalMinutes < 10)) return SessionState.Finished;
        return SessionState.None;
    }

    private void Tick()
    {
        bool changed = false;
        if (!demo)
        {
            try
            {
                string state = IOPath.Combine(stateDir, "usage.json");
                string source = File.Exists(state) ? File.ReadAllText(state) : "";
                if (source.Length > 0 && source != lastRead)
                {
                    latest = json.Deserialize<Dictionary<string, object>>(source) ?? new Dictionary<string, object>();
                    lastRead = source;
                    changed = true;
                }
            }
            catch { note = "Veri okunamadı; yeniden denenecek."; changed = true; }
        }
        if (changed) version++;
        Render();
    }

    // Kota ölçümü arka planda çalışır; bitince usage.json hemen okunur.
    private void Refresh(bool force)
    {
        if (demo) { note = "Örnek modunda gerçek veri okunmaz."; version++; Render(); return; }
        if (collecting) return;
        collecting = true;
        note = "Kullanım bilgisi alınıyor…";
        version++;
        Render();
        string dir = stateDir, home = ClaudeHome();
        Task.Factory.StartNew(() => Collector.Run(dir, home, force)).ContinueWith(task =>
        {
            bool failed = task.IsFaulted;
            string status = failed ? null : task.Result;
            Window.Dispatcher.BeginInvoke(new Action(() =>
            {
                collecting = false;
                note = failed ? "Veri yenilenemedi; son ölçüm gösteriliyor." : DefaultNote;
                version++;
                Tick();
                if (status == "login_required") AutoLogin();
            }));
        });
    }

    private void ScanSessions()
    {
        if (demo || scanning) return;
        scanning = true;
        DateTime now = DateTime.UtcNow;
        Task.Factory.StartNew(() => scanner.Scan(now)).ContinueWith(task =>
        {
            List<SessionInfo> result = task.IsFaulted ? null : task.Result;
            Window.Dispatcher.BeginInvoke(new Action(() =>
            {
                scanning = false;
                if (result != null) OnSessions(result);
            }));
        });
    }

    private void OnSessions(List<SessionInfo> list)
    {
        SessionState notifyState = SessionState.None;
        var map = new Dictionary<string, SessionState>();
        foreach (var session in list)
        {
            map[session.Key] = session.State;
            SessionState before;
            // Çalışırken bitti ya da onay beklemeye geçti → kısa süre göster (onay bekleme öncelikli)
            if (lastStates != null && lastStates.TryGetValue(session.Key, out before) && before == SessionState.Working
                && (session.State == SessionState.Finished || session.State == SessionState.Waiting)
                && (notifyState == SessionState.None || session.State == SessionState.Waiting))
                notifyState = session.State;
        }
        bool changed = lastStates == null || lastStates.Count != map.Count || map.Any(pair =>
        {
            SessionState before;
            return !lastStates.TryGetValue(pair.Key, out before) || before != pair.Value;
        });
        lastStates = map;
        sessions = list;
        if (changed) version++;
        Render();
        if (notifyState != SessionState.None && !hiddenForFullscreen)
        {
            if (prefs.NotifyPeek) Peek();
            if (prefs.NotifySound)
            {
                if (notifyState == SessionState.Waiting) System.Media.SystemSounds.Exclamation.Play();
                else System.Media.SystemSounds.Asterisk.Play();
            }
        }
    }

    private void LoadDemo()
    {
        double now = Now;
        Func<string, string, double, double, Dictionary<string, object>> quota = (id, label, used, hours) =>
            new Dictionary<string, object> { { "id", id }, { "label", label }, { "usedPercent", used }, { "resetsAt", now + hours * 3600000 } };
        latest = new Dictionary<string, object>
        {
            { "claude", new Dictionary<string, object> { { "name", "Claude" }, { "status", "ok" }, { "updatedAt", now - 95000 },
                { "windows", new object[] { quota("five_hour", "Oturum · 5 saat", 42, 2.22), quota("seven_day", "Haftalık · tüm modeller", 18, 98.6), quota("seven_day_opus", "Haftalık · Opus", 7, 98.6) } } } }
        };
        DateTime u = DateTime.UtcNow;
        // SessionScanner sıralamasıyla aynı: önce bekleyen, sonra çalışan, en son bitenler
        sessions = new List<SessionInfo>
        {
            new SessionInfo { Key = "d1", Project = "kinematik-analiz", State = SessionState.Waiting, LastWriteUtc = u.AddSeconds(-41) },
            new SessionInfo { Key = "d2", Project = "usage-widget", State = SessionState.Working, LastWriteUtc = u.AddSeconds(-3) },
            new SessionInfo { Key = "d3", Project = "otomasyon-araclari", State = SessionState.Finished, LastWriteUtc = u.AddMinutes(-12) },
            new SessionInfo { Key = "d4", Project = "rapor-betikleri", State = SessionState.Finished, LastWriteUtc = u.AddMinutes(-35) }
        };
        version++;
    }

    // ───────────────────────── Konum ve davranış ─────────────────────────

    private Forms.Screen FindScreen()
    {
        return Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == prefs.Monitor) ?? Forms.Screen.PrimaryScreen;
    }

    private static double Clamp(double value, double min, double max)
    {
        return max < min ? min : Math.Max(min, Math.Min(max, value));
    }

    // Pencere sabit boyutlu, saydam bir tuvaldir; çentik bu tuvalin kenara bakan ortasına yapışır.
    // Tamamen saydam pikseller tıklamayı alttaki pencereye geçirir.
    private void Place()
    {
        var area = FindScreen().WorkingArea;
        double left = area.Left / dpiScale, top = area.Top / dpiScale, width = area.Width / dpiScale, height = area.Height / dpiScale;
        double w = (Vertical ? 430 : 660) * Scale, h = (Vertical ? 660 : 580) * Scale;
        Window.Width = w;
        Window.Height = h;
        if (!Vertical)
        {
            Window.Left = Clamp(left + prefs.Offset * width - w / 2, left, left + width - w);
            Window.Top = prefs.Edge == "bottom" ? top + height - h : top;
        }
        else
        {
            Window.Top = Clamp(top + prefs.Offset * height - h / 2, top, top + height - h);
            Window.Left = prefs.Edge == "right" ? left + width - w : left;
        }
    }

    private void DragTo()
    {
        Native.POINT p;
        if (!Native.GetCursorPos(out p)) return;
        var screen = Forms.Screen.FromPoint(new Drawing.Point(p.X, p.Y));
        var b = screen.WorkingArea;
        double toLeft = p.X - b.Left, toRight = b.Right - p.X, toTop = p.Y - b.Top, toBottom = b.Bottom - p.Y;
        double nearest = Math.Min(Math.Min(toLeft, toRight), Math.Min(toTop, toBottom));
        string edge = nearest == toTop ? "top" : nearest == toBottom ? "bottom" : nearest == toLeft ? "left" : "right";
        bool horizontal = edge == "top" || edge == "bottom";
        double offset = horizontal ? (p.X - b.Left) / (double)Math.Max(1, b.Width) : (p.Y - b.Top) / (double)Math.Max(1, b.Height);
        bool rebuild = edge != prefs.Edge;
        prefs.Edge = edge;
        prefs.Offset = Clamp(offset, 0, 1);
        prefs.Monitor = screen.DeviceName;
        if (rebuild) BuildUi();
        Place();
    }

    private void EndDrag()
    {
        dragging = false;
        if (Window.IsMouseCaptured) Window.ReleaseMouseCapture();
        SavePrefs();
        RebuildSettings();
    }

    private void CheckFullscreen()
    {
        if (hwnd == IntPtr.Zero) return;
        bool full = prefs.FoldFullscreen && Native.IsForegroundFullscreen(hwnd);
        if (full && !hiddenForFullscreen) { hiddenForFullscreen = true; Fold(); Window.Hide(); }
        else if (!full && hiddenForFullscreen) { hiddenForFullscreen = false; Window.Show(); }
    }

    private void SetExpanded(bool value)
    {
        if (expanded == value) return;
        expanded = value;
        if (!value) { shelfOpen = false; UpdateShelf(); HideCard(); }
        ApplyNotch(true);
    }

    private void Fold()
    {
        cardPinned = false;
        peeking = false;
        SetExpanded(false);
    }

    private void Peek()
    {
        if (hiddenForFullscreen) return;
        peeking = true;
        SetExpanded(true);
        ShowCard();
        peekTimer.Stop();
        peekTimer.Start();
    }

    private void UpdateTrayIcon()
    {
        try
        {
            using (var bitmap = new Drawing.Bitmap(32, 32))
            {
                using (var g = Drawing.Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Drawing.Color.Transparent);
                    using (var track = new Drawing.Pen(Drawing.Color.FromArgb(110, 150, 158, 146), 4.5f)) g.DrawEllipse(track, 5, 5, 22, 22);
                    Color a = theme.Accent;
                    using (var pen = new Drawing.Pen(Drawing.Color.FromArgb(255, a.R, a.G, a.B), 4.5f))
                    {
                        pen.StartCap = Drawing.Drawing2D.LineCap.Round;
                        pen.EndCap = Drawing.Drawing2D.LineCap.Round;
                        g.DrawArc(pen, 5, 5, 22, 22, -90, 250);
                    }
                }
                IntPtr handle = bitmap.GetHicon();
                IntPtr previous = trayIconHandle;
                tray.Icon = Drawing.Icon.FromHandle(handle);
                trayIconHandle = handle;
                if (previous != IntPtr.Zero) Native.DestroyIcon(previous);
            }
        }
        catch { tray.Icon = Drawing.SystemIcons.Information; }
    }

    // Örn. "Claude · 5 saat %58 · Haftalık %26" ya da kota doluysa sonuna " · açılır 2:36:12"
    private void UpdateTrayText()
    {
        ProviderView view = Describe();
        string text = "Claude";
        if (view.Session.HasValue) text += " · 5 saat " + PercentText(view.Session.Value);
        if (view.Weekly.HasValue) text += " · Haftalık " + PercentText(view.Weekly.Value);
        if (!view.Used.HasValue) text += " " + ValueText(view);
        if (view.ReopenAt.HasValue) text += " · açılır " + Remaining(view.ReopenAt.Value, true);
        if (text.Length > 63) text = text.Substring(0, 63);
        if (tray.Text != text) tray.Text = text;
    }

    // Oturum satırına tıklama: Claude masaüstünde o Code oturumuna git.
    // claude://code/continue?session=local_… masaüstü uygulamasının kendi bağlantısıdır; belgelenmemiştir, sürümle değişebilir.
    private void OpenSession(SessionInfo session)
    {
        string id = SessionScanner.DesktopSessionId(session.SessionId);
        if (id == null)
        {
            note = "Bu oturum Claude masaüstünde bulunamadı (terminalden açılmış olabilir).";
            version++;
            Render();
            return;
        }
        OpenUrl("claude://code/continue?session=" + id);
        Fold();
    }

    private void Connect()
    {
        try
        {
            string executable = Text(runtime, "claudePath");
            if (executable.Length == 0) executable = "claude";
            Process.Start(new ProcessStartInfo(executable, "auth login --claudeai") { UseShellExecute = true });
            note = "Tarayıcıda girişini tamamla, ardından Yenile’ye bas.";
        }
        catch { note = "Giriş açılamadı. Kullanım kılavuzunu kontrol et."; }
        version++;
        Render();
    }

    // Anahtar reddedilince (401) Baslat.bat kendiliğinden açılır: girişi yeniler, widget'ı yeniden başlatır.
    // Son açılış 1 saatten yeniyse ya da o pencere hâlâ açıksa yeniden açılmaz; kayıt widget yeniden başlasa da kalır.
    private void AutoLogin()
    {
        string script = IOPath.Combine(root, "Baslat.bat");
        string stamp = IOPath.Combine(stateDir, "oto-baslat.txt");
        if (demo || snapshot != null || !File.Exists(script)) return;
        try
        {
            string[] last = File.Exists(stamp) ? File.ReadAllText(stamp).Split(' ') : new string[0];
            double at;
            int pid;
            if (last.Length == 2 && Double.TryParse(last[0], NumberStyles.Float, Inv, out at) && Int32.TryParse(last[1], out pid)
                && (Now - at < 3600000 || Alive(pid))) return;
            Process started = Process.Start(new ProcessStartInfo(script, "oto") { UseShellExecute = true, WorkingDirectory = root });
            File.WriteAllText(stamp, ((long)Now).ToString(Inv) + " " + (started != null ? started.Id : 0).ToString(Inv));
            note = "Giriş süresi doldu; Baslat.bat açıldı, tarayıcıda girişi tamamla.";
        }
        catch { note = "Giriş süresi doldu; Baslat.bat açılamadı, elle çalıştır."; }
        version++;
        Render();
    }

    private static bool Alive(int pid)
    {
        try { using (Process p = Process.GetProcessById(pid)) return !p.HasExited && p.ProcessName.Equals("cmd", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private void OpenHelp()
    {
        OpenUrl(IOPath.Combine(root, "Kullanim.html"));
    }

    private void TakeSnapshot()
    {
        try
        {
            Window.UpdateLayout();
            var element = (FrameworkElement)Window.Content;
            int width = (int)Math.Ceiling(element.ActualWidth * 2), height = (int)Math.Ceiling(element.ActualHeight * 2);
            var bitmap = new RenderTargetBitmap(width, height, 192, 192, PixelFormats.Pbgra32);
            if (backdrop)
            {
                // Okunabilir önizleme için masaüstü benzeri arka plan
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    var rect = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
                    dc.DrawRectangle(new LinearGradientBrush(Ui.Hex("#5B6B7C"), Ui.Hex("#1E2631"), 45), null, rect);
                    dc.DrawRectangle(new VisualBrush(element), null, rect);
                }
                bitmap.Render(visual);
            }
            else bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(snapshot)) encoder.Save(stream);
        }
        finally { Window.Close(); }
    }
}
