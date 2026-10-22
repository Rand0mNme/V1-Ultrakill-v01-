# Guide for AI assistants working on this repository

This file is written for an AI coding assistant (Claude Code or similar) that has been handed this repository,
most likely by the author of another ULTRAKILL V1 survivor mod for Risk of Rain 2 who wants to borrow features.
Read it before changing or porting anything.

## What this is

A BepInEx plugin for Risk of Rain 2 (game version 1.5.0, Unity 2021.3, Mono) that adds V1 from ULTRAKILL as a
survivor. The goal of the project was to be indistinguishable from ULTRAKILL: movement, weapons, arms, HUD, sounds
and effects are ported from the decompiled ULTRAKILL code with the same numbers. Comments name the ULTRAKILL class
and field each piece comes from (for example `NewMovement.TryDash`, `Shotgun.UpdateMeter`, `WeaponCharges.rev1charge`),
so you can look the original up in your own decompile of `ULTRAKILL_Data/Managed/Assembly-CSharp.dll`.

The repository contains no ULTRAKILL files. The mod needs an asset bundle (`v1assets`) and `.wav` sounds built from
the user's own ULTRAKILL copy; see `docs/ASSETS.md`. Without them the code compiles, but the survivor has no models,
effects or sounds.

## Repository layout

```
V1Mod/                     the mod itself (netstandard2.1, C# 7.3)
  V1Plugin.cs              BepInEx entry: config, sounds, asset bundle, then V1Survivor.Init
  Log.cs                   logging helper
  Modules/
    V1Assets.cs            loads the v1assets bundle; every prefab/sprite the mod uses is a static field here
    V1Sounds.cs            NAudio mixer: loads Sounds/*.wav, plays one-shots and loops (RoR2 has Unity audio off)
    V1Config.cs            BepInEx config (weapon keys, shop, blood healing); hot-reloads when the file changes
    V1HitStop.cs           ULTRAKILL hit stop / freeze frames (TimeController.HitStop, single player only)
  Survivors/V1/
    V1Survivor.cs          the body: a clone of CommandoBody with ULTRAKILL stats, gravity, no drop pod, extra "Hook" state machine
    V1Skills.cs            skill defs, the loadout row (full arsenal vs revolver only), alternate "RoR2 scale" movement
    V1ViewModel.cs         first-person weapons and arms, drawn with a command buffer on top of the frame; weapon screens
    V1Camera.cs            first-person camera (ULTRAKILL CameraController: FOV, tilt, bob, zoom)
    V1Crosshair.cs         ULTRAKILL crosshair with health, grey health and stamina rings
    V1GreyHealth.cs        ULTRAKILL hard damage (antiHp) and blood healing
    V1Style.cs             style points, ranks, bonus feed, weapon freshness (StyleHUD)
    V1StartRoom.cs         ULTRAKILL first room above each stage spawn, door, exit drop
    SkillStates/           movement states: V1CharacterMain (main), Dash, Slide, Slam, SlideSlam (Ctrl router),
                           V1SpawnFall (spawn drop), V1MovementData (state shared between states),
                           V1MovementAudio / V1MovementFx (movement sounds and particles)
    Weapons/               V1Arsenal (weapon slots and switching), V1Shop (SmileOS terminal), one file per weapon family,
                           Arms (Feedbacker/Knuckleblaster/Whiplash), ShotgunParry, SelfBlast (explosion self-damage
                           and launches), V1Effects (beams, sparks, explosion animation), V1Damage (networked damage helper)
  Assets/Sounds/           put the converted .wav files here (not in the repo)
V1Test/                    autotest plugin: drives the game from a command file (screenshots, input, teleport...)
V1Diag/                    logger plugin: records what a character does (states, skills, bullets, blasts, damage)
V1Unity/                   Unity 2021.3.33f1 project that builds the v1assets bundle
  Assets/Editor/BuildV1.cs list of ULTRAKILL prefabs to include, material/shader conversion, bundle build
  Assets/V1/Shaders/       the mod's replacement shaders (ULTRAKILL's own shaders are not in an AssetRipper export)
  Assets/V1/shadermap.txt  material file name -> original ULTRAKILL shader name (drives the conversion)
tools/                     UkDeps (copy an exported asset with its dependencies), UkScene (inspect/extract parts of
                           exported scenes and prefabs), OggToWav (sound conversion), v1test.sh, addshadermap.sh
docs/ULTRAKILL_values.md   numbers extracted from ULTRAKILL (movement, weapons, coins, arms)
docs/ASSETS.md             how to rebuild the asset bundle and sounds from your own ULTRAKILL copy
docs/PORTING.md            step-by-step porting into the original V1 mod: per feature, files + ULTRAKILL assets + sounds
```

