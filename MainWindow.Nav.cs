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
    // The navigation pane and the Task Manager style table headers. Part of MainUI (MainWindow.cs).
    static partial class MainUI
    {
        // ----------------------------------------------------------------------------- navigation pane
        const double PaneOpen = 200, PaneCompact = 48;   // open menu: just wide enough for the longest item and the search box

        static bool IsWide()
        {
            double wd = (w.ActualWidth > 0 ? w.ActualWidth : w.Width) / uiScale;   // controller mode zooms the layout
            // one width for every page, explanations or not (~1010, like NavigationView's 1008): narrower, the menu folds to icons
            return wd >= PaneOpen + ContentChrome + DetailsMin;
        }

        static void OpenPane()
        {
            if (IsWide()) userCompact = false; else overlayOpen = true;
            ApplyLayout(true);
        }

        /// <summary>Animates a width; a newer animation on the same element cancels the older one's finish.</summary>
        static void AnimateWidth(FrameworkElement el, double target, bool animate, Action done = null)
        {
            int token;
            widthTokens.TryGetValue(el, out token);
            token++;
            widthTokens[el] = token;
            double cur = el.ActualWidth > 0 ? el.ActualWidth : el.Width;
            if (!animate || !Anim.On || Math.Abs(cur - target) < 0.5)
            {
                el.BeginAnimation(FrameworkElement.WidthProperty, null);
                el.Width = target;
                if (done != null) done();
                return;
            }
            bool opening = target > cur;
            var an = new DoubleAnimation(cur, target, TimeSpan.FromMilliseconds(opening ? 300 : 200)) { EasingFunction = opening ? Anim.Quint : Anim.Smooth };
            an.Completed += (o, e) =>
            {
                int now;
                if (!widthTokens.TryGetValue(el, out now) || now != token) return;
                el.BeginAnimation(FrameworkElement.WidthProperty, null);
                el.Width = target;
                if (done != null) done();
            };
            el.BeginAnimation(FrameworkElement.WidthProperty, an);
        }

        /// <summary>The floating pane gets a solid background, an edge and a shadow; inline it sits straight on Mica.</summary>
        static void StylePane(bool floating)
        {
            if (navPane == null || paneStyled == floating) return;
            paneStyled = floating;
            if (floating)
            {
                navPane.SetResourceReference(Border.BackgroundProperty, "PaneOverlay");
                navPane.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
                navPane.BorderThickness = new Thickness(0, 1, 1, 1);
                if (!App.S.LiteMode) navPane.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 315, Opacity = Theme.Dark ? 0.45 : 0.18, Color = Colors.Black };
            }
            else
            {
                navPane.Background = Brushes.Transparent;
                navPane.BorderThickness = new Thickness(0);
                navPane.Effect = null;
            }
        }

        static void ApplyLayout(bool animate)
        {
            if (w == null || navPane == null) return;
            double wd = (w.ActualWidth > 0 ? w.ActualWidth : w.Width) / uiScale;   // controller mode zooms the layout
            if (padVisual)
            {
                // controller mode: no side pane, the pages are tabs across the top
                overlayOpen = false;
                navPane.Visibility = Visibility.Collapsed;
                scrim.Visibility = Visibility.Collapsed;
                navSpace.BeginAnimation(FrameworkElement.WidthProperty, null);
                navSpace.Width = 0;
                contentLayer.CornerRadius = new CornerRadius(0);
                contentLayer.BorderThickness = new Thickness(0, 1, 0, 0);
                UpdateColumns(wd - ContentChrome);
                return;
            }
            if (navPane.Visibility != Visibility.Visible)
            {
                navPane.Visibility = Visibility.Visible;
                contentLayer.CornerRadius = new CornerRadius(8, 0, 0, 0);
                contentLayer.BorderThickness = new Thickness(1, 1, 0, 0);
            }
            bool wide = IsWide();
            if (wide && overlayOpen) overlayOpen = false;           // grew wide: the pane goes back inline
            bool expanded = wide ? !userCompact : overlayOpen;
            bool floating = !wide && overlayOpen;
            compact = !expanded;

            double paneW = expanded ? PaneOpen : PaneCompact;
            double spaceW = wide ? paneW : PaneCompact;
            if (floating) StylePane(true);
            AnimateWidth(navPane, paneW, animate, () => { if (!overlayOpen) StylePane(false); });
            AnimateWidth(navSpace, spaceW, animate);
            scrim.Visibility = floating ? Visibility.Visible : Visibility.Collapsed;

            if (expanded != lastExpanded || !animate)
            {
                lastExpanded = expanded;
                searchWrap.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                searchIconBtn.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
                for (int i = 0; i < navBtns.Length; i++) navBtns[i].ToolTip = expanded ? null : navLabels[i];
                if (expanded) Anim.Fade(padStatus, 1, 200, 120); else Anim.Fade(padStatus, 0, 100);
            }
            UpdateColumns(wd - spaceW - ContentChrome);
        }

        static double lastColsFor = -1;
        const double DetailsMin = 760, ContentChrome = 52;   // content width needed for table + preview; page margins

        /// <summary>Responsive tables: drop the least important columns (and the details card) as the window narrows, so Name never gets squeezed out.</summary>
        static void UpdateColumns(double content)
        {
            if (w == null || detailsCard == null) return;
            content = Math.Round(content);
            if (Math.Abs(content - lastColsFor) < 1) return;
            lastColsFor = content;
            const double nameMin = 200, chrome = 36 + 28 + 8;   // header margins, icon, padding

            bool details = content >= DetailsMin;
            var dv = details ? Visibility.Visible : Visibility.Collapsed;
            detailsWanted = details;
            LayoutDetails();

            double avail = content - (details ? 332 : 0) - chrome - nameMin;
            double app = avail >= 150 ? 150 : 0; avail -= app;
            double status = avail >= 110 ? 110 : 0; avail -= status;
            double size = avail >= 110 ? 110 : 0;
            SetCol("WColApp", app); SetCol("WColStatus", status); SetCol("WColSize", size);
            ShowHeaderCells(winHeader, new[] { 1.0, app, size, status });

            avail = content - chrome - nameMin;
            double mode = avail >= 170 ? 170 : 0; avail -= mode;
            double rApp = avail >= 130 ? 130 : 0; avail -= rApp;
            double pos = avail >= 110 ? 110 : 0; avail -= pos;
            double rSize = avail >= 100 ? 100 : 0; avail -= rSize;
            double op = avail >= 70 ? 70 : 0;
            SetCol("RColMode", mode); SetCol("RColApp", rApp); SetCol("RColPos", pos); SetCol("RColSize", rSize); SetCol("RColOpacity", op);
            ShowHeaderCells(runHeader, new[] { 1.0, rApp, pos, rSize, op, mode });
        }

        /// <summary>The details card sits at the top right, next to the page title, on the Windows page only. The title, buttons and table make room for it.</summary>
        static void LayoutDetails()
        {
            if (detailsCard == null || pageHeader == null || pageWindows == null) return;
            bool show = detailsWanted && page == "windows";
            var dv = show ? Visibility.Visible : Visibility.Collapsed;
            if (detailsCard.Visibility != dv) { detailsCard.Visibility = dv; if (!show) HideThumb(); }
            double room = show ? 28 + 316 + 16 : 28;
            // the preview sits below the title and commands (they get the full width); controller mode keeps it level with the title
            bool below = !padVisual;
            if (Grid.GetRow(detailsCard) != (below ? 2 : 1)) { Grid.SetRow(detailsCard, below ? 2 : 1); Grid.SetRowSpan(detailsCard, below ? 1 : 2); }
            var want = below ? new Thickness(0, 0, 28, 24) : new Thickness(0, 22, 28, 24);
            if (detailsCard.Margin != want) detailsCard.Margin = want;
            double headRoom = below ? 28 : room;
            var hm = pageHeader.Margin;
            if (Math.Abs(hm.Right - headRoom) > 0.5) pageHeader.Margin = new Thickness(hm.Left, hm.Top, headRoom, hm.Bottom);
            var pm = pageWindows.Margin;
            if (Math.Abs(pm.Right - room) > 0.5) pageWindows.Margin = new Thickness(pm.Left, pm.Top, room, pm.Bottom);

            // settings pages: the explainer panel takes the same spot
            if (explainCard == null || pageScroll == null) return;
            ExplainLayout();
            bool ex = ExplainWanted() && (padVisual ? detailsWanted : pageScroll.ActualWidth <= 0 || pageScroll.ActualWidth >= 520);
            var ev = ex ? Visibility.Visible : Visibility.Collapsed;
            if (explainCard.Visibility != ev)
            {
                explainCard.Visibility = ev;
                if (ex) { Anim.Enter(explainCard, padVisual ? 28 : 16, 60); ExplainForPage(); }
                else if (explainSb != null) explainSb.Stop(explainStage);   // nothing plays while it's hidden
            }
            double sroom = ex && padVisual ? 28 + 316 + 12 : 0;   // the PiP floats over the page instead   // clear of the panel (and its right margin), so the scrollbar sits beside it
            if (ex && padVisual)
            {
                var hm2 = pageHeader.Margin;
                if (Math.Abs(hm2.Right - (sroom + 4)) > 0.5) pageHeader.Margin = new Thickness(hm2.Left, hm2.Top, sroom + 4, hm2.Bottom);
            }
            var sm = pageScroll.Margin;
            if (Math.Abs(sm.Right - sroom) > 0.5) pageScroll.Margin = new Thickness(sm.Left, sm.Top, sroom, sm.Bottom);
            if (settingsHost != null)   // room under the last setting, so it can scroll up past the PiP
            {
                double bottom = ex && !padVisual ? 36 + 250 : 36;
                var hm3 = settingsHost.Margin;
                if (Math.Abs(hm3.Bottom - bottom) > 0.5) settingsHost.Margin = new Thickness(hm3.Left, hm3.Top, hm3.Right, bottom);
            }
        }

        static void ShowHeaderCells(Grid header, double[] widths)
        {
            if (header == null) return;
            foreach (UIElement c in header.Children)
            {
                int col = Grid.GetColumn(c);
                var v = col < widths.Length && widths[col] <= 0 ? Visibility.Collapsed : Visibility.Visible;
                if (c.Visibility != v) c.Visibility = v;
            }
        }

        static void SetCol(string key, double px)
        {
            var cur = w.Resources[key] as GridLength?;
            if (cur.HasValue && Math.Abs(cur.Value.Value - px) < 0.5) return;
            w.Resources[key] = new GridLength(px);
        }

        /// <summary>Slides the accent pill to the selected item, stretching on the way like NavigationView.</summary>
        static void MoveIndicator(bool animate) { MoveIndicator(animate, 0); }

        static void MoveIndicator(bool animate, int attempt)
        {
            if (indicator == null || navBtns == null) return;
            int ni = Array.IndexOf(navKeys, page);
            if (ni < 0) { Anim.Fade(indicator, 0, 120); indTop = -1; return; }
            var item = navBtns[ni];
            if (item.ActualHeight <= 0) { if (attempt < 20) Dispatch(() => MoveIndicator(animate, attempt + 1), DispatcherPriority.Loaded); return; }
            double y;
            try { y = item.TranslatePoint(new Point(0, item.ActualHeight / 2), indCanvas).Y - 8; } catch { return; }
            var scale = (ScaleTransform)indicator.RenderTransform;
            if (!animate || indTop < 0 || !Anim.On)
            {
                indicator.BeginAnimation(Canvas.TopProperty, null);
                Canvas.SetTop(indicator, y);
                indTop = y;
                if (indicator.Opacity < 1) Anim.Fade(indicator, 1, animate ? 167 : 0);
                return;
            }
            double dist = Math.Abs(y - indTop);
            indicator.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(indTop, y, TimeSpan.FromMilliseconds(320)) { EasingFunction = Anim.Smooth });
            var stretch = new DoubleAnimationUsingKeyFrames();
            stretch.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1 + Math.Min(2.2, dist / 40.0), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)), Anim.Smooth));
            stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)), Anim.Smooth));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, stretch);
            Anim.Fade(indicator, 1, 83);
            indTop = y;
        }

        public static void Navigate(string key) { Navigate(key, true); }

        static void Navigate(string key, bool animate)
        {
            if (w == null) return;
            if (key == "pip") key = "windows";
            bool changed = key != page;
            if (L.Active) Dispatch(() => L.Walk(w), DispatcherPriority.Loaded);   // backup for the translation hook
            page = key;
            int ni = Array.IndexOf(navKeys, key);
            if (ni >= 0 && navBtns[ni].IsChecked != true) navBtns[ni].IsChecked = true;
            if (ni < 0) foreach (var nb in navBtns) nb.IsChecked = false;
            pageTitle.Text = ni >= 0 ? navLabels[ni] : "Search results";
            pageWindows.Visibility = key == "windows" ? Visibility.Visible : Visibility.Collapsed;
            pageRunning.Visibility = key == "running" ? Visibility.Visible : Visibility.Collapsed;
            bool settingsPage = key != "windows" && key != "running";
            pageScroll.Visibility = settingsPage ? Visibility.Visible : Visibility.Collapsed;
            foreach (var kv in pages) kv.Value.Visibility = kv.Key == key ? Visibility.Visible : Visibility.Collapsed;
            if (settingsPage && key != "search") { SmoothScroll.Jump(pageScroll, 0); lastSettingsPage = key; }
            if (key != "search" && searchBox.Text.Length > 0) searchBox.Text = "";   // leaving the settings results
            UpdateSearchHint();
            bool explainWasUp = explainCard != null && explainCard.Visibility == Visibility.Visible;
            if (changed) ApplyLayout(animate);                 // the menu's width depends on whether this page has a right-hand panel
            LayoutDetails();
            if (explainWasUp && explainCard.Visibility == Visibility.Visible && changed) ExplainForPage();   // new page: its first explainer
            BuildCommands();
            if (key == "windows") { winSig = ""; RefreshWindows(); }
            if (key == "running") { runSig = ""; RefreshRunning(); }
            if (key == "controller" && refreshBindings != null) refreshBindings();
            MoveIndicator(animate && changed);
            UpdateTabs(animate && changed);
            if (padVisual && changed) BuildPadHints();
            if (animate && changed) AnimatePage();
            UpdatePadStatus();
        }

        /// <summary>Page entrance: the title and command bar fade, the content rises; settings cards arrive one after another.</summary>
        static void AnimatePage()
        {
            if (w == null || !Anim.On) return;
            HideThumb();
            Anim.Enter(pageTitle, 10, 0, 250);
            Anim.Enter(cmdBar, 10, 30, 250);
            if (page == "windows") { Anim.Enter(pageWindows, 28, 40); if (detailsCard.Visibility == Visibility.Visible) Anim.Enter(detailsCard, 28, 60); }
            else if (page == "running") Anim.Enter(pageRunning, 28, 40);
            else
            {
                StackPanel sp;
                if (!pages.TryGetValue(page, out sp)) return;
                int i = 0;
                foreach (UIElement child in sp.Children)
                {
                    if (i > 16) { child.Opacity = 1; child.RenderTransform = null; continue; }
                    Anim.Enter(child, 20, 30 + i * 22, 340);
                    i++;
                }
            }
        }

        static void OnEngineState()
        {
            if (w == null) return;
            UpdatePadStatus();
            if (page == "running") { runSig = ""; RefreshRunning(); BuildCommands(); }
            if (page == "controller" && refreshBindings != null) refreshBindings();
        }

        static void UpdatePadStatus()
        {
            if (padStatus == null) return;
            var a = App.I;
            padStatus.Text = !App.S.PadEnabled ? "" : !a.PadConnected ? "No controller" : Pad.FamilyName(a.PadKind) + (a.PadOn ? " · controls on" : " · controls off");
        }

        static void UpdateSearchHint()
        {
            if (searchHint == null) return;
            searchHint.Text = "Find a setting";
            searchHint.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        static void OnTick()
        {
            if (w == null) return;
            tickCount++;
            if (tickCount % 6 == 0) UpdatePreview();                                  // ~10 times a second
            if (thumb != IntPtr.Zero && thumbAlpha < 255) FadeThumbIn();
            if (tickCount % 120 == 0 && page == "windows") RefreshWindows();          // every 2 s
            if (tickCount % 60 == 0 && page == "running") RefreshRunning();           // every second
        }

        // ----------------------------------------------------------------------------- Task Manager table headers
        static void BuildHeader(Grid header, string[] titles, string[] keys, bool sortable)
        {
            header.Children.Clear();
            for (int i = 0; i < titles.Length; i++)
            {
                var cell = new Grid { Background = Brushes.Transparent, Margin = new Thickness(i == 0 ? -8 : -8, 0, 4, 0) };
                var hov = new Border { CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 2, 0, 2) };
                hov.SetResourceReference(Border.BackgroundProperty, "Hover");
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                if (i == 0) sp.Margin = new Thickness(8 + 28, 0, 8, 0);
                var tb = Fluent.Label(titles[i], 12, "Sub", false);
                var chev = Fluent.Glyph("", 8, "Sub");
                chev.Margin = new Thickness(6, 1, 0, 0);
                sp.Children.Add(tb);
                sp.Children.Add(chev);
                cell.Children.Add(hov);
                cell.Children.Add(sp);
                Grid.SetColumn(cell, i);
                header.Children.Add(cell);
                hov.Opacity = 0;
                if (!sortable) continue;
                cell.Cursor = Cursors.Hand;
                Anim.Hover(cell, hov);
                string k = keys[i];
                cell.MouseLeftButtonUp += (o, e) =>
                {
                    if (sortKey == k) sortDesc = !sortDesc; else { sortKey = k; sortDesc = false; }
                    foreach (var c in header.Children.OfType<Grid>()) ((TextBlock)((StackPanel)c.Children[1]).Children[1]).Text = "";
                    chev.Text = sortDesc ? "" : "";
                    winSig = "";
                    RefreshWindows();
                };
            }
        }
    }
}
