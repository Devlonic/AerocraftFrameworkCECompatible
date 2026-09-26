using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MYDE_AerocraftFramework
{
    /// <summary>Orders shared by the weapon gizmos: choosing a target, attacking, stopping, holding fire, reloading.</summary>
    [StaticConstructorOnStartup]
    public static class AerocraftWeaponOrders
    {
        public static readonly Texture2D AttackIcon = ContentFinder<Texture2D>.Get("UI/Commands/Attack");
        public static readonly Texture2D HaltIcon = ContentFinder<Texture2D>.Get("UI/Commands/Halt");
        public static readonly Texture2D HoldFireIcon = ContentFinder<Texture2D>.Get("UI/Commands/HoldFire");
        private static readonly Material TargetLineMat = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, new Color(1f, 0.5f, 0.5f));

        /// <summary>Anything can be attacked, the ground too (rockets at an area, a building that is not hostile).</summary>
        public static TargetingParameters TargetParams => new TargetingParameters
        {
            canTargetPawns = true,
            canTargetBuildings = true,
            canTargetItems = true,
            canTargetLocations = true,
            mapObjectTargetsMustBeAutoAttackable = false
        };

        public static bool CanFire(Building_Aerocraft_Base turret)
        {
            return turret != null && turret.Spawned && !turret.If_BreakDown && turret.AttackVerb != null;
        }

        /// <summary>The same range check as <see cref="Building_Aerocraft_Base.OrderAttack"/>, without its messages.</summary>
        public static bool InRange(Building_Aerocraft_Base turret, LocalTargetInfo target)
        {
            Verb verb = turret?.AttackVerb;
            if (verb == null || !target.IsValid)
            {
                return false;
            }
            float distance = (target.Cell - turret.Position).LengthHorizontal;
            return distance >= verb.verbProps.EffectiveMinRange(target, turret) && distance <= verb.verbProps.range;
        }

        /// <summary>An aircraft does not shoot at itself or at its own weapon mounts.</summary>
        public static bool IsOwnPart(Building_Aerocraft_Base turret, LocalTargetInfo target)
        {
            return target.Thing is Building_Aerocraft_Base other && AerocraftUtility.AircraftOf(other) == AerocraftUtility.AircraftOf(turret);
        }

        public static bool CanAttack(Building_Aerocraft_Base turret, LocalTargetInfo target)
        {
            return CanFire(turret) && InRange(turret, target) && !IsOwnPart(turret, target);
        }

        /// <summary>Lets the player click a target for all these weapons; each one that reaches it attacks it.</summary>
        public static void BeginTargeting(IEnumerable<Building_Aerocraft_Base> turrets, Texture2D mouseAttachment = null)
        {
            List<Building_Aerocraft_Base> group = turrets.Where(CanFire).Distinct().ToList();
            if (group.Count == 0)
            {
                return;
            }
            Find.Targeter.BeginTargeting(TargetParams,
                action: target => OrderAttack(group, target),
                highlightAction: target =>
                {
                    if (target.IsValid)
                    {
                        GenDraw.DrawTargetHighlight(target);
                    }
                },
                targetValidator: target => group.Any(t => CanAttack(t, target)),
                mouseAttachment: mouseAttachment ?? AttackIcon,
                onGuiAction: target =>
                {
                    string label = TargetingLabel(group, target);
                    if (!label.NullOrEmpty())
                    {
                        Widgets.MouseAttachedLabel(label);
                    }
                },
                onUpdateAction: target => DrawRanges(group, target));
        }

        private static string TargetingLabel(List<Building_Aerocraft_Base> group, LocalTargetInfo target)
        {
            if (!target.IsValid)
            {
                return null;
            }
            int reaching = group.Count(t => CanAttack(t, target));
            if (reaching == 0)
            {
                return "AerocraftFramework_Weapon_OutOfRange".Translate();
            }
            return group.Count > 1 ? "AerocraftFramework_Weapon_InRange".Translate(reaching.ToString(), group.Count.ToString()).ToString() : null;
        }

        /// <summary>Range circles of the weapons and a line from each weapon that reaches the target.</summary>
        public static void DrawRanges(IEnumerable<Building_Aerocraft_Base> turrets, LocalTargetInfo target)
        {
            foreach (Building_Aerocraft_Base turret in turrets)
            {
                Verb verb = turret.AttackVerb;
                if (!turret.Spawned || verb == null || turret.Map != Find.CurrentMap)
                {
                    continue;
                }
                Vector3 center = turret.Position.ToVector3Shifted();
                GenDraw.DrawCircleOutline(center, verb.verbProps.range);
                float minRange = verb.verbProps.EffectiveMinRange(allowAdjacentShot: true);
                if (minRange > 0.1f)
                {
                    GenDraw.DrawCircleOutline(center, minRange, SimpleColor.Red);
                }
                if (target.IsValid && CanAttack(turret, target))
                {
                    Vector3 a = turret.DrawPos;
                    Vector3 b = target.HasThing ? target.Thing.DrawPos : target.Cell.ToVector3Shifted();
                    a.y = b.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                    GenDraw.DrawLineBetween(a, b, TargetLineMat);
                }
            }
        }

        /// <summary>Every weapon that reaches the target attacks it; an explicit order lifts its hold fire.</summary>
        public static int OrderAttack(IEnumerable<Building_Aerocraft_Base> turrets, LocalTargetInfo target)
        {
            int ordered = 0;
            foreach (Building_Aerocraft_Base turret in turrets.Distinct().ToList())
            {
                if (!CanAttack(turret, target))
                {
                    continue;
                }
                if (turret.HoldFire)
                {
                    turret.SetHoldFire(false);
                }
                turret.OrderAttack(target);
                ordered++;
            }
            if (ordered == 0)
            {
                Messages.Message("AerocraftFramework_Weapon_NoneInRange".Translate(), MessageTypeDefOf.RejectInput, historical: false);
            }
            return ordered;
        }

        public static void StopAttacking(IEnumerable<Building_Aerocraft_Base> turrets)
        {
            foreach (Building_Aerocraft_Base turret in turrets)
            {
                if (turret.ForcedTarget.IsValid)
                {
                    turret.ResetForcedTarget();
                }
            }
        }

        public static void SetHoldFire(IEnumerable<Building_Aerocraft_Base> turrets, bool holdFire)
        {
            foreach (Building_Aerocraft_Base turret in turrets)
            {
                turret.SetHoldFire(holdFire);
            }
        }

        /// <summary>Orders colonists to reload every gun of these turrets that is not full.</summary>
        public static void OrderReload(IEnumerable<Building_Aerocraft_Base> turrets, Thing messageTarget)
        {
            AcceptanceReport lastRefusal = true;
            int ordered = 0;
            foreach (Building_Aerocraft_Base turret in turrets.ToList())
            {
                foreach (Thing gun in turret.AllGuns.ToList())
                {
                    if (!AerocraftCompat.Ammo.NeedsReload(gun))
                    {
                        continue;
                    }
                    AcceptanceReport canReload = AerocraftCompat.Ammo.CanReloadNow(turret, gun);
                    if (!canReload.Accepted)
                    {
                        lastRefusal = canReload;
                        continue;
                    }
                    if (AerocraftCompat.Ammo.TryOrderReload(turret, gun, null, showMessages: false) != null)
                    {
                        ordered++;
                    }
                }
            }
            if (ordered == 0)
            {
                string reason = lastRefusal.Accepted || lastRefusal.Reason.NullOrEmpty() ? "AerocraftFramework_NothingToReload".Translate().ToString() : lastRefusal.Reason;
                Messages.Message(reason, messageTarget, MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        public static bool HasMagazine(Building_Aerocraft_Base turret)
        {
            return turret.Gun_Now != null && AerocraftCompat.Ammo.TryGetMagazine(turret.Gun_Now, out _, out int capacity) && capacity > 0;
        }
    }

    /// <summary>
    /// One weapon of an aircraft, in the style of Vehicle Framework turrets: click the icon and then a target;
    /// small buttons for hold fire, fire and aim modes, ammo and switching weapons; the ammo or cooldown below.
    /// The aircraft shows one for its own gun and one for each weapon mount, so the mounts need not be selected.
    /// Identical weapons group into one (two rocket pods, or the same gun of several selected aircraft).
    /// </summary>
    [StaticConstructorOnStartup]
    public class Command_AerocraftWeapon : Command
    {
        private const float Padding = 5f;
        private const float SubSize = 28f;
        private const float SubStep = 30f;
        private const int MinColumns = 2;

        private static readonly Texture2D BarBackground = SolidColorMaterials.NewSolidColorTexture(new Color(0.18f, 0.18f, 0.18f));
        private static readonly Texture2D CooldownBar = SolidColorMaterials.NewSolidColorTexture(new Color(0.25f, 0.35f, 0.5f));
        private static readonly Texture2D CooldownShade = SolidColorMaterials.NewSolidColorTexture(new Color(0f, 0f, 0f, 0.5f));
        private static readonly Texture2D EmptyShade = SolidColorMaterials.NewSolidColorTexture(new Color(0.6f, 0.1f, 0.1f, 0.35f));
        private static readonly Color EmptyCountColor = new Color(1f, 0.45f, 0.4f);

        public readonly Building_Aerocraft_Base turret;
        private List<Building_Aerocraft_Base> group;

        private struct SubButton
        {
            public Texture icon;
            public string tooltip;
            public Action onClick;
            public bool? toggled;
        }

        public Command_AerocraftWeapon(Building_Aerocraft_Base turret)
        {
            this.turret = turret;
            group = new List<Building_Aerocraft_Base> { turret };
            Order = -96f;
            // "GSH-30-2 (30x165mm)" does not fit under the icon; the tooltip has the full name.
            string label = turret.Gun_Now?.def.LabelCap ?? turret.LabelCap;
            int bracket = label.IndexOf(" (", StringComparison.Ordinal);
            defaultLabel = bracket > 0 ? label.Substring(0, bracket) : label;
            if (turret.If_BreakDown)
            {
                Disable("AerocraftFramework_AsWeapon_BrokenDown".Translate());
            }
        }

        /// <summary>The weapons this gizmo orders: its own and those of the identical gizmos it was grouped with.</summary>
        public IReadOnlyList<Building_Aerocraft_Base> Group => group;

        private Thing Gun => turret.Gun_Now;

        public override bool GroupsWith(Gizmo other)
        {
            return other is Command_AerocraftWeapon command && !group.Contains(command.turret) && command.turret.def == turret.def && command.Gun?.def == Gun?.def;
        }

        public override void MergeWith(Gizmo other)
        {
            if (!(other is Command_AerocraftWeapon command))
            {
                return;
            }
            foreach (Building_Aerocraft_Base member in command.group)
            {
                if (!group.Contains(member))
                {
                    group.Add(member);
                }
            }
            // The grid draws the first gizmo of the group that is not disabled: every member knows the whole group.
            command.group = group;
        }

        public override bool InheritInteractionsFrom(Gizmo other) => false;

        public override bool InheritFloatMenuInteractionsFrom(Gizmo other) => false;

        public override float GetWidth(float maxWidth)
        {
            return Mathf.Min(Height + Mathf.Max(MinColumns, SubButtons().Count) * SubStep + 4f, maxWidth);
        }

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AerocraftWeaponOrders.BeginTargeting(group, IconTexture);
        }

        public override void GizmoUpdateOnMouseover()
        {
            base.GizmoUpdateOnMouseover();
            foreach (Building_Aerocraft_Base member in group)
            {
                if (member.Spawned && member.Map == Find.CurrentMap)
                {
                    GenDraw.DrawTargetHighlight(member);
                }
            }
            AerocraftWeaponOrders.DrawRanges(group, LocalTargetInfo.Invalid);
        }

        // ------------------------------------------------------------------ drawing

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            List<SubButton> buttons = SubButtons();
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), Height);
            Widgets.DrawWindowBackground(rect);
            Rect inner = rect.ContractedBy(Padding);
            Rect iconRect = new Rect(inner.x, inner.y, inner.height, inner.height);
            Rect side = new Rect(iconRect.xMax + 4f, inner.y, inner.xMax - iconRect.xMax - 4f, inner.height);
            Text.Font = GameFont.Tiny;

            for (int i = 0; i < buttons.Count; i++)
            {
                DrawSubButton(new Rect(side.x + i * SubStep, side.y, SubSize, SubSize), buttons[i]);
            }
            float barY = side.y + SubSize + 5f;
            DrawStatusBar(new Rect(side.x, barY, side.width, side.yMax - barY));

            GizmoResult result = DrawIcon(iconRect, parms);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (result.State == GizmoState.Clear && Mouse.IsOver(rect))
            {
                return new GizmoResult(GizmoState.Mouseover);
            }
            return result;
        }

        /// <summary>
        /// A weapon mount is shown by its own icon (a rocket pod, a missile rail): its gun usually has an invisible
        /// graphic, the mount drawing itself. The aircraft's own gun is shown by the gun.
        /// </summary>
        private bool UseTurretIcon
        {
            get
            {
                bool gunHasIcon = HasVisibleIcon(Gun?.def);
                return turret is Building_Aerocraft_AsWeapon ? HasVisibleIcon(turret.def) || !gunHasIcon : !gunHasIcon;
            }
        }

        /// <summary>Mounts and their guns are often drawn with the framework's invisible "Nothing" texture.</summary>
        private static bool HasVisibleIcon(ThingDef def)
        {
            if (def?.uiIcon == null || def.uiIcon == BaseContent.BadTex)
            {
                return false;
            }
            string path = def.uiIconPath.NullOrEmpty() ? def.graphicData?.texPath : def.uiIconPath;
            return path == null || !path.Contains("/Nothing");
        }

        private Texture2D IconTexture => UseTurretIcon ? turret.def.uiIcon : Gun?.def.uiIcon ?? BaseContent.BadTex;

        private GizmoResult DrawIcon(Rect iconRect, GizmoRenderParms parms)
        {
            bool mouseOver = Mouse.IsOver(iconRect);
            Material material = disabled || parms.lowLight ? TexUI.GrayscaleGUI : null;
            GUI.color = mouseOver && !disabled ? GenUI.MouseoverColor : Color.white;
            GenUI.DrawTextureWithMaterial(iconRect, BGTexture, material);
            MouseoverSounds.DoRegion(iconRect, SoundDefOf.Mouseover_Command);
            GUI.color = Color.white;
            Rect iconInner = iconRect.ContractedBy(4f);
            if (UseTurretIcon || Gun == null)
            {
                Widgets.ThingIcon(iconInner, turret.def);
            }
            else
            {
                Widgets.ThingIcon(iconInner, Gun);
            }

            // Cooldown: a shade that shrinks as the weapon gets ready. Empty magazine: a red tint.
            int cooldown = turret.CooldownTicks;
            int cooldownMax = Mathf.Max(1, turret.BurstCooldownTime().SecondsToTicks());
            if (cooldown > 0)
            {
                float shade = Mathf.Clamp01((float)cooldown / cooldownMax);
                GUI.DrawTexture(new Rect(iconRect.x, iconRect.y, iconRect.width, iconRect.height * shade), CooldownShade);
            }
            if (group.All(t => t.Gun_Now != null && !AerocraftCompat.Ammo.CanFireNow(t.Gun_Now)))
            {
                GUI.DrawTexture(iconRect, EmptyShade);
            }
            bool hotKeyPressed = false;
            if (group.Count > 1)
            {
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(iconRect.x + 3f, iconRect.y + 1f, iconRect.width, 18f), "x" + group.Count);
            }
            else if (hotKey != null && hotKey.MainKey != KeyCode.None && !GizmoGridDrawer.drawnHotKeys.Contains(hotKey.MainKey))
            {
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(iconRect.x + 3f, iconRect.y + 1f, iconRect.width, 18f), hotKey.MainKey.ToStringReadable());
                GizmoGridDrawer.drawnHotKeys.Add(hotKey.MainKey);
                if (hotKey.KeyDownEvent)
                {
                    hotKeyPressed = true;
                    Event.current.Use();
                }
            }
            if (group.Any(t => t.HoldFire))
            {
                GUI.DrawTexture(new Rect(iconRect.x + 2f, iconRect.yMax - 36f, 16f, 16f), AerocraftWeaponOrders.HoldFireIcon);
            }
            string label = LabelCap;
            if (!label.NullOrEmpty())
            {
                float height = Text.CalcHeight(label, iconRect.width);
                Rect labelRect = new Rect(iconRect.x, iconRect.yMax - height + 12f, iconRect.width, height);
                GUI.DrawTexture(labelRect, TexUI.GrayTextBG);
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(labelRect, label.Truncate(iconRect.width));
                Text.Anchor = TextAnchor.UpperLeft;
            }

            // The stop button sits on the icon; it is handled before the icon, which covers it.
            if (group.Any(t => t.ForcedTarget.IsValid))
            {
                Rect halt = new Rect(iconRect.xMax - 24f, iconRect.y + 1f, 23f, 23f);
                TooltipHandler.TipRegion(halt, "CommandStopForceAttack".Translate());
                if (Widgets.ButtonImage(halt, AerocraftWeaponOrders.HaltIcon))
                {
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    AerocraftWeaponOrders.StopAttacking(group);
                    return new GizmoResult(GizmoState.Mouseover);
                }
                if (Mouse.IsOver(halt))
                {
                    return new GizmoResult(GizmoState.Mouseover);
                }
            }

            if (mouseOver)
            {
                TipSignal tip = Tooltip();
                if (disabled && !disabledReason.NullOrEmpty())
                {
                    tip.text += ("\n\n" + "DisabledCommand".Translate() + ": " + disabledReason).Colorize(ColorLibrary.RedReadable);
                }
                TooltipHandler.TipRegion(iconRect, tip);
            }
            if (!Widgets.ButtonInvisible(iconRect, doMouseoverSound: false) && !hotKeyPressed)
            {
                return new GizmoResult(mouseOver ? GizmoState.Mouseover : GizmoState.Clear);
            }
            if (disabled)
            {
                if (!disabledReason.NullOrEmpty())
                {
                    Messages.Message(disabledReason, MessageTypeDefOf.RejectInput, historical: false);
                }
                return new GizmoResult(GizmoState.Mouseover);
            }
            return Event.current.button == 1 ? new GizmoResult(GizmoState.OpenedFloatMenu, Event.current) : new GizmoResult(GizmoState.Interacted, Event.current);
        }

        private void DrawSubButton(Rect rect, SubButton button)
        {
            bool enabled = !disabled;
            GUI.color = !enabled ? new Color(1f, 1f, 1f, 0.4f) : Mouse.IsOver(rect) ? GenUI.MouseoverColor : Color.white;
            GUI.DrawTexture(rect, BGTexture);
            if (button.icon != null)
            {
                Widgets.DrawTextureFitted(rect.ContractedBy(2f), button.icon, 1f);
            }
            if (button.toggled.HasValue)
            {
                GUI.DrawTexture(new Rect(rect.xMax - 12f, rect.y, 12f, 12f), button.toggled.Value ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex);
            }
            GUI.color = Color.white;
            TooltipHandler.TipRegion(rect, button.tooltip);
            if (Widgets.ButtonInvisible(rect))
            {
                if (!enabled)
                {
                    Messages.Message(disabledReason, MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                button.onClick();
            }
        }

        /// <summary>Rounds left (summed over the group), or the cooldown of a weapon without a magazine.</summary>
        private void DrawStatusBar(Rect bar)
        {
            int current = 0;
            int capacity = 0;
            foreach (Building_Aerocraft_Base member in group)
            {
                if (member.Gun_Now != null && AerocraftCompat.Ammo.TryGetMagazine(member.Gun_Now, out int c, out int cap) && cap > 0)
                {
                    current += c;
                    capacity += cap;
                }
            }
            Text.Anchor = TextAnchor.MiddleCenter;
            if (capacity > 0)
            {
                float percent = (float)current / capacity;
                Widgets.FillableBar(bar, percent, percent < 0.25f ? MYDE_TexButton.AmmoLowBar : MYDE_TexButton.AmmoFullBar, BarBackground, doBorder: false);
                bool reloading = group.Any(t => AerocraftCompat.Ammo.IsReloading(t));
                GUI.color = current == 0 && !reloading ? EmptyCountColor : Color.white;
                Widgets.Label(bar, reloading ? "AerocraftFramework_Reloading".Translate().ToString() : current + " / " + capacity);
            }
            else
            {
                int cooldown = turret.CooldownTicks;
                int cooldownMax = Mathf.Max(1, turret.BurstCooldownTime().SecondsToTicks());
                Widgets.FillableBar(bar, 1f - Mathf.Clamp01((float)cooldown / cooldownMax), CooldownBar, BarBackground, doBorder: false);
                Widgets.Label(bar, cooldown > 0 ? cooldown.ToStringSecondsFromTicks() : "AerocraftFramework_Weapon_Ready".Translate().ToString());
            }
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private TipSignal Tooltip()
        {
            List<string> lines = new List<string> { LabelCap.Colorize(ColoredText.TipSectionTitleColor) + (group.Count > 1 ? " x" + group.Count : "") };
            foreach (Building_Aerocraft_Base member in group)
            {
                if (member.Gun_Now != null)
                {
                    lines.Add(Building_Aerocraft_Base.GunStatusLine(member.Gun_Now) + (member.If_BreakDown ? " [" + "AerocraftFramework_AsWeapon_BrokenDown".Translate() + "]" : ""));
                }
            }
            Verb verb = turret.AttackVerb;
            if (verb != null)
            {
                lines.Add("AerocraftFramework_Weapon_Range".Translate(verb.verbProps.range.ToString("0")));
            }
            LocalTargetInfo target = group.Select(t => t.ForcedTarget).FirstOrDefault(t => t.IsValid);
            if (target.IsValid)
            {
                lines.Add("AerocraftFramework_Weapon_Target".Translate(target.HasThing ? target.Thing.LabelShort : target.Cell.ToString()));
            }
            if (group.Any(t => t.HoldFire))
            {
                lines.Add("CommandHoldFire".Translate());
            }
            if (MYDE_AerocraftFramework_Setting.If_CanFireOnlyFlying && !turret.Is_Flying)
            {
                lines.Add("AerocraftFramework_Weapon_OnlyInFlight".Translate().Colorize(new Color(1f, 0.85f, 0.4f)));
            }
            lines.Add("");
            lines.Add("AerocraftFramework_Weapon_Tip".Translate());
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------ sub buttons and menu

        private List<SubButton> SubButtons()
        {
            List<SubButton> buttons = new List<SubButton>();
            bool holdFire = group.All(t => t.HoldFire);
            buttons.Add(new SubButton
            {
                icon = AerocraftWeaponOrders.HoldFireIcon,
                tooltip = "CommandHoldFire".Translate() + "\n\n" + "CommandHoldFireDesc".Translate(),
                toggled = holdFire,
                onClick = () => AerocraftWeaponOrders.SetHoldFire(group, !holdFire)
            });
            if (Gun == null)
            {
                return buttons;
            }
            List<Command> modes = AerocraftCompat.Ammo.GetGunModeCommands(turret, Gun).ToList();
            for (int i = 0; i < modes.Count; i++)
            {
                int index = i;
                buttons.Add(new SubButton
                {
                    icon = modes[i].icon,
                    tooltip = modes[i].LabelCap + "\n\n" + modes[i].Desc,
                    onClick = () => ToggleMode(index)
                });
            }
            if (AerocraftWeaponOrders.HasMagazine(turret))
            {
                ThingDef ammo = AerocraftCompat.Ammo.SelectedAmmoDef(Gun);
                buttons.Add(new SubButton
                {
                    icon = ammo?.uiIcon ?? MYDE_TexButton.Reload,
                    tooltip = "AerocraftFramework_Weapon_AmmoTip".Translate().ToString() + (ammo == null ? "" : "\n\n" + ammo.LabelCap),
                    onClick = () => Find.WindowStack.Add(new FloatMenu(AmmoOptions().ToList()))
                });
            }
            if (turret.Gun_InnerList.Count > 0)
            {
                buttons.Add(new SubButton
                {
                    icon = turret.Gun_InnerList[0].def.uiIcon,
                    tooltip = "AerocraftFramework_Weapon_SwitchTip".Translate(),
                    onClick = () => Find.WindowStack.Add(new FloatMenu(SwitchOptions().ToList()))
                });
            }
            return buttons;
        }

        /// <summary>Fire and aim modes (Combat Extended): the same button of every weapon in the group.</summary>
        private void ToggleMode(int index)
        {
            foreach (Building_Aerocraft_Base member in group)
            {
                if (member.Gun_Now != null && AerocraftCompat.Ammo.GetGunModeCommands(member, member.Gun_Now).ElementAtOrDefault(index) is Command_Action mode)
                {
                    mode.action?.Invoke();
                }
            }
        }

        private IEnumerable<FloatMenuOption> AmmoOptions()
        {
            List<Building_Aerocraft_Base> loaded = group.Where(AerocraftWeaponOrders.HasMagazine).ToList();
            AcceptanceReport canReload = loaded.Select(t => AerocraftCompat.Ammo.CanReloadNow(t, t.Gun_Now)).FirstOrDefault(r => r.Accepted);
            if (!canReload.Accepted)
            {
                canReload = loaded.Select(t => AerocraftCompat.Ammo.CanReloadNow(t, t.Gun_Now)).FirstOrDefault();
            }
            string reloadLabel = "AerocraftFramework_Command_ReloadNow".Translate();
            yield return canReload.Accepted
                ? new FloatMenuOption(reloadLabel, () => AerocraftWeaponOrders.OrderReload(loaded, turret))
                : new FloatMenuOption(reloadLabel + (canReload.Reason.NullOrEmpty() ? "" : " (" + canReload.Reason + ")"), null);
            if (!AerocraftCompat.Ammo.CanChooseAmmo(Gun))
            {
                yield break;
            }
            ThingDef selected = AerocraftCompat.Ammo.SelectedAmmoDef(Gun);
            foreach (ThingDef ammo in AerocraftCompat.Ammo.AmmoTypes(Gun))
            {
                ThingDef chosen = ammo;
                int available = turret.Map?.listerThings.ThingsOfDef(ammo).Sum(t => t.stackCount) ?? 0;
                string label = "AerocraftFramework_Weapon_Ammo".Translate(ammo.LabelCap) + " (" + available + ")" + (ammo == selected ? " ✔" : "");
                yield return new FloatMenuOption(label, () =>
                {
                    foreach (Building_Aerocraft_Base member in loaded)
                    {
                        if (AerocraftCompat.Ammo.AmmoTypes(member.Gun_Now).Contains(chosen))
                        {
                            AerocraftCompat.Ammo.SetSelectedAmmo(member.Gun_Now, chosen);
                        }
                    }
                }, ammo);
            }
        }

        /// <summary>Weapons stored in this turret (installed by colonists or taken from pilots).</summary>
        private IEnumerable<FloatMenuOption> SwitchOptions()
        {
            foreach (Thing stored in turret.Gun_InnerList.ToList())
            {
                Thing chosen = stored;
                yield return new FloatMenuOption("AerocraftFramework_Weapon_Use".Translate(Building_Aerocraft_Base.GunStatusLine(stored)), () => turret.Change_NowWeapon_ByITab(chosen), stored.def);
            }
        }

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                yield return new FloatMenuOption("AerocraftFramework_Weapon_Attack".Translate(), () => AerocraftWeaponOrders.BeginTargeting(group, IconTexture), AerocraftWeaponOrders.AttackIcon, Color.white);
                if (group.Any(t => t.ForcedTarget.IsValid))
                {
                    yield return new FloatMenuOption("CommandStopForceAttack".Translate(), () => AerocraftWeaponOrders.StopAttacking(group), AerocraftWeaponOrders.HaltIcon, Color.white);
                }
                bool holdFire = group.All(t => t.HoldFire);
                yield return new FloatMenuOption(holdFire ? "AerocraftFramework_Weapon_FireAtWill".Translate() : "CommandHoldFire".Translate(), () => AerocraftWeaponOrders.SetHoldFire(group, !holdFire), AerocraftWeaponOrders.HoldFireIcon, Color.white);
                if (Gun != null)
                {
                    List<Command> modes = AerocraftCompat.Ammo.GetGunModeCommands(turret, Gun).ToList();
                    for (int i = 0; i < modes.Count; i++)
                    {
                        int index = i;
                        yield return new FloatMenuOption(modes[i].LabelCap, () => ToggleMode(index), modes[i].icon as Texture2D, Color.white);
                    }
                    if (AerocraftWeaponOrders.HasMagazine(turret))
                    {
                        foreach (FloatMenuOption option in AmmoOptions())
                        {
                            yield return option;
                        }
                    }
                    foreach (FloatMenuOption option in SwitchOptions())
                    {
                        yield return option;
                    }
                }
                if (turret is Building_Aerocraft_AsWeapon mount && !Find.Selector.IsSelected(mount))
                {
                    yield return new FloatMenuOption("AerocraftFramework_Weapon_SelectMount".Translate(), () =>
                    {
                        Find.Selector.ClearSelection();
                        Find.Selector.Select(mount);
                    });
                }
            }
        }
    }

    /// <summary>Every weapon of the selected aircraft attacks the clicked target (those that reach it).</summary>
    public class Command_AerocraftAttackAll : Command
    {
        private List<Building_Aerocraft_Base> turrets;

        public Command_AerocraftAttackAll(IEnumerable<Building_Aerocraft_Base> turrets)
        {
            this.turrets = turrets.ToList();
            defaultLabel = "AerocraftFramework_AttackAll_Label".Translate();
            defaultDesc = "AerocraftFramework_AttackAll_Desc".Translate();
            icon = AerocraftWeaponOrders.AttackIcon;
            hotKey = KeyBindingDefOf.Misc4;
            Order = -97f;
        }

        public override bool GroupsWith(Gizmo other) => other is Command_AerocraftAttackAll;

        public override void MergeWith(Gizmo other)
        {
            if (other is Command_AerocraftAttackAll command)
            {
                turrets.AddRange(command.turrets.Where(t => !turrets.Contains(t)));
                command.turrets = turrets;
            }
        }

        public override bool InheritInteractionsFrom(Gizmo other) => false;

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AerocraftWeaponOrders.BeginTargeting(turrets);
        }

        public override void GizmoUpdateOnMouseover()
        {
            base.GizmoUpdateOnMouseover();
            AerocraftWeaponOrders.DrawRanges(turrets, LocalTargetInfo.Invalid);
        }
    }
}
