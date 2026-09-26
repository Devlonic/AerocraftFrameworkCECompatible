using RimWorld;
using UnityEngine;
using Verse;

namespace MYDE_AerocraftFramework
{
    /// <summary>Rotating gun drawn on top of an aerocraft (a variant of the vanilla TurretTop that follows the aircraft).</summary>
    public class TurretTop_ChangeDraw
    {
        private const float IdleTurnDegreesPerTick = 0.26f;
        private const int IdleTurnDuration = 140;
        private const int IdleTurnIntervalMin = 150;
        private const int IdleTurnIntervalMax = 350;

        public static readonly int ArtworkRotation = -90;

        private readonly Building_Aerocraft_Base Building_Aerocraft_Base;
        private float curRotationInt;
        private int ticksUntilIdleTurn;
        private int idleTurnTicksLeft;
        private bool idleTurnClockwise;

        public float ExtraRotationInt;

        public TurretTop_ChangeDraw(Building_Aerocraft_Base ParentTurret)
        {
            Building_Aerocraft_Base = ParentTurret;
        }

        public float CurRotation
        {
            get => curRotationInt;
            set => curRotationInt = Mathf.Repeat(value, 360f);
        }

        public float ExtraRotation
        {
            get => ExtraRotationInt;
            set => ExtraRotationInt = Mathf.Repeat(value, 360f);
        }

        public void SetRotationFromOrientation()
        {
            CurRotation = Building_Aerocraft_Base.Rotation.AsAngle;
        }

        public void ForceFaceTarget(LocalTargetInfo targ)
        {
            if (targ.IsValid)
            {
                CurRotation = (targ.Cell.ToVector3Shifted() - Building_Aerocraft_Base.DrawPos).AngleFlat();
            }
        }

        public void TurretTopTick()
        {
            LocalTargetInfo currentTarget = Building_Aerocraft_Base.CurrentTarget;
            if (currentTarget.IsValid)
            {
                CurRotation = (currentTarget.Cell.ToVector3Shifted() - Building_Aerocraft_Base.DrawPos).AngleFlat();
                ticksUntilIdleTurn = Rand.RangeInclusive(IdleTurnIntervalMin, IdleTurnIntervalMax);
            }
            else if (ticksUntilIdleTurn > 0)
            {
                ticksUntilIdleTurn--;
                if (ticksUntilIdleTurn == 0)
                {
                    idleTurnClockwise = Rand.Value < 0.5f;
                    idleTurnTicksLeft = IdleTurnDuration;
                }
            }
            else
            {
                CurRotation += idleTurnClockwise ? IdleTurnDegreesPerTick : -IdleTurnDegreesPerTick;
                idleTurnTicksLeft--;
                if (idleTurnTicksLeft <= 0)
                {
                    ticksUntilIdleTurn = Rand.RangeInclusive(IdleTurnIntervalMin, IdleTurnIntervalMax);
                }
            }
        }

        public void DrawTurret(Vector3 VPos, float Scale, float AltitudeLayerNum, Color Color)
        {
            Thing gun = Building_Aerocraft_Base.Gun_Now;
            string texPath = gun?.def?.graphicData?.texPath;
            if (texPath == null)
            {
                return;
            }
            float rotation = Building_Aerocraft_Base.CurrentEffectiveVerb?.AimAngleOverride ?? CurRotation;
            float angle = TurretTop.ArtworkRotation + rotation + ExtraRotation;
            if (!Building_Aerocraft_Base.Is_Flying && MYDE_AerocraftFramework_Setting.If_CanFireOnlyFlying)
            {
                angle = Building_Aerocraft_Base.Angle_Fly_Now;
            }
            Vector3 pos = VPos;
            pos.y = AltitudeLayerNum;
            Matrix4x4 matrix = default;
            matrix.SetTRS(pos, angle.ToQuat(), new Vector3(Scale, 1f, Scale));
            Material material = MaterialPool.MatFrom(texPath, ShaderDatabase.WorldOverlayTransparent, Color);
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
        }
    }
}
