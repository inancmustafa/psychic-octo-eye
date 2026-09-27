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

// Sağlayıcı verisinin ekrana hazır özeti
internal sealed class ProviderView
{
    public bool HasData;
    public string Status = "";
    public Dictionary<string, object>[] Windows = new Dictionary<string, object>[0];
    public bool Fresh;
    public double? Used;
    public double? Age;
}

internal sealed partial class Widget
{
    public readonly Window Window;

    private static readonly string[] ProviderIds = { "claude", "codex" };
    private static readonly Dictionary<string, string> Names = new Dictionary<string, string> { { "claude", "Claude" }, { "codex", "Codex" } };
    private static readonly Dictionary<string, string> Glyphs = new Dictionary<string, string> { { "claude", "✳" }, { "codex", "◉" } };
    // Sağlayıcının kendi kullanım sayfası; adres değişirse yalnızca burası güncellenir.
    private static readonly Dictionary<string, string> UsagePages = new Dictionary<string, string>
    {
        { "claude", "https://claude.ai/settings/usage" },
        { "codex", "https://chatgpt.com/codex/settings/usage" }
    };
    private static readonly CultureInfo Tr = new CultureInfo("tr-TR");
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string DefaultNote = "Yüzdeler kullanılan kotadır · 5 dk’da bir yenilenir.";

    private readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    private readonly string stateDir = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageWidget");
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
    private readonly SessionScanner scanner;
    private readonly bool demo, noAnim, backdrop;
    private readonly string snapshot, snapshotCard;

    private Prefs prefs;
    private Theme theme;
    private Dictionary<string, object> runtime;
    private Dictionary<string, object> latest = new Dictionary<string, object>();
    private List<SessionInfo> sessions = new List<SessionInfo>();
    private Dictionary<string, SessionState> lastStates;
    private Process collector;
    private string lastRead = "", note = DefaultNote;
    private int version, cardVersion = -1;
    private DateTime cardBuilt = DateTime.MinValue;

    // Arayüz öğeleri (tema veya kenar değişince yeniden kurulur)
    private Border notch, shelf, card;
    private FrameworkElement sliverView, idleView, expandedView;
    private readonly Dictionary<string, RingView> rings = new Dictionary<string, RingView>();
    private readonly Dictionary<string, RingView> minis = new Dictionary<string, RingView>();
    private readonly Dictionary<string, TextBlock> miniTexts = new Dictionary<string, TextBlock>();
    private readonly Dictionary<string, Border> slots = new Dictionary<string, Border>();

    private bool expanded, shelfOpen, cardPinned, dragging, peeking, hiddenForFullscreen, scanning;
    private string cardProvider, pendingCard;
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
        snapshotCard = Arg(args, "--card");
        demo = args.Contains("--demo");
        backdrop = args.Contains("--backdrop");
        noAnim = snapshot != null;
        runtime = Read(IOPath.Combine(root, "runtime.json"));

        // Kalıcı tercihler + komut satırı geçersiz kılmaları (--edge, --tone, --surface, --size, --reveal, --accent)
        var map = Prefs.From(Read(PrefsPath)).ToMap();
        foreach (string key in new[] { "edge", "tone", "surface", "size", "reveal", "accent" })
        {
            string value = Arg(args, "--" + key);
            if (value != null) map[key] = value;
        }
        prefs = Prefs.From(map);

