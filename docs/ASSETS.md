# Rebuilding the ULTRAKILL assets

The mod loads two things that come from ULTRAKILL and are therefore not in this repository:

1. `v1assets`: a Unity asset bundle with the weapon and arm models, projectiles, effects, the first room, the shop
   booth, the crosshair, style rank sprites and weapon icons.
2. `Sounds/*.wav`: ULTRAKILL sound clips converted to 16-bit 44.1 kHz stereo WAV.

Both have to be made from your own ULTRAKILL install. **`tools/build_assets.sh` does all of it in one go**
(AssetRipper export, asset copy, scene cut-outs, sound conversion with `tools/sounds.tsv`, Unity build):

```
bash tools/build_assets.sh --ultrakill "D:/SteamLibrary/steamapps/common/ULTRAKILL" \
  --assetripper "C:/Tools/AssetRipper/AssetRipper.GUI.Free.exe" \
  --unity "C:/Program Files/Unity/Hub/Editor/2021.3.33f1/Editor/Unity.exe"
```

The sections below explain what the script does, for when something needs to be changed or fixed by hand.

## 1. Export ULTRAKILL with AssetRipper

Export the game with AssetRipper (2.x) as a Unity project ("Export All Files" / project export). You get a folder like
`ULTRAKILL_export/ExportedProject/Assets` with prefabs, materials, textures, meshes, animations and `.ogg` audio.
Note: ULTRAKILL's custom shaders are not exported in a usable form, which is why `BuildV1.cs` replaces them.

## 2. Copy the needed assets into the Unity project

The Unity project is `V1Unity` (Unity **2021.3.33f1**; RoR2 runs 2021.3.45f2, bundles built with 2021.3.x load fine).
It needs the ProBuilder 5.2.2 package (already in `Packages/manifest.json`) because the first room's walls are
ProBuilder meshes.

`tools/UkDeps` copies an exported asset together with everything it references (meshes, materials, textures,
animations; not scripts, audio or nested effect prefabs) into `V1Unity/Assets/UK`:

```
dotnet run --project tools/UkDeps -- "<export>/ExportedProject/Assets" "V1Unity/Assets/UK" "<asset path relative to Assets>" ...
```

The assets `BuildV1.cs` expects in `Assets/UK` (same file names as in the export):

- Weapons: `Shotgun Grenade`, `Shotgun Pump`, `Shotgun Saw`, `Nailgun Magnet`, `Nailgun Overheat`, `Nailgun Zapper`,
  `Sawblade Launcher Magnet/Overheat/Zapper`, `Railcannon Electric`, `Railcannon Harpoon`, `Railcannon Malicious`,
  `Rocket Launcher Freeze/Cannonball/Napalm`, `Revolver Pierce`, `Revolver Ricochet`, `Revolver Twirl`, `Arm Blue_0`, `Arm Red`.
- Projectiles and effects: `Revolver Beam`, `Revolver Beam Super`, `Revolver Beam Sharp`, `LaserHitParticle`,
  `SuperLaserHitParticle`, `Shotgun Projectile`, `ShotgunMuzzleFlash`, `Grenade`, `Explosion`, `ExplosionChunk`,
  `Explosion Malicious Railcannon`, `Explosion Wave Knuckleblaster`, `Nail_0`, `NailHeated`, `NailAlt`,
  `NailAltHeated`, `NailAltFodder`, `NailgunMuzzleFlash`, `NailgunMuzzleFlash 3`, `NailgunMuzzleFlash 4`,
  `Harpoon_0`, `Harpoon Malicious`, `Zapper`, `Railcannon Beam`, `Railcannon Beam Malicious`, `Rocket_0`,
  `RocketLauncherFire`, `Cannonball_0`, `GasolineProjectile`, `ChainsawProjectile`, `SparksSawblade`, `Coin_0`,
  `CoinFlash`, `CoinCharge`, `PhysicalShockwavePlayer`.
- Movement particles: `DodgeParticle`, `SlideParticle`, `SlideScrape`, `WallScrape`, `FallParticle`, `DustBig`.
- Shop and UI: `Wrapped Shop`, `fs-tahoma-8px.ttf`, `circlethick`, `meter`, `RankD/C/B/A/S/SS/SSS/U` sprites,
  `batteryfull/mid/low/charging1-3.png`, and the weapon icons copied as `Assets/V1/UI/Icons/V1Icon_<name>.png`
  (`BuildV1.MakeWhiteIcons` turns them white).

All `.prefab` entries above are listed with their output names in the `Prefabs` table at the top of
`V1Unity/Assets/Editor/BuildV1.cs`; that table is the authoritative list.

Three pieces are not standalone prefabs in the export and were cut out of scenes/prefabs with `tools/UkScene`
(`UkScene <file> <guidmap.txt> extract <rootTransformId> <out.prefab>`; `show`/`refs`/`tree` help find the ids):

- `Assets/UK/V1FirstRoomRoom.prefab`: the "Room" branch of `FirstRoom.prefab`, root transform id `4145039172186485`
  (keeps ProBuilder meshes, colliders, lights).
