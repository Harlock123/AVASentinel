using AVASentinel.Engine;

namespace AVASentinel.Tests;

public class LandTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    public void The_sentinel_on_the_highest_flat_square_and_the_player_low_down_far_away(int level)
    {
        var l = LandBook.Build(level);
        Assert.True(l.Flat(l.Sentinel.X, l.Sentinel.Y));
        Assert.True(l.Flat(l.Start.X, l.Start.Y));
        for (int x = 0; x < Field.N; x++)
        for (int y = 0; y < Field.N; y++)
            if (l.Flat(x, y)) Assert.True(l.Corner[x, y] <= l.Corner[l.Sentinel.X, l.Sentinel.Y]);
        Assert.True(l.Corner[l.Sentinel.X, l.Sentinel.Y] - l.Corner[l.Start.X, l.Start.Y] >= 5);
        Assert.True(Math.Abs(l.Sentinel.X - l.Start.X) + Math.Abs(l.Sentinel.Y - l.Start.Y) > Field.N * 0.6);
    }

    [Fact]
    public void Trees_stand_on_flat_squares_and_there_is_plenty_to_build_on()
    {
        var l = LandBook.Build(1);
        Assert.NotEmpty(l.Trees);
        Assert.All(l.Trees, t => Assert.True(l.Flat(t.X, t.Y)));
        int flats = 0;
        for (int x = 0; x < Field.N; x++) for (int y = 0; y < Field.N; y++) if (l.Flat(x, y)) flats++;
        Assert.True(flats >= Field.N * Field.N / 5);
    }

    [Fact]
    public void The_ground_blends_between_the_corners()
    {
        var l = new LandDef();
        l.Corner[3, 3] = 2;
        Assert.Equal(1.0, l.Ground(3, 3), 6);
        Assert.Equal(0.25, l.Ground(3.5, 3.5) * 1, 6);
        Assert.Equal(0, l.Ground(4, 4), 6);
    }

    [Fact]
    public void A_hill_hides_what_is_behind_it()
    {
        var l = new LandDef();
        for (int x = 9; x <= 11; x++) for (int y = 0; y <= Field.N; y++) l.Corner[x, y] = 6;
        Assert.False(l.Clear((5.5, 5.5, 0.8), (15.5, 5.5, 0.02)));
        Assert.True(l.Clear((5.5, 5.5, 0.8), (8.0, 5.5, 0.02)));
    }

    [Fact]
    public void Each_landscape_is_the_same_every_time_and_gets_quicker()
    {
        var a = LandBook.Build(2);
        var b = LandBook.Build(2);
        Assert.Equal(a.Sentinel, b.Sentinel);
        Assert.Equal(a.Start, b.Start);
        Assert.True(LandBook.Build(5).TurnEvery < LandBook.Build(0).TurnEvery);
    }
}
