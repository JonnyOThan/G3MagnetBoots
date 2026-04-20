# Analysis: On-Hull States and Events Implementation

## Executive Summary

Your G3MagnetBoots mod already has a **complete, working implementation** of on-hull golf science experiments. The system is well-architected with proper FSM state management, Harmony patches, and science data collection. Here's what exists and how it works.

---

## 1. Current Implementation Status

### ✅ **ALREADY IMPLEMENTED:**
- Custom hull FSM states (`st_idle_hull`, `st_walk_hull`, `st_jump_hull`)
- Golf animation support while on hull
- Science data collection for hull golf experiments
- Harmony patches to intercept vanilla Dzhanibekov (spinning wingnut) and redirect to golf
- Proper FSM event routing to/from golf state
- Hull physics maintained during golf animation

---

## 2. FSM State Architecture

### **Vanilla KSP Golf State**
- **State:** `st_playing_golf`
- **Trigger:** `On_Playing_Golf` event
- **Conditions:** Only works on **ground** (requires `SurfaceContact()`)
- **Animation:** Golf swing animation
- **Completion:** `On_Golf_Complete` → returns to `st_idle_gr` (ground idle)
- **Physics:** Kerbal is grounded, standard gravity physics

### **Your Hull Golf Implementation**
Located in `ModuleG3MagnetBoots.cs` (lines 436-638):

#### **State Hookup (lines 436-453):**
```csharp
// Allow golf event to trigger from hull states
FSM.AddEvent(Kerbal.On_Playing_Golf, st_idle_hull);
FSM.AddEvent(Kerbal.On_Playing_Golf, st_walk_hull);

// Hook into golf lifecycle:
Kerbal.On_Playing_Golf.OnEvent += On_Playing_Golf_Hull_Hook;
Kerbal.st_playing_golf.OnFixedUpdate += playing_golf_hull_OnFixedUpdate;
Kerbal.On_Golf_Complete.OnEvent += On_Golf_Complete_Hull_Redirect;
```

#### **Key Differences from Vanilla:**

| Aspect | Vanilla Golf | Hull Golf |
|--------|--------------|-----------|
| **Available States** | `st_idle_gr`, `st_walk_gr` | `st_idle_hull`, `st_walk_hull` |
| **Trigger Conditions** | Ground contact | Magnetic attachment to hull |
| **Physics During Animation** | Grounded physics | Hull attachment physics (lines 619-625) |
| **Return State** | `st_idle_gr` | `st_idle_hull` (dynamically set) |
| **Location** | Planetary surfaces | Vessel hulls in space/orbit |
| **Movement Handling** | Stock ground movement | Custom hull movement (zeroed for golf) |

---

## 3. Golf Animation Lifecycle on Hull

### **Phase 1: Trigger (from ModuleG3HullGolfScience.cs)**
```csharp
// Line 115: User clicks "Play Hull Golf" button
eva.PlayGolf(OnAnimationComplete);
```

### **Phase 2: Pre-Animation Hook**
```csharp
// ModuleG3MagnetBoots.cs lines 612-617
private void On_Playing_Golf_Hull_Hook()
{
    if (FSM.CurrentState != st_idle_hull && FSM.CurrentState != st_walk_hull) return;
    _golfStartedFromHull = true;        // ← FLAG: tracks hull origin
    ZeroHullMovementForScience();       // ← Stops all movement
}
```

**What happens:**
- Sets `_golfStartedFromHull = true` flag
- Zeros out movement vectors (`tgtRpos`, `tgtSpeed`, `lastTgtSpeed`)
- Prevents kerbal from drifting during animation

### **Phase 3: Animation Physics (FixedUpdate loop)**
```csharp
// Lines 619-625
private void playing_golf_hull_OnFixedUpdate()
{
    if (!_golfStartedFromHull) return;  // Only for hull-originated golf
    RefreshHullTarget();                // Keep tracking hull surface
    OrientToSurfaceNormal();            // Maintain surface alignment
    UpdateMovementOnVessel();           // Apply hull physics
}
```

