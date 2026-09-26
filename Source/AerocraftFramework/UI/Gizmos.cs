using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MYDE_AerocraftFramework
{
    /// <summary>Ammo of every weapon of an aircraft (body and mounts) at a glance; clicking orders a reload of everything.</summary>
    [StaticConstructorOnStartup]
    public class Gizmo_AerocraftAmmo : Gizmo
    {
        private const int MaxRows = 4;
        private readonly Building_Aerocraft_AsBaseThing aircraft;

        public Gizmo_AerocraftAmmo(Building_Aerocraft_AsBaseThing aircraft)
        {
            this.aircraft = aircraft;
            Order = -99f;
        }

        public static bool HasAnyMagazine(Building_Aerocraft_AsBaseThing aircraft)
        {
            return Entries(aircraft).Any();
        }

        private static IEnumerable<(Building_Aerocraft_Base turret, Thing gun, int current, int capacity)> Entries(Building_Aerocraft_AsBaseThing aircraft)
        {
            foreach (Building_Aerocraft_Base turret in aircraft.AllTurrets)
            {
                Thing gun = turret.Gun_Now;
                if (gun != null && AerocraftCompat.Ammo.TryGetMagazine(gun, out int current, out int capacity) && capacity > 0)
                {
                    yield return (turret, gun, current, capacity);
                }
            }
        }

        public override float GetWidth(float maxWidth) => Mathf.Min(210f, maxWidth);

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Widgets.DrawWindowBackground(rect);
            Rect inner = rect.ContractedBy(5f);
            List<(Building_Aerocraft_Base turret, Thing gun, int current, int capacity)> entries = Entries(aircraft).ToList();
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 14f), "AerocraftFramework_Gizmo_Ammo".Translate());
            float rowHeight = 13f;
            float y = inner.y + 15f;
            int shown = Mathf.Min(entries.Count, MaxRows);
            for (int i = 0; i < shown; i++)
            {
                (Building_Aerocraft_Base turret, Thing gun, int current, int capacity) = entries[i];
                Rect labelRect = new Rect(inner.x, y, inner.width * 0.45f, rowHeight);
                Rect barRect = new Rect(inner.x + inner.width * 0.47f, y + 1f, inner.width * 0.53f, rowHeight - 2f);
                Widgets.Label(labelRect, gun.def.LabelCap.ToString().Truncate(labelRect.width));
                float percent = (float)current / capacity;
                Widgets.FillableBar(barRect, percent, percent < 0.25f ? MYDE_TexButton.AmmoLowBar : MYDE_TexButton.AmmoFullBar, MYDE_TexButton.EmptyBar, doBorder: false);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(barRect, current + "/" + capacity);
                Text.Anchor = TextAnchor.UpperLeft;
                y += rowHeight;
            }
            if (entries.Count > MaxRows)
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, rowHeight), "+" + (entries.Count - MaxRows));
            }
            Text.Font = GameFont.Small;
            string tip = "AerocraftFramework_Gizmo_Ammo_Tip".Translate() + "\n\n" + string.Join("\n", entries.Select(e => Building_Aerocraft_Base.GunStatusLine(e.gun)));
            TooltipHandler.TipRegion(rect, tip);
            if (Widgets.ButtonInvisible(rect))
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                OrderReloadAll(aircraft);
                return new GizmoResult(GizmoState.Interacted);
            }
            return new GizmoResult(Mouse.IsOver(rect) ? GizmoState.Mouseover : GizmoState.Clear);
        }

        /// <summary>Orders colonists to reload every weapon of the aircraft that is not full.</summary>
        public static void OrderReloadAll(Building_Aerocraft_AsBaseThing aircraft)
        {
            AcceptanceReport lastRefusal = true;
            int ordered = 0;
            foreach (Building_Aerocraft_Base turret in aircraft.AllTurrets.ToList())
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
                Messages.Message(reason, aircraft, MessageTypeDefOf.RejectInput, historical: false);
            }
        }
    }

    /// <summary>Battery charge of an electric aircraft.</summary>
    [StaticConstructorOnStartup]
    public class Gizmo_RefuelablePowerStatus : Gizmo
    {
        public CompPowerBattery CompPowerBattery;

        private static readonly Texture2D FullBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.35f, 0.35f, 0.2f));
        private static readonly Texture2D EmptyBarTex = SolidColorMaterials.NewSolidColorTexture(Color.black);

        public Gizmo_RefuelablePowerStatus()
        {
            Order = -101f;
        }

        public override float GetWidth(float maxWidth) => 140f;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect overRect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Widgets.DrawWindowBackground(overRect);
            Rect inner = overRect.ContractedBy(6f);
            Rect labelRect = inner;
            labelRect.height = overRect.height / 2f;
            Text.Font = GameFont.Tiny;
            Widgets.Label(labelRect, "PowerBatteryStored".Translate());
            Rect barRect = inner;
            barRect.yMin = inner.y + overRect.height / 2f - 6f;
            float max = CompPowerBattery?.Props.storedEnergyMax ?? 1f;
            float stored = CompPowerBattery?.StoredEnergy ?? 0f;
            Widgets.FillableBar(barRect, max > 0f ? stored / max : 0f, FullBarTex, EmptyBarTex, doBorder: false);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(barRect, stored.ToString("F0") + " / " + max.ToString("F0"));
            Text.Anchor = TextAnchor.UpperLeft;
            return new GizmoResult(GizmoState.Clear);
        }
    }

    /// <summary>Angle picker for a landed aircraft: a slider plus eight direction buttons.</summary>
    public class Dialog_Slider_Aerocraft : Window
    {
        public System.Func<int, string> textGetter;
        public int from;
        public int to;
        public float roundTo = 1f;
        private readonly System.Action<int> confirmAction;
        private int curValue;

        public override Vector2 InitialSize => new Vector2(660f, 200f);

        protected override float Margin => 10f;

        public Dialog_Slider_Aerocraft(System.Func<int, string> textGetter, int from, int to, System.Action<int> confirmAction, int startingValue = int.MinValue, float roundTo = 1f)
        {
            this.textGetter = textGetter;
            this.from = from;
            this.to = to;
            this.confirmAction = confirmAction;
            this.roundTo = roundTo;
            forcePause = true;
            closeOnClickedOutside = true;
            curValue = startingValue == int.MinValue ? from : startingValue;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            string text = textGetter(curValue);
            float height = Text.CalcHeight(text, inRect.width);
            Rect labelRect = new Rect(inRect.x, inRect.y, inRect.width - 60f, height);
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(labelRect, text);
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.DrawTextureFitted(new Rect(inRect.xMax - 40f, inRect.y, 40f, 40f), MYDE_TexButton.Right, 1f, Vector2.one, new Rect(0f, 0f, 1f, 1f), curValue);
            Rect sliderRect = new Rect(inRect.x, inRect.y + height + 10f, inRect.width - 60f, 30f);
            curValue = (int)Widgets.HorizontalSlider(sliderRect, curValue, from, to, middleAlignment: true, null, null, null, roundTo);
            (Texture2D icon, int angle)[] presets =
            {
                (MYDE_TexButton.UL, -135), (MYDE_TexButton.Up, -90), (MYDE_TexButton.UR, -45), (MYDE_TexButton.Right, 0),
                (MYDE_TexButton.DR, 45), (MYDE_TexButton.Down, 90), (MYDE_TexButton.DL, 135), (MYDE_TexButton.Left, 180)
            };
            float buttonSize = 40f;
            float x = inRect.x;
            float y = sliderRect.yMax + 8f;
            foreach ((Texture2D icon, int angle) in presets)
            {
                if (Widgets.ButtonImage(new Rect(x, y, buttonSize, buttonSize), icon, Color.white, GenUI.MouseoverColor))
                {
                    curValue = angle;
                }
                x += buttonSize + 8f;
            }
            float half = (inRect.width - 10f) / 2f;
            if (Widgets.ButtonText(new Rect(inRect.x, inRect.yMax - 30f, half, 30f), "CancelButton".Translate()))
            {
                Close();
            }
            if (Widgets.ButtonText(new Rect(inRect.x + half + 10f, inRect.yMax - 30f, half, 30f), "OK".Translate()))
            {
                Close();
                confirmAction(curValue);
            }
        }
    }
}
