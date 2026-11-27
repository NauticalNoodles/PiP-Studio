using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;

namespace PipStudio
{
    /// <summary>
    /// Store version only: once, after a few days of real use (a handful of PiPs opened), when you open PiP Studio's window,
    /// it asks whether you'd rate it, and on yes opens the Store's own rating dialog. Ratings are a big part of how the Store
    /// ranks apps in its search. Never over a game: only when you open the window yourself. "Not now" also counts as asked.
    /// </summary>
    static partial class MainUI
    {
        const int RateAfterDays = 3, RateAfterPips = 5;
        const string StoreProductId = "9NWJLMR7PFTH";

        /// <summary>Remembers when PiP Studio was first used (called at start).</summary>
        public static void NoteFirstUse()
        {
            if (string.IsNullOrEmpty(App.S.FirstUse)) { App.S.FirstUse = DateTime.Now.ToString("yyyy-MM-dd"); App.I.SaveSettingsSoon(); }
        }

        static void MaybeAskForRating()
        {
            var S = App.S;
            if (!Packaged.IsPackaged || S.RateAsked || w == null || !w.IsVisible || !w.IsActive || DialogOpen) return;
            DateTime first;
            if (!DateTime.TryParse(S.FirstUse, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out first)) return;
            if ((DateTime.Now - first).TotalDays < RateAfterDays || S.PipsOpened < RateAfterPips) return;
            S.RateAsked = true;
            App.I.SaveSettingsSoon();
            Ask("Enjoying PiP Studio?", "A rating in the Microsoft Store helps other people find it. It only takes a moment.", "Rate it", "Not now", ok => { if (ok) OpenStoreRating(); });
        }

        /// <summary>The Store's own rate-and-review dialog (or its review page, if that can't open).</summary>
        static async void OpenStoreRating()
        {
            try
            {
                var ctx = Windows.Services.Store.StoreContext.GetDefault();
                WinRT.Interop.InitializeWithWindow.Initialize(ctx, new WindowInteropHelper(w).Handle);   // a desktop app has to say which window it belongs to
                var r = await ctx.RequestRateAndReviewAppAsync();
                if (r.Status != Windows.Services.Store.StoreRateAndReviewStatus.Error) return;
            }
            catch { }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-windows-store://review/?ProductId=" + StoreProductId) { UseShellExecute = true }); } catch { }
        }
    }
}
