using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool created;
        using (var mutex = new Mutex(true, "Local\\UsageWidgetNative", out created))
        {
            if (!created) return;
            try
            {
                var application = new Application();
                var widget = new Widget(args);
                application.Run(widget.Window);
            }
            catch (Exception error)
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageWidget");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "ui-error.log"), error.ToString());
                MessageBox.Show("Gösterge açılamadı. UsageWidget klasöründeki ui-error.log dosyasını kontrol et.", "Usage Widget");
            }
            finally { mutex.ReleaseMutex(); }
        }
    }
}

internal sealed class Widget
{
    public readonly Window Window;
    private readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    private readonly string stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageWidget");
    private readonly JavaScriptSerializer json = new JavaScriptSerializer();
    private readonly Dictionary<string, FrameworkElement> cards = new Dictionary<string, FrameworkElement>();
    private readonly Dictionary<string, string> colors = new Dictionary<string, string> { {"claude", "#D8AD8F"}, {"codex", "#85C9B7"} };
    private Dictionary<string, object> runtime;
    private Process collector;
    private readonly DispatcherTimer clock = new DispatcherTimer();
    private readonly DispatcherTimer poll = new DispatcherTimer();
    private readonly DispatcherTimer collapse = new DispatcherTimer();
    private readonly Forms.NotifyIcon tray;
    private readonly string snapshot;
    private bool pinned;
    private string lastRead = "";
    private Dictionary<string, object> latest = new Dictionary<string, object>();
    private string preferencePath { get { return Path.Combine(stateDir, "preferences.json"); } }

