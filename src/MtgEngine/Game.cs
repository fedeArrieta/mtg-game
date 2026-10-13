namespace MtgEngine;

/// <summary>
/// Una partida de dos jugadores con reglas simplificadas de Magic: maná por color, pila con prioridad,
/// pasos del turno, combate con bloqueos y acciones de estado. Cada acción pública valida las reglas y
/// lanza <see cref="RulesException"/> si la jugada no es legal. <see cref="Clone"/> permite deshacer.
/// </summary>
public sealed class Game
{
    public const int MaxHandSize = 7;

    /// <summary>Qué declaración espera el juego antes de dar prioridad.</summary>
    public enum Pending { None, Attackers, Blockers }

    public PlayerState[] Players { get; private set; }
    public int ActivePlayer { get; private set; }
    public int PriorityPlayer { get; private set; }
    public int Turn { get; private set; } = 1;
    public Step Step { get; private set; } = Step.Main1;
    public Pending Awaiting { get; private set; }
    public List<StackItem> Stack { get; private set; } = [];
    public List<Attack> Combat { get; private set; } = [];
    public List<string> History { get; private set; } = [];
    public int? Winner { get; private set; }
    public bool IsOver => Players.Any(p => p.HasLost);

    int nextId = 1;
    int passesInRow;

    public Game(string firstPlayer = "Jugador", string secondPlayer = "Oponente", int life = 20)
    {
        Players =
        [
            new PlayerState { Index = 0, Name = firstPlayer, Life = life },
            new PlayerState { Index = 1, Name = secondPlayer, Life = life },
        ];
    }

    public PlayerState Active => Players[ActivePlayer];
    public PlayerState Defending => Players[1 - ActivePlayer];
    public PlayerState Priority => Players[PriorityPlayer];

    // ---------- Preparación (para tests y puzzles) ----------

    /// <summary>Arranca la partida en un paso dado, con prioridad para el jugador activo.</summary>
    public void StartAt(int activePlayer, Step step, int turn = 1)
    {
        ActivePlayer = PriorityPlayer = activePlayer;
        Turn = turn;
        Step = step;
        Awaiting = step switch { Step.DeclareAttackers => Pending.Attackers, _ => Pending.None };
    }

    public CardInstance AddToHand(int player, string cardId) => AddToZone(player, CardCatalog.Get(cardId), Zone.Hand);
    public CardInstance AddToLibrary(int player, string cardId) => AddToZone(player, CardCatalog.Get(cardId), Zone.Library);

    public CardInstance AddToBattlefield(int player, string cardId, bool tapped = false, bool summoningSick = false)
    {
        var c = AddToZone(player, CardCatalog.Get(cardId), Zone.Battlefield);
        c.Tapped = tapped;
        c.SummoningSick = summoningSick;
        return c;
    }

    public CardInstance AddToGraveyard(int player, string cardId) => AddToZone(player, CardCatalog.Get(cardId), Zone.Graveyard);

    /// <summary>Pone una carta en la zona de mando como comandante de ese jugador.</summary>
    public CardInstance AddCommander(int player, string cardId) => AddToZone(player, CardCatalog.Get(cardId), Zone.Command, isCommander: true);

    /// <summary>Crea una ficha de criatura en juego, sin mareo (para preparar partidas).</summary>
    public CardInstance AddToken(int player, string name, int power, int toughness, params Keyword[] keywords)
    {
        var def = new CardDefinition { Id = "token", Name = name, Type = CardType.Creature, Power = power, Toughness = toughness, Keywords = [.. keywords] };
        return AddToZone(player, def, Zone.Battlefield, isToken: true);
    }

    public void SetLife(int player, int life) => Players[player].Life = life;

    CardInstance AddToZone(int player, CardDefinition def, Zone zone, bool isToken = false, bool isCommander = false)
    {
        var c = new CardInstance { Id = nextId++, Definition = def, Owner = player, Controller = player, Zone = zone, IsToken = isToken, IsCommander = isCommander };
        ZoneList(Players[player], zone).Add(c);
        return c;
    }

