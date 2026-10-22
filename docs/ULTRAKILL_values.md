# ULTRAKILL movement values (for the V1 port)

Source: decompiled `ULTRAKILL_Data/Managed/Assembly-CSharp.dll` (NewMovement.cs) in `_reference/ULTRAKILL_decompiled` (private, never ship), plus `globalgamemanagers` project settings.

## Confirmed
- Physics tick (Fixed Timestep): 0.008 s (globalgamemanagers TimeManager). All per-tick values below use it.
- Gravity: (0, -40, 0) (DynamicsManager). RoR2 uses a different gravity.
- Ground walk: target velocity = inputDir * walkSpeed * dt * 2.75; velocity lerps 25% toward it each tick.
- Air move cap 16.5 u/s per axis; air control uses airAcceleration and rb.mass (values in prefab, not read yet).
- Stamina (boostCharge): max 300, dash costs 100, regen 70/s * difficulty (x2 Harmless, x1.5 Lenient, x1 Standard+). No regen while sliding.
- Dash: boostLeft = 100, minus 4 per tick = 25 ticks = 0.2 s. Velocity = dodgeDir * walkSpeed * dt * 2.75 * 3, horizontal only. Layer 15 (invincible) while dashing. After it ends, velocity = dodgeDir * walk speed.
- Dash direction: inputDir, or transform.forward when there's no input.
- Jump: AddForce(up * jumpPower * 1500 * k). k = 2.6 normal, 2 from slide, 1.5 dash jump (costs another 100 stamina), 3+(slamForce-1) slam bounce (12.5 when slamForce >= 5.5). Jump cooldown 0.2 s.
- Wall jump: up to 3 in air, reset on ground. velocity = 0, then AddForce((away-from-wall flat + up) * 2000 * wallJumpPower). Cooldown 0.1 s.
- Slam: needs fallTime > 0.5 s and no ground within 3 m. Velocity set to (0, -100, 0).
- Slide: speed = dodgeDir * walkSpeed * dt * 4 * preSlideSpeed (1..3). preSlideSpeed = speed/24, decays by friction on the ground. Strafe input adds 5 sideways.

## Player prefab (AssetRipper export, scene 005a4f2c..., NewMovement component)
- walkSpeed 750, airAcceleration 6000, jumpPower 90, wallJumpPower 150, hp 100.
- Rigidbody mass 100. Capsule radius 0.5, height 3.5. WallCheck trigger sphere radius 0.9 (0.5 x scale 1.8).
- Derived: jump dv = 90*1500*k*0.008/100 = 10.8*k (normal 28.08, slide 21.6, dash 16.2 m/s). Wall jump dv = 24 m/s away + 24 up.
- Slam: velocity (0,-100,0), slamForce += 5/s. Landing opens 0.1 s super-jump window, then 0.306 s extra window; jump multiplier 3+(slamForce-1), 12.5 if slamForce >= 5.5. Fall speed is capped at 100.

## Not ported yet
- Slam damage and shockwave, enemy step, slide-jump SSJ timing bonus, collider shrink and camera drop while sliding.

## Shotgun, Core Eject (Shotgun.cs variation 0, "Shotgun Grenade.prefab")
- Primary: 12 "Shotgun Projectile" pellets (speed 75 ±10, damage 1), spread 10 (random Rotate per axis). Point blank: RaycastAll 4 m on enemies, DeliverDamage 4. Rate: ReadyGun event at 1.333 s (FireWithReload_1).
- Alt: hold Fire2 to build grenadeForce to 60 at 60/s (gun shake ±force/3000). Release: Grenade.prefab, velocity (camForward + up*force*0.002)*(force+10). Next core after sinceLastCore > 0.5 s.
- Core explodes on any collision (Grenade.Collision). Explosion.prefab: damage 35 -> enemy damage 35/10 = 3.5, maxSize 6.
- In the port: pellets are hitscan with a visual tracer moving at 75 m/s. Blast radius is 6 m (approximation). The core's explosion damage applies only when the player is host (BlastAttack runs server-side).
- Sounds: shot "Steampunk Weapons - Shotgun 2 - Shot - 03", core "…Shot - 05", charge "electricCharge", explosion "Explosion 1".