    public Widget(string[] args)
    {
        Directory.CreateDirectory(stateDir);
        snapshot = args.Length == 2 && args[0] == "--snapshot" ? args[1] : null;
        runtime = Read(Path.Combine(root, "runtime.json"));
        Window = (Window)XamlReader.Parse(File.ReadAllText(Path.Combine(root, "Window.xaml")));
        AddCard("claude", "Claude"); AddCard("codex", "Codex");
        var prefs = Read(preferencePath);
        Rect area = SystemParameters.WorkArea;
        Window.Left = Number(prefs, "left") ?? area.Right - 436;
        Window.Top = Number(prefs, "top") ?? area.Top + 12;
        pinned = Flag(prefs, "pinned") || args.Contains("--expanded") || snapshot != null;
        ClampPosition(); UpdatePin();
        collapse.Interval = TimeSpan.FromMilliseconds(650);
        collapse.Tick += delegate { collapse.Stop(); if (!pinned && !Window.IsMouseOver) Expand(false); };
        Window.MouseEnter += delegate { collapse.Stop(); Expand(true); };
        Window.MouseLeave += delegate { if (!pinned) collapse.Start(); };
        Find<Grid>("DragArea").MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            while (source is Visual)
            {
                if (source is Button) return;
                source = VisualTreeHelper.GetParent(source);
            }
            try { Window.DragMove(); ClampPosition(); Save(); } catch (InvalidOperationException) { }
        };
        Find<Button>("PinButton").Click += delegate { pinned = !pinned; UpdatePin(); Save(); };
        Find<Button>("CloseButton").Click += delegate { Window.Close(); };
        Find<Button>("RefreshButton").Click += delegate { Refresh(true); };
        Find<Button>("HelpButton").Click += delegate { Process.Start(new ProcessStartInfo(Path.Combine(root, "Kullanim.html")) { UseShellExecute = true }); };
        Window.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { pinned=false; UpdatePin(); Expand(false); Save(); } };
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text="Usage Widget · Claude ve Codex", Visible=snapshot == null };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Göster", null, delegate { Window.Show(); Expand(true); Window.Activate(); });
        menu.Items.Add("Çıkış", null, delegate { Window.Close(); });
        tray.ContextMenuStrip=menu;
        tray.DoubleClick += delegate { Window.Show(); Expand(true); Window.Activate(); };
        clock.Interval=TimeSpan.FromSeconds(2);
        clock.Tick += delegate { Tick(); };
        poll.Interval=TimeSpan.FromMinutes(5);
        poll.Tick += delegate { Refresh(false); };
        Window.Closed += delegate
        {
            Save(); clock.Stop(); poll.Stop(); collapse.Stop(); tray.Visible=false; tray.Dispose();
            if (collector != null) collector.Dispose();
        };
        Window.ContentRendered += delegate
        {
            Tick();
            if (snapshot != null)
            {
                Expand(true); Window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Window.ActualWidth, (int)Window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(Window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(snapshot)) encoder.Save(stream);
                Window.Close();
            }
            else { Expand(pinned); clock.Start(); poll.Start(); Refresh(false); }
        };
    }

    private T Find<T>(string name) where T : FrameworkElement { return (T)Window.FindName(name); }
    private static T Find<T>(FrameworkElement element, string name) where T : FrameworkElement { return (T)element.FindName(name); }
    private static SolidColorBrush Color(string text) { return (SolidColorBrush)new BrushConverter().ConvertFromString(text); }
    private static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
    private static object Get(Dictionary<string, object> map, string key) { object result; return map.TryGetValue(key, out result) ? result : null; }
    private static string Text(Dictionary<string, object> map, string key) { return Convert.ToString(Get(map,key), CultureInfo.InvariantCulture); }
    private static double? Number(Dictionary<string, object> map, string key) { object v=Get(map,key); if(v == null) return null; double n; return double.TryParse(Convert.ToString(v,CultureInfo.InvariantCulture),NumberStyles.Any,CultureInfo.InvariantCulture,out n) ? (double?)n : null; }
    private static bool Flag(Dictionary<string, object> map, string key) { return Get(map,key) is bool && (bool)Get(map,key); }
    private static double Now { get { return (DateTime.UtcNow - new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds; } }
    private Dictionary<string, object> Read(string path) { try { return json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path)); } catch { return new Dictionary<string,object>(); } }

    private void Save()
    {
        if (snapshot != null) return;
        File.WriteAllText(preferencePath,json.Serialize(new { left=Window.Left, top=Window.Top, pinned=pinned }));
    }
    private void ClampPosition()
    {
        Window.Left=Math.Max(SystemParameters.VirtualScreenLeft,Math.Min(Window.Left,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-Window.Width));
        Window.Top=Math.Max(SystemParameters.VirtualScreenTop,Math.Min(Window.Top,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-560));
    }
    private void UpdatePin() { Find<Button>("PinButton").Content=pinned ? "◆" : "◇"; }
    private void Expand(bool expanded)
    {
        var details=Find<StackPanel>("Details");
        bool wasCollapsed = details.Visibility != Visibility.Visible;
        details.Visibility=expanded ? Visibility.Visible : Visibility.Collapsed;
        Window.Width=expanded ? 420 : 300;
        if (expanded && wasCollapsed)
            details.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0.65,1,TimeSpan.FromMilliseconds(160)));
        ClampPosition();
    }
    private void Refresh(bool force)
    {
        if (collector != null && !collector.HasExited) return;
        if (collector != null) { collector.Dispose(); collector=null; }
        try
        {
            collector=Process.Start(new ProcessStartInfo(Text(runtime,"nodePath"), "\""+Path.Combine(root,"collect.mjs")+"\""+(force ? " --force" : "")) { UseShellExecute=false, CreateNoWindow=true, WindowStyle=ProcessWindowStyle.Hidden, WorkingDirectory=root });
            Find<Button>("RefreshButton").IsEnabled=false;
            Find<TextBlock>("Footer").Text="Kullanım bilgisi alınıyor…";
        }
        catch { Find<TextBlock>("Footer").Text="Bağlantı başlatılamadı. Setup.ps1 ile yolları yenile."; }
    }
    private void Tick()
    {
        try
        {
            string state=Path.Combine(stateDir,"usage.json");
            string source=File.Exists(state) ? File.ReadAllText(state) : "";
            if (source.Length>0 && source != lastRead) { latest=json.Deserialize<Dictionary<string,object>>(source); lastRead=source; }
            UpdateCard("claude",Map(Get(latest,"claude")));
            UpdateCard("codex",Map(Get(latest,"codex")));
            if (collector != null && collector.HasExited)
            {
                int code=collector.ExitCode; collector.Dispose(); collector=null;
                Find<Button>("RefreshButton").IsEnabled=true;
                Find<TextBlock>("Footer").Text=code == 0 ? "Yüzdeler kullanılan kotayı gösterir." : "Veri yenilenemedi; son ölçümü kontrol et.";
            }
            if (snapshot != null) Find<TextBlock>("Footer").Text="Yüzdeler kullanılan kotayı gösterir.";
        }
        catch { Find<TextBlock>("Footer").Text="Veri okunamadı; yeniden denenecek."; }
    }
    private void AddCard(string id, string title)
    {
        string xaml=File.ReadAllText(Path.Combine(root,"Card.xaml")).Replace("$Title",title).Replace("$Color",colors[id]);
        var card=(FrameworkElement)XamlReader.Parse(xaml);
        cards[id]=card; Find<StackPanel>("CardHost").Children.Add(card);
        Find<Button>(card,"Connect").Click += delegate
        {
            try
            {
                string executable=Text(runtime,id+"Path");
                Process.Start(new ProcessStartInfo(executable,id == "claude" ? "auth login --claudeai" : "login") { UseShellExecute=true });
                Find<TextBlock>("Footer").Text="Tarayıcıda girişini tamamla, ardından Yenile’ye bas.";
            }
            catch { Find<TextBlock>("Footer").Text="Giriş açılamadı. Kullanım kılavuzunu kontrol et."; }
        };
    }
    private static bool Expired(Dictionary<string,object> quota) { var reset=Number(quota,"resetsAt"); return reset.HasValue && reset.Value <= Now; }
    private void UpdateCard(string id, Dictionary<string,object> data)
    {
        if (data.Count==0) return;
        var card=cards[id];
        var sequence=Get(data,"windows") as System.Collections.IEnumerable;
        var list=sequence == null ? new object[0] : sequence.Cast<object>().ToArray();
        var windows=list.Select(Map).ToArray();
        double? updated=Number(data,"updatedAt");
        double? age=updated.HasValue ? (double?)((Now-updated.Value)/1000) : null;
        bool fresh=Text(data,"status")=="ok" && age.HasValue && age.Value <= 660 && !windows.Any(Expired);
        var connect=Find<Button>(card,"Connect"); connect.Visibility=Visibility.Collapsed;
        string value="—";
        if (windows.Length>0)
        {
            var main=windows[0]; bool expired=Expired(main);
            double used=Number(main,"usedPercent") ?? 0;
            value=expired ? "—" : Math.Round(used).ToString()+"%";
            string color=!fresh ? "#8692A6" : used>=90 ? "#F49D88" : colors[id];
            Arc(card,expired ? 0 : used,color);
            Find<TextBlock>(card,"Value").Text=value;
            Find<TextBlock>(card,"RingLabel").Text=expired ? "bekliyor" : "kullanılan";
            Find<TextBlock>(card,"Status").Text=fresh ? "GÜNCEL" : "ESKİ VERİ";
            Find<TextBlock>(card,"Status").Foreground=Color(color);
            string ago=!age.HasValue ? "bilinmiyor" : age<60 ? "az önce" : age<3600 ? Math.Floor(age.Value/60)+" dk önce" : Math.Floor(age.Value/3600)+" sa önce";
            string label=id == "claude" ? "Claude Code’da bağlı hesap" : "Codex’te bağlı hesap";
            Find<TextBlock>(card,"Message").Text=label+"\nSon ölçüm: "+ago+(Text(data,"status")!="ok" ? "\nBağlantı bekleniyor; son ölçüm gösteriliyor." : "");
        }
        else
        {
            Arc(card,0,colors[id]); Find<TextBlock>(card,"Value").Text="—"; Find<TextBlock>(card,"RingLabel").Text="bekliyor";
            string status=Text(data,"status");
            Find<TextBlock>(card,"Status").Text="BAĞLANTI GEREKLİ";
            Find<TextBlock>(card,"Status").Foreground=Color("#D9B27B");
            string message;
            if(status=="login_required") { message=id=="claude" ? "Masaüstünde kullandığın Claude hesabıyla bir kez giriş yap." : "Kullanım kotasını görmek için Codex hesabına giriş yap."; connect.Visibility=Visibility.Visible; }
            else if(status=="forbidden") { message="Bu oturumun kota okuma izni yok. Abonelik hesabınla yeniden bağlan."; connect.Visibility=Visibility.Visible; }
            else if(status=="no_quota") { message="Bu hesap için kullanım kotası dönmedi."; Find<TextBlock>(card,"Status").Text="KOTA BİLGİSİ YOK"; }
            else { message="Veri alınamadı. Bağlantıyı kontrol edip Yenile’ye bas."; Find<TextBlock>(card,"Status").Text="BAĞLANTI BEKLENİYOR"; }
            connect.Content=id=="claude" ? "Claude’u bağla ↗" : "Codex’i bağla ↗";
            Find<TextBlock>(card,"Message").Text=message;
        }
        var mini=Find<TextBlock>(id=="claude" ? "MiniClaude" : "MiniCodex"); mini.Text=value; mini.Foreground=Color(fresh ? colors[id] : "#A8B3C6");
        var quotas=Find<StackPanel>(card,"Quotas"); quotas.Children.Clear(); quotas.Visibility=windows.Length==0 ? Visibility.Collapsed : Visibility.Visible;
        foreach(var quota in windows)
        {
            var row=(FrameworkElement)XamlReader.Parse(File.ReadAllText(Path.Combine(root,"Quota.xaml")));
            bool expired=Expired(quota); double used=Number(quota,"usedPercent") ?? 0;
            Find<TextBlock>(row,"Label").Text=Text(quota,"label");
            Find<TextBlock>(row,"Percent").Text=expired ? "—" : "%"+Math.Round(used)+" kullanıldı";
            Find<ProgressBar>(row,"Bar").Value=expired ? 0 : used;
            Find<ProgressBar>(row,"Bar").Foreground=Color(fresh ? colors[id] : "#8692A6");
            Find<TextBlock>(row,"Reset").Text=ResetText(quota);
            quotas.Children.Add(row);
        }
    }
    private static string ResetText(Dictionary<string,object> quota)
    {
        var reset=Number(quota,"resetsAt"); if(!reset.HasValue) return "Yenilenme zamanı yok";
        double seconds=(reset.Value-Now)/1000; if(seconds<=0) return "Süre doldu · yeni veri bekleniyor";
        var span=TimeSpan.FromSeconds(seconds);
        if(span.TotalDays>=1) return Math.Floor(span.TotalDays)+" gün "+span.Hours+" sa sonra";
        if(span.TotalHours>=1) return Math.Floor(span.TotalHours)+" sa "+span.Minutes+" dk sonra";
        return span.TotalMinutes>=1 ? Math.Ceiling(span.TotalMinutes)+" dk sonra" : "1 dk içinde";
    }
    private static void Arc(FrameworkElement card,double percent,string color)
    {
        var arc=Find<System.Windows.Shapes.Path>(card,"Arc"); arc.Stroke=Color(color);
        if(percent<=0) { arc.Data=null; return; }
        string d;
        if(percent>=100) d="M 40,5 A 35,35 0 1 1 40,75 A 35,35 0 1 1 40,5";
        else { double a=percent/100*Math.PI*2; d=String.Format(CultureInfo.InvariantCulture,"M 40,5 A 35,35 0 {0} 1 {1:0.###},{2:0.###}",percent>50 ? 1 : 0,40+35*Math.Sin(a),40-35*Math.Cos(a)); }
        arc.Data=Geometry.Parse(d);
    }
}
