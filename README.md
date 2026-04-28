# G3 Magnet Boots

**by MisterBluSky** — a KSP 1 mod that lets Kerbals magnetically attach to vessel hulls while on EVA.

[![License: CC BY-NC-ND 4.0](https://img.shields.io/badge/License-CC%20BY--NC--ND%204.0-lightgrey.svg)](https://creativecommons.org/licenses/by-nc-nd/4.0/)

This work is licensed under <a href="https://creativecommons.org/licenses/by-nc-nd/4.0/">CC BY-NC-ND 4.0</a> <img src="https://mirrors.creativecommons.org/presskit/icons/cc.svg" alt="" style="max-width:1em;max-height:1em;margin-left:.2em;"><img src="https://mirrors.creativecommons.org/presskit/icons/by.svg" alt="" style="max-width:1em;max-height:1em;margin-left:.2em;"><img src="https://mirrors.creativecommons.org/presskit/icons/nc.svg" alt="" style="max-width:1em;max-height:1em;margin-left:.2em;"><img src="https://mirrors.creativecommons.org/presskit/icons/nd.svg" alt="" style="max-width:1em;max-height:1em;margin-left:.2em;">

---

## Overview

G3 Magnet Boots adds EVA magnetic boots to every Kerbal. When a Kerbal floats within range of a vessel's hull in microgravity, the boots automatically engage and snap the Kerbal to the surface. Once attached, the Kerbal can walk around the hull, stand idle with a physics-locking parking brake joint, enter EVA construction mode, perform hull welds, and even plant flags — all while the vessel tumbles, rotates, or accelerates beneath their feet.

The mod is built entirely with Harmony patches and KSP's existing FSM; no new animations or models are required.

---

## Features

### Magnetic Attachment
- Kerbals automatically attach to nearby vessel hull surfaces in microgravity (default: above 3 500 m altitude and terrain height).
- A multi-hit spherecast with origin-correction finds the best valid surface hit beneath the Kerbal's feet each physics step.
- Smooth surface normal tracking using an angle-adaptive exponential moving average keeps orientation stable over curved or edge geometry. A forward-cast anticipation system gently blends the normal toward the upcoming surface while walking, reducing jolts when rounding vessel edges.
- Velocity matching continuously corrects the Kerbal's velocity to track the hull's point velocity at the contact point, so the Kerbal stays planted even when the vessel rotates or translates.

### Parking Brake Joint
When the Kerbal stands still on a hull for a brief moment, a rigid `ConfigurableJoint` is created between the Kerbal and the hull rigidbody, locking all six degrees of freedom. This eliminates slow drift when idling. The joint breaks automatically on movement, jump, or manual detach. A configurable G-load limit can be set to make the joint breakable under high acceleration (see Settings).

### Walking & Jumping
- Full walking on curved hull surfaces with surface-relative directional controls.
- RCS pack thrust is restricted to the vertical axis while on hull to prevent accidental lateral flight.
- Jumping off the hull automatically enables the jetpack (configurable) to catch the Kerbal after a short delay, mimicking the stock ladder-jump behaviour.

### LOCKED Camera Mode Boom
When the flight camera is in `LOCKED` mode and a Kerbal is on a hull, a custom camera controller takes over:
- A camera boom arm is attached to the Kerbal with configurable pitch, heading, and distance driven by stock camera input.
- The boom world-frame is smoothed each frame using an angle-adaptive EMA — heavy damping for micro-jitter, fast response for real rotations, hard snap above 175°.
- Smooth cross-fade transitions (0.35 s) blend between the previous camera position and the new boom position when engaging or disengaging.
- Releases cleanly to stock camera when leaving the hull or switching modes.

### EVA Construction Mode on Hull
Kerbals can enter and exit EVA construction mode while magnetically attached without dismounting. The FSM is patched to redirect back to the hull-idle state on construction exit. Stock locomotion events are suppressed during the transition animations to prevent erratic movement.

### Hull Welding on Hull
Kerbals can initiate a weld operation directly from the hull. The attachment state is maintained for the full duration of the weld and the Kerbal returns to the hull after the weld completes.

### Flag Planting on Vessel Hulls *(experimental, opt-in)*
With the setting enabled, a right-click menu button appears on the Kerbal when on a hull with a flag available. The planted flag's physics joint is re-wired to connect to the hull rigidbody, making the flag move with the vessel. **Back up your save before using this feature.**

### Part Attachment Blacklist (`ModuleG3NoAttach`)
A lightweight part module prevents Kerbals from magnetically attaching to specific parts. A default blacklist ships with the mod covering solar panels, wheels, lights, antennas, Breaking Ground surface experiments, planted flags, external command seats, science containers, and EVA cargo items. Mod authors can add `ModuleG3NoAttach` to their own parts via Module Manager.

### Parachute Repacking
Kerbals can repack their EVA parachute while attached to a hull (configurable).

### Helmet Off in Vacuum *(opt-in)*
An optional safety bypass allows Kerbals to remove their helmets in vacuum — useful for roleplay or in-game photography.

### Kerbalism Compatibility
A bundled Module Manager patch adds an EC drain (~0.006 EC/s) to the boots when Kerbalism is installed.

---

## Requirements

| Dependency | Notes |
|---|---|
| **Kerbal Space Program 1.x** | Tested on KSP 1.12.x |
| **Module Manager** | Required for the KerbalEVA and blacklist patches |
| **0Harmony** | Bundled in `GameData/G3MagnetBoots/Plugins/` |

---

## Installation

1. Download the latest release.
2. Copy the `GameData/G3MagnetBoots` folder into your KSP `GameData` folder.
3. Ensure Module Manager is installed (not bundled).

Your `GameData` should contain:
```
GameData/
  G3MagnetBoots/
    Flags/
    Patches/
    Plugins/
      0Harmony.dll
      G3MagnetBoots.dll
      System.Core.dll
```

---

## Career / Science Mode

The boots require the **Advanced Exploration** tech node (`advExploration`) to function in Career and Science game modes. A hidden dummy part (`G3MagnetBoots_TechUnlock`) placed in that node gates the feature — research the node and the boots activate automatically on all future EVAs. In Sandbox the boots are always unlocked.

---

## Settings

All settings are found in the in-game **Difficulty Settings → G3MagnetBoots** section. Changes take effect immediately.

### Features tab

| Setting | Default | Description |
|---|---|---|
| Allow Walking on Asteroids and Comets | ✅ On | Enables attachment to asteroid and comet parts. |
| Allow LOCKED Camera Mode on Hull | ✅ On | Enables the custom boom camera when in LOCKED flight camera mode. |
| Require Microgravity | ❌ Off | When on, boots only work above 3 500 m altitude and terrain height, preventing interference with atmospheric flight and parachutes. |
| Auto-Enable Jetpack When Detaching | ✅ On | Automatically switches the jetpack on after jumping off a hull. |
| Allow Repacking EVA Parachutes on Hull | ✅ On | Lets Kerbals repack their parachute without dismounting. |
| Safely Allow Helmets Off in Vacuum | ❌ Off | Bypasses the stock helmet-safety check so Kerbals can remove helmets in vacuum. |
| WIP: Allow Planting Flags on Vessels | ❌ Off | Experimental. Allows flags to be planted on hull surfaces. Back up your save first. |
| Show Debug Info | ❌ Off | Enables verbose logging to the KSP log for debugging. |

### Constants tab

| Setting | Default | Range | Description |
|---|---|---|---|
| Magnet Strength Factor | 4 | 0 – 15 | Strength of the surface-snapping force applied each physics step. |
| Parking Brake Maximum G-Load | 0 | 0 – 11 | Maximum G-load before the parking brake joint breaks. **0 = unlimited** (joint never breaks from acceleration). |

---

## Controls

| Action | Input |
|---|---|
| Attach / Detach (toggle) | **G** (Action Group: Gear) |
| Walk on hull | **WASD** / standard movement keys |
| Jump off hull | **Space** |
| Camera pitch / heading / zoom | Standard camera keys / scroll (in LOCKED mode) |

---

## Known Issues & Limitations

- Multiple Kerbals on the same vessel may occasionally interfere with each other's hull targeting in edge cases.
- Heading orientation is bound to the camera up vector, which can feel awkward on surfaces that are not aligned with the vessel's main axis.
- Flag planting on hulls is experimental and may cause instability — always back up your save before enabling it.
- The mod does not yet add custom suit animations for walking or idle on hull; stock animations are reused.

---

## For Mod Authors

To prevent Kerbals from attaching to a specific part, add the following via Module Manager:

```cfg
@PART[yourPartName]:FINAL
{
    MODULE
    {
        name = ModuleG3NoAttach
    }
}
```

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for a full history of changes since v0.1.

---

## License

© MisterBluSky — [CC BY-NC-ND 4.0](https://creativecommons.org/licenses/by-nc-nd/4.0/)
You may share this mod with attribution. You may not modify or redistribute it for commercial purposes.
