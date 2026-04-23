using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace G3MagnetBoots
{
    internal static class KerbalEVAAccess
    {
        // private methods
        internal static readonly Action<KerbalEVA> _updatePackLinear = AccessTools.MethodDelegate<Action<KerbalEVA>>(AccessTools.Method(typeof(KerbalEVA), "UpdatePackLinear", Type.EmptyTypes));
        internal static void UpdatePackLinear(KerbalEVA eva){ _updatePackLinear(eva); }

        internal static readonly Action<KerbalEVA, string, float> _postInteractionScreenMessage =
            AccessTools.MethodDelegate<Action<KerbalEVA, string, float>>(AccessTools.Method(typeof(KerbalEVA), "PostInteractionScreenMessage", new Type[] { typeof(string), typeof(float) }));
        internal static void PostInteractionScreenMessage(KerbalEVA eva, string msg, float duration) { _postInteractionScreenMessage(eva, msg, duration); }

        //CalculateGroundLevelAngle
        internal static readonly Action<KerbalEVA> _calculateGroundLevelAngle =
            AccessTools.MethodDelegate<Action<KerbalEVA>>(AccessTools.Method(typeof(KerbalEVA), "CalculateGroundLevelAngle", Type.EmptyTypes));
        internal static void CalculateGroundLevelAngle(KerbalEVA eva) => _calculateGroundLevelAngle(eva);

        //private void AddRBAnchor()
        internal static readonly Action<KerbalEVA> _addRBAnchor =
            AccessTools.MethodDelegate<Action<KerbalEVA>>(AccessTools.Method(typeof(KerbalEVA), "AddRBAnchor", Type.EmptyTypes));
        internal static void AddRBAnchor(KerbalEVA eva) => _addRBAnchor(eva);

        //private void RemoveRBAnchor()
        internal static readonly Action<KerbalEVA> _removeRBAnchor =
            AccessTools.MethodDelegate<Action<KerbalEVA>>(AccessTools.Method(typeof(KerbalEVA), "RemoveRBAnchor", Type.EmptyTypes));
        internal static void RemoveRBAnchor(KerbalEVA eva) => _removeRBAnchor(eva);

        // private fields
        internal static readonly AccessTools.FieldRef<KerbalEVA, float> _currentSpd =
            AccessTools.FieldRefAccess<KerbalEVA, float>("currentSpd");

        internal static readonly AccessTools.FieldRef<KerbalEVA, float> _lastTgtSpeed =
            AccessTools.FieldRefAccess<KerbalEVA, float>("lastTgtSpeed");

        internal static readonly AccessTools.FieldRef<KerbalEVA, float> _tgtSpeed =
            AccessTools.FieldRefAccess<KerbalEVA, float>("tgtSpeed");

        internal static readonly AccessTools.FieldRef<KerbalEVA, Vector3> _cmdDir =
            AccessTools.FieldRefAccess<KerbalEVA, Vector3>("cmdDir");

        internal static readonly AccessTools.FieldRef<KerbalEVA, Vector3> _cmdRot =
            AccessTools.FieldRefAccess<KerbalEVA, Vector3>("cmdRot");

        internal static readonly AccessTools.FieldRef<KerbalEVA, Vector3> _fUp =
            AccessTools.FieldRefAccess<KerbalEVA, Vector3>("fUp");

        internal static readonly AccessTools.FieldRef<KerbalEVA, Vector3> _tgtRpos =
            AccessTools.FieldRefAccess<KerbalEVA, Vector3>("tgtRpos");

        internal static readonly AccessTools.FieldRef<KerbalEVA, Vector3> _tgtFwd =
            AccessTools.FieldRefAccess<KerbalEVA, Vector3>("tgtFwd");

        internal static readonly AccessTools.FieldRef<KerbalEVA, float> _deltaHdg =
            AccessTools.FieldRefAccess<KerbalEVA, float>("deltaHdg");

        internal static readonly AccessTools.FieldRef<KerbalEVA, float> _turnRate =
            AccessTools.FieldRefAccess<KerbalEVA, float>("turnRate");

        internal static readonly AccessTools.FieldRef<KerbalEVA, Animation> _animation =
            AccessTools.FieldRefAccess<KerbalEVA, Animation>("_animation");

        internal static readonly AccessTools.FieldRef<KerbalEVA, Vector3> _packLinear =
            AccessTools.FieldRefAccess<KerbalEVA, Vector3>("packLinear");

        internal static readonly AccessTools.FieldRef<KerbalEVA, Vector3> _packTgtRPos =
            AccessTools.FieldRefAccess<KerbalEVA, Vector3>("packTgtRPos");

        internal static readonly AccessTools.FieldRef<KerbalEVA, float> _fuelFlowRate =
            AccessTools.FieldRefAccess<KerbalEVA, float>("fuelFlowRate");

        internal static readonly AccessTools.FieldRef<KerbalEVA, List<Collider>> _currentLadderTriggers =
            AccessTools.FieldRefAccess<KerbalEVA, List<Collider>>("currentLadderTriggers");

        //protected ModuleEvaChute evaChute;
        internal static readonly AccessTools.FieldRef<KerbalEVA, ModuleEvaChute> _evaChute =
            AccessTools.FieldRefAccess<KerbalEVA, ModuleEvaChute>("evaChute");

        ///wasVisorEnabledBeforeWelding
        internal static readonly AccessTools.FieldRef<KerbalEVA, bool> _wasVisorEnabledBeforeWelding =
            AccessTools.FieldRefAccess<KerbalEVA, bool>("wasVisorEnabledBeforeWelding");

        //private VisorStates visorState;
        internal static readonly AccessTools.FieldRef<KerbalEVA, KerbalEVA.VisorStates> _visorState =
            AccessTools.FieldRefAccess<KerbalEVA, KerbalEVA.VisorStates>("visorState");

        //private bool isAnchored;
        internal static readonly AccessTools.FieldRef<KerbalEVA, bool> _isAnchored =
            AccessTools.FieldRefAccess<KerbalEVA, bool>("isAnchored");

        //private float kerbalAnchorTimeThreshold = 0.5f;
        internal static readonly AccessTools.FieldRef<KerbalEVA, float> _kerbalAnchorTimeThreshold =
            AccessTools.FieldRefAccess<KerbalEVA, float>("kerbalAnchorTimeThreshold");

        //private float kerbalAnchorTimeCounter;
        internal static readonly AccessTools.FieldRef<KerbalEVA, float> _kerbalAnchorTimeCounter =
            AccessTools.FieldRefAccess<KerbalEVA, float>("kerbalAnchorTimeCounter");

        //private FixedJoint anchorJoint;
        internal static readonly AccessTools.FieldRef<KerbalEVA, FixedJoint> _anchorJoint =
            AccessTools.FieldRefAccess<KerbalEVA, FixedJoint>("anchorJoint");

        //isHelmetEnabled
        internal static readonly AccessTools.FieldRef<KerbalEVA, bool> _isHelmetEnabled =
            AccessTools.FieldRefAccess<KerbalEVA, bool>("isHelmetEnabled");

        //internal Part constructionTarget;
        internal static readonly AccessTools.FieldRef<KerbalEVA, Part> _constructionTarget =
            AccessTools.FieldRefAccess<KerbalEVA, Part>("constructionTarget");
        internal static Part ConstructionTarget(KerbalEVA eva) => _constructionTarget(eva);
        internal static void ConstructionTarget(KerbalEVA eva, Part value) => _constructionTarget(eva) = value;

        //HasWeldLineOfSight()
        internal static readonly Lazy<Func<KerbalEVA, bool>> _hasWeldLineOfSight =
            new Lazy<Func<KerbalEVA, bool>>(() => AccessTools.MethodDelegate<Func<KerbalEVA, bool>>(AccessTools.Method(typeof(KerbalEVA), "HasWeldLineOfSight", Type.EmptyTypes)));
        internal static bool HasWeldLineOfSight(KerbalEVA eva) => _hasWeldLineOfSight.Value(eva);

        //SurfaceContact()
        internal static readonly Lazy<Func<KerbalEVA, bool>> _surfaceContact =
            new Lazy<Func<KerbalEVA, bool>>(() => AccessTools.MethodDelegate<Func<KerbalEVA, bool>>(AccessTools.Method(typeof(KerbalEVA), "SurfaceContact", Type.EmptyTypes)));
        internal static bool SurfaceContact(KerbalEVA eva) => _surfaceContact.Value(eva);

        //playing_Golf_OnEnter
        internal static readonly Lazy<Action<KerbalEVA>> _playing_Golf_OnEnter =
            new Lazy<Action<KerbalEVA>>(() => AccessTools.MethodDelegate<Action<KerbalEVA>>(AccessTools.Method(typeof(KerbalEVA), "playing_Golf_OnEnter", Type.EmptyTypes)));
        internal static void playing_Golf_OnEnter(KerbalEVA eva) => _playing_Golf_OnEnter.Value(eva);

        //FinishedPlayingGolf
        internal static readonly Lazy<Action<KerbalEVA>> _finishedPlayingGolf =
            new Lazy<Action<KerbalEVA>>(() => AccessTools.MethodDelegate<Action<KerbalEVA>>(AccessTools.Method(typeof(KerbalEVA), "FinishedPlayingGolf", Type.EmptyTypes)));
        internal static void FinishedPlayingGolf(KerbalEVA eva) => _finishedPlayingGolf.Value(eva);

        internal static ref float CurrentSpd(KerbalEVA eva) => ref _currentSpd(eva);
        internal static ref float LastTgtSpeed(KerbalEVA eva) => ref _lastTgtSpeed(eva);
        internal static ref float TgtSpeed(KerbalEVA eva) => ref _tgtSpeed(eva);

        internal static ref Vector3 CmdDir(KerbalEVA eva) => ref _cmdDir(eva);
        internal static ref Vector3 CmdRot(KerbalEVA eva) => ref _cmdRot(eva);
        internal static ref Vector3 FUp(KerbalEVA eva) => ref _fUp(eva);
        internal static ref Vector3 TgtRpos(KerbalEVA eva) => ref _tgtRpos(eva);
        internal static ref Vector3 TgtFwd(KerbalEVA eva) => ref _tgtFwd(eva);
        internal static ref float DeltaHdg(KerbalEVA eva) => ref _deltaHdg(eva);
        internal static ref float TurnRate(KerbalEVA eva) => ref _turnRate(eva);
        internal static ref Animation Animation(KerbalEVA eva) => ref _animation(eva);
        internal static ref List<Collider> CurrentLadderTriggers(KerbalEVA eva) => ref _currentLadderTriggers(eva);
        internal static ref Vector3 PackLinear(KerbalEVA eva) => ref _packLinear(eva);
        internal static ref Vector3 PackTgtRPos(KerbalEVA eva) => ref _packTgtRPos(eva);
        internal static readonly Action<KerbalEVA> _updatePackFuel = AccessTools.MethodDelegate<Action<KerbalEVA>>(AccessTools.Method(typeof(KerbalEVA), "UpdatePackFuel", Type.EmptyTypes));
        internal static void UpdatePackFuel(KerbalEVA eva) { _updatePackFuel(eva); }

        internal static readonly AccessTools.FieldRef<KerbalEVA, float> _linFXLatch =
            AccessTools.FieldRefAccess<KerbalEVA, float>("linFXLatch");

        internal static readonly Action<KerbalEVA, bool> _toggleJetpack =
            AccessTools.MethodDelegate<Action<KerbalEVA, bool>>(AccessTools.Method(typeof(KerbalEVA), "ToggleJetpack", new[] { typeof(bool) }));
        internal static void ToggleJetpack(KerbalEVA eva, bool packState) => _toggleJetpack(eva, packState);

        internal static ref float FuelFlowRate(KerbalEVA eva) => ref _fuelFlowRate(eva);
        internal static ref bool WasVisorEnabledBeforeWelding(KerbalEVA eva) => ref _wasVisorEnabledBeforeWelding(eva);
        internal static ref KerbalEVA.VisorStates VisorState(KerbalEVA eva) => ref _visorState(eva);
        internal static ref ModuleEvaChute EvaChute(KerbalEVA eva) => ref _evaChute(eva);
        internal static ref bool IsAnchored(KerbalEVA eva) => ref _isAnchored(eva);
        internal static ref float KerbalAnchorTimeThreshold(KerbalEVA eva) => ref _kerbalAnchorTimeThreshold(eva);
        internal static ref float KerbalAnchorTimeCounter(KerbalEVA eva) => ref _kerbalAnchorTimeCounter(eva);
        internal static ref FixedJoint AnchorJoint(KerbalEVA eva) => ref _anchorJoint(eva);
        internal static ref bool IsHelmetEnabled(KerbalEVA eva) => ref _isHelmetEnabled(eva);

        // protected Vector3 flagSpot
        internal static readonly AccessTools.FieldRef<KerbalEVA, Vector3> _flagSpot =
            AccessTools.FieldRefAccess<KerbalEVA, Vector3>("flagSpot");
        internal static ref Vector3 FlagSpot(KerbalEVA eva) => ref _flagSpot(eva);

        // protected FlagSite flag
        internal static readonly AccessTools.FieldRef<KerbalEVA, FlagSite> _flag =
            AccessTools.FieldRefAccess<KerbalEVA, FlagSite>("flag");
        internal static ref FlagSite Flag(KerbalEVA eva) => ref _flag(eva);
    }
}
