using CameraFXModules;
using HarmonyLib;
using KSP.UI;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace G3MagnetBoots
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    internal sealed class EVACameraPatch : MonoBehaviour
    {
        private void Start()
        {
            GameEvents.onVesselSwitching.Add(OnVesselSwitching);
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselDestroy.Add(OnVesselDestroy);
            GameEvents.onGameSceneLoadRequested.Add(OnSceneLoadRequested);
        }

        private void OnDestroy()
        {
            GameEvents.onVesselSwitching.Remove(OnVesselSwitching);
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselDestroy.Remove(OnVesselDestroy);
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneLoadRequested);
        }

        private void OnVesselSwitching(Vessel from, Vessel to)
        {
            EvaLockedBoomCamera.ForceReleaseToStock(to);
        }

        private void OnVesselChange(Vessel v)
        {
            EvaLockedBoomCamera.ForceReleaseToStock(v);
        }

        private void OnVesselDestroy(Vessel v)
        {
            EvaLockedBoomCamera.ForceReleaseToStock(null);
        }

        private void OnSceneLoadRequested(GameScenes scene)
        {
            EvaLockedBoomCamera.ForceReleaseToStock(null);
        }
    }
}