using RimWorld;
using Verse;

// XML-facing properties. Field names and defaults must stay exactly as in the original mod:
// addons (RimThunder - Gruppa Krovi and others) set them from their own defs.
namespace MYDE_AerocraftFramework
{
    public class CompProperties_AddableWeapon_Add : CompProperties
    {
        public ThingDef AddableWeaponDef;

        public CompProperties_AddableWeapon_Add()
        {
            compClass = typeof(Comp_AddableWeapon_Add);
        }
    }

    public class CompProperties_AutoFindTarget : CompProperties
    {
        public bool Default_FindTargetSet = false;
        public string FindTarget_Icon_On_Label;
        public string FindTarget_Icon_Off_Label;
        public string FindTarget_Icon_Description;
        public string FindTarget_Icon_On_IconPath;
        public string FindTarget_Icon_Off_IconPath;
        public int FindTargetTickMax = 600;

        public CompProperties_AutoFindTarget()
        {
            compClass = typeof(Comp_AutoFindTarget);
        }
    }

    public class CompProperties_BaseDraw_DecorationAndShadow : CompProperties
    {
        public bool If_DrawWhenFlying = true;
        public bool If_ChangeScaleWithBaseThing = true;
        public bool If_UseSecondTexture = false;
        public string Draw_Decoration_TexturePath;
        public string Draw_Decoration_TexturePath_Second;
        public float Draw_Decoration_Scale = 1f;
        public float Draw_Decoration_Transparency = 1f;
        public AltitudeLayer Draw_Decoration_BaseAltitude;
        public float Draw_Decoration_ExtraAltitudeLayerNum = 0f;
        public float Draw_Decoration_Range = 0f;
        public float Draw_Decoration_Angle = 0f;
        public bool If_FallowBaseAngle = false;
        public bool If_ClockwiseOrCounterclockwise = true;
        public float Draw_Decoration_BaseRotate = 0f;
        public float Draw_Decoration_RotateSpeedPerTick_Max = 1f;
        public bool If_DrawShadow = false;
        public string Draw_Shadow_TexturePath;
        public string Draw_Shadow_TexturePath_Second;
        public float Draw_Shadow_ScaleAndRangeFactor = 0f;
        public float Draw_Shadow_Transparency = 0f;
        public float Draw_Shadow_ExtraAltitudeLayerNum = 0f;

        public CompProperties_BaseDraw_DecorationAndShadow()
        {
            compClass = typeof(Comp_BaseDraw_DecorationAndShadow);
        }
    }

    public class CompProperties_Base_Thing : CompProperties
    {
        public bool If_Draw_Base = false;
        public float Draw_ScaleIncreaseFactor_WhenFlying = 1f;
        public string Draw_Base_TexturePath;
        public float Draw_Base_Scale = 1f;
        public float Draw_Base_ExtraAltitudeLayerNum = 0f;
        public bool If_NeedDrawAllShadow = false;
        public bool If_DrawShadow_Base = false;
        public float Draw_Shadow_Base_Scale = 1f;
        public float Draw_Shadow_Base_HeightFactor_All = 1f;
        public float Draw_Shadow_Base_HeightFactor = 1f;
        public float Draw_Shadow_Base_ExtraAltitudeLayerNum = 0f;
        public float Draw_Shadow_Base_Transparency = 0f;

        public CompProperties_Base_Thing()
        {
            compClass = typeof(Comp_Base_Thing);
        }
    }

    public class CompProperties_Base_Weapon : CompProperties
    {
        public ThingDef WeaponDef;
        public bool If_Draw_Gun = false;
        public float Draw_Gun_Scale = 1f;
        public float Draw_Gun_ExtraAltitudeLayerNum = 0f;
        public bool If_DrawShadow_Gun = false;
        public float Draw_Shadow_Gun_Scale = 1f;
        public float Draw_Shadow_Gun_ExtraAltitudeLayerNum = 0f;
        public float Draw_Shadow_Gun_Transparency = 0f;

        public CompProperties_Base_Weapon()
        {
            compClass = typeof(Comp_Base_Weapon);
        }
    }

    public class CompProperties_CanCrossMap : CompProperties
    {
        public ThingDef LeavingThingDef;
        public WorldObjectDef WorldObjectDef;
        public float TravelSpeed = 0.00025f;
        public float FuelConsumeBase = 10f;
        public string CrossMap_Label;
        public string CrossMap_Description;
        public string CrossMap_IconPath;
        public ThingDef DefaultThing_ToRefuel;

        public CompProperties_CanCrossMap()
        {
            compClass = typeof(Comp_CanCrossMap);
        }
    }

