using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tags;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tools;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using Object = UnityEngine.Object;
using Path = System.IO.Path;

[assembly: MelonInfo(typeof(MenaceSnipersPromise.SnipersPromiseMod), "Waybackers: Personal Perks", "0.3.0", "Local MENACE mods")]
[assembly: MelonGame("Overhype Studios", "Menace")]
[assembly: HarmonyDontPatchAll]

namespace MenaceSnipersPromise;

public sealed class SnipersPromiseMod : MelonMod
{
    public const string CloverId = "squad_leader.msl_08350dd124ff41088d09291cc9305dd2";
    public const string PerkId = "perk.clover_snipers_promise";
    public const string OriginalId = "perk.zero_in_starting";
    public const string RooftopsId = "perk.clover_dublin_rooftops";
    // Clover cannot normally learn Steady Gun. Store this vanilla ID as the reversible tier-3 proxy.
    public const string RooftopsSaveId = "perk.steady_gun";
    public const string PerkTitle = "Sniper's Promise";
    public const string RooftopsTitle = "Dublin Rooftops";
    public const string RooftopsQuote = "She took a steady aim.";
    public const string Quote = "The night was icy cold I stood along\nI was waiting for an army foot patrol\nAnd when at last they came into my site\nI squeezed the trigger of my armalite";
    public const string PerkDescription = "\"" + Quote + "\"\n\n" +
        "• Clover's squad gains +15 percentage points to hit chance with primary weapons, excluding sniper rifles, until it moves during its turn.\n" +
        "• While the squad has not moved this turn, primary weapon hit chance cannot fall below 30%. Sniper rifles and special weapons are excluded.\n" +
        "• After movement, both the +15 bonus and the 30% minimum are lost until the squad's next turn.";
    public const string RooftopsDescription = "\"" + RooftopsQuote + "\"\n\n" +
        "• Clover's squad gains +10 percentage points to hit chance with sniper rifles until it moves during its turn.\n" +
        "• While the squad has not moved this turn, sniper rifle hit chance cannot fall below 50%.\n" +
        "• After movement, both effects are lost until the squad's next turn.\n" +
        "• Sniper rifles do not receive the Sniper's Promise bonus.";

    internal static SnipersPromiseMod Instance = null!;
    internal PerkTemplate? Promise;
    internal PerkTemplate? Original;
    internal PerkTemplate? Rooftops;
    internal PerkTemplate? RooftopsSaveProxy;
    internal UnitLeaderTemplate? Clover;
    internal readonly WaybackerPerks Waybackers = new();
    private float nextScan;
    private bool failed;
    private bool missingLogged;
    private bool verificationDone;
    private readonly HashSet<string> errors = new();
    internal int HookErrorCount => errors.Count;

    public override void OnInitializeMelon()
    {
        Instance = this;
        try
        {
            RuntimeVerification.MaintainTestMute();
            HarmonyInstance.PatchAll(typeof(SnipersPromiseMod).Assembly);
            MelonLogger.Msg("Clover: primary weapons +15/30; learned sniper tier 3 +10/50. Exclusive effects; both lost after movement.");
            if (RuntimeVerification.Enabled && Application.isBatchMode)
            {
                if (UnityEngine.InputSystem.Keyboard.current == null)
                    UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
                if (UnityEngine.InputSystem.Mouse.current == null)
                    UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
            }
        }
        catch (Exception ex)
        {
            failed = true;
            HarmonyInstance.UnpatchSelf();
            MelonLogger.Error(ex);
        }
    }

    public override void OnUpdate()
    {
        RuntimeVerification.MaintainTestMute();
        if (failed || Time.realtimeSinceStartup < nextScan) return;
        nextScan = Time.realtimeSinceStartup + 1f;
        Guard(() =>
        {
            if (!EnsureTemplate()) return;
            Waybackers.Ensure(this);
            Waybackers.RefreshNearby();
            var roster = StrategyState.Get()?.Roster;
            if (roster != null)
            {
                MigrateList(roster.m_HiredLeaders);
                MigrateList(roster.m_DismissedLeaders);
                MigrateList(roster.m_UnburiedLeaders);
                MigrateList(roster.m_BuriedLeaders);
            }
        });
        if (RuntimeVerification.Enabled && !verificationDone && Time.realtimeSinceStartup > 35f)
        {
            verificationDone = true;
            RuntimeVerification.Run(this);
        }
    }

