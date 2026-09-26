using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// End-to-end self test. Runs only when the game is started with "-quicktest -af_autotest"
    /// (optionally "-af_autotest_quit" and "-af_report=C:\path\report.txt"); otherwise it does nothing.
    /// Spawns every aircraft (including addons), then checks automatic and manual reloading, ammo type changes,
    /// flight and combat, bombs, boarding, save/load (also of a legacy save), cross-map travel, the UI and destruction.
    /// </summary>
    public class GameComponent_AFAutoTest : GameComponent
    {
        public static readonly bool Enabled = GenCommandLine.CommandLineArgPassed("af_autotest");
        private const string SaveName = "AF_AutoTest";
        private const string LegacySaveName = "AF_AutoTest_Legacy";
        private const int Done = 100;

        private int step = -1;
        private int stepStartTick = -1;
        private int failures;
        private int checks;
        private List<string> log = new List<string>();
        private List<Building_Aerocraft_AsBaseThing> aircraft = new List<Building_Aerocraft_AsBaseThing>();
        private List<string> savedSummary = new List<string>();
        private Building_Aerocraft_AsBaseThing subject;
        private Pawn hostile;
        private Thing testGun;
        private Building_Aerocraft_Base testTurret;
        private ThingDef testAmmo;
        private int counter;
        private string legacyAircraftId;
        private int homeTile = -1;

        private static int errorsSeen;
        private static readonly List<string> errorSamples = new List<string>();
        private static bool logHooked;
        private int uiFrames;
        private int uiStage;
        private int uiErrorsAtStart;
        private bool uiCaptured;
        private Window uiWindow;

        public GameComponent_AFAutoTest(Game game)
        {
            if (Enabled && !logHooked)
            {
                logHooked = true;
                Application.logMessageReceivedThreaded += (condition, stackTrace, type) =>
                {
                    if (type == LogType.Error || type == LogType.Exception)
                    {
                        errorsSeen++;
                        if (errorSamples.Count < 25)
                        {
                            errorSamples.Add(condition.Length > 400 ? condition.Substring(0, 400) : condition);
                        }
                    }
                };
            }
        }

        public override void ExposeData()
        {
            if (!Enabled)
            {
                return;
            }
            Scribe_Values.Look(ref step, "afTestStep", -1);
            Scribe_Values.Look(ref failures, "afTestFailures", 0);
            Scribe_Values.Look(ref checks, "afTestChecks", 0);
            Scribe_Values.Look(ref legacyAircraftId, "afTestLegacyId");
            Scribe_Values.Look(ref homeTile, "afTestHomeTile", -1);
            Scribe_Collections.Look(ref log, "afTestLog", LookMode.Value);
            Scribe_Collections.Look(ref savedSummary, "afTestSummary", LookMode.Value);
            Scribe_Collections.Look(ref aircraft, "afTestAircraft", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                log = log ?? new List<string>();
                savedSummary = savedSummary ?? new List<string>();
                aircraft = aircraft ?? new List<Building_Aerocraft_AsBaseThing>();
                aircraft.RemoveAll(a => a == null);
                stepStartTick = -1;
            }
        }

        // ------------------------------------------------------------------ driving

        public override void GameComponentOnGUI()
        {
            if (Enabled)
            {
                uiFrames++;
            }
        }

        public override void GameComponentUpdate()
        {
            if (!Enabled || step >= Done || LongEventHandler.ShouldWaitForEvent || Find.CurrentMap == null)
            {
                return;
            }
            Prefs.DevMode = true;
            if (step == 12)
            {
                try
                {
                    UiSmokeTest();
                }
                catch (Exception e)
                {
                    Fail("Exception in the UI smoke test: " + e);
                    Next(13);
                }
                return;
            }
            if (Find.TickManager.CurTimeSpeed != TimeSpeed.Ultrafast)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
            }
            foreach (Window window in Find.WindowStack.Windows.ToList())
            {
                if (window.forcePause || window is Dialog_NodeTree)
                {
                    window.Close(doCloseSound: false);
                }
            }
        }

        public override void GameComponentTick()
        {
            if (!Enabled || step >= Done)
            {
                return;
            }
            if (stepStartTick < 0)
            {
                stepStartTick = Find.TickManager.TicksGame;
            }
            try
            {
                RunStep();
            }
            catch (Exception e)
            {
                Fail($"Exception in step {step}: {e}");
                Finish();
            }
        }

        private int StepTicks => Find.TickManager.TicksGame - stepStartTick;

        private Map HomeMap => Find.Maps.FirstOrDefault(m => m.IsPlayerHome) ?? Find.CurrentMap;

        private void Next(int nextStep)
        {
            Note($"--- step {step} -> {nextStep} after {StepTicks} ticks");
            step = nextStep;
            stepStartTick = Find.TickManager.TicksGame;
            counter = 0;
        }

        private void RunStep()
        {
            // No raids or other incidents: they down the colonists the steps rely on. Loading a save restores them.
            if (Find.Storyteller.storytellerComps.Count > 0)
            {
                Find.Storyteller.storytellerComps.Clear();
                Find.Storyteller.incidentQueue.Clear();
            }
            switch (step)
            {
                case -1:
                    if (StepTicks > 600)
                    {
                        Next(0);
                    }
                    break;
                case 0:
                    Setup();
                    break;
                case 1:
                    if (StepTicks > 60)
                    {
                        VerifySpawn();
                    }
                    break;
                case 2:
                    StartAutoReload();
                    break;
                case 3:
                    WaitAutoReload();
                    break;
                case 4:
                    ManualReloadAndAmmoSwitch();
                    break;
                case 5:
                    FlightAndCombat();
                    break;
                case 6:
                    Bombs();
                    break;
                case 7:
                    Boarding();
                    break;
                case 8:
                    SaveAndReload();
                    break;
                case 9:
                    if (StepTicks > 60)
                    {
                        VerifyAfterLoadAndMakeLegacySave();
                    }
                    break;
                case 10:
                    if (StepTicks > 60)
                    {
                        VerifyLegacyLoad();
                    }
                    break;
                case 11:
                    CrossMap();
                    break;
                case 13:
                    Destruction();
                    break;
                case 14:
                    Finish();
                    break;
            }
        }

        // ------------------------------------------------------------------ 0-1: spawning

        private void Setup()
        {
            Map map = HomeMap;
            homeTile = map.Tile;
            Note($"Combat system: {AerocraftCompat.Ammo.Name}; mods: {string.Join(", ", LoadedModManager.RunningModsListForReading.Select(m => m.PackageId))}");
            Find.PlaySettings.useWorkPriorities = true;
            while (map.mapPawns.FreeColonistsSpawnedCount < 6)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 10), map);
            }
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                PrepareColonist(pawn);
            }
            foreach (Pawn hostile in map.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Faction.OfPlayer)).ToList())
            {
                hostile.Destroy();
            }
            CellRect area = CellRect.CenteredOn(map.Center, 60).ClipInsideMap(map);
            ClearArea(map, area);
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.thingClass != null && typeof(Building_Aerocraft_AsBaseThing).IsAssignableFrom(d.thingClass) && !d.IsBlueprint && !d.IsFrame && d.building != null)
                .OrderBy(d => d.defName).ToList();
            Note($"Aircraft defs: {string.Join(", ", defs.Select(d => d.defName + " [" + d.modContentPack?.Name + "]"))}");
            Check(defs.Count >= 6, $"all aircraft defs loaded ({defs.Count})");
            int x = area.minX + 6;
            int z = area.minZ + 6;
            int rowHeight = 0;
            foreach (ThingDef def in defs)
            {
                int size = Math.Max(def.size.x, def.size.z);
                if (x + size + 2 > area.maxX)
                {
                    x = area.minX + 6;
                    z += rowHeight + 5;
                    rowHeight = 0;
                }
                IntVec3 cell = new IntVec3(x + size / 2, 0, z + size / 2);
                x += size + 5;
                rowHeight = Math.Max(rowHeight, size);
                Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                thing.SetFactionDirect(Faction.OfPlayer);
                GenSpawn.Spawn(thing, cell, map);
                aircraft.Add((Building_Aerocraft_AsBaseThing)thing);
            }
            Next(1);
        }

        private static void PrepareColonist(Pawn pawn)
        {
            if (pawn.drafter != null)
            {
                pawn.drafter.Drafted = false;
            }
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (!pawn.WorkTypeIsDisabled(work))
                {
                    pawn.workSettings?.SetPriority(work, work == WorkTypeDefOf.Hauling ? 1 : 0);
                }
            }
            pawn.needs?.food?.SetInitialLevel();
            if (pawn.needs?.rest != null)
            {
                pawn.needs.rest.CurLevel = 1f;
            }
        }

        private static void ClearArea(Map map, CellRect area)
        {
            foreach (IntVec3 cell in area)
            {
                foreach (Thing thing in cell.GetThingList(map).ToList())
                {
                    if (!(thing is Pawn) && thing.def.destroyable)
                    {
                        thing.Destroy();
                    }
                }
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                map.roofGrid.SetRoof(cell, null);
                map.fogGrid.Unfog(cell);
            }
        }

        private void VerifySpawn()
        {
            Map map = HomeMap;
            MapComponent_AerocraftTracker tracker = MapComponent_AerocraftTracker.For(map);
            Pawn colonist = map.mapPawns.FreeColonistsSpawned.First();
            foreach (Building_Aerocraft_AsBaseThing craft in aircraft.ToList())
            {
                string name = craft.def.defName;
                Check(craft.Spawned && craft.Gun_Now != null, $"{name}: spawned with a weapon ({craft.Gun_Now?.def.defName})");
                int expectedMounts = craft.GetComps<Comp_GetExtraWeapon>().Count();
                Check(craft.AllExtraWeapon.Count == expectedMounts, $"{name}: {craft.AllExtraWeapon.Count}/{expectedMounts} weapon mounts");
                Check(craft.AllExtraWeapon.All(m => m.Spawned && (m as Building_Aerocraft_AsWeapon)?.Building_Aerocraft_AsBaseThing == craft), $"{name}: mounts are spawned and linked");
                Check(tracker.Turrets.Contains(craft) && craft.AllTurrets.All(t => tracker.Turrets.Contains(t)), $"{name}: registered in the tracker");
                foreach (Building_Aerocraft_Base turret in craft.AllTurrets)
                {
                    foreach (Thing gun in turret.AllGuns)
                    {
                        Check(AerocraftCompat.Ammo.IsLinkedTo(gun, turret), $"{name}: {gun.def.defName} ammo user linked to {turret.def.defName}");
                        if (AerocraftCompat.Ammo.TryGetMagazine(gun, out int current, out int capacity))
                        {
                            Note($"  {turret.def.defName}: {gun.def.defName} magazine {current}/{capacity}, ammo {AerocraftCompat.Ammo.SelectedAmmoDef(gun)?.defName}");
                        }
                    }
                }
                try
                {
                    craft.GetInspectString();
                    craft.GetGizmos().ToList();
                    craft.GetFloatMenuOptions(colonist).ToList();
                    foreach (Building_Aerocraft_Base mount in craft.AllExtraWeapon.OfType<Building_Aerocraft_Base>())
                    {
                        mount.GetInspectString();
                        mount.GetGizmos().ToList();
                    }
                    foreach (ThingComp comp in craft.AllComps)
                    {
                        comp.CompFloatMenuOptions(colonist).ToList();
                    }
                    Check(true, $"{name}: inspect string, gizmos and float menus");
                }
                catch (Exception e)
                {
                    Fail($"{name}: inspect/gizmos threw {e}");
                }
                if (craft.TryGetComp<Comp_DoExplosion_BySomeWays>()?.Props.If_CountDownToExplosion == true)
                {
                    Note($"  {name} self-destructs on a timer: removed after the spawn checks");
                    craft.Destroy();
                    aircraft.Remove(craft);
                }
            }
            Next(AerocraftCompat.Ammo.UsesAmmo ? 2 : 5);
        }

        // ------------------------------------------------------------------ 2-4: reloading

        private IEnumerable<(Building_Aerocraft_Base turret, Thing gun)> MagazineGuns()
        {
            foreach (Building_Aerocraft_AsBaseThing craft in aircraft)
            {
                foreach (Building_Aerocraft_Base turret in craft.AllTurrets)
                {
                    Thing gun = turret.Gun_Now;
                    if (gun != null && AerocraftCompat.Ammo.TryGetMagazine(gun, out _, out int capacity) && capacity > 0)
                    {
                        yield return (turret, gun);
                    }
                }
            }
        }

        private static void SpawnAmmo(Map map, IntVec3 near, ThingDef ammoDef, int count)
        {
            while (count > 0)
            {
                Thing ammo = ThingMaker.MakeThing(ammoDef);
                ammo.stackCount = Math.Min(count, ammoDef.stackLimit);
                count -= ammo.stackCount;
                GenPlace.TryPlaceThing(ammo, near, map, ThingPlaceMode.Near);
                ammo.SetForbidden(false, warnOnFail: false);
            }
        }

        private static IntVec3 CellBeside(Thing thing)
        {
            return thing.OccupiedRect().ExpandedBy(2).EdgeCells.First(c => c.InBounds(thing.Map) && c.Standable(thing.Map));
        }

        private void StartAutoReload()
        {
            Map map = HomeMap;
            int guns = 0;
            foreach ((Building_Aerocraft_Base turret, Thing gun) in MagazineGuns().ToList())
            {
                AerocraftCompat.Ammo.DebugSetMagazine(gun, 0);
                ThingDef ammo = AerocraftCompat.Ammo.SelectedAmmoDef(gun);
                AerocraftCompat.Ammo.TryGetMagazine(gun, out _, out int capacity);
                if (ammo != null)
                {
                    SpawnAmmo(map, CellBeside(AerocraftUtility.AircraftOf(turret)), ammo, capacity + 5);
                }
                guns++;
            }
            Note($"Emptied {guns} magazines and put ammo next to each aircraft");
            Check(guns > 0, "aircraft have guns with magazines under Combat Extended");
            Next(3);
        }

        private void WaitAutoReload()
        {
            if (StepTicks % 500 != 0)
            {
                return;
            }
            List<(Building_Aerocraft_Base turret, Thing gun)> guns = MagazineGuns().ToList();
            List<(Building_Aerocraft_Base turret, Thing gun)> notFull = guns.Where(g => AerocraftCompat.Ammo.NeedsReload(g.gun)).ToList();
            Note($"auto reload: {guns.Count - notFull.Count}/{guns.Count} full; jobs: {string.Join(", ", HomeMap.mapPawns.FreeColonistsSpawned.Select(p => p.LabelShort + "=" + (p.CurJobDef?.defName ?? "-")))}");
            if (notFull.Count == 0)
            {
                Check(true, $"colonists reloaded all {guns.Count} aircraft guns automatically");
                Next(4);
                return;
            }
            if (StepTicks > 40000)
            {
                foreach ((Building_Aerocraft_Base turret, Thing gun) in notFull)
                {
                    AerocraftCompat.Ammo.TryGetMagazine(gun, out int current, out int capacity);
                    Note($"  not reloaded: {turret.def.defName} {gun.def.defName} {current}/{capacity}: {AerocraftCompat.Ammo.CanReloadNow(turret, gun).Reason}");
                }
                Fail($"colonists reloaded all aircraft guns automatically ({notFull.Count} left)");
                Next(4);
            }
        }

        private void ManualReloadAndAmmoSwitch()
        {
            Map map = HomeMap;
            if (counter == 0)
            {
                (Building_Aerocraft_Base turret, Thing gun) choice = MagazineGuns().FirstOrDefault(g => AerocraftCompat.Ammo.AmmoTypes(g.gun).Count() > 1);
                if (choice.gun == null)
                {
                    Note("No gun with several ammo types: ammo switch not tested");
                    Next(5);
                    return;
                }
                testTurret = choice.turret;
                testGun = choice.gun;
                ThingDef current = AerocraftCompat.Ammo.CurrentAmmoDef(testGun);
                testAmmo = AerocraftCompat.Ammo.AmmoTypes(testGun).First(a => a != current);
                AerocraftCompat.Ammo.TryGetMagazine(testGun, out _, out int capacity);
                AerocraftCompat.Ammo.DebugSetMagazine(testGun, capacity / 2);
                AerocraftCompat.Ammo.SetSelectedAmmo(testGun, testAmmo);
                SpawnAmmo(map, CellBeside(AerocraftUtility.AircraftOf(testTurret)), testAmmo, capacity + 5);
                MYDE_AerocraftFramework_Setting.If_AutoReload = false;
                Pawn pawn = AerocraftCompat.Ammo.TryOrderReload(testTurret, testGun);
                Check(pawn != null, $"manual reload order of {testGun.def.defName} with {testAmmo.defName} given to {pawn}");
                Note($"Old ammo {current?.defName}: {map.listerThings.ThingsOfDef(current).Sum(t => t.stackCount)} on the map");
                counter = 1;
                return;
            }
            bool switched = AerocraftCompat.Ammo.CurrentAmmoDef(testGun) == testAmmo && !AerocraftCompat.Ammo.NeedsReload(testGun);
            if (switched || StepTicks > 20000)
            {
                MYDE_AerocraftFramework_Setting.If_AutoReload = true;
                AerocraftCompat.Ammo.TryGetMagazine(testGun, out int current, out int capacity);
                Check(switched, $"ammo type switched to {testAmmo.defName} and the magazine refilled ({current}/{capacity})");
                Next(5);
            }
        }

        // ------------------------------------------------------------------ 5: flight and combat

        private Building_Aerocraft_AsBaseThing PickCraft(params string[] preferred)
        {
            foreach (string defName in preferred)
            {
                Building_Aerocraft_AsBaseThing found = aircraft.FirstOrDefault(a => a.def.defName == defName && a.Spawned);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private void BoardPilots(Building_Aerocraft_AsBaseThing craft, int count)
        {
            foreach (Pawn pawn in craft.Map.mapPawns.FreeColonistsSpawned.Where(p => !p.Downed).Take(count).ToList())
            {
                craft.DoSomething_CarryPawn(pawn);
            }
        }

        private static void FillUp(Building_Aerocraft_AsBaseThing craft)
        {
            CompRefuelable fuel = craft.RefuelableComp;
            fuel?.Refuel(fuel.Props.fuelCapacity);
            craft.CompPowerBattery?.SetStoredEnergyPct(1f);
            foreach (Building_Aerocraft_Base turret in craft.AllTurrets)
            {
                AerocraftCompat.Ammo.FillMagazines(turret);
            }
        }

        private int TotalRounds(Building_Aerocraft_AsBaseThing craft)
        {
            int total = 0;
            foreach (Building_Aerocraft_Base turret in craft.AllTurrets)
            {
                if (turret.Gun_Now != null && AerocraftCompat.Ammo.TryGetMagazine(turret.Gun_Now, out int current, out _))
                {
                    total += current;
                }
            }
            return total;
        }

        private void FlightAndCombat()
        {
            Map map = HomeMap;
            switch (counter)
            {
                case 0:
                    subject = PickCraft("MYDE_AF_MI24_Base", "MYDE_AF_KA52_Base", "MYDE_AF_RotorCraft_FourPropeller_Base");
                    if (subject == null)
                    {
                        Fail("no helicopter to test flight");
                        Next(6);
                        return;
                    }
                    FillUp(subject);
                    BoardPilots(subject, Math.Max(1, subject.NeedPawnToControl_Number));
                    Check(subject.HasEnoughPilots, $"{subject.def.defName}: {subject.ListPawn.Count} pilots on board");
                    subject.Change_UpOrDown();
                    counter = 1;
                    break;
                case 1:
                    if (subject.Is_Flying)
                    {
                        Check(true, $"{subject.def.defName} took off in {StepTicks} ticks");
                        if (AerocraftCompat.Ammo.UsesAmmo && AerocraftCompat.Ammo.TryGetMagazine(subject.Gun_Now, out _, out int capacity))
                        {
                            AerocraftCompat.Ammo.DebugSetMagazine(subject.Gun_Now, capacity - 20);
                            AcceptanceReport canReload = AerocraftCompat.Ammo.CanReloadNow(subject, subject.Gun_Now);
                            Check(!canReload.Accepted && canReload.Reason == "AerocraftFramework_Service_NotLanded".Translate(), "no reloading while flying: " + canReload.Reason);
                        }
                        savedSummary.Clear();
                        savedSummary.Add(TotalRounds(subject).ToString());
                        Faction enemy = Find.FactionManager.RandomEnemyFaction(allowHidden: false, allowDefeated: false, allowNonHumanlike: false, minTechLevel: TechLevel.Undefined);
                        hostile = PawnGenerator.GeneratePawn(enemy?.RandomPawnKind() ?? PawnKindDefOf.Villager, enemy);
                        hostile.equipment?.DestroyAllEquipment();
                        IntVec3 cell = CellFinder.RandomClosewalkCellNear(subject.Position + new IntVec3(15, 0, 0), map, 3);
                        GenSpawn.Spawn(hostile, cell, map);
                        Note($"Spawned hostile {hostile} ({enemy?.Name}) at {cell}");
                        counter = 2;
                    }
                    else if (StepTicks > 2000)
                    {
                        Fail($"{subject.def.defName} did not take off");
                        Next(6);
                    }
                    break;
                case 2:
                    bool hurt = hostile.Dead || hostile.Downed || hostile.health.hediffSet.hediffs.Any(h => h is Hediff_Injury);
                    bool fired = !AerocraftCompat.Ammo.UsesAmmo || TotalRounds(subject) < int.Parse(savedSummary[0]);
                    if ((hurt && fired) || StepTicks > 4000)
                    {
                        Check(fired, $"{subject.def.defName} fired at the enemy (rounds {savedSummary[0]} -> {TotalRounds(subject)})");
                        Check(hurt, $"the enemy was hit ({(hostile.Dead ? "dead" : hostile.Downed ? "downed" : "injured")})");
                        if (!hostile.Dead)
                        {
                            hostile.Kill(null);
                        }
                        subject.Set_TargetVPos((subject.Position + new IntVec3(-15, 0, 10)).ToVector3Shifted());
                        counter = 3;
                        stepStartTick = Find.TickManager.TicksGame;
                    }
                    break;
                case 3:
                    if (subject.HaveGoToTarget || StepTicks > 3000)
                    {
                        Check(subject.HaveGoToTarget, $"{subject.def.defName} flew to the ordered point");
                        subject.Set_Target_FollowTargetThing(subject);
                        Check(subject.FollowTargetThing == null, "ordering an aircraft to follow itself is refused");
                        subject.Change_UpOrDown();
                        counter = 4;
                        stepStartTick = Find.TickManager.TicksGame;
                    }
                    break;
                case 4:
                    if (subject.Is_Static || StepTicks > 2000)
                    {
                        Check(subject.Is_Static, $"{subject.def.defName} landed");
                        List<Pawn> pilots = subject.ListPawn.ToList();
                        subject.ReleaseAllPawns(drafted: false);
                        Check(subject.ListPawn.Count == 0 && pilots.All(p => p.Spawned), $"{pilots.Count} pilots let out");
                        foreach (Pawn pilot in pilots)
                        {
                            PrepareColonist(pilot);
                        }
                        Next(6);
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ 6: bombs

        private void Bombs()
        {
            Map map = HomeMap;
            switch (counter)
            {
                case 0:
                    subject = aircraft.FirstOrDefault(a => a.Spawned && a.TryGetComp<Comp_CanLoadShell>() != null);
                    ThingDef shellDef = DefDatabase<ThingDef>.GetNamedSilentFail("Shell_HighExplosive");
                    if (subject == null || shellDef == null || !AerocraftCompat.Ammo.IsLoadableShell(shellDef))
                    {
                        Fail($"bomber and loadable shells available ({subject?.def.defName}, {shellDef?.defName})");
                        Next(7);
                        return;
                    }
                    SpawnAmmo(map, CellBeside(subject), shellDef, 6);
                    subject.TryGetComp<Comp_CanLoadShell>().OrderLoadShells(null);
                    counter = 1;
                    break;
                case 1:
                    Comp_CanLoadShell bay = subject.TryGetComp<Comp_CanLoadShell>();
                    if (bay.TotalShells >= 6 || StepTicks > 15000)
                    {
                        Check(bay.TotalShells >= 6, $"{subject.def.defName}: colonists loaded {bay.TotalShells} bombs");
                        Check(bay.CurrentShellProjectile != null, $"bomb projectile {bay.CurrentShellProjectile?.defName} ({bay.CurrentShellProjectile?.thingClass?.Name})");
                        FillUp(subject);
                        BoardPilots(subject, Math.Max(1, subject.NeedPawnToControl_Number));
                        subject.Set_Flying();
                        counter = 2;
                        stepStartTick = Find.TickManager.TicksGame;
                    }
                    break;
                case 2:
                    if (StepTicks < 120)
                    {
                        return;
                    }
                    Comp_CanLoadShell bay2 = subject.TryGetComp<Comp_CanLoadShell>();
                    int before = bay2.TotalShells;
                    Check(bay2.CanActivate().Accepted, "bombs can be dropped in flight: " + bay2.CanActivate().Reason);
                    bay2.DoSomething_LaunchShell_Drop();
                    Check(bay2.TotalShells == before - 1, $"a bomb was dropped ({before} -> {bay2.TotalShells})");
                    Building_Aerocraft_AsBaseThing support = aircraft.FirstOrDefault(a => a.Spawned && a.TryGetComp<Comp_ShootSomethingManual>() != null);
                    if (support != null)
                    {
                        ThingDef shot = support.TryGetComp<Comp_ShootSomethingManual>().Props.ShootSomethingDef;
                        Check(AerocraftCompat.Ammo.LaunchProjectile(support, shot, support.DrawPos, 1f, support.Position + new IntVec3(6, 0, 0)), $"{support.def.defName} launched {shot?.defName}");
                    }
                    subject.Change_Down();
                    counter = 3;
                    stepStartTick = Find.TickManager.TicksGame;
                    break;
                case 3:
                    if (subject.Is_Static || StepTicks > 3000)
                    {
                        Check(subject.Is_Static, $"{subject.def.defName} landed");
                        foreach (Pawn pilot in subject.ListPawn.ToList())
                        {
                            subject.ReleasePawn(pilot, drafted: false);
                            PrepareColonist(pilot);
                        }
                        Next(7);
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ 7: boarding

        private void Boarding()
        {
            switch (counter)
            {
                case 0:
                    subject = aircraft.FirstOrDefault(a => a.Spawned && a.TryGetComp<Comp_CarryPawn>() != null && a.ListPawn.Count == 0);
                    Pawn pawn = HomeMap.mapPawns.FreeColonistsSpawned.First();
                    Job job = JobMaker.MakeJob(MYDE_JobDefOf.MYDE_AerocraftFramework_Job_Enter_Building_Aerocraft_AsBaseThing, subject);
                    Check(pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc), $"{pawn} ordered to board {subject.def.defName}");
                    testGun = pawn;
                    counter = 1;
                    break;
                case 1:
                    Pawn boarding = (Pawn)testGun;
                    if (subject.ListPawn.Contains(boarding) || StepTicks > 3000)
                    {
                        Check(subject.ListPawn.Contains(boarding) && !boarding.Spawned, $"{boarding} boarded {subject.def.defName}");
                        Next(8);
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ 8-10: save and load

        private string Summary(Building_Aerocraft_AsBaseThing craft)
        {
            string guns = string.Join(";", craft.AllTurrets.Select(t => t.def.defName + ":" + t.Gun_Now?.def.defName + ":" + (AerocraftCompat.Ammo.TryGetMagazine(t.Gun_Now, out int c, out _) ? c : -1)));
            int shells = craft.TryGetComp<Comp_CanLoadShell>()?.TotalShells ?? 0;
            return $"{craft.ThingID} pilots={craft.ListPawn.Count} shells={shells} guns={guns}";
        }

        private void SaveAndReload()
        {
            savedSummary = aircraft.Where(a => a.Spawned).Select(Summary).ToList();
            Note("Saving and reloading");
            step = 9;
            stepStartTick = -1;
            GameDataSaveLoader.SaveGame(SaveName);
            LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame(SaveName));
            step = Done;
        }

        private void VerifyAfterLoadAndMakeLegacySave()
        {
            List<string> now = aircraft.Where(a => a.Spawned).Select(Summary).ToList();
            List<string> lost = savedSummary.Except(now).ToList();
            foreach (string line in lost)
            {
                Note("  before: " + line);
            }
            foreach (string line in now.Except(savedSummary))
            {
                Note("  after:  " + line);
            }
            Check(lost.Count == 0 && now.Count == savedSummary.Count, $"{now.Count} aircraft identical after save/load (weapons, magazines, pilots, bombs)");
            Check(aircraft.All(a => a.AllTurrets.All(t => t.AllGuns.All(g => AerocraftCompat.Ammo.IsLinkedTo(g, t)))), "ammo users re-linked to their turrets after loading");
            Check(aircraft.All(a => a.AllExtraWeapon.All(m => (m as Building_Aerocraft_AsWeapon)?.Building_Aerocraft_AsBaseThing == a)), "weapon mounts re-linked after loading");

            // Legacy save: the original mod deep-saved FollowTargetThing, nesting the aircraft into itself.
            legacyAircraftId = aircraft.First(a => a.Spawned).ThingID;
            step = 10;
            stepStartTick = -1;
            GameDataSaveLoader.SaveGame(SaveName);
            string path = GenFilePaths.FilePathForSavedGame(SaveName);
            XmlDocument doc = new XmlDocument();
            doc.Load(path);
            XmlElement node = (XmlElement)doc.SelectSingleNode($"//thing[@Class='MYDE_AerocraftFramework.Building_Aerocraft_AsBaseThing'][id='{legacyAircraftId}']");
            XmlNode follow = node["FollowTargetThing"];
            if (follow != null)
            {
                node.RemoveChild(follow);
            }
            XmlElement outer = doc.CreateElement("FollowTargetThing");
            outer.SetAttribute("Class", "MYDE_AerocraftFramework.Building_Aerocraft_AsBaseThing");
            foreach (XmlNode child in node.ChildNodes)
            {
                outer.AppendChild(child.CloneNode(deep: true));
            }
            node.AppendChild(outer);
            doc.Save(GenFilePaths.FilePathForSavedGame(LegacySaveName));
            Note($"Wrote a legacy save with {legacyAircraftId} deep-saved into itself");
            LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame(LegacySaveName));
            step = Done;
        }

        private void VerifyLegacyLoad()
        {
            List<Thing> all = HomeMap.listerThings.AllThings.Where(t => t is Building_Aerocraft_Base).ToList();
            Check(all.Select(t => t.ThingID).Distinct().Count() == all.Count, $"no duplicated aircraft after loading a legacy save ({all.Count} turrets)");
            Building_Aerocraft_AsBaseThing legacy = all.OfType<Building_Aerocraft_AsBaseThing>().FirstOrDefault(a => a.ThingID == legacyAircraftId);
            Check(legacy != null && legacy.FollowTargetThing == null, $"legacy deep-saved follow target dropped ({legacyAircraftId})");
            Next(11);
        }

        // ------------------------------------------------------------------ 11: cross-map travel

        private WorldObject_CrossMapThing_Flying Flying => Find.WorldObjects.AllWorldObjects.OfType<WorldObject_CrossMapThing_Flying>().FirstOrDefault();

        private void CrossMap()
        {
            switch (counter)
            {
                case 0:
                    subject = PickCraft("MYDE_AF_RotorCraft_FourPropeller_Base", "MYDE_AF_MI24_Base");
                    Settlement target = Find.WorldObjects.Settlements.Where(s => s.Faction != Faction.OfPlayer && !s.HasMap).OrderBy(s => Find.WorldGrid.ApproxDistanceInTiles(s.Tile, homeTile)).FirstOrDefault();
                    if (subject == null || target == null)
                    {
                        Note("No aircraft or settlement for the cross-map test");
                        Next(12);
                        return;
                    }
                    FillUp(subject);
                    foreach (Pawn pilot in subject.ListPawn.ToList())
                    {
                        subject.ReleasePawn(pilot, drafted: false);
                    }
                    BoardPilots(subject, Math.Max(1, subject.NeedPawnToControl_Number));
                    savedSummary = new List<string> { subject.ListPawn.Count.ToString(), subject.AllExtraWeapon.Count.ToString() };
                    Comp_CanCrossMap comp = subject.TryGetComp<Comp_CanCrossMap>();
                    Comp_CanCrossMap.ComputeRanges(subject.RefuelableComp, comp.Props.FuelConsumeBase, out comp.Fuel, out comp.SafeRange, out comp.NoBackRange);
                    comp.NoBackRange = 9999;
                    Note($"Flying {subject.def.defName} to {target.Label} ({Find.WorldGrid.ApproxDistanceInTiles(target.Tile, homeTile):F0} tiles)");
                    Check(comp.ChoseWorldTarget(new GlobalTargetInfo(target)), "cross-map flight ordered");
                    Check(!subject.Spawned, "the aircraft left the map");
                    counter = 1;
                    break;
                case 1:
                    WorldObject_CrossMapThing_Flying flying = Flying;
                    if (flying != null && flying.arrived)
                    {
                        Check(flying.LinkToAerocraft == subject && flying.AllExtraWeapon.Count == int.Parse(savedSummary[1]), "the aircraft and its mounts are travelling");
                        flying.DoSomething_Attack();
                        Check(subject.Spawned && subject.Map != HomeMap, $"landed on {subject.Map?.Parent?.Label}");
                        Check(subject.ListPawn.Count == int.Parse(savedSummary[0]), "pilots arrived with it");
                        Check(subject.AllExtraWeapon.All(m => m.Spawned && m.Map == subject.Map), "mounts arrived with it");
                        counter = 2;
                        stepStartTick = Find.TickManager.TicksGame;
                    }
                    else if (StepTicks > 120000)
                    {
                        Fail("the aircraft arrived at the settlement");
                        Next(12);
                    }
                    break;
                case 2:
                    if (StepTicks < 300)
                    {
                        return;
                    }
                    Comp_CanCrossMap back = subject.TryGetComp<Comp_CanCrossMap>();
                    Comp_CanCrossMap.ComputeRanges(subject.RefuelableComp, back.Props.FuelConsumeBase, out back.Fuel, out back.SafeRange, out back.NoBackRange);
                    back.NoBackRange = 9999;
                    Check(back.ChoseWorldTarget(new GlobalTargetInfo(HomeMap.Parent)), "return flight ordered");
                    counter = 3;
                    stepStartTick = Find.TickManager.TicksGame;
                    break;
                case 3:
                    if (subject.Spawned && subject.Map == HomeMap)
                    {
                        Check(subject.ListPawn.Count == int.Parse(savedSummary[0]) && subject.AllExtraWeapon.All(m => m.Spawned && m.Map == HomeMap), "back home with pilots and mounts");
                        Current.Game.CurrentMap = HomeMap;
                        Next(12);
                    }
                    else if (StepTicks > 120000)
                    {
                        Fail("the aircraft came back home");
                        Next(12);
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ 12: UI

        private void UiSmokeTest()
        {
            const int FramesPerStage = 60;
            if (uiStage > 0)
            {
                if (!uiCaptured && uiFrames >= FramesPerStage / 2)
                {
                    uiCaptured = true;
                    CaptureScreen("ui_" + uiStage);
                }
                if (uiFrames < FramesPerStage)
                {
                    return;
                }
                uiWindow?.Close(doCloseSound: false);
                uiWindow = null;
            }
            else
            {
                uiErrorsAtStart = errorsSeen;
                Current.Game.CurrentMap = HomeMap;
            }
            uiFrames = 0;
            uiCaptured = false;
            Building_Aerocraft_AsBaseThing craft = aircraft.Where(a => a.Spawned).OrderByDescending(a => Magazines(a).Count).ThenByDescending(a => a.AllExtraWeapon.Count).FirstOrDefault();
            switch (uiStage++)
            {
                case 0:
                    Note("UI: mod settings");
                    uiWindow = new Dialog_ModSettings(LoadedModManager.GetMod<MYDE_AerocraftFramework_Setting_Main>());
                    Find.WindowStack.Add(uiWindow);
                    break;
                case 1:
                    // An empty and an almost empty magazine, so that the ammo gizmo shows every state.
                    List<Thing> magazines = craft == null ? new List<Thing>() : Magazines(craft);
                    for (int i = 0; i < magazines.Count && i < 2; i++)
                    {
                        AerocraftCompat.Ammo.TryGetMagazine(magazines[i], out _, out int capacity);
                        AerocraftCompat.Ammo.DebugSetMagazine(magazines[i], i == 0 ? 0 : Mathf.Max(1, capacity / 10));
                    }
                    Note($"UI: aircraft tab and gizmos of {craft?.def.defName} ({magazines.Count} magazines)");
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(craft, playSound: false);
                    CameraJumper.TryJump(craft);
                    InspectPaneUtility.OpenTab(typeof(ITab_Aerocraft_Weapon));
                    break;
                case 2:
                    Building_Aerocraft_Base mount = craft?.AllExtraWeapon.OfType<Building_Aerocraft_Base>().FirstOrDefault();
                    Note($"UI: weapon mount {mount?.def.defName}");
                    Find.Selector.ClearSelection();
                    if (mount != null)
                    {
                        Find.Selector.Select(mount, playSound: false);
                        InspectPaneUtility.OpenTab(typeof(ITab_Aerocraft_Weapon));
                    }
                    break;
                case 3:
                    Note("UI: angle dialog and ammo menu");
                    uiWindow = new Dialog_Slider_Aerocraft(v => v.ToString(), -180, 180, v => { }, 0);
                    Find.WindowStack.Add(uiWindow);
                    if (craft?.Gun_Now != null && AerocraftCompat.Ammo.CanChooseAmmo(craft.Gun_Now))
                    {
                        AerocraftCompat.Ammo.OpenAmmoMenu(craft, craft.Gun_Now);
                    }
                    break;
                default:
                    foreach (Window window in Find.WindowStack.Windows.OfType<FloatMenu>().ToList())
                    {
                        window.Close(doCloseSound: false);
                    }
                    Find.Selector.ClearSelection();
                    Check(errorsSeen == uiErrorsAtStart, $"UI drew without errors ({errorsSeen - uiErrorsAtStart} errors)");
                    uiStage = 0;
                    Next(13);
                    return;
            }
        }

        private static List<Thing> Magazines(Building_Aerocraft_AsBaseThing craft)
        {
            return craft.AllTurrets.Select(t => t.Gun_Now).Where(g => g != null && AerocraftCompat.Ammo.TryGetMagazine(g, out _, out int capacity) && capacity > 0).ToList();
        }

        /// <summary>Saves a screenshot next to the report (for looking at the UI after a run).</summary>
        private void CaptureScreen(string name)
        {
            if (GenCommandLine.TryGetCommandLineArg("af_report", out string report))
            {
                string path = Path.Combine(Path.GetDirectoryName(report), name + ".png");
                ScreenCapture.CaptureScreenshot(path);
                Note("screenshot " + path);
            }
        }

        // ------------------------------------------------------------------ 13: destruction

        private void Destruction()
        {
            Map map = HomeMap;
            switch (counter)
            {
                case 0:
                    subject = aircraft.Where(a => a.Spawned && a.Is_Static && a.TryGetComp<Comp_CarryPawn>() != null).OrderByDescending(a => a.AllExtraWeapon.Count).FirstOrDefault();
                    if (subject == null)
                    {
                        Next(14);
                        return;
                    }
                    if (subject.ListPawn.Count == 0)
                    {
                        BoardPilots(subject, 1);
                    }
                    List<Pawn> pilots = subject.ListPawn.ToList();
                    List<Building> mounts = subject.AllExtraWeapon.ToList();
                    ThingDef refund = subject.def.CostList?.FirstOrDefault()?.thingDef;
                    int refundBefore = refund == null ? 0 : map.listerThings.ThingsOfDef(refund).Sum(t => t.stackCount);
                    subject.Destroy(DestroyMode.Deconstruct);
                    Check(pilots.All(p => p.Spawned), $"deconstructing {subject.def.defName} let the {pilots.Count} pilots out");
                    Check(mounts.All(m => m.Destroyed), $"its {mounts.Count} mounts were removed");
                    int refundAfter = refund == null ? 0 : map.listerThings.ThingsOfDef(refund).Sum(t => t.stackCount);
                    Check(refund == null || refundAfter > refundBefore, $"deconstruction refunded {refund?.defName} ({refundBefore} -> {refundAfter})");
                    aircraft.Remove(subject);
                    counter = 1;
                    break;
                case 1:
                    subject = aircraft.FirstOrDefault(a => a.Spawned && a.Is_Static && a.TryGetComp<Comp_CarryPawn>() != null);
                    if (subject == null)
                    {
                        Next(14);
                        return;
                    }
                    BoardPilots(subject, 1);
                    Pawn pilot = subject.ListPawn.FirstOrDefault();
                    subject.TakeDamage(new DamageInfo(DamageDefOf.Bomb, 99999f));
                    Check(subject.Destroyed, $"{subject.def.defName} destroyed by damage");
                    Check(pilot == null || pilot.Spawned || pilot.Dead, "the pilot came out (alive or dead) instead of vanishing");
                    aircraft.Remove(subject);
                    Next(14);
                    break;
            }
        }

        // ------------------------------------------------------------------ reporting

        private void Check(bool condition, string what)
        {
            checks++;
            if (condition)
            {
                Note("PASS " + what);
            }
            else
            {
                Fail(what);
            }
        }

        private void Fail(string what)
        {
            failures++;
            Note("FAIL " + what);
        }

        private void Note(string text)
        {
            string line = $"[{Find.TickManager?.TicksGame}] {text}";
            log.Add(line);
            Log.Message("[AF AutoTest] " + line);
        }

        private void Finish()
        {
            Note($"Errors logged during the whole run: {errorsSeen}");
            foreach (string sample in errorSamples)
            {
                Note("  error: " + sample);
            }
            Note($"DONE: {checks} checks, {failures} failures");
            step = Done;
            try
            {
                if (GenCommandLine.TryGetCommandLineArg("af_report", out string path))
                {
                    File.WriteAllLines(path, log);
                }
            }
            catch (Exception e)
            {
                Log.Error("[AF AutoTest] Could not write report: " + e);
            }
            if (GenCommandLine.CommandLineArgPassed("af_autotest_quit"))
            {
                Root.Shutdown();
            }
        }
    }
}
