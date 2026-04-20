using System;
using HarmonyLib;
using UnityEngine;

namespace G3MagnetBoots
{
    [KSPAddon(KSPAddon.Startup.Instantly, false)]
    internal sealed class HarmonyLoader : MonoBehaviour
    {
        private void Awake()
        {
            try
            {
                Harmony h2 = new("EVAMagBoots");
                h2.PatchAll(typeof(HarmonyLoader).Assembly);
                AccessTools.Method(typeof(KerbalEVA), "SetupFSM");
            }
            catch (Exception ex)
            {
                Logger.Exception(ex);
            }
        }

        
        // Treat hull as valid surface for all stock SurfaceContact() checks (weld, science, etc)
        [HarmonyPatch(typeof(KerbalEVA), "SurfaceContact")]
        internal static class Patch_KerbalEVA_SurfaceContact
        {
            static bool Prefix(KerbalEVA __instance, ref bool __result)
            {
                var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
                if (magBoots != null && magBoots.IsOnHull)
                {
                    __result = true;
                    return false;
                }
                return true;
            }
        }


        [HarmonyPatch(typeof(KerbalEVA), "CheckHelmetOffSafe", new[] { typeof(bool), typeof(bool) })]
        internal static class Patch_KerbalEVA_CheckHelmetOffSafe
        {
            static bool Prefix(bool includeSafetyMargins, bool startEVAChecks, ref bool __result)
            {
                try
                {
                    var cfg = G3MagnetBootsSettings.Current;
                    if (cfg != null && cfg.allowHelmetOffInSpace)
                    {
                        __result = true;
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex);
                }
                return true;
            }
        }

        // =========================================================================
        // Weld-on-Hull patches: use hull orientation/heading instead of stock float logic
        // =========================================================================

        [HarmonyPatch(typeof(KerbalEVA), "weld_acquireHeading_OnFixedUpdate")]
        internal static class Patch_KerbalEVA_weld_acquireHeading_OnFixedUpdate
        {
            static readonly Action<ModuleG3MagnetBoots> _refreshHullTarget = 
                AccessTools.MethodDelegate<Action<ModuleG3MagnetBoots>>(AccessTools.Method(typeof(ModuleG3MagnetBoots), "RefreshHullTarget"));
            static readonly Action<ModuleG3MagnetBoots> _orientToSurfaceNormal = 
                AccessTools.MethodDelegate<Action<ModuleG3MagnetBoots>>(AccessTools.Method(typeof(ModuleG3MagnetBoots), "OrientToSurfaceNormal"));
            static readonly Action<ModuleG3MagnetBoots> _updateHeading = 
                AccessTools.MethodDelegate<Action<ModuleG3MagnetBoots>>(AccessTools.Method(typeof(ModuleG3MagnetBoots), "UpdateHeading"));
            static readonly Action<ModuleG3MagnetBoots> _updateRagdollVelocities = 
                AccessTools.MethodDelegate<Action<ModuleG3MagnetBoots>>(AccessTools.Method(typeof(ModuleG3MagnetBoots), "updateRagdollVelocities"));

            static bool Prefix(KerbalEVA __instance)
            {
                if (__instance == null) return true;
                var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
                if (magBoots?.IsOnHull != true) return true; // use stock float logic

                // Hull path: use grounded-equivalent logic with hull orientation
                Logger.Info("[Weld] weld_acquireHeading_OnFixedUpdate: on hull, using grounded path with hull orientation");

                // Grounded branch equivalent: update hull orientation and heading
                _refreshHullTarget(magBoots);
                _orientToSurfaceNormal(magBoots);
                _updateHeading(magBoots);
                _updateRagdollVelocities(magBoots);

                return false; // skip original
            }
        }

        [HarmonyPatch(typeof(KerbalEVA), "weld_acquireHeading_OnLateUpdate")]
        internal static class Patch_KerbalEVA_weld_acquireHeading_OnLateUpdate
        {
            static readonly Action<ModuleG3MagnetBoots> _updateHeading = 
                AccessTools.MethodDelegate<Action<ModuleG3MagnetBoots>>(AccessTools.Method(typeof(ModuleG3MagnetBoots), "UpdateHeading"));