    internal void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            if (errors.Add(ex.ToString())) MelonLogger.Error(ex);
        }
    }

    internal bool EnsureTemplate()
    {
        if (Promise != null && Rooftops != null && Clover != null)
        {
            Clover.InitialPerk = Promise;
            EnsureTierThree();
            return true;
        }
        Clover = Resources.FindObjectsOfTypeAll<UnitLeaderTemplate>().FirstOrDefault(t => t.name == CloverId);
        if (Clover == null)
        {
            if (!missingLogged && Time.realtimeSinceStartup > 45f)
            {
                missingLogged = true;
                MelonLogger.Warning("Clover not found. Enable the Waybackers character pack; no other leader will be modified.");
            }
            return false;
        }
        Original = DataTemplateLoader.Get<PerkTemplate>(OriginalId);
        if (Original == null) throw new InvalidOperationException("Zero In template not loaded.");
        RooftopsSaveProxy = DataTemplateLoader.Get<PerkTemplate>(RooftopsSaveId);
        if (RooftopsSaveProxy == null) throw new InvalidOperationException("Steady Gun save proxy not loaded.");

        Promise = CreatePerk(PerkId, PerkTitle, PerkDescription,
            "Primary weapons except sniper rifles: +15 hit chance and a 30% minimum until movement; both return next turn.",
            "snipers-promise.png");
        Rooftops = CreatePerk(RooftopsId, RooftopsTitle, RooftopsDescription,
            "Sniper rifles: +10 hit chance and a 50% minimum until movement; both return next turn. Does not stack with Sniper's Promise.",
            "dublin-rooftops.png");
        Clover.InitialPerk = Promise;
        EnsureTierThree();
        MelonLogger.Msg($"Registered {PerkId} and {RooftopsId}; Clover's tier 3 extended. Original native perks unchanged.");
        return true;
    }

    internal PerkTemplate CreatePerk(string id, string title, string description, string shortDescription, string iconFile, PerkTemplate? source = null, bool keepHandlers = false)
    {
        // Keep native passive-perk defaults, replacing all effects with our hit-chance hook.
        var promise = Object.Instantiate(source ?? Original ?? throw new InvalidOperationException("Initial perk template missing."));
        promise.name = id;
        promise.m_ID = id;
        promise.hideFlags = HideFlags.DontUnloadUnusedAsset;
        if (!keepHandlers) promise.EventHandlers = new Il2CppSystem.Collections.Generic.List<SkillEventHandlerTemplate>();
        promise.Title = new LocalizedLine(LocaCategory.Skills, "Title") { m_DefaultTranslation = title };
        promise.Description = new LocalizedMultiLine(LocaCategory.Skills, "Description", false) { m_DefaultTranslation = description };
        promise.ShortDescription = new LocalizedMultiLine(LocaCategory.Skills, "ShortDescription", false)
        {
            m_DefaultTranslation = shortDescription
        };
        promise.Title.SetId(id);
        promise.Description.SetId(id);
        promise.ShortDescription.SetId(id);
        var path = Path.Combine(MelonEnvironment.GameRootDirectory, "Mods", "MenaceSnipersPromise", iconFile);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
            throw new InvalidDataException("Cannot load perk icon: " + iconFile);
        texture.name = id + "_icon";
        texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
        texture.filterMode = FilterMode.Bilinear;
        var icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
        icon.name = id;
        icon.hideFlags = HideFlags.DontUnloadUnusedAsset;
        promise.PerkIcon = icon;
        promise.Icon = icon;
        promise.IconDisabled = icon;
        RegisterTemplate(promise);
        return promise;
    }

    internal void EnsureTierThree()
    {
        if (Clover?.PerkTrees == null || Clover.PerkTrees.Length == 0 || Rooftops == null)
            throw new InvalidOperationException("Clover's perk tree is unavailable.");
        // Waybackers gives Clover a private tree. Never append to a shared native leader tree.
        var tree = Clover.PerkTrees[0];
        if (tree == null || tree.name != "perk_tree.msl_08350dd124ff41088d09291cc9305dd2")
            throw new InvalidOperationException("Unexpected shared Clover perk tree.");
        var old = tree.Perks;
        for (var i = 0; i < old.Length; i++)
        {
            if (old[i]?.Skill?.name == RooftopsSaveId)
                throw new InvalidOperationException("Steady Gun is already offered to Clover; cannot safely reserve it for save compatibility.");
            if (old[i]?.Skill?.name == RooftopsId) return;
        }
        var updated = new Il2CppReferenceArray<Perk>(old.Length + 1);
        for (var i = 0; i < old.Length; i++) updated[i] = old[i];
        updated[old.Length] = new Perk { Skill = Rooftops, Tier = 3 };
        tree.Perks = updated;
    }

    private static void RegisterTemplate(PerkTemplate perk)
    {
        var loader = DataTemplateLoader.GetSingleton();
        var type = Il2CppType.Of<PerkTemplate>();
        // Force the normal native caches to exist before adding one runtime template.
        _ = DataTemplateLoader.GetAll<PerkTemplate>();
        var map = loader.m_TemplateMaps[type];
        map[perk.name] = perk;
        var old = loader.m_TemplateArrays[type];
        // Preserve the native PerkTemplate[] array class: GetAll<PerkTemplate> casts to that type.
        // A DataTemplate[] works through the cache field but fails the next generic lookup.
        var updated = new Il2CppReferenceArray<PerkTemplate>(old.Length + 1);
        for (var i = 0; i < old.Length; i++) updated[i] = old[i].Cast<PerkTemplate>();
        updated[old.Length] = perk;
        loader.m_TemplateArrays[type] = new Il2CppReferenceArray<DataTemplate>(updated.Pointer);
    }

    internal static bool IsClover(BaseUnitLeader? leader) => leader != null && leader.LeaderTemplate?.name == CloverId;

    private void MigrateList(Il2CppSystem.Collections.Generic.List<BaseUnitLeader>? leaders)
    {
        if (leaders == null) return;
        for (var i = 0; i < leaders.Count; i++) Migrate(leaders[i]);
    }

    internal void Migrate(BaseUnitLeader? leader)
    {
        Waybackers.Migrate(leader);
        if (Promise == null || Original == null || Rooftops == null || RooftopsSaveProxy == null || !IsClover(leader) || leader!.m_Perks == null) return;
        var perks = leader.m_Perks;
        bool changed = false;
        for (var i = 0; i < perks.Count; i++)
        {
            if (perks[i]?.name == OriginalId) { perks[i] = Promise; changed = true; }
            else if (perks[i]?.name == RooftopsSaveId) { perks[i] = Rooftops; changed = true; }
        }
        var skills = leader.GetSkills();
        if (skills != null)
        {
            var skillsChanged = false;
            if (leader.HasPerk(Promise))
            {
                skillsChanged |= skills.Remove(Original);
                if (skills.GetSkillByID(PerkId) == null) { skills.Add(Promise.CreateSkill(new())); skillsChanged = true; }
            }
            if (leader.HasPerk(Rooftops))
            {
                skillsChanged |= skills.Remove(RooftopsSaveProxy);
                if (skills.GetSkillByID(RooftopsId) == null) { skills.Add(Rooftops.CreateSkill(new())); skillsChanged = true; }
            }
            if (skillsChanged) skills.Update();
        }
        if (changed) MelonLogger.Msg("Restored Clover's custom perks (promotion points and other perks preserved).");
    }

    internal bool Applies(Skill skill, out Actor? actor)
    {
        actor = skill.GetActor();
        var unit = actor?.TryCast<UnitActor>();
        var leader = unit?.GetLeader();
        if (!IsClover(leader) || Promise == null || Rooftops == null || !skill.IsAttack()) return false;
        var weapon = skill.GetItem()?.GetTemplate()?.TryCast<WeaponTemplate>();
        if (weapon == null || (weapon.SlotType != ItemSlot.InfantryWeapon && weapon.SlotType != ItemSlot.InfantrySpecial)) return false;
        if (IsSniperWeapon(weapon)) return leader!.HasPerk(Rooftops);
        return weapon.SlotType == ItemSlot.InfantryWeapon && leader!.HasPerk(Promise);
    }

    internal static bool IsSniperWeapon(WeaponTemplate weapon)
    {
        if (weapon.HasTag(TagType.SNIPER, false)) return true;
        // Vanilla infantry sniper rifles have no SNIPER item tag. All four grant
        // the dedicated aimed_shot skill; DMRs and anti-materiel rifles grant others.
        var granted = weapon.SkillsGranted;
        if (granted == null) return false;
        for (var i = 0; i < granted.Count; i++)
            if (granted[i]?.name == "active.aimed_shot") return true;
        return false;
    }

    internal void ApplyHitChance(Skill skill, ref HitChance result)
    {
        if (!Applies(skill, out var actor)) return;
        var leader = actor!.TryCast<UnitActor>()!.GetLeader();
        var weapon = skill.GetItem().GetTemplate().Cast<WeaponTemplate>();
        result.FinalValue = PromiseRules.Apply(result.FinalValue, weapon.SlotType == ItemSlot.InfantryWeapon, IsSniperWeapon(weapon), actor.TilesMovedThisTurn,
            leader.HasPerk(Promise), leader.HasPerk(Rooftops), result.AlwaysHits);
    }
}

