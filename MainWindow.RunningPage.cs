using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PipStudio
{
    // The Running PiPs page, and the options for one PiP. Part of MainUI (MainWindow.cs).
    static partial class MainUI
    {
        // ----------------------------------------------------------------------------- running page
        static IntPtr SelectedExternal()
        {
            var it = runList == null ? null : runList.SelectedItem as PipRowItem;
            return it != null && it.Pip == null && Native.IsWindow(it.External) ? it.External : IntPtr.Zero;
        }

        // ------------------------------------------------------------ options for one PiP (Running PiPs: ⋯ or right-click)
        static System.Windows.Controls.Primitives.Popup optPopup;

        /// <summary>A small card at the pointer with this PiP's own options: opacity, volume, size, shape, black bars and snapping.
        /// Works for PiP Studio's PiPs and other apps' PiPs alike; changes apply right away.</summary>
        static void ShowPipOptions(PipRowItem row)
        {
            if (optPopup != null) optPopup.IsOpen = false;
            var pip = row.Pip;
            IntPtr ext = row.External;
            if ((pip == null || pip.IsClosing) && (ext == IntPtr.Zero || !Native.IsWindow(ext))) return;
            bool own = pip != null;

            var sp = new StackPanel();
            var title = Fluent.Label(row.Title, 14, "Text", false);
            title.FontWeight = FontWeights.SemiBold;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            sp.Children.Add(title);
            var sub = Fluent.Label(own ? row.App : row.App + " · " + L.T("App's own PiP"), 12, "Sub", false);
            sub.Margin = new Thickness(0, 2, 0, 10);
            sp.Children.Add(sub);

            Action<string, FrameworkElement> add = (label, ctl) =>
            {
                var g = new Grid { Margin = new Thickness(0, 6, 0, 0), MinHeight = 32 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var l = Fluent.Label(label, 13, "Text", false);
                l.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(l);
                Grid.SetColumn(ctl, 1);
                ctl.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(ctl);
                sp.Children.Add(g);
            };
            Func<int, int, int, string, Action<int>, FrameworkElement> slider = (min, max, val, suffix, set) =>
            {
                var dp = new DockPanel();
                var v = Fluent.Label(val + suffix, 13, "Sub", false);
                v.Width = 44; v.TextAlignment = TextAlignment.Right;
                DockPanel.SetDock(v, Dock.Right);
                dp.Children.Add(v);
                var sl = new Slider { Minimum = min, Maximum = max, Value = val, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
                sl.ValueChanged += (o, e) => { v.Text = (int)sl.Value + suffix; set((int)sl.Value); };
                dp.Children.Add(sl);
                return dp;
            };
            Func<string[], int, Action<int>, FrameworkElement> combo = (items, val, set) =>
            {
                var cb = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var i in items) cb.Items.Add(L.Plain(L.T(i)));
                L.Combo(cb);
                cb.SelectedIndex = U.Clamp(val, 0, items.Length - 1);
                cb.SelectionChanged += (o, e) => { if (cb.SelectedIndex >= 0) set(cb.SelectedIndex); };
                return cb;
            };

            // opacity
            int op = own ? (int)Math.Round(pip.St.Opacity * 100) : ExternalPip.OpacityPercent(ext);
            add("Opacity", slider(own ? 10 : 20, 100, op, "%", v => { if (own) pip.SetOpacity(v / 100.0); else ExternalPip.SetOpacityPercent(ext, v); }));

            // volume (the app behind it)
            string proc = own ? pip.St.ProcessName : U.ProcessName(ext);
            float? vol = string.IsNullOrEmpty(proc) || (own && pip.St.Kind != SourceKind.Window) ? null : Audio.GetVolume(proc);
            if (vol.HasValue)
                add("Volume", slider(0, 100, (int)Math.Round(vol.Value * 100), "%", v => { if (own) pip.SetVolume(v / 100f); else ExternalPip.SetVolumeLevel(ext, v / 100f); }));
            else
            {
                var none = Fluent.Label("No sound from this app right now", 12, "Sub", false);
                none.TextWrapping = TextWrapping.Wrap;
                add("Volume", none);
            }

            // size, shape, black bars, snapping
            add("Size", combo(SizePresets.Names, own ? pip.PresetIndex : ExternalPip.PresetIndex(ext),
                i => { if (own) pip.SetSizePreset(i); else ExternalPip.SetSizePreset(ext, i); }));
            add("Shape", combo(AspectChoices.Names, own ? pip.St.Aspect : ExternalPip.AspectOf(ext),
                i => { if (own) pip.SetAspect(i); else ExternalPip.SetAspect(ext, i); }));
            var trim = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = own ? pip.St.TrimBars : ExternalPip.TrimsBars(ext), HorizontalAlignment = HorizontalAlignment.Left };
            trim.Click += (o, e) => { bool on = trim.IsChecked == true; if (own) pip.SetTrimBars(on); else ExternalPip.SetTrim(ext, on); };
            add("Black bars", trim);
            var trimNote = Fluent.Label(own ? "Crops away the bars around the video" : "Fits the window to the video so the bars disappear", 12, "Sub", false);
            trimNote.TextWrapping = TextWrapping.Wrap;
            trimNote.Margin = new Thickness(112, 0, 0, 0);
            sp.Children.Add(trimNote);
            add("Snapping", combo(new[] { "Follow my setting", "Always snap to spots", "Never snap" }, own ? pip.St.Snap : ExternalPip.SnapModeOf(ext),
                i => { if (own) pip.SetSnap(i); else ExternalPip.SetSnapMode(ext, i); }));

            var card = new Border
            {
                Width = 340, Padding = new Thickness(16, 14, 16, 16), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                Child = sp, Margin = new Thickness(12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.28, Color = Colors.Black }
            };
            card.SetResourceReference(Border.BackgroundProperty, "AppBg");
            card.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            card.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Text");
            optPopup = new System.Windows.Controls.Primitives.Popup
            {
                Child = card, StaysOpen = false, AllowsTransparency = true,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                PopupAnimation = App.S.LiteMode ? System.Windows.Controls.Primitives.PopupAnimation.None : System.Windows.Controls.Primitives.PopupAnimation.Fade,
                PlacementTarget = runList
            };
            optPopup.Closed += (o, e) => { runSig = ""; RefreshRunning(); };
            optPopup.IsOpen = true;
            L.Walk(card);
        }

        static PipWindow SelectedPip()
        {
            var it = runList == null ? null : runList.SelectedItem as PipRowItem;
            if (it == null || it.Pip == null || it.Pip.IsClosing) return null;
            return it.Pip;
        }

        static string SpotName(PipWindow p)
        {
            if (p.InTheater) return "Theater";
            switch (p.NearestSpot(true))
            {
                case "TL": return "Top left"; case "T": return "Top"; case "TR": return "Top right";
                case "L": return "Left"; case "R": return "Right";
                case "BL": return "Bottom left"; case "B": return "Bottom"; default: return "Bottom right";
            }
        }

        /// <summary>Running PiPs > Marked as PiPs: one row per Treat as a PiP you chose, each with Remove.</summary>
        static void RefreshMarked()
        {
            if (markedList == null) return;
            var marks = ExternalPip.Marks();
            string sig = string.Join("|", marks.Select(m => m.Hwnd.ToInt64() + m.Key + m.Title));
            markedBox.Visibility = marks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (sig == markedSig) return;
            markedSig = sig;
            markedList.Children.Clear();
            foreach (var mk in marks)
            {
                var m = mk;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var bg = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 12, 10) };
                bg.SetResourceReference(Border.BackgroundProperty, "Card");
                bg.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
                Grid.SetColumnSpan(bg, 2);
                row.Children.Add(bg);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 8, 12, 8) };
                text.Children.Add(Fluent.Label(m.Title, 14, "Text", false));
                text.Children.Add(Fluent.Label(m.WholeKind ? "Every window like this from " + m.App : m.App + " · this window only, until it closes", 12, "Sub", false));
                row.Children.Add(text);
                var remove = PlainButton("Remove", () =>
                {
                    ExternalPip.Unmark(m);                                   // a normal window again, off always on top
                    runSig = ""; winSig = ""; RefreshRunning(); RefreshMarked(); BuildCommands();
                });
                remove.VerticalAlignment = VerticalAlignment.Center;
                remove.Margin = new Thickness(0, 0, 12, 0);
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                markedList.Children.Add(row);
            }
        }

        static void RefreshRunning()
        {
            if (runList == null) return;
            RefreshMarked();
            var items = new List<PipRowItem>();
            string q = "";
            foreach (var p in App.I.Pips.Where(x => !x.IsClosing))
            {
                var modes = new List<string>();
                if (p.St.ClickThrough) modes.Add("Ghost");
                if (p.St.Auto) modes.Add("Auto");
                else if (p.St.Follow) modes.Add("Follows");
                if (p.IsParked) modes.Add("Source hidden");
                if (p.St.Locked) modes.Add("Locked");
                if (!p.HasSource) modes.Add("Waiting");
                if (App.I.HiddenAll) modes.Add("Hidden");
                var b = p.TargetBounds;
                var item = new PipRowItem
                {
                    Pip = p,
                    Title = p.DisplayName,
                    App = p.St.Kind == SourceKind.Screen ? "Screen" : p.St.ProcessName,
                    Position = SpotName(p),
                    SizeText = b.Width + " × " + b.Height,
                    OpacityText = (int)Math.Round(p.St.Opacity * 100) + "%",
                    Mode = modes.Count == 0 ? "Normal" : string.Join(", ", modes),
                    Glyph = p.St.Kind == SourceKind.Screen ? "" : null,
                    Icon = p.St.Kind == SourceKind.Window && p.Src != IntPtr.Zero ? IconFor(p.Src) : null
                };
                if (q.Length > 0 && item.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && (item.App ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                items.Add(item);
            }
            // other apps' own PiP windows
            if (App.S.ControlOtherPips)
                foreach (var h in ExternalPip.FindAll())
                {
                    var b = U.WindowRect(h);
                    string proc = U.ProcessName(h);
                    items.Add(new PipRowItem
                    {
                        External = h,
                        Title = ExternalPip.AppName(h) + "'s own PiP",
                        App = proc,
                        Position = ExternalPip.SpotName(h),
                        SizeText = b.Width + " × " + b.Height,
                        OpacityText = (int)Math.Round(ExternalPip.OpacityOf(h) * 100) + "%",
                        Mode = "App's own PiP",
                        Icon = IconFor(h)
                    });
                }
            // ...and the ones that are minimized: still yours, just out of sight (Restore brings them back)
            if (App.S.ControlOtherPips)
                foreach (var h in ExternalPip.MinimizedPips())
                    items.Add(new PipRowItem
                    {
                        External = h,
                        Title = ExternalPip.AppName(h) + "'s own PiP",
                        App = U.ProcessName(h),
                        Position = "—",
                        SizeText = "—",
                        OpacityText = (int)Math.Round(ExternalPip.OpacityOf(h) * 100) + "%",
                        Mode = "Minimized",
                        Icon = IconFor(h)
                    });
            // never empty: "" means "refresh needed", and an empty list must still replace the old rows
            string sig = "n" + items.Count + "|" + string.Join("|", items.Select(x => (x.Pip != null ? x.Pip.GetHashCode() : x.External.ToInt64()) + x.Title + x.Position + x.SizeText + x.OpacityText + x.Mode));
            // the empty message only when the list really is empty
            bool showEmpty = items.Count == 0;
            runEmpty.Text = showEmpty ? "No PiPs are open.\nPick a window on the Windows page, or press " + HK.Display("HK_NewPip") + " anywhere." : "";
            var emptyPanel = F<StackPanel>("RunEmptyPanel");
            runList.Visibility = showEmpty ? Visibility.Collapsed : Visibility.Visible;
            if (showEmpty && emptyPanel.Visibility != Visibility.Visible) { emptyPanel.Visibility = Visibility.Visible; Anim.Enter(emptyPanel, 12, 60, 300); }
            else if (!showEmpty) emptyPanel.Visibility = Visibility.Collapsed;
            if (sig == runSig) return;
            runSig = sig;
            var prev = SelectedPip();
            var prevExt = SelectedExternal();
            runList.ItemsSource = items;
            var again = items.FirstOrDefault(x => prev != null ? x.Pip == prev : prevExt != IntPtr.Zero && x.External == prevExt) ?? (items.Count > 0 ? items[0] : null);
            runList.SelectedItem = again;
            BuildCommands();
        }
    }
}
