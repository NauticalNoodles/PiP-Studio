using System.Drawing;
using PipStudio;
using Xunit;

public class PipRulesTests
{
    static readonly Rectangle Monitor = new Rectangle(0, 0, 1920, 1080);

    static WindowFacts Win(string process, string cls, string title, int w, int h, bool topmost = true, bool resizable = true)
    {
        return new WindowFacts
        {
            Process = process, Class = cls, Title = title,
            Style = resizable ? PipRules.WS_THICKFRAME : 0,
            ExStyle = topmost ? PipRules.WS_EX_TOPMOST : 0,
            Rect = new Rectangle(1400, 700, w, h), Monitor = Monitor
        };
    }

    [Fact] public void BrowserPipTitles() { Assert.True(PipRules.IsPipTitle(" Picture in picture ")); Assert.False(PipRules.IsPipTitle("YouTube")); }

    [Fact]
    public void DiscordPopout_IsAPip()
    {
        Assert.True(PipRules.IsDiscordPopout(Win("Discord", "Chrome_WidgetWin_1", "", 480, 270, topmost: false)));
    }

    [Fact]
    public void DiscordMainWindow_IsNot()
    {
        Assert.False(PipRules.IsDiscordPopout(Win("Discord", "Chrome_WidgetWin_1", "#general | Server - Discord", 1200, 800)));
    }

    [Fact]
    public void DiscordTrayMenu_IsNot()
    {
        // untitled, on top, big enough, but can't be resized
        var menu = Win("Discord", "Chrome_WidgetWin_1", "", 260, 400, resizable: false);
        Assert.False(PipRules.IsDiscordPopout(menu));
        Assert.False(PipRules.LooksLikePip(menu));
    }

    [Fact]
    public void DiscordOverlay_IsNot()
    {
        Assert.False(PipRules.IsDiscordPopout(Win("Discord", "Chrome_WidgetWin_1", "Discord Overlay", 1920, 1080)));
        Assert.False(PipRules.IsDiscordPopout(Win("Discord", "Chrome_WidgetWin_1", "", 1920, 1080)));   // full screen
    }

    [Fact]
    public void SmallOnTopPlayer_LooksLikeAPip() { Assert.True(PipRules.LooksLikePip(Win("vlc", "Qt5QWindowIcon", "song.mp4", 480, 270))); }

    [Fact] public void NotOnTop_DoesNot() { Assert.False(PipRules.LooksLikePip(Win("vlc", "Qt5QWindowIcon", "", 480, 270, topmost: false))); }

    [Fact] public void TooBig_DoesNot() { Assert.False(PipRules.LooksLikePip(Win("vlc", "Qt5QWindowIcon", "", 1600, 900))); }

    [Fact] public void TooSmall_DoesNot() { Assert.False(PipRules.LooksLikePip(Win("vlc", "Qt5QWindowIcon", "", 120, 80))); }

    [Fact] public void OddShape_DoesNot() { Assert.False(PipRules.LooksLikePip(Win("vlc", "Qt5QWindowIcon", "", 900, 120))); }

    [Fact] public void WindowsParts_DoNot() { Assert.False(PipRules.LooksLikePip(Win("explorer", "Shell_TrayWnd", "", 480, 270))); }

    [Fact]
    public void ClickThroughOverlay_DoesNot()
    {
        var w = Win("RTSSx", "Overlay", "", 480, 270);
        w.ExStyle |= PipRules.WS_EX_TRANSPARENT;
        Assert.False(PipRules.LooksLikePip(w));
    }

    [Fact]
    public void Dialog_DoesNot()
    {
        var w = Win("someapp", "Dialog", "", 480, 270);
        w.HasOwner = true;
        Assert.False(PipRules.LooksLikePip(w));
    }
}