            static bool Prefix(KerbalEVA __instance)
            {
                if (__instance == null) return true;
                var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
                if (magBoots?.IsOnHull != true) return true; // use stock float logic (Slerp)

                // Hull path: use grounded-equivalent logic instead of full-body Slerp
                Logger.Info("[Weld] weld_acquireHeading_OnLateUpdate: on hull, using grounded path (SetWaypoint + UpdateHeading)");

                // Grounded branch: just set waypoint and heading, no body rotation
                Part constructionTarget = KerbalEVAAccess.ConstructionTarget(__instance);
                if (constructionTarget != null)
                    __instance.SetWaypoint(constructionTarget.transform.position);

                _updateHeading(magBoots);

                return false; // skip original (which does Slerp for float)
            }
        }

        [HarmonyPatch(typeof(KerbalEVA), "weld_OnFixedUpdate")]
        internal static class Patch_KerbalEVA_weld_OnFixedUpdate
        {
            static readonly Action<ModuleG3MagnetBoots> _refreshHullTarget = 
                AccessTools.MethodDelegate<Action<ModuleG3MagnetBoots>>(AccessTools.Method(typeof(ModuleG3MagnetBoots), "RefreshHullTarget"));
            static readonly Action<ModuleG3MagnetBoots> _orientToSurfaceNormal = 
                AccessTools.MethodDelegate<Action<ModuleG3MagnetBoots>>(AccessTools.Method(typeof(ModuleG3MagnetBoots), "OrientToSurfaceNormal"));
            static readonly Action<ModuleG3MagnetBoots> _updateMovementOnVessel = 
                AccessTools.MethodDelegate<Action<ModuleG3MagnetBoots>>(AccessTools.Method(typeof(ModuleG3MagnetBoots), "UpdateMovementOnVessel"));
            static readonly Action<ModuleG3MagnetBoots> _updateRagdollVelocities = 
                AccessTools.MethodDelegate<Action<ModuleG3MagnetBoots>>(AccessTools.Method(typeof(ModuleG3MagnetBoots), "updateRagdollVelocities"));

            static bool Prefix(KerbalEVA __instance)
            {
                if (__instance == null) return true;
                var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
                if (magBoots?.IsOnHull != true) return true; // use stock float logic

                Logger.Info("[Weld] weld_OnFixedUpdate: on hull, using grounded path with hull orientation + arm-aim blend");

                // Hull physics: keep kerbal planted and oriented to hull
                _refreshHullTarget(magBoots);
                _orientToSurfaceNormal(magBoots);
                _updateMovementOnVessel(magBoots);
                _updateRagdollVelocities(magBoots);

                // Zero angular velocity so kerbal doesn't drift while welding
                var rb = __instance.part.rb;
                if (rb != null)
                    rb.angularVelocity = Vector3.zero;

                // Arm-aim animation blend (mirrors stock grounded branch)
                Part constructionTarget = KerbalEVAAccess.ConstructionTarget(__instance);
                if (constructionTarget != null && rb != null)
                {
                    // Calculate angle from kerbal forward to construction target
                    // Use hull-aligned forward instead of world-space
                    Vector3 toTarget = constructionTarget.transform.position - __instance.transform.position;
                    float value = Vector3.SignedAngle(toTarget, __instance.transform.forward, __instance.transform.right);

                    value = Mathf.Clamp(value, -20f, 45f);

                    var anim = KerbalEVAAccess.Animation(__instance);
                    if (anim != null)
                    {
                        if (value > 0f)
                        {
                            // Target above kerbal: blend weld_aim_up
                            float t = value / 45f;
                            anim.Blend(__instance.Animations.weld_aim_up, t, 0.15f);
                            anim.Blend(__instance.Animations.weld_aim_down, 0f, 0.15f);
                            anim.Blend(__instance.Animations.weld, 1f - t, 0.15f);
                        }
                        else
                        {
                            // Target below kerbal: blend weld_aim_down
                            float t = -value / 20f;
                            anim.Blend(__instance.Animations.weld_aim_down, t, 0.15f);
                            anim.Blend(__instance.Animations.weld_aim_up, 0f, 0.15f);
                            anim.Blend(__instance.Animations.weld, 1f + value, 0.15f);
                        }
                    }
                }

                return false; // skip original (which does UpdateOrientationPID + LookAt for float)
            }
        }
    }