**Critical:** This maintains magnetic attachment during the golf swing animation. Without this, the kerbal would drift away mid-swing.

### **Phase 4: Completion Redirect**
```csharp
// Lines 627-638
private void On_Golf_Complete_Hull_Redirect()
{
    if (_golfStartedFromHull)
    {
        // Dynamically change the event's target state
        Kerbal.On_Golf_Complete.GoToStateOnEvent = st_idle_hull;
        _golfStartedFromHull = false;   // Reset flag
    }
    else
    {
        // Vanilla behavior: return to ground idle
        Kerbal.On_Golf_Complete.GoToStateOnEvent = Kerbal.st_idle_gr;
    }
}
```

**Key Innovation:** The FSM event's target state is **dynamically modified** based on where golf originated:
- Hull golf → `st_idle_hull`
- Ground golf → `st_idle_gr`

---

## 4. Science Data Collection

### **ModuleG3HullGolfScience.cs** - Full Implementation

#### **Module Purpose (lines 8-18):**
```
Science experiment module that plays the golf animation when a Kerbal is
magnetically attached to a vessel hull via magnet boots.

Designed for use in space (orbit / sub-orbital) where the vanilla
ModuleScienceExperiment would otherwise dispatch the Spinning Wingnut
(Dzhanibekov) animation instead of golf.
```

#### **Deployment Flow:**
1. **User Action:** Clicks "Play Hull Golf" (line 82)
2. **Validation:**
   - Must be on hull (`_magBoots.IsOnHull`)
   - Experiment must exist in R&D database
   - Situation must be valid (orbit/space)
3. **Animation Trigger:** `eva.PlayGolf(OnAnimationComplete)` (line 115)
4. **Science Collection:** `OnAnimationComplete()` callback (lines 121-154)
   - Gets experiment situation (orbit, sub-orbital, etc.)
   - Determines biome if relevant
   - Creates `ScienceData` object
   - Shows results dialog
5. **Results UI:** Full science dialog with transmit/store/lab options (lines 156-202)

#### **Science Experiment Definition:**
```cfg
// G3MagnetBoots_Science.cfg
EXPERIMENT_DEFINITION
{
    id = hullGolf
    title = EVA Science Experiment
    baseValue = 20
    scienceCap = 20
    dataScale = 1

    requireAtmosphere = False
    requireNoAtmosphere = true
    situationMask = 48           // InSpaceLow + InSpaceHigh
    biomeMask = 0                // No biome variation
}
```

---

## 5. Harmony Patch: Dzhanibekov Intercept

### **The Problem:**
Vanilla `ModuleScienceExperiment` with `KerbalAction = "Dzhanibekov"` triggers the spinning wingnut animation in space. But `On_Spinning_Wingnut` event is **not registered** to your hull states, causing:
- Warning in console
- Silent failure
- Experiment becomes unusable

### **The Solution (HarmonyLoader.cs lines 80-96):**
```csharp
[HarmonyPatch(typeof(KerbalEVA), "Dzhanibekov", new[] { typeof(Callback) })]
internal static class Patch_KerbalEVA_Dzhanibekov
{
    static bool Prefix(KerbalEVA __instance)
    {
        var magBoots = __instance.part?.FindModuleImplementing<ModuleG3MagnetBoots>();
        if (magBoots?.IsOnHull != true) return true;  // Not on hull: vanilla behavior

        // On hull: block stock wingnut, run golf instead
        var hullScience = __instance.part?.FindModuleImplementing<ModuleG3HullGolfScience>();
        hullScience?.DeployHullGolfExperiment();
        return false;  // ← BLOCKS vanilla Dzhanibekov call
    }
}
```

