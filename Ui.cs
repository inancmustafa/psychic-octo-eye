using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

// Renk ve metin yardımcıları
internal static class Ui
{
    public static readonly SolidColorBrush Hit = B(Color.FromArgb(1, 0, 0, 0)); // görünmez ama fareyi yakalayan yüzey

    public static Color Hex(string text) { return (Color)ColorConverter.ConvertFromString(text); }

    public static Color Parse(string text, Color fallback)
    {
        try { return String.IsNullOrEmpty(text) ? fallback : Hex(text); }
        catch { return fallback; }
    }

    public static Color Mix(Color a, Color b, double t)
    {
        return Color.FromArgb(Lerp(a.A, b.A, t), Lerp(a.R, b.R, t), Lerp(a.G, b.G, t), Lerp(a.B, b.B, t));
    }

    private static byte Lerp(byte a, byte b, double t) { return (byte)Math.Round(a + (b - a) * Math.Max(0, Math.Min(1, t))); }

    public static Color Alpha(Color c, byte a) { return Color.FromArgb(a, c.R, c.G, c.B); }

    public static double Luma(Color c) { return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0; }

    public static SolidColorBrush B(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    public static TextBlock T(string text, double size, Color color, FontWeight weight)
    {
        return new TextBlock { Text = text ?? "", FontSize = size, Foreground = B(color), FontWeight = weight };
    }

    public static string Hex(Color c)
    {
        return String.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
    }
}

// Kullanıcı tercihleri; %LOCALAPPDATA%\UsageWidget\preferences.json (collect.mjs sağlayıcı anahtarlarını okur)
internal sealed class Prefs
{
    public string Edge = "top";          // top | bottom | left | right
    public double Offset = 0.5;          // kenar boyunca konum (0..1)
    public string Monitor = "";
    public string Surface = "solid";     // solid | dark | glass
    public string Tone = "ink";          // ink | graphite | paper
    public string Accent = "#9FD08A";
    public string Size = "m";            // s | m | l
    public string Reveal = "always";     // always | hover
    public bool FoldFullscreen = true;
    public bool NotifyPeek = true;
    public bool NotifySound = false;
    public bool Claude = true;
    public bool Codex = true;

    public static Prefs From(Dictionary<string, object> map)
    {
        var p = new Prefs();
        map = map ?? new Dictionary<string, object>();
        p.Edge = Pick(map, "edge", p.Edge, "top", "bottom", "left", "right");
        p.Surface = Pick(map, "surface", p.Surface, "solid", "dark", "glass");
        p.Tone = Pick(map, "tone", p.Tone, "ink", "graphite", "paper");
        p.Size = Pick(map, "size", p.Size, "s", "m", "l");
        p.Reveal = Pick(map, "reveal", p.Reveal, "always", "hover");
        string accent = Str(map, "accent");
        if (accent != null && accent.StartsWith("#") && accent.Length == 7) p.Accent = accent.ToUpperInvariant();
        p.Monitor = Str(map, "monitor") ?? "";
        double offset;
        if (Num(map, "offset", out offset)) p.Offset = Math.Max(0, Math.Min(1, offset));
        p.FoldFullscreen = Flag(map, "foldFullscreen", p.FoldFullscreen);
        p.NotifyPeek = Flag(map, "notifyPeek", p.NotifyPeek);
        p.NotifySound = Flag(map, "notifySound", p.NotifySound);
        var providers = Get(map, "providers") as Dictionary<string, object>;
        if (providers != null)
        {
            p.Claude = Flag(providers, "claude", true);
            p.Codex = Flag(providers, "codex", true);
        }
        return p;
    }

    public Dictionary<string, object> ToMap()
    {
        return new Dictionary<string, object>
        {
            { "edge", Edge }, { "offset", Offset }, { "monitor", Monitor }, { "surface", Surface }, { "tone", Tone },
            { "accent", Accent }, { "size", Size }, { "reveal", Reveal }, { "foldFullscreen", FoldFullscreen },
            { "notifyPeek", NotifyPeek }, { "notifySound", NotifySound },
            { "providers", new Dictionary<string, object> { { "claude", Claude }, { "codex", Codex } } }
        };
    }

    private static object Get(Dictionary<string, object> map, string key)
    {
        object value;
        return map.TryGetValue(key, out value) ? value : null;
    }

    private static string Str(Dictionary<string, object> map, string key)
    {
        string value = Get(map, key) as string;
        return String.IsNullOrEmpty(value) ? null : value;
    }

    private static string Pick(Dictionary<string, object> map, string key, string fallback, params string[] allowed)
    {
        string value = Str(map, key);
        return value != null && allowed.Contains(value) ? value : fallback;
    }

    private static bool Flag(Dictionary<string, object> map, string key, bool fallback)
    {
        object value = Get(map, key);
        return value is bool ? (bool)value : fallback;
    }

    private static bool Num(Dictionary<string, object> map, string key, out double result)
    {
        result = 0;
        object value = Get(map, key);
        if (value == null) return false;
        return Double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }
}

// Çentik tonu ve durum renkleri; Kağıt tonu paleti ters çevirir.
internal sealed class Theme
{
    public Color Surface, Raised, Line, Text, Sub, Faint, Track, Accent, OnAccent, Warn, Crit, Stale;
    public bool Light;
    public string Kind;

    public static readonly string[] Tones = { "ink", "graphite", "paper" };

    public static Color ToneSwatch(string tone)
    {
        return Ui.Hex(tone == "paper" ? "#F4F1EA" : tone == "graphite" ? "#2A2E33" : "#0B0D0B");
    }