    static List<CardInstance> ZoneList(PlayerState p, Zone zone) => zone switch
    {
        Zone.Library => p.Library,
        Zone.Hand => p.Hand,
        Zone.Battlefield => p.Battlefield,
        Zone.Graveyard => p.Graveyard,
        Zone.Command => p.Command,
        _ => throw new ArgumentOutOfRangeException(nameof(zone)),
    };

    // ---------- Consultas ----------

    public IEnumerable<CardInstance> Creatures() => Players.SelectMany(p => p.Battlefield).Where(c => c.IsCreature);

    public CardInstance? FindOnBattlefield(int id) => Players.SelectMany(p => p.Battlefield).FirstOrDefault(c => c.Id == id);

    public CardInstance Find(int id) =>
        Players.SelectMany(AllCards)
            .Concat(Stack.Select(s => s.Card))
            .FirstOrDefault(c => c.Id == id) ?? throw new RulesException("Esa carta no existe.");

    static IEnumerable<CardInstance> AllCards(PlayerState p) =>
        p.Library.Concat(p.Hand).Concat(p.Battlefield).Concat(p.Graveyard).Concat(p.Command);

    /// <summary>Lo que cuesta lanzar la carta ahora, con el impuesto si es un comandante.</summary>
    public ManaCost CostOf(CardInstance c)
    {
        var cost = c.IsCommander ? c.Definition.ManaCost.PlusGeneric(2 * c.CommanderCasts) : c.Definition.ManaCost;
        if (c.Definition.Statics.Contains(StaticAbility.CostReducedByTotalPower))
            cost = cost.PlusGeneric(-Math.Min(cost.Generic, Players[c.Owner].Battlefield.Where(x => x.IsCreature).Sum(x => Math.Max(0, x.Power))));
        return cost;
    }

    /// <summary>Si el jugador puede lanzar hechizos ahora (Drómoka lo impide en el turno de su controlador).</summary>
    public bool CanCastSpells(int player) =>
        player == ActivePlayer || !Active.Battlefield.Any(c => c.Definition.Statics.Contains(StaticAbility.OpponentsCantCastOnYourTurn));

    public bool IsMainPhase => Step is Step.Main1 or Step.Main2;

    /// <summary>Si el jugador con prioridad puede lanzar a velocidad de conjuro ahora.</summary>
    public bool CanActAtSorcerySpeed => IsMainPhase && Stack.Count == 0 && PriorityPlayer == ActivePlayer && Awaiting == Pending.None;

    /// <summary>
    /// Si el jugador podría jugar ahora esta carta de su mano o de la zona de mando: es su momento, le alcanza el maná
    /// y, si pide objetivo, hay alguno válido. Sirve para resaltar las cartas jugables.
    /// </summary>
    public bool CanPlayNow(int player, CardInstance card)
    {
        if (IsOver || PriorityPlayer != player || Awaiting != Pending.None) return false;
        bool sorcerySpeed = CanActAtSorcerySpeed && ActivePlayer == player;
        if (card.IsLand) return sorcerySpeed && !Players[player].LandPlayedThisTurn && Players[player].Hand.Contains(card);
        var def = card.Definition;
        if (!CanCastSpells(player) || (def.Type != CardType.Instant && !sorcerySpeed)) return false;
        if (!CanAfford(player, CostOf(card))) return false;
        if (def.Target == TargetKind.None) return true;
        return Players.Any(p => IsLegalTarget(def, Target.OfPlayer(p.Index), player))
            || Creatures().Any(c => IsLegalTarget(def, Target.OfCard(c.Id), player));
    }

    // ---------- Acciones del jugador con prioridad ----------

    public void PlayLand(int cardId)
    {
        RequireOngoing();
        var p = Priority;
        var card = InHand(p, cardId);
        if (!card.IsLand) throw new RulesException($"{card.Name} no es una tierra.");
        if (!CanActAtSorcerySpeed) throw new RulesException("Las tierras se juegan en tu fase principal, con la pila vacía.");
        if (p.LandPlayedThisTurn) throw new RulesException("Ya jugaste una tierra este turno.");
        p.Hand.Remove(card);
        card.Zone = Zone.Battlefield;
        p.Battlefield.Add(card);
        p.LandPlayedThisTurn = true;
        passesInRow = 0;
        Log($"{p.Name} juega {card.Name}.");
    }

