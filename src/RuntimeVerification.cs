using System.Text.Json;
using System.Text;
using Il2CppMenace;
using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tags;
using Il2CppMenace.Tools;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using MelonLoader;
using UnityEngine;
using Math = System.Math;

namespace MenaceSnipersPromise;

internal static class RuntimeVerification
{
    internal static bool Enabled => Environment.GetCommandLineArgs().Contains("--snipers-promise-test");
    internal static string Output => Environment.GetEnvironmentVariable("MENACE_PROMISE_VERIFY_OUTPUT")
        ?? System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "SnipersPromiseVerification");

    internal static void MaintainTestMute()
    {
        if (!Enabled) return;
        // Process-local only: never change persistent game preferences or Windows volume.
        AudioListener.volume = 0f;
        AudioListener.pause = true;
    }

    internal static void Run(SnipersPromiseMod mod)
    {
        Directory.CreateDirectory(Output);
        var previous = StrategyState.s_Singleton;
        var previousConfig = StrategyConfig.Current;
        try
        {
            MaintainTestMute();
            if (AudioListener.volume != 0f || !AudioListener.pause)
                throw new Exception("Test-process audio mute failed.");
            if (!mod.EnsureTemplate() || mod.Promise == null || mod.Clover == null || mod.Original == null || mod.Rooftops == null)
                throw new Exception("Clover/perk templates were not registered.");
            var tree = mod.Clover.PerkTrees[0];
            var entries = tree.Perks.Where(p => p.Skill?.name == SnipersPromiseMod.RooftopsId).ToArray();
            if (entries.Length != 1 || entries[0].Tier != 3) throw new Exception("New perk is missing or duplicated on tier 3.");
            var existingTierThree = new[] { "perk.ambush", "perk.take_aim", "perk.minimize_silhouette", "perk.critical_hits" };
            if (existingTierThree.Any(id => !tree.Perks.Any(p => p.Tier == 3 && p.Skill?.name == id)))
                throw new Exception("An existing tier-3 choice was removed.");
            mod.EnsureTierThree();
            if (tree.Perks.Count(p => p.Skill?.name == SnipersPromiseMod.RooftopsId) != 1) throw new Exception("Repeated registration duplicated the choice.");
            StrategyConfig.InitCurrent(StrategyConfig.GetDefault());
            var state = new StrategyState { m_Seed = 20261008, Roster = new Roster() };
            StrategyState.s_Singleton = state;
            var created = Roster.CreateUnitLeader(mod.Clover, true);
            if (!created.HasPerk(mod.Promise) || created.HasPerk(mod.Original)) throw new Exception("New leader has incorrect initial perk.");
            if (created.HasPerk(mod.Rooftops)) throw new Exception("Tier-3 perk was granted for free on recruitment.");
            if (created.GetSkills().GetSkillByID(SnipersPromiseMod.PerkId) == null) throw new Exception("New perk not in skill container.");
            var title = mod.Promise.Title.GetTranslated();
            if (title != SnipersPromiseMod.PerkTitle) throw new Exception("Wrong translated title: " + title);
            if (!mod.Promise.Description.GetTranslated().Contains(SnipersPromiseMod.Quote)) throw new Exception("Lyrics altered or not displayed.");
            if (mod.Promise.Description.GetTranslated().Contains("— A Sniper's Promise")) throw new Exception("Source attribution was not removed.");
            if (mod.Rooftops.Title.GetTranslated() != SnipersPromiseMod.RooftopsTitle ||
                !mod.Rooftops.Description.GetTranslated().Contains("\"She took a steady aim.\""))
                throw new Exception("Dublin Rooftops title or adapted quote is incorrect.");
            if (mod.Promise.EventHandlers.Count != 0) throw new Exception("Inherited Zero In effects were not removed.");
            if (mod.Rooftops.EventHandlers.Count != 0) throw new Exception("Rooftops inherited native Zero In effects.");
            if (DataTemplateLoader.Get<PerkTemplate>(SnipersPromiseMod.PerkId)?.Pointer != mod.Promise.Pointer)
                throw new Exception("Custom template lookup failed.");
            if (DataTemplateLoader.Get<PerkTemplate>(SnipersPromiseMod.RooftopsId)?.Pointer != mod.Rooftops.Pointer)
                throw new Exception("Tier-3 custom lookup failed.");
            // Recreate the initial-perk state of a previously saved Clover in isolated memory.
            var initialMinimum = created.GetCurrentProperties().HitchanceMin;
            created.m_Perks[0] = mod.Original;
            created.GetSkills().Remove(mod.Promise);
            created.GetSkills().Add(mod.Original.CreateSkill(new()));
            created.GetSkills().Update();
            if (created.GetCurrentProperties().HitchanceMin != 30) throw new Exception("Old native Zero In floor not recreated.");
            mod.Migrate(created);
            mod.Migrate(created);
            if (!created.HasPerk(mod.Promise) || created.HasPerk(mod.Original) || created.GetSkills().GetSkillByID(SnipersPromiseMod.OriginalId) != null)
                throw new Exception("Existing leader migration failed.");
            if (created.GetCurrentProperties().HitchanceMin != initialMinimum) throw new Exception("Old Zero In floor still affects non-sniper weapons.");
            MelonLogger.Msg("SNIPERS_PROMISE_TEST: native creation and old-perk migration passed.");
            var initialSaveBytes = VerifySaveRoundTrip(mod, created, state, false);
            var combatRows = VerifyNativeHitChance(mod, created);
            var learnedSaveBytes = VerifySaveRoundTrip(mod, created, state, true);
            // An existing vanilla tier-3 choice must remain that choice, never become Rooftops.
            var control = Roster.CreateUnitLeader(mod.Clover, true);
            var takeAim = DataTemplateLoader.Get<PerkTemplate>("perk.take_aim");
            control.AddPerk(takeAim, false);
            mod.Migrate(control);
            if (!control.HasPerk(takeAim) || control.HasPerk(mod.Rooftops)) throw new Exception("Existing Take Aim was replaced.");
            WaybackerVerification.Run(mod, state);
            if (mod.HookErrorCount != 0) throw new Exception("A mod hook logged an exception during verification.");
            var weapons = Resources.FindObjectsOfTypeAll<WeaponTemplate>().Where(SnipersPromiseMod.IsSniperWeapon)
                .Select(w => new { id = w.name, tags = Names(w.Tags), skills = Names(w.SkillsGranted) }).ToArray();
            if (weapons.Length == 0) throw new Exception("No native sniper rifle templates found.");
            File.WriteAllText(Output + "/verification.json", JsonSerializer.Serialize(new
            {
                success = true, game = Application.version, unity = Application.unityVersion,
                audioMuted = AudioListener.volume == 0f && AudioListener.pause,
                isolatedMemoryOnly = true, nativeNewLeader = true, existingLeaderMigration = true,
                nativeSaveRoundTrip = true, savedPerk = SnipersPromiseMod.OriginalId,
                savedTierThreePerk = SnipersPromiseMod.RooftopsSaveId, initialSaveBytes, learnedSaveBytes,
                tierThree = new { id = mod.Rooftops.name, tier = 3, available = true, grantedOnRecruitment = false,
                    title = mod.Rooftops.Title.GetTranslated(), quote = SnipersPromiseMod.RooftopsQuote,
                    description = mod.Rooftops.Description.GetTranslated(),
                    iconWidth = mod.Rooftops.PerkIcon.texture.width, iconHeight = mod.Rooftops.PerkIcon.texture.height,
                    choices = tree.Perks.Where(p => p.Tier == 3).Select(p => p.Skill.name).ToArray(),
                    nativeLearning = true, existingTakeAimUnchanged = true },
                nativeHitChance = combatRows,
                customLookup = true, originalUnchanged = mod.Original.Title.GetTranslated(),
                title, description = mod.Promise.Description.GetTranslated(),
                id = mod.Promise.name, initialPerk = mod.Clover.InitialPerk.name,
                icon = new { name = mod.Promise.PerkIcon.name, width = mod.Promise.PerkIcon.texture.width, height = mod.Promise.PerkIcon.texture.height },
                sniperWeapons = weapons
            }, new JsonSerializerOptions { WriteIndented = true }));
            MelonLogger.Msg("SNIPERS_PROMISE_TEST SUCCESS: primary/sniper separation, native tier-3 learning, both save round trips, text and badges verified.");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Output + "/error.txt", ex.ToString());
            MelonLogger.Error(ex);
        }
        finally
        {
            StrategyState.s_Singleton = previous;
            StrategyConfig._Current_k__BackingField = previousConfig;
            File.WriteAllText(Output + "/finished.flag", DateTime.UtcNow.ToString("O"));
            if (Application.isBatchMode) Application.Quit();
        }
    }

    private static int VerifySaveRoundTrip(SnipersPromiseMod mod, BaseUnitLeader leader, StrategyState state, bool learned)
    {
        var memory = new Il2CppSystem.IO.MemoryStream();
        var saving = new SaveState(memory, SaveStateMode.Saving, state);
        leader.ProcessSaveState(saving);
        saving.m_Writer.Flush();
        var bytes = memory.ToArray().ToArray();
        var rawSave = Encoding.UTF8.GetString(bytes);
        if (rawSave.Contains(SnipersPromiseMod.PerkId) || rawSave.Contains(SnipersPromiseMod.RooftopsId) ||
            !rawSave.Contains(SnipersPromiseMod.OriginalId) || rawSave.Contains(SnipersPromiseMod.RooftopsSaveId) != learned)
            throw new Exception("Save contains custom IDs or incorrect native proxies.");
        if (!leader.HasPerk(mod.Promise) || leader.HasPerk(mod.Rooftops) != learned)
            throw new Exception("Saving did not restore the original perk list in memory.");
        memory.Position = 0;
        var loading = new SaveState(memory, SaveStateMode.Loading, state) { Version = saving.Version };
        var reloaded = Roster.CreateUnitLeader(mod.Clover, true);
        reloaded.ProcessSaveState(loading);
        if (!reloaded.HasPerk(mod.Promise) || reloaded.HasPerk(mod.Original) || reloaded.HasPerk(mod.Rooftops) != learned ||
            reloaded.HasPerk(mod.RooftopsSaveProxy) || reloaded.m_Perks.Count != leader.m_Perks.Count)
            throw new Exception("Native save round trip failed to restore learned/unlearned perks.");
        if (learned && reloaded.GetSkills().GetSkillByID(SnipersPromiseMod.RooftopsId) == null)
            throw new Exception("Loaded Rooftops is not present in the native skill container.");
        MelonLogger.Msg($"SNIPERS_PROMISE_TEST: native save round trip (tier 3 learned={learned}) passed, {bytes.Length} bytes.");
        return bytes.Length;
    }

    private static string[] Names<T>(Il2CppSystem.Collections.Generic.List<T> list) where T : UnityEngine.Object
    {
        var result = new string[list.Count];
        for (var i = 0; i < result.Length; i++) result[i] = list[i].name;
        return result;
    }

    private static object[] VerifyNativeHitChance(SnipersPromiseMod mod, BaseUnitLeader leader)
    {
        _ = DataTemplateLoader.GetAll<WeaponTemplate>();
        var weapons = Resources.FindObjectsOfTypeAll<WeaponTemplate>();
        File.WriteAllText(Output + "/weapon-catalog.json", JsonSerializer.Serialize(weapons.Select(w => new
        {
            id = w.name, slot = w.SlotType.ToString(), tags = Names(w.Tags), directSniper = w.HasTag(TagType.SNIPER, false),
            indirectSniper = w.HasTag(TagType.SNIPER, true), skills = Names(w.SkillsGranted)
        }), new JsonSerializerOptions { WriteIndented = true }));
        var sniper = weapons.First(SnipersPromiseMod.IsSniperWeapon);
        SkillTemplate? attackTemplate = null;
        for (var i = 0; i < sniper.SkillsGranted.Count; i++)
            if (sniper.SkillsGranted[i].IsAttack) { attackTemplate = sniper.SkillsGranted[i]; break; }
        if (attackTemplate == null) throw new Exception("No real sniper attack skill.");
        var actor = new UnitActor { m_UnitLeader = leader, m_Template = leader.GetTemplate() };
        var owner = actor.Cast<IEntityProperties>();
        var attack = attackTemplate.CreateSkill(new());
        attack.SetOwner(owner);
        attack.SetContainer(new SkillContainer(owner));
        attack.SetItem(new Item(sniper, "snipers-promise-native-test"));
        if (mod.Applies(attack, out _)) throw new Exception("Sniper receives initial perk before tier 3 is learned.");
        var from = new Tile { m_X = 0, m_Z = 0 };
        var target = new Tile { m_X = 1, m_Z = 0 };
        var properties = new EntityProperties { Accuracy = 50, AccuracyMult = 1f };
        var defense = new EntityProperties { DefenseMult = 1f };
        var rows = new List<object>();
        void Check(string label, float accuracy, int moved, float expected)
        {
            properties.Accuracy = accuracy;
            actor._TilesMovedThisTurn_k__BackingField = moved;
            var immediate = attack.GetHitchance(from, target, properties, defense, false, null, true);
            var preview = attack.GetHitchance(from, target, properties, defense, false, null, false);
            rows.Add(new { label, accuracy, moved = actor.TilesMovedThisTurn, result = immediate.FinalValue, preview = preview.FinalValue, expected });
            MelonLogger.Msg($"SNIPERS_PROMISE_NATIVE: {label}: {immediate.FinalValue}% (preview {preview.FinalValue}%).");
            if (Math.Abs(immediate.FinalValue - expected) > .001f || Math.Abs(preview.FinalValue - expected) > .001f)
                throw new Exception($"Native GetHitchance {label}: actual={immediate.FinalValue}, preview={preview.FinalValue}, expected={expected}.");
        }
        Check("unlearned sniper unchanged", 60, 0, 60);
        Check("unlearned sniper has no floor", 5, 0, 5);
        var ordinary = weapons.First(w => w.name.StartsWith("weapon.generic_assault_rifle_tier1") && w.SlotType == ItemSlot.InfantryWeapon);
        attack.SetItem(new Item(ordinary, "snipers-promise-primary-test"));
        if (!mod.Applies(attack, out _)) throw new Exception("Primary weapon not recognised.");
        Check("primary stationary +15", 50, 0, 65);
        Check("primary stationary floor 30", 5, 0, 30);
        Check("primary moved no +15", 50, 1, 50);
        Check("primary moved no floor", 5, 1, 5);
        Check("primary next turn bonus returns", 50, 0, 65);
        Check("primary next turn floor returns", 5, 0, 30);
        Check("primary maximum 100", 95, 0, 100);
        var special = weapons.First(w => w.SlotType == ItemSlot.InfantrySpecial && !SnipersPromiseMod.IsSniperWeapon(w));
        attack.SetItem(new Item(special, "snipers-promise-special-test"));
        Check("special weapon unchanged", 5, 0, 5);
        // Exercise native learning and tier lookup rather than adding directly to m_Perks.
        var tree = mod.Clover!.PerkTrees[0];
        leader.AddPerk(tree.Perks.First(p => p.Tier == 1).Skill, false);
        leader.AddPerk(tree.Perks.First(p => p.Tier == 2).Skill, false);
        leader.AddPerk(mod.Rooftops, false);
        if (!leader.HasPerk(mod.Rooftops) || !leader.HasPerkOfTier(3) ||
            leader.GetSkills().GetSkillByID(SnipersPromiseMod.RooftopsId) == null)
            throw new Exception("Native tier-3 learning failed.");
        attack.SetItem(new Item(sniper, "dublin-rooftops-native-test"));
        if (!mod.Applies(attack, out _)) throw new Exception("Learned sniper effect not recognised.");
        Check("sniper stationary exclusive +10", 60, 0, 70);
        Check("sniper stationary floor 50", 5, 0, 50);
        Check("sniper moved no +10", 60, 1, 60);
        Check("sniper moved no floor", 5, 1, 5);
        Check("sniper next turn bonus returns", 60, 0, 70);
        Check("sniper next turn floor returns", 5, 0, 50);
        Check("sniper maximum 100", 95, 0, 100);
        foreach (var rifle in weapons.Where(SnipersPromiseMod.IsSniperWeapon))
        {
            attack.SetItem(new Item(rifle, "dublin-rooftops-all-rifles"));
            Check("native rifle: " + rifle.name, 60, 0, 70);
        }
        attack.SetItem(new Item(ordinary, "dublin-rooftops-primary-control"));
        Check("learned tier 3 primary still +15", 50, 0, 65);
        attack.SetItem(new Item(special, "dublin-rooftops-special-control"));
        Check("learned tier 3 special still unchanged", 5, 0, 5);
        attack.SetItem(new Item(sniper, "snipers-promise-other-leader-test"));
        var otherTemplate = Resources.FindObjectsOfTypeAll<UnitLeaderTemplate>()
            .First(t => t.name.StartsWith("squad_leader.") && !t.name.Contains(".msl_") && t.InitialPerk != null);
        actor.m_UnitLeader = Roster.CreateUnitLeader(otherTemplate, true);
        Check("other leader unchanged", 50, 0, 50);
        File.WriteAllText(Output + "/native-hitchance.json", JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        MelonLogger.Msg($"SNIPERS_PROMISE_TEST: real native GetHitchance + preview passed all {rows.Count} scenarios.");
        return rows.ToArray();
    }
}
