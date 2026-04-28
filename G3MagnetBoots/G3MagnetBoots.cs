using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace G3MagnetBoots
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    internal class G3MagnetBoots : MonoBehaviour
    {
        public static G3MagnetBoots Instance;
        internal static G3MagnetBootsSettings Settings => G3MagnetBootsSettings.Current;
        internal static bool lockedCameraModeEnabled => Settings?.magbootsLockedCameraModeEnabled ?? false;

        internal const string MODID = "G3MagnetBoots";
        internal const string MODNAME = "G3 Magnet Boots";

        private void Awake()
        {
            Instance = this;
            Logger.Trace("G3MagnetBoots Awake");
        }

        private void OnDestroy() { }

        private void Start() { Logger.Trace("G3MagnetBoots Start"); }

        private void Update() { }
    }
}