    /// <summary>Gira una tierra para agregar su maná. No usa la pila ni cambia la prioridad.</summary>
    public void TapForMana(int permanentId)
    {
        RequireOngoing();
        var p = Priority;
        var land = p.Battlefield.FirstOrDefault(c => c.Id == permanentId) ?? throw new RulesException("Esa tierra no es tuya o no está en juego.");
        if (land.Definition.Produces is not { } color) throw new RulesException($"{land.Name} no produce maná.");
        if (land.Tapped) throw new RulesException($"{land.Name} ya está girada.");
        if (land.IsCreature && !land.CanTapToAttack) throw new RulesException($"{land.Name} tiene mareo de invocación.");
        land.Tapped = true;
        p.Pool.Add(color);
    }

    /// <summary>
    /// Gira las tierras que falten para pagar la carta (usando primero el maná flotante) y la lanza.
    /// Si no alcanza, no gira nada y lanza <see cref="RulesException"/>.
    /// </summary>
    public void CastWithAutoTap(int cardId, Target? target = null)
    {
        RequireOngoing();
        var p = Priority;
        var card = Castable(p, cardId);
        var cost = CostOf(card);
        var lands = PlanLandsFor(p, cost)
            ?? throw new RulesException($"No te alcanza el maná para {card.Name}: cuesta {cost}.");
        var snapshot = lands.Select(l => (Land: l, l.Tapped)).ToList();
        var pool = p.Pool.Clone();
        foreach (var land in lands) TapForMana(land.Id);
        try { Cast(cardId, target); }
        catch
        {
            // Si la jugada no era legal por otro motivo, se deshace el giro de tierras.
            foreach (var (land, tapped) in snapshot) land.Tapped = tapped;
            p.RestorePool(pool);
            throw;
        }
    }

    /// <summary>Si con el maná flotante y las tierras sin girar alcanza para pagar el coste.</summary>
    public bool CanAfford(int player, ManaCost cost) => PlanLandsFor(Players[player], cost) is not null;

    /// <summary>Elige qué tierras girar: primero las del color que falta, después cualquiera para el genérico.</summary>
    static List<CardInstance>? PlanLandsFor(PlayerState p, ManaCost cost)
    {
        var pool = p.Pool.Clone();
        // Primero las tierras y después las criaturas de maná, para no girar atacantes si no hace falta.
        var untapped = p.Battlefield.Where(c => c.Definition.Produces is not null && !c.Tapped && (!c.IsCreature || c.CanTapToAttack))
            .OrderBy(c => c.IsCreature).ToList();
        var chosen = new List<CardInstance>();
        foreach (var color in Enum.GetValues<Color>())
        {
            int missing = cost[color] - pool[color];
            foreach (var land in untapped.Where(l => l.Definition.Produces == color).Take(Math.Max(0, missing)).ToList())
            {
                pool.Add(color);
                chosen.Add(land);
                untapped.Remove(land);
            }
        }
        // Para el genérico se gastan primero las tierras cuyo color hay más.
        var spare = untapped.GroupBy(l => (l.IsCreature, l.Definition.Produces)).OrderBy(g => g.Key.IsCreature).ThenByDescending(g => g.Count()).SelectMany(g => g).ToList();
        foreach (var land in spare)
        {
            if (pool.CanPay(cost)) break;
            pool.Add(land.Definition.Produces!.Value);
            chosen.Add(land);
        }
        return pool.CanPay(cost) ? chosen : null;
    }