[HarmonyPatch(typeof(Roster), nameof(Roster.CreateUnitLeader))]
internal static class NewLeaderPatch
{
    private static void Prefix() => SnipersPromiseMod.Instance.Guard(() => { if (SnipersPromiseMod.Instance.EnsureTemplate()) SnipersPromiseMod.Instance.Waybackers.Ensure(SnipersPromiseMod.Instance); });
    private static void Postfix(BaseUnitLeader __result) => SnipersPromiseMod.Instance.Guard(() => SnipersPromiseMod.Instance.Migrate(__result));
}

[HarmonyPatch(typeof(Skill), nameof(Skill.GetHitchance))]
internal static class HitChancePatch
{
    // The flat percentage-point bonus is applied after the game's cover/range/defense calculation.
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Skill __instance, ref HitChance __result)
    {
        try { SnipersPromiseMod.Instance.ApplyHitChance(__instance, ref __result); }
        catch (Exception ex) { SnipersPromiseMod.Instance.Guard(() => throw ex); }
    }
}

[HarmonyPatch(typeof(BaseUnitLeader), nameof(BaseUnitLeader.ProcessSaveState))]
internal static class SavePatch
{
    internal sealed class SaveSwap
    {
        internal BaseUnitLeader Leader = null!;
        internal int[] Indices = Array.Empty<int>();
        internal PerkTemplate[] Originals = Array.Empty<PerkTemplate>();
    }

