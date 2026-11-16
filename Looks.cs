using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace PipStudio
{
    /// <summary>
    /// The look of the main PiP Studio window (Settings > Appearance > Window style): Windows 11 (the normal look) or
    /// Xbox (always dark, Xbox green highlights, rounder cards, buttons that light up green with a white focus outline,
    /// like the console's dashboard; it goes well with controller-friendly mode).
    /// Only the main window changes; PiPs, menus and the window picker keep their look.
    /// How: the window's XAML is rewritten before it's parsed (corners, no Mica), and the window gets its own resources
    /// that win over the app's: colors, WPF-UI's dark theme and accent colors, corner sizes and button styles.
    /// Changing the style rebuilds the window, like changing the language.
    /// </summary>
    static class Looks
    {
        public const int Win11 = 0, Xbox = 1;
        public static readonly string[] Names = { "Windows 11", "Xbox" };

        public static int Current { get { return App.S == null ? Win11 : Math.Max(Win11, Math.Min(Xbox, App.S.WindowStyle)); } }

        /// <summary>The Xbox look is always dark.</summary>
        public static bool Dark { get { return Theme.Dark || Current == Xbox; } }

        const string Green = "#107C10", GreenHover = "#1E9E1E", GreenPressed = "#0B5A0B";

        // ------------------------------------------------------------ corners
        /// <summary>A corner size in this look: half again as round for Xbox (up to 12).</summary>
        public static double R(double r) { return Current == Xbox && r > 0 ? Math.Min(12, Math.Round(r * 1.5)) : r; }
        public static CornerRadius CR(double r) { return new CornerRadius(R(r)); }
        public static CornerRadius CR(double tl, double tr, double br, double bl) { return new CornerRadius(R(tl), R(tr), R(br), R(bl)); }

        // ------------------------------------------------------------ the window's XAML, before it's parsed
        public static string Xaml(string xaml)
        {
            if (Current == Win11) return xaml;
            // a solid background instead of Mica (the same swap Lite mode makes)
            xaml = xaml.Replace("WindowBackdropType='Mica'", "WindowBackdropType='None' Background='{DynamicResource AppBg}'");
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
            // backgrounds behind the title bar and the navigation pane
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
            d.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });   // WPF-UI's controls in dark colors
            XboxColors(d);
            d.MergedDictionaries.Add(XboxStyles());
            d["ControlCornerRadius"] = CR(4);
            d["OverlayCornerRadius"] = CR(8);
            w.Resources.MergedDictionaries.Add(d);
            applied = d;
        }

        static SolidColorBrush B(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex); b.Freeze(); return b; }
        static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }

        static LinearGradientBrush G(params string[] stops)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            for (int i = 0; i < stops.Length; i++) g.GradientStops.Add(new GradientStop(C(stops[i]), i / (double)Math.Max(1, stops.Length - 1)));
            g.Freeze();
            return g;
        }

        /// <summary>Xbox: near-black with a faint green glow at the bottom, dark gray tiles, green selection.</summary>
        static void XboxColors(ResourceDictionary d)
        {
            d["AppBg"] = G("#0E0E0E", "#0E0E0E", "#0F1A10");
            d["Layer"] = B("#00000000"); d["LayerBorder"] = B("#00000000");     // tiles sit straight on the background
            d["Card"] = B("#1C1C1C"); d["CardSecondary"] = B("#181818"); d["CardBorder"] = B("#262626");
            d["Hover"] = B("#17FFFFFF"); d["Pressed"] = B("#26FFFFFF");
            d["NavSel"] = B(Green);
            d["Text"] = B("#FFFFFF"); d["Sub"] = B("#B3B3B3"); d["Tertiary"] = B("#808080");
            d["Divider"] = B("#2A2A2A"); d["Tile"] = B("#1C1C1C"); d["PaneOverlay"] = B("#161616"); d["Attention"] = B("#1F2A1F");
            d["TrackBrush"] = B("#5C5C5C"); d["Smoke"] = B("#66000000"); d["Preview"] = B("#33000000");
            d["Accent"] = B(Green); d["OnAccent"] = B("#FFFFFF");
            d["LookTitle"] = B("#0E0E0E");
            d["LookNav"] = B("#141414");
            // WPF-UI's own accent (switches, sliders, focus): Xbox green
            foreach (var k in new[] { "SystemAccentColor", "SystemAccentColorPrimary", "SystemAccentColorSecondary", "SystemAccentColorTertiary" }) d[k] = C(Green);
            d["AccentFillColorDefaultBrush"] = B(Green);
            d["AccentFillColorSecondaryBrush"] = B(GreenHover);
            d["AccentFillColorTertiaryBrush"] = B(GreenPressed);
            d["AccentTextFillColorPrimaryBrush"] = B("#5FD35F");
            d["TextOnAccentFillColorPrimaryBrush"] = B("#FFFFFF");
        }

        /// <summary>Xbox buttons: dark gray, green when you point at them, a white outline when focused (keyboard or controller).</summary>
        static ResourceDictionary XboxStyles()
        {
            const string setters = @"
      <Setter Property='Foreground' Value='#FFFFFF'/>
      <Setter Property='Background' Value='#2B2B2B'/>
      <Setter Property='Padding' Value='14,5'/>
      <Setter Property='MinHeight' Value='30'/>
      <Setter Property='Template'><Setter.Value>
        <ControlTemplate TargetType='{x:Type ButtonBase}'>
          <Border x:Name='b' CornerRadius='6' BorderThickness='2' BorderBrush='Transparent' Background='{TemplateBinding Background}' SnapsToDevicePixels='True'>
            <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='" + GreenHover + @"'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='#FFFFFF'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Background' Value='" + GreenPressed + @"'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='0.45'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value></Setter>";
            string xaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        xmlns:ui='http://schemas.lepo.co/wpfui/2022/xaml'>
    <Style TargetType='{x:Type Button}'>" + setters + @"
    </Style>
    <Style TargetType='{x:Type ui:Button}'>" + setters + @"
      <Style.Triggers>
        <Trigger Property='Appearance' Value='Primary'><Setter Property='Background' Value='" + Green + @"'/></Trigger>
        <Trigger Property='Appearance' Value='Transparent'><Setter Property='Background' Value='Transparent'/></Trigger>
      </Style.Triggers>
    </Style>
</ResourceDictionary>";
            return (ResourceDictionary)XamlReader.Parse(xaml);
        }
    }
}
