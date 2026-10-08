using System.Text.Json;
using HarmonyLib;
using Il2CppMenace;
using Il2CppMenace.Items;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Il2CppMenace.Tags;
using Il2CppMenace.Tools;
using MelonLoader.Utils;
using UnityEngine;
using Math = System.Math;
using Path = System.IO.Path;

namespace MenaceSnipersPromise;

internal sealed class WaybackerDefinition
{
    public string Name { get; set; } = "";
    public string LeaderId { get; set; } = "";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string RussianTitle { get; set; } = "";
    public string OriginalId { get; set; } = "";
    public string Quote { get; set; } = "";
    public string Effect { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool Pilot { get; set; }
    internal UnitLeaderTemplate Leader = null!;
    internal PerkTemplate Original = null!;
    internal PerkTemplate Perk = null!;
}

internal sealed class WaybackerTurn
{
    internal int Attacks;
    internal bool KillRefunded, MoveShotSpent, TurnShotSpent, HasTurned, OwnTurn;
    internal int EscapeTiles, EscapeEnds, MedicalEnds;
    internal int LastMovedTiles;
    internal bool? NearbyKnown;
    internal bool FirstAttack, MoveAttack, TurnAttack;
    internal IntPtr ActiveAttack;
    internal Tile? PreviewTarget;
}

internal sealed class WaybackerPerks
{
    internal readonly Dictionary<string, WaybackerDefinition> Leaders = new();
    internal readonly Dictionary<string, WaybackerDefinition> Perks = new();
    internal readonly Dictionary<IntPtr, WaybackerTurn> Turns = new();
    internal static WaybackerPerks Current => SnipersPromiseMod.Instance.Waybackers;
    internal int PredictionDepth;
    internal bool Ready => Leaders.Count == 21;
    private WaybackerDefinition[]? definitions;
    private IntPtr tacticalSession;

    internal bool Ensure(SnipersPromiseMod mod)
    {
        if (Ready) { foreach (var d in Leaders.Values) d.Leader.InitialPerk = d.Perk; return true; }
        definitions ??= JsonSerializer.Deserialize<WaybackerDefinition[]>(
            File.ReadAllText(Path.Combine(MelonEnvironment.GameRootDirectory, "Mods", "MenaceSnipersPromise", "waybackers-perks.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Perk manifest missing.");
        var templates = Resources.FindObjectsOfTypeAll<UnitLeaderTemplate>();
        foreach (var d in definitions)
        {
            if (Leaders.ContainsKey(d.LeaderId)) { d.Leader.InitialPerk = d.Perk; continue; }
            var leader = templates.FirstOrDefault(t => t.name == d.LeaderId);
            if (leader == null) continue;
            var original = DataTemplateLoader.Get<PerkTemplate>(d.OriginalId);
            if (original == null || leader.InitialPerk?.name != d.OriginalId)
                throw new InvalidOperationException("Unexpected starting perk for " + d.Name);
            d.Leader = leader;
            d.Original = original;
            d.Perk = mod.CreatePerk(d.Id, d.Title, "\"" + d.Quote + "\"\n\n" + d.Effect,
                d.Effect, "waybackers/" + d.Icon, d.Name == "Sigrid" ? original : mod.Original, d.Name == "Sigrid");
            leader.InitialPerk = d.Perk;
            Leaders.Add(d.LeaderId, d);
            Perks.Add(d.Id, d);
            MelonLoader.MelonLogger.Msg("Waybackers: registered " + d.Name + " / " + d.Title);
        }
        return Ready;
    }

    internal void Migrate(BaseUnitLeader? leader)
    {
        if (leader?.LeaderTemplate == null || !Leaders.TryGetValue(leader.LeaderTemplate.name, out var d) || leader.m_Perks == null) return;
        // Only the starting copy is migrated. A later learned copy of the old perk stays learned.
        var list = leader.m_Perks;
        if (!leader.HasPerk(d.Perk))
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i]?.name == d.OriginalId) { list[i] = d.Perk; break; }
        }
        var skills = leader.GetSkills();
        if (skills == null || !leader.HasPerk(d.Perk)) return;
        bool changed = false;
        if (!leader.HasPerk(d.Original)) changed |= skills.Remove(d.Original);
        if (skills.GetSkillByID(d.Id) == null) { skills.Add(d.Perk.CreateSkill(new())); changed = true; }
        if (changed) skills.Update();
    }