- `Assets/UK/_V1WhiplashArm.prefab`: the Whiplash arm model from `FirstRoom Encore.prefab`, root id `4077724941760488`.
- `Assets/V1/UI/UKCrosshair.prefab`: `Player/Canvas/Crosshair Filler/Crosshair` from scene
  `005a4f2ce549277458596ee0f0d6e88c.unity`, root RectTransform id `24705`.

These ids are from the October 2026 ULTRAKILL build. If a future build changes them, `build_assets.sh` stops with a
message; find the new ids with `UkScene <file> <guidmap.txt> tree <id>` / `show` and update the `extract` lines.

`guidmap.txt` (guid -> file path for the export) is written by `UkDeps` next to its binary.

## 3. Shader map and bundle build

ULTRAKILL materials use ULTRAKILL shaders that don't exist in RoR2. `BuildV1.ConvertMaterial` swaps each one for a
shader from `Assets/V1/Shaders`, choosing by the original shader name stored in `Assets/V1/shadermap.txt`
(`material file name<TAB>original shader name`). After copying new materials, run `tools/addshadermap.sh` (edit its
paths) to append their entries.

Build the bundle:

```
Unity.exe -batchmode -quit -projectPath V1Unity -executeMethod BuildV1.Build -logFile build.log
```

It prepares `Assets/V1/V1*.prefab` (strips ULTRAKILL scripts, audio and physics, moves everything to layer 0, bakes
ProBuilder meshes, fixes animators) and writes `V1Unity/AssetBundles/v1assets`. The mod build copies it to the plugin
folder.

## 4. Sounds

Copy the `.ogg` clips from the export into a folder, rename them to the mod's names below, and convert:

```
dotnet run --project tools/OggToWav -- <folder with .ogg> V1Mod/Assets/Sounds
```

`V1Sounds.Play("Name", ...)` plays `Sounds/Name.wav`. Mod name -> ULTRAKILL clip name (from the export):

| Mod sound | ULTRAKILL clip |
|---|---|
| BlastWave | Explosion Wave |
| CannonCharge | MachinePumpLoop |
| ChainsawLoop | ChainsawLoop |
| CoinFlip | coinflip |
| CoinSpin | coinspin |
| CoreEject | Steampunk Weapons - Shotgun 2 - Shot - 05 |
| Drill | Drill |
| Explosion | Explosion 1 |
| FreezeStart | ClockStart |
| FreezeStop | ClockStop |
| FreezeTick | ClockTickTock |
| HookArmClink | Whiplash Clink |
| HookArmHit | harpoonPierce |
| HookArmPull | Whiplash Pull Start |
| HookArmThrow | Whiplash Throw Start |
| MagnetShot | harpoonShoot |
| MaliciousExplosion | Explosion 1 |
| NailRicochet | Ricochet |
| NailShot | MachineGun3B |
| NailSpin | MachineGunSpin |
| NapalmLoop | RocketSprayLoop |
| NapalmStart | RocketSprayStart |
| NapalmStop | RocketSprayStop |
| Parry | punch_projectile |
| Pump1 | shotgunpump1 |
| Pump2 | shotgunpump2 |
| PumpCharge | pumpCharge |
| PunchHeavy | PunchSwooshHeavy |
| PunchHit | punch_grit_wet_impact_05 |
| PunchHitHeavy | punch_heavy |
| PunchSwoosh | PunchSwoosh |
| RailFire | RailcannonFire4 |
| RailFullCharge | RailcannonFullClickAndCharge |
| RailMaliciousFire | Bluezone-Autobots-shutdown-003 |
| RocketFire | RocketFire5 |
| RocketLoop | RocketLoop |
| SawHit, SawImpact | Impacts_SIMPLE_003 |
| SawLaunch | PlasmaFire |
| SawRicochet | Ricochet |
| SawShot | Steampunk Weapons - Shotgun 2 - Shot - 03 |
| SawThrownLoop | chainsawThrown |
| ShopDrawerOpen | ShopDrawerOpen |
| ShotgunShot | Steampunk Weapons - Shotgun 2 - Shot - 03 |
| ShotgunCharge | electricCharge |
| SmileOSStartup | smileOS2Startup |
| SuperSaw | harpoonShoot |
| TwirlShot | TwirlShot |
| Twirling | Twirling |
| WarningBeep | CSFX-2_Mechanics_45 |
| ZapperLoop | ElectricitySurgeContinuous |
| ZapperWarning | ZapperWarning |
| BeepBeep_high, Charging, Dodge3, Landing, LandingHeavy, PierceCharge, Shoot1c4, Shoot2b3, Whoosh | same name |
| Jump, DashJump, StaminaFail, FinalWallJump, SlideStop, Scrape, Footstep1-4 | the clips referenced by `NewMovement` and `PlayerFootsteps` on the player prefab (jumpSound, dashJumpSound, staminaFailSound, finalWallJump, slideStopSound, scrape, footsteps); check the player prefab in your export |

A missing sound only logs a warning; the mod still runs.
