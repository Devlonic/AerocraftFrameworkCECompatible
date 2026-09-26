using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MYDE_AerocraftFramework
{
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