    public static string ToneName(string tone)
    {
        return tone == "paper" ? "Kağıt" : tone == "graphite" ? "Grafit" : "Mürekkep";
    }

    public static Theme Create(Prefs p)
    {
        var t = new Theme();
        t.Kind = p.Surface;
        Color accent = Ui.Parse(p.Accent, Ui.Hex("#9FD08A"));
        if (p.Tone == "paper")
        {
            t.Light = true;
            t.Surface = Ui.Hex("#F4F1EA"); t.Raised = Ui.Hex("#E8E3D8"); t.Line = Ui.Hex("#D8D2C5");
            t.Text = Ui.Hex("#1A1C19"); t.Sub = Ui.Hex("#5E6159"); t.Faint = Ui.Hex("#8E9088"); t.Track = Ui.Hex("#DCD7CB");
            t.Accent = Ui.Luma(accent) > 0.45 ? Ui.Mix(accent, Colors.Black, 0.42) : accent;
            t.Warn = Ui.Hex("#B7791F"); t.Crit = Ui.Hex("#C2412D"); t.Stale = Ui.Hex("#A3A59C");
        }
        else
        {
            if (p.Tone == "graphite")
            {
                t.Surface = Ui.Hex("#1D2024"); t.Raised = Ui.Hex("#282C31"); t.Line = Ui.Hex("#343940");
                t.Text = Ui.Hex("#F2F4F6"); t.Sub = Ui.Hex("#A3ABB4"); t.Faint = Ui.Hex("#737B85"); t.Track = Ui.Hex("#373D44");
            }
            else
            {
                t.Surface = Ui.Hex("#0B0D0B"); t.Raised = Ui.Hex("#171A17"); t.Line = Ui.Hex("#242924");
                t.Text = Ui.Hex("#F3F5F0"); t.Sub = Ui.Hex("#9BA397"); t.Faint = Ui.Hex("#6B7368"); t.Track = Ui.Hex("#262C26");
            }
            t.Accent = accent;
            t.Warn = Ui.Hex("#F2B84B"); t.Crit = Ui.Hex("#F2705E"); t.Stale = Ui.Hex("#6F776C");
        }
        t.OnAccent = Ui.Luma(t.Accent) > 0.55 ? Ui.Hex("#0B0D0B") : Colors.White;
        return t;
    }

    // Arkaplan bulanıklığı WPF katmanlı pencerede yok; cam yarı saydam katman + ışık kenarıdır.
    public Brush SurfaceBrush()
    {
        if (Kind == "glass")
        {
            var gradient = new LinearGradientBrush(Ui.Alpha(Ui.Mix(Surface, Colors.White, Light ? 0.35 : 0.09), 0xCC), Ui.Alpha(Surface, 0xB0), 90);
            gradient.Freeze();
            return gradient;
        }
        if (Kind == "dark") return Ui.B(Ui.Alpha(Surface, 0xE4));
        return Ui.B(Surface);
    }

    public Brush LineBrush()
    {
        if (Kind == "glass") return Ui.B(Light ? Ui.Alpha(Colors.Black, 0x1C) : Ui.Alpha(Colors.White, 0x33));
        if (Kind == "dark") return Ui.B(Light ? Ui.Alpha(Colors.Black, 0x14) : Ui.Alpha(Colors.White, 0x18));
        return Ui.B(Line);
    }

    public Color UsageColor(double used)
    {
        return used >= 90 ? Crit : used >= 75 ? Warn : Accent;
    }

    public Color StateColor(SessionState state)
    {
        return state == SessionState.Waiting ? Warn : state == SessionState.Working ? Accent : Sub;
    }
}

// Kullanım halkası: iz + yay + ortada değer + köşede oturum noktası
internal sealed class RingView
{
    public readonly Grid Root;
    public readonly Shapes.Path Arc;
    public readonly TextBlock Value;
    public readonly Shapes.Ellipse Dot;
    public SessionState DotState = SessionState.None;
    private readonly double diameter;
    private readonly double stroke;

    public RingView(double diameter, double stroke, double fontSize, bool showValue, Theme theme)
    {
        this.diameter = diameter;
        this.stroke = stroke;
        Root = new Grid { Width = diameter, Height = diameter };
        Root.Children.Add(new Shapes.Ellipse { Width = diameter, Height = diameter, Stroke = Ui.B(theme.Track), StrokeThickness = stroke });
        Arc = new Shapes.Path { StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        Root.Children.Add(Arc);
        if (showValue)
        {
            Value = new TextBlock { FontSize = fontSize, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.B(theme.Text) };
            Root.Children.Add(Value);
        }
        double dot = Math.Max(6, Math.Round(diameter * 0.24));
        Dot = new Shapes.Ellipse
        {
            Width = dot, Height = dot, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Stroke = Ui.B(theme.Surface), StrokeThickness = diameter > 20 ? 2 : 1.2, Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, -dot * 0.25, -dot * 0.25, 0)
        };
        Root.Children.Add(Dot);
    }

    public void Set(double percent, Color color)
    {
        Arc.Stroke = Ui.B(color);
        Arc.Data = BuildArc(percent);
    }

    private Geometry BuildArc(double percent)
    {
        if (percent <= 0.05) return null;
        double r = (diameter - stroke) / 2, c = diameter / 2;
        if (percent >= 99.95) return new EllipseGeometry(new Point(c, c), r, r);
        double angle = percent / 100 * Math.PI * 2;
        var figure = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        figure.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle)), new Size(r, r), 0, percent > 50, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