    internal WaybackerDefinition? Definition(Actor? actor)
    {
        var leader = actor?.TryCast<UnitActor>()?.GetLeader();
        if (leader?.LeaderTemplate == null || !Leaders.TryGetValue(leader.LeaderTemplate.name, out var d) || !leader.HasPerk(d.Perk)) return null;
        if (d.Pilot && !actor!.IsVehicle()) return null;
        return d;
    }

    internal WaybackerTurn Turn(Actor actor)
    {
        var session = TacticalManager.Get()?.Pointer ?? IntPtr.Zero;
        if (session != tacticalSession) { Turns.Clear(); tacticalSession = session; }
        if (!Turns.TryGetValue(actor.Pointer, out var state)) Turns[actor.Pointer] = state = new();
        return state;
    }

    internal static WeaponTemplate? Weapon(Skill skill) => skill.GetItem()?.GetTemplate()?.TryCast<WeaponTemplate>();
    internal static bool Primary(WeaponTemplate? w) => w?.SlotType == ItemSlot.InfantryWeapon;
    internal static bool Sniper(WeaponTemplate? w) => w != null && SnipersPromiseMod.IsSniperWeapon(w);
    internal static bool MachineGun(WeaponTemplate? w)
    {
        if (w == null || w.SlotType != ItemSlot.InfantryWeapon && w.SlotType != ItemSlot.InfantrySpecial) return false;
        // Native machine guns have empty public tags; their granted attacks identify the weapon.
        if (w.name.Contains("machine_gun") || w.name.Contains("machinegun")) return true;
        var granted = w.SkillsGranted;
        if (granted != null) for (var i = 0; i < granted.Count; i++)
            if (granted[i].name.Contains("machinegun") || granted[i].name.Contains("machine_gun")) return true;
        return false;
    }
    internal static bool Flamethrower(Skill skill) => Weapon(skill)?.name == "specialweapon.flamethrower"
        && skill.GetID() == "active.shoot_flamethrower";
    internal static bool Walker(Actor actor) => actor.IsVehicle() &&
        (actor.HasTag(TagType.WALKER) || actor.GetTemplate()?.name.Contains("walker") == true);
    internal static bool VehicleGun(Skill skill) => skill.IsAttack() && skill.GetActor()?.IsVehicle() == true && Weapon(skill) != null;
    internal static int Distance(Tile? a, Tile? b) => a == null || b == null ? int.MaxValue : a.GetDistanceTo(b);
    internal static bool Cover(Actor actor) => actor.GetTile() is { } t && (t.HasCover() || t.GetCoverMask() != 0);

    internal static IEnumerable<Actor> Actors()
    {
        var manager = TacticalManager.Get();
        if (manager == null) yield break;
        var factions = manager.GetFactions();
        if (factions == null) yield break;
        for (var f = 0; f < factions.Length; f++)
        {
            var actors = factions[f]?.GetActors();
            if (actors == null) continue;
            for (var i = 0; i < actors.Count; i++) if (actors[i] != null && actors[i].m_IsAlive) yield return actors[i];
        }
    }
    internal static bool Ally(Actor a, Actor b) => a.Pointer != b.Pointer && (a.GetFactionID() == b.GetFactionID() || a.IsAlliedWith(b.GetFactionID()));
    internal static bool NearbyInfantry(Actor actor, int distance) => Actors().Any(a => a.IsInfantry() && Ally(actor, a) && Distance(actor.GetTile(), a.GetTile()) <= distance);
    internal static bool Concealed(Actor actor)
    {
        // Concealment means not detected by any opposing faction, regardless of player visibility.
        var manager = TacticalManager.Get();
        if (manager == null) return false;
        var factions = manager.GetFactions();
        bool opponent = false;
        for (var i = 0; i < factions.Length; i++)
        {
            if (factions[i] == null || factions[i].IsAlliedWith(actor.GetFactionID())) continue;
            opponent = true;
            if (actor.IsDetectedByFaction(i)) return false;
        }
        return opponent;
    }