    // Patch into KerbalEVA.cs SetupFSM method to initialize custom hull states and events
    [HarmonyPatch(typeof(KerbalEVA), "SetupFSM")]
    internal static class Patch_KerbalEVA_SetupFSM
    {
        static void Postfix(KerbalEVA __instance)
        {
            if (__instance == null) return;
            var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
            magBoots?.HookIntoEva(__instance);

            var velMatch = __instance.part?.FindModuleImplementing<ModuleG3VelocityMatch>();
            velMatch?.HookIntoEva(__instance);
        }
    }

    // Postfix HandleMovementInput so our thrust contribution is added to packTgtRPos after player input, before fsm.FixedUpdateFSM() calls UpdatePackLinear. This routes velocity match thrust through the full stock pipeline: UpdatePackLinear -> packLinear -> AddForce + fuelFlowRate -> JetpackIsThrusting -> FX + fuel drain.
    [HarmonyPatch(typeof(KerbalEVA), "HandleMovementInput")]
    internal static class Patch_KerbalEVA_HandleMovementInput
    {
        static void Postfix(KerbalEVA __instance)
        {
            if (__instance == null) return;
            var velMatch = __instance.part?.FindModuleImplementing<ModuleG3VelocityMatch>();
            if (velMatch == null) return;

            Vector3 playerInput = KerbalEVAAccess.PackTgtRPos(__instance);
            Vector3 contribution = velMatch.GetPackTgtRPosContribution(playerInput);
            if (contribution != Vector3.zero)
                KerbalEVAAccess.PackTgtRPos(__instance) += contribution;
        }
    }

    // Track whether the current ToggleJetpack(bool) call originated from the public parameterless ToggleJetpack(), which is the player-facing API (keybind / UI). KSP stock code also calls ToggleJetpack(bool) directly during FSM transitions (swim, ladder, landing, etc.) — those are system-initiated and must NOT set _playerStowedJetpack, otherwise auto-deploy after hull jump / velocity match breaks.
    [HarmonyPatch(typeof(KerbalEVA), "ToggleJetpack", new System.Type[0])]
    internal static class Patch_KerbalEVA_ToggleJetpackPublic
    {
        internal static bool _fromPublicToggle;
        static void Prefix() => _fromPublicToggle = true;
        static void Postfix() => _fromPublicToggle = false;
    }




    // Only mark as player-stowed when the call came through the public parameterless overload (i.e. the player pressed the jetpack key). System stows are ignored. Deploys always clear the flag regardless of origin.
    [HarmonyPatch(typeof(KerbalEVA), "ToggleJetpack", new[] { typeof(bool) })]
    internal static class Patch_KerbalEVA_ToggleJetpack
    {
        static void Postfix(KerbalEVA __instance, bool packState)
        {
            if (__instance == null) return;
            var velMatch = __instance.part?.FindModuleImplementing<ModuleG3VelocityMatch>();
            if (velMatch == null) return;

            if (packState)
            {
                velMatch.OnPlayerDeployedJetpack();  // clear intent
            }
            else if (Patch_KerbalEVA_ToggleJetpackPublic._fromPublicToggle)
            {
                velMatch.OnPlayerStowedJetpack();   // player only
            }
        }
    }

