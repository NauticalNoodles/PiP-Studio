using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace PipStudio
{
    /// <summary>
    /// The look of the main PiP Studio window (Settings > Appearance > Window style): Windows 11 (the normal look),
    /// Windows 10 (flat, square corners, solid colors) or Windows 7 (Aero: light, blue gradients, glossy buttons).
    /// Only the main window changes; PiPs, menus and the window picker keep their look.
    /// How: the window's XAML is rewritten before it's parsed (corners, fonts, no Mica), and the window gets its own
    /// resources that win over the app's (colors, WPF-UI's corner sizes, and for Windows 7 WPF-UI's light theme and
    /// button styles). Changing the style rebuilds the window, like changing the language.
    /// </summary>
    static class Looks
    {
        public const int Win11 = 0, Win10 = 1, Win7 = 2;
        public static readonly string[] Names = { "Windows 11", "Windows 10", "Windows 7" };

        public static int Current { get { return App.S == null ? Win11 : Math.Max(Win11, Math.Min(Win7, App.S.WindowStyle)); } }

        /// <summary>Windows 7 had no dark mode, so its look is always light.</summary>
        public static bool Dark { get { return Theme.Dark && Current != Win7; } }

        // ------------------------------------------------------------ corners
        /// <summary>A corner size in this look: square for Windows 10, small for Windows 7.</summary>
        public static double R(double r) { return Current == Win10 ? 0 : Current == Win7 ? Math.Min(r, 3) : r; }
        public static CornerRadius CR(double r) { return new CornerRadius(R(r)); }
        public static CornerRadius CR(double tl, double tr, double br, double bl) { return new CornerRadius(R(tl), R(tr), R(br), R(bl)); }

        // ------------------------------------------------------------ the window's XAML, before it's parsed
        public static string Xaml(string xaml)
        {
            if (Current == Win11) return xaml;
            // solid background instead of Mica (the same swap Lite mode makes)
            xaml = xaml.Replace("WindowBackdropType='Mica'", "WindowBackdropType='None' Background='{DynamicResource AppBg}'");
            // corners
            xaml = Regex.Replace(xaml, "CornerRadius='([0-9.,]+)'", m =>
            {
                var parts = m.Groups[1].Value.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    double v;
                    if (double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                        parts[i] = R(v).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                return "CornerRadius='" + string.Join(",", parts) + "'";
            });
            // Segoe UI Variable is Windows 11's font
            xaml = xaml.Replace("Segoe UI Variable Display, ", "").Replace("Segoe UI Variable Text, ", "");
            // a background behind the title bar and the navigation pane (Windows 7's blue glass and side pane)
            xaml = xaml.Replace("<ui:TitleBar Grid.Row='0' Title=''/>", "<Border Grid.Row='0' Background='{DynamicResource LookTitle}'/><ui:TitleBar Grid.Row='0' Title=''/>");
            xaml = Regex.Replace(xaml, "(x:Name='NavPane'[^>]*?)Background='Transparent'", "$1Background='{DynamicResource LookNav}'");
            return xaml;
        }

        /// <summary>True when the window should have a solid background (no Mica).</summary>
        public static bool Solid { get { return Current != Win11 || App.S.LiteMode; } }

        // ------------------------------------------------------------ the window's own resources
        static ResourceDictionary applied;

        /// <summary>Gives the window the look's colors and styles. Run again when light/dark or the accent changes.</summary>
        public static void Apply(Window w)
        {
            if (w == null) return;
            if (applied != null) { w.Resources.MergedDictionaries.Remove(applied); applied = null; }
            if (Current == Win11) return;
            var d = new ResourceDictionary();
            if (Current == Win7)
            {
                d.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Light });   // WPF-UI's controls in light colors
                Win7Colors(d);
                d.MergedDictionaries.Add(Win7Styles());
            }
            else Win10Colors(d, Theme.Dark);
            d["ControlCornerRadius"] = CR(4);
            d["OverlayCornerRadius"] = CR(8);
            d["ContentControlThemeFontFamily"] = new FontFamily("Segoe UI, Microsoft YaHei UI");
            w.Resources.MergedDictionaries.Add(d);
            applied = d;
            w.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        }

        /// <summary>Window corners: square for Windows 10, slightly rounded for Windows 7 (Windows 11 only; older Windows ignores it).</summary>
        public static void ApplyFrame(Window w)
        {
            if (Current == Win11) return;
            try
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                int pref = Current == Win10 ? 1 : 3;                     // DWMWCP_DONOTROUND, DWMWCP_ROUNDSMALL
                Native.DwmSetWindowAttribute(h, 33, ref pref, 4);         // DWMWA_WINDOW_CORNER_PREFERENCE
            }
            catch { }
        }

        static SolidColorBrush B(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex); b.Freeze(); return b; }

        static LinearGradientBrush G(params string[] stops)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            for (int i = 0; i < stops.Length; i++)
                g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stops[i]), stops.Length == 1 ? 0 : i / (double)(stops.Length - 1)));
            g.Freeze();
            return g;
        }

        /// <summary>Windows 10: flat solid colors, no layers or card outlines to speak of.</summary>
        static void Win10Colors(ResourceDictionary d, bool dark)
        {
            if (dark)
            {
                d["AppBg"] = B("#1F1F1F"); d["Layer"] = B("#1F1F1F"); d["LayerBorder"] = B("#00000000");
                d["Card"] = B("#2B2B2B"); d["CardSecondary"] = B("#262626"); d["CardBorder"] = B("#2B2B2B");
                d["Hover"] = B("#19FFFFFF"); d["Pressed"] = B("#33FFFFFF"); d["NavSel"] = B("#26FFFFFF");
                d["Text"] = B("#FFFFFF"); d["Sub"] = B("#A6A6A6"); d["Tertiary"] = B("#7A7A7A");
                d["Divider"] = B("#3A3A3A"); d["Tile"] = B("#2B2B2B"); d["PaneOverlay"] = B("#2B2B2B"); d["Attention"] = B("#2B2B2B");
            }
            else
            {
                d["AppBg"] = B("#FFFFFF"); d["Layer"] = B("#FFFFFF"); d["LayerBorder"] = B("#00000000");
                d["Card"] = B("#F2F2F2"); d["CardSecondary"] = B("#FAFAFA"); d["CardBorder"] = B("#F2F2F2");
                d["Hover"] = B("#0F000000"); d["Pressed"] = B("#1F000000"); d["NavSel"] = B("#1A000000");
                d["Text"] = B("#000000"); d["Sub"] = B("#5D5D5D"); d["Tertiary"] = B("#8A8A8A");
                d["Divider"] = B("#E5E5E5"); d["Tile"] = B("#F2F2F2"); d["PaneOverlay"] = B("#F2F2F2"); d["Attention"] = B("#FFF4CE");
            }
            d["LookTitle"] = d["AppBg"];
            d["LookNav"] = B("#00000000");
        }

        /// <summary>Windows 7 (Aero): white content, a light blue glass title bar and side pane, blue selection.</summary>
        static void Win7Colors(ResourceDictionary d)
        {
            d["AppBg"] = B("#FFFFFF");
            d["Layer"] = B("#FFFFFF"); d["LayerBorder"] = B("#A9BFD6");
            d["Card"] = B("#FFFFFF"); d["CardSecondary"] = B("#F7F9FC"); d["CardBorder"] = B("#D5DFEA");
            d["Hover"] = G("#F2F8FF", "#E3F0FC"); d["Pressed"] = G("#DCEBFC", "#C1DBFC"); d["NavSel"] = G("#EBF4FE", "#CFE5FC");
            d["Text"] = B("#1A1A1A"); d["Sub"] = B("#4F6A87"); d["Tertiary"] = B("#8496AA");
            d["Divider"] = B("#DDE5EE"); d["Tile"] = B("#F4F8FC"); d["PaneOverlay"] = B("#EEF4FB"); d["Attention"] = B("#FFFBE5");
            d["TrackBrush"] = B("#9AABBF"); d["Smoke"] = B("#33000000"); d["Preview"] = B("#14000000");
            d["Accent"] = B("#2C7BD6"); d["OnAccent"] = B("#FFFFFF");
            d["LookTitle"] = G("#DCE9F8", "#C2D7F0", "#B4CDEB");                 // Aero glass, light
            d["LookNav"] = G("#E9F1FB", "#D6E5F6");                             // the Control Panel's side pane
        }

        /// <summary>Glossy Windows 7 buttons (gray, blue when you point at them), for plain and WPF-UI buttons alike.</summary>
        static ResourceDictionary Win7Styles()
        {
            const string template = @"
      <Setter Property='Foreground' Value='#1A1A1A'/>
      <Setter Property='Padding' Value='12,4'/>
      <Setter Property='MinHeight' Value='26'/>
      <Setter Property='Template'><Setter.Value>
        <ControlTemplate TargetType='{x:Type ButtonBase}'>
          <Border x:Name='b' CornerRadius='3' BorderThickness='1' BorderBrush='#707070' SnapsToDevicePixels='True'>
            <Border.Background><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='#F2F2F2' Offset='0'/><GradientStop Color='#EBEBEB' Offset='0.5'/><GradientStop Color='#DDDDDD' Offset='0.5'/><GradientStop Color='#CFCFCF' Offset='1'/></LinearGradientBrush></Border.Background>
            <Border CornerRadius='2' BorderThickness='1' BorderBrush='#B3FFFFFF'>
              <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
            </Border>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='b' Property='BorderBrush' Value='#3C7FB1'/>
              <Setter TargetName='b' Property='Background'><Setter.Value><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='#EAF6FD' Offset='0'/><GradientStop Color='#D9F0FC' Offset='0.5'/><GradientStop Color='#BEE6FD' Offset='0.5'/><GradientStop Color='#A7D9F5' Offset='1'/></LinearGradientBrush></Setter.Value></Setter>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='b' Property='BorderBrush' Value='#2C628B'/>
              <Setter TargetName='b' Property='Background'><Setter.Value><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='#E5F4FC' Offset='0'/><GradientStop Color='#C4E5F6' Offset='0.5'/><GradientStop Color='#98D1EF' Offset='0.5'/><GradientStop Color='#68B3DB' Offset='1'/></LinearGradientBrush></Setter.Value></Setter>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='0.5'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value></Setter>";
            string xaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        xmlns:ui='http://schemas.lepo.co/wpfui/2022/xaml'>
    <Style TargetType='{x:Type Button}'>" + template + @"
    </Style>
    <Style TargetType='{x:Type ui:Button}'>" + template + @"
    </Style>
</ResourceDictionary>";
            return (ResourceDictionary)XamlReader.Parse(xaml);
        }
    }
}
