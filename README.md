# V1 (ULTRAKILL) survivor for Risk of Rain 2: source code

A Risk of Rain 2 mod that turns V1 from ULTRAKILL into a playable survivor, with ULTRAKILL's movement, weapons,
arms, style meter and HUD reproduced as closely as possible. Values, timings and behavior were taken from the
decompiled ULTRAKILL code and its assets (see `docs/ULTRAKILL_values.md`), not tuned by feel, unless a comment says so.

This repository is shared with the author of the original ULTRAKILL V1 survivor mod
(IdontMakeMods-ULTRAKILL_V1_Survivor) so they can take whatever is useful: ideas, numbers, or whole pieces of code.
**You may use any part of this code in your mod.** No credit is required, but it is appreciated.

**If you are an AI assistant (Claude or similar) working on this code, read [`CLAUDE.md`](CLAUDE.md) first.**
It explains the layout, how everything is built, and where each feature lives.
To move a feature into the original mod, follow [`docs/PORTING.md`](docs/PORTING.md).

## What is included and what is not

Included: all C# source of the mod, the in-game autotest and logger plugins, the Unity editor script that builds the
asset bundle, the mod's own shaders, and small command-line tools used in the asset pipeline.

**Not included: any ULTRAKILL files** (models, textures, sounds, the decompiled code, the asset bundle built from
them). They belong to ULTRAKILL's developers. To build the mod you need your own copy of ULTRAKILL and to export its
assets yourself; [`docs/ASSETS.md`](docs/ASSETS.md) explains how.

## Features (all ported from ULTRAKILL)

| Area | What it does | Main files |
|---|---|---|
| Movement | Walk, air control, jump, 3 wall jumps, wall cling, dash with i-frames and 3 stamina bars, slide, ground slam with super jump, slam shockwave that knocks enemies up | `V1Mod/Survivors/V1/SkillStates/*` |
| Camera and HUD | First-person view with ULTRAKILL FOV, tilt and bob; the ULTRAKILL crosshair with health, grey "hard damage" health and stamina rings | `V1Camera.cs`, `V1Crosshair.cs`, `V1GreyHealth.cs` |
| Viewmodel | ULTRAKILL weapon and arm models drawn on top of the frame, with animations, barrel spin, heat glow and the weapon screens (charge bars, nail counters, coin and battery screens) | `V1ViewModel.cs` |
| Weapons | 5 slots x 3 variants: Piercer / Marksman (coins) / Sharpshooter; Core Eject / Pump Charge / Sawed-on shotguns; Attractor / Overheat / JumpStart nailguns and their sawblade-launcher forms; Electric / Screwdriver / Malicious railcannons; Freezeframe / S.R.S. / Firestarter rocket launchers | `V1Mod/Survivors/V1/Weapons/*` |
| Arms | Feedbacker (parry projectiles, coins, cores, shotgun pellets), Knuckleblaster (blast wave), Whiplash hook (pulls light enemies, pulls you to heavy ones) | `Weapons/Arms.cs`, `Weapons/ShotgunParry.cs` |
| Explosions | Growing/fading explosion spheres, shockwave rings, chunks, self-damage and rocket/core jumps, parry freeze-frames | `Weapons/V1Effects.cs`, `Weapons/SelfBlast.cs`, `Modules/V1HitStop.cs` |
| Style | Style points, ranks D..ULTRAKILL with the ULTRAKILL meter and drain, bonus feed, weapon freshness | `V1Style.cs` |
| Shop and start | Each stage starts in the ULTRAKILL first room: you fall down a shaft, buy weapons at the SmileOS terminal with style points, walk out and drop onto the map | `V1StartRoom.cs`, `Weapons/V1Shop.cs`, `SkillStates/V1SpawnFall.cs` |
| Sounds | ULTRAKILL sounds played through NAudio (RoR2 disables Unity audio) | `Modules/V1Sounds.cs` |

Screenshots are in `docs/screenshots/`.

## Building (short version)

1. Install Risk of Rain 2 with BepInEx and R2API through r2modman (Core, Prefab, Language, ContentManagement, DamageType).
2. Edit `GameDir` and `R2ProfileDir` at the top of `V1Mod/V1Mod.csproj` (or pass `-p:GameDir=... -p:R2ProfileDir=...`).
3. Export ULTRAKILL assets and build the asset bundle with Unity 2021.3 (see `docs/ASSETS.md`).
4. `dotnet build -c Release` in `V1Mod`. The build copies the dll, NAudio, the bundle and the sounds into
   `<profile>/BepInEx/plugins/Oleksandr-V1Mod`.

Details for every step are in `CLAUDE.md` and `docs/ASSETS.md`.

## Credits

ULTRAKILL is made by Arsi "Hakita" Patala and published by New Blood Interactive; all ULTRAKILL assets and designs
are theirs. Mod code by YaOlexandr (GitHub: Rand0mNme), written with the help of Claude (an AI assistant).