    public void Cast(int cardId, Target? target = null)
    {
        RequireOngoing();
        if (Awaiting != Pending.None) throw new RulesException("Primero hay que declarar atacantes o bloqueadores.");
        var p = Priority;
        var card = Castable(p, cardId);
        var def = card.Definition;
        var cost = CostOf(card);
        if (def.IsPermanent && def.Type == CardType.Land) throw new RulesException("Las tierras no se lanzan: se juegan.");
        if (def.Type != CardType.Instant && !CanActAtSorcerySpeed)
            throw new RulesException($"{card.Name} solo se puede lanzar en tu fase principal, con la pila vacía.");
        if (!CanCastSpells(p.Index))
            throw new RulesException("No podés lanzar hechizos durante el turno del rival mientras controle a Drómoka.");
        if (def.Target != TargetKind.None)
        {
            if (target is null) throw new RulesException($"{card.Name} necesita un objetivo.");
            if (!IsLegalTarget(def, target.Value, p.Index)) throw new RulesException("Ese objetivo no es válido para este hechizo.");
        }
        else target = null;
        if (!p.Pool.Pay(cost))
            throw new RulesException($"No te alcanza el maná para {card.Name}: cuesta {cost} y tenés {p.Pool}.");

        if (card.Zone == Zone.Command)
        {
            p.Command.Remove(card);
            card.CommanderCasts++;
        }
        else p.Hand.Remove(card);
        card.Zone = Zone.Stack;
        Stack.Add(new StackItem(card, p.Index, target));
        passesInRow = 0;
        Log($"{p.Name} lanza {card.Name}{(target is { } t ? $" con objetivo {Describe(t)}" : "")}.");
    }

    /// <summary>Declara atacantes (la lista puede estar vacía). Solo el jugador activo, en el paso de atacantes.</summary>
    public void DeclareAttackers(IEnumerable<int> attackerIds)
    {
        RequireOngoing();
        if (Awaiting != Pending.Attackers) throw new RulesException("Ahora no se declaran atacantes.");
        var ids = attackerIds.Distinct().ToList();
        foreach (var id in ids)
        {
            var c = Active.Battlefield.FirstOrDefault(x => x.Id == id && x.IsCreature)
                ?? throw new RulesException("Solo podés atacar con tus criaturas en juego.");
            if (c.Has(Keyword.Defender)) throw new RulesException($"{c.Name} tiene defensor y no puede atacar.");
            if (!c.CanTapToAttack)
                throw new RulesException(c.Tapped ? $"{c.Name} está girada." : $"{c.Name} tiene mareo de invocación.");
        }
        Combat = ids.Select(id => new Attack { AttackerId = id }).ToList();
        foreach (var id in ids)
        {
            var c = FindOnBattlefield(id)!;
            if (!c.Has(Keyword.Vigilance)) c.Tapped = true;
        }
        Log(ids.Count == 0 ? $"{Active.Name} no ataca." : $"{Active.Name} ataca con {string.Join(", ", ids.Select(i => FindOnBattlefield(i)!.Name))}.");
        Awaiting = Pending.None;
        GivePriority(ActivePlayer);
        if (ids.Count == 0) { Step = Step.EndCombat; GivePriority(ActivePlayer); }
    }

    /// <summary>El jugador defensor asigna bloqueadores: bloqueador → atacante.</summary>
    public void DeclareBlockers(IReadOnlyDictionary<int, int> blockerToAttacker)
    {
        RequireOngoing();
        if (Awaiting != Pending.Blockers) throw new RulesException("Ahora no se declaran bloqueadores.");
        foreach (var (blockerId, attackerId) in blockerToAttacker)
        {
            var b = Defending.Battlefield.FirstOrDefault(x => x.Id == blockerId && x.IsCreature)
                ?? throw new RulesException("Solo podés bloquear con tus criaturas en juego.");
            if (b.Tapped) throw new RulesException($"{b.Name} está girada y no puede bloquear.");
            var attack = Combat.FirstOrDefault(a => a.AttackerId == attackerId) ?? throw new RulesException("Esa criatura no está atacando.");
            var attacker = FindOnBattlefield(attackerId)!;
            if (attacker.Has(Keyword.Flying) && !b.Has(Keyword.Flying) && !b.Has(Keyword.Reach))
                throw new RulesException($"{b.Name} no puede bloquear a {attacker.Name}: vuela.");
            attack.BlockerIds.Add(blockerId);
            attack.Blocked = true;
            Log($"{b.Name} bloquea a {attacker.Name}.");
        }
        Awaiting = Pending.None;
        GivePriority(ActivePlayer);
    }