**Effect:**
- When on hull: Intercepts all Dzhanibekov calls → triggers golf experiment instead
- When on ground/in space (no hull): Vanilla behavior (spinning wingnut in space, golf on ground)

---

## 6. Hull vs Vanilla State Differences

### **Core Distinction:**

| Feature | Vanilla States | Hull States |
|---------|----------------|-------------|
| **Attachment** | Gravity/ground collision | Magnetic spherecast to vessel |
| **Movement** | Horizontal (WASD) | Relative to hull surface |
| **Orientation** | Gravity up vector | Surface normal of hull |
| **Physics** | Stock RB on ground | Custom surface tracking + vessel velocity |
| **Jetpack** | Full 6DOF | Restricted to up/down only |
| **State Names** | `st_idle_gr`, `st_walk_fps`, etc. | `st_idle_hull`, `st_walk_hull`, `st_jump_hull` |
| **Events** | `On_ladderGrab`, `On_board`, etc. | Same + `On_attachToHull`, `On_detachFromHull`, `On_letGo` |

### **Hull State FixedUpdate Differences:**

#### **`st_idle_hull` (lines 302-311):**
```csharp
st_idle_hull.OnFixedUpdate = RefreshHullTarget;           // Track hull surface
st_idle_hull.OnFixedUpdate += OrientToSurfaceNormal;      // Align to surface
st_idle_hull.OnFixedUpdate += UpdateMovementOnVessel;     // Match vessel motion
st_idle_hull.OnFixedUpdate += UpdateHeading;              // Rotate kerbal
st_idle_hull.OnFixedUpdate += UpdatePackLinear;           // Jetpack (up/down only)
st_idle_hull.OnFixedUpdate += updateRagdollVelocities;    // Sync ragdoll
```

Compare to vanilla `st_idle_gr`:
- No `RefreshHullTarget` (uses ground collision)
- No `OrientToSurfaceNormal` (uses gravity vector)
- No `UpdateMovementOnVessel` (vessel is stationary)

---

## 7. Implementation Checklist

### **What You Already Have:**
- ✅ Custom hull FSM states with proper physics
- ✅ Golf animation support from hull states
- ✅ Science experiment module (`ModuleG3HullGolfScience`)
- ✅ Harmony patches for Dzhanibekov intercept
- ✅ Science data collection and results UI
- ✅ Dynamic FSM event routing
- ✅ Hull physics maintenance during golf
- ✅ Proper movement zeroing for science animations
- ✅ Configuration files (`.cfg` for parts and experiments)

### **What's Already Configured:**
- ✅ Module added to `kerbalEVA` parts via ModuleManager
- ✅ Experiment definition with proper situation masks
- ✅ UI button integration ("Perform EVA Science")
- ✅ IScienceDataContainer implementation
- ✅ Transmit/store/lab routing

---

## 8. Code Flow Diagram

```
User clicks "Play Hull Golf"
        ↓
ModuleG3HullGolfScience.DeployHullGolfExperiment()
        ↓
eva.PlayGolf(OnAnimationComplete)
        ↓
[FSM Event] On_Playing_Golf fires
        ↓
On_Playing_Golf_Hull_Hook()
    - Sets _golfStartedFromHull = true
    - Zeros movement (tgtRpos, tgtSpeed)
        ↓
[State Transition] st_idle_hull → st_playing_golf
        ↓
[FixedUpdate Loop] playing_golf_hull_OnFixedUpdate()
    - RefreshHullTarget()
    - OrientToSurfaceNormal()
    - UpdateMovementOnVessel()
        ↓
[Animation Completes]
        ↓
[FSM Event] On_Golf_Complete fires
        ↓
On_Golf_Complete_Hull_Redirect()
    - Changes GoToStateOnEvent to st_idle_hull
        ↓
[State Transition] st_playing_golf → st_idle_hull
        ↓
OnAnimationComplete() callback
    - Collects science data
    - Shows results dialog
        ↓
DONE
```

---

