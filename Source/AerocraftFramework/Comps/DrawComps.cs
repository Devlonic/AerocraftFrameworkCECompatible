using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>Draws the shadow of the aircraft body.</summary>
    [StaticConstructorOnStartup]
    public class Comp_Base_Thing : ThingComp
    {
        public CompProperties_Base_Thing Props => props as CompProperties_Base_Thing;

        private Building_Aerocraft_Base Building_Aerocraft_Base => parent as Building_Aerocraft_Base;

        public override void PostDraw()
        {
            base.PostDraw();
            if (Props.If_DrawShadow_Base && Props.If_Draw_Base && Building_Aerocraft_Base != null)
            {
                Draw_ShadowBase_Base(Building_Aerocraft_Base);
            }
        }

        public void Draw_ShadowBase_Base(Building_Aerocraft_Base turret)
        {
            if (!MYDE_AerocraftFramework_Setting.If_DrawShadow || Props.Draw_Base_TexturePath.NullOrEmpty())
            {
                return;
            }
            Vector3 pos = turret.Shadow_Pos;
            pos.y = turret.DrawPos.y + Props.Draw_Shadow_Base_ExtraAltitudeLayerNum;
            float scale = Props.Draw_Shadow_Base_Scale * turret.Draw_ScaleFactorNow;
            Matrix4x4 matrix = default;
            matrix.SetTRS(pos, Quaternion.AngleAxis(turret.Angle_Fly_Now, Vector3.up), new Vector3(scale, 0f, scale));
            Color color = new Color(0f, 0f, 0f, Props.Draw_Shadow_Base_Transparency * turret.Shadow_Transparency);
            Graphics.DrawMesh(MeshPool.plane10, matrix, MaterialPool.MatFrom(Props.Draw_Base_TexturePath, ShaderDatabase.Transparent, color), 0);
        }
    }

    /// <summary>Draws the shadow of the turret gun.</summary>
    [StaticConstructorOnStartup]
    public class Comp_Base_Weapon : ThingComp
    {
        public CompProperties_Base_Weapon Props => props as CompProperties_Base_Weapon;

        private Building_Aerocraft_Base Building_Aerocraft_Base => parent as Building_Aerocraft_Base;

        public override void PostDraw()
        {
            base.PostDraw();
            if (Props.If_DrawShadow_Gun && Props.If_Draw_Gun && Building_Aerocraft_Base != null)
            {
                Draw_ShadowBase_Gun(Building_Aerocraft_Base);
            }
        }

        public void Draw_ShadowBase_Gun(Building_Aerocraft_Base turret)
        {
            if (!MYDE_AerocraftFramework_Setting.If_DrawShadow)
            {
                return;
            }
            float scale = Props.Draw_Shadow_Gun_Scale * turret.Draw_ScaleFactorNow;
            float altitude = turret.DrawPos.y + Props.Draw_Shadow_Gun_ExtraAltitudeLayerNum;
            Color color = new Color(0f, 0f, 0f, Props.Draw_Shadow_Gun_Transparency * turret.Shadow_Transparency);
            turret.Top.DrawTurret(turret.Shadow_Pos, scale, altitude, color);
        }
    }

    /// <summary>Propellers, rotors and other decorations drawn over the aircraft, with their shadows.</summary>
    [StaticConstructorOnStartup]
    public class Comp_BaseDraw_DecorationAndShadow : ThingComp
    {
        public float SecondTextureTransparency;
        public float RotateAngleSpeedNow;
        private float RotateAngle;

        public CompProperties_BaseDraw_DecorationAndShadow Props => props as CompProperties_BaseDraw_DecorationAndShadow;

        private Building_Aerocraft_AsBaseThing Building_Aerocraft_AsBaseThing => parent as Building_Aerocraft_AsBaseThing;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            // Several decoration comps share the parent's save node, so each one restarts from its own base angle.
            RotateAngle = Props.Draw_Decoration_BaseRotate;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref SecondTextureTransparency, "SecondTextureTransparency", 0f);
            Scribe_Values.Look(ref RotateAngleSpeedNow, "RotateAngleSpeedNow", 0f);
            Scribe_Values.Look(ref RotateAngle, "RotateAngle", 0f);
        }

        public override void PostDraw()
        {
            base.PostDraw();
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            if (aircraft == null)
            {
                return;
            }
            if (Props.If_DrawWhenFlying || aircraft.Is_Static || aircraft.Is_Uping)
            {
                Draw_Decoration();
                if (Props.If_DrawShadow)
                {
                    Draw_Shadow();
                }
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            if (aircraft == null || aircraft.Move_WarmUpTickMax <= 0)
            {
                return;
            }
            SecondTextureTransparency = Mathf.Clamp01((float)aircraft.Move_WarmUpTick / aircraft.Move_WarmUpTickMax);
            RotateAngleSpeedNow = Props.Draw_Decoration_RotateSpeedPerTick_Max * SecondTextureTransparency;
            RotateAngle = Mathf.Repeat(RotateAngle + (Props.If_ClockwiseOrCounterclockwise ? RotateAngleSpeedNow : -RotateAngleSpeedNow), 360f);
        }

        private Matrix4x4 Matrix(Vector3 center, float rangeFactor, float scaleFactor, float altitudeOffset)
        {
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            float range = Props.Draw_Decoration_Range * rangeFactor * aircraft.Draw_ScaleFactorNow;
            float angle = Props.Draw_Decoration_Angle + aircraft.Angle_Fly_Now;
            Vector3 pos = MYDE_ModFront.GetVector3_By_AngleFlat(center, range, angle);
            pos.y = Props.Draw_Decoration_BaseAltitude.AltitudeFor() + altitudeOffset;
            float scale = Props.Draw_Decoration_Scale * scaleFactor;
            if (Props.If_ChangeScaleWithBaseThing)
            {
                scale *= aircraft.Draw_ScaleFactorNow;
            }
            float rotation = Props.If_FallowBaseAngle ? aircraft.Angle_Fly_Now : RotateAngle;
            Matrix4x4 matrix = default;
            matrix.SetTRS(pos, Quaternion.AngleAxis(rotation, Vector3.up), new Vector3(scale, 0f, scale));
            return matrix;
        }

        public void Draw_Decoration()
        {
            if (Props.Draw_Decoration_TexturePath.NullOrEmpty())
            {
                return;
            }
            Matrix4x4 matrix = Matrix(Building_Aerocraft_AsBaseThing.DrawPos, 1f, 1f, Props.Draw_Decoration_ExtraAltitudeLayerNum);
            if (Props.If_UseSecondTexture && !Props.Draw_Decoration_TexturePath_Second.NullOrEmpty())
            {
                DrawMesh(matrix, Props.Draw_Decoration_TexturePath, ShaderDatabase.WorldOverlayTransparent, new Color(1f, 1f, 1f, Props.Draw_Decoration_Transparency * (1f - SecondTextureTransparency)));
                DrawMesh(matrix, Props.Draw_Decoration_TexturePath_Second, ShaderDatabase.WorldOverlayTransparent, new Color(1f, 1f, 1f, Props.Draw_Decoration_Transparency * SecondTextureTransparency));
            }
            else
            {
                DrawMesh(matrix, Props.Draw_Decoration_TexturePath, ShaderDatabase.WorldOverlayTransparent, new Color(1f, 1f, 1f, Props.Draw_Decoration_Transparency));
            }
        }

        public void Draw_Shadow()
        {
            if (!MYDE_AerocraftFramework_Setting.If_DrawShadow || Props.Draw_Shadow_TexturePath.NullOrEmpty())
            {
                return;
            }
            Building_Aerocraft_AsBaseThing aircraft = Building_Aerocraft_AsBaseThing;
            Matrix4x4 matrix = Matrix(aircraft.Shadow_Pos, Props.Draw_Shadow_ScaleAndRangeFactor, Props.Draw_Shadow_ScaleAndRangeFactor, Props.Draw_Shadow_ExtraAltitudeLayerNum);
            float alpha = Props.Draw_Shadow_Transparency * aircraft.Shadow_Transparency;
            if (Props.If_UseSecondTexture && !Props.Draw_Shadow_TexturePath_Second.NullOrEmpty())
            {
                DrawMesh(matrix, Props.Draw_Shadow_TexturePath, ShaderDatabase.Transparent, new Color(0f, 0f, 0f, alpha * (1f - SecondTextureTransparency)));
                DrawMesh(matrix, Props.Draw_Shadow_TexturePath_Second, ShaderDatabase.Transparent, new Color(0f, 0f, 0f, alpha * SecondTextureTransparency));
            }
            else
            {
                DrawMesh(matrix, Props.Draw_Shadow_TexturePath, ShaderDatabase.Transparent, new Color(0f, 0f, 0f, alpha));
            }
        }

        private static void DrawMesh(Matrix4x4 matrix, string texPath, Shader shader, Color color)
        {
            Graphics.DrawMesh(MeshPool.plane10, matrix, MaterialPool.MatFrom(texPath, shader, color), 0);
        }
    }
}
