using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;

namespace PipStudio
{
    /// <summary>
    /// Translations. The English text in the code is the key: every piece of text is looked up when it reaches the screen
    /// (WPF windows after each layout pass, WinForms menus as they open, text drawn on PiPs, tray notifications).
    /// Text with names or numbers in it is matched by patterns like "Opacity {0}%".
    /// </summary>
    static class L
    {
        public static string Code = "en";
        public static bool Rtl;   // Arabic (the layout itself is never mirrored)
        static Dictionary<string, string> map;
        static readonly List<KeyValuePair<Regex, string>> patterns = new List<KeyValuePair<Regex, string>>();
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>();

        /// <summary>Codes and native names offered in Settings. "" follows Windows.</summary>
        public static readonly string[] Codes = { "", "en", "ar", "zh" };
        public static readonly string[] Names = { "Use my Windows language", "English", "العربية", "中文（简体）" };

        public static void Init(string setting)
        {
            string c = setting ?? "";
            if (c == "")
            {
                string sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                c = sys == "ar" || sys == "zh" ? sys : "en";
            }
            Code = c;
            map = c == "ar" ? LangData.Arabic() : c == "zh" ? LangData.Chinese() : null;
            Rtl = c == "ar";
            patterns.Clear(); cache.Clear();
            if (map == null) return;
            foreach (var kv in map)
            {
                if (!kv.Key.Contains("{0}")) continue;
                string re = "^" + Regex.Replace(Regex.Escape(kv.Key), @"\\\{(\d)}", m => "(?<g" + m.Groups[1].Value + ">.+?)") + "$";
                patterns.Add(new KeyValuePair<Regex, string>(new Regex(re, RegexOptions.Singleline | RegexOptions.CultureInvariant), kv.Value));
            }
            // longer patterns first, so "Display {0} (main)" wins over "Display {0}"
            patterns.Sort((a, b) => b.Key.ToString().Length.CompareTo(a.Key.ToString().Length));
        }

        public static bool Active { get { return map != null; } }

        /// <summary>The text in the current language (or unchanged when there's no translation).</summary>
        public static string T(string s)
        {
            if (map == null || string.IsNullOrEmpty(s)) return s;
            string r;
            if (cache.TryGetValue(s, out r)) return r;
            r = Lookup(s, 0);
            if (Rtl && !ReferenceEquals(r, s) && r != s) r = RtlRun(r);
            if (cache.Count > 4000) cache.Clear();
            cache[s] = r;
            return r;
        }

        /// <summary>Arabic text reads right to left even though the layout stays left to right: each line is wrapped in a
        /// right-to-left embedding (RLE … PDF), so English words inside it ("Windows", "PiP Studio") land on the correct side
        /// and the sentence's punctuation sits at its end, while the line itself stays where English text would be.</summary>
        static string RtlRun(string r)
        {
            var lines = r.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Length > 0 && lines[i][0] != '\u202B') lines[i] = "\u202B" + lines[i] + "\u202C";
            return string.Join("\n", lines);
        }

        static string Lookup(string s, int depth)
        {
            string r;
            if (map.TryGetValue(s, out r)) return r;
            if (depth > 2 || s.Length > 1200) return s;
            string trimmed = s.Trim();
            if (trimmed.Length != s.Length && trimmed.Length > 0 && map.TryGetValue(trimmed, out r)) return s.Replace(trimmed, r);
            foreach (var p in patterns)
            {
                var m = p.Key.Match(s);
                if (!m.Success) continue;
                string outp = p.Value;
                for (int i = 0; i < 4; i++)
                {
                    var g = m.Groups["g" + i];
                    if (g.Success) outp = outp.Replace("{" + i + "}", Lookup(g.Value, depth + 1));
                }
                return outp;
            }
            // several sentences or parts joined together: translate each part
            foreach (var sep in new[] { "\n\n", "\n", "  ·  ", " · " })
            {
                if (!s.Contains(sep)) continue;
                var parts = s.Split(new[] { sep }, StringSplitOptions.None);
                bool any = false;
                for (int i = 0; i < parts.Length; i++)
                {
                    string t = Lookup(parts[i], depth + 1);
                    if (!ReferenceEquals(t, parts[i]) && t != parts[i]) { parts[i] = t; any = true; }
                }
                if (any) return string.Join(sep, parts);
            }
            return s;
        }

        // ------------------------------------------------------------ WPF: windows are kept translated by Watch (a walk after each layout pass)
        static void Apply(TextBlock tb)
        {
            string t = tb.Text;
            if (string.IsNullOrEmpty(t)) { if (Rtl) Direction(tb, false); return; }
            string r = Plain(T(t));
            if (r != t) tb.SetCurrentValue(TextBlock.TextProperty, r);   // keeps any binding, so later changes come through too
            if (Rtl) Direction(tb, HasArabic(r));
        }

        /// <summary>WPF gets plain text: right-to-left reading is set on the TextBlock itself (the embedding marks are for WinForms text).</summary>
        public static string Plain(string s) { return s == null ? null : s.Replace("\u202B", "").Replace("\u202C", ""); }

        public static bool HasArabic(string s)
        {
            foreach (char c in s) if (c >= '\u0600' && c <= '\u06FF') return true;
            return false;
        }

        static readonly DependencyProperty RtlOnProperty = DependencyProperty.RegisterAttached("RtlOn", typeof(bool), typeof(L), new PropertyMetadata(false));