    internal void Passive(Skill skill, EntityProperties p)
    {
        if (!Perks.TryGetValue(skill.GetID(), out var d)) return;
        var actor = skill.GetActor();
        if (d.Pilot && actor != null && !actor.IsVehicle()) return;
        switch (d.Name)
        {
            case "Bulk": p.HitpointsPerElementMult *= 1.1f; break;
            case "Creed": p.Discipline += 10; break;
            case "Crow": p.Vision += 1; p.DeployCostMult *= .9f; break;
            case "Lynx": p.Concealment += 1; p.Detection += 1; break;
            case "Luo": if (actor != null && NearbyInfantry(actor, 3)) p.Discipline += 10; break;
        }
    }

    internal int HitBonus(Skill skill, Tile from, Tile to, Entity? target)
    {
        var actor = skill.GetActor();
        var d = Definition(actor);
        if (d == null || !skill.IsAttack()) return 0;
        var t = Turn(actor!);
        t.PreviewTarget = to;
        var w = Weapon(skill);
        var enemy = target ?? to.GetActorOnTile();
        return d.Name switch
        {
            "Ash" when enemy?.GetFaction() == FactionType.Pirates => 15,
            "Caleb" when Primary(w) && actor!.TilesMovedThisTurn == 0 && Cover(actor) => 10,
            "Camila" when Sniper(w) && t.Attacks == 0 => 10,
            "Luo" when Primary(w) && NearbyInfantry(actor!, 3) => 5,
            "Lynx" when Concealed(actor!) => 10,
            "Marcus" when Primary(w) && Distance(from, to) <= 3 => 10,
            "Markov" when MachineGun(w) && actor!.TilesMovedThisTurn == 0 => 10,
            "Rook" when VehicleGun(skill) && t.HasTurned && !t.TurnShotSpent => 10,
            "Track" when VehicleGun(skill) && actor!.TilesMovedThisTurn == 0 => 10,
            "Viper" when VehicleGun(skill) && Walker(actor!) && actor!.TilesMovedThisTurn > 0 && !t.MoveShotSpent => 10,
            _ => 0
        };
    }

    internal float DamageMultiplier(Skill skill, Tile? targetTile, Entity? target, bool immediate)
    {
        var actor = skill.GetActor();
        var d = Definition(actor);
        if (d == null || !skill.IsAttack()) return 1;
        var t = Turn(actor!);
        var first = t.Attacks == 0 || immediate && t.ActiveAttack == skill.Pointer && t.FirstAttack;
        var entity = target ?? targetTile?.GetActorOnTile();
        return d.Name switch
        {
            "Ash" when entity?.GetFaction() == FactionType.Pirates => 1.1f,
            "Camila" when Sniper(Weapon(skill)) && first => 1.15f,
            "Darius" when first && Distance(actor!.GetTile(), targetTile) <= 3 => 1.2f,
            "Null" when entity != null && entity.GetHitpointsPct() < .5f => 1.15f,
            "Voss" when Flamethrower(skill) => 1.2f,
            _ => 1
        };
    }

    internal float SuppressionMultiplier(Skill skill, Tile? target, bool immediate)
    {
        var actor = skill.GetActor();
        var d = Definition(actor);
        if (d == null || !skill.IsAttack()) return 1;
        var t = Turn(actor!);
        var first = t.Attacks == 0 || immediate && t.ActiveAttack == skill.Pointer && t.FirstAttack;
        return d.Name switch
        {
            "Darius" when first && Distance(actor!.GetTile(), target) <= 3 => 1.25f,
            "Markov" when MachineGun(Weapon(skill)) => 1.5f,
            "Voss" when Flamethrower(skill) => 1.25f,
            _ => 1
        };
    }

    internal void Predict(Skill skill, Tile? tile, Entity? target, EntityProperties p)
    {
        p.DamageMult *= DamageMultiplier(skill, tile, target, false);
        if (Definition(skill.GetActor())?.Name == "Track" && VehicleGun(skill) && skill.GetActor().TilesMovedThisTurn == 0) p.ArmorPenetrationMult *= 1.15f;
    }

