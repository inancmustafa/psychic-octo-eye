using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Shapes = System.Windows.Shapes;

internal sealed partial class Widget
{
    // ───────────────────────── Kurulum ─────────────────────────

    private void BuildUi()
    {
        theme = Theme.Create(prefs);
        rings.Clear(); minis.Clear(); miniTexts.Clear(); slots.Clear();
        string[] ids = EnabledIds();
        bool vertical = Vertical;

        notch = new Border { ClipToBounds = true, SnapsToDevicePixels = true };
        var layers = new Grid();
        sliverView = BuildSliver(vertical);
        idleView = BuildIdle(ids, vertical);
        expandedView = BuildExpanded(ids, vertical);
        layers.Children.Add(sliverView);
        layers.Children.Add(idleView);
        layers.Children.Add(expandedView);
        notch.Child = layers;

        shelf = BuildShelf(vertical);
        shelf.Visibility = shelfOpen ? Visibility.Visible : Visibility.Collapsed;

        card = new Border
        {
            Width = 316, CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 14, 16, 12),
            Background = theme.SurfaceBrush(), BorderBrush = theme.LineBrush(), BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed, RenderTransform = new TranslateTransform(), Effect = Shadow(theme.Light ? 0.16 : 0.42)
        };
        card.MouseEnter += delegate { cardTimer.Stop(); };
        card.MouseLeave += delegate { cardTimer.Start(); };

        Thickness gap = Gap(8);
        shelf.Margin = gap;
        card.Margin = gap;

        var stack = new StackPanel { Orientation = vertical ? Orientation.Horizontal : Orientation.Vertical };
        var order = new List<FrameworkElement> { notch, shelf, card };
        if (FarSide) order.Reverse();
        foreach (FrameworkElement element in order)
        {
            if (vertical) element.VerticalAlignment = VerticalAlignment.Center;
            else element.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(element);
        }
        switch (prefs.Edge)
        {
            case "bottom": stack.VerticalAlignment = VerticalAlignment.Bottom; stack.HorizontalAlignment = HorizontalAlignment.Center; break;
            case "left": stack.HorizontalAlignment = HorizontalAlignment.Left; stack.VerticalAlignment = VerticalAlignment.Center; break;
            case "right": stack.HorizontalAlignment = HorizontalAlignment.Right; stack.VerticalAlignment = VerticalAlignment.Center; break;
            default: stack.VerticalAlignment = VerticalAlignment.Top; stack.HorizontalAlignment = HorizontalAlignment.Center; break;
        }

        var root = new Grid { LayoutTransform = new ScaleTransform(Scale, Scale) };
        root.Children.Add(stack);
        Window.Content = root;

