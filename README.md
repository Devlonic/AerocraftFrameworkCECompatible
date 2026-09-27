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
- Pawns on board were kept in a plain list, so the game did not know they were on the map. They vanished from
  the colonist bar, and a map with only aircraft crews left was closed with the aircraft and its crew on it: a
  town that capitulated to Occupation & Annexation, a defeated settlement, a caravan battlefield. The original
  only kept enemy settlement and site maps open. The aircraft now holds its crew like a cryptosleep casket holds its
  sleeper (the save format is unchanged), and no map is closed while a player aircraft is on it.
- Deconstruction refunded nothing (the destroy mode was ignored).
- Switching weapons could lose the original weapon, or leave the turret with none.
- Extra weapon mounts were orphaned when the aircraft was destroyed or uninstalled.
- The angle dialog logged a message every frame; weapon kits logged errors when built.
- With CE a flying aircraft fired from the height of a building (about a metre), so its shots flew almost level:
  a rocket that missed went on to the end of its range. It now fires from flight altitude and misses come down
  around the target.
- Any EMP or stun damage landed a flying aircraft, including the EMP that charged "ion" bullets of CE carry. Now
  the aircraft stays up with its weapons silent while stunned; landing is a setting.
- Flying to another tile despawned the aircraft on the spot and played a drop pod's shrinking take-off, and it
  appeared in a map corner at the other end. Now it flies off the map edge towards its destination, climbing (drawn
  larger) as it goes, and comes in over the edge on the side it flew from, as the aircraft of Vehicle Framework do.
  The fuel is paid when it leaves the map; another order before that cancels the departure.

## What changed

- One core assembly without any CE reference, plus `AerocraftFramework.CE.dll` loaded only with CE. The CE
  module plugs into `AerocraftAmmoCompat`: magazines, reloading, CE projectiles and explosions.
- **Reloading** (CE): haulers reload the current gun, the weapons stored on board and every weapon mount of
  landed aircraft. The job unloads the old ammo type, carries the right amount and puts leftovers into other
  guns. CE methods whose signatures changed between releases are bound by reflection.
- **Weapon control**, in the style of Vehicle Framework turrets. The aircraft shows a gizmo for each weapon, its
  own gun and every mount, so the mounts never need to be selected. Click the icon, then a target: the targeter
  draws the ranges and refuses targets out of reach. The stop button sits on the icon; small buttons toggle hold
  fire, CE fire and aim modes, the ammo type and reload, and weapons stored on board; the ammo (or the cooldown)
  is shown below; right click lists every order. Identical weapons group into one gizmo (two rocket pods, or the
  same gun of several selected aircraft), so one click aims them all. *Attack with all weapons* (the old attack
  hotkey) aims every weapon that reaches the target; *Stop attacking*, *Hold fire (all)* and *Reload all* cover
  the whole aircraft. A click on an aircraft selects the aircraft rather than the mount drawn over it; a second
  click selects the mount.
- **Orders**: reload and ammo type buttons in the aircraft tab, and right-click *Reload X with Y* options for a
  selected colonist.
- **Interface**: an aircraft tab with flight status, fuel, crew, weapons with ammo bars, weapon mounts and
  bombs; inspect lines with ammo and flight status; *Everyone out*; *Load bombs*; a clearer settings page;
  Ukrainian translation.
- **Bombs** accept CE mortar shells; bombs and fire foam shells fly as CE projectiles; weapon mounts no longer
  hit their own aircraft.
- **Bombing run**: click a target and the aircraft flies over it and drops a stick of bombs along its approach;
  shift-click the start of a line and click its end to bomb evenly along the line (a fixed-wing aircraft lines up
  before the line first). The targeter shows where each bomb falls and its blast radius; right click sets the bombs
  per run; every selected bomber makes the run. Another order, landing or running out of bombs ends it.
- **Strafing run**: the same targeting (a target, or shift-click a line); every weapon that reaches fires at a
  point walking along the line, warming up on its start while the aircraft lines up. Several selected aircraft
  strafe parallel lines.
- **Troop drop**: a helicopter (an aircraft that takes off vertically) flies to the clicked spot, hovers, and
  everyone aboard but its crew ropes down one after another, drafted; downed pawns stay aboard. Several selected
  helicopters drop around the spot. The tab also lets single pawns rope down while it hovers.
- **Medevac**: *Evacuate the wounded* sends the closest colonists to carry the downed colonists, slaves, prisoners
  and guests around a landed aircraft aboard (right click the aircraft with a colonist selected to carry one). On
  board they do not bleed or worsen; a downed pawn is not counted as a pilot.
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
- *EMP and stun force an aircraft to land* (off; the original behaviour when on).
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
and quests and removes other hostiles from the home map (a raid downs the colonists the test needs), and checks:

- every aircraft def (framework and addons) spawns with its weapon, mounts and links;
- colonists reload every empty gun automatically; a manual order switches the ammo type;
- a helicopter with pilots takes off, cannot be reloaded in the air, shoots and hits an enemy, flies, lands,
  refuses to follow itself;
- colonists load bombs, a bomb is dropped in flight, bombing runs on a point and along a line drop their bombs
  over the targets, the support aircraft fires a fire foam shell;
- boarding by job, a save/load round trip and the load of a legacy save with a nested follow target;
- a flight to an enemy settlement and back with pilots and mounts, off the map edge and in over it; the crew counts as colonists there, and the
  map stays open when the settlement is defeated with only the crew on it;
- medevac, troop drop and strafing with a helicopter (the Mi-24 with Gruppa Krovi): a colonist carries a downed
  one aboard, who does not count as a pilot; the troops rope down at the drop point, drafted, while the crew and
  the wounded stay aboard; rockets of the mounts fired at a point come down around it; the weapons aim along the
  strafing line, fire, and let go of it after the run;
- the weapon orders: every weapon that reaches an enemy attacks it at once and its hold fire is lifted; the
  aircraft shows a weapon gizmo for each weapon, with a right click menu; a click selects the aircraft first;
- the settings window, the aircraft tab, the weapon gizmos, the targeter and the dialogs draw without errors
  (screenshots are saved next to the report);
- deconstruction and destruction let the pilots out.

It writes a PASS/FAIL report and counts every error logged during the run.

`tools\Run-SaveCheck.ps1 -Save <name> -DataDir C:\Temp\af-savecheck` loads one of your saves with a copy of your
mod list and checks its aircraft: loading, legacy follow targets, reloading by colonists and the size of a new save.

### Dev-mode tools

Debug actions, category *Aerocraft Framework*: fill or empty all magazines, log the state of every aircraft.

## Credits

Aerocraft Framework: 空曜 (code, XML), mo (textures), 3HST (CE), Ancot and 灵能罐头 (texts).
Combat Extended compatibility patch: Devloner.
