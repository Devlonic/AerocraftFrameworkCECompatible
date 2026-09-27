using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// Aircraft tab: status, crew, weapons with their ammo (reload and ammo type buttons), extra weapon mounts and bombs.
    /// </summary>
    public class ITab_Aerocraft_Weapon : ITab
    {
        private const float RowHeight = 28f;
        private const float IconSize = 24f;
        private const float BarWidth = 130f;
        private const float MinHeight = 300f;

        public static readonly Color ThingLabelColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        public static readonly Color HighlightColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        private Vector2 scrollPosition = Vector2.zero;
        private float scrollViewHeight;
        private static readonly List<Thing> tmpThings = new List<Thing>();

        public ITab_Aerocraft_Weapon()
        {
            size = new Vector2(560f, 480f);
            labelKey = "AerocraftFramework_ITab";
            tutorTag = "AerocraftFramework_ITab";
        }

        public override bool IsVisible => SelTurret != null && AerocraftUtility.IsControllable(SelTurret);

        private Building_Aerocraft_Base SelTurret => SelThing as Building_Aerocraft_Base;

        private bool CanControl => AerocraftUtility.IsControllable(SelTurret);

        protected override void FillTab()
        {
            Building_Aerocraft_Base turret = SelTurret;
            if (turret == null)
            {
                return;
            }
            Text.Font = GameFont.Small;
            Rect outRect = new Rect(0f, 20f, size.x, size.y - 20f).ContractedBy(10f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, scrollViewHeight);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float y = 0f;
            float width = viewRect.width;
            Building_Aerocraft_AsBaseThing aircraft = AerocraftUtility.AircraftOf(turret);

            if (aircraft != null)
            {
                DrawStatus(ref y, width, aircraft);
            }
            if (turret is Building_Aerocraft_AsBaseThing body && body.ListPawn.Count > 0)
            {
                Widgets.ListSeparator(ref y, width, "AerocraftFramework_ITab_InnerPawn".Translate() + " (" + body.ListPawn.Count + "/" + body.CarryPawnNumMax + ")");
                foreach (Pawn pawn in body.ListPawn.ToList())
                {
                    DrawPawnRow(ref y, width, body, pawn);
                }
            }
            if (turret.Gun_Now != null)
            {
                Widgets.ListSeparator(ref y, width, "AerocraftFramework_ITab_CurrentWeapon".Translate());
                DrawWeaponRow(ref y, width, turret, turret.Gun_Now, isCurrent: true);
            }
            if (turret.Gun_InnerList.Count > 0)
            {
                Widgets.ListSeparator(ref y, width, "AerocraftFramework_ITab_InnerWeapon".Translate());
                tmpThings.Clear();
                tmpThings.AddRange(turret.Gun_InnerList);
                foreach (Thing gun in tmpThings)
                {
                    DrawWeaponRow(ref y, width, turret, gun, isCurrent: false);
                }
                tmpThings.Clear();
            }
            if (turret is Building_Aerocraft_AsBaseThing withMounts && withMounts.AllExtraWeapon.Count > 0)
            {
                Widgets.ListSeparator(ref y, width, "AerocraftFramework_ITab_ExtraWeapon".Translate());
                foreach (Building_Aerocraft_Base mount in withMounts.AllExtraWeapon.OfType<Building_Aerocraft_Base>().ToList())
                {
                    DrawMountRow(ref y, width, mount);
                }
            }
            Comp_CanLoadShell bay = turret.TryGetComp<Comp_CanLoadShell>();
            if (bay != null)
            {
                DrawShells(ref y, width, bay);
            }
            if (Event.current.type == EventType.Layout)
            {
                size.y = Mathf.Clamp(y + 70f, MinHeight, UI.screenHeight - 35f - 165f - 30f);
                scrollViewHeight = y + 20f;
            }
            Widgets.EndScrollView();
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // ------------------------------------------------------------------ status

        private void DrawStatus(ref float y, float width, Building_Aerocraft_AsBaseThing aircraft)
        {
            Rect rect = new Rect(0f, y, width, RowHeight);
            Text.Anchor = TextAnchor.MiddleLeft;
            string status = aircraft.LabelCap + ": " + aircraft.FlightStatusLabel;
            if (aircraft.If_NeedPawnToControl)
            {
                status += " · " + "AerocraftFramework_PilotsNeeded".Translate(aircraft.NeedPawnToControl_Number);
            }
            Widgets.Label(rect.LeftPart(0.62f), status.Truncate(rect.width * 0.62f));
            CompRefuelable refuelable = aircraft.RefuelableComp;
            if (refuelable != null)
            {
                Rect bar = new Rect(rect.xMax - BarWidth - 4f, y + 2f, BarWidth, RowHeight - 4f);
                DrawBar(bar, refuelable.FuelPercentOfMax, refuelable.Fuel.ToString("0") + " / " + refuelable.Props.fuelCapacity.ToString("0"), MYDE_TexButton.FullBar);
                TooltipHandler.TipRegion(bar, refuelable.Props.FuelGizmoLabel);
            }
            else if (aircraft.CompPowerBattery != null)
            {
                Rect bar = new Rect(rect.xMax - BarWidth - 4f, y + 2f, BarWidth, RowHeight - 4f);
                CompPowerBattery battery = aircraft.CompPowerBattery;
                DrawBar(bar, battery.StoredEnergyPct, battery.StoredEnergy.ToString("0") + " / " + battery.Props.storedEnergyMax.ToString("0"), MYDE_TexButton.FullBar);
                TooltipHandler.TipRegion(bar, "PowerBatteryStored".Translate());
            }
            Text.Anchor = TextAnchor.UpperLeft;
            y += RowHeight;
            if (aircraft.Is_Static || !AerocraftCompat.Ammo.UsesAmmo)
            {
                return;
            }
            Rect hint = new Rect(0f, y, width, 22f);
            GUI.color = ColoredText.SubtleGrayColor;
            Text.Font = GameFont.Tiny;
            Widgets.Label(hint, "AerocraftFramework_ITab_LandToReload".Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            y += 22f;
        }

        private static void DrawBar(Rect rect, float fillPercent, string label, Texture2D fillTex)
        {
            Widgets.FillableBar(rect, Mathf.Clamp01(fillPercent), fillTex, MYDE_TexButton.EmptyBar, doBorder: true);
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            Widgets.Label(rect, label);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
        }

        // ------------------------------------------------------------------ rows

        /// <summary>Draws the icon and label of a row and returns the rect left for buttons (right side).</summary>
        private static void DrawThingLabel(Rect rowRect, Thing thing, Thing iconThing, string label, float reservedRight)
        {
            Rect labelArea = new Rect(rowRect.x, rowRect.y, rowRect.width - reservedRight, rowRect.height);
            if (Mouse.IsOver(labelArea))
            {
                GUI.color = HighlightColor;
                GUI.DrawTexture(labelArea, TexUI.HighlightTex);
                GUI.color = Color.white;
            }
            if (iconThing != null)
            {
                Widgets.ThingIcon(new Rect(4f, rowRect.y + 2f, IconSize, IconSize), iconThing);
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = ThingLabelColor;
            Rect labelRect = new Rect(36f, rowRect.y, labelArea.width - 36f, rowRect.height);
            Text.WordWrap = false;
            Widgets.Label(labelRect, label.Truncate(labelRect.width));
            Text.WordWrap = true;
            GUI.color = Color.white;
            if (Mouse.IsOver(labelArea) && thing != null)
            {
                string tip = thing.LabelNoParenthesisCap.AsTipTitle() + GenLabel.LabelExtras(thing, includeHp: true, includeQuality: true) + "\n\n" + thing.DescriptionDetailed;
                TooltipHandler.TipRegion(labelArea, tip);
            }
        }

        /// <summary>A small icon button laid out from the right edge; returns true when clicked.</summary>
        private static bool RowButton(ref float x, float y, Texture2D icon, string tip, bool enabled = true, string disabledReason = null)
        {
            x -= IconSize + 4f;
            Rect rect = new Rect(x, y + 2f, IconSize, IconSize);
            if (!enabled)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                GUI.DrawTexture(rect, icon);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(rect, tip + (disabledReason.NullOrEmpty() ? "" : "\n\n" + disabledReason.Colorize(ColorLibrary.RedReadable)));
                return false;
            }
            TooltipHandler.TipRegion(rect, tip);
            if (Widgets.ButtonImage(rect, icon, Color.white, GenUI.MouseoverColor))
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                return true;
            }
            return false;
        }

        private void DrawPawnRow(ref float y, float width, Building_Aerocraft_AsBaseThing aircraft, Pawn pawn)
        {
            Rect row = new Rect(0f, y, width, RowHeight);
            float x = width;
            Widgets.InfoCardButton(x - IconSize, y + 2f, pawn);
            x -= IconSize;
            if (CanControl)
            {
                AcceptanceReport canLeave = CanLeave(aircraft);
                if (canLeave.Accepted && !aircraft.Is_Static && pawn.Downed)
                {
                    canLeave = "AerocraftFramework_TroopDrop_Downed".Translate();
                }
                if (RowButton(ref x, y, MYDE_TexButton.Down, "AerocraftFramework_ITab_Pawn_Drop".Translate(), canLeave.Accepted, canLeave.Reason))
                {
                    aircraft.ReleasePawn(pawn, drafted: false);
                }
                if (RowButton(ref x, y, MYDE_TexButton.DownByDraft, "AerocraftFramework_ITab_Pawn_DropDrafted".Translate(), canLeave.Accepted, canLeave.Reason))
                {
                    aircraft.ReleasePawn(pawn, drafted: true);
                }
            }
            string label = pawn.LabelCap;
            if (pawn.Downed)
            {
                label += " (" + "Downed".Translate() + ")";
            }
            DrawThingLabel(row, pawn, pawn, label, width - x + 4f);
            y += RowHeight;
        }

        private static AcceptanceReport CanLeave(Building_Aerocraft_AsBaseThing aircraft)
        {
            if (!aircraft.Spawned)
            {
                return false;
            }
            if (!aircraft.Is_Static)
            {
                // A hovering helicopter lets pawns rope down.
                AcceptanceReport canRopeDown = aircraft.CanRopeDown;
                return canRopeDown.Accepted ? canRopeDown : (AcceptanceReport)"AerocraftFramework_AerocraftIsNotStatic".Translate();
            }
            return true;
        }

        private void DrawWeaponRow(ref float y, float width, Building_Aerocraft_Base turret, Thing gun, bool isCurrent)
        {
            Rect row = new Rect(0f, y, width, RowHeight);
            float x = width;
            Widgets.InfoCardButton(x - IconSize, y + 2f, gun);
            x -= IconSize;
            if (CanControl)
            {
                if (!isCurrent)
                {
                    if (RowButton(ref x, y, MYDE_TexButton.Down, "AerocraftFramework_ITab_Drop".Translate(), turret.Is_Static, "AerocraftFramework_AerocraftIsNotStatic".Translate()))
                    {
                        turret.TryDropWeapon(gun);
                    }
                    if (RowButton(ref x, y, MYDE_TexButton.Up, "AerocraftFramework_ITab_Replace".Translate()))
                    {
                        turret.Change_NowWeapon_ByITab(gun);
                    }
                }
                else if (turret.OriginWeaponDef != null && gun.def != turret.OriginWeaponDef)
                {
                    if (RowButton(ref x, y, MYDE_TexButton.Down, "AerocraftFramework_ITab_ReplaceToOrigin".Translate()))
                    {
                        turret.Change_NowWeapon_ToOrigin();
                    }
                }
                DrawAmmoControls(ref x, y, turret, gun);
            }
            DrawThingLabel(row, gun, gun, gun.LabelCapNoCount, width - x + 4f);
            y += RowHeight;
        }

        /// <summary>Ammo bar, ammo type and reload buttons (only for guns with a magazine).</summary>
        private static void DrawAmmoControls(ref float x, float y, Building_Aerocraft_Base turret, Thing gun)
        {
            if (!AerocraftCompat.Ammo.TryGetMagazine(gun, out int current, out int capacity) || capacity <= 0)
            {
                return;
            }
            AcceptanceReport canReload = AerocraftCompat.Ammo.CanReloadNow(turret, gun);
            if (RowButton(ref x, y, MYDE_TexButton.Reload, "AerocraftFramework_ITab_Reload".Translate(), canReload.Accepted, canReload.Reason))
            {
                AerocraftCompat.Ammo.TryOrderReload(turret, gun);
            }
            if (AerocraftCompat.Ammo.CanChooseAmmo(gun))
            {
                ThingDef selected = AerocraftCompat.Ammo.SelectedAmmoDef(gun);
                if (RowButton(ref x, y, selected?.uiIcon ?? MYDE_TexButton.Reload, "AerocraftFramework_ITab_ChooseAmmo".Translate().ToString() + (selected == null ? "" : ": " + selected.LabelCap.ToString())))
                {
                    AerocraftCompat.Ammo.OpenAmmoMenu(turret, gun);
                }
            }
            x -= BarWidth + 6f;
            Rect bar = new Rect(x, y + 3f, BarWidth, RowHeight - 6f);
            float percent = (float)current / capacity;
            DrawBar(bar, percent, current + " / " + capacity, percent < 0.25f ? MYDE_TexButton.AmmoLowBar : MYDE_TexButton.AmmoFullBar);
            string ammoLabel = AerocraftCompat.Ammo.CurrentAmmoLabel(gun);
            if (!ammoLabel.NullOrEmpty())
            {
                TooltipHandler.TipRegion(bar, ammoLabel);
            }
        }

        private void DrawMountRow(ref float y, float width, Building_Aerocraft_Base mount)
        {
            Rect row = new Rect(0f, y, width, RowHeight);
            float x = width;
            if (mount.Gun_Now != null)
            {
                Widgets.InfoCardButton(x - IconSize, y + 2f, mount.Gun_Now);
            }
            x -= IconSize;
            if (mount.Spawned && RowButton(ref x, y, MYDE_TexButton.True, "AerocraftFramework_ITab_ExtraWeapon_Select".Translate()))
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(mount);
            }
            Comp_ReplaceCurrentWeapon replace = mount.TryGetComp<Comp_ReplaceCurrentWeapon>();
            if (CanControl && mount.Spawned && replace != null && replace.Props.If_CanShowGizmosToReplace && RowButton(ref x, y, MYDE_TexButton.SelectPawnAndLetItReplace, "AerocraftFramework_SelectPawnAndLetItReplace_Desc".Translate()))
            {
                replace.DoSomething_SelectPawnAndLetItReplace();
            }
            if (CanControl && mount.Gun_Now != null)
            {
                DrawAmmoControls(ref x, y, mount, mount.Gun_Now);
            }
            string label = mount.Gun_Now != null ? mount.Gun_Now.LabelCapNoCount : mount.LabelCap;
            if (mount.If_BreakDown)
            {
                label += " (" + "AerocraftFramework_AsWeapon_BrokenDown".Translate() + ")";
            }
            DrawThingLabel(row, mount, mount.Gun_Now ?? (Thing)mount, label, width - x + 4f);
            y += RowHeight;
        }

        private void DrawShells(ref float y, float width, Comp_CanLoadShell bay)
        {
            Widgets.ListSeparator(ref y, width, "AerocraftFramework_ITab_Shells".Translate(bay.TotalShells, bay.Props.LoadShell_Max));
            if (bay.CurrentShell != null)
            {
                DrawShellRow(ref y, width, bay, bay.CurrentShell, 1, isCurrent: true);
            }
            foreach (Thing stack in bay.ListShell.ToList())
            {
                DrawShellRow(ref y, width, bay, stack, stack.stackCount, isCurrent: false);
            }
        }

        private void DrawShellRow(ref float y, float width, Comp_CanLoadShell bay, Thing shell, int count, bool isCurrent)
        {
            Rect row = new Rect(0f, y, width, RowHeight);
            float x = width;
            Widgets.InfoCardButton(x - IconSize, y + 2f, shell.def);
            x -= IconSize;
            if (CanControl)
            {
                bool landed = (bay.parent as Building_Aerocraft_AsBaseThing)?.Is_Static ?? false;
                if (RowButton(ref x, y, MYDE_TexButton.Down, "AerocraftFramework_ITab_Shell_Drop".Translate(), landed, "AerocraftFramework_AerocraftIsNotStatic".Translate()))
                {
                    bay.DropShell(shell.def);
                }
                if (!isCurrent && RowButton(ref x, y, MYDE_TexButton.Up, "AerocraftFramework_ITab_Shell_Replace".Translate()))
                {
                    bay.SetCurrentShell(shell.def);
                }
            }
            string label = shell.def.LabelCap + " x" + count + (isCurrent ? " (" + "AerocraftFramework_ITab_CurrentShell".Translate() + ")" : "");
            DrawThingLabel(row, shell, shell, label, width - x + 4f);
            y += RowHeight;
        }
    }
}
