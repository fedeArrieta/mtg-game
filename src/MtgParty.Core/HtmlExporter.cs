using System.Net;
using System.Text.Json;

namespace MtgParty.Core;

/// <summary>Genera el HTML autocontenido que se comparte con los jugadores.</summary>
public static class HtmlExporter
{
    const string ConfigPlaceholder = "/*__CONFIG__*/null";
    const string TitlePlaceholder = "__TITLE__";

    /// <summary>Devuelve los problemas que impiden exportar; vacío si la partida está lista.</summary>
    public static IReadOnlyList<string> Validate(PartyConfig config)
    {
        var errors = new List<string>();
        var players = config.Players.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
        if (players.Count < PartyConfig.MinPlayers || players.Count > PartyConfig.MaxPlayers)
            errors.Add($"La partida necesita entre {PartyConfig.MinPlayers} y {PartyConfig.MaxPlayers} jugadores con nombre.");
        if (config.StartingLife < 1)
            errors.Add("Las vidas iniciales tienen que ser al menos 1.");
        if (!config.Challenges.Any(c => c.Enabled) && !config.Missions.Any(m => m.Enabled) && !config.Planes.Any(p => p.Enabled))
            errors.Add("Activá al menos un desafío, una misión o un plano.");
        return errors;
    }

    public static string Export(PartyConfig config, string? gameId = null)
    {
        var errors = Validate(config);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

        var title = string.IsNullOrWhiteSpace(config.Title) ? "Mesa del Caos" : config.Title.Trim();
        var payload = new
        {
            // Identifica esta exportación para que el progreso guardado de otra partida no se mezcle.
            gameId = gameId ?? Guid.NewGuid().ToString("N")[..12],
            title,
            startingLife = config.StartingLife,
            autoEventEachRound = config.AutoEventEachRound,
            players = config.Players
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new { name = p.Name.Trim(), color = ManaColor.All.Contains(p.Color) ? p.Color : ManaColor.White }),
            challenges = config.Challenges.Where(c => c.Enabled)
                .Select(c => new { title = c.Title, text = c.Text, duration = c.Duration }),
            missions = config.Missions.Where(m => m.Enabled).Select(m => new { text = m.Text, reward = m.Reward }),
            planes = config.Planes.Where(p => p.Enabled)
                .Select(p => new { name = p.Name, staticEffect = p.StaticEffect, chaosEffect = p.ChaosEffect }),
        };

        // El codificador por defecto escapa < > & y comillas, así el JSON no puede cerrar el <script>.
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(PartyJson.Options) { WriteIndented = false });

        return Resources.Read("party-template.html")
            .Replace(ConfigPlaceholder, json)
            .Replace(TitlePlaceholder, WebUtility.HtmlEncode(title));
    }

    public static void ExportToFile(PartyConfig config, string path) => File.WriteAllText(path, Export(config));
}