        /// <summary>Arabic text reads right to left inside its own box, while the box stays exactly where it was:
        /// the TextBlock's reading direction flips, and its alignment is swapped so the text keeps the same edge.</summary>
        static void Direction(TextBlock tb, bool rtl)
        {
            bool on = (bool)tb.GetValue(RtlOnProperty);
            if (on == rtl) return;
            tb.SetValue(RtlOnProperty, rtl);
            tb.FlowDirection = rtl ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;
            var a = tb.TextAlignment;
            if (a == TextAlignment.Left) tb.TextAlignment = TextAlignment.Right;
            else if (a == TextAlignment.Right) tb.TextAlignment = TextAlignment.Left;
        }

        /// <summary>Translates every TextBlock already in a window or panel (backs up the Loaded hook, e.g. after switching language).</summary>
        public static void Walk(DependencyObject root)
        {
            if (map == null || root == null) return;
            var tb = root as TextBlock;
            if (tb != null) Apply(tb);
            var fe = root as FrameworkElement;
            if (fe != null)
            {
                var tip = fe.ToolTip as string;
                if (tip != null) { string r = Plain(T(tip)); if (r != tip) fe.ToolTip = r; }
                var ub = root as Wpf.Ui.Controls.TextBox;
                if (ub != null && !string.IsNullOrEmpty(ub.PlaceholderText)) { string r = Plain(T(ub.PlaceholderText)); if (r != ub.PlaceholderText) ub.PlaceholderText = r; }
            }
            int n = 0;
            try { n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); } catch { }
            for (int i = 0; i < n; i++) Walk(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
        }

        /// <summary>Keeps a window translated: after every layout pass (new or changed text always causes one) the window is
        /// walked right away, before it's drawn, so English never shows for a frame. A walk that changes text causes one more
        /// layout pass, whose walk then finds nothing left to do.</summary>
        public static void Watch(Window win)
        {
            if (win == null) return;
            bool walking = false;
            win.LayoutUpdated += (o, e) =>
            {
                if (map == null || walking) return;
                walking = true;
                try { Walk(win); } finally { walking = false; }
            };
        }

        /// <summary>Drop-down lists open in their own pop-up: in Arabic their items get a right-to-left template up front.</summary>
        public static void Combo(System.Windows.Controls.ComboBox cb)
        {
            if (!Rtl || cb == null) return;
            cb.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse(
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                "<TextBlock Text='{Binding}' FlowDirection='RightToLeft' TextAlignment='Right'/></DataTemplate>");
        }

        // ------------------------------------------------------------ WinForms menus
        public static void Menu(ToolStripItemCollection items)
        {
            if (map == null || items == null) return;
            foreach (ToolStripItem it in items)
            {
                if (!string.IsNullOrEmpty(it.Text))
                {
                    // "&&" in menu text is a literal "&"
                    string plain = it.Text.Replace("&&", "&");
                    string r = T(plain);
                    if (r != plain) it.Text = r.Replace("&", "&&");
                }
                if (!string.IsNullOrEmpty(it.ToolTipText)) it.ToolTipText = T(it.ToolTipText);
                var mi = it as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems) Menu(mi.DropDownItems);
            }
        }
    }

    /// <summary>TextRenderer with translation, for text drawn on PiPs and pickers.</summary>
    static class LT
    {

        public static void DrawText(IDeviceContext dc, string text, Font font, System.Drawing.Point pt, Color fore) { TextRenderer.DrawText(dc, L.T(text), font, pt, fore, TextFormatFlags.Default); }
        public static void DrawText(IDeviceContext dc, string text, Font font, System.Drawing.Point pt, Color fore, TextFormatFlags flags) { TextRenderer.DrawText(dc, L.T(text), font, pt, fore, flags); }
        public static void DrawText(IDeviceContext dc, string text, Font font, System.Drawing.Point pt, Color fore, Color back) { TextRenderer.DrawText(dc, L.T(text), font, pt, fore, back, TextFormatFlags.Default); }
        public static void DrawText(IDeviceContext dc, string text, Font font, System.Drawing.Point pt, Color fore, Color back, TextFormatFlags flags) { TextRenderer.DrawText(dc, L.T(text), font, pt, fore, back, flags); }
        public static void DrawText(IDeviceContext dc, string text, Font font, Rectangle r, Color fore) { TextRenderer.DrawText(dc, L.T(text), font, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
        public static void DrawText(IDeviceContext dc, string text, Font font, Rectangle r, Color fore, TextFormatFlags flags) { TextRenderer.DrawText(dc, L.T(text), font, r, fore, flags); }
        public static void DrawText(IDeviceContext dc, string text, Font font, Rectangle r, Color fore, Color back) { TextRenderer.DrawText(dc, L.T(text), font, r, fore, back, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
        public static void DrawText(IDeviceContext dc, string text, Font font, Rectangle r, Color fore, Color back, TextFormatFlags flags) { TextRenderer.DrawText(dc, L.T(text), font, r, fore, back, flags); }

        public static System.Drawing.Size MeasureText(string text, Font font) { return TextRenderer.MeasureText(L.T(text), font); }
        public static System.Drawing.Size MeasureText(string text, Font font, System.Drawing.Size proposed) { return TextRenderer.MeasureText(L.T(text), font, proposed); }
        public static System.Drawing.Size MeasureText(string text, Font font, System.Drawing.Size proposed, TextFormatFlags flags) { return TextRenderer.MeasureText(L.T(text), font, proposed, flags); }
        public static System.Drawing.Size MeasureText(IDeviceContext dc, string text, Font font) { return TextRenderer.MeasureText(dc, L.T(text), font); }
        public static System.Drawing.Size MeasureText(IDeviceContext dc, string text, Font font, System.Drawing.Size proposed) { return TextRenderer.MeasureText(dc, L.T(text), font, proposed); }
        public static System.Drawing.Size MeasureText(IDeviceContext dc, string text, Font font, System.Drawing.Size proposed, TextFormatFlags flags) { return TextRenderer.MeasureText(dc, L.T(text), font, proposed, flags); }
    }
}