## How the main systems fit together

- **Units.** All movement math is in ULTRAKILL units (1 Unity unit = 1 m, physics tick 0.008 s, gravity -40).
  `V1MovementData.scale` multiplies speeds and gravity: 1 is pure ULTRAKILL, 0.52 is the alternate "RoR2 scale" Shift.
- **Movement.** `V1CharacterMain` replaces RoR2's main state. It sets the KinematicCharacterController velocity itself
  (the RoR2 motor acceleration is 0) and ports `NewMovement.Move/Jump/WallJump/Cling`. Dash is on Shift; slide and
  ground slam are on the sprint button (Ctrl), read through `inputBank.rawSprintInput` because RoR2 sprint is a toggle.
  RoR2 only starts skills from the main state, so Dash/Slide/Slam forward skill presses with
  `V1MovementData.PerformSkillInputs`.
- **Weapons.** `V1Arsenal` owns the current weapon and swaps the primary/secondary `SkillDef`s with RoR2 skill
  overrides. Each weapon's charges live in a small component on the body (`NailgunData`, `RailData`, `RocketData`...)
  so they recharge while holstered, like `WeaponCharges`. Weapon ids are constants in `V1ViewModel` (`WeaponPiercer`,
  `WeaponShotgun`, ...). Equip delays come from the ULTRAKILL draw animations (a Harmony postfix on
  `GenericSkill.CanExecute`).
- **Damage.** 1 ULTRAKILL damage = 275% of the character's damage (`Piercer.DamagePerUnit`). Hitscan goes through RoR2
  `BulletAttack`; projectile hits go through `V1Damage.Deal`, which applies damage on the host or fires a tiny targeted
  bullet from a client so it works in multiplayer. Every hit is tagged with a RoR2 `DamageSource` (LMB Primary,
  RMB Secondary, slam Utility, arms Special).
- **Viewmodel.** ULTRAKILL draws weapons with a second camera; RoR2 cannot, so `V1ViewModel` renders the weapon layer
  with a command buffer after post-processing and stretches it to match ULTRAKILL's 90 degree HUD FOV. The weapon
  screens (shotgun charge bar, nailgun counters, Marksman coins, Sharpshooter bars) are drawn into small textures.
- **Explosions.** `V1ExplosionAnim` reproduces `Explosion.cs` (sphere growth per tick, fade after maxSize, white first
  0.1 s), `V1SpriteFade` reproduces `ScaleNFade` for the glow and ground rings, `SelfBlast` reproduces the player launch
  and rocket-jump rules.
- **Style and shop.** Style points (`V1Style`) are the shop currency. The SmileOS terminal (`V1Shop`) sells weapon
  variants for points; purchases are stored on the `CharacterMaster`, buying is done on the server.
- **Spawn.** `V1StartRoom` builds the ULTRAKILL first room 1000 m above the spawn point (local to each player);
  leaving through the door runs `V1SpawnFall`, which drops V1 onto the map from up to 200 m.

## Building

Requirements: Windows, .NET SDK 8 (the projects target netstandard2.1), Risk of Rain 2 with an r2modman profile
containing BepInEx 5 and R2API (Core, Prefab, Language, ContentManagement, DamageType). References point straight at
the game's `Managed` folder and the profile, because nuget.bepinex.dev doesn't serve the packages.

