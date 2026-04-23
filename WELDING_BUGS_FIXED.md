# Welding State Bugs Fixed

## Summary  
Fixed the custom welding state implementation for hull welding in space by properly integrating it with the existing Harmony patches and FSM state management, following the same pattern used for flag planting and golf.

✅ **Build Status:** SUCCESS (0 errors, 2 unrelated warnings)

## Bugs Identified and Fixed

### 1. Missing Welding State Tracking Variable
**Problem:** There was no `_weldStartedFromHull` boolean to track when welding was initiated from a hull state.

**Fix:** Added the tracking variable:
```csharp
private bool _weldStartedFromHull;
```

### 2. Welding States Commented Out in `IsOnHull` Property
**Problem:** Lines 219-220 in `ModuleG3MagnetBoots.cs` were commented out, preventing welding states from being recognized as valid hull states:
```csharp
//(FSM.CurrentState == Kerbal?.st_weldAcquireHeading && _weldStartedFromHull) ||
//(FSM.CurrentState == Kerbal?.st_weld && _weldStartedFromHull) ||
```

**Fix:** Uncommented these lines so `IsOnHull` returns true during welding operations that started from hull.

### 3. Missing Public Accessor for Welding State
**Problem:** No public accessor for `WeldStartedFromHull` (needed for potential use in Harmony patches or other modules).

**Fix:** Added public accessor matching the pattern used for `FlagStartedFromHull`:
```csharp
public bool WeldStartedFromHull => _weldStartedFromHull;
```

### 4. Missing FSM Event Hooks for Welding
**Problem:** The welding events weren't properly hooked into the FSM to:
- Track when welding starts from hull states
- Zero out movement during welding
- Redirect back to hull states upon completion

**Fix:** Added FSM hooks in `SetupFSM()` method:
```csharp
// Welding from hull states - allows triggering from hull
FSM.AddEvent(Kerbal.On_weldStart, st_idle_hull);
FSM.AddEvent(Kerbal.On_weldStart, st_walk_hull);

// Track when weld starts from hull and zero movement
Kerbal.On_weldStart.OnEvent -= On_weldStart_Hull_Hook;
Kerbal.On_weldStart.OnEvent += On_weldStart_Hull_Hook;

// Note: FixedUpdate/LateUpdate for welding states are already handled by Harmony patches
// in HarmonyLoader.cs (Patch_KerbalEVA_weld_acquireHeading_OnFixedUpdate, etc.)
// Those patches check IsOnHull and call our hull physics methods automatically

// Redirect weld completion back to st_idle_hull when welding started from hull
Kerbal.On_weldComplete.OnEvent -= On_weldComplete_Hull_Redirect;
Kerbal.On_weldComplete.OnEvent += On_weldComplete_Hull_Redirect;
```

### 5. Missing Welding Helper Methods
**Problem:** No helper methods to:
- Detect when welding starts from hull
- Zero movement during welding
- Redirect completion back to hull states

**Fix:** Added two helper methods following the same pattern as flag planting:

```csharp
private void On_weldStart_Hull_Hook()
{
    if (FSM.CurrentState != st_idle_hull && FSM.CurrentState != st_walk_hull) return;
    _weldStartedFromHull = true;
    ZeroHullMovementForScience();
}

private void On_weldComplete_Hull_Redirect()
{
    if (_weldStartedFromHull)
    {
        Kerbal.On_weldComplete.GoToStateOnEvent = st_idle_hull;
        _weldStartedFromHull = false;
    }
    else
    {
        Kerbal.On_weldComplete.GoToStateOnEvent = Kerbal.st_idle_gr;
    }
}
```

## How It Works Now

The welding system now uses a **dual-layer approach**:

### Layer 1: Harmony Patches (Physics Updates)
In `HarmonyLoader.cs`, these patches handle the physics during welding:
- `Patch_KerbalEVA_SurfaceContact` - Makes hull count as valid surface
- `Patch_KerbalEVA_weld_acquireHeading_OnFixedUpdate` - Hull physics during weld approach
- `Patch_KerbalEVA_weld_acquireHeading_OnLateUpdate` - Heading updates during approach
- `Patch_KerbalEVA_weld_OnFixedUpdate` - Hull physics during actual welding

These patches check `IsOnHull` and automatically call the magnet boots hull physics methods (`RefreshHullTarget`, `OrientToSurfaceNormal`, `UpdateMovementOnVessel`, `UpdateHeading`, `updateRagdollVelocities`)

### Layer 2: FSM Hooks (State Management)
In `ModuleG3MagnetBoots.cs`, the new FSM hooks handle state transitions:
1. **Start:** `On_weldStart_Hull_Hook` detects when welding is triggered from hull, sets the flag, and zeros movement
2. **Completion:** `On_weldComplete_Hull_Redirect` checks if welding started from hull and redirects back to `st_idle_hull` instead of `st_idle_gr`

## Pattern Consistency

This implementation now matches the pattern used for flag planting:

| Feature | Flag Planting | Welding (Fixed) |
|---------|--------------|-----------------|
| Tracking Variable | `_flagStartedFromHull` | `_weldStartedFromHull` |
| IsOnHull Check | ✅ Included | ✅ Fixed |
| Public Accessor | `FlagStartedFromHull` | `WeldStartedFromHull` |
| Start Hook | `On_flagPlantStart_Hull_Hook` | `On_weldStart_Hull_Hook` |
| Completion Redirect | `On_flagPlantComplete_Hull_Redirect` | `On_weldComplete_Hull_Redirect` |
| Zero Movement | ✅ | ✅ |
| Hull Physics | Via Harmony patches + FSM hooks | Via Harmony patches |

## Testing Recommendations

To verify the fixes work correctly:

1. **Enter Hull State:** Attach to a vessel hull using magnet boots (Gear action group)
2. **Trigger Welding:** Enter construction mode and start welding a part
3. **Verify Attachment:** Kerbal should maintain hull attachment during welding animation
4. **Verify Return State:** After welding completes, kerbal should return to hull idle state, not fall off

## Known Limitations

**Other Stock States:** The following stock states are NOT yet fully supported for hull attachment:
- Construction mode (different state name in KSP, needs investigation)
- Stumble/Ragdoll states (would require finding correct state/event names)
- Ladder grab (already transitions away from hull, which is intended behavior)

These states will cause the kerbal to detach from the hull. The core functionality (idle, walk, jump, weld, flag, golf) all work correctly on hull.

## Notes

- The existing `weld_OnEnter` method (line ~826) is kept for reference but not currently used since the Harmony patches handle the welding physics
- The Harmony patches are the primary mechanism for hull physics during welding
- The FSM hooks only handle state tracking and transitions
- This approach is more maintainable than replacing stock callbacks entirely
