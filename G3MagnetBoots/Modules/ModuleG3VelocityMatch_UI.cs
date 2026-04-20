using KSP.UI;
using UnityEngine;
using UnityEngine.Events;

namespace G3MagnetBoots
{
    public partial class ModuleG3VelocityMatch : PartModule, IModuleInfo
    {
        // IModuleInfo implementation
        public string GetModuleTitle() { return "EVA Velocity Match Module"; }
        public override string GetInfo() { return "Allows EVA Kerbals to match velocity with a target vessel using their RCS jetpack."; }
        public Callback<Rect> GetDrawModulePanelCallback() { return null; }
        public string GetPrimaryField() { return "Velocity Match"; }

        internal enum VelMatchState { Matching, Matched, Aborted, AbortedNoFuel, Interrupted, FuelLow }

        private ScreenMessage _velMatchMsg;

        private void UpdateVelocityMatchUI()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || _kerbal == null) return;
            if (DifficultySettings == null || !DifficultySettings.velocityMatchEnabled) return;
            if (!_kerbal.HasJetpack) return;

            // Detect vessel switch: resync _lastBrakes so the edge detector doesn't fire spuriously.
            bool activeNow = VesselUnderControl;
            if (activeNow && !_wasActive)
                _lastBrakes = IsBrakesOn;
            _wasActive = activeNow;

            // Brakes keybind toggles the AG
            if (GameSettings.BRAKES.GetKeyDown() && VesselUnderControl)
                ToggleAG(KSPActionGroup.Brakes);

            bool brakesNow = IsBrakesOn;

            if (brakesNow != _lastBrakes)
            {
                if (brakesNow)
                {
                    // Brakes turned on — record intent. FixedUpdate will engage when in st_idle_fl.
                    // Clear stow intent so TryDeployJetpack isn't blocked when we do engage.
                    _playerStowedJetpack = false;
                    _fuelLowWarned = false;
                    _wasShowingMatchingMsg = false;
                    _targetVessel = GetTargetVessel();
                }
                else
                {
                    // Brakes turned off — player explicitly disabling.
                    // Show Aborted only if 'Matching Velocity...' was shown (i.e. we actually started).
                    if (_wasShowingMatchingMsg)
                        PostVelMatchMsg(VelMatchState.Aborted);

                    _matchActive = false;
                    _velocityMatched = false;
                    _offHullDelay = 0f;
                    _fuelLowWarned = false;
                    _wasShowingMatchingMsg = false;
                    _emergencyMatchEngaged = false;
                    _targetVessel = null;
                }

                _lastBrakes = brakesNow;
            }

            SyncAGBrakesButton();
        }

        private bool _wasShowingMatchingMsg; // true once 'Matching Velocity...' has been shown since last enable

        private void PostVelMatchMsg(VelMatchState state)
        {
            string msg;
            switch (state)
            {
                case VelMatchState.Matching:      msg = "Matching Velocity..."; _wasShowingMatchingMsg = true; break;
                case VelMatchState.Matched:       msg = "Velocity Matched."; break;
                case VelMatchState.Aborted:       msg = "Velocity Match Aborted."; break;
                case VelMatchState.AbortedNoFuel: msg = "Velocity Match Aborted - No Fuel!"; break;
                case VelMatchState.Interrupted:   msg = "Velocity Match Interrupted."; break;
                case VelMatchState.FuelLow:       msg = "Warning: Low RCS Fuel."; break;
                default: return;
            }

            // Prefix kerbal name when not the active vessel, matching PostMagMsg pattern.
            bool isActive = vessel != null && vessel == FlightGlobals.ActiveVessel;
            string prefix = isActive ? "" : $"{Crew?.displayName}: ";

            _velMatchMsg = ScreenMessages.PostScreenMessage(prefix + msg,
                state == VelMatchState.Matched ? VELOCITY_MATCHED_MSG_DURATION : VELOCITY_MATCH_MSG_DURATION,
                ScreenMessageStyle.UPPER_CENTER, _velMatchMsg);
        }

        // Keep the stock Brakes AG button in sync visually
        private UIButtonToggle agBrakesButton;
        private bool _syncingAGButtons;
        private UnityAction _agBrakesOnAction;
        private UnityAction _agBrakesOffAction;

        protected void HookAGBrakesButton()
        {
            _lastBrakes = IsAGOn(KSPActionGroup.Brakes);

            var buttonObj = GameObject.Find("ButtonActionGroupBrakes");
            agBrakesButton = buttonObj?.GetComponent<UIButtonToggle>();
            if (agBrakesButton == null)
            {
                StartCoroutine(RetryHookAGBrakesButton());
                return;
            }

            _agBrakesOnAction = SyncAGBrakesButton;
            _agBrakesOffAction = SyncAGBrakesButton;
            agBrakesButton.onToggleOn.AddListener(_agBrakesOnAction);
            agBrakesButton.onToggleOff.AddListener(_agBrakesOffAction);
        }

        private System.Collections.IEnumerator RetryHookAGBrakesButton()
        {
            while (agBrakesButton == null && this != null && enabled)
            {
                yield return new WaitForSeconds(0.5f);
                var buttonObj = GameObject.Find("ButtonActionGroupBrakes");
                agBrakesButton = buttonObj?.GetComponent<UIButtonToggle>();
            }
            if (agBrakesButton == null) yield break;

            _agBrakesOnAction = SyncAGBrakesButton;
            _agBrakesOffAction = SyncAGBrakesButton;
            agBrakesButton.onToggleOn.AddListener(_agBrakesOnAction);
            agBrakesButton.onToggleOff.AddListener(_agBrakesOffAction);
        }

        private void SyncAGBrakesButton()
        {
            if (agBrakesButton == null || _syncingAGButtons) return;

            _syncingAGButtons = true;
            try { agBrakesButton.SetState(IsAGOn(KSPActionGroup.Brakes)); }
            finally { _syncingAGButtons = false; }
        }

        private void OnDisable()
        {
            _matchActive = false;
            _offHullDelay = 0f;
            _fuelLowWarned = false;
            _wasShowingMatchingMsg = false;
            _emergencyMatchEngaged = false;
            _syncingAGButtons = false;
            try
            {
                if (agBrakesButton != null)
                {
                    if (_agBrakesOnAction != null) agBrakesButton.onToggleOn.RemoveListener(_agBrakesOnAction);
                    if (_agBrakesOffAction != null) agBrakesButton.onToggleOff.RemoveListener(_agBrakesOffAction);
                }
            }
            catch { }
        }
    }
}
