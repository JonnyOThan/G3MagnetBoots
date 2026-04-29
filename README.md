# G3MagnetBoots

**by MisterBluSky** — a KSP1 mod that lets Kerbals magnetically attach to vessel hulls and do work while on EVA.

[![License: CC BY-NC-ND 4.0](https://img.shields.io/badge/License-CC%20BY--NC--ND%204.0-lightgrey.svg)](https://creativecommons.org/licenses/by-nc-nd/4.0/)

This work is licensed under <a href="https://creativecommons.org/licenses/by-nc-nd/4.0/">CC BY-NC-ND 4.0</a> <img src="https://mirrors.creativecommons.org/presskit/icons/cc.svg" alt="" style="max-width:1em;max-height:1em;margin-left:.2em;"><img src="https://mirrors.creativecommons.org/presskit/icons/by.svg" alt="" style="max-width:1em;max-height:1em;margin-left:.2em;"><img src="https://mirrors.creativecommons.org/presskit/icons/nc.svg" alt="" style="max-width:1em;max-height:1em;margin-left:.2em;"><img src="https://mirrors.creativecommons.org/presskit/icons/nd.svg" alt="" style="max-width:1em;max-height:1em;margin-left:.2em;">

---

## Overview

G3 Magnet Boots adds EVA magnetic boots to every Kerbal. When a Kerbal floats within range of a vessel's collider, the boots automatically engage and snap the Kerbal to the surface. Once attached, the Kerbal can walk around the hull, stand idle with a physics-locking parking brake joint, enter EVA construction mode, weld, and even plant flags all while the vessel tumbles, rotates, or accelerates beneath them.

The mod is built entirely with Harmony patches and KSP's existing FSM; no new animations or models are required.

---

## Features

### Magnetic Attachment
- Kerbals automatically attach to nearby vessel hull surfaces.
- Magnet boots now work in atmosphere and on planets/moons, this can be configured to only allow on micro-gravity (meaning above terrain by >3500m).
- A multi-hit spherecast finds the best valid surface hit beneath the Kerbal's feet each physics step.
- Smooth surface normal tracking using angle-adaptive smoothing keeps orientation stable over curves or edges. A future-cast system gently blends the normal toward upcoming surfaces while walking, reducing jolts when rounding vessel edges.
- Velocity matching continuously corrects the Kerbal's velocity to track the hull's point velocity at the contact point, so the Kerbal stays planted even when the vessel rotates or translates.

### Parking Brake Hull Anchor Joint
When the Kerbal stands still on a hull, after a brief moment a rigid `ConfigurableJoint` is created between the Kerbal and the hull rigidbody, locking them in place relative. This eliminates slow drift, improves phantom physics forces, and allows more extreme manuevers without flying off. The joint breaks automatically on movement, jump, or manual detach. G-load limit can be set so it breaks under high acceleration, but default it is unbreakable. Kerbals who go unconcious due to over-Gee will detach no matter the setting.

### Walking & Jumping
- Full walking on arbitrary vessel colliders with surface-relative controls.
- RCS pack thrust is restricted to the kerbals up-axis while on hull to allow jetting away or pushing downwards.
- QoL: Jumping off the hull automatically enables the jetpack (optional) to catch you after a short delay.

### LOCKED EVA Camera Mode w/ Stabilized Boom
Use the new camera mode when attached to a hull. Flight camera `LOCKED` mode uses a custom camera controller:
- A simulated camera boom arm is attached to the Kerbal's body with stock camera controls.
- Camera stabilized reference-frame is smoothed each frame using an angle-adaptive EMA — heavy damping for Kraken jitter, snaps above 175°.
- Smooth cross-fade transitions to blend between previous camera position and boom position when to hopefully feel stock-alike.
- Releases to stock camera modes when leaving the hull or switching modes.

### EVA Construction Mode on Hull
Kerbals can use now EVA construction mode while attached. KerbalFSM is patched to redirect back to the hull-idle state on construction exit. Stock movement is suppressed during the animation to prevent weird animation issues.

### Hull Welding on Hull
Kerbals now enter the welding animation directly when on a hull and return to idle safely after the weld completes.

### Flag Planting on Vessel Hulls *(experimental, opt-in)*
With the setting enabled, a right-click menu button appears on the Kerbal when on a hull with a flag available. The planted flag's physics joint is re-wired to connect to the hull rigidbody, making the flag move with the vessel. **Back up your save before using this feature.**

### Part Attachment Blacklist (`ModuleG3NoAttach`)
A lightweight part module prevents Kerbals from magnetically attaching to specific parts. A default blacklist ships with the mod covering solar panels, wheels, lights, antennas, Breaking Ground surface experiments, planted flags, external command seats, science containers, and EVA cargo items. Mod authors can add `ModuleG3NoAttach` to their own parts via Module Manager.

### Parachute Repacking
Kerbals can repack their EVA parachute while attached to a hull (configurable).

### Helmet Off in Vacuum *(opt-in)*
An optional safety bypass allows Kerbals to remove their helmets in vacuum — useful for roleplay or in-game photography.

### Kerbalism Compatibility
A bundled Module Manager patch adds an EC drain (~0.006 EC/s) to the boots while attached with Kerbalism is installed.

---

## Requirements

Dependencies
**Module Manager**
**0Harmony** 

---

## Installation

1. Download the latest release.
2. Copy the `GameData/G3MagnetBoots` folder into your KSP `GameData` folder.
3. Ensure Module Manager is installed).

Your `GameData` should contain:
```
GameData/
  G3MagnetBoots/
    Flags/
    Patches/
    Plugins/
      G3MagnetBoots.dll
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
| Allow Walking on Asteroids and Comets | ✅ On | Enables attachment to asteroids and comets. |
| Allow LOCKED Camera Mode on Hull | ✅ On | Enables the custom boom camera when in LOCKED flight camera mode. |
| Require Microgravity | ❌ Off | When on, boots only work above 3 500 m altitude and terrain height, preventing interference with atmospheric flight and parachutes. |
| Auto-Enable Jetpack When Detaching | ✅ On | Automatically switches the jetpack on after jumping off a hull. |
| Allow Repacking EVA Parachutes on Hull | ✅ On | Lets Kerbals repack their parachute in orbit while on hulls. |
| Safely Allow Helmets Off in Vacuum | ❌ Off | Bypasses the stock helmet-safety check so Kerbals can remove helmets in vacuum. |
| WIP: Allow Planting Flags on Vessels | ❌ Off | WIP! Allows flags to be planted on hull surfaces. BACKUP SAVE FIRST. |
| Show Debug Info | ❌ Off | Enables logging for debugging. |

### Constants tab

| Setting | Default | Range | Description |
|---|---|---|---|
| Magnet Strength Factor | 4 | 0 – 15 | Strength of the surface-snapping force applied each physics step. Higher can allow stronger attachment especially in gravity, but risks higher physics forces|
| Parking Brake Maximum G-Load | 0 | 0 – 11 | Maximum G-load before the parking brake joint breaks. **0 = unlimited** (joint never breaks from acceleration, only from Kerbal over-Gee going unconcious). |

---

## Controls

| Action | Input |
|---|---|
| Toggle Magnet Boots (toggle) | **G** (Action Group: Gear) |
| Walking on hull | standard movement keys |
| Jumping off hull | **Space** to push off or **Shift+Space** to jump harder|
| "LOCKED" Mode: Camera pitch / heading / zoom | Standard camera keys / scroll (in LOCKED mode) |

---

## Known Issues & Limitations

- Multiple Kerbals on the same vessel might cause weird phantom forces unless anchored.
- Flag planting on hulls is highly experimental and WILL cause instability — back up your save before having fun with it!
- For now, doing anything on the hull except Idling will detach the anchor, even if the animation has no effect on the kerbals position.
- No inventory part is needed but this is planned, just need to learn modeling first...

---

## For Mod Authors


You can prevent Kerbals from attaching to your parts by adding the following patch via Module Manager:

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
