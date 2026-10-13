using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Avalonia.Media.Imaging;
using MtgEngine;

namespace MtgParty.Desktop.Services;

/// <summary>
/// Las imágenes de las cartas reales. Se bajan de Scryfall la primera vez (la impresión en español si existe,
/// si no la inglesa) y quedan guardadas en la carpeta de datos del usuario, así después andan sin internet.
/// Si no hay imagen (sin conexión, o una ficha), la carta se dibuja con su texto.
/// </summary>
public static class CardImages
{
    /// <summary>Se puede apuntar a otro servidor con la variable de entorno MESADELCAOS_SCRYFALL (para probar).</summary>
    static readonly string ApiBase =
        (Environment.GetEnvironmentVariable("MESADELCAOS_SCRYFALL") is { Length: > 0 } custom ? custom : "https://api.scryfall.com").TrimEnd('/');

    static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MesaDelCaos", "cartas");

    static readonly HttpClient http = CreateClient();
    /// <summary>Scryfall pide no más de unas 10 consultas por segundo: se hacen de a una con una pausa.</summary>
    static readonly SemaphoreSlim gate = new(1, 1);
    static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(110);
    static readonly ConcurrentDictionary<string, Task<Loaded>> loaded = new();
    /// <summary>Si la red falla, no se reintenta por un rato para no trabar cada carta con un timeout.</summary>
    static DateTime offlineUntil = DateTime.MinValue;

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MesaDelCaos", "0.2"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(proyecto-de-fans-sin-fines-de-lucro)"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json", 0.9));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));
        return client;
    }

    /// <summary>La imagen ya cargada, si está; no espera.</summary>
    public static Bitmap? Ready(CardDefinition card) =>
        loaded.TryGetValue(Key(card), out var task) && task.IsCompletedSuccessfully ? task.Result.Image : null;

    /// <summary>La imagen de la carta, o null si no hay (fichas, o sin conexión y sin copia guardada).</summary>
    public static Task<Bitmap?> GetAsync(CardDefinition card)
    {
        if (string.IsNullOrEmpty(card.EnglishName)) return Task.FromResult<Bitmap?>(null);
        var key = Key(card);
        var task = loaded.GetOrAdd(key, _ => LoadAsync(card, Spanish));
        return task.ContinueWith(t =>
        {
            // Un fallo de red no se recuerda: la próxima vez que se pida la carta se vuelve a probar.
            if (t.Result.Retry) loaded.TryRemove(new(key, t));
            return t.Result.Image;
        }, TaskScheduler.Default);
    }

    readonly record struct Loaded(Bitmap? Image, bool Retry = false);

    /// <summary>Si se prefieren las impresiones en español.</summary>
    public static bool Spanish => UiSettings.Instance.SpanishImages;

    static string Key(CardDefinition card) => (Spanish ? "es/" : "en/") + card.Id;

    static async Task<Loaded> LoadAsync(CardDefinition card, bool spanish)
    {
        var file = Path.Combine(CacheDir, spanish ? "es" : "en", card.Id + ".jpg");
        try
        {
            if (!File.Exists(file))
            {
                if (DateTime.UtcNow < offlineUntil) return new(null, Retry: true);
                var bytes = await DownloadAsync(card.EnglishName, spanish);
                if (bytes is null) return new(null);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllBytesAsync(file + ".tmp", bytes);
                File.Move(file + ".tmp", file, overwrite: true);
            }
            return new(await Task.Run(() =>
            {
                using var stream = File.OpenRead(file);
                return new Bitmap(stream);
            }));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            offlineUntil = DateTime.UtcNow.AddMinutes(1);
            return new(null, Retry: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            // Una imagen rota en el disco: se borra para bajarla de nuevo la próxima vez.
            try { File.Delete(file); } catch (IOException) { }
            return new(null, Retry: true);
        }
    }

    static async Task<byte[]?> DownloadAsync(string englishName, bool spanish)
    {
        var quoted = Uri.EscapeDataString($"!\"{englishName}\"");
        if (spanish)
        {
            // La impresión en español más nueva, si hay alguna.
            var url = await Throttled(async () =>
            {
                using var response = await http.GetAsync($"{ApiBase}/cards/search?q={quoted}%20lang%3Aes&unique=prints&order=released&dir=desc");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("data").EnumerateArray().Select(ImageUrl).FirstOrDefault(u => u is not null);
            });
            if (url is not null) return await Throttled(() => http.GetByteArrayAsync(url));
        }
        return await Throttled(async () =>
        {
            using var response = await http.GetAsync($"{ApiBase}/cards/named?exact={Uri.EscapeDataString(englishName)}&format=image&version=normal");
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        });
    }

    static string? ImageUrl(JsonElement card)
    {
        if (card.TryGetProperty("image_uris", out var uris) && uris.TryGetProperty("normal", out var normal)) return normal.GetString();
        if (card.TryGetProperty("card_faces", out var faces))
            foreach (var face in faces.EnumerateArray())
                if (face.TryGetProperty("image_uris", out var f) && f.TryGetProperty("normal", out var n)) return n.GetString();
        return null;
    }

    static async Task<T> Throttled<T>(Func<Task<T>> request)
    {
        await gate.WaitAsync();
        try { return await request(); }
        finally
        {
            await Task.Delay(Pause);
            gate.Release();
        }
    }
}
