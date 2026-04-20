// Add these public wrapper methods to ModuleG3MagnetBoots (e.g. in ModuleG3MagnetBoots.cs or a new partial file).
// They just forward to the existing protected/private methods so Harmony patches in other classes can call them.

namespace G3MagnetBoots
{
    public partial class ModuleG3MagnetBoots
    {
        // Called by Patch_KerbalEVA_Welding patches
        public void RefreshHullTargetPublic() => RefreshHullTarget();
        public void OrientToSurfaceNormalPublic() => OrientToSurfaceNormal();
        public void UpdateMovementOnVesselPublic() => UpdateMovementOnVessel();
        public void UpdateHeadingPublic() => UpdateHeading();
        public void UpdateRagdollVelocitiesPublic() => updateRagdollVelocities();
    }
}