namespace G3MagnetBoots
{
    internal static class VesselUtils
    {
        internal static bool IsAGOn(Vessel vessel, KSPActionGroup g) => vessel != null && vessel.ActionGroups[g];
        internal static void SetAG(Vessel vessel, KSPActionGroup g, bool active) => vessel?.ActionGroups.SetGroup(g, active);
        internal static void ToggleAG(Vessel vessel, KSPActionGroup g) => vessel?.ActionGroups.ToggleGroup(g);
        internal static bool VesselUnderControl(KerbalEVA kerbal) => kerbal != null && kerbal.vessel != null && FlightGlobals.ActiveVessel == kerbal.vessel;
    }
}
