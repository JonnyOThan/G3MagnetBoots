using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using UnityEngine;
using static KerbalEVA;

/*  For the future:
 *  Kerbals surface - aligned forward direction(the way they face) doesn't turn alongside the plane they are attached to, resulting in clear misaligned facing direction when the vessel beneath rotates.
 *  KSP2-style time-smoothed spherecasts and orientation changes with SMA Simple Moving Average filter to reduce jitter, consider a 2nd spherecast from a predicted future position to surface normal early.
 *  Consider adding sound effects for magnet engage/disengage, walking on hull, jumping off hull.
 *  Consider adding an LED indicator on the Kerbal suit boot model to show magnet status and provide slight illumination.
 *  Hook into stock EVA Ground science state changes (to allow playing golf on a ship and possibly collecting samples of a ship)
 */

namespace G3MagnetBoots
{
    public partial class ModuleG3MagnetBoots
    {
        public Vector3 HullNormal
        {
            get
            {
                if (_hullTarget.IsValid() && _hullTarget.hitNormal.sqrMagnitude > VECTOR_ZERO_THRESHOLD)
                    return _hullTarget.hitNormal.normalized;
                return Kerbal != null ? Kerbal.transform.up : Vector3.up;
            }
        }

        // Physics Constants
        internal const float SPHERECAST_UP_OFFSET_DEFAULT = 0.15f;
        internal const float SPHERECAST_RADIUS_DEFAULT = 0.25f;
        internal const float SPHERECAST_LENGTH_DEFAULT = 0.23f;
        internal const float ENGAGE_RADIUS_DEFAULT = 0.55f;
        internal const float ENGAGE_RADIUS_JETPACK_THRUSTING = 0.1f;
        internal const float RELEASE_RADIUS = 0.65f;
        internal const float FOOT_HULL_PAD = 0.05f;
        internal const float JETPACK_UP_THRUST_THRESHOLD = 0.15f;
        internal const float JETPACK_UP_THRUST_ATTACH_THRESHOLD = 0.05f;

        // Timing Constants
        internal const float ANIMATION_CROSSFADE_TIME = 0.2f;
        internal const float ANIMATION_CROSSFADE_TIME_LONG = 1.2f;
        internal const float IDLE_ANIMATION_CROSSFADE_TIME = 0.1f;
        internal const float JUMP_STILL_TIME_OFFSET = -0.2f;
        internal const float JUMP_STILL_DURATION = 0.2f;
        internal const float LOW_GEE_ANIMATION_SPEED = 2.7f;

        // Impulse & Force Constants
        internal const float LET_GO_IMPULSE_STRENGTH = 0.5f;
        internal const float ROTATION_RATE_MULTIPLIER = 360f;

        // Cooldown & Delay Constants
        internal const float LET_GO_COOLDOWN_TIME = 1.0f;
        internal const float JETPACK_DEPLOY_DELAY_JUMP = 1.0f;
        internal const float JETPACK_DEPLOY_DELAY_LETGO = 0.5f;
        internal const float LADDER_LETGO_SPHERECAST_BOOST_TIME = 2.0f;
        internal const float LADDER_LETGO_SPHERECAST_RADIUS_BOOST = 6.0f;
        internal const float LADDER_LETGO_SPHERECAST_LENGTH_BOOST = 2.0f;

        // Speed & Movement Constants
        internal const float MIN_MOVEMENT_THRESHOLD = 0.01f;
        internal const float MIN_SPEED_THRESHOLD = 0.2f;
        internal const float RELATIVE_VELOCITY_DISPLAY_SCALE = 0.4f;

        // EMA Smoothing Constants
        internal const float SURFACE_VELOCITY_SMOOTHING_TAU = 0.05f;

        // Vector Magnitude Thresholds
        internal const float VECTOR_ZERO_THRESHOLD = 1e-6f;
        internal const float VECTOR_ZERO_THRESHOLD_TIGHT = 1e-8f;
        internal const float VECTOR_ZERO_THRESHOLD_LOOSE = 1e-5f;

        // Quaternion Dot Product Thresholds
        internal const float QUAT_DOT_NEARLY_SAME = 0.9999f;
        internal const float QUAT_DOT_OPPOSITE = -0.9999f;
        internal const float QUAT_FLIP_ANGLE = 180f;

        // Camera Lock Constants
        internal const int OFF_HULL_FRAMES_TO_UNLOCK = 5;

        private static G3MagnetBootsSettings Settings => G3MagnetBootsSettings.Current;
        private static G3MagnetBootsDifficultySettings DifficultySettings => G3MagnetBootsDifficultySettings.Current;

        public KerbalEVA Kerbal { get; private set; }
        public KerbalFSM FSM { get { return Kerbal.fsm; } }
        public Part Part { get { return Kerbal.part; } }
        public ProtoCrewMember Crew { get { return Part.protoModuleCrew?.FirstOrDefault(); } }

        // Tech tree requirement check
        [KSPField] public string unlockTech = "";

        // Custom FSM States & FSM Events
        private KFSMState st_idle_hull;
        private KFSMEvent On_attachToHull; // st_idle_fl -> st_idle_hull
        private KFSMEvent On_detachFromHull; // st_idle_hull -> st_idle_fl
        private KFSMEvent On_letGo; // st_idle_hull OR st_walk_hull -> st_idle_fl

        private KFSMState st_walk_hull;
        private KFSMEvent On_MoveHull; // st_idle_hull -> st_walk_hull
        private KFSMEvent On_stopHull; // st_walk_hull -> st_idle_hull

        private KFSMState st_jump_hull;
        private KFSMEvent On_jump_hull; // st_idle_hull OR st_walk_hull -> st_jump_hull
        private KFSMTimedEvent On_jump_hull_completed; // st_jump_hull -> st_idle_fl

        // EVA science on Hull – tracks whether golf/flag-plant/weld was triggered from a hull state
        private bool _golfStartedFromHull;
        private bool _flagStartedFromHull;
        public bool _constructionFromHull;
        private bool _weldStartedFromHull;

        // KSP2 styled tuning
        public float GroundSpherecastUpOffset = SPHERECAST_UP_OFFSET_DEFAULT;
        public float GroundSpherecastRadius = SPHERECAST_RADIUS_DEFAULT;
        public float GroundSpherecastLength = SPHERECAST_LENGTH_DEFAULT;
        public int ContactNormalSmoothingSamples = 20;

        public float EngageRadius = ENGAGE_RADIUS_DEFAULT; // snap distance (feet -> hull)
        public float ReleaseRadius = RELEASE_RADIUS; // must be > EngageRadius
        public float FootHullPad = FOOT_HULL_PAD; // extra distance from foot to hull surface

        ScreenMessage _magMsg;

        private HullTarget _hullTarget;
        private Vector3 _localHullForward;       // kerbal forward stored in hull local space
        private Transform _hullTransform;        // hull part transform
        private bool _inLetGoCooldown;
        public bool _lastGear;
        public bool _lastOnHull;