    internal float IncomingSuppression(Actor actor)
    {
        var d = Definition(actor);
        float mult = Turn(actor).MedicalEnds > 0 ? .8f : 1f;
        if (d?.Name == "Bulk" && (MachineGun(actor.GetItems()?.GetItemAtSlot(ItemSlot.InfantryWeapon)?.GetTemplate()?.TryCast<WeaponTemplate>()) || MachineGun(actor.GetItems()?.GetItemAtSlot(ItemSlot.InfantrySpecial)?.GetTemplate()?.TryCast<WeaponTemplate>()))) mult *= .85f;
        if (d?.Name == "Caleb" && actor.TilesMovedThisTurn == 0 && Cover(actor)) mult *= .8f;
        return mult;
    }

    internal bool Protected(Actor? actor) => actor != null && actor.IsInfantry() && Actors().Any(a =>
        Definition(a)?.Name == "Don" && Ally(actor, a) && Distance(actor.GetTile(), a.GetTile()) <= 2);

    internal void Defense(SkillContainer container, Skill? skill, EntityProperties result)
    {
        if (skill?.IsAttack() != true) return;
        var actor = container.GetOwner()?.TryCast<Actor>();
        if (Protected(actor)) result.DamageSustainedMult *= .9f;
    }

    internal int Cost(Skill skill, int cost)
    {
        var actor = skill.GetActor();
        var d = Definition(actor);
        if (d == null) return cost;
        var t = Turn(actor!);
        float multiplier = 1;
        if (d.Name == "Sigrid" && skill.GetID() == d.Id) multiplier = .75f;
        if (d.Name == "Creed" && skill.IsAttack() && Primary(Weapon(skill)) && t.Attacks == 0) multiplier = .9f;
        if (d.Name == "Marcus" && skill.IsAttack() && Primary(Weapon(skill)) && Distance(actor!.GetTile(), t.PreviewTarget ?? skill.GetLastTargetTile()) <= 3) multiplier = .9f;
        return multiplier == 1 || cost == 0 ? cost : Math.Max(1, (int)Math.Ceiling(cost * multiplier));
    }

    internal void BeginUse(Skill skill, Tile target)
    {
        var actor = skill.GetActor();
        if (actor == null || Definition(actor) == null) return;
        var t = Turn(actor);
        t.PreviewTarget = target;
        if (!skill.IsAttack()) return;
        t.ActiveAttack = skill.Pointer;
        t.FirstAttack = t.Attacks == 0;
        t.MoveAttack = actor.TilesMovedThisTurn > 0 && !t.MoveShotSpent;
        t.TurnAttack = t.HasTurned && !t.TurnShotSpent;
    }
    internal void Used(Skill skill, Tile target, bool success)
    {
        var actor = skill.GetActor();
        var d = Definition(actor);
        if (d == null || !success) return;
        var t = Turn(actor!);
        if (skill.IsAttack())
        {
            if (d.Name == "Talia" && t.Attacks == 0) { t.EscapeTiles = 2; t.EscapeEnds = 1; t.LastMovedTiles = actor!.TilesMovedThisTurn; }
            t.Attacks++;
            if (t.MoveAttack) t.MoveShotSpent = true;
            if (t.TurnAttack) t.TurnShotSpent = true;
        }
        else if (d.Name == "Sigrid" && skill.GetID() == d.Id)
        {
            var treated = target.GetActorOnTile() ?? actor;
            var treatment = Turn(treated!);
            treatment.MedicalEnds = treatment.OwnTurn ? 2 : 1;
        }
    }
    internal void Start(Actor actor)
    {
        var t = Turn(actor);
        t.Attacks = 0; t.KillRefunded = false; t.MoveShotSpent = false; t.TurnShotSpent = false;
        t.HasTurned = false; t.ActiveAttack = IntPtr.Zero; t.FirstAttack = false; t.MoveAttack = false; t.TurnAttack = false;
        t.PreviewTarget = null; t.OwnTurn = true;
        t.LastMovedTiles = 0;
        if (Definition(actor)?.Name == "Talia") { t.EscapeTiles = 0; t.EscapeEnds = 0; }
    }
    internal void End(Actor actor)
    {
        var t = Turn(actor);
        t.OwnTurn = false;
        if (t.MedicalEnds > 0) --t.MedicalEnds;
        if (t.EscapeEnds > 0 && --t.EscapeEnds == 0) t.EscapeTiles = 0;
    }
    internal void Moved(Actor actor)
    {
        var t = Turn(actor);
        var delta = Math.Max(0, actor.TilesMovedThisTurn - t.LastMovedTiles);
        if (delta > 0) t.EscapeTiles = Math.Max(0, t.EscapeTiles - delta);
        t.LastMovedTiles = actor.TilesMovedThisTurn;
    }
    internal void Killed(Skill skill)
    {
        var actor = skill.GetActor();
        if (Definition(actor)?.Name != "Null" || !skill.IsAttack()) return;
        var t = Turn(actor!);
        if (t.KillRefunded) return;
        t.KillRefunded = true;
        actor!.SetActionPoints(actor.GetActionPoints() + 20, true);
    }
    internal void Damaged(SkillContainer owner, Skill? skill, DamageInfo damage)
    {
        var actor = owner.GetOwner()?.TryCast<Actor>();
        if (actor == null || Definition(actor)?.Name != "Vale" || skill?.IsAttack() != true || damage.Damage <= 0) return;
        var t = Turn(actor);
        t.EscapeTiles = 2;
        t.EscapeEnds = t.OwnTurn ? 2 : 1;
        t.LastMovedTiles = actor.TilesMovedThisTurn;
    }
    internal void RefreshNearby()
    {
        foreach (var actor in Actors())
            if (Definition(actor)?.Name == "Luo")
            {
                var nearby = NearbyInfantry(actor, 3);
                var t = Turn(actor);
                if (t.NearbyKnown == nearby) continue;
                t.NearbyKnown = nearby;
                actor.GetSkills()?.Update();
            }
    }
}