    /// <summary>
    /// Pasa la prioridad. Si los dos pasan seguido, se resuelve lo de arriba de la pila
    /// o, con la pila vacía, se avanza al paso siguiente. Mientras se espera una declaración, pasar es declarar "nada".
    /// </summary>
    public void PassPriority()
    {
        RequireOngoing();
        if (Awaiting == Pending.Attackers) { DeclareAttackers([]); return; }
        if (Awaiting == Pending.Blockers) { DeclareBlockers(new Dictionary<int, int>()); return; }

        passesInRow++;
        if (passesInRow < 2)
        {
            PriorityPlayer = 1 - PriorityPlayer;
            return;
        }
        if (Stack.Count > 0)
        {
            ResolveTop();
            if (!IsOver) GivePriority(ActivePlayer);
        }
        else AdvanceStep();
    }

    // ---------- Pila ----------

    void ResolveTop()
    {
        var item = Stack[^1];
        Stack.RemoveAt(Stack.Count - 1);
        var card = item.Card;
        var owner = Players[card.Owner];
        var def = card.Definition;

        if (def.Target != TargetKind.None && (item.Target is not { } t || !IsLegalTarget(def, t, item.Controller)))
        {
            Log($"{card.Name} no tiene un objetivo válido y se neutraliza.");
            card.Zone = Zone.Graveyard;
            owner.Graveyard.Add(card);
            return;
        }

        Log($"Se resuelve {card.Name}.");
        if (def.Type == CardType.Creature)
        {
            card.Zone = Zone.Battlefield;
            card.SummoningSick = true;
            Players[item.Controller].Battlefield.Add(card);
        }
        var ctx = new EffectContext(this, card, item.Controller, item.Target);
        foreach (var effect in def.Effects)
        {
            if (def.Type == CardType.Creature && effect.UsesChosenTarget) continue; // las criaturas no eligen objetivo en esta versión
            effect.Apply(ctx);
        }
        if (def.IsSpell)
        {
            card.Zone = Zone.Graveyard;
            owner.Graveyard.Add(card);
        }
        CheckStateBasedActions();
    }

    /// <summary>Si el objetivo es válido para el hechizo lanzado por <paramref name="caster"/>.</summary>
    public bool IsLegalTarget(CardDefinition spell, Target t, int caster)
    {
        if (t.IsPlayer) return spell.Target is TargetKind.Any or TargetKind.Player && IsPlayerIndex(t.Player!.Value);
        if (spell.Target is not (TargetKind.Any or TargetKind.Creature)) return false;
        if (FindOnBattlefield(t.Card ?? -1) is not { IsCreature: true } c) return false;
        if (c.Has(Keyword.Hexproof) && c.Controller != caster) return false;
        return spell.TargetRestriction switch
        {
            TargetRestriction.AttackingOrBlocking => Combat.Any(a => a.AttackerId == c.Id || a.BlockerIds.Contains(c.Id)),
            _ => true,
        };
    }

    static bool IsPlayerIndex(int i) => i is 0 or 1;

    string Describe(Target t) => t.IsPlayer ? Players[t.Player!.Value].Name : Find(t.Card!.Value).Name;

    // ---------- Turno ----------

    void AdvanceStep()
    {
        foreach (var p in Players) p.Pool.Clear();
        Step = Step == Step.Cleanup ? Step.Untap : Step + 1;

        switch (Step)
        {
            case Step.Untap:
                ActivePlayer = 1 - ActivePlayer;
                Turn++;
                Active.LandPlayedThisTurn = false;
                foreach (var c in Active.Battlefield) { c.Tapped = false; c.SummoningSick = false; }
                Log($"Turno {Turn}: juega {Active.Name}.");
                AdvanceStep(); // en el paso de enderezar nadie recibe prioridad
                return;
            case Step.Draw:
                Draw(Active, 1);
                CheckStateBasedActions();
                break;
            case Step.DeclareAttackers:
                Combat = [];
                Awaiting = Pending.Attackers;
                PriorityPlayer = ActivePlayer;
                passesInRow = 0;
                return;
            case Step.DeclareBlockers:
                Awaiting = Pending.Blockers;
                PriorityPlayer = 1 - ActivePlayer;
                passesInRow = 0;
                return;
            case Step.CombatDamage:
                DealCombatDamage();
                break;
            case Step.EndCombat:
                Combat = [];
                break;
            case Step.Cleanup:
                Cleanup();
                AdvanceStep();
                return;
        }
        if (!IsOver) GivePriority(ActivePlayer);
    }