    private static void Prefix(BaseUnitLeader __instance, SaveState _saveState, out SaveSwap? __state)
    {
        __state = null;
        var mod = SnipersPromiseMod.Instance;
        if (!_saveState.IsSaving() || !SnipersPromiseMod.IsClover(__instance) || mod.Original == null || mod.RooftopsSaveProxy == null) return;
        var list = __instance.m_Perks;
        var indices = new List<int>();
        for (var i = 0; i < list.Count; i++)
            if (list[i]?.name == SnipersPromiseMod.PerkId || list[i]?.name == SnipersPromiseMod.RooftopsId) indices.Add(i);
        __state = new SaveSwap { Leader = __instance, Indices = indices.ToArray(), Originals = indices.Select(i => list[i]).ToArray() };
        foreach (var i in __state.Indices)
            list[i] = list[i].name == SnipersPromiseMod.PerkId ? mod.Original : mod.RooftopsSaveProxy;
    }

    private static Exception? Finalizer(BaseUnitLeader __instance, SaveState _saveState, SaveSwap? __state, Exception? __exception)
    {
        var mod = SnipersPromiseMod.Instance;
        mod.Guard(() =>
        {
            if (__state != null)
                for (var p = 0; p < __state.Indices.Length; p++)
                    if (__state.Indices[p] < __state.Leader.m_Perks.Count)
                        __state.Leader.m_Perks[__state.Indices[p]] = __state.Originals[p];
            if (__exception == null && _saveState.IsLoading()) mod.Migrate(__instance);
        });
        return __exception;
    }
}