[HarmonyPatch(typeof(Skill), nameof(Skill.OnUpdate))]
internal static class WaybackerStatsPatch
{
    private static void Postfix(Skill __instance, EntityProperties _properties) =>
        SnipersPromiseMod.Instance.Guard(() => WaybackerPerks.Current.Passive(__instance, _properties));
}
[HarmonyPatch(typeof(Skill), nameof(Skill.GetHitchance))]
internal static class WaybackerHitPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Skill __instance, Tile _from, Tile _targetTile, Entity _overrideTargetEntity, ref HitChance __result)
    {
        var result = __result;
        SnipersPromiseMod.Instance.Guard(() => {
            if (!result.AlwaysHits) result.FinalValue = Math.Clamp(result.FinalValue + WaybackerPerks.Current.HitBonus(__instance, _from, _targetTile, _overrideTargetEntity), 0, 100);
        });
        __result = result;
    }
}
[HarmonyPatch(typeof(Skill), nameof(Skill.GetActionPointCost))]
internal static class WaybackerCostPatch
{
    private static void Postfix(Skill __instance, ref int __result)
    {
        var cost = __result;
        SnipersPromiseMod.Instance.Guard(() => cost = WaybackerPerks.Current.Cost(__instance, cost));
        __result = cost;
    }
}
[HarmonyPatch(typeof(Skill), nameof(Skill.Use))]
internal static class WaybackerUsePatch
{
    private static void Prefix(Skill __instance, Tile _targetTile) => SnipersPromiseMod.Instance.Guard(() => WaybackerPerks.Current.BeginUse(__instance, _targetTile));
}
[HarmonyPatch(typeof(SkillContainer), nameof(SkillContainer.OnSkillUsed))]
internal static class WaybackerUsedEventPatch
{
    private static void Postfix(Skill _skill, Tile _targetTile) => SnipersPromiseMod.Instance.Guard(() => WaybackerPerks.Current.Used(_skill, _targetTile, true));
}
[HarmonyPatch(typeof(SkillContainer), nameof(SkillContainer.OnBeforeTargetHit))]
internal static class WaybackerDamagePatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Skill _skill, Entity _targetEntity, DamageInfo _damageInfo) =>
        SnipersPromiseMod.Instance.Guard(() => {
            var mod = WaybackerPerks.Current;
            _damageInfo.Damage = (int)Math.Round(_damageInfo.Damage * mod.DamageMultiplier(_skill, _targetEntity.GetTile(), _targetEntity, true), MidpointRounding.AwayFromZero);
            if (mod.Definition(_skill.GetActor())?.Name == "Track" && WaybackerPerks.VehicleGun(_skill) && _skill.GetActor().TilesMovedThisTurn == 0)
                _damageInfo.ArmorPenetration = (int)Math.Round(_damageInfo.ArmorPenetration * 1.15f, MidpointRounding.AwayFromZero);
        });
}
[HarmonyPatch(typeof(Skill), nameof(Skill.GetExpectedDamage), new[] { typeof(Tile), typeof(Tile), typeof(Tile), typeof(Entity), typeof(EntityProperties), typeof(EntityProperties), typeof(int), typeof(Skill.ExpectedDamage) })]
internal static class WaybackerExpectedDamagePatch
{
    private static void Prefix(Skill __instance, Tile _targetTile, Entity _overrideTargetEntity, ref EntityProperties _properties, ref EntityProperties _defenderProperties, out bool __state)
    {
        __state = true;
        var mod = WaybackerPerks.Current;
        mod.PredictionDepth++;
        if (_properties != null)
        {
            _properties = _properties.GetClone();
            mod.Predict(__instance, _targetTile, _overrideTargetEntity, _properties);
        }
    }
    private static Exception? Finalizer(bool __state, Exception? __exception)
    {
        if (__state) --WaybackerPerks.Current.PredictionDepth;
        return __exception;
    }
}
[HarmonyPatch(typeof(SkillContainer), nameof(SkillContainer.BuildPropertiesForUse))]
internal static class WaybackerPredictionPropertiesPatch
{
    private static void Postfix(Skill _skill, Tile _targetTile, Entity _overrideTargetEntity, EntityProperties __result) =>
        SnipersPromiseMod.Instance.Guard(() => { if (WaybackerPerks.Current.PredictionDepth > 0) WaybackerPerks.Current.Predict(_skill, _targetTile, _overrideTargetEntity, __result); });
}
[HarmonyPatch(typeof(Skill), nameof(Skill.ApplySuppression))]
internal static class WaybackerOutgoingSuppressionPatch
{
    private static void Prefix(Skill __instance, Tile _target, ref float _value) => _value *= WaybackerPerks.Current.SuppressionMultiplier(__instance, _target, true);
}
[HarmonyPatch(typeof(Skill), nameof(Skill.GetExpectedSuppression))]
internal static class WaybackerExpectedSuppressionPatch
{
    private static void Postfix(Skill __instance, Tile _targetTile, Entity _overrideTargetEntity, ref float __result)
    {
        __result *= WaybackerPerks.Current.SuppressionMultiplier(__instance, _targetTile, false);
        var actor = (_overrideTargetEntity ?? _targetTile.GetActorOnTile())?.TryCast<Actor>();
        if (actor != null) __result *= WaybackerPerks.Current.IncomingSuppression(actor);
    }
}
[HarmonyPatch(typeof(Actor), nameof(Actor.ApplySuppression))]
internal static class WaybackerIncomingSuppressionPatch
{
    private static void Prefix(Actor __instance, ref float _value) => _value *= WaybackerPerks.Current.IncomingSuppression(__instance);
}
[HarmonyPatch(typeof(SkillContainer), nameof(SkillContainer.BuildPropertiesForDefense))]
internal static class WaybackerDefensePreviewPatch
{
    private static void Postfix(SkillContainer __instance, Skill _skill, EntityProperties __result) => SnipersPromiseMod.Instance.Guard(() => WaybackerPerks.Current.Defense(__instance, _skill, __result));
}
[HarmonyPatch(typeof(SkillContainer), nameof(SkillContainer.BuildPropertiesForBeingHit))]
internal static class WaybackerDefenseDamagePatch
{
    private static void Postfix(SkillContainer __instance, Skill _skill, EntityProperties __result) => SnipersPromiseMod.Instance.Guard(() => WaybackerPerks.Current.Defense(__instance, _skill, __result));
}
[HarmonyPatch(typeof(Actor), nameof(Actor.OnTurnStart))]
internal static class WaybackerTurnStartPatch { private static void Prefix(Actor __instance) => WaybackerPerks.Current.Start(__instance); }
[HarmonyPatch(typeof(Actor), nameof(Actor.OnTurnEnd))]
internal static class WaybackerTurnEndPatch { private static void Postfix(Actor __instance) => WaybackerPerks.Current.End(__instance); }
[HarmonyPatch(typeof(Actor), nameof(Actor.OnMovement))]
internal static class WaybackerMovementPatch { private static void Postfix(Actor __instance) => WaybackerPerks.Current.Moved(__instance); }
[HarmonyPatch(typeof(Actor), nameof(Actor.OnTileChanged))]
internal static class WaybackerMovedTilePatch { private static void Postfix(Actor __instance) => WaybackerPerks.Current.Moved(__instance); }
[HarmonyPatch(typeof(Actor), nameof(Actor.OnMovementFinished))]
internal static class WaybackerNearbyRefreshPatch { private static void Postfix() => SnipersPromiseMod.Instance.Guard(() => WaybackerPerks.Current.RefreshNearby()); }
[HarmonyPatch(typeof(SkillContainer), nameof(SkillContainer.OnTargetKilled))]
internal static class WaybackerKillPatch { private static void Postfix(Skill _skill) => WaybackerPerks.Current.Killed(_skill); }
[HarmonyPatch(typeof(SkillContainer), nameof(SkillContainer.OnDamageReceived))]
internal static class WaybackerHurtPatch { private static void Postfix(SkillContainer __instance, Skill _skill, DamageInfo _damageInfo) => WaybackerPerks.Current.Damaged(__instance, _skill, _damageInfo); }
[HarmonyPatch(typeof(VehicleRotationHandler), nameof(VehicleRotationHandler.DoTurn))]
internal static class WaybackerVehicleTurnPatch
{
    private static void Prefix(Actor _actor, out Direction __state) => __state = _actor.GetDirection();
    private static void Postfix(Actor _actor, Direction __state) { if (_actor.GetDirection() != __state) WaybackerPerks.Current.Turn(_actor).HasTurned = true; }
}

