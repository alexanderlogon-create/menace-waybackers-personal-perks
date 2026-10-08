using System.Text;
using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime;
using Il2CppMenace;
using Il2CppMenace.Items;
using Il2CppMenace.Strategy;
using Il2CppMenace.States;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.AI;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tools;
using UnityEngine;
using Math = System.Math;

namespace MenaceSnipersPromise;

internal static class WaybackerVerification
{
    internal static void Run(SnipersPromiseMod mod, StrategyState state)
    {
        var managerBefore = TacticalManager.s_Singleton;
        var randomBefore = TacticalManager.s_Random;
        var rows = new List<object>();
        var templates = new List<object>();
        var created = new Dictionary<string, BaseUnitLeader>();
        void Check(string name, float actual, float expected)
        {
            rows.Add(new { name, actual, expected });
            if (Math.Abs(actual - expected) > .001f) throw new Exception("Waybackers " + name + ": actual=" + actual + ", expected=" + expected);
        }
        try
        {
            if (!mod.Waybackers.Ensure(mod) || mod.Waybackers.Leaders.Count != 21) throw new Exception("Expected all 21 additional Waybackers.");
            foreach (var d in mod.Waybackers.Leaders.Values.OrderBy(d => d.Name))
            {
                var leader = Roster.CreateUnitLeader(d.Leader, true);
                created.Add(d.Name, leader);
                if (!leader.HasPerk(d.Perk) || leader.HasPerk(d.Original) || leader.GetSkills().GetSkillByID(d.Id) == null)
                    throw new Exception(d.Name + ": native initial perk assignment failed.");
                if (DataTemplateLoader.Get<PerkTemplate>(d.Id)?.Pointer != d.Perk.Pointer ||
                    d.Perk.Title.GetTranslated() != d.Title || !d.Perk.Description.GetTranslated().Contains(d.Quote))
                    throw new Exception(d.Name + ": template registration/text failed.");
                if (d.Perk.PerkIcon.texture.width < 512 || d.Perk.PerkIcon.texture.GetPixel(0, 0).a > .01f)
                    throw new Exception(d.Name + ": invalid badge size or transparency.");
                if (d.Name == "Sigrid")
                {
                    if (d.Perk.EventHandlers.Count != d.Original.EventHandlers.Count || d.Perk.EventHandlers.Count == 0 ||
                        d.Perk.IsActive != d.Original.IsActive || d.Perk.IsLimitedUses != d.Original.IsLimitedUses || d.Perk.Uses != d.Original.Uses)
                        throw new Exception("Sigrid's native First Aid behavior or use limits changed.");
                    for (var i = 0; i < d.Original.EventHandlers.Count; i++)
                        if (d.Perk.EventHandlers[i].Pointer != d.Original.EventHandlers[i].Pointer) throw new Exception("First Aid native handler was replaced.");
                }
                else if (d.Perk.EventHandlers.Count != 0) throw new Exception(d.Name + ": inherited starting effects remain.");
                // Simulate an old save, then use the real migration routine twice.
                leader.m_Perks[0] = d.Original;
                leader.GetSkills().Remove(d.Perk);
                leader.GetSkills().Add(d.Original.CreateSkill(new()));
                leader.GetSkills().Update();
                mod.Migrate(leader); mod.Migrate(leader);
                if (!leader.HasPerk(d.Perk) || leader.GetSkills().GetSkillByID(d.OriginalId) != null) throw new Exception(d.Name + ": existing leader migration failed.");
                var stream = new Il2CppSystem.IO.MemoryStream();
                var saving = new SaveState(stream, SaveStateMode.Saving, state);
                leader.ProcessSaveState(saving); saving.m_Writer.Flush();
                var bytes = stream.ToArray().ToArray();
                var raw = Encoding.UTF8.GetString(bytes);
                if (raw.Contains(d.Id) || !raw.Contains(d.OriginalId) || !leader.HasPerk(d.Perk)) throw new Exception(d.Name + ": reversible save proxy failed.");
                stream.Position = 0;
                var loaded = Roster.CreateUnitLeader(d.Leader, true);
                loaded.ProcessSaveState(new SaveState(stream, SaveStateMode.Loading, state) { Version = saving.Version });
                if (!loaded.HasPerk(d.Perk) || loaded.HasPerk(d.Original) || loaded.m_Perks.Count != leader.m_Perks.Count)
                    throw new Exception(d.Name + ": native save round trip failed.");
                templates.Add(new { name = d.Name, id = d.Id, title = d.Title, quote = d.Quote,
                    description = d.Perk.Description.GetTranslated(), saveBytes = bytes.Length,
                    iconWidth = d.Perk.PerkIcon.texture.width, iconHeight = d.Perk.PerkIcon.texture.height,
                    nativeMigration = true, nativeSaveRoundTrip = true, active = d.Perk.IsActive,
                    uses = d.Perk.Uses, handlers = d.Perk.EventHandlers.Count });
            }
            // Allocate an isolated native fixture without running the campaign-dependent battle constructor.
            var tactical = new TacticalManager(IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<TacticalManager>.NativeClassPtr));
            var factions = new Il2CppReferenceArray<BaseFaction>(10);
            var factionTemplate = Resources.FindObjectsOfTypeAll<FactionTemplate>().First();
            for (var i = 0; i < 10; i++)
            {
                BaseFaction faction = i == 1
                    ? new PlayerFaction(IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<PlayerFaction>.NativeClassPtr))
                    : new AIFaction(IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<AIFaction>.NativeClassPtr));
                faction.m_FactionIndex = i; faction.m_FactionType = (FactionType)i; faction.m_Template = factionTemplate;
                faction.m_Actors = new Il2CppSystem.Collections.Generic.List<Actor>(); faction.m_DeadActors = new Il2CppSystem.Collections.Generic.List<Actor>();
                factions[i] = faction;
            }
            tactical.m_Factions = factions;
            TacticalManager.s_Singleton = tactical;
            var map = new Map(IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<Map>.NativeClassPtr));
            tactical.m_Map = map;
            map.Resize(16, 16);
            var actors = new Dictionary<string, UnitActor>();
            var entityTemplates = Resources.FindObjectsOfTypeAll<EntityTemplate>();
            var vehicle = entityTemplates.First(t => t.ActorType.ToString().Contains("Vehicle"));
            var walker = entityTemplates.First(t => t.name.Contains("walker") && t.ActorType == vehicle.ActorType);
            Tile TileAt(int x, int z = 0) => new() { m_X = x + 2, m_Z = z + 2, m_WorldPos = new Vector3(x + 2, 0, z + 2) };
            void Place(UnitActor actor, Tile tile)
            {
                actor.m_CachedTags ??= new Il2CppSystem.Collections.Generic.HashSet<Il2CppMenace.Tags.TagTemplate>();
                actor.m_CachedTagTypes ??= new Il2CppSystem.Collections.Generic.HashSet<Il2CppMenace.Tags.TagType>();
                actor.m_Elements ??= new Il2CppSystem.Collections.Generic.List<Element>();
                actor.m_OriginalElementCount = 1;
                actor.m_Segments = new Il2CppSystem.Collections.Generic.List<EntitySegment>();
                var segment = new EntitySegment(actor, tile);
                actor.m_Segments.Add(segment); tile.m_EntitySegment = segment;
            }
            foreach (var d in mod.Waybackers.Leaders.Values)
            {
                var leader = created[d.Name];
                var actor = new UnitActor { m_UnitLeader = leader, m_Template = d.Pilot ? (d.Name == "Viper" ? walker : vehicle) : leader.GetTemplate(),
                    m_FactionID = 1, m_BaseFactionID = 1, m_IsAlive = true, m_Hitpoints = 100, m_HitpointsMax = 100, m_Forward = Vector3.forward };
                Place(actor, TileAt(0));
                actor.GetSkills().SetOwner(actor.Cast<IEntityProperties>());
                actor.GetSkills().Update();
                actors[d.Name] = actor;
                factions[1].m_Actors.Add(actor);
                if (actor.GetTile()?.m_X != 2) throw new Exception("Native entity segment tile fixture failed.");
                if (d.Pilot && !actor.IsVehicle()) throw new Exception(d.Name + ": pilot fixture is not a vehicle.");
            }
            var enemyLeader = Roster.CreateUnitLeader(mod.Clover, true);
            var enemy = new UnitActor { m_UnitLeader = enemyLeader, m_Template = enemyLeader.GetTemplate(),
                m_FactionID = 6, m_BaseFactionID = 6, m_IsAlive = true, m_Hitpoints = 100, m_HitpointsMax = 100 };
            Place(enemy, TileAt(1));
            factions[6].m_Actors.Add(enemy);
            Check("native pirate faction", (float)enemy.GetFaction(), (float)FactionType.Pirates);
            var weapons = Resources.FindObjectsOfTypeAll<WeaponTemplate>();
            var rifle = weapons.First(w => w.name.StartsWith("weapon.generic_assault_rifle_tier1") && w.SlotType == ItemSlot.InfantryWeapon);
            var sniper = weapons.First(SnipersPromiseMod.IsSniperWeapon);
            var machineGun = weapons.First(WaybackerPerks.MachineGun);
            var flamer = weapons.First(w => w.name == "specialweapon.flamethrower");
            var gun = weapons.First(w => w.SlotType.ToString().Contains("Turret") && w.SkillsGranted.Count > 0 && w.SkillsGranted[0].IsAttack);
            Skill Attack(string name, WeaponTemplate weapon)
            {
                var granted = weapon.SkillsGranted;
                SkillTemplate? template = null;
                for (var i = 0; i < granted.Count; i++) if (granted[i].IsAttack) { template = granted[i]; break; }
                if (template == null) throw new Exception("No native attack for " + weapon.name);
                var skill = template.CreateSkill(new());
                var actor = actors[name];
                skill.SetOwner(actor.Cast<IEntityProperties>()); skill.SetContainer(actor.GetSkills());
                skill.SetItem(new Item(weapon, "waybackers-native-test"));
                return skill;
            }
            var aim = new EntityProperties { Accuracy = 50, AccuracyMult = 1 };
            var defense = new EntityProperties { DefenseMult = 1 };
            void Hit(string label, Skill attack, float expected)
            {
                MelonLoader.MelonLogger.Msg("WAYBACKERS_NATIVE: " + label);
                var actor = attack.GetActor();
                var immediate = attack.GetHitchance(actor.GetTile(), enemy.GetTile(), aim, defense, false, enemy, true);
                var preview = attack.GetHitchance(actor.GetTile(), enemy.GetTile(), aim, defense, false, enemy, false);
                Check(label + " immediate", immediate.FinalValue, expected);
                Check(label + " preview", preview.FinalValue, expected);
            }
            Hit("Ash pirates", Attack("Ash", rifle), 65);
            enemy.m_FactionID = 9; Hit("Ash nonpirates", Attack("Ash", rifle), 50); enemy.m_FactionID = 6;
            var camila = Attack("Camila", sniper);
            Hit("Camila first sniper", camila, 60);
            mod.Waybackers.BeginUse(camila, enemy.GetTile());
            camila.GetContainer().OnSkillUsed(camila, enemy.GetTile());
            Hit("Camila subsequent sniper", camila, 50);
            mod.Waybackers.Start(actors["Camila"]); Hit("Camila next turn", camila, 60);
            actors["Camila"]._TilesMovedThisTurn_k__BackingField = 2; Hit("Camila movement allowed", camila, 60);
            var markov = Attack("Markov", machineGun);
            Hit("Markov stationary", markov, 60);
            actors["Markov"]._TilesMovedThisTurn_k__BackingField = 1; Hit("Markov moved", markov, 50);
            Hit("Markov rifle excluded", Attack("Markov", rifle), 50);
            Hit("Marcus close", Attack("Marcus", rifle), 60);
            Place(enemy, TileAt(4)); Hit("Marcus far", Attack("Marcus", rifle), 50); Place(enemy, TileAt(1));
            Hit("Luo near allies", Attack("Luo", rifle), 55);
            foreach (var a in actors.Values.Where(a => a.Pointer != actors["Luo"].Pointer)) Place(a, TileAt(8));
            Hit("Luo isolated", Attack("Luo", rifle), 50);
            foreach (var a in actors.Values) Place(a, TileAt(0));
            actors["Caleb"].GetTile().SetCover((CoverType)1);
            Hit("Caleb in cover", Attack("Caleb", rifle), 60);
            actors["Caleb"]._TilesMovedThisTurn_k__BackingField = 1; Hit("Caleb cover after move", Attack("Caleb", rifle), 50);
            actors["Caleb"]._TilesMovedThisTurn_k__BackingField = 0;
            Check("Caleb suppression protection", mod.Waybackers.IncomingSuppression(actors["Caleb"]), .8f);
            var track = Attack("Track", gun);
            Hit("Track stationary gun", track, 60);
            actors["Track"]._TilesMovedThisTurn_k__BackingField = 1; Hit("Track moved gun", track, 50);
            var viper = Attack("Viper", gun);
            actors["Viper"]._TilesMovedThisTurn_k__BackingField = 1;
            Hit("Viper first moved gun", viper, 60);
            mod.Waybackers.BeginUse(viper, enemy.GetTile()); viper.GetContainer().OnSkillUsed(viper, enemy.GetTile());
            Hit("Viper consumed moved gun", viper, 50);
            var rook = Attack("Rook", gun);
            mod.Waybackers.Turn(actors["Rook"]).HasTurned = true; Hit("Rook first after turn", rook, 60);
            mod.Waybackers.BeginUse(rook, enemy.GetTile()); rook.GetContainer().OnSkillUsed(rook, enemy.GetTile());
            Hit("Rook turn bonus consumed", rook, 50);
            // Native property updates: compare actual totals to unmodified attribute-derived bases.
            foreach (var name in new[] { "Bulk", "Creed", "Crow", "Lynx" })
            {
                var leader = created[name];
                var definition = mod.Waybackers.Leaders.Values.First(d => d.Name == name);
                var baseline = new EntityProperties { HitpointsPerElement = 100, HitpointsPerElementMult = 1, Discipline = 50, Vision = 4, Concealment = 2, Detection = 2, DeployCostMult = 1 };
                var current = baseline.GetClone();
                leader.GetSkills().GetSkillByID(definition.Id).Cast<Skill>().OnUpdate(current);
                switch (name)
                {
                    case "Bulk": Check("Bulk max HP", current.GetHitpointsPerElement(), (int)(baseline.GetHitpointsPerElement() * 1.1f)); break;
                    case "Creed": Check("Creed discipline", current.Discipline, baseline.Discipline + 10); break;
                    case "Crow": Check("Crow vision", current.Vision, baseline.Vision + 1); Check("Crow deploy cost", current.DeployCostMult, baseline.DeployCostMult * .9f); break;
                    case "Lynx": Check("Lynx camouflage", current.Concealment, baseline.Concealment + 1); Check("Lynx detection", current.Detection, baseline.Detection + 1); break;
                }
            }
            void Cost(string name, Skill skill, float mult)
            {
                var d = mod.Waybackers.Leaders.Values.First(d => d.Name == name);
                var leader = created[name];
                var index = leader.m_Perks.IndexOf(d.Perk);
                leader.m_Perks.RemoveAt(index);
                var originalCost = skill.GetActionPointCost();
                leader.m_Perks.Insert(index, d.Perk);
                Check(name + " native AP cost", skill.GetActionPointCost(), WaybackerRules.Discount(originalCost, mult));
            }
            Cost("Creed", Attack("Creed", rifle), .9f);
            var marcus = Attack("Marcus", rifle); Hit("Marcus cost aim", marcus, 60); Cost("Marcus", marcus, .9f);
            var sigrid = mod.Waybackers.Leaders.Values.First(d => d.Name == "Sigrid");
            var aid = actors["Sigrid"].GetSkills().GetSkillByID(sigrid.Id).Cast<Skill>();
            Cost("Sigrid", aid, .75f);
            actors["Sigrid"].GetSkills().OnSkillUsed(aid, actors["Creed"].GetTile());
            Check("medical suppression benefit", mod.Waybackers.IncomingSuppression(actors["Creed"]), .8f);
            mod.Waybackers.Start(actors["Creed"]); mod.Waybackers.End(actors["Creed"]);
            Check("medical next turn expiry", mod.Waybackers.IncomingSuppression(actors["Creed"]), 1);
            Check("Don protects nearby ally", mod.Waybackers.Protected(actors["Creed"]) ? 1 : 0, 1);
            Check("Don does not protect self", mod.Waybackers.Protected(actors["Don"]) ? 1 : 0, 0);
            Place(actors["Don"], TileAt(5)); Check("Don aura range", mod.Waybackers.Protected(actors["Creed"]) ? 1 : 0, 0); Place(actors["Don"], TileAt(0));
            var talia = Attack("Talia", rifle);
            mod.Waybackers.BeginUse(talia, enemy.GetTile()); talia.GetContainer().OnSkillUsed(talia, enemy.GetTile());
            Check("Talia grants two tiles", mod.Waybackers.Turn(actors["Talia"]).EscapeTiles, 2);
            actors["Talia"]._TilesMovedThisTurn_k__BackingField = 1; mod.Waybackers.Moved(actors["Talia"]);
            actors["Talia"]._TilesMovedThisTurn_k__BackingField = 2; mod.Waybackers.Moved(actors["Talia"]);
            Check("Talia tiles consumed", mod.Waybackers.Turn(actors["Talia"]).EscapeTiles, 0);
            var nullAttack = Attack("Null", rifle);
            actors["Null"].m_ActionPoints = 30;
            nullAttack.GetContainer().OnTargetKilled(nullAttack, enemy);
            Check("Null first kill refund", actors["Null"].GetActionPoints(), 50);
            nullAttack.GetContainer().OnTargetKilled(nullAttack, enemy);
            Check("Null once per turn", actors["Null"].GetActionPoints(), 50);
            var hurt = new DamageInfo { Damage = 10, IsDamageInflicted = true };
            actors["Vale"].GetSkills().OnDamageReceived(enemy, camila, hurt);
            Check("Vale damage grants two tiles", mod.Waybackers.Turn(actors["Vale"]).EscapeTiles, 2);
            actors["Vale"]._TilesMovedThisTurn_k__BackingField = 1; mod.Waybackers.Moved(actors["Vale"]);
            actors["Vale"].GetSkills().OnDamageReceived(enemy, camila, hurt);
            Check("Vale refresh does not stack", mod.Waybackers.Turn(actors["Vale"]).EscapeTiles, 2);
            mod.Waybackers.Start(actors["Vale"]); mod.Waybackers.End(actors["Vale"]);
            Check("Vale next own turn expiry", mod.Waybackers.Turn(actors["Vale"]).EscapeTiles, 0);
            var flame = Attack("Voss", flamer);
            Check("Voss flame direct damage multiplier", mod.Waybackers.DamageMultiplier(flame, enemy.GetTile(), enemy, true), 1.2f);
            Check("Voss flame suppression multiplier", mod.Waybackers.SuppressionMultiplier(flame, enemy.GetTile(), true), 1.25f);
            Check("Voss other weapon excluded", mod.Waybackers.DamageMultiplier(Attack("Voss", rifle), enemy.GetTile(), enemy, true), 1);
            Check("Markov MG suppression", mod.Waybackers.SuppressionMultiplier(markov, enemy.GetTile(), true), 1.5f);
            enemy.m_Hitpoints = 49;
            Check("Null wounded damage", mod.Waybackers.DamageMultiplier(nullAttack, enemy.GetTile(), enemy, true), 1.15f);
            enemy.m_Hitpoints = 50;
            Check("Null half health boundary", mod.Waybackers.DamageMultiplier(nullAttack, enemy.GetTile(), enemy, true), 1);
            var movement = new MovementType { m_MovementCosts = new Il2CppStructArray<uint>(new uint[32]), m_LowestMovementCost = 10 };
            for (var i = 0; i < 32; i++) movement.m_MovementCosts[i] = (uint)(i == 0 ? 10 : 20);
            Check("Lucky native rough terrain", movement.GetMovementCostForTileType(1, actors["Lucky"]), 16);
            Check("Lucky native ordinary terrain", movement.GetMovementCostForTileType(0, actors["Lucky"]), 10);
            Check("Viper native walker movement", movement.GetMovementCostForTileType(0, actors["Viper"]), 8);
            Hit("Lynx concealed", Attack("Lynx", rifle), 60);
            actors["Lynx"].m_DetectedMask = ulong.MaxValue;
            Hit("Lynx detected", Attack("Lynx", rifle), 50);
            actors["Lynx"].m_DetectedMask = 0;
            var darius = Attack("Darius", rifle);
            Check("Darius first close damage", mod.Waybackers.DamageMultiplier(darius, enemy.GetTile(), enemy, false), 1.2f);
            Check("Darius first close suppression", mod.Waybackers.SuppressionMultiplier(darius, enemy.GetTile(), false), 1.25f);
            mod.Waybackers.BeginUse(darius, enemy.GetTile()); darius.GetContainer().OnSkillUsed(darius, enemy.GetTile());
            Check("Darius subsequent preview damage", mod.Waybackers.DamageMultiplier(darius, enemy.GetTile(), enemy, false), 1);
            Check("Darius committed first shot damage", mod.Waybackers.DamageMultiplier(darius, enemy.GetTile(), enemy, true), 1.2f);
            mod.Waybackers.Start(actors["Darius"]);
            Place(enemy, TileAt(4));
            Check("Darius distant attack excluded", mod.Waybackers.DamageMultiplier(darius, enemy.GetTile(), enemy, false), 1);
            Place(enemy, TileAt(1));
            void DirectDamage(string name, Skill skill, float mult)
            {
                var leader = created[name];
                var d = mod.Waybackers.Leaders.Values.First(d => d.Name == name);
                var index = leader.m_Perks.IndexOf(d.Perk);
                var baseline = new DamageInfo { Damage = 100, ArmorPenetration = 100 };
                leader.m_Perks.RemoveAt(index);
                TacticalManager.s_Random = new PseudoRandom(12345);
                skill.GetContainer().OnBeforeTargetHit(skill, enemy, baseline);
                leader.m_Perks.Insert(index, d.Perk);
                var enhanced = new DamageInfo { Damage = 100, ArmorPenetration = 100 };
                TacticalManager.s_Random = new PseudoRandom(12345);
                skill.GetContainer().OnBeforeTargetHit(skill, enemy, enhanced);
                if (baseline.Damage <= 0) throw new Exception(name + ": empty native damage fixture.");
                Check(name + " native pre-hit damage packet", enhanced.Damage, (int)Math.Round(baseline.Damage * mult, MidpointRounding.AwayFromZero));
            }
            DirectDamage("Voss", flame, 1.2f);
            DirectDamage("Darius", darius, 1.2f);
            DirectDamage("Ash", Attack("Ash", rifle), 1.1f);
            mod.Waybackers.Start(actors["Camila"]);
            DirectDamage("Camila", camila, 1.15f);
            enemy.m_Hitpoints = 49;
            DirectDamage("Null", nullAttack, 1.15f);
            enemy.m_Hitpoints = 100;
            var calebActor = actors["Caleb"];
            var calebDef = mod.Waybackers.Leaders.Values.First(d => d.Name == "Caleb");
            created["Caleb"].m_Perks.Remove(calebDef.Perk);
            calebActor.m_Suppression = 0;
            calebActor.ApplySuppression(10, true, enemy, flame);
            var normalSuppression = calebActor.GetSuppression();
            created["Caleb"].m_Perks.Add(calebDef.Perk);
            calebActor.m_Suppression = 0;
            calebActor.ApplySuppression(10, true, enemy, flame);
            Check("Caleb real ApplySuppression", calebActor.GetSuppression(), normalSuppression * .8f);
            var defenseInfo = new DamageInfo { Damage = 10 };
            var defended = actors["Creed"].GetSkills().BuildPropertiesForBeingHit(enemy, flame, defenseInfo);
            Place(actors["Don"], TileAt(5));
            var undefended = actors["Creed"].GetSkills().BuildPropertiesForBeingHit(enemy, flame, defenseInfo);
            Check("Don real incoming damage properties", defended.DamageSustainedMult, undefended.DamageSustainedMult * .9f);
            Place(actors["Don"], TileAt(0));
            mod.Waybackers.Turn(actors["Talia"]).EscapeTiles = 2;
            mod.Waybackers.Turn(actors["Talia"]).EscapeEnds = 1;
            var query = WaybackerMovementQuery.Push(actors["Talia"], MovementAction.Default);
            try
            {
                Check("Talia native first tile", movement.GetMovementCostForTileType(1, actors["Talia"]), 15);
                Check("Talia native second tile", movement.GetMovementCostForTileType(1, actors["Talia"]), 15);
                Check("Talia native third tile", movement.GetMovementCostForTileType(1, actors["Talia"]), 20);
            }
            finally { WaybackerMovementQuery.Current = query.Previous; }
            var path = new Il2CppSystem.Collections.Generic.List<Vector3>();
            for (var i = 0; i <= 3; i++) path.Add(map.TileToWorldPos(new Vector2Int(2, 2 + i), false));
            var td = mod.Waybackers.Leaders.Values.First(d => d.Name == "Talia");
            created["Talia"].m_Perks.Remove(td.Perk);
            var basePathCost = movement.GetTotalPathCost(path, MovementAction.Default, actors["Talia"], (Direction)0);
            created["Talia"].m_Perks.Add(td.Perk);
            var reducedPathCost = movement.GetTotalPathCost(path, MovementAction.Default, actors["Talia"], (Direction)0);
            if (basePathCost <= 0) throw new Exception("Empty native path cost fixture.");
            Check("Talia real three tile path cost", reducedPathCost, basePathCost - 10);
            Il2CppSystem.Collections.Generic.List<Vector3> CopyPath()
            {
                var copy = new Il2CppSystem.Collections.Generic.List<Vector3>();
                for (var i = 0; i < path.Count; i++) copy.Add(path[i]);
                return copy;
            }
            foreach (var clip in new[] { false, true })
            {
                var clipped = CopyPath();
                var action = MovementAction.Default;
                var clippedCost = movement.ClipPathToCost(clipped, out var dest, out var destIndex, ref action, 30, actors["Talia"], (Direction)0, clip);
                Check("Talia native budget cost clip=" + clip, clippedCost, 30);
                Check("Talia native budget destination clip=" + clip, destIndex, 2);
                Check("Talia native path clipping clip=" + clip, clipped.Count, clip ? 3 : 4);
                Check("Movement query restored clip=" + clip, WaybackerMovementQuery.BypassDepth, 0);
                if (dest == null) throw new Exception("Native clipped destination missing.");
            }

            File.WriteAllText(RuntimeVerification.Output + "/waybackers-verification.json", JsonSerializer.Serialize(new {
                success = true, initialPerks = 22, additionalNativeSaveRoundTrips = 21, nativeTemplates = templates,
                cases = rows, caseCount = rows.Count, nativeOnlyIsolatedMemory = true, audioMuted = AudioListener.pause && AudioListener.volume == 0
            }, new JsonSerializerOptions { WriteIndented = true }));
            MelonLoader.MelonLogger.Msg("WAYBACKERS_TEST SUCCESS: 21 new initial perks, native migrations/save round trips and " + rows.Count + " effect checks.");
        }
        finally
        {
            File.WriteAllText(RuntimeVerification.Output + "/waybackers-progress.json", JsonSerializer.Serialize(new { templates, rows }, new JsonSerializerOptions { WriteIndented = true }));
            TacticalManager.s_Singleton = managerBefore;
            TacticalManager.s_Random = randomBefore;
            mod.Waybackers.Turns.Clear();
        }
    }
}
