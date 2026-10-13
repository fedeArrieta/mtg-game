using MtgEngine;

namespace MtgEngine.Tests;

static class TestHelpers
{
    public const int Me = 0, Opp = 1;

    /// <summary>Partida en la fase principal del jugador 0, con bibliotecas para que nadie pierda por robar.</summary>
    public static Game NewGame()
    {
        var g = new Game("Fede", "Rival");
        for (int i = 0; i < 10; i++) { g.AddToLibrary(Me, "lobo-musgo"); g.AddToLibrary(Opp, "lobo-musgo"); }
        g.StartAt(Me, Step.Main1);
        return g;
    }

    /// <summary>Pone tierras en juego y las gira todas para tener el maná en la reserva.</summary>
    public static void AddMana(this Game g, int player, params string[] lands)
    {
        foreach (var land in lands) g.TapForMana(g.AddToBattlefield(player, land).Id);
    }

    /// <summary>Ambos jugadores pasan la prioridad una vez.</summary>
    public static void BothPass(this Game g)
    {
        g.PassPriority();
        g.PassPriority();
    }

    /// <summary>Pasa prioridades hasta llegar al paso pedido.</summary>
    public static void PassUntil(this Game g, Step step)
    {
        for (int guard = 0; g.Step != step || g.Stack.Count > 0; guard++)
        {
            if (guard > 200) throw new InvalidOperationException($"Nunca se llegó a {step}.");
            g.PassPriority();
        }
    }
}
