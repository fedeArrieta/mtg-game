using MtgEngine;
using Xunit;

namespace MtgEngine.Tests;

public class ManaTests
{
    [Fact]
    public void ParsesCosts()
    {
        var cost = ManaCost.Parse("{3}{G}{G}");
        Assert.Equal(3, cost.Generic);
        Assert.Equal(2, cost[Color.Green]);
        Assert.Equal(5, cost.ManaValue);
        Assert.Equal("{3}{G}{G}", cost.ToString());
    }

    [Fact]
    public void PaysColoredFirstThenGeneric()
    {
        var pool = new ManaPool();
        pool.Add(Color.Red, 2);
        pool.Add(Color.Green, 1);
        Assert.True(pool.Pay(ManaCost.Parse("{1}{G}")));
        Assert.Equal(1, pool[Color.Red]);
        Assert.Equal(0, pool[Color.Green]);
    }

    [Fact]
    public void RejectsMissingColor()
    {
        var pool = new ManaPool();
        pool.Add(Color.Red, 3);
        Assert.False(pool.Pay(ManaCost.Parse("{U}")));
        Assert.Equal(3, pool.Total);
    }

    [Fact]
    public void AllCatalogCardsParse()
    {
        Assert.Equal(29, CardCatalog.All.Count);
        foreach (var card in CardCatalog.All.Values) _ = card.ManaCost;
        Assert.Equal(Color.Red, CardCatalog.Get("montana").Produces);
        Assert.IsType<DealDamage>(Assert.Single(CardCatalog.Get("chispazo").Effects));
    }
}
