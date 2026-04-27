using KSP.UI;
using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using static KerbalEVA;

namespace G3MagnetBoots
{
    public partial class ModuleG3MagnetBoots : PartModule, IModuleInfo
    {
        // IModuleInfo implementation, mainly for PartInfo tooltip
        public string GetModuleTitle() { return "EVA Magnetic Boots Module"; }
        public override string GetInfo() { return "Allows EVA Kerbals to walk and stand on spacecraft hulls using magnetic boots."; }
        public Callback<Rect> GetDrawModulePanelCallback() { return null; }
        public string GetPrimaryField() { return "Magnetic Boots"; }

        public void UpdateUI()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || Kerbal == null || Kerbal.part == null || Kerbal.fsm == null) return;
            if (GameSettings.LANDING_GEAR.GetKeyDown())
                if (!IsTechUnlocked())
                    PostTechNotResearchedMsg();
                else
                    ToggleAG(KSPActionGroup.Gear);

            if (!IsTechUnlocked()) return;

            if (_lastGear != IsGearOn)
            {
                SetEnabled(IsGearOn);

                if (!IsGearOn && _hullTarget.IsValid() && (FSM.CurrentState == st_idle_hull || FSM.CurrentState == st_walk_hull))
                {
                    ClearHullTarget();  
                    ApplyLetGoImpulse();
                    Kerbal.StartCoroutine(AutoDeployJetpack_Coroutine(0.5f));
                }

                _lastGear = IsGearOn;
            }

            if (_lastOnHull != IsOnHull)
            {
                PostMagMsg(IsOnHull);
                _lastOnHull = IsOnHull;
            }

