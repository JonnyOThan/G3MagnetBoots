using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using UnityEngine;
using static KerbalEVA;

namespace G3MagnetBoots
{
    public partial class ModuleG3VelocityMatch
    {
        // Velocity Match Constants
        internal const float VELOCITY_DEAD_ZONE = 0.05f;               // m/s - fallback default
        internal const float VELOCITY_MATCH_MSG_DURATION = 2f;
        internal const float VELOCITY_MATCHED_MSG_DURATION = 0.8f;     // short flash — already matched

        private static G3MagnetBootsSettings Settings => G3MagnetBootsSettings.Current;
        private static G3MagnetBootsDifficultySettings DifficultySettings => G3MagnetBootsDifficultySettings.Current;
        private float VelocityDeadZone => Settings?.velocityMatchDeadZone ?? VELOCITY_DEAD_ZONE;

        private KerbalEVA _kerbal;
        private bool _lastBrakes;
        private bool _matchActive;
        private bool _playerHasManualInput; // set by Harmony patch before our contribution is added
        private float _offHullDelay;        // seconds remaining before matching resumes after leaving hull
        private bool _matchEngageDelayActive;   // true while a post-hull-exit coroutine delay is in effect
        private int _matchEngageDelayGen;       // generation counter to discard stale coroutine completions
        internal bool _playerStowedJetpack; // player intentionally stowed pack; suppress auto-redeploy
        private bool _velocityMatched;      // true while within dead zone; prevents repeated Matched messages
        private Vessel _targetVessel;       // cached target; updated while active, kept while packed
        private bool _wasActive;            // true if this was the active vessel last frame

        internal const float OFF_HULL_MATCH_DELAY = 2.0f; // mirrors JETPACK_DEPLOY_DELAY_JUMP
        internal const float FUEL_LOW_THRESHOLD = 0.1f;  // 10% — warn before abort
        private bool _fuelLowWarned;
        private bool _lastWasRagdoll;         // ragdoll state last frame, for edge detection
        private bool _emergencyMatchEngaged;  // true if we auto-enabled match due to ragdoll emergency

        public Part _part { get { return _kerbal.part; } }
        public ProtoCrewMember Crew { get { return _part.protoModuleCrew?.FirstOrDefault(); } }

        // Accessors for protected KerbalEVA fields
        private float fuelFlowRate
        {
            get => _kerbal != null ? KerbalEVAAccess.FuelFlowRate(_kerbal) : 0f;
            set { if (_kerbal != null) KerbalEVAAccess.FuelFlowRate(_kerbal) = value; }
        }

        bool IsAGOn(KSPActionGroup g) => VesselUtils.IsAGOn(vessel, g);
        public void SetAG(KSPActionGroup g, bool active) => VesselUtils.SetAG(vessel, g, active);
        void ToggleAG(KSPActionGroup g) => VesselUtils.ToggleAG(vessel, g);
        public bool IsBrakesOn => IsAGOn(KSPActionGroup.Brakes);

        public bool IsMatchActive => _matchActive;

        public bool VesselUnderControl => VesselUtils.VesselUnderControl(_kerbal);

        private Vessel GetTargetVessel()
        {
            if (FlightGlobals.fetch == null) return null;
            var target = FlightGlobals.fetch.VesselTarget;
            if (target == null) return null;

            Vessel targetVessel = target as Vessel;
            if (targetVessel == null)
            {
                try { targetVessel = target.GetVessel(); }
                catch { return null; }
            }

            if (targetVessel == null) return null;
            if (targetVessel == vessel) return null;
            if (targetVessel.packed || targetVessel.mainBody == null) return null;
            return targetVessel;
        }

        private bool HasManualInput() => _playerHasManualInput;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!HighLogic.LoadedSceneIsFlight) return;

            _matchActive = false;
            _offHullDelay = OFF_HULL_MATCH_DELAY;
            _playerStowedJetpack = false;
            _velocityMatched = false;
            _fuelLowWarned = false;
            _targetVessel = null;
            _wasActive = VesselUnderControl;
            HookAGBrakesButton();
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            if (!HighLogic.LoadedSceneIsFlight) return;