## Arsenal (ported 2026-10-19, 1 ULTRAKILL damage = 275%)
- Pump Charge: LMB pause 0.993 s, RMB pump 0.641 s; charge 0/1/2 → 10 (spread 10/1.5) / 16 (10) / 24 (20) pellets; 3 → explosion at the barrel for 5 damage, size x1.5.
- Sawed-on: saw zone 0.25 damage every 0.05 s; throw (force+10)*1.5; saw 1.5 damage x3 per enemy (0.05 s), return up to 100 m/s, ricochet at half speed; charge 4 s (8 s while the saw is flying); 40/60 hits without a kill = overheat/break.
- Nailguns: pause fireRate/100 s (4 → 25/s), nail 200 m/s, gravity 40, spread ±spread/3. Attractor: spread 10, damage 0.205, 100 nails (+5/s), up to 3 magnets (charge 3/s), radius 25, force 300. Overheat: spread 20, damage 0.185, heat 0.55/s, cooling 0.3/s, past 0.5 heat pause +(h-0.5)*7, heatsink vent: pause 1.5, spread x2, heated; 2 heatsinks (+0.125/s). JumpStart: damage 0.185, pause 4 (with clamp) / 6.35, clamp 100 m/s, 5 s of charge, shock 10, snaps beyond 20 m, cooldown 5 s.
- Railcannons: charge 0.25/s up to 4 → full (16 s), RMB = FOV/2. Electric: 2 damage x4 on every enemy, piercing. Screwdriver: 250 m/s, 3 damage + 116 x 0.0625 every 0.05 s. Malicious: 2 damage + explosion 50 (x1.25 on enemies), maxSize 13.5.
- Rocket launchers: a rocket once per 1 s, 100 m/s, explosion 3.5 (maxSize 6). Freezeframe: freeze up to 5 s, recharges twice as slowly. S.R.S.: charge 1 s, speed max(15, 150*charge), damage min(5, v*0.15), cooldown 8 s. Firestarter: 50 drops/s at 0.015 of the tank each, 150 m/s, the tank recharges in 8 s.
- Marksman: 4 coins (100 of 400 each, +25/s), toss forward 20 + up 15 + player velocity; power 2, +1 for every coin in the chain; double-hit windows 0.35–0.417 s and after 1 s.
- Sharpshooter: 3 charges (100 of 300 each, +15/s), spin-up 75/s, bounces floor(charge/25) up to 3, beam 1 damage, piercing.
- Arms: stamina 2 (+1.25/s). Feedbacker 1 damage, cost 1, pause 0.25 s, parry. Knuckleblaster 2.5 damage, cost 1.5, pause 0.375 s, wave when held (0.416 s) 1 damage, maxSize 12. Whiplash 250 m/s up to 300 m, pull 60 m/s, pause 0.5 s.
- Coin (Coin.cs, 2026-10-20): reflection priority: another coin (power +1) -> your own explosive within 100 m (Grenade/Cannonball, the core gives Explosion Super: 35, radius 12) -> the nearest visible enemy not yet hit by this chain (CoinChainCache), on the weak spot -> glass -> random. hitTimes 2 in the 0.35–0.417 s window and after 1 s: the second reflection 0.05 s later into another target. Piercing beams (charged Piercer, railcannon) fly on by themselves, damage += power/4; ricocheting ones (Sharpshooter) get more maxHitsPerTarget. Punchflection (Feedbacker): damage = power, a new coin in the enemy's place, up at 25 m/s, power +1 (above 5 only on a hit). Removed after 5 s or at speed < 1 (timeToDelete -10/s).
