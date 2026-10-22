# Porting features into the original V1 survivor mod

This is a recipe for an AI assistant working in the **original** ULTRAKILL V1 survivor mod
(IdontMakeMods-ULTRAKILL_V1_Survivor, `UltrakillV1.dll`). That mod builds its asset bundle on the player's PC from
their ULTRAKILL install (its "first-time setup"), while this repository builds `v1assets` with Unity on the
developer's PC. So **code ports, assets don't**: for every feature you take, you add the ULTRAKILL assets it needs
to the original mod's setup, then point the ported code at the original mod's asset loader, sound player and
damage helpers.

## General recipe for one feature

1. Find the feature in the table below. Read the header comment of each listed file: it names the ULTRAKILL classes
   the code reproduces and all the numbers.
2. Add the listed ULTRAKILL assets to the original mod's setup. The names below are ULTRAKILL's own prefab and
   audio clip names (as they appear in an AssetRipper export; the Addressables/bundle names inside the game may
   differ slightly). Find where the setup lists the ULTRAKILL assets it extracts and add these.
3. Copy the code. Replace this mod's framework calls with the original mod's equivalents:
   - `V1Assets.<field>` -> the original mod's loaded prefab (the table gives the ULTRAKILL source of each field);
   - `V1Sounds.Play("Name", ...)` / loops -> the original mod's audio mixer (the table gives the ULTRAKILL clip);
   - `V1ViewModel` (weapon models, screens, weapon ids) -> the original mod's viewmodel/weapon system;
   - `V1Damage.Deal`, `Piercer.DamagePerUnit` (1 ULTRAKILL damage = 275%) -> the original mod's damage helper and scale;
   - `V1Arsenal` / `V1Shop.Owns` (weapon slots and unlocks) -> the original mod's weapon selection and boss drops;
   - `V1Style.Add(...)` calls -> remove them, or keep them if you also port the style meter.