internal sealed class WaybackerMovementQuery
{
    [ThreadStatic] internal static WaybackerMovementQuery? Current;
    [ThreadStatic] internal static int BypassDepth;
    internal Actor Actor = null!;
    internal int Tiles;
    internal bool Backwards;
    internal WaybackerMovementQuery? Previous;
    internal static WaybackerMovementQuery Push(Actor actor, MovementAction action)
    {
        var query = new WaybackerMovementQuery { Actor = actor, Backwards = (action & MovementAction.Backwards) != 0, Previous = Current };
        Current = query; return query;
    }
}

[HarmonyPatch(typeof(MovementType), nameof(MovementType.GetTotalPathCost))]
internal static class WaybackerPathCostScope
{
    private static void Prefix(Actor _actor, out WaybackerPathState __state) => __state = WaybackerMovement.Begin(_actor);
    private static void Postfix(MovementType __instance, Il2CppSystem.Collections.Generic.List<Vector3> _path, MovementAction _action, Actor _actor, Direction _currentDir, WaybackerPathState __state, ref int __result)
    {
        WaybackerMovement.End(__state);
        if (__state.Owned) __result -= WaybackerMovement.Plan(__instance, _path, _action, _actor, _currentDir).Savings.LastOrDefault();
    }
    private static Exception? Finalizer(WaybackerPathState __state, Exception? __exception) { WaybackerMovement.End(__state); return __exception; }
}
[HarmonyPatch(typeof(MovementType), nameof(MovementType.ClipPathToCost))]
internal static class WaybackerPathClipScope
{
    private static void Prefix(MovementType __instance, Il2CppSystem.Collections.Generic.List<Vector3> _path, MovementAction _action, Actor _actor, Direction _currentDir, ref int _ap, out WaybackerPathState __state)
    {
        __state = WaybackerMovement.Begin(_actor);
        if (!__state.Owned) return;
        __state.Plan = WaybackerMovement.Plan(__instance, _path, _action, _actor, _currentDir);
        var chosen = 0;
        for (var i = 1; i < __state.Plan.NativeCosts.Length; i++)
            if (__state.Plan.NativeCosts[i] - __state.Plan.Savings[i] <= _ap) chosen = i; else break;
        if (chosen > 0) _ap = __state.Plan.NativeCosts[chosen];
    }
    private static void Postfix(int _destIndex, WaybackerPathState __state, ref int __result)
    {
        WaybackerMovement.End(__state);
        if (__state.Owned && __state.Plan != null && __state.Plan.Savings.Length > 0 && _destIndex >= 0)
            __result = Math.Max(0, __result - __state.Plan.Savings[Math.Min(_destIndex, __state.Plan.Savings.Length - 1)]);
    }
    private static Exception? Finalizer(WaybackerPathState __state, Exception? __exception) { WaybackerMovement.End(__state); return __exception; }
}
[HarmonyPatch(typeof(MovementType), nameof(MovementType.GetMovementCostForTileType))]
internal static class WaybackerTileCostPatch
{
    private static void Postfix(MovementType __instance, Actor _actor, ref int __result)
    {
        if (WaybackerMovementQuery.BypassDepth > 0) return;
        var d = WaybackerPerks.Current.Definition(_actor);
        if (d == null) return;
        var query = WaybackerMovementQuery.Current;
        var index = query != null && query.Actor.Pointer == _actor.Pointer ? query.Tiles++ : 0;
        var t = WaybackerPerks.Current.Turn(_actor);
        __result = WaybackerRules.MovementCost(d.Name, __result, __instance.GetLowestMovementCost(), index < t.EscapeTiles && t.EscapeEnds > 0, WaybackerPerks.Walker(_actor), query?.Backwards == true);
    }
}
[HarmonyPatch(typeof(Actor), nameof(Actor.GetTurningCost), new[] { typeof(Direction) })]
internal static class WaybackerTurningPatch
{
    private static void Postfix(Actor __instance, ref int __result) { if (WaybackerMovementQuery.BypassDepth == 0 && WaybackerMovementQuery.Current == null && WaybackerPerks.Current.Definition(__instance)?.Name == "Rook") __result = WaybackerRules.Discount(__result, .75f); }
}
[HarmonyPatch(typeof(Actor), nameof(Actor.GetTurningCost), new[] { typeof(Direction), typeof(Direction), typeof(MovementType), typeof(EntityProperties) })]
internal static class WaybackerPathTurningPatch
{
    private static void Postfix(ref int __result) { if (WaybackerMovementQuery.BypassDepth == 0 && WaybackerPerks.Current.Definition(WaybackerMovementQuery.Current?.Actor)?.Name == "Rook") __result = WaybackerRules.Discount(__result, .75f); }
}

