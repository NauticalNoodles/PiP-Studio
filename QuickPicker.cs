using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PipStudio
{
    class PickResult
    {
        public IntPtr Window = IntPtr.Zero;
        public System.Drawing.Rectangle Screen = System.Drawing.Rectangle.Empty;
        public bool PickOnScreen, Region;
    }

    // =================================================================================
    //  Window picker (Ctrl+Alt+P, or "Change source…"): a small window in the same style as PiP Studio's
    //  Windows page — page title and commands, a search box, the list of windows and displays, and a details
    //  card with one live preview of the selected window and the button that opens it.
    // =================================================================================
    class QuickPicker
    {
        /// <summary>One row of the list (bound by the row template).</summary>
        public class Row
        {
            public IntPtr Hwnd;
            public System.Drawing.Rectangle ScreenBounds;
            public bool IsDisplay, Minimized;
            public string Title { get; set; }
            public string App { get; set; }
            public string Status { get; set; }
            public ImageSource Icon { get; set; }
            public string Glyph { get; set; }
            public ImageSource Snap;
        }

        const string Xaml = @"
<ui:FluentWindow xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        xmlns:ui='http://schemas.lepo.co/wpfui/2022/xaml'
        Title='Choose a window' Width='980' Height='640' MinWidth='640' MinHeight='440' UseLayoutRounding='True'
        FontFamily='Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI' FontSize='14'
        ExtendsContentIntoTitleBar='True' WindowBackdropType='Mica' WindowStartupLocation='CenterScreen'
        ShowInTaskbar='False' Topmost='True'>
  <ui:FluentWindow.Resources>
" + MainUI.SharedStyles + @"    <DataTemplate x:Key='PickRow'>
      <Grid Height='36'>
        <Grid.ColumnDefinitions><ColumnDefinition Width='*' MinWidth='120'/><ColumnDefinition Width='150'/><ColumnDefinition Width='100'/></Grid.ColumnDefinitions>
        <DockPanel Grid.Column='0' Margin='0,0,12,0'>
          <Grid DockPanel.Dock='Left' Width='28'>
            <Image Source='{Binding Icon}' Width='16' Height='16' HorizontalAlignment='Left' VerticalAlignment='Center' RenderOptions.BitmapScalingMode='HighQuality'/>
            <TextBlock Text='{Binding Glyph}' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='16' VerticalAlignment='Center' Foreground='{DynamicResource Sub}'/>
          </Grid>
          <TextBlock Text='{Binding Title}' Style='{StaticResource Cell}'/>
        </DockPanel>
        <TextBlock Grid.Column='1' Text='{Binding App}' Style='{StaticResource CellSub}' Margin='0,0,8,0'/>
        <TextBlock Grid.Column='2' Text='{Binding Status}' Style='{StaticResource CellSub}'/>
      </Grid>
    </DataTemplate>
  </ui:FluentWindow.Resources>
  <Grid>
    <Grid.RowDefinitions><RowDefinition Height='32'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
    <ui:TitleBar Grid.Row='0' Title='' ShowMaximize='False' ShowMinimize='False'/>
    <StackPanel Grid.Row='0' Orientation='Horizontal' VerticalAlignment='Center' IsHitTestVisible='False' Margin='16,0,0,0'>
      <Image x:Name='AppIcon' Width='16' Height='16' Margin='0,0,12,0' RenderOptions.BitmapScalingMode='HighQuality'/>
      <TextBlock Text='PiP Studio' FontSize='12' VerticalAlignment='Center' Foreground='{DynamicResource Text}'/>
    </StackPanel>

    <StackPanel x:Name='Header' Grid.Row='1' Margin='36,14,28,10'>
      <TextBlock x:Name='Head' Style='{StaticResource PageTitle}' Text='Choose a window' TextTrimming='CharacterEllipsis'/>
      <TextBlock x:Name='SubHead' Style='{StaticResource Caption}' FontSize='13' Margin='0,2,0,0' Text='It opens in a floating picture-in-picture window'/>
      <WrapPanel Orientation='Horizontal' Margin='-11,8,0,0'>
        <ui:Button x:Name='PickBtn' Appearance='Transparent' BorderThickness='0' Height='36' Padding='11,4' Margin='0,0,4,2'>
          <StackPanel Orientation='Horizontal'><TextBlock Text='&#xE7C9;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='16' Margin='0,0,8,0' VerticalAlignment='Center'/><TextBlock Text='Click a window' VerticalAlignment='Center'/></StackPanel>
        </ui:Button>
        <ui:Button x:Name='RegionBtn' Appearance='Transparent' BorderThickness='0' Height='36' Padding='11,4' Margin='0,0,4,2'>
          <StackPanel Orientation='Horizontal'><TextBlock Text='&#xE7A8;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='16' Margin='0,0,8,0' VerticalAlignment='Center'/><TextBlock Text='Screen region' VerticalAlignment='Center'/></StackPanel>
        </ui:Button>
        <ui:Button x:Name='RefreshBtn' Appearance='Transparent' BorderThickness='0' Height='36' Padding='11,4' Margin='0,0,4,2'>
          <StackPanel Orientation='Horizontal'><TextBlock Text='&#xE72C;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='16' Margin='0,0,8,0' VerticalAlignment='Center'/><TextBlock Text='Refresh' VerticalAlignment='Center'/></StackPanel>
        </ui:Button>
      </WrapPanel>
    </StackPanel>

    <Grid Grid.Row='2' Margin='24,0,28,12'>
      <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='16'/><ColumnDefinition x:Name='DetailsCol' Width='300'/></Grid.ColumnDefinitions>
      <DockPanel>
        <Grid DockPanel.Dock='Top' Width='320' HorizontalAlignment='Left' Margin='12,0,0,12'>
          <TextBox x:Name='Search' Height='34' Padding='11,6,34,6' VerticalContentAlignment='Center'/>
          <TextBlock x:Name='Hint' Text='Search windows' Foreground='{DynamicResource Tertiary}' IsHitTestVisible='False' VerticalAlignment='Center' Margin='12,0,34,0' FontSize='14'/>
          <TextBlock Text='&#xE721;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='12' Foreground='{DynamicResource Sub}' IsHitTestVisible='False' VerticalAlignment='Center' HorizontalAlignment='Right' Margin='0,0,12,0'/>
        </Grid>
        <Grid DockPanel.Dock='Top' Height='32' Margin='26,0,22,0'>
          <Grid.ColumnDefinitions><ColumnDefinition Width='*' MinWidth='120'/><ColumnDefinition Width='150'/><ColumnDefinition Width='100'/></Grid.ColumnDefinitions>
          <TextBlock Grid.Column='0' Text='Name' Style='{StaticResource CellSub}' FontSize='12' Margin='28,0,0,0'/>
          <TextBlock Grid.Column='1' Text='App' Style='{StaticResource CellSub}' FontSize='12'/>
          <TextBlock Grid.Column='2' Text='Status' Style='{StaticResource CellSub}' FontSize='12'/>
        </Grid>
        <Border DockPanel.Dock='Top' Height='1' Background='{DynamicResource Divider}' Margin='12,0,0,4'/>
        <Grid>
          <ListBox x:Name='List' Margin='12,0,0,0' Background='Transparent' BorderThickness='0' Padding='0' ItemTemplate='{StaticResource PickRow}' ItemContainerStyle='{StaticResource RowItem}'
                   HorizontalContentAlignment='Stretch' ScrollViewer.HorizontalScrollBarVisibility='Disabled' VirtualizingPanel.ScrollUnit='Pixel'/>
          <TextBlock x:Name='Empty' Text='No matching windows' Style='{StaticResource Caption}' FontSize='14' HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/>
        </Grid>
      </DockPanel>

      <Border x:Name='Details' Grid.Column='2' VerticalAlignment='Top' Padding='16,14,16,16' CornerRadius='8' BorderThickness='1' Background='{DynamicResource Card}' BorderBrush='{DynamicResource CardBorder}'>
        <StackPanel>
          <Border x:Name='PreviewBox' Height='160' CornerRadius='4' Background='{DynamicResource Preview}' ClipToBounds='True'>
            <Grid>
              <Image x:Name='PreviewImage' Stretch='Uniform' Margin='6'/>
              <StackPanel x:Name='PreviewNoteBox' VerticalAlignment='Center' HorizontalAlignment='Center' Margin='12'>
                <Image x:Name='PreviewIcon' Width='40' Height='40' Margin='0,0,0,8' RenderOptions.BitmapScalingMode='HighQuality'/>
                <TextBlock x:Name='PreviewNote' Style='{StaticResource Caption}' TextAlignment='Center' Text='Live preview'/>
              </StackPanel>
            </Grid>
          </Border>
          <TextBlock x:Name='SelTitle' FontSize='14' FontWeight='SemiBold' Foreground='{DynamicResource Text}' Margin='0,14,0,0' TextWrapping='Wrap' MaxHeight='40' TextTrimming='CharacterEllipsis'/>
          <TextBlock x:Name='SelSub' Style='{StaticResource Caption}' Margin='0,2,0,12'/>
          <ui:Button x:Name='OpenBtn' Appearance='Primary' HorizontalAlignment='Stretch' Height='32' Content='Open in picture-in-picture'/>
        </StackPanel>
      </Border>
    </Grid>

    <TextBlock x:Name='Foot' Grid.Row='3' Margin='36,0,28,14' Style='{StaticResource Caption}' Foreground='{DynamicResource Tertiary}'
               Text='Enter opens the selected window  ·  Arrow keys move  ·  Type to search  ·  Esc closes'/>
  </Grid>
</ui:FluentWindow>";

        public static PickResult Show(bool changingSource)
        {
            QuickPicker q;
            try { q = new QuickPicker(changingSource); }
            catch (Exception ex) { App.ReportError(ex); return null; }
            try { q.win.ShowDialog(); }
            catch (Exception ex) { App.ReportError(ex); }
            return q.result;
        }

        readonly Window win;
        readonly bool lite, changing;
        readonly ListBox list;
        readonly TextBox search;
        readonly TextBlock hint, empty, selTitle, selSub, previewNote;
        readonly Border previewBox, details;
        readonly Image previewImage, previewIcon;
        readonly FrameworkElement noteBox;
        readonly Wpf.Ui.Controls.Button openBtn;
        readonly ColumnDefinition detailsCol;
        List<Row> all = new List<Row>();
        PickResult result;
        bool closing;
        IntPtr hwnd;
        // the one live preview: the selected window's thumbnail
        IntPtr thumb, thumbSrc;
        System.Drawing.Rectangle thumbRect;
        readonly DispatcherTimer tick;

        QuickPicker(bool changingSource)
        {
            changing = changingSource;
            Fluent.PickBrushes();
            lite = App.S.LiteMode;
            win = (Window)XamlReader.Parse((lite ? MainUI.LiteXaml(Xaml) : Xaml).Replace("Microsoft YaHei UI", L.Cjk));
            L.Watch(win);
            list = (ListBox)win.FindName("List");
            search = (TextBox)win.FindName("Search");
            hint = (TextBlock)win.FindName("Hint");
            empty = (TextBlock)win.FindName("Empty");
            selTitle = (TextBlock)win.FindName("SelTitle");
            selSub = (TextBlock)win.FindName("SelSub");
            previewNote = (TextBlock)win.FindName("PreviewNote");
            previewBox = (Border)win.FindName("PreviewBox");
            previewImage = (Image)win.FindName("PreviewImage");
            previewIcon = (Image)win.FindName("PreviewIcon");
            noteBox = (FrameworkElement)win.FindName("PreviewNoteBox");
            details = (Border)win.FindName("Details");
            detailsCol = (ColumnDefinition)win.FindName("DetailsCol");
            openBtn = (Wpf.Ui.Controls.Button)win.FindName("OpenBtn");
            if (changing)
            {
                ((TextBlock)win.FindName("Head")).Text = "Change what this PiP shows";
                ((TextBlock)win.FindName("SubHead")).Text = "Pick a window or a display for this PiP";
                openBtn.Content = "Show in this PiP";
            }
            SetAppIcon();

            // about two thirds of the display under the pointer (CenterScreen puts it on that display)
            var cur = System.Windows.Forms.Cursor.Position;
            var wa = System.Windows.Forms.Screen.FromPoint(cur).WorkingArea;
            double s = U.ScaleAt(cur);
            win.Width = Math.Max(720, Math.Min(1100, wa.Width / s * 0.62));
            win.Height = Math.Max(480, Math.Min(760, wa.Height / s * 0.70));

            ((Button)win.FindName("PickBtn")).Click += (o, e) => Finish(new PickResult { PickOnScreen = true });
            ((Button)win.FindName("RegionBtn")).Click += (o, e) => Finish(new PickResult { Region = true });
            ((Button)win.FindName("RefreshBtn")).Click += (o, e) => { Load(); ApplyFilter(); search.Focus(); };
            openBtn.Click += (o, e) => Choose(list.SelectedItem as Row);
            search.TextChanged += (o, e) => { hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; ApplyFilter(); };
            list.SelectionChanged += (o, e) => { ShowDetails(list.SelectedItem as Row); if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem); };
            list.MouseDoubleClick += (o, e) =>
            {
                var c = ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) as ListBoxItem;
                if (c != null) Choose(c.DataContext as Row);
            };
            win.PreviewKeyDown += OnKey;
            win.SizeChanged += (o, e) => FitDetails();
            win.Deactivated += (o, e) => { /* stays open: you may be checking another window */ };
            win.SourceInitialized += (o, e) =>
            {
                hwnd = new WindowInteropHelper(win).Handle;
                MainUI.Cloak(hwnd, true);                                   // hidden until the first frame is drawn: no white flash
                U.ExcludeFromCapture(hwnd, false);
            };
            win.Loaded += (o, e) =>
            {
                Anim.Enter((UIElement)win.FindName("Header"), 8, 0, 260);
                MainUI.UncloakAfterRender(hwnd);
                // a safety net: if the first frame never reports in, show the window anyway
                var safety = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                safety.Tick += (o2, e2) => { safety.Stop(); try { MainUI.Cloak(hwnd, false); } catch { } };
                safety.Start();
                win.Activate();
                U.ForceForeground(hwnd);
                search.Focus();
                FitDetails();
                ShowDetails(list.SelectedItem as Row);
            };
            win.Closed += (o, e) => { tick.Stop(); DropThumb(); };

            tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            tick.Tick += (o, e) => PlaceThumb();
            tick.Start();

            Load();
            ApplyFilter();
        }

        void SetAppIcon()
        {
            try
            {
                if (App.AppIconHandle == IntPtr.Zero) return;
                var src = Imaging.CreateBitmapSourceFromHIcon(App.AppIconHandle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                ((Image)win.FindName("AppIcon")).Source = src;
            }
            catch { }
        }

        void Finish(PickResult r)
        {
            if (closing) return;
            closing = true;
            result = r;
            DropThumb();
            try { win.Close(); } catch { }
        }

        // ------------------------------------------------------------------ rows
        static ImageSource IconOf(IntPtr h)
        {
            try
            {
                IntPtr hi = U.WindowIconHandle(h, true);
                if (hi == IntPtr.Zero) return null;
                var bs = Imaging.CreateBitmapSourceFromHIcon(hi, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                return bs;
            }
            catch { return null; }
        }

        static ImageSource SnapOf(System.Drawing.Rectangle r)
        {
            try
            {
                using (var big = new System.Drawing.Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                {
                    using (var g = System.Drawing.Graphics.FromImage(big)) g.CopyFromScreen(r.Location, System.Drawing.Point.Empty, r.Size);
                    int w = Math.Min(560, r.Width), h = Math.Max(1, r.Height * w / r.Width);
                    using (var small = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                    {
                        using (var g = System.Drawing.Graphics.FromImage(small))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                            g.DrawImage(big, 0, 0, w, h);
                        }
                        IntPtr hb = small.GetHbitmap();
                        try
                        {
                            var bs = Imaging.CreateBitmapSourceFromHBitmap(hb, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            bs.Freeze();
                            return bs;
                        }
                        finally { Native.DeleteObject(hb); }
                    }
                }
            }
            catch { return null; }
        }

        void Load()
        {
            var rows = new List<Row>();
            try { U.ClearProcessCache(); } catch { }
            IEnumerable<IntPtr> wins;
            try { wins = WinEnum.AltTab().ToList(); } catch { wins = new List<IntPtr>(); }
            foreach (var h in wins)
            {
                if (h == hwnd) continue;
                try
                {
                    bool min = Native.IsIconic(h);
                    string title = U.WindowTitle(h);
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    var icon = IconOf(h);
                    rows.Add(new Row
                    {
                        Hwnd = h, Title = title, App = U.ProcessName(h), Minimized = min,
                        Status = min ? "Minimized" : "", Icon = icon, Glyph = icon == null ? "" : ""
                    });
                }
                catch { }
            }
            var screens = System.Windows.Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var b = screens[i].Bounds;
                rows.Add(new Row
                {
                    IsDisplay = true, ScreenBounds = b, Glyph = "",
                    Title = "Display " + (i + 1) + (screens[i].Primary ? " (main)" : ""),
                    App = "Whole screen", Status = b.Width + " × " + b.Height
                });
            }
            all = rows;
        }

        void ApplyFilter()
        {
            string q = search.Text.Trim();
            var keep = list.SelectedItem as Row;
            var view = all.Where(r => q.Length == 0
                                      || (r.Title ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                                      || (r.App ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                                      || L.T(r.Title ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            list.ItemsSource = view;
            empty.Visibility = view.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (view.Count == 0) { list.SelectedItem = null; ShowDetails(null); return; }
            list.SelectedItem = keep != null && view.Contains(keep) ? keep : view[0];
        }

        void Choose(Row r)
        {
            if (r == null) return;
            if (r.IsDisplay) Finish(new PickResult { Screen = r.ScreenBounds });
            else if (Native.IsWindow(r.Hwnd)) Finish(new PickResult { Window = r.Hwnd });
            else { Load(); ApplyFilter(); }                                // it closed meanwhile: refresh the list
        }

        // ------------------------------------------------------------------ details card
        void FitDetails()
        {
            // narrow window: the card gets narrower before the list does
            double wd = win.ActualWidth > 0 ? win.ActualWidth : win.Width;
            detailsCol.Width = new GridLength(wd < 820 ? 240 : 300);
        }

        void ShowDetails(Row r)
        {
            DropThumb();
            previewImage.Source = null;
            previewIcon.Source = null;
            previewIcon.Visibility = Visibility.Collapsed;
            noteBox.Visibility = Visibility.Visible;
            openBtn.IsEnabled = r != null;
            if (r == null)
            {
                selTitle.Text = "Nothing selected";
                selSub.Text = "Pick a window in the list.";
                previewNote.Text = "Live preview";
                return;
            }
            selTitle.Text = r.Title;
            selSub.Text = r.IsDisplay ? r.App + " · " + r.Status : r.App + (r.Minimized ? " · Minimized" : "");
            if (r.IsDisplay)
            {
                if (r.Snap == null) r.Snap = SnapOf(r.ScreenBounds);
                previewImage.Source = r.Snap;
                noteBox.Visibility = r.Snap == null ? Visibility.Visible : Visibility.Collapsed;
                previewNote.Text = "Mirrors this whole display";
                return;
            }
            if (r.Minimized || lite || !Native.IsWindow(r.Hwnd))
            {
                previewIcon.Source = r.Icon;
                previewIcon.Visibility = r.Icon != null ? Visibility.Visible : Visibility.Collapsed;
                previewNote.Text = r.Minimized ? "Minimized — it will be restored" : r.App;
                return;
            }
            previewNote.Text = "Live preview";
            thumbSrc = r.Hwnd;
            PlaceThumb();
        }

        void DropThumb()
        {
            if (thumb != IntPtr.Zero) { try { Native.DwmUnregisterThumbnail(thumb); } catch { } }
            thumb = IntPtr.Zero;
            thumbSrc = IntPtr.Zero;
            thumbRect = System.Drawing.Rectangle.Empty;
        }

        /// <summary>Keeps the selected window's live preview inside the preview box (it follows resizes and the card's width).</summary>
        void PlaceThumb()
        {
            if (thumbSrc == IntPtr.Zero || hwnd == IntPtr.Zero || closing || !win.IsLoaded) return;
            if (!Native.IsWindow(thumbSrc)) { DropThumb(); previewNote.Text = "This window has closed"; return; }
            if (thumb == IntPtr.Zero)
            {
                IntPtr t;
                if (Native.DwmRegisterThumbnail(hwnd, thumbSrc, out t) != 0 || t == IntPtr.Zero)
                {
                    thumbSrc = IntPtr.Zero;
                    previewNote.Text = "No preview for this window";    // it can still be opened
                    return;
                }
                thumb = t;
            }
            System.Drawing.Rectangle box;
            try
            {
                var dpi = VisualTreeHelper.GetDpi(win);
                Point p = previewBox.TransformToAncestor(win).Transform(new Point(0, 0));
                int pad = (int)(6 * dpi.DpiScaleX);
                box = new System.Drawing.Rectangle((int)Math.Round(p.X * dpi.DpiScaleX) + pad, (int)Math.Round(p.Y * dpi.DpiScaleY) + pad,
                    (int)Math.Round(previewBox.ActualWidth * dpi.DpiScaleX) - 2 * pad, (int)Math.Round(previewBox.ActualHeight * dpi.DpiScaleY) - 2 * pad);
            }
            catch { return; }
            if (box.Width < 4 || box.Height < 4) return;
            Native.SIZE sz;
            if (Native.DwmQueryThumbnailSourceSize(thumb, out sz) != 0 || sz.cx < 2 || sz.cy < 2) return;
            var dst = U.FitRect(new System.Drawing.Size(sz.cx, sz.cy), box);
            if (dst == thumbRect) return;
            thumbRect = dst;
            noteBox.Visibility = Visibility.Collapsed;
            var props = new Native.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_VISIBLE | Native.DWM_TNP_OPACITY,
                rcDestination = Native.RECT.From(dst),
                opacity = 255,
                fVisible = true
            };
            Native.DwmUpdateThumbnailProperties(thumb, ref props);
        }

        // ------------------------------------------------------------------ keys: typing always goes to the search box
        void OnKey(object sender, KeyEventArgs e)
        {
            var items = list.ItemsSource as List<Row>;
            int n = items == null ? 0 : items.Count;
            int i = list.SelectedIndex;
            switch (e.Key)
            {
                case Key.Escape:
                    if (search.Text.Length > 0) search.Text = ""; else Finish(null);
                    e.Handled = true; break;
                case Key.Enter: Choose(list.SelectedItem as Row); e.Handled = true; break;
                case Key.Down: if (n > 0) list.SelectedIndex = Math.Min(n - 1, i + 1); e.Handled = true; break;
                case Key.Up: if (n > 0) list.SelectedIndex = Math.Max(0, i - 1); e.Handled = true; break;
                case Key.PageDown: if (n > 0) list.SelectedIndex = Math.Min(n - 1, i + 8); e.Handled = true; break;
                case Key.PageUp: if (n > 0) list.SelectedIndex = Math.Max(0, i - 8); e.Handled = true; break;
                case Key.Home: if (n > 0 && search.Text.Length == 0) { list.SelectedIndex = 0; e.Handled = true; } break;
                case Key.End: if (n > 0 && search.Text.Length == 0) { list.SelectedIndex = n - 1; e.Handled = true; } break;
                case Key.F5: Load(); ApplyFilter(); e.Handled = true; break;
                default:
                    if (!search.IsKeyboardFocusWithin && !(e.OriginalSource is TextBox)) search.Focus();   // keep typing into search
                    break;
            }
        }
    }
}