        // Accessors for protected KerbalEVA fields (via EVAAccess utility)
        public float currentSpd
        {
            get => Kerbal != null ? KerbalEVAAccess.CurrentSpd(Kerbal) : 0f;
            set { if (Kerbal != null) KerbalEVAAccess.CurrentSpd(Kerbal) = value; }
        }
        public float tgtSpeed
        {
            get => Kerbal != null ? KerbalEVAAccess.TgtSpeed(Kerbal) : 0f;
            set { if (Kerbal != null) KerbalEVAAccess.TgtSpeed(Kerbal) = value; }
        }
        public float lastTgtSpeed
        {
            get => Kerbal != null ? KerbalEVAAccess.LastTgtSpeed(Kerbal) : 0f;
            set { if (Kerbal != null) KerbalEVAAccess.LastTgtSpeed(Kerbal) = value; }
        }
        public Vector3 cmdDir
        {
            get => Kerbal != null ? KerbalEVAAccess.CmdDir(Kerbal) : Vector3.zero;
            set { if (Kerbal != null) KerbalEVAAccess.CmdDir(Kerbal) = value; }
        }
        public Vector3 fUp
        {
            get => Kerbal != null ? KerbalEVAAccess.FUp(Kerbal) : Vector3.up;
            set { if (Kerbal != null) KerbalEVAAccess.FUp(Kerbal) = value; }
        }
        public Vector3 tgtRpos
        {
            get => Kerbal != null ? KerbalEVAAccess.TgtRpos(Kerbal) : Vector3.zero;
            set { if (Kerbal != null) KerbalEVAAccess.TgtRpos(Kerbal) = value; }
        }
        public Vector3 tgtFwd
        {
            get => Kerbal != null ? KerbalEVAAccess.TgtFwd(Kerbal) : Vector3.forward;
            set { if (Kerbal != null) KerbalEVAAccess.TgtFwd(Kerbal) = value; }
        }
        public float deltaHdg
        {
            get => Kerbal != null ? KerbalEVAAccess.DeltaHdg(Kerbal) : 0f;
            set { if (Kerbal != null) KerbalEVAAccess.DeltaHdg(Kerbal) = value; }
        }
        public float turnRate
        {
            get => Kerbal != null ? KerbalEVAAccess.TurnRate(Kerbal) : 0f;
            set { if (Kerbal != null) KerbalEVAAccess.TurnRate(Kerbal) = value; }
        }
        public Animation _animation
        {
            get => Kerbal != null ? KerbalEVAAccess.Animation(Kerbal) : null;
            set { if (Kerbal != null) KerbalEVAAccess.Animation(Kerbal) = value; }
        }
        public List<Collider> currentLadderTriggers
        {
            get => Kerbal != null ? KerbalEVAAccess.CurrentLadderTriggers(Kerbal) : null;
            set { if (Kerbal != null) KerbalEVAAccess.CurrentLadderTriggers(Kerbal) = value; }
        }
        public Vector3 packLinear
        {
            get => Kerbal != null ? KerbalEVAAccess.PackLinear(Kerbal) : Vector3.zero;
            set { if (Kerbal != null) KerbalEVAAccess.PackLinear(Kerbal) = value; }
        }
        public Vector3 packTgtRPos
        {
            get => Kerbal != null ? KerbalEVAAccess.PackTgtRPos(Kerbal) : Vector3.zero;
            set { if (Kerbal != null) KerbalEVAAccess.PackTgtRPos(Kerbal) = value; }
        }
        public float fuelFlowRate
        {
            get => Kerbal != null ? KerbalEVAAccess.FuelFlowRate(Kerbal) : 0f;
            set { if (Kerbal != null) KerbalEVAAccess.FuelFlowRate(Kerbal) = value; }
        }

        bool IsAGOn(KSPActionGroup g) => VesselUtils.IsAGOn(vessel, g);
        void SetAG(KSPActionGroup g, bool active) => VesselUtils.SetAG(vessel, g, active);
        void ToggleAG(KSPActionGroup g) => VesselUtils.ToggleAG(vessel, g);
        public bool IsGearOn => IsAGOn(KSPActionGroup.Gear);
        public bool IsOnHull =>
            this.enabled && _hullTarget.IsValid() && (
                FSM.CurrentState == st_idle_hull ||
                FSM.CurrentState == st_walk_hull ||
                FSM.CurrentState == st_jump_hull ||
                (FSM.CurrentState == Kerbal?.st_playing_golf && _golfStartedFromHull) ||
                (FSM.CurrentState == Kerbal?.st_flagAcquireHeading && _flagStartedFromHull) ||
                (FSM.CurrentState == Kerbal?.st_flagPlant && _flagStartedFromHull) ||
                (FSM.CurrentState == Kerbal?.st_enteringConstruction && _constructionFromHull) ||
                (FSM.CurrentState == Kerbal?.st_exitingConstruction && _constructionFromHull) ||
                (FSM.CurrentState == Kerbal?.st_weldAcquireHeading && _constructionFromHull) ||
                (FSM.CurrentState == Kerbal?.st_weld && _constructionFromHull)
            );

        public bool FlagStartedFromHull => _flagStartedFromHull;
        public bool HullTargetIsValid => _hullTarget.IsValid();
        public string CurrentFSMStateName => FSM?.CurrentState?.name ?? "null";
        public Rigidbody HullTargetRigidbody => _hullTarget.rigidbody;
        public bool VesselUnderControl => VesselUtils.VesselUnderControl(Kerbal);
        public bool IsJetpackThrustingUp = false;

        [KSPEvent(guiActive = true, guiName = "#autoLOC_6003095", active = false)]
        public void PlantFlagOnHull()
        {
            if (Kerbal == null || !IsOnHull) return;
            Kerbal.flagItems--;
            Kerbal.fsm.RunEvent(Kerbal.On_flagPlantStart);
        }

        public void UpdatePlantFlagOnHullButton()
        {
            if (Kerbal == null) return;
            Events["PlantFlagOnHull"].active = IsOnHull
                && Kerbal.vessel.state == Vessel.State.ACTIVE
                && Kerbal.flagItems > 0
                && !Kerbal.isRagdoll
                && GameVariables.Instance.UnlockedEVAFlags(ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex))
                && !Kerbal.InConstructionMode;
        }


        private bool IsTechUnlocked(string techId = "advExploration")
        {
            techId = string.IsNullOrEmpty(techId) ? unlockTech : techId;
            if (HighLogic.CurrentGame == null) return true;
            if (HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX) return true; // Sandbox always unlocked
            if (ResearchAndDevelopment.Instance == null) return false;
            if (string.IsNullOrEmpty(techId)) return true; // no tech required
            return ResearchAndDevelopment.GetTechnologyState(techId) == RDTech.State.Available;
        }

        public bool IsAboveHighAltitude()
        {
            if (Part.rb != null)
            {
                float groundAndSeaHighAltitude = 3500f;
                return vessel.altitude > (double)groundAndSeaHighAltitude && vessel.heightFromTerrain > (double)groundAndSeaHighAltitude;
            }
            return true;
        }

        public void SetEnabled(bool enabled)
        {
            if (IsTechUnlocked())
            {
                this.enabled = enabled;
                SetAG(KSPActionGroup.Gear, enabled); // sync to the EVA vessel's Gear AG
            }
            else
            {
                this.enabled = false;
                SetAG(KSPActionGroup.Gear, false); // sync to the EVA vessel's Gear AG
            }
        }