    // When on a hull, replace ScanSurroundingTerrain (layer 32768 terrain only) with a raycast that hits vessel parts (layer 32769 = terrain | default).  The kerbal is already surface-aligned by magboots so transform.up IS the hull normal — we shoot forward+down from the kerbal's position to find the exact hull surface point.
    [HarmonyPatch(typeof(KerbalEVA), "flagAcquireHeading_OnEnter")]
    internal static class Patch_KerbalEVA_flagAcquireHeading_OnEnter
    {
        static void Postfix(KerbalEVA __instance)
        {
            if (__instance == null) return;
            var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
            if (magBoots?.IsOnHull != true) return;

            // Raycast from forward+up toward hull surface on both terrain and default layers
            Vector3 origin = __instance.transform.position
                + __instance.transform.forward * __instance.flagReach
                + __instance.transform.up * 1f;
            if (Physics.Raycast(origin, -__instance.transform.up, out RaycastHit hit, 3f, 32769, QueryTriggerInteraction.Ignore))
                KerbalEVAAccess.FlagSpot(__instance) = hit.point;
            else
                KerbalEVAAccess.FlagSpot(__instance) = __instance.transform.position + __instance.transform.forward * __instance.flagReach;

            __instance.SetWaypoint(KerbalEVAAccess.FlagSpot(__instance));
        }
    }

    // When on a hull, fully replace flagPlant_OnEnter so the raycast uses layer 32769 (terrain + default) and hits vessel geometry.  Running as a prefix that returns false skips the original entirely, preventing the double CreateFlag fire that would happen if we let stock run first (terrain raycast misses -> CreateFlag at zero, then we destroy+respawn).
    [HarmonyPatch(typeof(KerbalEVA), "flagPlant_OnEnter")]
    internal static class Patch_KerbalEVA_flagPlant_OnEnter
    {
        internal static Rigidbody PendingHullRb;
        internal static bool PlantInProgress;

        static bool Prefix(KerbalEVA __instance)
        {
            if (__instance == null) return true;
            var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
            if (magBoots?.IsOnHull != true) return true;

            PlantInProgress = true;

            // Mirror stock preamble
            KerbalEVAAccess.DeltaHdg(__instance) = 0f;
            __instance.GetComponent<Rigidbody>().angularVelocity = Vector3.zero;
            KerbalEVAAccess.Animation(__instance).CrossFade(__instance.Animations.flagPlant, 0.2f, PlayMode.StopAll);
            InputLockManager.SetControlLock(~(ControlTypes.UI | ControlTypes.CAMERACONTROLS), "FlagDeployLock_" + __instance.vessel.id);

            Vector3 origin = __instance.transform.position
                + __instance.transform.forward * __instance.flagReach
                + __instance.transform.up * 1f;

            Vector3 spot;
            if (Physics.Raycast(origin, -__instance.transform.up, out RaycastHit hit, 3f, 32769, QueryTriggerInteraction.Ignore))
            {
                spot = hit.point;
            }
            else
            {
                spot = __instance.transform.position + __instance.transform.forward * __instance.flagReach;
            }

            PendingHullRb = magBoots.HullTargetRigidbody;
            if (PendingHullRb != null)
                Logger.Info($"[HullFlag]   hull rb velocity = {PendingHullRb.velocity}  pos={PendingHullRb.position}");

            KerbalEVAAccess.FlagSpot(__instance) = spot;
            Logger.Info($"[HullFlag]   calling CreateFlag at spot={spot}  rot={Quaternion.LookRotation(__instance.transform.forward, __instance.transform.up).eulerAngles}");

            var flagSite = FlagSite.CreateFlag(
                spot,
                Quaternion.LookRotation(__instance.transform.forward, __instance.transform.up),
                __instance.part);
            KerbalEVAAccess.Flag(__instance) = flagSite;

            if (flagSite != null)
            {
                // Temporarily set crashTolerance to infinity
                float originalCrashTol = flagSite.part.crashTolerance;
                flagSite.part.crashTolerance = float.MaxValue;

                var flagRb = flagSite.GetComponent<Rigidbody>();
                if (flagRb != null)
                {
                    if (PendingHullRb != null)
                    {
                        flagRb.velocity = PendingHullRb.velocity;
                    }
                }

                var allCols = flagSite.GetComponentsInChildren<Collider>();
                foreach (var col in allCols)
                    col.enabled = false;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(FlagSite), "SetJoint")]
    internal static class Patch_FlagSite_SetJoint
    {
        static void Postfix(FlagSite __instance)
        {
            Rigidbody hullRb = Patch_KerbalEVA_flagPlant_OnEnter.PendingHullRb;
            if (hullRb == null)
            {
                return;
            }

            var joint = __instance.GetComponent<ConfigurableJoint>();
            if (joint == null) return;

            joint.connectedBody = hullRb;
            // projectionMode = None prevents a correction impulse on first frame. Do NOT touch anchor/connectedAnchor
            joint.projectionMode = JointProjectionMode.None;

            var flagRb = __instance.GetComponent<Rigidbody>();
            if (flagRb != null)
                Logger.Info($"[HullFlag]   flag rb velocity at SetJoint = {flagRb.velocity}  hull rb velocity = {hullRb.velocity}");

            Patch_KerbalEVA_flagPlant_OnEnter.PendingHullRb = null;
        }
    }