            UpdateVelocityMatchUI();
        }

        private void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (_kerbal == null) return;
            if (part == null) return;
            if (vessel == null || vessel.mainBody == null) return;
            if (!_kerbal.HasJetpack) return;

            // Refresh cached target only while this is the active vessel.
            if (VesselUnderControl)
                _targetVessel = GetTargetVessel();

            // --- Ragdoll edge detection: reset matched flag so message fires again after recovery ---
            bool isRagdollNow = _kerbal.isRagdoll;
            if (!isRagdollNow && _lastWasRagdoll)
                _velocityMatched = false;
            _lastWasRagdoll = isRagdollNow;

            // AG is the sticky player-intent flag; never force it off automatically.
            // _matchActive only reflects whether we are currently firing thrusters.
            // Keep AG on whenever _matchActive is true (guards against KSP clearing it on switch).
            if (_matchActive && !IsBrakesOn)
                SetAG(KSPActionGroup.Brakes, true);

            // Not in free-float idle and not ragdoll: keep intent in AG, do nothing else.
            // Ragdoll is allowed through so emergency match works regardless of active vessel.
            if (!IsInFreeFloatIdle() && !_kerbal.isRagdoll)
            {
                if (_matchActive)
                {
                    _matchActive = false;
                    _velocityMatched = false;
                }
                return;
            }

            // --- From here we are in st_idle_fl ---

            // Honour any extra engage delay set by hull-exit paths (e.g. jump doubles jetpack delay).
            if (_matchEngageDelayActive)
                return;

            // Packed (background) vessel: HandleMovementInput never runs.
            if (vessel.packed)
            {
                if (IsBrakesOn && _targetVessel != null)
                    ApplyDirectThrust(_targetVessel);
                return;
            }

            // Player turned brakes off while we were active — that is the explicit player abort.
            if (_matchActive && !IsBrakesOn)
            {
                // This branch is unreachable because of the keep-AG-on sync above,
                // but guard it anyway for safety.
                StopMatch();
                return;
            }

            // Brakes off and not active — nothing to do.
            if (!IsBrakesOn)
            {
                _matchActive = false;
                return;
            }

            // Brakes are on — we want to be matching. Check preconditions silently.
            if (_kerbal.Fuel <= 0.0)
            {
                // Out of fuel — hard abort, clear intent.
                HardAbortMatch();
                return;
            }

            if (_targetVessel == null)
            {
                // No target — suspend silently (keep AG on, don't message).
                _matchActive = false;
                return;
            }

            // Jetpack must be deployed.
            if (!_kerbal.JetpackDeployed)
            {
                TryDeployJetpack();
                return;
            }

            // Warn once when fuel drops below threshold while actively matching.
            if (_matchActive && !_fuelLowWarned && _kerbal.Fuel <= FUEL_LOW_THRESHOLD)
            {
                _fuelLowWarned = true;
                PostVelMatchMsg(VelMatchState.FuelLow);
            }

            // Engage if not yet active (e.g. just entered st_idle_fl with brakes already on).
            if (!_matchActive)
            {
                _matchActive = true;
                PostVelMatchMsg(VelMatchState.Matching);
            }

            // Non-active vessel or ragdoll: use direct force path.
            if (!VesselUnderControl || _kerbal.isRagdoll)
            {
                ApplyDirectThrust(_targetVessel);
                return;
            }

            // Dead-zone check — idle if matched, keep _matchActive true so we resume on drift.
            if (part.rb != null)
            {
                float relSpeed = ((Vector3)(vessel.GetObtVelocity() - _targetVessel.GetObtVelocity())).magnitude;
                if (relSpeed < VelocityDeadZone)
                    OnVelocityMatched();
                else
                    _velocityMatched = false; // drifted out — allow Matched message again
            }
        }

        // Direct force + FX + fuel path used for both ragdoll state and packed (background) vessel,
        // where UpdatePackLinear / HandleMovementInput pipelines are unavailable.
        // Packed vessels have no rigidbody; velocity is changed via vessel.ChangeWorldVelocity.
        private void ApplyDirectThrust(Vessel target)
        {
            if (target == null) return;

            Vector3 relVel = (Vector3)(vessel.GetObtVelocity() - target.GetObtVelocity());
            float relSpeed = relVel.magnitude;

            if (relSpeed < VelocityDeadZone)
            {
                OnVelocityMatched();
                return;
            }

            float thrustPercentageFactor = _kerbal.thrustPercentage * 0.01f;
            if (thrustPercentageFactor <= 0f || _kerbal.linPower <= 0f) return;
            if (_kerbal.Fuel <= 0.0) return;

            Vector3 thrustDir = -relVel.normalized;

            if (vessel.packed)
            {
                // No rigidbody when packed — apply delta-V directly to the orbital state.
                // Compute the maximum delta-V this frame at full thrust, capped to exactly
                // cancel relative velocity so we never overshoot.
                float maxDeltaV = thrustPercentageFactor * _kerbal.linPower * Time.fixedDeltaTime;
                float cancelDeltaV = Mathf.Min(relSpeed, maxDeltaV);
                vessel.ChangeWorldVelocity((Vector3d)(thrustDir * cancelDeltaV));

                // Drain fuel proportional to fractional thrust used
                float thrustFraction = cancelDeltaV / Mathf.Max(maxDeltaV, 1e-6f);
                KerbalEVAAccess.FuelFlowRate(_kerbal) += thrustPercentageFactor * thrustFraction * Time.fixedDeltaTime;
                KerbalEVAAccess.UpdatePackFuel(_kerbal);
                return;
            }

            // Unpacked ragdoll: use AddForce + full FX pipeline (same as before)
            var rb = part.rb;
            if (rb == null) return;

            float exactScale = (relSpeed * Mathf.Max(rb.mass, 0.001f)) / (_kerbal.linPower * Time.fixedDeltaTime);
            float thrustScale = Mathf.Min(thrustPercentageFactor, exactScale);

            part.AddForce(thrustDir * thrustScale * _kerbal.linPower);

            KerbalEVAAccess.PackLinear(_kerbal) = thrustDir * thrustScale;

            KerbalEVAAccess.FuelFlowRate(_kerbal) += thrustScale * Time.fixedDeltaTime;
            KerbalEVAAccess.UpdatePackFuel(_kerbal);

            // Drive thruster FX groups directly (updateJetpackEffects is blocked for ragdoll)
            float latch = KerbalEVAAccess._linFXLatch(_kerbal);
            Vector3 local = Quaternion.Inverse(_kerbal.transform.rotation) * (thrustDir * thrustScale);

            int count = part.fxGroups.Count;
            while (count-- > 0) part.fxGroups[count].Unlatch();

            FXGroup xp = part.findFxGroup("X+");
            FXGroup xn = part.findFxGroup("X-");
            FXGroup yp = part.findFxGroup("Y+");
            FXGroup yn = part.findFxGroup("Y-");
            FXGroup zp = part.findFxGroup("Z+");
            FXGroup zn = part.findFxGroup("Z-");

            xp?.SetLatch(local.x >  latch); xp?.SetPowerLatch( local.x);
            xn?.SetLatch(local.x < -latch); xn?.SetPowerLatch(-local.x);
            yp?.SetLatch(local.y >  latch); yp?.SetPowerLatch( local.y);
            yn?.SetLatch(local.y < -latch); yn?.SetPowerLatch(-local.y);
            zp?.SetLatch(local.z >  latch); zp?.SetPowerLatch( local.z);
            zn?.SetLatch(local.z < -latch); zn?.SetPowerLatch(-local.z);
        }

        internal void HookIntoEva(KerbalEVA eva)
        {
            _kerbal = eva;

            // Hook stumble so we can detect ragdoll entry for emergency velocity match.
            if (_kerbal?.On_stumble != null)
                _kerbal.On_stumble.OnEvent += OnKerbalStumble;
        }

        private void OnKerbalStumble()
        {
            // Kerbal just went ragdoll. If emergency match is enabled and brakes are off, engage intent.
            if (DifficultySettings?.emergencyVelocityMatchOnRagdoll != true) return;
            if (DifficultySettings?.velocityMatchEnabled != true) return;
            if (!_kerbal.HasJetpack) return;
            if (IsBrakesOn) return; // already engaged
            if (_targetVessel == null && GetTargetVessel() == null) return; // no target
            if (_kerbal?.Fuel <= 0.0) return;

            // Cache target if not already set (non-active vessel keeps last cached value).
            if (_targetVessel == null)
                _targetVessel = GetTargetVessel();

            SetAG(KSPActionGroup.Brakes, true);
            _emergencyMatchEngaged = true;
            _playerStowedJetpack = false;
            _fuelLowWarned = false;

            // Deploy jetpack immediately if not deployed (no delay for emergency).
            if (!_kerbal.JetpackDeployed)
                KerbalEVAAccess.ToggleJetpack(_kerbal, true);
        }

        // Called by ModuleG3MagnetBoots on hull-exit to impose a delay before velocity match
        // may engage, on top of any jetpack deploy delay.
        internal void SetEngageDelay(float seconds)
        {
            if (_kerbal == null) return;
            int gen = ++_matchEngageDelayGen;
            // Set the flag synchronously here so it is in effect immediately on the current
            // FixedUpdate tick. StartCoroutine does NOT run the initial coroutine block until
            // the next Update phase, so relying on the coroutine to set it is too late.
            _matchEngageDelayActive = true;
            _kerbal.StartCoroutine(MatchEngageDelayCoroutine(seconds, gen));
        }

        private IEnumerator MatchEngageDelayCoroutine(float seconds, int generation)
        {
            yield return new WaitForSeconds(seconds);
            if (_matchEngageDelayGen == generation)
                _matchEngageDelayActive = false;
        }

        // True only when in free-float idle — the one state where velocity matching makes sense.
        private bool IsInFreeFloatIdle()
        {
            if (_kerbal == null) return false;
            var s = _kerbal.fsm?.CurrentState;
            return s == _kerbal.st_idle_fl;
        }

        // Returns true for any FSM state where we should NOT be thrusting:
        // on-hull custom states, grounded, ladder, seated, swimming, construction.
        // Ragdoll and free-float states return false.
        private bool IsOnHullOrGrounded()
        {
            if (_kerbal == null) return false;
            var s = _kerbal.fsm?.CurrentState;
            if (s == null) return false;

            // Our custom on-hull states (ModuleG3MagnetBoots adds these to the FSM)
            var magBoots = part?.FindModuleImplementing<ModuleG3MagnetBoots>();
            if (magBoots != null && magBoots.IsOnHull) return true;

            // Stock grounded / surface states
            return s == _kerbal.st_idle_gr
                || s == _kerbal.st_idle_b_gr
                || s == _kerbal.st_walk_acd
                || s == _kerbal.st_walk_fps
                || s == _kerbal.st_run_acd
                || s == _kerbal.st_run_fps
                || s == _kerbal.st_heading_acquire
                || s == _kerbal.st_bound_gr_acd
                || s == _kerbal.st_bound_gr_fps
                || s == _kerbal.st_land
                || s == _kerbal.st_ladder_idle
                || s == _kerbal.st_ladder_acquire
                || s == _kerbal.st_ladder_climb
                || s == _kerbal.st_ladder_descend
                || s == _kerbal.st_ladder_lean
                || s == _kerbal.st_ladder_pushoff
                || s == _kerbal.st_ladder_end_reached
                || s == _kerbal.st_swim_idle
                || s == _kerbal.st_swim_fwd
                || s == _kerbal.st_seated_cmd
                || s == _kerbal.st_grappled
                || s == _kerbal.st_enteringConstruction
                || s == _kerbal.st_exitingConstruction
                || s == _kerbal.st_weld
                || s == _kerbal.st_weldAcquireHeading;
        }

        // Called by Harmony patch when player intentionally stows the jetpack
        internal void OnPlayerStowedJetpack()
        {
            _playerStowedJetpack = true;
        }

        // Called by Harmony patch when player manually deploys the jetpack
        internal void OnPlayerDeployedJetpack()
        {
            _playerStowedJetpack = false;
        }

        private void TryDeployJetpack()
        {
            if (_playerStowedJetpack) return; // respect player's intent
            if (_kerbal.HasJetpack && !_kerbal.JetpackDeployed
                && Settings?.jetpackAutoToggleEnabled == true)
            {
                _kerbal.StartCoroutine(DeployJetpackCoroutine());
            }
        }

        private System.Collections.IEnumerator DeployJetpackCoroutine()
        {
            yield return new UnityEngine.WaitForSeconds(OFF_HULL_MATCH_DELAY);
            if (_kerbal != null && _kerbal.HasJetpack && !_kerbal.JetpackDeployed)
                KerbalEVAAccess.ToggleJetpack(_kerbal, true);
        }

        // Called by Harmony postfix on HandleMovementInput, after player input is written into
        // packTgtRPos but before UpdatePackLinear reads it. Records whether the player is
        // thrusting manually, then returns our contribution to add to packTgtRPos.
        // Only called for the active vessel, so vessel.packed is always false here.
        internal Vector3 GetPackTgtRPosContribution(Vector3 playerPackTgtRPos)
        {
            _playerHasManualInput = playerPackTgtRPos.sqrMagnitude > 0.001f;

            if (!_matchActive) return Vector3.zero;
            if (_kerbal == null || vessel == null || vessel.mainBody == null) return Vector3.zero;
            if (!_kerbal.JetpackDeployed || _kerbal.Fuel <= 0.0) return Vector3.zero;

            Vessel target = _targetVessel;
            if (target == null) return Vector3.zero;

            var rb = part?.rb;
            if (rb == null) return Vector3.zero;

            Vector3d kerbalVel = vessel.GetObtVelocity();
            Vector3d targetVel = target.GetObtVelocity();
            Vector3 relVel = (Vector3)(kerbalVel - targetVel);
            float relSpeed = relVel.magnitude;

            if (relSpeed < VelocityDeadZone)
                return Vector3.zero;

            Vector3 thrustDir = -relVel.normalized;
            float thrustPercentageFactor = _kerbal.thrustPercentage * 0.01f;
            if (thrustPercentageFactor <= 0f || _kerbal.linPower <= 0f) return Vector3.zero;

            // Nerf SmartRCS thrust unless target is moving dangerously fast
            float maxSmartRcs = Settings?.maxSmartRcsThrust ?? 0.5f;
            float thrustCap = (relSpeed > 2.5f) ? 1.0f : maxSmartRcs;

            // Exact packLinear scale to cancel relVel in one frame, capped at pack maximum and SmartRCS cap
            float exactScale = (relSpeed * Mathf.Max(rb.mass, 0.001f)) / (_kerbal.linPower * Time.fixedDeltaTime);
            float thrustScale = Mathf.Min(thrustPercentageFactor, exactScale, thrustCap);

            Vector3 ourContrib = thrustDir * thrustScale;

            // Blend with manual input: remove the component of our contribution that is already
            // covered by the player's input direction, so manual thrust takes priority on any
            // axis it occupies while we keep matching on the remaining axes.
            if (_playerHasManualInput)
            {
                Vector3 playerDir = playerPackTgtRPos.normalized;
                float overlap = Vector3.Dot(ourContrib, playerDir);
                ourContrib -= playerDir * overlap;
                if (ourContrib.sqrMagnitude < 0.0001f)
                    return Vector3.zero;
                thrustScale = ourContrib.magnitude;
            }

            // UpdatePackLinear multiplies packTgtRPos by thrustPercentageFactor to get packLinear,
            // so pre-divide here so the desired thrustScale survives that multiplication intact.
            return ourContrib.normalized * (thrustScale / thrustPercentageFactor);
        }

        // Velocity matched: flash message once, stay idle (keep AG on, keep _matchActive true).
        // Thrust contribution returns zero while within dead zone, so no fuel is wasted.
        // If the Kerbal drifts back out, _velocityMatched is cleared and matching resumes.
        private void OnVelocityMatched()
        {
            if (_velocityMatched) return;
            _velocityMatched = true;
            PostVelMatchMsg(VelMatchState.Matched);
        }

        // Player explicitly turned off velocity match (brakes-off edge while active).
        // Shows no message — the AG going dark is feedback enough.
        private void StopMatch()
        {
            _matchActive = false;
            _velocityMatched = false;
            _fuelLowWarned = false;
        }

        // Hard abort: fuel gone while mid-burn. Clears intent (turns AG off).
        private void HardAbortMatch()
        {
            PostVelMatchMsg(VelMatchState.AbortedNoFuel);
            SetAG(KSPActionGroup.Brakes, false);
            _matchActive = false;
            _velocityMatched = false;
            _fuelLowWarned = false;
            _targetVessel = null;
        }
    }
}