        ApplyNotch(false);
        if (cardProvider != null && rings.ContainsKey(cardProvider))
        {
            card.Child = BuildCard(cardProvider);
            card.Visibility = Visibility.Visible;
            cardVersion = version;
            cardBuilt = DateTime.UtcNow;
        }
        else cardProvider = null;
        Render();
    }

    private Thickness Gap(double size)
    {
        switch (prefs.Edge)
        {
            case "bottom": return new Thickness(0, 0, 0, size);
            case "left": return new Thickness(size, 0, 0, 0);
            case "right": return new Thickness(0, 0, size, 0);
            default: return new Thickness(0, size, 0, 0);
        }
    }

    // Kenara bakan köşeler düz, içe bakan köşeler yuvarlak
    private CornerRadius NotchCorners(double r)
    {
        switch (prefs.Edge)
        {
            case "bottom": return new CornerRadius(r, r, 0, 0);
            case "left": return new CornerRadius(0, r, r, 0);
            case "right": return new CornerRadius(r, 0, 0, r);
            default: return new CornerRadius(0, 0, r, r);
        }
    }

    private Thickness EdgeThickness()
    {
        switch (prefs.Edge)
        {
            case "bottom": return new Thickness(1, 1, 1, 0);
            case "left": return new Thickness(0, 1, 1, 1);
            case "right": return new Thickness(1, 1, 0, 1);
            default: return new Thickness(1, 0, 1, 1);
        }
    }

    private static Effect Shadow(double opacity)
    {
        return new DropShadowEffect { BlurRadius = 26, ShadowDepth = 5, Direction = 270, Opacity = opacity, Color = Colors.Black };
    }

    private FrameworkElement BuildSliver(bool vertical)
    {
        var strip = new Border { Background = theme.SurfaceBrush(), BorderBrush = theme.LineBrush(), BorderThickness = new Thickness(1), CornerRadius = NotchCorners(3), IsHitTestVisible = false };
        if (vertical)
        {
            strip.Width = 5; strip.Height = 72;
            strip.HorizontalAlignment = prefs.Edge == "left" ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            strip.VerticalAlignment = VerticalAlignment.Center;
        }
        else
        {
            strip.Height = 5; strip.Width = 72;
            strip.VerticalAlignment = prefs.Edge == "top" ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            strip.HorizontalAlignment = HorizontalAlignment.Center;
        }
        return strip;
    }

    // Dinlenme hâli: sağlayıcı başına küçük halka + yüzde
    private FrameworkElement BuildIdle(string[] ids, bool vertical)
    {
        var panel = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
        if (ids.Length == 0) { panel.Children.Add(Ui.T("—", 12, theme.Sub, FontWeights.SemiBold)); return panel; }
        foreach (string id in ids)
        {
            var mini = new RingView(vertical ? 16 : 14, 2.6, 0, false, theme);
            var text = Ui.T("—", vertical ? 10 : 11.5, theme.Text, FontWeights.SemiBold);
            var slot = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Margin = vertical ? new Thickness(0, 5, 0, 5) : new Thickness(7, 0, 7, 0) };
            mini.Root.HorizontalAlignment = HorizontalAlignment.Center;
            mini.Root.VerticalAlignment = VerticalAlignment.Center;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Margin = vertical ? new Thickness(0, 3, 0, 0) : new Thickness(6, 0, 0, 0);
            slot.Children.Add(mini.Root);
            slot.Children.Add(text);
            panel.Children.Add(slot);
            minis[id] = mini;
            miniTexts[id] = text;
        }
        return panel;
    }

    // Açık hâl: büyük halkalar + palet düğmesi
    private FrameworkElement BuildExpanded(string[] ids, bool vertical)
    {
        var panel = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        foreach (string id in ids) panel.Children.Add(RingSlot(id));
        if (ids.Length == 0)
        {
            var off = Ui.T("Sağlayıcı yok", 10.5, theme.Sub, FontWeights.Normal);
            off.VerticalAlignment = VerticalAlignment.Center;
            off.Margin = new Thickness(6);
            panel.Children.Add(off);
        }
        var palette = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Ui.B(theme.Raised), Cursor = Cursors.Hand,
            Margin = vertical ? new Thickness(0, 4, 0, 0) : new Thickness(4, 0, 0, 0), ToolTip = "Ton, yenileme ve ayarlar",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        palette.Child = new TextBlock
        {
            Text = "◐", FontSize = 14, Foreground = Ui.B(theme.Sub), FontFamily = new FontFamily("Segoe UI Symbol"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0)
        };
        palette.MouseEnter += delegate { palette.Background = Ui.B(Ui.Mix(theme.Raised, theme.Text, 0.12)); };
        palette.MouseLeave += delegate { palette.Background = Ui.B(theme.Raised); };
        palette.MouseLeftButtonUp += (s, e) =>
        {
            e.Handled = true;
            if (dragging) return;
            shelfOpen = !shelfOpen;
            UpdateShelf();
        };
        panel.Children.Add(palette);
        return panel;
    }

    private FrameworkElement RingSlot(string id)
    {
        var ring = new RingView(44, 4.2, 13, true, theme);
        var label = Ui.T(Names[id], 10.5, theme.Sub, FontWeights.Normal);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Margin = new Thickness(0, 5, 0, 0);
        ring.Root.HorizontalAlignment = HorizontalAlignment.Center;
        var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        inner.Children.Add(ring.Root);
        inner.Children.Add(label);
        var slot = new Border { Width = 70, Height = 72, CornerRadius = new CornerRadius(16), Background = Ui.Hit, Cursor = Cursors.Hand, Child = inner };
        // Halkanın üzerinde kısa süre durunca kartı aç; kart zaten açıksa hemen geçiş yap
        slot.MouseEnter += delegate
        {
            slot.Background = Ui.B(Ui.Alpha(theme.Text, 0x12));
            cardTimer.Stop();
            if (card.Visibility == Visibility.Visible) ShowCard(id);
            else { pendingCard = id; hoverTimer.Stop(); hoverTimer.Start(); }
        };
        slot.MouseLeave += delegate
        {
            slot.Background = Ui.Hit;
            hoverTimer.Stop();
            pendingCard = null;
            cardTimer.Start();
        };
        // Tıklama kartı sabitler; tekrar tıklama sabitlemeyi kaldırır
        slot.MouseLeftButtonUp += (s, e) =>
        {
            e.Handled = true;
            if (dragging) return;
            hoverTimer.Stop();
            cardPinned = !(cardPinned && cardProvider == id);
            ShowCard(id);
        };
        rings[id] = ring;
        slots[id] = slot;
        return slot;
    }

    // Ton rafı: üç hazır ton, vurgu rengi, yenile, ayarlar
    private Border BuildShelf(bool vertical)
    {
        var panel = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal };
        foreach (string tone in Theme.Tones)
        {
            string selected = tone;
            panel.Children.Add(Swatch(Theme.ToneSwatch(tone), prefs.Tone == tone, Theme.ToneName(tone), delegate { prefs.Tone = selected; Changed(); }));
        }
        panel.Children.Add(Separator(vertical));
        panel.Children.Add(Swatch(theme.Accent, false, "Vurgu rengi seç…", PickAccent));
        panel.Children.Add(Separator(vertical));
        panel.Children.Add(IconButton("↻", 13, "Şimdi yenile", delegate { Refresh(true); }));
        panel.Children.Add(IconButton("⚙", 13, "Ayarlar", OpenSettings));
        return new Border
        {
            CornerRadius = new CornerRadius(17), Padding = new Thickness(6, 5, 6, 5), Background = theme.SurfaceBrush(),
            BorderBrush = theme.LineBrush(), BorderThickness = new Thickness(1), Child = panel, Effect = Shadow(theme.Light ? 0.12 : 0.3)
        };
    }

    private FrameworkElement Separator(bool vertical)
    {
        return new Border
        {
            Background = Ui.B(theme.Line), Width = vertical ? 16 : 1, Height = vertical ? 1 : 16,
            Margin = vertical ? new Thickness(0, 4, 0, 4) : new Thickness(4, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
    }

    private FrameworkElement Swatch(Color color, bool selected, string tip, Action action)
    {
        var dot = new Shapes.Ellipse
        {
            Width = 18, Height = 18, Fill = Ui.B(color),
            Stroke = Ui.B(selected ? theme.Accent : Ui.Alpha(theme.Text, 0x40)), StrokeThickness = selected ? 2 : 1
        };
        var holder = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Ui.Hit, Cursor = Cursors.Hand, ToolTip = tip, Child = dot };
        holder.MouseEnter += delegate { holder.Background = Ui.B(Ui.Alpha(theme.Text, 0x14)); };
        holder.MouseLeave += delegate { holder.Background = Ui.Hit; };
        holder.MouseLeftButtonUp += (s, e) => { e.Handled = true; action(); };
        return holder;
    }

    private Border IconButton(string text, double size, string tip, Action action)
    {
        var label = Ui.T(text, size, theme.Sub, FontWeights.Normal);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        var button = new Border
        {
            Padding = new Thickness(7, 3, 7, 3), MinWidth = 28, MinHeight = 26, CornerRadius = new CornerRadius(9), Background = Ui.Hit,
            Cursor = Cursors.Hand, Child = label, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center
        };
        button.MouseEnter += delegate { button.Background = Ui.B(Ui.Alpha(theme.Text, 0x14)); label.Foreground = Ui.B(theme.Text); };
        button.MouseLeave += delegate { button.Background = Ui.Hit; label.Foreground = Ui.B(theme.Sub); };
        button.MouseLeftButtonUp += (s, e) => { e.Handled = true; action(); };
        return button;
    }

    private Border PillButton(string text, Action action)
    {
        var label = Ui.T(text, 11.5, theme.Text, FontWeights.SemiBold);
        var button = new Border
        {
            Padding = new Thickness(12, 5, 12, 6), CornerRadius = new CornerRadius(10), Background = Ui.B(theme.Raised),
            Cursor = Cursors.Hand, Child = label, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4)
        };
        button.MouseEnter += delegate { button.Background = Ui.B(Ui.Mix(theme.Raised, theme.Text, 0.1)); };
        button.MouseLeave += delegate { button.Background = Ui.B(theme.Raised); };
        button.MouseLeftButtonUp += (s, e) => { e.Handled = true; action(); };
        return button;
    }

    private FrameworkElement Divider()
    {
        return new Border { Height = 1, Background = theme.LineBrush(), Margin = new Thickness(0, 4, 0, 10) };
    }

    // ───────────────────────── Geçişler ─────────────────────────

    private Size NotchTarget(bool sliver)
    {
        int n = Math.Max(1, EnabledIds().Length);
        bool vertical = Vertical;
        if (expanded) return vertical ? new Size(84, 68 + n * 72) : new Size(68 + n * 70, 84);
        if (sliver) return vertical ? new Size(14, 110) : new Size(110, 14);
        return vertical ? new Size(40, 18 + n * 46) : new Size(22 + n * 64, 30);
    }

    private void ApplyNotch(bool animate)
    {
        bool sliver = !expanded && prefs.Reveal == "hover";
        Size target = NotchTarget(sliver);
        // İnce şerit hâlinde yüzey görünmez ama fareyi yakalar; böylece kenara dokunmak açmaya yeter.
        notch.Background = sliver ? (Brush)Ui.Hit : theme.SurfaceBrush();
        notch.BorderBrush = theme.LineBrush();
        notch.BorderThickness = sliver ? new Thickness(0) : EdgeThickness();
        notch.CornerRadius = NotchCorners(expanded ? 26 : 15);
        sliverView.Visibility = sliver ? Visibility.Visible : Visibility.Collapsed;
        Fade(idleView, !expanded && !sliver ? 1 : 0, animate);
        Fade(expandedView, expanded ? 1 : 0, animate);
        expandedView.IsHitTestVisible = expanded;
        Grow(notch, FrameworkElement.WidthProperty, target.Width, animate);
        Grow(notch, FrameworkElement.HeightProperty, target.Height, animate);
    }

    private void Fade(UIElement element, double to, bool animate)
    {
        if (!animate || noAnim)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = to;
            return;
        }
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 220 : 110)));
    }

    private void Grow(UIElement element, DependencyProperty property, double to, bool animate)
    {
        if (!animate || noAnim)
        {
            element.BeginAnimation(property, null);
            element.SetValue(property, to);
            return;
        }
        // Açılırken hafif yaylanma, kapanırken yumuşak geri çekilme
        IEasingFunction easing = expanded
            ? (IEasingFunction)new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 }
            : new CubicEase { EasingMode = EasingMode.EaseInOut };
        element.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(expanded ? 300 : 210)) { EasingFunction = easing });
    }

    private void UpdateShelf()
    {
        if (shelf == null) return;
        bool visible = shelfOpen && expanded;
        bool was = shelf.Visibility == Visibility.Visible;
        shelf.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible && !was && !noAnim) shelf.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    private double SlideFrom()
    {
        return prefs.Edge == "bottom" || prefs.Edge == "right" ? 8 : -8;
    }

    private void ShowCard(string id)
    {
        if (card == null || !rings.ContainsKey(id)) return;
        bool was = card.Visibility == Visibility.Visible;
        cardProvider = id;
        card.Child = BuildCard(id);
        cardVersion = version;
        cardBuilt = DateTime.UtcNow;
        card.BorderBrush = cardPinned ? Ui.B(Ui.Alpha(theme.Accent, 0xA0)) : theme.LineBrush();
        card.Visibility = Visibility.Visible;
        if (!was && !noAnim)
        {
            card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));
            var slide = card.RenderTransform as TranslateTransform;
            if (slide != null)
            {
                var motion = new DoubleAnimation(SlideFrom(), 0, TimeSpan.FromMilliseconds(230)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                slide.BeginAnimation(Vertical ? TranslateTransform.XProperty : TranslateTransform.YProperty, motion);
            }
        }
    }

    private void HideCard()
    {
        cardProvider = null;
        cardPinned = false;
        hoverTimer.Stop();
        if (card != null) card.Visibility = Visibility.Collapsed;
    }

    // Tema/kenar değişikliği: tercihi kaydet ve arayüzü (olay dışından) yeniden kur
    private void Changed()
    {
        SavePrefs();
        Window.Dispatcher.BeginInvoke(new Action(() =>
        {
            BuildUi();
            Place();
            UpdateTrayIcon();
            RebuildSettings();
        }));
    }

    private void PickAccent()
    {
        Color raw = Ui.Parse(prefs.Accent, Ui.Hex("#9FD08A"));
        using (var dialog = new Forms.ColorDialog { FullOpen = true, AnyColor = true, Color = Drawing.Color.FromArgb(raw.R, raw.G, raw.B) })
        {
            bool wasPeeking = peeking;
            peeking = true; // iletişim kutusu açıkken çentik katlanmasın
            var result = dialog.ShowDialog();
            peeking = wasPeeking;
            if (result != Forms.DialogResult.OK) return;
            prefs.Accent = Ui.Hex(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
            Changed();
        }
    }

    // ───────────────────────── Değerler ─────────────────────────

    private void Render()
    {
        if (theme == null || notch == null) return;
        foreach (string id in ProviderIds)
        {
            ProviderView view = Describe(id);
            Color color = RingColor(view);
            string value = ValueText(view);
            double percent = view.Used.HasValue ? view.Used.Value : 0;
            SessionState activity = Activity(id);
            RingView ring;
            if (rings.TryGetValue(id, out ring))
            {
                ring.Set(percent, color);
                ring.Value.Text = value;
                ring.Value.FontSize = value.Length >= 4 ? 11.5 : 13;
                ring.Value.Foreground = Ui.B(view.Used.HasValue ? theme.Text : value == "!" ? theme.Warn : theme.Faint);
                SetDot(ring, activity);
            }
            if (minis.TryGetValue(id, out ring))
            {
                ring.Set(percent, color);
                SetDot(ring, activity);
            }
            TextBlock text;
            if (miniTexts.TryGetValue(id, out text))
            {
                text.Text = value;
                text.Foreground = Ui.B(view.Used.HasValue && view.Fresh ? theme.Text : value == "!" ? theme.Warn : theme.Sub);
            }
        }
        if (cardProvider != null && card != null && card.Visibility == Visibility.Visible
            && (cardVersion != version || (DateTime.UtcNow - cardBuilt).TotalSeconds > 20))
        {
            card.Child = BuildCard(cardProvider);
            cardVersion = version;
            cardBuilt = DateTime.UtcNow;
        }
        UpdateTrayText();
    }

    private void SetDot(RingView ring, SessionState state)
    {
        if (state == SessionState.None)
        {
            ring.Dot.BeginAnimation(UIElement.OpacityProperty, null);
            ring.Dot.Visibility = Visibility.Collapsed;
            ring.DotState = state;
            return;
        }
        ring.Dot.Visibility = Visibility.Visible;
        if (ring.DotState == state) return; // nabız animasyonunu her yenilemede baştan başlatma
        ring.DotState = state;
        ring.Dot.Fill = Ui.B(theme.StateColor(state));
        if (state == SessionState.Working && !noAnim) ring.Dot.BeginAnimation(UIElement.OpacityProperty, Pulse());
        else { ring.Dot.BeginAnimation(UIElement.OpacityProperty, null); ring.Dot.Opacity = 1; }
    }

    private static DoubleAnimation Pulse()
    {
        return new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(850)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
    }

    // ───────────────────────── Kart ─────────────────────────

    private FrameworkElement BuildCard(string id)
    {
        ProviderView view = Describe(id);
        var panel = new StackPanel();

        var header = new DockPanel();
        var open = IconButton("↗", 13, "Sağlayıcının kullanım sayfasını aç", delegate { OpenUrl(UsagePages[id]); });
        DockPanel.SetDock(open, Dock.Right);
        header.Children.Add(open);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var glyph = Ui.T(Glyphs[id], 14, id == "claude" ? (theme.Light ? Ui.Hex("#B4552F") : Ui.Hex("#E0896A")) : theme.Text, FontWeights.Normal);
        glyph.FontFamily = new FontFamily("Segoe UI Symbol");
        glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.Margin = new Thickness(0, 0, 8, 0);
        title.Children.Add(glyph);
        var name = Ui.T(Names[id], 15.5, theme.Text, FontWeights.SemiBold);
        name.VerticalAlignment = VerticalAlignment.Center;
        title.Children.Add(name);
        if (cardPinned)
        {
            var pin = Ui.T("SABİT", 9, theme.Accent, FontWeights.SemiBold);
            pin.VerticalAlignment = VerticalAlignment.Center;
            pin.Margin = new Thickness(9, 2, 0, 0);
            title.Children.Add(pin);
        }
        header.Children.Add(title);
        panel.Children.Add(header);

        bool problem = view.Status == "login_required" || view.Status == "forbidden" || view.Status == "no_quota";
        var status = Ui.T(StatusLine(id, view), 11, problem ? theme.Warn : theme.Sub, FontWeights.Normal);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 2, 0, 12);
        panel.Children.Add(status);

        if (view.Windows.Length > 0)
        {
            foreach (var quota in view.Windows) panel.Children.Add(QuotaRow(quota, view.Fresh));
        }
        else
        {
            var message = Ui.T(ProblemText(id, view), 11.5, theme.Sub, FontWeights.Normal);
            message.TextWrapping = TextWrapping.Wrap;
            message.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(message);
            if (view.Status == "login_required" || view.Status == "forbidden")
                panel.Children.Add(PillButton(id == "claude" ? "Claude’u bağla ↗" : "Codex’i bağla ↗", delegate { Connect(id); }));
        }

        panel.Children.Add(Divider());
        panel.Children.Add(SessionsBlock(id));
        panel.Children.Add(Divider());

        var footer = new DockPanel();
        bool busy = collector != null && !collector.HasExited;
        var refresh = IconButton(busy ? "…" : "↻  Yenile", 11, "Şimdi yenile", delegate { Refresh(true); });
        DockPanel.SetDock(refresh, Dock.Right);
        footer.Children.Add(refresh);
        var foot = Ui.T(note, 10, theme.Faint, FontWeights.Normal);
        foot.TextWrapping = TextWrapping.Wrap;
        foot.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(foot);
        panel.Children.Add(footer);
        return panel;
    }

    private string StatusLine(string id, ProviderView view)
    {
        string source = id == "claude" ? "Claude Code oturumu" : "Codex CLI oturumu";
        if (demo) return "Örnek değerler · hesap okunmuyor";
        if (!view.HasData) return source + " · bağlanıyor…";
        if (view.Status == "login_required") return source + " · giriş gerekli";
        if (view.Status == "forbidden") return source + " · kota okuma izni yok";
        if (view.Status == "no_quota") return source + " · kota bilgisi dönmedi";
        if (view.Windows.Length == 0) return source + " · bağlantı bekleniyor";
        return source + " · " + (view.Fresh ? "güncel" : "eski veri") + " · " + Ago(view.Age);
    }

    private static string ProblemText(string id, ProviderView view)
    {
        switch (view.Status)
        {
            case "login_required":
                return id == "claude" ? "Masaüstünde kullandığın Claude hesabıyla Claude Code’a bir kez giriş yap." : "Kullanım kotasını görmek için Codex hesabına giriş yap.";
            case "forbidden": return "Bu oturumun kota okuma izni yok. Abonelik hesabınla yeniden bağlan.";
            case "no_quota": return "Bu hesap için kullanım kotası dönmedi.";
            case "": return "Kullanım bilgisi alınıyor…";
            default: return "Veri alınamadı. Bağlantıyı kontrol edip Yenile’ye bas.";
        }
    }

    private FrameworkElement QuotaRow(Dictionary<string, object> quota, bool fresh)
    {
        bool expired = Expired(quota);
        double used = Number(quota, "usedPercent") ?? 0;
        Color color = !fresh || expired ? theme.Stale : theme.UsageColor(used);
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var top = new DockPanel();
        var percent = Ui.T(expired ? "—" : "%" + Math.Round(used).ToString(Inv), 12.5, theme.Text, FontWeights.SemiBold);
        DockPanel.SetDock(percent, Dock.Right);
        top.Children.Add(percent);
        top.Children.Add(Ui.T(Text(quota, "label"), 11.5, theme.Sub, FontWeights.Normal));
        row.Children.Add(top);
        var bar = Bar(expired ? 0 : used, color);
        bar.Margin = new Thickness(0, 6, 0, 0);
        row.Children.Add(bar);
        var reset = Ui.T(ResetText(quota), 10.5, theme.Faint, FontWeights.Normal);
        reset.Margin = new Thickness(0, 5, 0, 0);
        row.Children.Add(reset);
        return row;
    }

    private FrameworkElement Bar(double percent, Color color)
    {
        percent = Math.Max(0, Math.Min(100, percent));
        var grid = new Grid { Height = 5 };
        grid.Children.Add(new Border { Background = Ui.B(theme.Track), CornerRadius = new CornerRadius(2.5) });
        if (percent > 0)
        {
            // Yıldız sütunlarıyla oran; ölçüm gerektirmez
            var fill = new Grid();
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(percent, GridUnitType.Star) });
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - percent, GridUnitType.Star) });
            fill.Children.Add(new Border { Background = Ui.B(color), CornerRadius = new CornerRadius(2.5), MinWidth = 5 });
            grid.Children.Add(fill);
        }
        return grid;
    }

    private FrameworkElement SessionsBlock(string id)
    {
        var all = sessions.Where(s => s.Provider == id).ToList();
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var count = Ui.T(all.Count == 0 ? "" : all.Count.ToString(Inv), 10, theme.Faint, FontWeights.SemiBold);
        DockPanel.SetDock(count, Dock.Right);
        head.Children.Add(count);
        head.Children.Add(Ui.T("AJAN OTURUMLARI", 9.5, theme.Faint, FontWeights.SemiBold));
        panel.Children.Add(head);
        if (all.Count == 0)
        {
            panel.Children.Add(Ui.T("Son 3 saatte etkinlik yok.", 11, theme.Faint, FontWeights.Normal));
            return panel;
        }
        foreach (var session in all.Take(4))
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            Color color = theme.StateColor(session.State);
            var dot = new Shapes.Ellipse { Width = 7, Height = 7, Fill = Ui.B(color), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            if (session.State == SessionState.Working && !noAnim) dot.BeginAnimation(UIElement.OpacityProperty, Pulse());
            row.Children.Add(dot);
            var name = Ui.T(session.Project, 11.5, theme.Text, FontWeights.Normal);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.ToolTip = session.Project;
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var state = Ui.T(StateText(session.State), 11, color, session.State == SessionState.Finished ? FontWeights.Normal : FontWeights.SemiBold);
            state.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(state, 2);
            row.Children.Add(state);
            var age = Ui.T(Short(DateTime.UtcNow - session.LastWriteUtc), 10.5, theme.Faint, FontWeights.Normal);
            age.HorizontalAlignment = HorizontalAlignment.Right;
            age.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(age, 3);
            row.Children.Add(age);
            panel.Children.Add(row);
        }
        if (all.Count > 4) panel.Children.Add(Ui.T("+" + (all.Count - 4).ToString(Inv) + " oturum daha", 10.5, theme.Faint, FontWeights.Normal));
        return panel;
    }

    private static string StateText(SessionState state)
    {
        return state == SessionState.Waiting ? "Seni bekliyor" : state == SessionState.Working ? "Çalışıyor" : "Bitti";
    }

    private static string Short(TimeSpan span)
    {
        if (span.TotalSeconds < 60) return "şimdi";
        if (span.TotalMinutes < 60) return Math.Floor(span.TotalMinutes).ToString(Inv) + " dk";
        return Math.Floor(span.TotalHours).ToString(Inv) + " sa";
    }

    private static string Ago(double? seconds)
    {
        if (!seconds.HasValue) return "zaman bilinmiyor";
        if (seconds.Value < 60) return "az önce";
        if (seconds.Value < 3600) return Math.Floor(seconds.Value / 60).ToString(Inv) + " dk önce";
        return Math.Floor(seconds.Value / 3600).ToString(Inv) + " sa önce";
    }

    private static string ResetText(Dictionary<string, object> quota)
    {
        double? reset = Number(quota, "resetsAt");
        if (!reset.HasValue) return "Yenilenme zamanı bilinmiyor";
        double seconds = (reset.Value - Now) / 1000;
        if (seconds <= 0) return "Süre doldu · yeni ölçüm bekleniyor";
        var span = TimeSpan.FromSeconds(seconds);
        string relative;
        if (span.TotalDays >= 1) relative = Math.Floor(span.TotalDays).ToString(Inv) + " gün " + span.Hours.ToString(Inv) + " sa sonra";
        else if (span.TotalHours >= 1) relative = Math.Floor(span.TotalHours).ToString(Inv) + " sa " + span.Minutes.ToString(Inv) + " dk sonra";
        else relative = span.TotalMinutes >= 1 ? Math.Ceiling(span.TotalMinutes).ToString(Inv) + " dk sonra" : "1 dk içinde";
        DateTime local = Epoch.AddMilliseconds(reset.Value).ToLocalTime();
        string clock = local.Date == DateTime.Now.Date ? local.ToString("HH:mm", Tr) : local.ToString("ddd HH:mm", Tr);
        return "Yenilenme " + relative + " · " + clock;
    }
}