    public class CompProperties_CanLoadShell : CompProperties
    {
        public int LoadShell_Max = 20;
        public float LaunchShell_Range = 10f;
        public int LaunchShell_ForceRadius = 5;
        public int LaunchShell_ReloadTickMax = 600;
        public int LaunchShell_ConsumeTickPerLaunch = 150;
        public string LaunchShell_Label;
        public string LaunchShell_Description;
        public string LaunchShell_False_IconPath;
        public string DropShell_Label;
        public string DropShell_Description;

        public CompProperties_CanLoadShell()
        {
            compClass = typeof(Comp_CanLoadShell);
        }
    }

    public class CompProperties_CarryPawn : CompProperties
    {
        public int CarryPawnNumMax = 1;
        public bool If_ChangeWeaponByPawnWeaponWhenCarry = false;
        public bool If_ShowFastLordGizmos = false;
        public string Gizmos_CarryPawn_Label;
        public string Gizmos_CarryPawn_Description;
        public string Gizmos_CarryPawn_IconPath;
        public int CarryPawn_MaxRange = 1;
        public bool If_NeedPawnToControl = false;
        public int NeedPawnToControl_Number = 1;
        public bool If_DraftedWhenDrop = true;

        public CompProperties_CarryPawn()
        {
            compClass = typeof(Comp_CarryPawn);
        }
    }

    public class CompProperties_DoExplosion_BySomeWays : CompProperties
    {
        public bool If_DoExplosion_WhenDestroy = false;
        public bool If_ForceDoExplosion_Mannable = false;
        public string ForceDoExplosion_Label;
        public string ForceDoExplosion_Description;
        public string ForceDoExplosion_IconPath;
        public bool If_Drop_WhenHitpointZero = false;
        public int Drop_Range = 2;
        public bool If_CountDownToExplosion = false;
        public string ShowCountDownToExplosion_Label;
        public bool If_ShowCountDownToExplosionTick = false;
        public int ExplosionCountDown_TickMax = 3000;
        public int DrawExplosion_ChangeTickMax = 60;
        public float DrawExplosion_Scale = 60f;
        public int DrawExplosion_BeginTick = 600;
        public int DrawExplosion_RedTick = 300;
        public float explosiveRadius = 1.9f;
        public DamageDef explosiveDamageType;
        public int damageAmountBase = -1;
        public float armorPenetrationBase = -1f;
        public ThingDef postExplosionSpawnThingDef;
        public float postExplosionSpawnChance;
        public int postExplosionSpawnThingCount = 1;
        public bool applyDamageToExplosionCellsNeighbors;
        public ThingDef preExplosionSpawnThingDef;
        public float preExplosionSpawnChance;
        public int preExplosionSpawnThingCount = 1;
        public float chanceToStartFire;
        public bool damageFalloff;
        public bool explodeOnKilled;
        public GasType? postExplosionGasType;
        public bool doVisualEffects = true;
        public float propagationSpeed = 1f;
        public SoundDef explosionSound;

        public CompProperties_DoExplosion_BySomeWays()
        {
            compClass = typeof(Comp_DoExplosion_BySomeWays);
        }
    }

    public class CompProperties_GetExtraWeapon : CompProperties
    {
        public ThingDef ExtraWeaponDef;
        public float ExtraWeapon_Range = 0f;
        public float ExtraWeapon_Angle = 0f;

        public CompProperties_GetExtraWeapon()
        {
            compClass = typeof(Comp_GetExtraWeapon);
        }
    }

    public class CompProperties_LinkToVerbSpawnr : CompProperties
    {
        public ThingDef SpawnDef;
        public bool If_SpawnInMapBoundary = false;
        public int SpawnNum = 1;
        public int SpawnConsumePerNum = 1;
        public int SpawnRadius = 0;
        public bool If_DefaultFollow = false;
        public bool If_AutoSelectAfterSpawn = false;
        public bool If_OneUse = false;
        public ThingDef WeaponDefAfterOneUse;

        public CompProperties_LinkToVerbSpawnr()
        {
            compClass = typeof(Comp_LinkToVerbSpawnr);
        }
    }

    public class CompProperties_MoveToTargetAndHover : CompProperties
    {
        public int Check_CollideMoveRangeMax = 5;
        public int Move_WarmUpTickMax = 60;
        public bool If_NeedTurnWhenMoving = true;
        public bool If_NeedGlidingWhenTakeOff = false;
        public int Gliding_Range = 2;
        public int GlidingTakeOffOrDownTickMax = 60;
        public float MoveSpeed_Max = 0.15f;
        public float MoveSpeed_Turning = 0.08f;
        public bool Default_HoverSet = false;
        public bool If_ShowHover_Icon = true;
        public string Hover_Icon_On_Label;
        public string Hover_Icon_Off_Label;
        public string Hover_Icon_Description;
        public string Hover_Icon_On_IconPath;
        public string Hover_Icon_Off_IconPath;
        public string TakeOffAndLanding_Icon_On_Label;
        public string TakeOffAndLanding_Icon_Off_Label;
        public string TakeOffAndLanding_Icon_Description;
        public string TakeOffAndLanding_Icon_On_IconPath;
        public string TakeOffAndLanding_Icon_Off_IconPath;
        public float AngleChangePerTick_Hover = 1f;
        public float AngleChangePerTick_Turning = 2f;
        public bool If_CanWrap = false;
        public EffecterDef Wrap_Effecter_Start;
        public EffecterDef Wrap_Effecter_End;
        public float FuelConsumePerTick = 0f;

