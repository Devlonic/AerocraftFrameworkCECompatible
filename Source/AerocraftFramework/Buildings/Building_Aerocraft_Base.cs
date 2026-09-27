using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MYDE_AerocraftFramework
{
    /// <summary>
    /// Shared turret logic of an aircraft body and of its extra weapon mounts.
    /// Public field names and save keys are kept from the original mod for addon and save compatibility.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Building_Aerocraft_Base : Building_Turret
    {
        protected int CooldownTicksLeft;
        protected int WarmupTicksLeft;
        protected LocalTargetInfo CurrentTargetInt = LocalTargetInfo.Invalid;
        public bool HoldFire;
        private bool Is_ShowRadiusOfRange;

        /// <summary>The weapon in use. Never null on a spawned turret.</summary>
        public Thing Gun_Now;

        /// <summary>Other weapons stored in the turret (installed by colonists, taken from pilots). Never contains Gun_Now.</summary>
        public List<Thing> Gun_InnerList = new List<Thing>();

        public TurretTop_ChangeDraw top;
        public CompRefuelable RefuelableComp;
        public static Material ForcedTargetLineMat = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, new Color(1f, 0.5f, 0.5f));

        public Vector2 RealCurrentPosition;
        public float Angle_Fly_Now;
        public float Angle_Fly_End;
        public float Anti_Angle_Fly_End;

        public bool If_Draw_Base;
        public float Draw_ScaleFactorNow = 1f;
        public float Draw_ScaleIncreaseFactor_WhenFlying = 1f;
        public string Draw_Base_TexturePath;
        public float Draw_Base_Scale = 1f;
        public float Draw_Base_ExtraAltitudeLayerNum;
        public bool If_NeedDrawAllShadow;
        public Vector3 Shadow_Pos;
        public float Shadow_Transparency;
        public float Draw_Shadow_Base_HeightFactor_All = 1f;
        public float Draw_Shadow_Base_HeightNow = 1f;
        public float Draw_Shadow_Base_HeightFactor = 1f;

        public ThingDef WeaponDef;
        public bool If_Draw_Gun;
        public float Draw_Gun_Scale_Origin = 1f;
        public float Draw_Gun_Scale_Now = 1f;
        public float Draw_Gun_ExtraAltitudeLayerNum;

        public int ChangPosTick;
        public int ChangPosTickMax = 10;
        public bool If_BreakDown;

        /// <summary>Set while a colonist is reloading one of the guns (read by Combat Extended).</summary>
        public bool isReloading;

        private bool everSpawned;
        private int lastOutOfAmmoMessageTick = -99999;

        /// <summary>Kept for API compatibility with the original CE build (unused: aircraft are not manned turrets).</summary>
        public bool isSlow;

        /// <summary>Kept for API compatibility with the original CE build (always null).</summary>
        public CompMannable mannableComp;

        public Building_Aerocraft_Base()
        {
            top = new TurretTop_ChangeDraw(this);
        }

        // ------------------------------------------------------------------ properties

        public override Vector3 DrawPos => new Vector3(RealCurrentPosition.x, def.Altitude, RealCurrentPosition.y);

        public virtual bool Active
        {
            get
            {
                if (RefuelableComp != null && RefuelableComp.Fuel <= 1f)
                {
                    return false;
                }
                return !IsStunned;
            }
        }

        public CompEquippable GunCompEq => Gun_Now?.TryGetComp<CompEquippable>();

        public override Verb AttackVerb => GunCompEq?.PrimaryVerb;

        public override LocalTargetInfo CurrentTarget => CurrentTargetInt;

        private bool WarmingUp => WarmupTicksLeft > 0;

        /// <summary>Warm-up ticks of the current burst. Exposed for Combat Extended's verbs.</summary>
        public int WarmupTicks
        {
            get => WarmupTicksLeft;
            set => WarmupTicksLeft = value;
        }

        public int CooldownTicks => CooldownTicksLeft;

        private bool CanSetForcedTarget => true;

        private bool CanToggleHoldFire => true;

        public TurretTop_ChangeDraw Top => top;

        public virtual bool Is_Flying => true;

        public virtual bool Is_Static => true;

        public bool IsControllable => AerocraftUtility.IsControllable(this);

        public CompProperties_Base_Weapon WeaponProps => GetComp<Comp_Base_Weapon>()?.Props;

        /// <summary>The weapon the turret was built with.</summary>
        public ThingDef OriginWeaponDef => WeaponProps?.WeaponDef;

        /// <summary>The current weapon followed by the stored ones.</summary>
        public IEnumerable<Thing> AllGuns
        {
            get
            {
                if (Gun_Now != null)
                {
                    yield return Gun_Now;
                }
                for (int i = 0; i < Gun_InnerList.Count; i++)
                {
                    if (Gun_InnerList[i] != null)
                    {
                        yield return Gun_InnerList[i];
                    }
                }
            }
        }

        // ------------------------------------------------------------------ lifecycle

        public override void PostMake()
        {
            base.PostMake();
            MakeGun();
            if (Gun_Now != null)
            {
                CooldownTicksLeft = Gun_Now.GetStatValue(StatDefOf.RangedWeapon_Cooldown).SecondsToTicks();
            }
        }

        /// <summary>Creates the original weapon and makes it the current one.</summary>
        public void MakeGun()
        {
            ThingDef weaponDef = OriginWeaponDef;
            if (weaponDef == null)
            {
                Log.ErrorOnce($"[Aerocraft Framework] {def.defName} has no CompProperties_Base_Weapon.WeaponDef.", def.shortHash ^ 0x0A11);
                return;
            }
            Thing previous = Gun_Now;
            Gun_Now = ThingMaker.MakeThing(weaponDef);
            if (previous != null && previous != Gun_Now && !Gun_InnerList.Contains(previous))
            {
                Gun_InnerList.Add(previous);
            }
            UpdateGunVerbs();
        }

        /// <summary>Links every gun to this turret: verb caster, burst callback and (with CE) the ammo user.</summary>
        public void UpdateGunVerbs()
        {
            Gun_InnerList.RemoveAll(g => g == null || g == Gun_Now);
            foreach (Thing gun in AllGuns)
            {
                CompEquippable eq = gun.TryGetComp<CompEquippable>();
                if (eq != null)
                {
                    List<Verb> verbs = eq.AllVerbs;
                    for (int i = 0; i < verbs.Count; i++)
                    {
                        verbs[i].caster = this;
                        verbs[i].castCompleteCallback = gun == Gun_Now ? BurstComplete : null;
                    }
                }
                AerocraftCompat.Ammo.Notify_GunAttached(this, gun);
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            RefuelableComp = this.TryGetComp<CompRefuelable>();
            CompProperties_Base_Thing thingProps = GetComp<Comp_Base_Thing>()?.Props;
            if (thingProps != null)
            {
                If_Draw_Base = thingProps.If_Draw_Base;
                Draw_ScaleIncreaseFactor_WhenFlying = thingProps.Draw_ScaleIncreaseFactor_WhenFlying;
                Draw_Base_TexturePath = thingProps.Draw_Base_TexturePath;
                Draw_Base_Scale = thingProps.Draw_Base_Scale;
                Draw_Base_ExtraAltitudeLayerNum = thingProps.Draw_Base_ExtraAltitudeLayerNum;
                If_NeedDrawAllShadow = thingProps.If_NeedDrawAllShadow;
                Draw_Shadow_Base_HeightFactor_All = thingProps.Draw_Shadow_Base_HeightFactor_All;
                Draw_Shadow_Base_HeightFactor = thingProps.Draw_Shadow_Base_HeightFactor;
            }
            CompProperties_Base_Weapon weaponProps = WeaponProps;
            if (weaponProps != null)
            {
                WeaponDef = weaponProps.WeaponDef;
                If_Draw_Gun = weaponProps.If_Draw_Gun;
                Draw_Gun_Scale_Origin = weaponProps.Draw_Gun_Scale;
                Draw_Gun_ExtraAltitudeLayerNum = weaponProps.Draw_Gun_ExtraAltitudeLayerNum;
            }
            if (Gun_Now == null)
            {
                MakeGun();
            }
            else
            {
                UpdateGunVerbs();
            }
            if (!respawningAfterLoad)
            {
                top.SetRotationFromOrientation();
            }
            if (!respawningAfterLoad || RealCurrentPosition == Vector2.zero)
            {
                RealCurrentPosition = new Vector2(Position.x + 0.5f, Position.z + 0.5f);
            }
            if (!everSpawned)
            {
                everSpawned = true;
                AerocraftCompat.Ammo.Notify_FirstSpawn(this);
            }
            MapComponent_AerocraftTracker.For(map)?.Register(this);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            MapComponent_AerocraftTracker.For(Map)?.Deregister(this);
            ResetCurrentTarget();
            isReloading = false;
            base.DeSpawn(mode);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref Gun_Now, "Gun_Now");
            Scribe_Collections.Look(ref Gun_InnerList, "Gun_InnerList", LookMode.Deep);
            Scribe_Values.Look(ref isReloading, "isReloading", false);
            Scribe_Values.Look(ref CooldownTicksLeft, "CooldownTicksLeft", 0);
            Scribe_Values.Look(ref WarmupTicksLeft, "WarmupTicksLeft", 0);
            Scribe_TargetInfo.Look(ref CurrentTargetInt, "CurrentTarget");
            Scribe_Values.Look(ref HoldFire, "HoldFire", false);
            Scribe_Values.Look(ref everSpawned, "everSpawned", false);
            Scribe_Values.Look(ref Is_ShowRadiusOfRange, "Is_ShowRadiusOfRange", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars && !Is_ShowRadiusOfRange)
            {
                // The non-CE build of the original mod used another key.
                Scribe_Values.Look(ref Is_ShowRadiusOfRange, "If_ShowRadiusOfRange", false);
            }
            Scribe_Values.Look(ref RealCurrentPosition, "RealCurrentPosition");
            Scribe_Values.Look(ref Angle_Fly_Now, "Angle_Fly_Now", 0f);
            Scribe_Values.Look(ref Angle_Fly_End, "Angle_Fly_End", 0f);
            Scribe_Values.Look(ref Anti_Angle_Fly_End, "Anti_Angle_Fly_End", 0f);
            Scribe_Values.Look(ref If_Draw_Base, "If_Draw_Base", false);
            Scribe_Values.Look(ref Draw_ScaleFactorNow, "Draw_ScaleFactorNow", 0f);
            Scribe_Values.Look(ref Draw_ScaleIncreaseFactor_WhenFlying, "Draw_ScaleIncreaseFactor_WhenFlying", 0f);
            Scribe_Values.Look(ref Draw_Base_Scale, "Draw_Base_Scale", 0f);
            Scribe_Values.Look(ref Draw_Base_ExtraAltitudeLayerNum, "Draw_Base_ExtraAltitudeLayerNum", 0f);
            Scribe_Values.Look(ref If_NeedDrawAllShadow, "If_NeedDrawAllShadow", false);
            Scribe_Values.Look(ref Shadow_Pos, "Shadow_Pos");
            Scribe_Values.Look(ref Shadow_Transparency, "Shadow_Transparency", 0f);
            Scribe_Values.Look(ref Draw_Shadow_Base_HeightFactor_All, "Draw_Shadow_Base_HeightFactor_All", 0f);
            Scribe_Values.Look(ref Draw_Shadow_Base_HeightNow, "Draw_Shadow_Base_HeightNow", 0f);
            Scribe_Values.Look(ref Draw_Shadow_Base_HeightFactor, "Draw_Shadow_Base_HeightFactor", 0f);
            Scribe_Values.Look(ref If_Draw_Gun, "If_Draw_Gun", false);
            Scribe_Values.Look(ref Draw_Gun_Scale_Origin, "Draw_Gun_Scale_Origin", 0f);
            Scribe_Values.Look(ref Draw_Gun_Scale_Now, "Draw_Gun_Scale_Now", 0f);
            Scribe_Values.Look(ref Draw_Gun_ExtraAltitudeLayerNum, "Draw_Gun_ExtraAltitudeLayerNum", 0f);
            Scribe_Values.Look(ref If_BreakDown, "If_BreakDown", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Gun_InnerList == null)
                {
                    Gun_InnerList = new List<Thing>();
                }
                Gun_InnerList.RemoveAll(g => g == null);
                if (Gun_Now == null)
                {
                    Log.Warning($"[Aerocraft Framework] {this} had no weapon after loading; recreating {OriginWeaponDef?.defName}.");
                    MakeGun();
                }
                else
                {
                    UpdateGunVerbs();
                }
            }
        }

        public override bool ClaimableBy(Faction by, StringBuilder reason = null)
        {
            return base.ClaimableBy(by, reason) && !Active;
        }

        // ------------------------------------------------------------------ inspection

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder();
            string inspectString = base.GetInspectString();
            if (!inspectString.NullOrEmpty())
            {
                sb.AppendLine(inspectString);
            }
            if (Gun_Now != null)
            {
                sb.AppendLine("AerocraftFramework_CurrentWeaponName".Translate() + ": " + GunStatusLine(Gun_Now));
            }
            if (AerocraftCompat.Ammo.IsReloading(this))
            {
                sb.AppendLine("AerocraftFramework_Reloading".Translate());
            }
            else if (Spawned && CooldownTicksLeft > 0 && Is_Flying)
            {
                sb.AppendLine("CanFireIn".Translate() + ": " + CooldownTicksLeft.ToStringSecondsFromTicks());
            }
            return sb.ToString().TrimEndNewlines();
        }

        /// <summary>"Gun label (ammo: 120/200 FMJ)".</summary>
        public static string GunStatusLine(Thing gun)
        {
            string line = gun.LabelCapNoCount;
            if (AerocraftCompat.Ammo.TryGetMagazine(gun, out int current, out int capacity))
            {
                string ammo = AerocraftCompat.Ammo.CurrentAmmoLabel(gun);
                line += " (" + current + "/" + capacity + (ammo.NullOrEmpty() ? "" : ", " + ammo) + ")";
            }
            return line;
        }

        // ------------------------------------------------------------------ targeting and firing

        public override void OrderAttack(LocalTargetInfo targ)
        {
            if (!targ.IsValid)
            {
                if (forcedTarget.IsValid)
                {
                    ResetForcedTarget();
                }
                return;
            }
            Verb verb = AttackVerb;
            if (verb == null)
            {
                return;
            }
            float distance = (targ.Cell - Position).LengthHorizontal;
            if (distance < verb.verbProps.EffectiveMinRange(targ, this))
            {
                Messages.Message("MessageTargetBelowMinimumRange".Translate(), this, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            if (distance > verb.verbProps.range)
            {
                Messages.Message("MessageTargetBeyondMaximumRange".Translate(), this, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            if (forcedTarget != targ)
            {
                forcedTarget = targ;
                if (CooldownTicksLeft <= 0)
                {
                    TryStartShootSomething(canBeginBurstImmediately: false);
                }
            }
            if (HoldFire)
            {
                Messages.Message("MessageTurretWontFireBecauseHoldFire".Translate(def.label), this, MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        public override void Tick()
        {
            base.Tick();
            DrawShadowTick();
            Draw_Gun_Scale_Now = Draw_Gun_Scale_Origin * Draw_ScaleFactorNow;
            if (forcedTarget.ThingDestroyed)
            {
                ResetForcedTarget();
            }
            bool canFireNow = Is_Flying || !MYDE_AerocraftFramework_Setting.If_CanFireOnlyFlying;
            if (If_BreakDown || !canFireNow)
            {
                if (CurrentTargetInt.IsValid || WarmupTicksLeft > 0)
                {
                    ResetCurrentTarget();
                }
                return;
            }
            Verb verb = AttackVerb;
            if (Active && Spawned && verb != null)
            {
                GunCompEq.verbTracker.VerbsTick();
                if (IsStunned || verb.state == VerbState.Bursting)
                {
                    return;
                }
                if (WarmingUp)
                {
                    WarmupTicksLeft--;
                    if (WarmupTicksLeft == 0)
                    {
                        BeginBurst();
                    }
                }
                else
                {
                    if (CooldownTicksLeft > 0)
                    {
                        CooldownTicksLeft--;
                    }
                    if (CooldownTicksLeft <= 0 && (forcedTarget.IsValid || this.IsHashIntervalTick(10)))
                    {
                        TryStartShootSomething(canBeginBurstImmediately: true);
                    }
                }
                top.TurretTopTick();
            }
            else
            {
                ResetCurrentTarget();
            }
        }

        protected virtual void BeginBurst()
        {
            Verb verb = AttackVerb;
            if (verb == null || !CurrentTarget.IsValid)
            {
                ResetCurrentTarget();
                return;
            }
            verb.TryStartCastOn(CurrentTarget);
            OnAttackedTarget(CurrentTarget);
        }

        public void TryStartShootSomething(bool canBeginBurstImmediately)
        {
            Verb verb = AttackVerb;
            if (!Spawned || verb == null || (HoldFire && CanToggleHoldFire) || (verb.ProjectileFliesOverhead() && Map.roofGrid.Roofed(Position)))
            {
                ResetCurrentTarget();
                return;
            }
            if (!AerocraftCompat.Ammo.CanFireNow(Gun_Now))
            {
                ResetCurrentTarget();
                Notify_OutOfAmmo();
                return;
            }
            AerocraftCompat.Ammo.PrepareToFire(this, verb);
            if (!verb.Available())
            {
                ResetCurrentTarget();
                return;
            }
            CurrentTargetInt = forcedTarget.IsValid ? forcedTarget : TryFindNewTarget();
            if (!CurrentTargetInt.IsValid)
            {
                ResetCurrentTarget();
                return;
            }
            float warmupTime = verb.verbProps.warmupTime;
            if (warmupTime > 0f)
            {
                WarmupTicksLeft = warmupTime.SecondsToTicks();
            }
            else if (canBeginBurstImmediately)
            {
                BeginBurst();
            }
            else
            {
                WarmupTicksLeft = 1;
            }
        }

        public virtual LocalTargetInfo TryFindNewTarget()
        {
            Verb verb = AttackVerb;
            if (verb == null)
            {
                return LocalTargetInfo.Invalid;
            }
            IAttackTargetSearcher searcher = this;
            Faction faction = Faction;
            float range = verb.verbProps.range;
            bool fliesOverhead = verb.ProjectileFliesOverhead();
            if (Rand.Value < 0.5f && fliesOverhead && faction != null && faction.HostileTo(Faction.OfPlayer)
                && Map.listerBuildings.allBuildingsColonist.Where(x =>
                {
                    float minRange = verb.verbProps.EffectiveMinRange(x, this);
                    float distSquared = x.Position.DistanceToSquared(Position);
                    return distSquared > minRange * minRange && distSquared < range * range;
                }).TryRandomElement(out Building building))
            {
                return building;
            }
            TargetScanFlags flags = TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable;
            if (!fliesOverhead)
            {
                flags |= TargetScanFlags.NeedLOSToAll | TargetScanFlags.LOSBlockableByGas;
            }
            else
            {
                flags |= TargetScanFlags.NeedNotUnderThickRoof;
            }
            if (verb.IsIncendiary_Ranged())
            {
                flags |= TargetScanFlags.NeedNonBurning;
            }
            return (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(searcher, flags, IsValidTarget, 0f, range);
        }

        private bool IsValidTarget(Thing t)
        {
            if (t is Pawn pawn)
            {
                if (Faction == Faction.OfPlayer && pawn.IsPrisoner)
                {
                    return false;
                }
                if (AttackVerb != null && AttackVerb.ProjectileFliesOverhead())
                {
                    RoofDef roof = Map.roofGrid.RoofAt(t.Position);
                    if (roof != null && roof.isThickRoof)
                    {
                        return false;
                    }
                }
                return !GenAI.MachinesLike(Faction, pawn);
            }
            return true;
        }

        protected virtual void BurstComplete()
        {
            Verb verb = AttackVerb;
            if (Gun_Now != null)
            {
                CooldownTicksLeft = AerocraftCompat.Ammo.BurstCooldownSeconds(this, Gun_Now).SecondsToTicks();
            }
            if (verb != null && AerocraftCompat.Ammo.IsOneUseVerb(verb))
            {
                Change_NowWeapon_OneUse();
            }
            else if (Gun_Now != null && !AerocraftCompat.Ammo.CanFireNow(Gun_Now))
            {
                Notify_OutOfAmmo();
            }
        }

        public float BurstCooldownTime()
        {
            return Gun_Now == null ? 1f : AerocraftCompat.Ammo.BurstCooldownSeconds(this, Gun_Now);
        }

        /// <summary>The current gun is empty: switch to a loaded stored gun, or tell the player.</summary>
        protected virtual void Notify_OutOfAmmo()
        {
            Thing loaded = Gun_InnerList.FirstOrDefault(g => g.def == OriginWeaponDef && AerocraftCompat.Ammo.CanFireNow(g))
                ?? Gun_InnerList.FirstOrDefault(g => AerocraftCompat.Ammo.CanFireNow(g));
            if (loaded != null)
            {
                Change_NowWeapon_ByITab(loaded);
                return;
            }
            if (Faction == Faction.OfPlayer && Find.TickManager.TicksGame - lastOutOfAmmoMessageTick > 2500)
            {
                lastOutOfAmmoMessageTick = Find.TickManager.TicksGame;
                Building_Aerocraft_AsBaseThing aircraft = AerocraftUtility.AircraftOf(this);
                Messages.Message("AerocraftFramework_OutOfAmmo".Translate((aircraft ?? this).LabelShort, Gun_Now.LabelShort), this, MessageTypeDefOf.CautionInput, historical: false);
            }
        }

        // ------------------------------------------------------------------ drawing

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            if (If_Draw_Base)
            {
                Draw_BaseThing();
            }
            if (If_Draw_Gun)
            {
                Draw_TurretGun();
            }
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            Verb verb = AttackVerb;
            if (Is_ShowRadiusOfRange && verb != null)
            {
                // A circle, not a radius ring: rings fail above the radius of the precomputed cell pattern (about 56).
                Vector3 center = Position.ToVector3Shifted();
                GenDraw.DrawCircleOutline(center, verb.verbProps.range);
                float minRange = verb.verbProps.EffectiveMinRange(allowAdjacentShot: true);
                if (minRange > 0.1f)
                {
                    GenDraw.DrawCircleOutline(center, minRange, SimpleColor.Red);
                }
            }
            if (WarmingUp && CurrentTarget.IsValid)
            {
                int degreesWide = (int)(WarmupTicksLeft * 0.5f);
                GenDraw.DrawAimPie(this, CurrentTarget, degreesWide, def.size.x * 0.5f);
            }
            if (forcedTarget.IsValid && (!forcedTarget.HasThing || forcedTarget.Thing.Spawned))
            {
                Vector3 b = forcedTarget.HasThing ? forcedTarget.Thing.TrueCenter() : forcedTarget.Cell.ToVector3Shifted();
                Vector3 a = DrawPos;
                b.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                a.y = b.y;
                GenDraw.DrawLineBetween(a, b, ForcedTargetLineMat);
            }
        }

        private void Draw_BaseThing()
        {
            if (Draw_Base_TexturePath.NullOrEmpty())
            {
                return;
            }
            Vector3 drawPos = DrawPos;
            drawPos.y = def.Altitude + Draw_Base_ExtraAltitudeLayerNum;
            float scale = Draw_Base_Scale * Draw_ScaleFactorNow;
            Matrix4x4 matrix = default;
            matrix.SetTRS(drawPos, Quaternion.AngleAxis(Angle_Fly_Now, Vector3.up), new Vector3(scale, 0f, scale));
            Color color = Stuff != null ? def.GetColorForStuff(Stuff) : Color.white;
            Material material = MaterialPool.MatFrom(Draw_Base_TexturePath, ShaderDatabase.WorldOverlayTransparent, color);
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
        }

        private void Draw_TurretGun()
        {
            Vector3 drawPos = DrawPos;
            top.DrawTurret(drawPos, Draw_Gun_Scale_Now, drawPos.y + Draw_Gun_ExtraAltitudeLayerNum, Color.white);
        }

        public void DrawShadowTick()
        {
            if (MYDE_AerocraftFramework_Setting.If_DrawShadow && If_NeedDrawAllShadow)
            {
                float range = AerocraftFramework_MapManager_ToGetShadow.Shadow_Range * Draw_ScaleFactorNow * Draw_Shadow_Base_HeightNow * Draw_Shadow_Base_HeightFactor_All;
                Shadow_Pos = MYDE_ModFront.GetVector3_By_AngleFlat(DrawPos, range, AerocraftFramework_MapManager_ToGetShadow.Shadow_Angle);
                Shadow_Transparency = AerocraftFramework_MapManager_ToGetShadow.Shadow_Transparency;
            }
        }

        // ------------------------------------------------------------------ gizmos and menus

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            if (!IsControllable)
            {
                yield break;
            }
            foreach (Gizmo gizmo in GetWeaponGizmos())
            {
                yield return gizmo;
            }
            if (Spawned)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "AerocraftFramework_ShowRadiusofRange_Label".Translate(),
                    defaultDesc = "AerocraftFramework_ShowRadiusofRange_Description".Translate(),
                    icon = MYDE_TexButton.ShowRadiusfRange,
                    hotKey = KeyBindingDefOf.Misc9,
                    toggleAction = () => Is_ShowRadiusOfRange = !Is_ShowRadiusOfRange,
                    isActive = () => Is_ShowRadiusOfRange
                };
                yield return new Command_Action
                {
                    defaultLabel = "AerocraftFramework_ShowRadiusofRange_SelectAll_Label".Translate(),
                    defaultDesc = "AerocraftFramework_ShowRadiusofRange_SelectAll_Desc".Translate(),
                    icon = def.uiIcon ?? BaseContent.BadTex,
                    hotKey = KeyBindingDefOf.Misc3,
                    action = () =>
                    {
                        List<Thing> list = Map.listerThings.ThingsOfDef(def);
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i].Faction == Faction)
                            {
                                Find.Selector.Select(list[i], playSound: false, forceDesignatorDeselect: false);
                            }
                        }
                    }
                };
            }
        }

        /// <summary>
        /// The weapon gizmo of this turret: target, stop, hold fire, fire modes, ammo and stored weapons in one
        /// place. The aircraft body shows the gizmos of all its weapons.
        /// </summary>
        public virtual IEnumerable<Gizmo> GetWeaponGizmos()
        {
            if (Gun_Now != null && CanSetForcedTarget)
            {
                yield return new Command_AerocraftWeapon(this);
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
            {
                yield return option;
            }
            if (Faction == Faction.OfPlayer)
            {
                foreach (FloatMenuOption option in AerocraftCompat.Ammo.GetReloadFloatMenuOptions(this, selPawn))
                {
                    yield return option;
                }
            }
        }

        public virtual void ToggleHoldFire()
        {
            SetHoldFire(!HoldFire);
        }

        /// <summary>
        /// Moves the forced target without restarting a warm-up in progress, as <see cref="OrderAttack"/> would: a
        /// strafing run walks the fire along its line every few ticks.
        /// </summary>
        public void RetargetForced(LocalTargetInfo target, bool skipWarmup = false)
        {
            if (!target.IsValid)
            {
                return;
            }
            forcedTarget = target;
            if (WarmupTicksLeft > 0)
            {
                CurrentTargetInt = target;
                if (skipWarmup)
                {
                    // Already aimed along the flight path: a CE gun would otherwise warm up for two or three
                    // seconds and a short run would be over before its first shot.
                    WarmupTicksLeft = 1;
                }
            }
        }

        /// <summary>Hold fire for this turret only (the body's <see cref="ToggleHoldFire"/> also covers its mounts).</summary>
        public void SetHoldFire(bool holdFire)
        {
            HoldFire = holdFire;
            if (HoldFire)
            {
                ResetForcedTarget();
            }
        }

        public void ResetForcedTarget()
        {
            forcedTarget = LocalTargetInfo.Invalid;
            WarmupTicksLeft = 0;
            if (CooldownTicksLeft <= 0 && Spawned)
            {
                TryStartShootSomething(canBeginBurstImmediately: false);
            }
        }

        public void ResetCurrentTarget()
        {
            CurrentTargetInt = LocalTargetInfo.Invalid;
            WarmupTicksLeft = 0;
        }

        /// <summary>Original CE build API: orders a colonist to reload the current weapon.</summary>
        public void TryOrderReload(bool forced = false)
        {
            if (Gun_Now != null && AerocraftCompat.Ammo.NeedsReload(Gun_Now))
            {
                AerocraftCompat.Ammo.TryOrderReload(this, Gun_Now, null, showMessages: forced);
            }
        }

        public void TryForceReload()
        {
            TryOrderReload(forced: true);
        }

        public void Set_ForceTarget(Thing Thing)
        {
            if (Thing == null)
            {
                return;
            }
            forcedTarget = Thing;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            if (Thing.Spawned)
            {
                FleckMaker.Static(Thing.DrawPos, Thing.Map, FleckDefOf.FeedbackShoot);
            }
        }

        // ------------------------------------------------------------------ weapon management

        /// <summary>Installs a new weapon as the current one; the previous one is kept in storage.</summary>
        public void Change_NowWeapon(Thing Thing)
        {
            if (Thing == null || Thing == Gun_Now)
            {
                return;
            }
            Gun_InnerList.Remove(Thing);
            if (Gun_Now != null)
            {
                Gun_InnerList.Add(Gun_Now);
            }
            Gun_Now = Thing;
            ResetCurrentTarget();
            UpdateGunVerbs();
        }

        /// <summary>Switches back to the weapon the turret was built with (recreated if it was lost).</summary>
        public void Change_NowWeapon_ToOrigin()
        {
            ThingDef originDef = OriginWeaponDef;
            if (originDef == null || (Gun_Now != null && Gun_Now.def == originDef))
            {
                return;
            }
            Thing origin = Gun_InnerList.FirstOrDefault(g => g.def == originDef);
            if (origin != null)
            {
                Change_NowWeapon_ByITab(origin);
            }
            else
            {
                MakeGun();
            }
        }

        /// <summary>Switches to the next stored weapon.</summary>
        public void Change_NowWeapon_SelectList()
        {
            if (Gun_InnerList.Count > 0)
            {
                Change_NowWeapon_ByITab(Gun_InnerList[0]);
            }
            else
            {
                Change_NowWeapon_ToOrigin();
            }
        }

        /// <summary>The current weapon was single-use and is spent: discard it and take another one.</summary>
        public void Change_NowWeapon_OneUse()
        {
            Thing spent = Gun_Now;
            Gun_Now = null;
            if (spent != null)
            {
                AerocraftCompat.Ammo.Notify_GunDetached(this, spent);
                if (!spent.Destroyed)
                {
                    spent.Destroy();
                }
            }
            ThingDef originDef = OriginWeaponDef;
            Thing next = Gun_InnerList.FirstOrDefault(g => g.def == originDef) ?? Gun_InnerList.FirstOrDefault();
            if (next != null)
            {
                Gun_InnerList.Remove(next);
                Gun_Now = next;
                UpdateGunVerbs();
            }
            else
            {
                MakeGun();
            }
            ResetCurrentTarget();
        }

        /// <summary>Makes a stored weapon the current one (the current one goes to storage).</summary>
        public void Change_NowWeapon_ByITab(Thing Thing)
        {
            if (Thing == null || Thing == Gun_Now || !Gun_InnerList.Contains(Thing))
            {
                return;
            }
            Gun_InnerList.Remove(Thing);
            if (Gun_Now != null)
            {
                Gun_InnerList.Add(Gun_Now);
            }
            Gun_Now = Thing;
            ResetCurrentTarget();
            UpdateGunVerbs();
        }

        /// <summary>Takes a weapon out of the turret (not the original one while it is the only weapon). Returns it unspawned.</summary>
        public bool TryRemoveWeapon(Thing gun)
        {
            if (gun == null)
            {
                return false;
            }
            if (gun == Gun_Now)
            {
                if (Gun_InnerList.Count == 0 && gun.def == OriginWeaponDef)
                {
                    return false;
                }
                Change_NowWeapon_SelectList();
                if (gun == Gun_Now)
                {
                    return false;
                }
            }
            if (!Gun_InnerList.Remove(gun))
            {
                return false;
            }
            DetachGun(gun);
            return true;
        }

        /// <summary>Drops a stored weapon next to the aircraft.</summary>
        public bool TryDropWeapon(Thing gun, bool forbid = true)
        {
            if (!Spawned || !TryRemoveWeapon(gun))
            {
                return false;
            }
            if (GenPlace.TryPlaceThing(gun, Position, Map, ThingPlaceMode.Near, out Thing placed) && forbid)
            {
                placed.SetForbidden(true, warnOnFail: false);
            }
            return true;
        }

        protected void DetachGun(Thing gun)
        {
            CompEquippable eq = gun.TryGetComp<CompEquippable>();
            if (eq != null)
            {
                List<Verb> verbs = eq.AllVerbs;
                for (int i = 0; i < verbs.Count; i++)
                {
                    verbs[i].caster = null;
                    verbs[i].castCompleteCallback = null;
                }
            }
            AerocraftCompat.Ammo.Notify_GunDetached(this, gun);
        }
    }
}
