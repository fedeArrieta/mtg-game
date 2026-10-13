using System.Text.Json.Serialization;

namespace MtgEngine;

/// <summary>A quién afecta un efecto. "Chosen" es el objetivo elegido al lanzar el hechizo.</summary>
public enum Recipient { Default, Chosen, You, EachOpponent, EachCreature, EachPlayer }

/// <summary>Contexto de resolución: quién controla el efecto, su fuente y el objetivo elegido.</summary>
internal sealed record EffectContext(Game Game, CardInstance Source, int Controller, Target? Target);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DealDamage), "damage")]
[JsonDerivedType(typeof(GainLife), "gainLife")]
[JsonDerivedType(typeof(LoseLife), "loseLife")]
[JsonDerivedType(typeof(DrawCards), "draw")]
[JsonDerivedType(typeof(Pump), "pump")]
[JsonDerivedType(typeof(GrantKeyword), "grantKeyword")]
[JsonDerivedType(typeof(Destroy), "destroy")]
[JsonDerivedType(typeof(Bounce), "bounce")]
[JsonDerivedType(typeof(CreateToken), "token")]
[JsonDerivedType(typeof(AddMana), "addMana")]
public abstract record Effect
{
    public Recipient To { get; init; }
    protected abstract Recipient DefaultRecipient { get; }

    internal abstract void Apply(EffectContext ctx);

    Recipient Resolved => To == Recipient.Default ? DefaultRecipient : To;

    /// <summary>Si el efecto necesita el objetivo elegido al lanzar.</summary>
    internal bool UsesChosenTarget => Resolved == Recipient.Chosen;

    private protected IEnumerable<PlayerState> Players(EffectContext ctx) => Resolved switch
    {
        Recipient.You => [ctx.Game.Players[ctx.Controller]],
        Recipient.EachOpponent => [ctx.Game.Players[1 - ctx.Controller]],
        Recipient.EachPlayer => ctx.Game.Players,
        Recipient.Chosen when ctx.Target is { IsPlayer: true } t => [ctx.Game.Players[t.Player!.Value]],
        _ => [],
    };

    private protected IEnumerable<CardInstance> Creatures(EffectContext ctx) => Resolved switch
    {
        Recipient.EachCreature => ctx.Game.Creatures().ToList(),
        Recipient.Chosen when ctx.Target is { IsCard: true } t && ctx.Game.FindOnBattlefield(t.Card!.Value) is { } c => [c],
        _ => [],
    };
}

public sealed record DealDamage(int Amount) : Effect
{
    protected override Recipient DefaultRecipient => Recipient.Chosen;
    internal override void Apply(EffectContext ctx)
    {
        foreach (var p in Players(ctx)) ctx.Game.DamagePlayer(p, Amount, ctx.Source);
        foreach (var c in Creatures(ctx)) ctx.Game.DamageCreature(c, Amount, ctx.Source);
    }
}

public sealed record GainLife(int Amount) : Effect
{
    protected override Recipient DefaultRecipient => Recipient.You;
    internal override void Apply(EffectContext ctx)
    {
        foreach (var p in Players(ctx)) ctx.Game.GainLife(p, Amount);
    }
}

public sealed record LoseLife(int Amount) : Effect
{
    protected override Recipient DefaultRecipient => Recipient.EachOpponent;
    internal override void Apply(EffectContext ctx)
    {
        foreach (var p in Players(ctx)) ctx.Game.LoseLife(p, Amount);
    }
}

public sealed record DrawCards(int Amount) : Effect
{
    protected override Recipient DefaultRecipient => Recipient.You;
    internal override void Apply(EffectContext ctx)
    {
        foreach (var p in Players(ctx)) ctx.Game.Draw(p, Amount);
    }
}

/// <summary>+X/+Y hasta el final del turno.</summary>
public sealed record Pump(int Power, int Toughness) : Effect
{
    protected override Recipient DefaultRecipient => Recipient.Chosen;
    internal override void Apply(EffectContext ctx)
    {
        foreach (var c in Creatures(ctx))
        {
            c.PowerBonus += Power;
            c.ToughnessBonus += Toughness;
            ctx.Game.Log($"{c.Name} recibe {Power:+0;-0}/{Toughness:+0;-0} hasta el final del turno.");
        }
    }
}

/// <summary>Da una habilidad hasta el final del turno.</summary>
public sealed record GrantKeyword(Keyword Keyword) : Effect
{
    protected override Recipient DefaultRecipient => Recipient.Chosen;
    internal override void Apply(EffectContext ctx)
    {
        foreach (var c in Creatures(ctx)) c.TempKeywords.Add(Keyword);
    }
}

public sealed record Destroy : Effect
{
    protected override Recipient DefaultRecipient => Recipient.Chosen;
    internal override void Apply(EffectContext ctx)
    {
        foreach (var c in Creatures(ctx)) ctx.Game.MoveToGraveyard(c, "es destruida");
    }
}

/// <summary>Regresa una criatura a la mano de su dueño.</summary>
public sealed record Bounce : Effect
{
    protected override Recipient DefaultRecipient => Recipient.Chosen;
    internal override void Apply(EffectContext ctx)
    {
        foreach (var c in Creatures(ctx)) ctx.Game.ReturnToHand(c);
    }
}

public sealed record CreateToken(string Name, int Power, int Toughness, List<Keyword>? Keywords = null, int Count = 1) : Effect
{
    protected override Recipient DefaultRecipient => Recipient.You;
    internal override void Apply(EffectContext ctx)
    {
        var def = new CardDefinition
        {
            Id = "token",
            Name = Name,
            Type = CardType.Creature,
            Power = Power,
            Toughness = Toughness,
            Keywords = Keywords ?? [],
        };
        foreach (var p in Players(ctx))
            for (int i = 0; i < Count; i++)
                ctx.Game.PutOntoBattlefield(p, def, isToken: true);
    }
}

/// <summary>Agrega maná a la reserva de quien lo controla (rituales).</summary>
public sealed record AddMana(Color Color, int Amount) : Effect
{
    protected override Recipient DefaultRecipient => Recipient.You;
    internal override void Apply(EffectContext ctx)
    {
        foreach (var p in Players(ctx)) p.Pool.Add(Color, Amount);
    }
}
