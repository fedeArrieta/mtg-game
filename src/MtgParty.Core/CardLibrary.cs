using System.Text.Json;

namespace MtgParty.Core;

/// <summary>Contenido original que viene con la app (desafíos, misiones y planos).</summary>
public static class CardLibrary
{
    public static PartyConfig CreateDefaultConfig()
    {
        var config = PartyJson.Deserialize(Resources.Read("default-cards.json"));
        config.Players =
        [
            new() { Name = "Jugador 1", Color = ManaColor.Red },
            new() { Name = "Jugador 2", Color = ManaColor.Blue },
            new() { Name = "Jugador 3", Color = ManaColor.Green },
            new() { Name = "Jugador 4", Color = ManaColor.Black },
        ];
        return config;
    }
}

internal static class Resources
{
    public static string Read(string name)
    {
        using var stream = typeof(Resources).Assembly.GetManifestResourceStream("MtgParty.Core." + name)
            ?? throw new InvalidOperationException($"Falta el recurso embebido {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>Lectura y escritura de archivos .mtgparty.</summary>
public static class PartyJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string Serialize(PartyConfig config) => JsonSerializer.Serialize(config, Options);

    public static PartyConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<PartyConfig>(json, Options) ?? throw new InvalidDataException("El archivo de partida está vacío.");

    public static void Save(PartyConfig config, string path) => File.WriteAllText(path, Serialize(config));

    public static PartyConfig Load(string path) => Deserialize(File.ReadAllText(path));
}