4. Materials: ULTRAKILL shaders don't exist in RoR2. The original mod already converts them; if a ported effect looks
   wrong, compare with `V1Unity/Assets/V1/Shaders` and `BuildV1.ConvertMaterial` (for example the explosion sphere is
   additive `Blend SrcAlpha One` with `Cull Off`, like ULTRAKILL's Explosion material).
5. Build, run, compare with ULTRAKILL side by side.

## Features the original mod (0.2.0) doesn't have yet

| Feature | Files in this repo | ULTRAKILL prefabs / assets | ULTRAKILL sounds |
|---|---|---|---|
| Sharpshooter revolver (spin-up, bouncing beam, wall-normal smoothing for RoR2 rocks) | `Weapons/RevolverAlts.cs` (V1SharpBeam, SharpshooterCharge), screen in `V1ViewModel.cs` (UpdateAltScreen) | `Revolver Twirl`, `Revolver Beam Sharp`, `LaserHitParticle` | `TwirlShot`, `Twirling`, `Shoot2b3`, `Ricochet` |
| Sawed-on shotgun (barrel saw, chain throw) | `Weapons/ShotgunSaw.cs`, `Weapons/Sawblade.cs` | `Shotgun Saw`, `ChainsawProjectile`, `SparksSawblade` | `ChainsawLoop`, `chainsawThrown`, `Steampunk Weapons - Shotgun 2 - Shot - 03`, `Impacts_SIMPLE_003`, scrape clip |
| JumpStart nailgun (cable clamp) | `Weapons/Nailgun.cs` (V1Zapper, NailgunZapperShot) | `Nailgun Zapper`, `Zapper`, `Sawblade Launcher Zapper` | `ElectricitySurgeContinuous`, `ZapperWarning` |
| Screwdriver and Malicious railcannons | `Weapons/Railcannon.cs` | `Railcannon Harpoon`, `Railcannon Malicious`, `Harpoon Malicious`, `Railcannon Beam Malicious`, `Explosion Malicious Railcannon` | `Drill`, `Explosion 1`, `Bluezone-Autobots-shutdown-003`, `RailcannonFire4`, `RailcannonFullClickAndCharge` |
| Rocket launchers: Freezeframe, S.R.S. Cannon, Firestarter | `Weapons/RocketLauncher.cs` (rockets, cannonball, gasoline, ignition) | `Rocket Launcher Freeze`, `Rocket Launcher Cannonball`, `Rocket Launcher Napalm`, `Rocket_0`, `Cannonball_0`, `GasolineProjectile`, `RocketLauncherFire`, `Explosion` | `RocketFire5`, `RocketLoop`, `ClockStart`, `ClockStop`, `ClockTickTock`, `MachinePumpLoop`, `RocketSprayStart`, `RocketSprayLoop`, `RocketSprayStop`, `Explosion 1` |
| Rocket jumps and explosion self-damage (ULTRAKILL launch rules) | `Weapons/SelfBlast.cs` | none | none |
| Knuckleblaster arm (blast wave) | `Weapons/Arms.cs` (punch part), viewmodel in `V1ViewModel.cs` | `Arm Red`, `Explosion Wave Knuckleblaster` | `PunchSwooshHeavy`, `punch_heavy`, `Explosion Wave` |
| Whiplash hook (pull light enemies, pull yourself to heavy ones, stop in front of the player) | `Weapons/Arms.cs` (WhiplashThrow), extra "Hook" state machine in `V1Survivor.cs` | the Whiplash arm model from the player hierarchy (see `docs/ASSETS.md`) | `Whiplash Throw Start`, `Whiplash Pull Start`, `Whiplash Clink`, `harpoonPierce` |
| Shotgun parry (punch your own pellets) with freeze frames and white flash | `Weapons/ShotgunParry.cs`, `Modules/V1HitStop.cs` | none new | `punch_grit_wet_impact_05` |
| Explosion look (growing sphere, white first frames, ground rings, glow, chunks, camera shake) | `Weapons/V1Effects.cs` (V1ExplosionAnim, V1SpriteFade, V1ExplosionChunk), `V1Unity/Assets/V1/Shaders/V1Explosion.shader` | `Explosion`, `ExplosionChunk` | `Explosion 1` |
| Ground slam shockwave and enemy knock-up | `SkillStates/Slam.cs` (KnockUp, V1SlamShockwave) | `PhysicalShockwavePlayer` | none |
| Style meter: points, ranks D..ULTRAKILL with ULTRAKILL drain, bonus feed, weapon freshness | `V1Style.cs` | rank sprites `RankD`, `RankC`, `RankB`, `RankA`, `RankS`, `RankSS`, `RankSSS`, `RankU` | none |
| Grey "hard damage" health | `V1GreyHealth.cs`, arc in `V1Crosshair.cs` | none | none |
| ULTRAKILL crosshair with health/stamina rings | `V1Crosshair.cs` | `Player/Canvas/Crosshair Filler/Crosshair` from a level scene, sprites `meter`, `circlethick` | none |
| Start room, SmileOS shop, drop onto the map | `V1StartRoom.cs`, `Weapons/V1Shop.cs`, `SkillStates/V1SpawnFall.cs` | the FirstRoom "Room" branch, `Wrapped Shop`, font `fs-tahoma-8px`, weapon icons | `ShopDrawerOpen`, `smileOS2Startup`, `smileOSclick1` |
| Weapon screens (shotgun bar with 4 sections, nailgun counters, Marksman coins, Sharpshooter bars, Piercer battery) | `V1ViewModel.cs` (UpdateShotgunScreen, nailgun and alt screens) | the screens are drawn into textures; Piercer uses `batteryfull/mid/low/charging1-3` | `WarningBeep` (`CSFX-2_Mechanics_45`) |
| Equip delay (can't skip fire rate by switching) | `Weapons/V1Arsenal.cs` (EquipDelay, EquipPatch) | none | none |

## Features both mods have

For these, the useful part is usually the numbers and edge cases in the header comments, not the code:
Piercer, Marksman coins (`Weapons/Coin.cs`: reflection priority, double-hit windows, punchflection),
Core Eject and Pump Charge (`ShotgunCore.cs`, `ShotgunPump.cs`), Attractor/Overheat nailguns and sawblade launchers
(`Nailgun.cs`, `Sawblade.cs`), Electric railcannon, Feedbacker parry, movement (`SkillStates/*`, see
`docs/ULTRAKILL_values.md`), blood healing (`V1GreyHealth.cs`).

## Pitfalls already solved here

- RoR2 sprint is a toggle: read `inputBank.rawSprintInput` for "held" (slide/slam).
- RoR2 starts skills only from the main state; movement states must forward skill presses (`V1MovementData.PerformSkillInputs`).
- RoR2 hitboxes are triggers: projectile casts need `QueryTriggerInteraction.Collide` and own filtering (`V1Physics.Cast` in `V1Damage.cs`).
- Unity audio is disabled in RoR2 (Wwise only); sounds must go through your own mixer.
- Damage from clients: fire a tiny targeted `BulletAttack` so the host applies it (`V1Damage.Deal`).
- Slam knock-up: ULTRAKILL's formula throws RoR2 enemies tens of meters; it is capped to a 3-9 m jump height here.
- Hit stop can't be per-player in multiplayer, so it only runs in single player.