## 9. Key Technical Points

### **IsOnHull Property (line 203):**
```csharp
public bool IsOnHull => 
    this.enabled 
    && _hullTarget.IsValid() 
    && (FSM.CurrentState == st_idle_hull 
        || FSM.CurrentState == st_walk_hull 
        || FSM.CurrentState == st_jump_hull 
        || (FSM.CurrentState == Kerbal?.st_playing_golf && _golfStartedFromHull));
```

**Critical:** Golf state is considered "on hull" ONLY if `_golfStartedFromHull` is true. This prevents ground golf from being treated as hull golf.

### **Movement Zeroing (lines 605-610):**
```csharp
private void ZeroHullMovementForScience()
{
    tgtRpos = Vector3.zero;      // Target position (movement input)
    tgtSpeed = 0f;               // Target speed
    lastTgtSpeed = 0f;           // Previous speed
}
```

Prevents drift during science animations. Essential for maintaining position on a moving vessel.

---

## 10. Potential Enhancements

Your implementation is already production-ready, but here are optional improvements:

### **A. Multiple Hull Experiments**
Currently only golf is supported. You could add:
- Surface sample collection from hull
- Hammer test (seismic/structural)
- EVA report while on hull

**Implementation:**
1. Create new experiment IDs in `EXPERIMENT_DEFINITION`
2. Add new `ModuleG3Hull[Name]Science` modules
3. Use the same FSM hookup pattern

### **B. Situational Results**
Add more flavor text based on:
- Vessel type (station, ship, lander)
- Situation (orbit altitude, surface vs space)
- Body (Kerbin, Mun, etc.)

**Implementation:**
Expand `RESULTS` section in `.cfg`:
```cfg
RESULTS
{
    default = Generic golf result
    KerbinInSpaceLow = You chip a ball over Kerbin...
    MunInSpaceLow = Low gravity makes this too easy...
}
```

### **C. Animation Variants**
Different animations based on context:
- Low-G golf (slower swing)
- High-speed vessel (difficulty modifier)

**Implementation:**
Check vessel velocity in `OnAnimationComplete()`, modify science value or text.

---

## 11. Debugging Tips

### **Common Issues:**

**Issue:** Golf doesn't trigger from hull
- **Check:** `IsOnHull` returns true
- **Check:** `On_Playing_Golf` is added to hull states (line 437-438)
- **Check:** `_golfStartedFromHull` is set in hook (line 615)

**Issue:** Kerbal drifts during golf animation
- **Check:** `playing_golf_hull_OnFixedUpdate` is registered (line 448)
- **Check:** `ZeroHullMovementForScience()` is called (line 616)

**Issue:** Returns to wrong state after golf
- **Check:** `On_Golf_Complete_Hull_Redirect` is registered (line 452)
- **Check:** `_golfStartedFromHull` flag is properly managed

**Issue:** Dzhanibekov (wingnut) plays instead of golf
- **Check:** Harmony patch is applied (check logs for "EVAMagBoots")
- **Check:** `IsOnHull` returns true during trigger

---

## 12. Conclusion

Your implementation is **complete and well-architected**. The key innovations are:

1. **Dynamic FSM Event Routing** - `GoToStateOnEvent` is modified at runtime
2. **Physics Continuity** - Hull physics maintained during animations via `OnFixedUpdate` hooks
3. **State Awareness** - `_golfStartedFromHull` flag tracks animation origin
4. **Harmony Interception** - Blocks incompatible vanilla behaviors (Dzhanibekov)
5. **Movement Zeroing** - Prevents drift during science collection

The system properly accounts for:
- ✅ Being in space on a hull
- ✅ Magnetic attachment physics during animation
- ✅ Proper state transitions (hull → golf → hull)
- ✅ Science data collection and UI
- ✅ Preventing interference from vanilla systems

**No additional implementation is needed** - your golf experiment is fully functional for on-hull use in space!