        // MonoBehaviour... behavior?
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!HighLogic.LoadedSceneIsFlight) return;

            //_viz = new("MagBootsPoC_DebugViz");
            //_viz.Create();
            //_viz.Enabled = Viz;

            _inLetGoCooldown = false;
            HookAGGearButton();
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            UpdateUI();

            if (!IsTechUnlocked())
            {
                this.enabled = false;
                return;
            }

            IsJetpackThrustingUp = Vector3.Dot(Vector3.Project(packTgtRPos, Kerbal.transform.up), Kerbal.transform.up) > JETPACK_UP_THRUST_THRESHOLD;
            EngageRadius = (Kerbal != null && Kerbal.JetpackDeployed && Kerbal.Fuel > 0.0 && Kerbal.thrustPercentage > 0f && IsJetpackThrustingUp) ? ENGAGE_RADIUS_JETPACK_THRUSTING : ENGAGE_RADIUS_DEFAULT;
        }

        // EVA Hookup via Harmony Patch
        private int _lastFsmHash;
        private bool _installed;
        internal void HookIntoEva(KerbalEVA eva)
        {
            Kerbal = eva;

            int fsmHash = RuntimeHelpers.GetHashCode(FSM);
            if (_installed && fsmHash == _lastFsmHash) return;

            _lastFsmHash = fsmHash;
            _installed = false;

            try
            {
                SetupFSM();
            }
            catch (Exception ex)
            {
                Logger.Error($"Init failed for {Kerbal?.name}: {ex}");
                _installed = false;
            }
        }

        protected virtual void SetupFSM()
        {
            if (_installed) return;
            _installed = true;
            Logger.Trace();

            // Idle (On Hull) State
            st_idle_hull = new("Idle (On Hull)");
            st_idle_hull.updateMode = KFSMUpdateMode.FIXEDUPDATE;
            st_idle_hull.OnEnter = idle_hull_OnEnter;
            st_idle_hull.OnFixedUpdate = RefreshHullTarget;
            st_idle_hull.OnFixedUpdate += OrientToSurfaceNormal;
            st_idle_hull.OnFixedUpdate += UpdateMovementOnVessel;
            st_idle_hull.OnFixedUpdate += UpdateHeading;
            st_idle_hull.OnFixedUpdate += UpdatePackLinear;
            st_idle_hull.OnFixedUpdate += updateRagdollVelocities;
            FSM.AddState(st_idle_hull);

            FSM.AddEvent(Kerbal.On_packToggle, st_idle_hull);
            FSM.AddEvent(Kerbal.On_stumble, st_idle_hull);
            FSM.AddEvent(Kerbal.On_ladderGrabStart, st_idle_hull);
            FSM.AddEvent(Kerbal.On_flagPlantStart, st_idle_hull);
            FSM.AddEvent(Kerbal.On_boardPart, st_idle_hull);

            // Attach / Detach Events
            On_attachToHull = new("Attach to Hull");
            On_attachToHull.updateMode = KFSMUpdateMode.FIXEDUPDATE;
            On_attachToHull.GoToStateOnEvent = st_idle_hull;
            On_attachToHull.OnCheckCondition = currentState => ShouldEnterHullIdle();
            FSM.AddEvent(On_attachToHull, Kerbal.st_idle_fl);

            On_detachFromHull = new("Detach from Hull");
            On_detachFromHull.updateMode = KFSMUpdateMode.FIXEDUPDATE;
            On_detachFromHull.GoToStateOnEvent = Kerbal.st_idle_fl;
            On_detachFromHull.OnCheckCondition = _ => ShouldExitHullIdle();
            FSM.AddEvent(On_detachFromHull, st_idle_hull);

            On_letGo = new("Let go from Hull");
            On_letGo.updateMode = KFSMUpdateMode.FIXEDUPDATE;
            On_letGo.GoToStateOnEvent = Kerbal.st_idle_fl;
            On_letGo.OnCheckCondition = currentState =>
                GameSettings.EVA_Jump.GetKey()
                && !GameSettings.EVA_Run.GetKey()
                && VesselUnderControl
                && !Kerbal.PartPlacementMode
                && !EVAConstructionModeController.MovementRestricted
                && _hullTarget.IsValid()
                && !Kerbal.vessel.packed;

            On_letGo.OnEvent = On_letGoFromHull;
            FSM.AddEvent(On_letGo, st_idle_hull);

            // Walk (On Hull) State
            st_walk_hull = new("Walk (On Hull)");
            st_walk_hull.updateMode = KFSMUpdateMode.FIXEDUPDATE;
            st_walk_hull.OnFixedUpdate = RefreshHullTarget;
            st_walk_hull.OnFixedUpdate += OrientToSurfaceNormal;
            st_walk_hull.OnFixedUpdate += UpdateHullInputTargets;
            st_walk_hull.OnFixedUpdate += walk_hull_OnUpdate;
            st_walk_hull.OnFixedUpdate += UpdateMovementOnVessel;
            st_walk_hull.OnFixedUpdate += UpdateHeading;
            st_walk_hull.OnFixedUpdate += UpdatePackLinear;
            st_walk_hull.OnFixedUpdate += updateRagdollVelocities;
            st_walk_hull.OnLeave = walk_hull_OnLeave;
            FSM.AddState(st_walk_hull);

            FSM.AddEvent(On_detachFromHull, st_walk_hull);
            FSM.AddEvent(On_letGo, st_walk_hull);
            FSM.AddEvent(Kerbal.On_packToggle, st_walk_hull);
            FSM.AddEvent(Kerbal.On_stumble, st_walk_hull);
            FSM.AddEvent(Kerbal.On_ladderGrabStart, st_walk_hull);
            FSM.AddEvent(Kerbal.On_flagPlantStart, st_walk_hull);
            FSM.AddEvent(Kerbal.On_boardPart, st_walk_hull);

            // Move (Hull) Event
            On_MoveHull = new("Move (Hull / FPS)");
            On_MoveHull.updateMode = KFSMUpdateMode.FIXEDUPDATE;
            On_MoveHull.GoToStateOnEvent = st_walk_hull;
            On_MoveHull.OnCheckCondition = currentState =>
                tgtRpos != Vector3.zero
                && _hullTarget.IsValid()
                && !EVAConstructionModeController.MovementRestricted
                && VesselUnderControl
                && !Kerbal.vessel.packed;
            FSM.AddEvent(On_MoveHull, st_idle_hull);

            // Stop (Hull) Event
            On_stopHull = new("Stop (Hull)");
            On_stopHull.updateMode = KFSMUpdateMode.FIXEDUPDATE;
            On_stopHull.GoToStateOnEvent = st_idle_hull;
            On_stopHull.OnCheckCondition = currentState =>
                tgtRpos == Vector3.zero
                || EVAConstructionModeController.MovementRestricted
                || !VesselUnderControl
                || Kerbal.vessel.packed;
            FSM.AddEvent(On_stopHull, st_walk_hull);

            // Jumping (On Hull) State
            st_jump_hull = new KFSMState("Jumping (On Hull)");
            st_jump_hull.updateMode = KFSMUpdateMode.FIXEDUPDATE;
            st_jump_hull.OnEnter = jump_hull_OnEnter;
            st_jump_hull.OnFixedUpdate = UpdateMovementOnVessel;
            st_jump_hull.OnFixedUpdate += UpdateHeading;
            st_jump_hull.OnFixedUpdate += updateRagdollVelocities;
            FSM.AddState(st_jump_hull);

            On_jump_hull = new KFSMEvent("Jump (On Hull)");
            On_jump_hull.updateMode = KFSMUpdateMode.FIXEDUPDATE;
            On_jump_hull.GoToStateOnEvent = st_jump_hull;
            On_jump_hull.OnCheckCondition = currentState =>
                GameSettings.EVA_Jump.GetKey() && GameSettings.EVA_Run.GetKey() &&
                VesselUnderControl &&
                !Kerbal.PartPlacementMode &&
                !EVAConstructionModeController.MovementRestricted &&
                _hullTarget.IsValid() &&
                !Kerbal.vessel.packed;
            On_jump_hull.OnEvent += delegate
            {
                Kerbal.StartCoroutine(AutoDeployJetpack_Coroutine(JETPACK_DEPLOY_DELAY_JUMP));
                part?.FindModuleImplementing<ModuleG3VelocityMatch>()?.SetEngageDelay(JETPACK_DEPLOY_DELAY_JUMP * 2f);
            };
            FSM.AddEvent(On_jump_hull, st_idle_hull, st_walk_hull);

            On_jump_hull_completed = new KFSMTimedEvent("Jump (On Hull) Complete", 0.3);
            On_jump_hull_completed.GoToStateOnEvent = Kerbal.st_idle_fl;
            On_jump_hull_completed.OnEvent = jump_hull_Completed;
            FSM.AddEvent(On_jump_hull_completed, st_jump_hull);

            // Stock Ladder Let Go Event augment
            Kerbal.On_ladderLetGo.OnEvent += delegate
            {
                SetAG(KSPActionGroup.Gear, true);
                Kerbal.StartCoroutine(On_ladderLetGo_Coroutine());
            };

            // Zero movement and record origin when flag plant starts from hull
            Kerbal.On_flagPlantStart.OnEvent -= On_flagPlantStart_Hull_Hook;
            Kerbal.On_flagPlantStart.OnEvent += On_flagPlantStart_Hull_Hook;

            // Run hull grip physics during both flag FSM states
            Kerbal.st_flagAcquireHeading.OnFixedUpdate -= flag_hull_OnFixedUpdate;
            Kerbal.st_flagAcquireHeading.OnFixedUpdate += flag_hull_OnFixedUpdate;
            Kerbal.st_flagPlant.OnFixedUpdate -= flag_hull_OnFixedUpdate;
            Kerbal.st_flagPlant.OnFixedUpdate += flag_hull_OnFixedUpdate;

            // Redirect flag-plant completion back to st_idle_hull
            Kerbal.On_flagPlantComplete.OnEvent -= On_flagPlantComplete_Hull_Redirect;
            Kerbal.On_flagPlantComplete.OnEvent += On_flagPlantComplete_Hull_Redirect;

            // EVA Science on Hull – allow golf to trigger from hull states
            FSM.AddEvent(Kerbal.On_Playing_Golf, st_idle_hull);
            FSM.AddEvent(Kerbal.On_Playing_Golf, st_walk_hull);

            // Unsubscribe before subscribing so FSM recreation never double-registers.

            // Zero movement when golf is triggered from hull
            Kerbal.On_Playing_Golf.OnEvent -= On_Playing_Golf_Hull_Hook;
            Kerbal.On_Playing_Golf.OnEvent += On_Playing_Golf_Hull_Hook;

            // Run hull grip physics during the golf animation
            Kerbal.st_playing_golf.OnFixedUpdate -= playing_golf_hull_OnFixedUpdate;
            Kerbal.st_playing_golf.OnFixedUpdate += playing_golf_hull_OnFixedUpdate;

            // Redirect golf completion back to st_idle_hull instead of the vanilla ground state
            Kerbal.On_Golf_Complete.OnEvent -= On_Golf_Complete_Hull_Redirect;
            Kerbal.On_Golf_Complete.OnEvent += On_Golf_Complete_Hull_Redirect;


            // EVA Construction pipeline
            FSM.AddEvent(Kerbal.On_constructionModeEnter, st_idle_hull);
            FSM.AddEvent(Kerbal.On_constructionModeEnter, st_walk_hull);
            FSM.AddEvent(Kerbal.On_constructionModeExit, st_idle_hull);
            FSM.AddEvent(Kerbal.On_constructionModeExit, st_walk_hull);

            Kerbal.st_enteringConstruction.OnFixedUpdate -= construction_hull_OnFixedUpdate;
            Kerbal.st_enteringConstruction.OnFixedUpdate += construction_hull_OnFixedUpdate;
            Kerbal.st_exitingConstruction.OnFixedUpdate -= construction_hull_OnFixedUpdate;
            Kerbal.st_exitingConstruction.OnFixedUpdate += construction_hull_OnFixedUpdate;

            Kerbal.On_constructionModeEnter.OnEvent -= On_ConstructionEnter_Hull_Hook;
            Kerbal.On_constructionModeEnter.OnEvent += On_ConstructionEnter_Hull_Hook;
            Kerbal.On_constructionModeExit.OnEvent -= On_ConstructionExit_Hull_Hook;
            Kerbal.On_constructionModeExit.OnEvent += On_ConstructionExit_Hull_Hook;
            Kerbal.On_constructionModeTrigger_fl_Complete.OnEvent -= On_ConstructionComplete_Hull_Redirect;
            Kerbal.On_constructionModeTrigger_fl_Complete.OnEvent += On_ConstructionComplete_Hull_Redirect;
            Kerbal.On_constructionModeTrigger_gr_Complete.OnEvent -= On_ConstructionComplete_Hull_Redirect;
            Kerbal.On_constructionModeTrigger_gr_Complete.OnEvent += On_ConstructionComplete_Hull_Redirect;

            // Welding on hull – allow weld to trigger from hull states
            FSM.AddEvent(Kerbal.On_weldStart, st_idle_hull);
            FSM.AddEvent(Kerbal.On_weldStart, st_walk_hull);

            // Zero movement and set flag when weld is triggered from hull
            Kerbal.On_weldStart.OnEvent -= On_weldStart_Hull_Hook;
            Kerbal.On_weldStart.OnEvent += On_weldStart_Hull_Hook;

            // Run hull grip physics during weld acquire-heading and weld animation
            Kerbal.st_weldAcquireHeading.OnFixedUpdate -= weld_hull_OnFixedUpdate;
            Kerbal.st_weldAcquireHeading.OnFixedUpdate += weld_hull_OnFixedUpdate;
            Kerbal.st_weld.OnFixedUpdate -= weld_hull_OnFixedUpdate;
            Kerbal.st_weld.OnFixedUpdate += weld_hull_OnFixedUpdate;

            // Hull-relative heading update during weld acquire-heading
            Kerbal.st_weldAcquireHeading.OnLateUpdate -= weldAcquireHeading_hull_OnLateUpdate;
            Kerbal.st_weldAcquireHeading.OnLateUpdate += weldAcquireHeading_hull_OnLateUpdate;

            // Redirect weld completion back to st_idle_hull
            Kerbal.On_weldComplete.OnEvent -= On_weldComplete_Hull_Redirect;
            Kerbal.On_weldComplete.OnEvent += On_weldComplete_Hull_Redirect;
        }



        protected virtual bool ShouldEnterHullIdle()
        {
            if (!this.enabled || _inLetGoCooldown) return false;

            RefreshHullTarget_DoProbe();

            if (!_hullTarget.IsValid()) return false;

            if (DifficultySettings.magbootsRequireHighAltitudeEnabled && !IsAboveHighAltitude()) return false;
            if (HullTargeting.GetRelativeSpeedToHullPoint(_hullTarget, base.part) > Kerbal.stumbleThreshold) return false;

            // Don't attach while commanding upward jetpack thrust (relative to Kerbal up)
            if (Kerbal != null && Kerbal.JetpackDeployed && Kerbal.Fuel > 0.0 && Kerbal.thrustPercentage > 0f)
            {
                if (Vector3.Dot(packTgtRPos, Kerbal.transform.up) > JETPACK_UP_THRUST_ATTACH_THRESHOLD)
                    return false;
            }

            return true;
        }

        public void ClearHullTarget()
        {
            _hullTarget = default;
            _hullTransform = null;
            _surfaceVelCount = 0;
        }


        protected virtual bool ShouldExitHullIdle()
        {
            if (!_hullTarget.IsValid()) { ClearHullTarget(); return true; }

            if (DifficultySettings.magbootsRequireHighAltitudeEnabled && !IsAboveHighAltitude()) { ClearHullTarget(); return true; }
            if (HullTargeting.GetRelativeSpeedToHullPoint(_hullTarget, base.part) > Kerbal.stumbleThreshold) { ClearHullTarget(); return true; }

            return false;
        }

        protected virtual void idle_hull_OnEnter(KFSMState s)
        {
            Kerbal.Events["PlantFlag"].active = DifficultySettings.magbootsPlantFlagEnabled;
            tgtSpeed = 0f;
            currentSpd = 0f;
            KerbalEVAAccess.KerbalAnchorTimeCounter(Kerbal) = 0f;
            RefreshHullTarget();
            OrientToSurfaceNormal();

            // Allow repacking chute while on hull
            if (KerbalEVAAccess.EvaChute(Kerbal) != null)
            {
                KerbalEVAAccess.EvaChute(Kerbal).AllowRepack(allowRepack: DifficultySettings.magbootsRepackChuteEnabled);
            }

            _animation.CrossFade(Kerbal.Animations.idle, ANIMATION_CROSSFADE_TIME_LONG, PlayMode.StopSameLayer);
            if (_hullTarget.part != null)
            {
                _hullTransform = _hullTarget.part.transform;
                _localHullForward = _hullTransform.InverseTransformDirection(Part.rb.transform.forward); //
            }
        }

        // Stock-alike but using the Kerbal-relative coordinate frame
        protected virtual void walk_hull_OnUpdate()
        {
            float fwdDot = Vector3.Dot(tgtRpos, Kerbal.transform.forward);
            float fwdPos = Mathf.Clamp01(fwdDot);
            float fwdNeg = Mathf.Clamp01(-fwdDot);

            float rightDot = Vector3.Dot(tgtRpos, Kerbal.transform.right);
            float rightPos = Mathf.Clamp01(rightDot);
            float rightNeg = Mathf.Clamp01(-rightDot);

            tgtSpeed = Kerbal.walkSpeed * (fwdPos + fwdNeg) + Kerbal.strafeSpeed * (rightPos + rightNeg);

            if (fwdPos > MIN_MOVEMENT_THRESHOLD)
            {
                _animation.CrossFade(Kerbal.Animations.walkFwd, ANIMATION_CROSSFADE_TIME);
                _animation.Blend(Kerbal.Animations.walkLowGee,
                    Mathf.InverseLerp(1f, Kerbal.minWalkingGee, (float)Kerbal.vessel.mainBody.GeeASL));
                Kerbal.Animations.walkLowGee.State.speed = LOW_GEE_ANIMATION_SPEED;

                if (rightPos > 0f)
                    _animation.Blend(Kerbal.Animations.strafeRight, rightPos);
                else if (rightNeg > 0f)
                    _animation.Blend(Kerbal.Animations.strafeLeft, rightNeg);
            }
            else if (fwdNeg > MIN_MOVEMENT_THRESHOLD)
            {
                _animation.CrossFade(Kerbal.Animations.walkBack, ANIMATION_CROSSFADE_TIME);

                if (rightPos > 0f)
                    _animation.Blend(Kerbal.Animations.strafeRight, rightPos);
                else if (rightNeg > 0f)
                    _animation.Blend(Kerbal.Animations.strafeLeft, rightNeg);
            }
            else if (rightPos > MIN_MOVEMENT_THRESHOLD)
            {
                _animation.CrossFade(Kerbal.Animations.strafeRight, ANIMATION_CROSSFADE_TIME);
            }
            else if (rightNeg > MIN_MOVEMENT_THRESHOLD)
            {
                _animation.CrossFade(Kerbal.Animations.strafeLeft, ANIMATION_CROSSFADE_TIME);
            }
        }

        protected virtual void walk_hull_OnLeave(KFSMState s)
        {
            lastTgtSpeed = tgtSpeed;
        }
        protected virtual void jump_hull_OnEnter(KFSMState st)
        {
            if (tgtSpeed < MIN_SPEED_THRESHOLD)
            {
                // standing jump
                On_jump_hull_completed.TimerDuration = JUMP_STILL_DURATION;
                Kerbal.Animations.JumpStillStart.State.time = JUMP_STILL_TIME_OFFSET;
                _animation.CrossFade(Kerbal.Animations.JumpStillStart, ANIMATION_CROSSFADE_TIME, PlayMode.StopAll);
            }
            else
            {
                // running jump
                On_jump_hull_completed.TimerDuration = Kerbal.Animations.JumpFwdStart.end;
                Kerbal.Animations.JumpFwdStart.State.time = Kerbal.Animations.JumpFwdStart.start;
                _animation.CrossFade(Kerbal.Animations.JumpFwdStart, ANIMATION_CROSSFADE_TIME, PlayMode.StopAll);
            }
        }
        protected virtual void jump_hull_Completed()
        {
            Vector3 impulse = (Kerbal.transform.up * Mathf.Pow(Part.mass / PhysicsGlobals.PerCommandSeatReduction, Kerbal.jumpMultiplier) * Kerbal.maxJumpForce) + (Kerbal.transform.forward * tgtSpeed * Kerbal.massMultiplier);
            Part.AddImpulse(impulse);

            var endAnim = (tgtSpeed < MIN_SPEED_THRESHOLD)
                ? Kerbal.Animations.JumpStillEnd
                : Kerbal.Animations.JumpFwdEnd;
            _animation.CrossFade(endAnim, IDLE_ANIMATION_CROSSFADE_TIME, PlayMode.StopAll);
        }

        private IEnumerator On_ladderLetGo_Coroutine()
        {
            GroundSpherecastRadius += LADDER_LETGO_SPHERECAST_RADIUS_BOOST;
            GroundSpherecastLength += LADDER_LETGO_SPHERECAST_LENGTH_BOOST;
            yield return new WaitForSeconds(LADDER_LETGO_SPHERECAST_BOOST_TIME);
            GroundSpherecastRadius -= LADDER_LETGO_SPHERECAST_RADIUS_BOOST;
            GroundSpherecastLength -= LADDER_LETGO_SPHERECAST_LENGTH_BOOST;
        }

        // Helper: zero Kerbal movement state when triggering EVA science from a hull state.
        private void ZeroHullMovementForScience()
        {
            tgtRpos = Vector3.zero;
            tgtSpeed = 0f;
            lastTgtSpeed = 0f;
            currentSpd = 0f;
            if (Part.rb != null)
                Part.rb.angularVelocity = Vector3.zero;
        }

        private void On_Playing_Golf_Hull_Hook()
        {
            if (FSM.CurrentState != st_idle_hull && FSM.CurrentState != st_walk_hull) return;
            _golfStartedFromHull = true;
            ZeroHullMovementForScience();
        }

        private void playing_golf_hull_OnFixedUpdate()
        {
            if (!_golfStartedFromHull) return;
            RefreshHullTarget();
            OrientToSurfaceNormal();
            UpdateMovementOnVessel();
        }

        private void On_Golf_Complete_Hull_Redirect()
        {
            if (_golfStartedFromHull)
            {
                Kerbal.On_Golf_Complete.GoToStateOnEvent = st_idle_hull;
                _golfStartedFromHull = false;
            }
            else
            {
                Kerbal.On_Golf_Complete.GoToStateOnEvent = Kerbal.st_idle_gr;
            }
        }

        private void construction_hull_OnFixedUpdate()
        {
            if (!_constructionFromHull) return;
            RefreshHullTarget();
            OrientToSurfaceNormal();
            UpdateMovementOnVessel();

            KerbalEVAAccess.CmdRot(Kerbal) = Vector3.zero;
            updateRagdollVelocities();
        }

        private void On_ConstructionEnter_Hull_Hook()
        {
            if (FSM.CurrentState == st_idle_hull || FSM.CurrentState == st_walk_hull)
            {
                _constructionFromHull = true;
                ZeroHullMovementForScience();
            }
        }

        private void On_ConstructionExit_Hull_Hook()
        {
            if (!_constructionFromHull) return;
            ZeroHullMovementForScience();
        }

        private void On_ConstructionComplete_Hull_Redirect()
        {
            if (_constructionFromHull)
            {
                Kerbal.On_constructionModeTrigger_fl_Complete.GoToStateOnEvent = st_idle_hull;
                Kerbal.On_constructionModeTrigger_gr_Complete.GoToStateOnEvent = st_idle_hull;

                // Clear only when exiting construction mode, not entering
                if (FSM.CurrentState == Kerbal.st_exitingConstruction)
                    _constructionFromHull = false;
            }
            else
            {
                Kerbal.On_constructionModeTrigger_fl_Complete.GoToStateOnEvent = Kerbal.st_idle_fl;
                Kerbal.On_constructionModeTrigger_gr_Complete.GoToStateOnEvent = Kerbal.st_idle_gr;
            }
        }



        private void On_weldStart_Hull_Hook()
        {
            if (FSM.CurrentState != st_idle_hull && FSM.CurrentState != st_walk_hull) return;
            _weldStartedFromHull = true;
            ZeroHullMovementForScience();
        }

        private void weld_hull_OnFixedUpdate()
        {
            if (!_constructionFromHull) return;
            RefreshHullTarget();
            OrientToSurfaceNormal();
            UpdateMovementOnVessel();
           
            KerbalEVAAccess.CmdRot(Kerbal) = Vector3.zero;
            updateRagdollVelocities();
        }

        private void weldAcquireHeading_hull_OnLateUpdate()
        {
            if (!_weldStartedFromHull) return;
            UpdateHeading();
        }

        private void On_weldComplete_Hull_Redirect()
        {
            if (_weldStartedFromHull)
            {
                Kerbal.On_weldComplete.GoToStateOnEvent = st_idle_hull;
                _weldStartedFromHull = false;
            }
            else
            {
                Kerbal.On_weldComplete.GoToStateOnEvent = Kerbal.st_idle_gr;
            }
        }

        private void On_flagPlantStart_Hull_Hook()
        {
            if (FSM.CurrentState != st_idle_hull && FSM.CurrentState != st_walk_hull) return;
            _flagStartedFromHull = true;
            ZeroHullMovementForScience();
        }

        private void flag_hull_OnFixedUpdate()
        {
            if (!_flagStartedFromHull) return;
            RefreshHullTarget();
            OrientToSurfaceNormal();
            UpdateMovementOnVessel();
        }

        private void On_flagPlantComplete_Hull_Redirect()
        {
            if (_flagStartedFromHull)
            {
                Kerbal.On_flagPlantComplete.GoToStateOnEvent = st_idle_hull;
                _flagStartedFromHull = false;
            }
            else
            {
                Kerbal.On_flagPlantComplete.GoToStateOnEvent = Kerbal.st_idle_gr;
            }
        }

        private void ApplyLetGoImpulse()
        {
            Part.rb.velocity += Kerbal.transform.up * LET_GO_IMPULSE_STRENGTH;
        }

        private void On_letGoFromHull()
        {
            _inLetGoCooldown = true;
            SetEnabled(false);
            ClearHullTarget();
            ApplyLetGoImpulse();
            Kerbal.StartCoroutine(On_letGo_Coroutine(LET_GO_COOLDOWN_TIME));
            Kerbal.StartCoroutine(AutoDeployJetpack_Coroutine(JETPACK_DEPLOY_DELAY_LETGO));
            part?.FindModuleImplementing<ModuleG3VelocityMatch>()?.SetEngageDelay(JETPACK_DEPLOY_DELAY_LETGO);
        }

        private IEnumerator On_letGo_Coroutine(float delay = 2.0f)
        {
            yield return new WaitForSeconds(delay);
            SetEnabled(true);
            _inLetGoCooldown = false;
        }

        private IEnumerator AutoDeployJetpack_Coroutine(float delay = 1.0f)
        {
            yield return new WaitForSeconds(delay);
            if (Kerbal.HasJetpack && !Kerbal.JetpackDeployed)
            {
                SetToggleJetpack(true);
            }
        }

        private void SetToggleJetpack(bool enable)
        {
            if (Settings.jetpackAutoToggleEnabled && Kerbal.HasJetpack)
            {
                if (enable && !Kerbal.JetpackDeployed)
                {
                    Kerbal.ToggleJetpack();
                }
                else if (!enable && Kerbal.JetpackDeployed)
                {
                    Kerbal.ToggleJetpack();
                }
            }
        }

        private bool _doProbeRay = false;
        protected virtual void RefreshHullTarget_DoProbe()
        {
            _doProbeRay = true;
            try { RefreshHullTarget(); }
            finally { _doProbeRay = false; }
        }

        protected virtual void RefreshHullTarget()
        {
            if (!this.enabled || FSM.CurrentState == st_jump_hull)
            {
                ClearHullTarget();
                return;
            }

            if (!HullTargeting.TryAcquireHullSpherecast(
                Kerbal,
                (!_hullTarget.IsValid() && _doProbeRay), // doProbeRay
                GroundSpherecastUpOffset,
                GroundSpherecastRadius,
                GroundSpherecastLength,
                EngageRadius,
                out HullTarget target))
            {
                ClearHullTarget();
                return;
            }
            _hullTarget = target;
            _hullTransform = _hullTarget.part?.transform;
            _localHullForward = Vector3.zero; // re-init next orient
        }

        protected virtual void UpdateHeading()
        {
            if (base.vessel.packed || !_hullTarget.IsValid()) return;

            var rb = Part.rb;
            if (rb == null) return;

            Vector3 curFwd = Vector3.ProjectOnPlane(base.transform.forward, fUp);
            Vector3 desFwd = Vector3.ProjectOnPlane(tgtFwd, fUp);

            if (curFwd.sqrMagnitude < VECTOR_ZERO_THRESHOLD || desFwd.sqrMagnitude < VECTOR_ZERO_THRESHOLD) return;

            curFwd.Normalize();
            desFwd.Normalize();

            deltaHdg = Vector3.SignedAngle(curFwd, desFwd, fUp);
            float sign = Mathf.Sign(deltaHdg);

            if (Mathf.Abs(deltaHdg) < turnRate * 2f)
                rb.angularVelocity = deltaHdg * 0.5f * fUp;
            else
                rb.angularVelocity = turnRate * sign * fUp;
        }

        protected virtual void UpdatePackLinear()
        {
            // Same as stock but blocked if not on hull
            if (!Kerbal.JetpackDeployed) return;
            if (base.vessel.packed || Kerbal.isRagdoll || EVAConstructionModeController.MovementRestricted) return;
            if (!_hullTarget.IsValid()) return;

            // Only allow up/down thrust when on hull
            Vector3 vertical = Vector3.Project(packTgtRPos, Kerbal.transform.up);
            packLinear = vertical * (Kerbal.thrustPercentage * 0.01f);
            if (packLinear != Vector3.zero && Kerbal.Fuel > 0.0)
            {
                base.part.AddForce(packLinear * Kerbal.linPower);
                fuelFlowRate += packLinear.magnitude * Time.fixedDeltaTime;
            }

        }

        protected virtual void updateRagdollVelocities()
        {
            if (!base.vessel.packed)
            {
                int num = Kerbal.ragdollNodes.Length;
                while (num-- > 0)
                {
                    Kerbal.ragdollNodes[num].updateVelocity(base.transform.position, base.part.rb.velocity, 1f / Time.fixedDeltaTime);
                }
            }
        }

        // Stock-alike custom implementation replacing correctGroundedRotation() which uses the scene-fixed coordinate frame
        private void OrientToSurfaceNormal()
        {
            if (base.vessel.packed || !_hullTarget.IsValid()) return;

            var rb = Part.rb;
            if (rb == null) return;

            Vector3 upN = _hullTarget.hitNormal;
            fUp = upN;

            /*
            if (_viz != null && _viz.Enabled)
            {
                _viz.BeginFrame();

                if (Kerbal.footPivot != null) 
                { 
                    // Contact point + normal is YELLOW
                    _viz.Sphere("hitPoint", _hullTarget.hitPoint, 0.05f, Color.yellow, 0.20f);
                    _viz.Line("hitNormal",
                        _hullTarget.hitPoint,
                        _hullTarget.hitPoint + _hullTarget.hitNormal.normalized,
                        Color.yellow, 0.02f);

                    // Kerbal up (fUp) is RED
                    var kerbalPos = Part.rb != null ? Part.rb.worldCenterOfMass : Kerbal.transform.position;
                    _viz.Line("fUp",
                        kerbalPos,
                        kerbalPos + fUp.normalized,
                        Color.red, 0.02f);

                    // Current forward projected on plane is BLUE
                    if (Part.rb != null)
                    {
                        var curFwd = Vector3.ProjectOnPlane(Part.rb.rotation * Vector3.forward, fUp);
                        if (curFwd.sqrMagnitude > 1e-6f)
                        {
                            curFwd.Normalize();
                            _viz.Line("curFwd",
                                kerbalPos,
                                kerbalPos + curFwd * 0.7f,
                                Color.blue, 0.02f);
                        }
                    }

                    // Relative velocity to hull point is ORANGE
                    if (Part.rb != null)
                    {
                        var surfaceVel = HullTargeting.GetSurfacePointVelocity(_hullTarget);
                        var relVel = Part.rb.velocity - surfaceVel;
                        if (relVel.sqrMagnitude > 1e-6f)
                        {
                            var rv = relVel.normalized * 0.4f;
                            _viz.Line("relVel",
                                _hullTarget.hitPoint,
                                _hullTarget.hitPoint + rv,
                                new Color(1f, 0.5f, 0f, 0.05f), 0.02f); // orange
                        }
                    }
                }

                // Spherecast origin + radius
                bool originInsideAny = Physics.CheckSphere(Kerbal.footPivot.position + (Kerbal.transform.up * GroundSpherecastUpOffset), GroundSpherecastRadius * 0.98f, HullTargeting.HullMask, QueryTriggerInteraction.Ignore);
                _viz.Sphere("sphereOrigin", _hullTarget.debugSphereOrigin, _hullTarget.debugSphereRadius, originInsideAny ? new Color(1f, 0f, 0f, 0.15f) : new Color(0f, 1f, 1f, 0.15f), 0.05f);

                _viz.EndFrame();
            }
            */

            Vector3 fwdWorld;
            if (_localHullForward.sqrMagnitude > VECTOR_ZERO_THRESHOLD)
                fwdWorld = _hullTransform.TransformDirection(_localHullForward); // forward follows hull rotation
            else
                fwdWorld = rb.rotation * Vector3.forward; // fallback to current forward

            Vector3 fwd = Vector3.ProjectOnPlane(fwdWorld, fUp);
            if (fwd.sqrMagnitude < VECTOR_ZERO_THRESHOLD) return;
            fwd.Normalize();
            /*
            // Preserve current heading, only tilt to match the surface normal
            Vector3 fwd = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, fUp);
            fwd.Normalize();
            */

            Quaternion targetRot = Quaternion.LookRotation(fwd, fUp);
            Quaternion newRot = Quaternion.RotateTowards(
                rb.rotation,
                targetRot,
                ROTATION_RATE_MULTIPLIER * Time.fixedDeltaTime
            );

            Vector3 pivot = rb.worldCenterOfMass;
            Quaternion delta = newRot * Quaternion.Inverse(rb.rotation);

            rb.MoveRotation(newRot);
            rb.MovePosition(pivot + (delta * (rb.position - pivot)));

            _localHullForward = _hullTransform.InverseTransformDirection(newRot * Vector3.forward); // update local hull forward after turning
        }

        private Vector3 _surfaceVelSMA;
        private int _surfaceVelCount;
        protected virtual void UpdateMovementOnVessel() // Stock-alike custom implementation replacing updateMovementOnVessel() which uses the scene-fixed coordinate frame
        {
            if (base.vessel.packed || !_hullTarget.IsValid()) return;

            float num = (float)FSM.TimeAtCurrentState;
            num = ((num >= 0.3f) ? 1f : ((!(num > 0f)) ? 0f : (num * 3.3333333f)));
            currentSpd = Mathf.Lerp(lastTgtSpeed, tgtSpeed, num);

            Vector3 desiredDir = (tgtRpos != Vector3.zero) ? tgtRpos : Vector3.zero;
            desiredDir = Vector3.ProjectOnPlane(desiredDir, fUp);
            if (desiredDir.sqrMagnitude > VECTOR_ZERO_THRESHOLD) desiredDir.Normalize();
            Vector3 desiredTangentVel = desiredDir * currentSpd; // desired tangential relative velocity (walking); when idle this is zero.

            var rb = Part.rb;
            if (rb == null) return;

            Vector3 surfaceVelRaw = HullTargeting.GetSurfacePointVelocity(_hullTarget);
            // simple EMA works well and is cheap
            float dt = Time.fixedDeltaTime;
            float tau = SURFACE_VELOCITY_SMOOTHING_TAU;
            float alpha = 1f - Mathf.Exp(-dt / tau);
            _surfaceVelSMA = (_surfaceVelCount++ == 0) ? surfaceVelRaw : Vector3.Lerp(_surfaceVelSMA, surfaceVelRaw, alpha);

            Vector3 surfaceVel = _surfaceVelSMA;
            Vector3 v = rb.velocity;
            Vector3 vRel = v - surfaceVel;

            float vn = Vector3.Dot(vRel, fUp);
            Vector3 vRelN = fUp * vn;
            Vector3 vRelT = vRel - vRelN;

            float vnTarget = (_hullTarget.hitDistance > FootHullPad) ? (FootHullPad - _hullTarget.hitDistance) / dt : 0f;
            float dv = Mathf.Clamp(vnTarget - vn, -Settings.magbootsClampForce * dt, Settings.magbootsClampForce * dt);

            float vnNew = vn + dv;
            Vector3 vRelNNew = fUp * vnNew;


            // control tangential relative velocity toward desiredTangentVel. desiredTangentVel is already in world space tangent to the surface
            Vector3 slip = vRelT - desiredTangentVel;
            float slipMag = slip.magnitude;

            if (slipMag > VECTOR_ZERO_THRESHOLD_LOOSE)
            {
                float reduce = Mathf.Min(slipMag, Settings.magbootsStaticFrictionForce * dt);
                Vector3 slipNew = slip - slip.normalized * reduce;
                Vector3 vRelTNew = desiredTangentVel + slipNew;

                vRelT = vRelTNew;
            }


            /*
            // after computing vRelT and desiredTangentVel...
            bool idle = tgtRpos.sqrMagnitude < VECTOR_ZERO_THRESHOLD || desiredTangentVel.sqrMagnitude < VECTOR_ZERO_THRESHOLD;
            if (idle)
            {
                // Hard static friction: kill tangential relative velocity completely
                vRelT = Vector3.zero;
            }
            else
            {
                // Walking: converge toward desiredTangentVel with traction limit
                Vector3 slip = vRelT - desiredTangentVel;
                float slipMag = slip.magnitude;
                if (slipMag > VECTOR_ZERO_THRESHOLD)
                {
                    float maxDv = (Settings.magbootsStaticFrictionForce / Mathf.Max(rb.mass, 1e-6f)) * dt;
                    if (slipMag <= maxDv) vRelT = desiredTangentVel;
                    else vRelT -= slip * (maxDv / slipMag);
                }
                else vRelT = desiredTangentVel;
            }
            */
            /*
            Vector3 slip = vRelT - desiredTangentVel; // what friction must remove
            float slipMag = slip.magnitude;



            if (slipMag > VECTOR_ZERO_THRESHOLD)
            {
                float maxDv = (Settings.magbootsStaticFrictionForce / Mathf.Max(rb.mass, VECTOR_ZERO_THRESHOLD)) * dt;

                if (slipMag <= maxDv)
                {
                    // Static friction can fully cancel slip this frame:
                    vRelT = desiredTangentVel;
                }
                else
                {
                    // Not enough traction this frame: reduce slip as much as possible
                    vRelT = vRelT - slip.normalized * maxDv;
                }
            }
            else
            {
                // Close enough; snap to target to prevent jitter creep
                vRelT = desiredTangentVel;
            }
            */

            // set new world velocity
            Vector3 vRelNew = vRelT + vRelNNew;
            rb.velocity = surfaceVel + vRelNew;
        }

        private void UpdateHullInputTargets()
        {
            if (!_hullTarget.IsValid() || !VesselUnderControl) return;

            GetCameraTangentBasis(fUp, out var camFwdT, out var camRightT);
            if (camFwdT == Vector3.zero || camRightT == Vector3.zero) return;

            Vector3 basisFwd = Vector3.ProjectOnPlane(Kerbal.transform.forward, fUp);
            if (basisFwd.sqrMagnitude < VECTOR_ZERO_THRESHOLD)
                basisFwd = Vector3.ProjectOnPlane(Kerbal.transform.right, fUp);
            basisFwd.Normalize();

            Vector3 basisRight = Vector3.ProjectOnPlane(Kerbal.transform.right, fUp);
            if (basisRight.sqrMagnitude < VECTOR_ZERO_THRESHOLD)
                basisRight = Vector3.Cross(fUp, basisFwd);
            basisRight.Normalize();


            float v = 0f, h = 0f;
            if (GameSettings.EVA_forward.GetKey()) v += 1f;
            if (GameSettings.EVA_back.GetKey()) v -= 1f;
            if (GameSettings.EVA_right.GetKey()) h += 1f;
            if (GameSettings.EVA_left.GetKey()) h -= 1f;

            Vector3 move = basisFwd * v + basisRight * h;
            if (move.sqrMagnitude > 1f) move.Normalize();
            tgtRpos = move;

            if (Mathf.Abs(v) > 0f || Mathf.Abs(h) > 0f)
            {
                Vector3 heading = basisFwd + basisRight * h;  // ignores v sign
                if (heading.sqrMagnitude < VECTOR_ZERO_THRESHOLD)
                    heading = basisFwd;

                tgtFwd = heading.normalized;
            }
        }
        private void GetCameraTangentBasis(Vector3 surfaceUp, out Vector3 tFwd, out Vector3 tRight)
        {
            // Init outputs
            tFwd = Vector3.zero;
            tRight = Vector3.zero;

            var cam = (FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null)
                ? FlightCamera.fetch.mainCamera.transform
                : (Camera.main != null ? Camera.main.transform : null);

            if (cam == null) return;

            // Use camera forward projected onto the plane
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, surfaceUp);
            if (fwd.sqrMagnitude < VECTOR_ZERO_THRESHOLD)
                fwd = Vector3.ProjectOnPlane(cam.up, surfaceUp); // fallback, but only if needed

            if (fwd.sqrMagnitude < VECTOR_ZERO_THRESHOLD) return;
            fwd.Normalize();

            Vector3 right = Vector3.Cross(surfaceUp, fwd);
            if (right.sqrMagnitude < VECTOR_ZERO_THRESHOLD) return;
            right.Normalize();

            tFwd = fwd;
            tRight = right;
        }
    }
}