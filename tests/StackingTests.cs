using System.Collections.Generic;
using System.Drawing;
using PipStudio;
using Xunit;

public class StackingTests
{
    static readonly Rectangle Screen = new Rectangle(0, 0, 1920, 1040);
    const int Gap = 12;

    [Fact]
    public void NothingInTheWay_NothingMoves()
    {
        var plan = Stacking.PlanBump(new Rectangle(100, 100, 400, 225), new[] { new Rectangle(1000, 600, 400, 225) }, new Rectangle[0], Screen, Gap);
        Assert.NotNull(plan);
        Assert.Empty(plan);
    }

    [Fact]
    public void DroppedOnTopEdge_PushesItDown()
    {
        var a = new Rectangle(700, 420, 400, 225);
        var plan = Stacking.PlanBump(new Rectangle(700, 300, 400, 225), new[] { a }, new Rectangle[0], Screen, Gap);
        Assert.Equal(new Rectangle(700, 300 + 225 + Gap, 400, 225), plan[0]);
    }

    [Fact]
    public void DroppedOnSide_PushesItSideways()
    {
        var a = new Rectangle(800, 400, 400, 225);
        var plan = Stacking.PlanBump(new Rectangle(500, 400, 400, 225), new[] { a }, new Rectangle[0], Screen, Gap);
        Assert.Equal(new Rectangle(500 + 400 + Gap, 400, 400, 225), plan[0]);
    }

    [Fact]
    public void NoRoomBelowACornerPip_GivesUp_SoTheMovedOneStacksInstead()
    {
        var corner = new Rectangle(1496, 791, 400, 225);
        Assert.Null(Stacking.PlanBump(new Rectangle(1496, 620, 400, 225), new[] { corner }, new Rectangle[0], Screen, Gap));
        // ...and stacking the moved one past it puts it right above
        var stacked = Stacking.StackPast(new Rectangle(1496, 620, 400, 225), new[] { corner }, false, Gap);
        Assert.Equal(791 - Gap - 225, stacked.Y);
    }

    [Fact]
    public void PushedPipPushesTheNextOne_TheyStack()
    {
        var a = new Rectangle(700, 250, 400, 225);
        var c = new Rectangle(700, 500, 400, 225);
        var plan = Stacking.PlanBump(new Rectangle(700, 100, 400, 225), new[] { a, c }, new Rectangle[0], Screen, Gap);
        Assert.Equal(100 + 225 + Gap, plan[0].Y);
        Assert.Equal(plan[0].Bottom + Gap, plan[1].Y);
        Assert.False(plan[0].IntersectsWith(plan[1]));
    }

    [Fact]
    public void NeverSwapsThemRound()
    {
        // dropped on the lower part of A: A must not jump above the dropped one
        var a = new Rectangle(700, 300, 400, 225);
        var r = new Rectangle(700, 450, 400, 225);
        var plan = Stacking.PlanBump(r, new[] { a }, new Rectangle[0], Screen, Gap);
        Assert.True(plan[0].Bottom <= r.Top);
    }

    [Fact]
    public void LockedPipInTheWay_MovesNothing()
    {
        var locked = new Rectangle(700, 420, 400, 225);
        Assert.Null(Stacking.PlanBump(new Rectangle(700, 300, 400, 225), new Rectangle[0], new[] { locked }, Screen, Gap));
    }

    [Fact]
    public void NeverPushesOntoALockedPip()
    {
        var a = new Rectangle(700, 420, 400, 225);
        var locked = new Rectangle(700, 660, 400, 225);       // right where A would be pushed
        var plan = Stacking.PlanBump(new Rectangle(700, 300, 400, 225), new[] { a }, new[] { locked }, Screen, Gap);
        if (plan != null) Assert.False(plan[0].IntersectsWith(locked));
    }

    [Fact]
    public void ResultsStayOnScreenAndDontOverlap()
    {
        var pips = new List<Rectangle> { new Rectangle(100, 100, 300, 170), new Rectangle(100, 290, 300, 170), new Rectangle(420, 100, 300, 170) };
        var r = new Rectangle(150, 150, 300, 170);
        var plan = Stacking.PlanBump(r, pips, new Rectangle[0], Screen, Gap);
        if (plan == null) return;
        var all = new List<Rectangle> { r };
        for (int i = 0; i < pips.Count; i++) all.Add(plan.ContainsKey(i) ? plan[i] : pips[i]);
        foreach (var x in all) Assert.True(Screen.Contains(x));
        for (int i = 0; i < all.Count; i++) for (int k = i + 1; k < all.Count; k++) Assert.False(all[i].IntersectsWith(all[k]), i + " overlaps " + k);
    }
}

