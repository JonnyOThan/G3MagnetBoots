using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace G3MagnetBoots
{
    public class ModuleG3NoAttach : PartModule, IModuleInfo // Marker class to identify parts that should not be attachable for hull walking
    {
        public string GetModuleTitle() { return "G3MagnetBoots Slippery Module"; }
        public override string GetInfo() { return "This part cannot be attached to when using magnetic boots."; }
        public Callback<Rect> GetDrawModulePanelCallback() { return null; }
        public string GetPrimaryField() { return "G3MagnetBoots Slippery"; }

    }
}