    // Restore crashTolerance once the flag is fully placed (deploy animation done). We set it to MaxValue at spawn to prevent _CheckPartG killing the flag during the first physics frames while the joint stabilises.
    [HarmonyPatch(typeof(FlagSite), "OnPlacementComplete")]
    internal static class Patch_FlagSite_OnPlacementComplete
    {
        // Default crash tolerance from the flag part prefab
        private const float DefaultFlagCrashTolerance = 9f;

        static void Prefix(FlagSite __instance)
        {
            if (__instance?.part == null) return;
            if (__instance.part.crashTolerance == float.MaxValue)
            {
                __instance.part.crashTolerance = DefaultFlagCrashTolerance;
                Logger.Info($"[HullFlag] OnPlacementComplete: crashTolerance restored to {DefaultFlagCrashTolerance}");
            }
        }
    }

    // Suppress _CheckPartG entirely for the flag vessel while a hull plant is in progress. crashTolerance=MaxValue blocks the velocity path but _CheckPartG also checks joint constraint forces independently — the locked ConfigurableJoint generates a large force spike on its first solve due to anchor offset, which triggers that path regardless of crashTolerance.  Safe to suppress: the flag has no crew and the PlantInProgress gate ensures this only fires during our hull-plant window.
    [HarmonyPatch(typeof(Part), "_CheckPartG")]
    internal static class Patch_Part_CheckPartG
    {
        static bool Prefix(Part __instance)
        {
            if (!Patch_KerbalEVA_flagPlant_OnEnter.PlantInProgress) return true;
            if (__instance?.GetComponent<FlagSite>() != null)
            {
                return false;
            }
            return true;
        }
    }

    // Suppress Part.explode on the flag part during hull plant — a nearby blast uses Part.explode directly, bypassing _CheckPartG entirely (blastAwesomeness path). crashTolerance=MaxValue does not protect against this path. Patch the float overload — the real implementation. The parameterless overload just delegates to this one, so patching here catches both call paths.
    [HarmonyPatch(typeof(Part), "explode", new[] { typeof(float) })]
    internal static class Patch_Part_Explode
    {
        static bool Prefix(Part __instance)
        {
            if (!Patch_KerbalEVA_flagPlant_OnEnter.PlantInProgress) return true;
            if (__instance?.GetComponent<FlagSite>() != null)
            {
                return false;
            }
            return true;
        }
    }

    // Log when FlagSite's own OnJointBreak fires — this is the moment the flag detaches
    [HarmonyPatch(typeof(FlagSite), "OnJointBreak")]
    internal static class Patch_FlagSite_OnJointBreak
    {
        static void Prefix(FlagSite __instance, float breakForce)
        {
            Logger.Info($"[HullFlag] *** OnJointBreak *** flag={__instance?.name}  breakForce={breakForce:F2}");
            var joint = __instance?.GetComponent<ConfigurableJoint>();
            if (joint != null)
                Logger.Info($"[HullFlag]   joint.breakForce={joint.breakForce}  connectedBody={joint.connectedBody?.name ?? "null"}");
        }
    }

    // Log when FlagSite.Start fires — this is when SetupFSM and SetJoint actually run
    [HarmonyPatch(typeof(FlagSite), "Start")]
    internal static class Patch_FlagSite_Start
    {
        static void Prefix(FlagSite __instance)
        {
            Logger.Info($"[HullFlag] FlagSite.Start firing  name={__instance?.name}  frame={Time.frameCount}");
            Logger.Info($"[HullFlag]   part.packed          = {__instance?.part?.packed}");
            Logger.Info($"[HullFlag]   PendingHullRb        = {(Patch_KerbalEVA_flagPlant_OnEnter.PendingHullRb == null ? "null" : Patch_KerbalEVA_flagPlant_OnEnter.PendingHullRb.name)}");
            var flagRb = __instance?.GetComponent<Rigidbody>();
            if (flagRb != null)
                Logger.Info($"[HullFlag]   flag rb velocity at Start = {flagRb.velocity}");
        }
    }

