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
        public string GetModuleTitle() { return "Magnetic Boots No Attach Module"; }
        public override string GetInfo() { return "Lorem ipsum dolor sit amet consequitor"; }
        public Callback<Rect> GetDrawModulePanelCallback() { return null; }
        public string GetPrimaryField() { return "Lorem ipsum"; }

    }
}
