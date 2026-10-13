using System.Text.Json;
using System.Text.RegularExpressions;
using MtgParty.Core;
using Xunit;

namespace MtgParty.Core.Tests;

public class HtmlExporterTests
{
    static JsonElement ExtractConfig(string html)
    {
        var match = Regex.Match(html, @"const CONFIG = (\{.*?\});\n");
        Assert.True(match.Success, "El HTML no tiene la configuración inyectada.");
        return JsonDocument.Parse(match.Groups[1].Value).RootElement;
    }

    [Fact]
    public void DefaultLibraryHasContent()
    {
        var config = CardLibrary.CreateDefaultConfig();
        Assert.Equal(26, config.Challenges.Count);
        Assert.Equal(18, config.Missions.Count);
        Assert.Equal(16, config.Planes.Count);
        Assert.Equal(4, config.Players.Count);
        Assert.Empty(HtmlExporter.Validate(config));
    }

    [Fact]
    public void ExportInjectsOnlyEnabledCardsAndPlayers()
    {
        var config = CardLibrary.CreateDefaultConfig();
        config.Title = "Viernes de Commander";
        config.StartingLife = 30;
        config.Challenges[0].Enabled = false;
        config.Planes.ForEach(p => p.Enabled = false);
        config.Players.Add(new Player { Name = "   " });

        var html = HtmlExporter.Export(config, gameId: "test");
        var json = ExtractConfig(html);

        Assert.DoesNotContain("__TITLE__", html);
        Assert.Contains("<title>Viernes de Commander</title>", html);
        Assert.Equal("test", json.GetProperty("gameId").GetString());
        Assert.Equal(30, json.GetProperty("startingLife").GetInt32());
        Assert.Equal(25, json.GetProperty("challenges").GetArrayLength());
        Assert.Equal("round", json.GetProperty("challenges")[0].GetProperty("duration").GetString());
        Assert.Equal(0, json.GetProperty("planes").GetArrayLength());
        Assert.Equal(4, json.GetProperty("players").GetArrayLength());
    }

    [Fact]
    public void ExportCannotBreakOutOfScript()
    {
        var config = CardLibrary.CreateDefaultConfig();
        config.Title = "<script>alert(1)</script>";
        config.Players[0].Name = "</script><b>x</b>";
        var html = HtmlExporter.Export(config);

        Assert.Single(Regex.Matches(html, "</script>"));
        Assert.Contains("&lt;script&gt;", html);
        Assert.Equal("</script><b>x</b>", ExtractConfig(html).GetProperty("players")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void ValidateRejectsTooFewPlayers()
    {
        var config = CardLibrary.CreateDefaultConfig();
        config.Players.RemoveRange(1, 3);
        Assert.NotEmpty(HtmlExporter.Validate(config));
        Assert.Throws<InvalidOperationException>(() => HtmlExporter.Export(config));
    }

    [Fact]
    public void PartyFileRoundTrips()
    {
        var config = CardLibrary.CreateDefaultConfig();
        config.Missions[2].Enabled = false;
        config.Challenges[1].Duration = ChallengeDuration.Game;
        var back = PartyJson.Deserialize(PartyJson.Serialize(config));
        Assert.False(back.Missions[2].Enabled);
        Assert.Equal(ChallengeDuration.Game, back.Challenges[1].Duration);
        Assert.Equal(config.Players.Count, back.Players.Count);
    }
}