        try { using (var g = Drawing.Graphics.FromHwnd(IntPtr.Zero)) dpiScale = g.DpiX / 96.0; }
        catch { dpiScale = 1; }
        scanner = new SessionScanner(ParseJson, ClaudeHome(), CodexHome());

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
            Border slot;
            if (cardProvider != null && slots.TryGetValue(cardProvider, out slot) && slot.IsMouseOver) return;
            HideCard();
        };
        hoverTimer.Interval = TimeSpan.FromMilliseconds(200);
        hoverTimer.Tick += delegate { hoverTimer.Stop(); if (pendingCard != null) ShowCard(pendingCard); pendingCard = null; };
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

        tray = new Forms.NotifyIcon { Text = "Usage Notch", Visible = false };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Göster", null, delegate { Peek(null); });
        menu.Items.Add("Ayarlar…", null, delegate { OpenSettings(); });
        menu.Items.Add("Şimdi yenile", null, delegate { Refresh(true); });
        menu.Items.Add("Kullanım kılavuzu", null, delegate { OpenHelp(); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, delegate { Window.Close(); });
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) Peek(null); };

        displayChanged = delegate { Window.Dispatcher.BeginInvoke(new Action(Place)); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += displayChanged;

        Window.Closed += delegate
        {
            SavePrefs();
            foreach (var timer in new[] { clock, poll, fold, cardTimer, hoverTimer, peekTimer, sessionTimer, screenTimer }) timer.Stop();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= displayChanged;
            if (settingsWindow != null) settingsWindow.Close();
            tray.Visible = false;
            tray.Dispose();
            if (trayIconHandle != IntPtr.Zero) Native.DestroyIcon(trayIconHandle);
            if (collector != null) collector.Dispose();
        };

        BuildUi();
        Place();
        if (demo) LoadDemo();
        else if (snapshot != null) { try { sessions = scanner.Scan(DateTime.UtcNow, prefs.Claude, prefs.Codex); } catch { } }
        Tick();
        UpdateTrayIcon();
        tray.Visible = snapshot == null;

        Window.ContentRendered += delegate
        {
            if (snapshot != null)
            {
                SetExpanded(true);
                if (snapshotCard != null) ShowCard(snapshotCard);
                Window.Dispatcher.BeginInvoke(new Action(TakeSnapshot), DispatcherPriority.ApplicationIdle);
                return;
            }
            clock.Start(); poll.Start(); sessionTimer.Start(); screenTimer.Start();
            ScanSessions();
            Refresh(false);
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

    private static string CodexHome()
    {
        string custom = Environment.GetEnvironmentVariable("CODEX_HOME");
        return !String.IsNullOrEmpty(custom) ? custom : IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
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

    private string[] EnabledIds()
    {
        return ProviderIds.Where(id => id == "claude" ? prefs.Claude : prefs.Codex).ToArray();
    }

    private void SavePrefs()
    {
        if (snapshot != null) return;
        try { File.WriteAllText(PrefsPath, json.Serialize(prefs.ToMap())); }
        catch { }
    }

    // ───────────────────────── Veri ─────────────────────────

    private ProviderView Describe(string id)
    {
        var view = new ProviderView();
        var data = Map(Get(latest, id));
        view.HasData = data.Count > 0;
        view.Status = Text(data, "status");
        var sequence = Get(data, "windows") as System.Collections.IEnumerable;
        view.Windows = sequence == null ? new Dictionary<string, object>[0] : sequence.Cast<object>().Select(Map).ToArray();
        double? updated = Number(data, "updatedAt");
        view.Age = updated.HasValue ? (double?)((Now - updated.Value) / 1000) : null;
        view.Fresh = view.Status == "ok" && view.Age.HasValue && view.Age.Value <= 660 && !view.Windows.Any(Expired);
        if (view.Windows.Length > 0 && !Expired(view.Windows[0])) view.Used = Number(view.Windows[0], "usedPercent");
        return view;
    }

    private Color RingColor(ProviderView view)
    {
        if (!view.Used.HasValue || !view.Fresh) return theme.Stale;
        return theme.UsageColor(view.Used.Value);
    }

    private static string ValueText(ProviderView view)
    {
        if (view.Status == "login_required" || view.Status == "forbidden") return "!";
        return view.Used.HasValue ? "%" + Math.Round(view.Used.Value).ToString(Inv) : "—";
    }

    private SessionState Activity(string id)
    {
        var list = sessions.Where(s => s.Provider == id).ToList();
        if (list.Any(s => s.State == SessionState.Waiting)) return SessionState.Waiting;
        if (list.Any(s => s.State == SessionState.Working)) return SessionState.Working;
        if (list.Any(s => s.State == SessionState.Finished && (DateTime.UtcNow - s.LastWriteUtc).TotalMinutes < 10)) return SessionState.Finished;
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
        if (collector != null && collector.HasExited)
        {
            int code = collector.ExitCode;
            collector.Dispose();
            collector = null;
            note = code == 0 ? DefaultNote : "Veri yenilenemedi; son ölçüm gösteriliyor.";
            changed = true;
        }
        if (changed) version++;
        Render();
    }

    private void Refresh(bool force)
    {
        if (demo) { note = "Örnek modunda gerçek veri okunmaz."; version++; Render(); return; }
        if (collector != null && !collector.HasExited) return;
        if (collector != null) { collector.Dispose(); collector = null; }
        try
        {
            string node = Text(runtime, "nodePath");
            if (node.Length == 0) node = "node";
            collector = Process.Start(new ProcessStartInfo(node, "\"" + IOPath.Combine(root, "collect.mjs") + "\"" + (force ? " --force" : ""))
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = root
            });
            note = "Kullanım bilgisi alınıyor…";
        }
        catch { note = "Bağlantı başlatılamadı. Setup.ps1 ile yolları yenile."; }
        version++;
        Render();
    }

    private void ScanSessions()
    {
        if (demo || scanning) return;
        scanning = true;
        bool claude = prefs.Claude, codex = prefs.Codex;
        DateTime now = DateTime.UtcNow;
        Task.Factory.StartNew(() => scanner.Scan(now, claude, codex)).ContinueWith(task =>
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
        string notifyProvider = null;
        SessionState notifyState = SessionState.None;
        var map = new Dictionary<string, SessionState>();
        foreach (var session in list)
        {
            map[session.Key] = session.State;
            SessionState before;
            // Çalışırken bitti ya da onay beklemeye geçti → kısa süre göster
            if (lastStates != null && lastStates.TryGetValue(session.Key, out before) && before == SessionState.Working
                && (session.State == SessionState.Finished || session.State == SessionState.Waiting)
                && (notifyProvider == null || session.State == SessionState.Waiting))
            {
                notifyProvider = session.Provider;
                notifyState = session.State;
            }
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
        if (notifyProvider != null && !hiddenForFullscreen)
        {
            if (prefs.NotifyPeek) Peek(notifyProvider);
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
                { "windows", new object[] { quota("five_hour", "Oturum · 5 saat", 42, 2.22), quota("seven_day", "Haftalık · tüm modeller", 18, 98.6), quota("seven_day_opus", "Haftalık · Opus", 7, 98.6) } } } },
            { "codex", new Dictionary<string, object> { { "name", "Codex" }, { "status", "ok" }, { "updatedAt", now - 40000 },
                { "windows", new object[] { quota("primary", "5 saatlik", 82, 1.1), quota("secondary", "Haftalık", 31, 61.4) } } } }
        };
        DateTime u = DateTime.UtcNow;
        sessions = new List<SessionInfo>
        {
            new SessionInfo { Key = "d1", Provider = "claude", Project = "usage-widget", State = SessionState.Working, LastWriteUtc = u.AddSeconds(-3) },
            new SessionInfo { Key = "d2", Provider = "claude", Project = "otomasyon-araclari", State = SessionState.Finished, LastWriteUtc = u.AddMinutes(-12) },
            new SessionInfo { Key = "d3", Provider = "codex", Project = "kinematik-analiz", State = SessionState.Waiting, LastWriteUtc = u.AddSeconds(-41) },
            new SessionInfo { Key = "d4", Provider = "codex", Project = "rapor-betikleri", State = SessionState.Finished, LastWriteUtc = u.AddMinutes(-35) }
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

    private void Peek(string id)
    {
        if (hiddenForFullscreen) return;
        peeking = true;
        SetExpanded(true);
        string target = id ?? EnabledIds().FirstOrDefault();
        if (target != null) ShowCard(target);
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

    private void UpdateTrayText()
    {
        var parts = EnabledIds().Select(id => Names[id] + " " + ValueText(Describe(id))).ToArray();
        string text = parts.Length == 0 ? "Usage Notch" : String.Join(" · ", parts);
        tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
    }

    private void Connect(string id)
    {
        try
        {
            string executable = Text(runtime, id + "Path");
            if (executable.Length == 0) executable = id;
            Process.Start(new ProcessStartInfo(executable, id == "claude" ? "auth login --claudeai" : "login") { UseShellExecute = true });
            note = "Tarayıcıda girişini tamamla, ardından Yenile’ye bas.";
        }
        catch { note = "Giriş açılamadı. Kullanım kılavuzunu kontrol et."; }
        version++;
        Render();
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