    void Cleanup()
    {
        while (Active.Hand.Count > MaxHandSize)
        {
            var c = Active.Hand[^1];
            Active.Hand.RemoveAt(Active.Hand.Count - 1);
            c.Zone = Zone.Graveyard;
            Active.Graveyard.Add(c);
            Log($"{Active.Name} descarta {c.Name}.");
        }
        foreach (var c in Players.SelectMany(p => p.Battlefield)) c.ClearEndOfTurn();
    }

    void GivePriority(int player)
    {
        PriorityPlayer = player;
        passesInRow = 0;
    }

    // ---------- Combate ----------

    void DealCombatDamage()
    {
        // Primero se calcula todo el daño y después se aplica junto, como en las reglas reales.
        var hits = new List<(CardInstance Source, CardInstance? Creature, PlayerState? Player, int Amount)>();
        foreach (var attack in Combat)
        {
            if (FindOnBattlefield(attack.AttackerId) is not { } attacker) continue;
            var blockers = attack.BlockerIds.Select(FindOnBattlefield).OfType<CardInstance>().ToList();
            int left = attacker.Power;
            if (!attack.Blocked)
            {
                hits.Add((attacker, null, Defending, left));
                continue;
            }
            foreach (var b in blockers)
            {
                hits.Add((b, attacker, null, b.Power));
                if (left <= 0) continue;
                int lethal = attacker.Has(Keyword.Deathtouch) ? 1 : Math.Max(0, b.Toughness - b.Damage);
                bool last = b == blockers[^1];
                int amount = last && !attacker.Has(Keyword.Trample) ? left : Math.Min(left, lethal);
                hits.Add((attacker, b, null, amount));
                left -= amount;
            }
            if (left > 0 && attacker.Has(Keyword.Trample)) hits.Add((attacker, null, Defending, left));
        }
        foreach (var (source, creature, player, amount) in hits.Where(h => h.Amount > 0))
        {
            if (creature is not null) DamageCreature(creature, amount, source);
            else DamagePlayer(player!, amount, source);
        }
        CheckStateBasedActions();
    }

    // ---------- Efectos de juego (los usan los efectos de cartas) ----------

    internal void DamagePlayer(PlayerState p, int amount, CardInstance source)
    {
        if (amount <= 0) return;
        p.Life -= amount;
        Log($"{source.Name} le hace {amount} de daño a {p.Name} ({p.Life} vidas).");
        Lifelink(source, amount);
    }

    internal void DamageCreature(CardInstance c, int amount, CardInstance source)
    {
        if (amount <= 0) return;
        c.Damage += amount;
        if (source.Has(Keyword.Deathtouch)) c.HitByDeathtouch = true;
        Log($"{source.Name} le hace {amount} de daño a {c.Name}.");
        Lifelink(source, amount);
    }

    void Lifelink(CardInstance source, int amount)
    {
        if (source.Has(Keyword.Lifelink)) GainLife(Players[source.Controller], amount);
    }

    internal void GainLife(PlayerState p, int amount)
    {
        p.Life += amount;
        Log($"{p.Name} gana {amount} vidas ({p.Life}).");
    }

    internal void LoseLife(PlayerState p, int amount)
    {
        p.Life -= amount;
        Log($"{p.Name} pierde {amount} vidas ({p.Life}).");
    }

