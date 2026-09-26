using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>Loads the save named by "-af_loadsave=Name" right after startup (used by the save check).</summary>
    [StaticConstructorOnStartup]
    public static class AFSaveCheckLoader
    {
        static AFSaveCheckLoader()
        {
            if (GenCommandLine.TryGetCommandLineArg("af_loadsave", out string saveName) && !saveName.NullOrEmpty())
            {
                LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame(saveName));
            }
        }
    }

    /// <summary>
    /// Checks a real save made with the original mod. Runs only with "-af_savecheck -af_loadsave=Name"
    /// (optionally "-af_autotest_quit" and "-af_report=file"): the aircraft must load intact, legacy nested
    /// follow targets must be dropped, colonists must be able to reload, and saving again must not grow the file.
    /// </summary>
    public class GameComponent_AFSaveCheck : GameComponent
    {
        public static readonly bool Enabled = GenCommandLine.CommandLineArgPassed("af_savecheck");

        private int step;
        private int startTick = -1;
        private int checks;
        private int failures;
        private readonly List<string> log = new List<string>();
        private readonly List<(Building_Aerocraft_Base turret, Thing gun)> watched = new List<(Building_Aerocraft_Base, Thing)>();
        private static int errorsSeen;
        private static readonly List<string> errorSamples = new List<string>();
        private static bool logHooked;

        public GameComponent_AFSaveCheck(Game game)
        {
            if (Enabled && !logHooked)
            {
                logHooked = true;
                Application.logMessageReceivedThreaded += (condition, stackTrace, type) =>
                {
                    if ((type == LogType.Error || type == LogType.Exception) && Current.ProgramState == ProgramState.Playing)
                    {
                        errorsSeen++;
                        if (errorSamples.Count < 30)
                        {
                            errorSamples.Add(condition.Length > 400 ? condition.Substring(0, 400) : condition);
                        }
                    }
                };
            }
        }

        private int Ticks => Find.TickManager.TicksGame - startTick;

        public override void GameComponentUpdate()
        {
            if (Enabled && step < 100 && Find.TickManager.CurTimeSpeed != TimeSpeed.Ultrafast && !LongEventHandler.ShouldWaitForEvent)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
            }
        }

        public override void GameComponentTick()
        {
            if (!Enabled || step >= 100)
            {
                return;
            }
            if (startTick < 0)
            {
                startTick = Find.TickManager.TicksGame;
            }
            try
            {
                Run();
            }
            catch (Exception e)
            {
                Fail("exception: " + e);
                Finish();
            }
        }

        private IEnumerable<Building_Aerocraft_Base> AllTurrets => Find.Maps.SelectMany(m => MapComponent_AerocraftTracker.For(m).Turrets).ToList();

        private void Run()
        {
            switch (step)
            {
                case 0:
                    if (Ticks < 300)
                    {
                        return;
                    }
                    List<Building_Aerocraft_Base> turrets = AllTurrets.ToList();
                    Note($"Loaded {turrets.Count} aircraft turrets: {string.Join(", ", turrets.Select(t => t.ThingID))}");
                    Check(turrets.Count > 0, "the save contains aircraft");
                    Check(turrets.Select(t => t.ThingID).Distinct().Count() == turrets.Count, "no duplicated aircraft");
                    foreach (Building_Aerocraft_Base turret in turrets)
                    {
                        Check(turret.Gun_Now != null && turret.AllGuns.All(g => AerocraftCompat.Ammo.IsLinkedTo(g, turret)), $"{turret.ThingID}: weapon {turret.Gun_Now?.def.defName} present and linked");
                        if (turret is Building_Aerocraft_AsBaseThing aircraft)
                        {
                            Check(aircraft.FollowTargetThing != aircraft, $"{turret.ThingID}: does not follow itself");
                            Note($"  {aircraft.ThingID} {aircraft.FlightStatusLabel}, pilots {aircraft.ListPawn.Count}, mounts {aircraft.AllExtraWeapon.Count}, {Building_Aerocraft_Base.GunStatusLine(aircraft.Gun_Now)}");
                        }
                    }
                    if (AerocraftCompat.Ammo.UsesAmmo)
                    {
                        foreach (Building_Aerocraft_Base turret in turrets.Where(t => t.Faction == Faction.OfPlayer))
                        {
                            Building_Aerocraft_AsBaseThing aircraft = AerocraftUtility.AircraftOf(turret);
                            if (aircraft != null && !aircraft.Is_Static)
                            {
                                aircraft.Change_Down();
                            }
                        }
                    }
                    step = 1;
                    startTick = Find.TickManager.TicksGame;
                    break;
                case 1:
                    if (!AerocraftCompat.Ammo.UsesAmmo)
                    {
                        step = 3;
                        return;
                    }
                    if (Ticks < 600)
                    {
                        return;
                    }
                    foreach (Building_Aerocraft_Base turret in AllTurrets.Where(t => t.Faction == Faction.OfPlayer && t.Spawned && t.Is_Static))
                    {
                        Thing gun = turret.Gun_Now;
                        if (gun == null || !AerocraftCompat.Ammo.TryGetMagazine(gun, out _, out int capacity) || capacity <= 0)
                        {
                            continue;
                        }
                        AerocraftCompat.Ammo.DebugSetMagazine(gun, 0);
                        ThingDef ammoDef = AerocraftCompat.Ammo.SelectedAmmoDef(gun);
                        if (ammoDef != null)
                        {
                            Thing ammo = ThingMaker.MakeThing(ammoDef);
                            ammo.stackCount = Math.Min(capacity + 5, ammoDef.stackLimit);
                            GenPlace.TryPlaceThing(ammo, turret.Position, turret.Map, ThingPlaceMode.Near);
                            ammo.SetForbidden(false, warnOnFail: false);
                        }
                        Pawn pawn = AerocraftCompat.Ammo.TryOrderReload(turret, gun, null, showMessages: false);
                        Note($"{turret.ThingID}: emptied {gun.def.defName}, reload ordered to {pawn?.ToString() ?? "nobody"}: {AerocraftCompat.Ammo.CanReloadNow(turret, gun).Reason}");
                        watched.Add((turret, gun));
                    }
                    step = 2;
                    startTick = Find.TickManager.TicksGame;
                    break;
                case 2:
                    bool done = watched.All(w => !AerocraftCompat.Ammo.NeedsReload(w.gun));
                    if (done || Ticks > 20000)
                    {
                        foreach ((Building_Aerocraft_Base turret, Thing gun) in watched)
                        {
                            AerocraftCompat.Ammo.TryGetMagazine(gun, out int current, out int capacity);
                            Check(!AerocraftCompat.Ammo.NeedsReload(gun), $"{turret.ThingID}: {gun.def.defName} reloaded by a colonist ({current}/{capacity})");
                        }
                        step = 3;
                    }
                    break;
                case 3:
                    const string resaved = "AF_SaveCheck_Resaved";
                    GameDataSaveLoader.SaveGame(resaved);
                    string text = File.ReadAllText(GenFilePaths.FilePathForSavedGame(resaved));
                    int nested = Regex.Matches(text, "<FollowTargetThing Class=").Count;
                    Check(nested == 0, $"saving again writes no nested follow targets ({nested})");
                    Note($"Resaved size: {text.Length / 1024 / 1024} MB");
                    Finish();
                    break;
            }
        }

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
            Log.Message("[AF SaveCheck] " + line);
        }

        private void Finish()
        {
            Note($"Errors logged while playing: {errorsSeen}");
            foreach (string sample in errorSamples)
            {
                Note("  error: " + sample);
            }
            Note($"DONE: {checks} checks, {failures} failures");
            step = 100;
            if (GenCommandLine.TryGetCommandLineArg("af_report", out string path))
            {
                File.WriteAllLines(path, log);
            }
            if (GenCommandLine.CommandLineArgPassed("af_autotest_quit"))
            {
                Root.Shutdown();
            }
        }
    }
}