[HarmonyPatch(typeof(BaseUnitLeader), nameof(BaseUnitLeader.ProcessSaveState))]
internal static class WaybackerSavePatch
{
    private static void Prefix(BaseUnitLeader __instance, SaveState _saveState, out SavePatch.SaveSwap? __state)
    {
        __state = null;
        var mod = WaybackerPerks.Current;
        if (!_saveState.IsSaving() || __instance.LeaderTemplate == null || !mod.Leaders.TryGetValue(__instance.LeaderTemplate.name, out var d)) return;
        var list = __instance.m_Perks;
        var indices = Enumerable.Range(0, list.Count).Where(i => list[i]?.name == d.Id).ToArray();
        __state = new SavePatch.SaveSwap { Leader = __instance, Indices = indices, Originals = indices.Select(i => list[i]).ToArray() };
        foreach (var i in indices) list[i] = d.Original;
    }
    private static Exception? Finalizer(BaseUnitLeader __instance, SaveState _saveState, SavePatch.SaveSwap? __state, Exception? __exception)
    {
        SnipersPromiseMod.Instance.Guard(() => {
            if (__state != null) for (var p = 0; p < __state.Indices.Length; p++) __state.Leader.m_Perks[__state.Indices[p]] = __state.Originals[p];
            if (__exception == null && _saveState.IsLoading()) WaybackerPerks.Current.Migrate(__instance);
        });
        return __exception;
    }
}