1. Set the paths: `GameDir` and `R2ProfileDir` at the top of `V1Mod/V1Mod.csproj` (same in `V1Test` and `V1Diag`),
   or pass them on the command line: `dotnet build -c Release -p:GameDir="C:\...\Risk of Rain 2" -p:R2ProfileDir="C:\...\profiles\MyProfile"`.
2. Build the asset bundle and sounds with `tools/build_assets.sh` (run `bash tools/build_assets.sh --help`; it needs the
   ULTRAKILL folder, AssetRipper 2.x and Unity 2021.3.x, and does every step in `docs/ASSETS.md` automatically).
   It was verified on a fresh clone: it reproduces the original bundle exactly (same asset list, same build log) and the
   mod built from it runs. The mod build picks up `V1Unity/AssetBundles/v1assets` and `V1Mod/Assets/Sounds/*.wav`.
3. `cd V1Mod && dotnet build -c Release`. Output goes to `Build/plugins` and to
   `<profile>/BepInEx/plugins/Oleksandr-V1Mod` (dll, NAudio dlls, `v1assets`, `Sounds/`). The copy fails while the
   game is running, because the game locks the files.
4. Launch RoR2 through r2modman (or with doorstop: `"Risk of Rain 2.exe" --doorstop-enabled true --doorstop-target-assembly "<profile>\BepInEx\core\BepInEx.Preloader.dll"`).
   The log is `<profile>/BepInEx/LogOutput.log`; the mod logs a line listing which bundle assets were found.

## Testing in game without a human

`V1Test` is a separate plugin that only activates when the file `<profile>/BepInEx/V1Test/enabled` exists.
`tools/v1test.sh` creates it, launches the game, waits for a stage, and deletes the switch so normal launches are not
affected (never leave that file behind: it makes the user's own launch run the autotest). Commands are written one per
line to `<profile>/BepInEx/V1Test/cmd.txt` (the file is renamed to `cmd.done` when consumed); the full command list is
at the top of `V1Test/V1TestPlugin.cs` (wait, shot, camshot, move, look, hold/tap skill1..4/jump/sprint, weapon N,
tp, god, money, style, explode, console, ...). State is written to `status.txt`, screenshots to `shots/`.
Paths in `v1test.sh` are for the original machine; change them first.

## Porting a feature into another mod

Start with `docs/PORTING.md`: it lists every feature with its files and the exact ULTRAKILL prefabs and sound clips it needs.


- Start from the feature table in `README.md`, then read the file's header comment: it lists the ULTRAKILL source
  classes and the exact numbers.
- Most weapon files depend on `V1ViewModel` (models and screens), `V1Assets` (prefab names in the bundle),
  `V1Sounds` (sound names = wav file names, see `docs/ASSETS.md`) and `V1Damage`/`Piercer.DamagePerUnit`. When porting,
  replace those with the target mod's equivalents rather than copying the whole framework.
- Movement depends on V1's main state replacing RoR2's (`V1CharacterMain`); in a mod that keeps RoR2 movement, port
  the numbers from `docs/ULTRAKILL_values.md` instead.
- Self-contained pieces that port easily: `V1HitStop`, `V1GreyHealth`, `V1Style` (needs a HUD parent),
  `SelfBlast`, `Coin` reflection logic, `RevolverAlts` (Sharpshooter wall-normal smoothing for RoR2's uneven rocks),
  `V1SpawnFall.FindDropPoint`.

## Conventions

- Code comments and logs are in English; each class starts with a comment describing the ULTRAKILL behavior it ports.
- Numbers that were tuned for RoR2 rather than copied are marked in comments ("tuned by eye", "as in the other V1 mod").
- Multiplayer: damage, launches of other bodies and purchases run on the server; the start room, shop terminal and
  viewmodel are local only. Hit stop is single player only.
- Do not commit ULTRAKILL files (exports, bundles, sounds, decompiled code). `.gitignore` excludes the usual places.
