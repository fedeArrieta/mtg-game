using MtgEngine;

namespace MtgParty.Desktop.ViewModels;

/// <summary>Un paso del turno en la barra de fases: el actual se resalta y los anteriores se apagan.</summary>
public sealed record PhaseChip(string Label, bool Current, bool Past)
{
    static readonly (string Label, Step[] Steps)[] Phases =
    [
        ("Inicio", [Step.Untap, Step.Upkeep, Step.Draw]),
        ("Principal 1", [Step.Main1]),
        ("Atacantes", [Step.BeginCombat, Step.DeclareAttackers]),
        ("Bloqueos", [Step.DeclareBlockers]),
        ("Daño", [Step.CombatDamage, Step.EndCombat]),
        ("Principal 2", [Step.Main2]),
        ("Final", [Step.End, Step.Cleanup]),
    ];

    public static List<PhaseChip> For(Step step)
    {
        int current = Array.FindIndex(Phases, p => p.Steps.Contains(step));
        return [.. Phases.Select((p, i) => new PhaseChip(p.Label, i == current, i < current))];
    }
}