            UpdatePlantFlagOnHullButton();
            UpdateHullAnchorAGInterface();
        }

        void PostMagMsg(bool on)
        {
            string msg = on ? "Magnet Boots Engaged" : "Magnet Boots Disengaged";
            string prefix = (vessel != null && vessel == FlightGlobals.ActiveVessel) ? "" : $"{Crew.displayName}: ";
            _magMsg = ScreenMessages.PostScreenMessage(prefix + msg, 2f, ScreenMessageStyle.UPPER_CENTER, _magMsg);
        }

        float _postTechMsgCooldown = 0f;
        void PostTechNotResearchedMsg()
        {
            if (_postTechMsgCooldown > 0f)
            {
                _postTechMsgCooldown -= Time.deltaTime;
                return;
            }
            _postTechMsgCooldown = 5f;
            _magMsg = ScreenMessages.PostScreenMessage($"{unlockTech} not researched!", 3f, ScreenMessageStyle.UPPER_CENTER, _magMsg);
        }

        // Keep the stock Gear AG button in sync with the magboots state visually
        private UIButtonToggle agGearButton;
        private bool _syncingAGButtons;
        private UnityAction _agGearOnAction;
        private UnityAction _agGearOffAction;

        protected void HookAGGearButton()
        {
            _lastGear = IsAGOn(KSPActionGroup.Gear);

            var buttonObj = GameObject.Find("ButtonActionGroupGears");
            agGearButton = buttonObj?.GetComponent<UIButtonToggle>();
            if (agGearButton == null)
            {
                StartCoroutine(RetryHookAGGearButton());
                return;
            }

            _agGearOnAction = SyncAGGearButton;
            _agGearOffAction = SyncAGGearButton;
            agGearButton.onToggleOn.AddListener(_agGearOnAction);
            agGearButton.onToggleOff.AddListener(_agGearOffAction);
        }

        private System.Collections.IEnumerator RetryHookAGGearButton()
        {
            while (agGearButton == null && this != null && enabled)
            {
                yield return new WaitForSeconds(0.5f);
                var buttonObj = GameObject.Find("ButtonActionGroupGears");
                agGearButton = buttonObj?.GetComponent<UIButtonToggle>();
            }
            if (agGearButton == null) yield break;

            _agGearOnAction = SyncAGGearButton;
            _agGearOffAction = SyncAGGearButton;
            agGearButton.onToggleOn.AddListener(_agGearOnAction);
            agGearButton.onToggleOff.AddListener(_agGearOffAction);
        }

        private void SyncAGGearButton()
        {
            if (agGearButton == null || _syncingAGButtons) return;

            _syncingAGButtons = true;
            try { agGearButton.SetState(IsAGOn(KSPActionGroup.Gear)); }
            finally { _syncingAGButtons = false; }
        }

        private void OnDisable()
        { 
            _syncingAGButtons = false;
            try
            {
                if (agGearButton != null)
                {
                    if (_agGearOnAction != null) agGearButton.onToggleOn.RemoveListener(_agGearOnAction);
                    if (_agGearOffAction != null) agGearButton.onToggleOff.RemoveListener(_agGearOffAction);
                }
            }
            catch { }
            UnhookAGBrakesButton();
        }

        private bool _lastBrakes;
        private UIButtonToggle _agBrakesButton;
        private bool _syncingAGBrakesButtons;
        private UnityAction _agBrakesOnAction;
        private UnityAction _agBrakesOffAction;

        public bool IsBrakesOn => IsAGOn(KSPActionGroup.Brakes);

        private void UpdateHullAnchorAGInterface()
        {
            if (!HighLogic.LoadedSceneIsFlight || Kerbal == null || vessel == null)
                return;

            if (GameSettings.BRAKES.GetKeyDown() && VesselUnderControl)
            {
                ToggleAG(KSPActionGroup.Brakes);
            }

            bool brakesNow = IsBrakesOn;

            if (brakesNow != _lastBrakes)
            {
                if (!brakesNow)
                {
                    RemoveHullAnchor();
                    _hullAnchorTimer = 0f;
                }
                else
                {
                    // Do not immediately AddHullAnchor() here.
                    // Just allow TryAddHullAnchor() to engage when stable.
                    _hullAnchorTimer = 0f;
                }

                _lastBrakes = brakesNow;
            }

            SyncAGBrakesButton();
        }


        private void HookAGBrakesButton()
        {
            _lastBrakes = IsAGOn(KSPActionGroup.Brakes);

            _agBrakesButton = GameObject.Find("ButtonActionGroupBrakes")
                ?.GetComponent<UIButtonToggle>();

            if (_agBrakesButton == null)
            {
                StartCoroutine(RetryHookAGBrakesButton());
                return;
            }

            _agBrakesOnAction = SyncAGBrakesButton;
            _agBrakesOffAction = SyncAGBrakesButton;

            _agBrakesButton.onToggleOn.AddListener(_agBrakesOnAction);
            _agBrakesButton.onToggleOff.AddListener(_agBrakesOffAction);

            SyncAGBrakesButton();
        }

        private IEnumerator RetryHookAGBrakesButton()
        {
            while (_agBrakesButton == null && this != null && base.enabled)
            {
                yield return new WaitForSeconds(0.5f);

                _agBrakesButton = GameObject.Find("ButtonActionGroupBrakes")
                    ?.GetComponent<UIButtonToggle>();
            }

            if (_agBrakesButton == null)
                yield break;

            _agBrakesOnAction = SyncAGBrakesButton;
            _agBrakesOffAction = SyncAGBrakesButton;

            _agBrakesButton.onToggleOn.AddListener(_agBrakesOnAction);
            _agBrakesButton.onToggleOff.AddListener(_agBrakesOffAction);

            SyncAGBrakesButton();
        }

        private void UnhookAGBrakesButton()
        {
            try
            {
                if (_agBrakesButton != null)
                {
                    if (_agBrakesOnAction != null)
                        _agBrakesButton.onToggleOn.RemoveListener(_agBrakesOnAction);

                    if (_agBrakesOffAction != null)
                        _agBrakesButton.onToggleOff.RemoveListener(_agBrakesOffAction);
                }
            }
            catch
            {
            }

            _agBrakesButton = null;
            _agBrakesOnAction = null;
            _agBrakesOffAction = null;
            _syncingAGBrakesButtons = false;
        }

        private void SyncAGBrakesButton()
        {
            if (_agBrakesButton == null || _syncingAGBrakesButtons)
                return;

            _syncingAGBrakesButtons = true;
            try
            {
                _agBrakesButton.SetState(IsAGOn(KSPActionGroup.Brakes));
            }
            finally
            {
                _syncingAGBrakesButtons = false;
            }
        }

    }
}