        public CompProperties_MoveToTargetAndHover()
        {
            compClass = typeof(Comp_MoveToTargetAndHover);
        }
    }

    public class CompProperties_ReplaceCurrentWeapon : CompProperties
    {
        public bool If_CanShowGizmosToReplace = false;

        public CompProperties_ReplaceCurrentWeapon()
        {
            compClass = typeof(Comp_ReplaceCurrentWeapon);
        }
    }

    public class CompProperties_ShootSomethingManual : CompProperties
    {
        public ThingDef ShootSomethingDef;
        public string ShootSomething_Label;
        public string ShootSomething_Description;
        public string ShootSomething_True_IconPath;
        public string ShootSomething_False_IconPath;
        public float ShootSomething_Range = 10f;
        public int ShootSomething_ReloadTickMax = 600;

        public CompProperties_ShootSomethingManual()
        {
            compClass = typeof(Comp_ShootSomethingManual);
        }
    }

    public class CompProperties_ShowBuildingShieldGizmos : CompProperties
    {
        public CompProperties_ShowBuildingShieldGizmos()
        {
            compClass = typeof(Comp_ShowBuildingShieldGizmos);
        }
    }

    public class CompProperties_ShowStoredEnergyGizmos : CompProperties
    {
        public CompProperties_ShowStoredEnergyGizmos()
        {
            compClass = typeof(Comp_ShowStoredEnergyGizmos);
        }
    }

    public class CompProperties_SpawnFleck : CompProperties
    {
        public bool If_SpawnEffectOrFleckOnlyStatic = false;
        public FleckDef FleckDef;
        public int Fleck_MakeFleckTickMax = 10;
        public bool If_Fleck_Addable = false;
        public float Fleck_MakeFleck_AddNumTickMax = 10f;
        public int Fleck_MakeFleckNum_Origin = 0;
        public int Fleck_MakeFleckNum_Max = 0;
        public float Fleck_Range_ToSetPosition = 0f;
        public float Fleck_Angle_ToSetPosition = 0f;
        public bool Fleck_If_FollowBaseThingAngle = false;
        public FloatRange Fleck_Angle = new FloatRange(-180f, 180f);
        public FloatRange Fleck_Scale = new FloatRange(1f, 2f);
        public FloatRange Fleck_Speed = new FloatRange(5f, 7f);
        public FloatRange Fleck_Rotation = new FloatRange(-180f, 180f);

        public CompProperties_SpawnFleck()
        {
            compClass = typeof(Comp_SpawnFleck);
        }
    }

    public class CompProperties_SpawnFleck_Projectile : CompProperties
    {
        public FleckDef FleckDef;
        public int Fleck_MakeFleckTickMax = 10;
        public IntRange Fleck_MakeFleckNum;
        public FloatRange Fleck_Angle = new FloatRange(-180f, 180f);
        public FloatRange Fleck_Scale = new FloatRange(1f, 2f);
        public FloatRange Fleck_Speed = new FloatRange(5f, 7f);
        public FloatRange Fleck_Rotation = new FloatRange(-180f, 180f);

        public CompProperties_SpawnFleck_Projectile()
        {
            compClass = typeof(Comp_SpawnFleck_Projectile);
        }
    }

    public class CompProperties_SpawnLight : CompProperties
    {
        public ThingDef SpawnLightDef;
        public int SpawnLightTickMax = 10;

        public CompProperties_SpawnLight()
        {
            compClass = typeof(Comp_SpawnLight);
        }
    }

    public class CompProperties_SpawnSound : CompProperties
    {
        public SoundDef SoundDef;
        public int Sound_MakeSoundkTick_Add = 10;
        public int Sound_MakeSoundkTick_Max = 20;

        public CompProperties_SpawnSound()
        {
            compClass = typeof(Comp_SpawnSound);
        }
    }

    public class CompProperties_Test_Get_AngleAndRange : CompProperties
    {
        public CompProperties_Test_Get_AngleAndRange()
        {
            compClass = typeof(Comp_Test_Get_AngleAndRange);
        }
    }
}