    // Log any part death that happens while a hull flag plant is in progress
    [HarmonyPatch(typeof(Part), "Die")]
    internal static class Patch_Part_Die
    {
        static void Prefix(Part __instance)
        {
            if (!Patch_KerbalEVA_flagPlant_OnEnter.PlantInProgress) return;
            Logger.Info($"[HullFlag] *** Part.Die *** part={__instance?.name}  vessel={__instance?.vessel?.vesselName}  crashTol={__instance?.crashTolerance}");
            Logger.Info(Environment.StackTrace);
        }
    }

    [HarmonyPatch(typeof(KerbalEVA), "flagPlant_OnLeave")]
    internal static class Patch_KerbalEVA_flagPlant_OnLeave
    {
        static bool Prefix(KerbalEVA __instance)
        {
            if (__instance == null) return true;
            var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
            Logger.Info($"[HullFlag] flagPlant_OnLeave PREFIX entry  FlagStartedFromHull={magBoots?.FlagStartedFromHull}  PlantInProgress={Patch_KerbalEVA_flagPlant_OnEnter.PlantInProgress}");
            if (magBoots == null || !magBoots.FlagStartedFromHull) return true;

            FlagSite flag = KerbalEVAAccess.Flag(__instance);
            bool completed = __instance.fsm.LastEvent == __instance.On_flagPlantComplete;

            Logger.Info($"[HullFlag] flagPlant_OnLeave PREFIX");
            Logger.Info($"[HullFlag]   completed        = {completed}");
            Logger.Info($"[HullFlag]   flag ref         = {(flag == null ? "NULL/destroyed" : flag.name)}");
            Logger.Info($"[HullFlag]   lastEvent        = {__instance.fsm.LastEvent?.name ?? "null"}");

            KerbalEVAAccess.LastTgtSpeed(__instance) = 0f;
            InputLockManager.RemoveControlLock("FlagDeployLock_" + __instance.vessel.id);

            if (!completed)
            {
                Logger.Info("[HullFlag]   → OnPlacementFail path");
                if (flag != null && flag)
                    flag.OnPlacementFail();
                else
                    Logger.Info("[HullFlag]   flag was null/destroyed — skipping OnPlacementFail");
            }
            else if (flag != null && flag)
            {
                Logger.Info("[HullFlag]   → OnPlacementComplete path");
                flag.OnPlacementComplete();

                // Couple the flag part into the hull vessel so it moves with it through timewarp and persists across save/load. Coupling:   1. Merges the flag's single-part vessel into the hull vessel's part tree   2. Creates a proper PartJoint (attachJoint) that KSP serialises   3. Destroys the flag's separate Vessel object  We then destroy our temporary ConfigurableJoint — the PartJoint replaces it.
                Rigidbody hullRb = magBoots.HullTargetRigidbody;
                Part hullPart = hullRb != null ? hullRb.GetComponent<Part>() : null;
                if (hullPart != null)
                {
                    Logger.Info($"[HullFlag]   coupling flag to hull part={hullPart.name}");
                    flag.part.attachMode = AttachModes.SRF_ATTACH;
                    flag.part.Couple(hullPart);
                    // Destroy the temporary ConfigurableJoint — the attachJoint takes over.
                    var tempJoint = flag.part.GetComponent<ConfigurableJoint>();
                    if (tempJoint != null)
                        UnityEngine.Object.Destroy(tempJoint);
                    Logger.Info("[HullFlag]   coupling complete");
                }
                else
                {
                    Logger.Info("[HullFlag]   hullPart not found — flag will not be coupled (will drift)");
                }

                string bodyName = __instance.vessel.orbit.referenceBody.name;
                Logger.Info($"[HullFlag]   logging PlantFlag for body={bodyName}");
                __instance.part.protoModuleCrew[0].flightLog.AddEntryUnique(FlightLog.EntryType.PlantFlag, bodyName);
                __instance.part.protoModuleCrew[0].UpdateExperience();

                int count = FlightGlobals.VesselsLoaded.Count;
                while (count-- > 0)
                {
                    Vessel v = FlightGlobals.VesselsLoaded[count];
                    if (v == null || !v.loaded || v == FlightGlobals.ActiveVessel) continue;
                    if (v.vesselType == VesselType.EVA)
                    {
                        ProtoCrewMember c = v.GetVesselCrew()[0];
                        c.flightLog.AddEntryUnique(FlightLog.EntryType.PlantFlag, bodyName);
                        c.UpdateExperience();
                    }
                    else if (v.situation == Vessel.Situations.LANDED ||
                             v.situation == Vessel.Situations.SPLASHED ||
                             v.situation == Vessel.Situations.PRELAUNCH)
                    {
                        var crew = v.GetVesselCrew();
                        int c2 = crew.Count;
                        while (c2-- > 0)
                        {
                            crew[c2].flightLog.AddEntryUnique(FlightLog.EntryType.PlantFlag, bodyName);
                            crew[c2].UpdateExperience();
                        }
                    }
                }
            }

            KerbalEVAAccess.Flag(__instance) = null;
            Patch_KerbalEVA_flagPlant_OnEnter.PlantInProgress = false;
            return false;
        }
    }

