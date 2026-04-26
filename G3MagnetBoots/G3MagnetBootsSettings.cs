using System.Reflection;

namespace G3MagnetBoots
{
    public class G3MagnetBootsSettings : GameParameters.CustomParameterNode
    {
        public override string Title => "QoL";
        public override string DisplaySection => "G3MagnetBoots";
        public override string Section => "G3MagnetBoots";
        public override int SectionOrder => 1;
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
        public override bool HasPresets => false;

        [GameParameters.CustomParameterUI("Auto-enable Jetpack", toolTip = "Automatically enable jetpack when detaching from hull to improve quality of life when falling into space")]
        public bool jetpackAutoToggleEnabled = true;

        [GameParameters.CustomParameterUI("Enable LOCKED camera when on hull", toolTip = "Re-enable the LOCKED camera mode for EVA Kerbals, but only while on hull")]
        public bool magbootsLockedCameraModeEnabled = true;

        //[GameParameters.CustomFloatParameterUI("Magboots hull attraction force", minValue = 0.0f, maxValue = 15.0f, stepCount = 31, displayFormat = "F1", toolTip = "Strength of surface normal snapping force")] // increment of 0.5
        public float magbootsClampForce= 4.0f;

        //[GameParameters.CustomFloatParameterUI("Magboots static friction force", minValue = 0.0f, maxValue = 15.0f, stepCount = 31, displayFormat = "F1", toolTip = "Strength of anti-surface-sliding force")] // increment of 0.5
        public float magbootsStaticFrictionForce = 6.0f;

        [GameParameters.CustomParameterUI("Show Debug Info", toolTip = "Enable logging for developers")]
        public bool isDebugMode = false;

        [GameParameters.CustomFloatParameterUI("(SmartRCS) Maximum SmartRCS Jetpack Thrust", minValue = 0.1f, maxValue = 1.0f, stepCount = 19, displayFormat = "F2", toolTip = "Maximum thrust fraction for SmartRCS/velocity match (manual input always uses full thrust)")]
        public float maxSmartRcsThrust = 0.5f;

        [GameParameters.CustomParameterUI("Allow taking off helmet in a vacuum (for roleplay)", toolTip = "When enabled, kerbals may remove their helmets even without atmosphere (useful for roleplay/photography). WARNING: may cause death)")]
        public bool allowHelmetOffInSpace = false;

        [GameParameters.CustomFloatParameterUI("(SmartRCS) Velocity Match Dead Zone (m/s)", minValue = 0.01f, maxValue = 0.5f, stepCount = 50, displayFormat = "F2", toolTip = "Relative speed below which velocity is considered matched")]
        public float velocityMatchDeadZone = 0.02f;

        public override bool Enabled(MemberInfo member, GameParameters parameters) => true;
        public override bool Interactible(MemberInfo member, GameParameters parameters) => true;

        public static G3MagnetBootsSettings Current => HighLogic.CurrentGame?.Parameters.CustomParams<G3MagnetBootsSettings>();
    }

    public class G3MagnetBootsDifficultySettings : GameParameters.CustomParameterNode
    {
        public override string Title => "Difficulty";
        public override string DisplaySection => "G3MagnetBoots";
        public override string Section => "G3MagnetBoots";
        public override int SectionOrder => 2;
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
        public override bool HasPresets => false;

        [GameParameters.CustomParameterUI("Allow asteroids", toolTip = "Allow magnet boots to function on asteroids and comets")]
        public bool magbootsAsteroidsEnabled = true;

        [GameParameters.CustomParameterUI("Require micro-gravity", toolTip = "Prevent magnet boots from working below 3500m altitude to avoid interference with parachute deployment and atmospheric flight")]
        public bool magbootsRequireHighAltitudeEnabled = false;

        [GameParameters.CustomParameterUI("Allow packing parachute on hull", toolTip = "Allow kerbals to pack their parachute while attached to hull")]
        public bool magbootsRepackChuteEnabled = true;

        [GameParameters.CustomParameterUI("(SmartRCS) Enable velocity matching", toolTip = "Allow kerbals with an RCS pack to match velocity with a nearby target vessel")]
        public bool velocityMatchEnabled = true;

        [GameParameters.CustomParameterUI("(SmartRCS) Emergency velocity match on ragdoll", toolTip = "Automatically enable velocity matching if the Kerbal goes ragdoll while a target is set, even if it was not already enabled")]
        public bool emergencyVelocityMatchOnRagdoll = true; 
        
        [GameParameters.CustomParameterUI("Enable planting flags on hull (WIP: BACKUP WORLD FIRST)", toolTip = "Allow kerbals to plant flags while attached to hull")]
        public bool magbootsPlantFlagEnabled = false;

        public override bool Enabled(MemberInfo member, GameParameters parameters) => true;
        public override bool Interactible(MemberInfo member, GameParameters parameters) => true;

        public static G3MagnetBootsDifficultySettings Current => HighLogic.CurrentGame?.Parameters.CustomParams<G3MagnetBootsDifficultySettings>();
    }
}
