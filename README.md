# Aerocraft Framework (CE patched), RimWorld 1.5

A patched build of [Aerocraft Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=2959802157)
by 空曜 (textures by mo) that works with Combat Extended 15.6 for RimWorld 1.5. It is a drop-in
replacement: the package id, class names, XML fields, defs and save keys are the original ones, so addons
such as **RimThunder - Gruppa Krovi** and existing saves keep working.

## Why the original does not work with Combat Extended

The original ships two copies of its code, `1.5/Assemblies` and, when CE is active, `1.5/CombatExtended/Assemblies`.
The CE copy was built against an older CE release.

1. **Reloading always failed.** Its reload job ends with `CompAmmoUser.LoadAmmo(Thing)`. CE 15.6 replaced that
   method with `LoadAmmo(Thing, bool)`. The pawn fetched the ammo, walked to the aircraft and waited for the
   reload, and then the last step threw `MissingMethodException`: the magazine stayed empty. The test
   `TheOriginalCeBuildIsBrokenAgainstInstalledCombatExtended` shows it: it is the only reference of the original
   assembly that does not resolve against the installed CE.
2. **CE did not know the aircraft.** CE asks non-CE turrets for their gun, ammo and reload state through
   `CombatExtended.Compatibility.TurretRegistry`. The original never registered, so CE's own reload paths,
   `SetReloading` and the ammo gizmos treated the aircraft as unknown turrets.
3. **Home area only.** The original reload work giver (and its refuel patch) only served aircraft inside the
   home area. Aircraft have `expandHomeArea = false`, so an aircraft built or parked outside of it was never
   reloaded or refuelled.
4. **Error spam while firing.** CE verbs keep the burst warm-up in `Building_TurretGunCE`. For the aircraft,
   every aimed shot logged `Verb caster is not a turret and does not have a WarmupStance`.
5. **No bombs under CE.** CE redefines the vanilla mortar shells as `AmmoDef` without `projectileWhenLoaded`,
   so the bomber's shell search (`ThingRequestGroup.Shell`) found nothing.

Other bugs of the original that show up in normal play:

- `FollowTargetThing` was deep-saved. An aircraft ordered to follow itself (a right click on its own cell)
  wrote a full copy of itself into the save, recursively, at every save: a 125 MB save with 1500 nested copies
  and `deep-saved twice` errors. It is now a reference, and legacy nested nodes are skipped on load.
- Letting a pawn out never removed it from the aircraft; destroying an aircraft spawned the same pawn again.
- Deconstruction refunded nothing (the destroy mode was ignored).
- Switching weapons could lose the original weapon, or leave the turret with none.
- Extra weapon mounts were orphaned when the aircraft was destroyed or uninstalled.
- The angle dialog logged a message every frame; weapon kits logged errors when built.

## What changed

- One core assembly without any CE reference, plus `AerocraftFramework.CE.dll` loaded only with CE. The CE
  module plugs into `AerocraftAmmoCompat`: magazines, reloading, CE projectiles and explosions.
- **Reloading** (CE): haulers reload the current gun, the weapons stored on board and every weapon mount of
  landed aircraft. The job unloads the old ammo type, carries the right amount and puts leftovers into other
  guns. CE methods whose signatures changed between releases are bound by reflection.
- **Orders**: a *Reload now* gizmo, reload and ammo type buttons in the aircraft tab, and right-click
  *Reload X with Y* options for a selected colonist.
- **Interface**: an aircraft tab with flight status, fuel, crew, weapons with ammo bars, weapon mounts and
  bombs; an ammo gizmo for all weapons of the aircraft (click: reload everything); inspect lines with ammo and
  flight status; *Everyone out*; *Load bombs*; a clearer settings page; Ukrainian translation.
- **Bombs** accept CE mortar shells; bombs and fire foam shells fly as CE projectiles; weapon mounts no longer
  hit their own aircraft.
- Pilots count as colonists (no game over while everyone is in the air).

## Installation

Put the mod folder into `RimWorld\Mods` (or link it, see below) and keep `Aerocraft Framework` enabled in the
mod list. When the workshop original is also installed, RimWorld gives this local copy the id
`MYDE.AerocraftFramework` and renames the workshop copy to `MYDE.AerocraftFramework_steam`; only this one is
used. To go back to the original, remove the folder.

Load order: Harmony, Combat Extended, Vehicle Framework / RimThunder Core, this mod, its addons.

## Saves damaged by the original