    // Low-priority prefix on flagPlant_OnLeave — runs after our hull prefix. When the hull prefix returned false this is skipped entirely by Harmony. When the hull prefix passed through (non-hull plant), this null-guards flag before the original flagPlant_OnLeave calls flag.OnPlacementFail/Complete.
    [HarmonyPatch(typeof(KerbalEVA), "flagPlant_OnLeave")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_KerbalEVA_flagPlant_OnLeave_NREGuard
    {
        static bool Prefix(KerbalEVA __instance)
        {
            if (__instance == null) return true;
            FlagSite flag = KerbalEVAAccess.Flag(__instance);
            if (flag != null && flag) return true; // flag alive, stock handles

            // flag is null or destroyed — replicate stock skip
            KerbalEVAAccess.LastTgtSpeed(__instance) = 0f;
            InputLockManager.RemoveControlLock("FlagDeployLock_" + __instance.vessel.id);
            KerbalEVAAccess.Flag(__instance) = null;
            return false;
        }
    }

    /// <summary>
    /// Fixes EVA construction welding when on a hull.
    ///
    /// Root cause: SurfaceContact() returns false on a vessel hull (no terrain contact),
    /// so all three weld FSM callbacks fall into the "floating in space" branch:
    ///   - weld_acquireHeading_OnFixedUpdate  → UpdateOrientationPID() spins the whole kerbal
    ///   - weld_acquireHeading_OnLateUpdate   → Slerp rotates whole kerbal toward target
    ///   - weld_OnFixedUpdate                → LookAt spins whole kerbal instead of arm-blending
    ///
    /// Fix: postfix each method so that when the kerbal is on a hull we redirect to the
    /// grounded code path (arm-aim animation blending, correctGroundedRotation, etc.)
    /// instead of the free-float path (full-body rotation in 3D).
    ///
    /// We also keep the kerbal pinned to the hull during the weld states via the existing
    /// hull physics callbacks, which are already registered on the stock weld FSM states
    /// via On_weldStart being added to st_idle_hull / st_walk_hull.
    /// </summary>

    // -------------------------------------------------------------------------
    // weld_acquireHeading_OnFixedUpdate
    // Stock grounded path: correctGroundedRotation() + UpdateMovement() + UpdateHeading()
    // Stock float path:    UpdateOrientationPID()
    // TODO: Weld-on-hull feature removed due to incomplete implementation.
    // The other AI wrote patches that call non-existent Public wrapper methods
    // on ModuleG3MagnetBoots and reference non-existent KerbalEVA.constructionTarget.
    // This feature should be re-implemented properly if needed.
}