    internal void Draw(PlayerState p, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (p.Library.Count == 0) { p.DrewFromEmptyLibrary = true; continue; }
            var c = p.Library[0];
            p.Library.RemoveAt(0);
            c.Zone = Zone.Hand;
            p.Hand.Add(c);
        }
        Log($"{p.Name} roba {(count == 1 ? "una carta" : $"{count} cartas")}.");
    }

    internal void PutOntoBattlefield(PlayerState p, CardDefinition def, bool isToken)
    {
        var c = AddToZone(p.Index, def, Zone.Battlefield, isToken);
        c.SummoningSick = true;
        Log($"{p.Name} crea {c}.");
    }

    internal void MoveToGraveyard(CardInstance c, string reason)
    {
        var controller = Players[c.Controller];
        if (!controller.Battlefield.Remove(c)) return;
        RemoveFromCombat(c);
        c.ClearEndOfTurn();
        c.Tapped = false;
        Log($"{c.Name} {reason}.");
        if (c.IsToken) { c.Zone = Zone.Exile; return; }
        if (ToCommandZone(c)) return;
        c.Zone = Zone.Graveyard;
        Players[c.Owner].Graveyard.Add(c);
    }

    internal void ReturnToHand(CardInstance c)
    {
        if (!Players[c.Controller].Battlefield.Remove(c)) return;
        RemoveFromCombat(c);
        c.ClearEndOfTurn();
        c.Tapped = false;
        if (c.IsToken) { Log($"{c.Name} deja de existir."); c.Zone = Zone.Exile; return; }
        if (ToCommandZone(c)) return;
        Log($"{c.Name} vuelve a la mano de su dueño.");
        c.Zone = Zone.Hand;
        Players[c.Owner].Hand.Add(c);
    }

    /// <summary>Un comandante que deja el campo vuelve a la zona de mando en vez de ir a otra zona.</summary>
    bool ToCommandZone(CardInstance c)
    {
        if (!c.IsCommander) return false;
        c.Zone = Zone.Command;
        Players[c.Owner].Command.Add(c);
        Log($"{c.Name} vuelve a la zona de mando.");
        return true;
    }

    /// <summary>Una criatura que deja el campo deja de atacar o bloquear. Un atacante bloqueado sigue bloqueado.</summary>
    void RemoveFromCombat(CardInstance c)
    {
        Combat.RemoveAll(a => a.AttackerId == c.Id);
        foreach (var a in Combat) a.BlockerIds.Remove(c.Id);
    }

    // ---------- Acciones de estado ----------

    void CheckStateBasedActions()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var c in Creatures().ToList())
            {
                if (c.Toughness <= 0) { MoveToGraveyard(c, "muere (resistencia 0)"); changed = true; }
                else if (c.Damage >= c.Toughness || (c.HitByDeathtouch && c.Damage > 0)) { MoveToGraveyard(c, "muere"); changed = true; }
            }
        } while (changed);

        foreach (var p in Players.Where(p => !p.HasLost && (p.Life <= 0 || p.DrewFromEmptyLibrary)))
        {
            p.HasLost = true;
            Log(p.Life <= 0 ? $"{p.Name} se quedó sin vidas y pierde." : $"{p.Name} no pudo robar y pierde.");
        }
        if (IsOver && Winner is null)
        {
            var alive = Players.Where(p => !p.HasLost).ToList();
            Winner = alive.Count == 1 ? alive[0].Index : null;
            Log(Winner is { } w ? $"¡{Players[w].Name} gana la partida!" : "La partida termina en empate.");
        }
    }

    // ---------- Utilidades ----------

    void RequireOngoing()
    {
        if (IsOver) throw new RulesException("La partida ya terminó.");
    }

    static CardInstance InHand(PlayerState p, int cardId) =>
        p.Hand.FirstOrDefault(c => c.Id == cardId) ?? throw new RulesException("Esa carta no está en tu mano.");

    /// <summary>Una carta que se puede lanzar: de la mano o, si es el comandante, de la zona de mando.</summary>
    static CardInstance Castable(PlayerState p, int cardId) =>
        p.Hand.Concat(p.Command).FirstOrDefault(c => c.Id == cardId) ?? throw new RulesException("Esa carta no está en tu mano.");

    internal void Log(string message) => History.Add(message);

    /// <summary>Copia completa e independiente del estado, para deshacer jugadas o probar líneas.</summary>
    public Game Clone()
    {
        var g = (Game)MemberwiseClone();
        var cards = Players.SelectMany(AllCards)
            .Concat(Stack.Select(s => s.Card))
            .ToDictionary(c => c.Id, c => c.Clone());
        g.Players = Players.Select(p => p.Clone(cards)).ToArray();
        g.Stack = Stack.Select(s => s with { Card = cards[s.Card.Id] }).ToList();
        g.Combat = Combat.Select(a => new Attack { AttackerId = a.AttackerId, BlockerIds = [.. a.BlockerIds], Blocked = a.Blocked }).ToList();
        g.History = [.. History];
        return g;
    }
}