Saves made with the original keep loading, and nested follow targets are dropped on load. But when an aircraft
had followed itself for many saves, the original eventually failed half way through writing: such a file ends
in the middle of hundreds of nested `<FollowTargetThing>` elements and RimWorld cannot open it at all
("Unexpected end of file"). `tools\Repair-Save.ps1` writes a loadable copy: it drops the nested copies and closes
the file where it was cut. Everything saved before the damaged aircraft is kept; everything the game would have
written after it (the rest of that map's things, often including colonists that arrived later) was never
written and is lost. Prefer an older save when you have one; the repair only salvages what is there.

```
tools\Repair-Save.ps1 -Path <Saves>\testreload.rws -Output <Saves>\testreload_repaired.rws
```

## Settings

- *Colonists reload aircraft automatically* (CE, on).
- *Also reload weapons stored on board* (on).
- *Refuel and reload only inside the home area* (off; the original behaviour when on).
- Flight, shadows and debug options of the original.

## Development

```
Source/AerocraftFramework/AerocraftFramework.csproj         # core, net472, Krafs.Rimworld.Ref 1.5.4409, Lib.Harmony 2.3.3
Source/AerocraftFramework.CE/AerocraftFramework.CE.csproj   # CE module, references RimWorld\Mods\CombatExtended
Source/AerocraftFramework.Tests                             # offline tests (xUnit, Mono.Cecil)
dotnet build -c Release                                     # in each project folder; outputs to 1.5/Assemblies and 1.5/CombatExtended/Assemblies
```

The CE module is built against the Combat Extended found at
`$(RimWorldDir)\Mods\CombatExtended\Assemblies\CombatExtended.dll`; override `RimWorldDir` or `CombatExtendedDll`
with `-p:`. The game locks the DLLs while running; close it before rebuilding.

The mod folder is linked into the game with a directory junction:
`RimWorld\Mods\AerocraftFrameworkCE -> D:\Development\AerocraftFrameworkCECompatible`.

### Offline tests

`dotnet test Source/AerocraftFramework.Tests` checks that:

- every type and member reference of both assemblies resolves against the installed game, Harmony and CE
  (the check that catches a changed CE API);
- the original CE build is indeed broken against the installed CE;
- the public API of the original (types, public fields, methods) is kept;
- every class and CompProperties field used by our XML and by every installed addon that depends on
  `MYDE.AerocraftFramework` exists, and every def and parent def of the original is still there;
- translation keys used in code exist in English, Ukrainian has every English key with the same arguments,
  and DefInjected entries target existing defs.

Paths default to the local install; set `RIMWORLD_DIR`, `WORKSHOP_DIR` or `CE_DLL` to change them.

### In-game self test

```
tools\Run-AutoTest.ps1 -DataDir C:\Temp\af-test-ce
tools\Run-AutoTest.ps1 -DataDir C:\Temp\af-test-vanilla -NoCombatExtended
```

It uses its own data folder (your config and saves are not touched), starts a quick test game with Harmony, the
DLCs, CE (optional), Vehicle Framework, RimThunder Core, this mod and Gruppa Krovi, turns off random incidents
(a raid on the test map downs the colonists it needs), and checks:

- every aircraft def (framework and addons) spawns with its weapon, mounts and links;
- colonists reload every empty gun automatically; a manual order switches the ammo type;
- a helicopter with pilots takes off, cannot be reloaded in the air, shoots and hits an enemy, flies, lands,
  refuses to follow itself;
- colonists load bombs, a bomb is dropped in flight, the support aircraft fires a fire foam shell;
- boarding by job, a save/load round trip and the load of a legacy save with a nested follow target;
- a flight to an enemy settlement and back with pilots and mounts;
- the settings window, the aircraft tab and the dialogs draw without errors;
- deconstruction and destruction let the pilots out.

It writes a PASS/FAIL report and counts every error logged during the run.

`tools\Run-SaveCheck.ps1 -Save <name> -DataDir C:\Temp\af-savecheck` loads one of your saves with a copy of your
mod list and checks its aircraft: loading, legacy follow targets, reloading by colonists and the size of a new save.

### Dev-mode tools

Debug actions, category *Aerocraft Framework*: fill or empty all magazines, log the state of every aircraft.

## Credits

Aerocraft Framework: 空曜 (code, XML), mo (textures), 3HST (CE), Ancot and 灵能罐头 (texts).
Combat Extended compatibility patch: Devloner.