public class PlaceTests
{
    static readonly Rectangle Screen = new Rectangle(0, 0, 1920, 1040);
    const int Gap = 12;
    static readonly Rectangle[] None = new Rectangle[0];
    static readonly Rectangle BottomRight = new Rectangle(1496, 791, 400, 225);

    static Rectangle Place(Rectangle want, Rectangle[] movable, Rectangle[] stuck, bool down, bool stackFirst, bool bump, out Dictionary<int, Rectangle> plan)
    {
        return Stacking.Place(want, movable, stuck, Screen, Gap, down, stackFirst, bump, out plan);
    }

    [Fact]
    public void FreeSpot_GoesThere()
    {
        Dictionary<int, Rectangle> plan;
        var r = Place(new Rectangle(100, 100, 400, 225), new[] { BottomRight }, None, false, true, true, out plan);
        Assert.Equal(new Rectangle(100, 100, 400, 225), r);
        Assert.Empty(plan);
    }

    [Fact]
    public void TakenCorner_StacksAboveIt_AndStaysOnScreen()
    {
        // the controller moves a PiP down into the bottom-right corner, where another PiP is
        Dictionary<int, Rectangle> plan;
        var r = Place(BottomRight, new[] { BottomRight }, None, false, true, true, out plan);
        Assert.Equal(BottomRight.Top - Gap - 225, r.Y);
        Assert.True(Screen.Contains(r));
        Assert.Empty(plan);
    }

    [Fact]
    public void StackingDownWouldGoOffScreen_StacksTheOtherWay()
    {
        // asked to stack below a PiP at the bottom: there's no room, so it goes above instead (the old code went off-screen)
        Dictionary<int, Rectangle> plan;
        var r = Place(BottomRight, new[] { BottomRight }, None, true, true, false, out plan);
        Assert.True(Screen.Contains(r));
        Assert.False(r.IntersectsWith(BottomRight));
    }

    [Fact]
    public void FullColumn_NeverOffScreen()
    {
        // three PiPs already fill the right edge top to bottom
        var col = new[] { new Rectangle(1496, 20, 400, 320), new Rectangle(1496, 352, 400, 320), new Rectangle(1496, 684, 400, 320) };
        Dictionary<int, Rectangle> plan;
        var r = Place(new Rectangle(1496, 684, 400, 320), new Rectangle[0], col, false, true, false, out plan);
        Assert.True(Screen.Contains(r));
    }

    [Fact]
    public void OffScreenWish_IsPulledBackOn()
    {
        Dictionary<int, Rectangle> plan;
        var r = Place(new Rectangle(1800, 900, 400, 225), None, None, false, true, true, out plan);
        Assert.True(Screen.Contains(r));
    }

    [Fact]
    public void ResizeIntoANeighbour_PushesIt()
    {
        // a PiP grows (stays where it is) into the one below it
        var below = new Rectangle(100, 400, 400, 225);
        Dictionary<int, Rectangle> plan;
        var r = Place(new Rectangle(100, 100, 400, 350), new[] { below }, None, false, false, true, out plan);
        Assert.Equal(new Rectangle(100, 100, 400, 350), r);
        Assert.Equal(100 + 350 + Gap, plan[0].Y);
    }

    [Fact]
    public void MovedToward_OnlyCountsRealMovesTheRightWay()
    {
        var a = new Rectangle(1496, 400, 400, 225);
        Assert.True(Stacking.MovedToward(a, new Rectangle(1496, 791, 400, 225), 0, 1));     // down
        Assert.False(Stacking.MovedToward(a, new Rectangle(1496, 403, 400, 225), 0, 1));    // a few pixels: no
        Assert.False(Stacking.MovedToward(a, new Rectangle(1496, 100, 400, 225), 0, 1));    // went up instead
        Assert.True(Stacking.MovedToward(a, new Rectangle(20, 400, 400, 225), -1, 0));      // left
    }
}
