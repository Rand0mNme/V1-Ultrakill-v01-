using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // The "Piercer" revolver from ULTRAKILL (Revolver.cs, gunVariation 0; RevolverBeam prefabs):
    // - LMB: 1 damage, x2 on a weak spot; 0.5 s between shots (shootCharge +200 per second)
    // - hold RMB: charge +175 per second up to 100; fires on releasing RMB or on LMB when fully charged
    // - charged beam: 3 hits of 1 damage (x2.5 on a weak spot); if the enemy dies, the rest flies on
    // - after a charged shot RMB recharges for 2.5 s (pierceCharge +40 per second)
    // - a charge released early drains at 175 per second
    public static class Piercer
    {
        // How many percent of RoR2 damage 1 ULTRAKILL damage is worth (2.75 = 275%): tuned by enemy health
        // so a Lemurian dies to about as many shots as a Stray in ULTRAKILL, and a Golem like a Malicious Face
        public static float DamagePerUnit = 2.75f;

        public const float ShotInterval = 0.5f;
        public const float ChargeRate = 175f;
        public const float SuperRecharge = 2.5f;
        public const int SuperHits = 3;
        public const float HeadshotMultiplier = 2f;
        public const float SuperHeadshotMultiplier = 2.5f;

        public static GameObject tracerNormal;
        public static GameObject tracerSuper;
        public static GameObject hitEffect;

        public static void LoadEffects()
        {
            tracerNormal = TryLoad("RoR2/Base/Commando/TracerCommandoDefault.prefab");
            tracerSuper = TryLoad("RoR2/DLC1/Railgunner/TracerRailgunSuper.prefab") ?? TryLoad("RoR2/Base/Huntress/TracerHuntressSnipe.prefab") ?? tracerNormal;
            hitEffect = TryLoad("RoR2/Base/Commando/HitsparkCommando.prefab");
        }

        private static GameObject TryLoad(string key)
        {
            try
            {
                return Addressables.LoadAssetAsync<GameObject>(key).WaitForCompletion();
            }
            catch
            {
                Log.Warning("Effect not found " + key);
                return null;
            }
        }

        // One hitscan shot. Skips enemies that are already dead so the rest of a charged beam flies on.
        // Returns the point where the bullet stopped (for the ULTRAKILL beam) and draws hit sparks.
        public struct ShotEnd
        {
            public Vector3 point;
            public Vector3 normal;
            public Collider collider;
            public bool enemy;
            public bool hit;
        }

        public static ShotEnd FireBullet(GameObject owner, CharacterBody body, Ray aimRay, bool super, float headshotMultiplier)
        {
            bool ukVisuals = V1Assets.revolverBeam;
            ShotEnd end = new ShotEnd { point = aimRay.origin + aimRay.direction * 1000f };
            BulletAttack bullet = new BulletAttack
            {
                owner = owner,
                weapon = owner,
                origin = aimRay.origin,
                aimVector = aimRay.direction,
                minSpread = 0f,
                maxSpread = 0f,
                bulletCount = 1,
                damage = DamagePerUnit * body.damage,
                damageColorIndex = DamageColorIndex.Default,
                damageType = V1Damage.Combo(DamageType.Generic, super ? DamageSource.Secondary : DamageSource.Primary),
                falloffModel = BulletAttack.FalloffModel.None,
                maxDistance = 1000f,
                force = super ? 1500f : 300f,
                hitMask = LayerIndex.CommonMasks.bullet,
                isCrit = body.RollCrit(),
                procCoefficient = 1f,
                radius = 0.4f, // RevolverBeam: a non-piercing shot uses a SphereCast of radius 0.4
                smartCollision = true,
                tracerEffectPrefab = ukVisuals ? null : (super ? tracerSuper : tracerNormal),
                hitEffectPrefab = ukVisuals ? null : hitEffect,
                muzzleName = "MuzzleRight",
            };
            bullet.filterCallback = (BulletAttack attack, ref BulletAttack.BulletHit hit) =>
            {
                if (hit.hitHurtBox && hit.hitHurtBox.healthComponent && !hit.hitHurtBox.healthComponent.alive)
                {
                    return false;
                }
                return BulletAttack.DefaultFilterCallback(attack, ref hit);
            };
            // weak spot (like the head in ULTRAKILL)
            bullet.modifyOutgoingDamageCallback = (BulletAttack attack, ref BulletAttack.BulletHit hit, DamageInfo damageInfo) =>
            {
                if (hit.hitHurtBox && hit.hitHurtBox.isSniperTarget)
                {
                    damageInfo.damage *= headshotMultiplier;
                }
            };
            bullet.hitCallback = (BulletAttack attack, ref BulletAttack.BulletHit hit) =>
            {
                // true: the bullet flies on, false: it stopped here
                bool keepGoing = HitWithoutRoR2Effects(attack, ref hit);
                if (!keepGoing)
                {
                    end = new ShotEnd { point = hit.point, normal = hit.surfaceNormal, collider = hit.collider, enemy = hit.hitHurtBox, hit = true };
                }
                return keepGoing;
            };
            bullet.Fire();
            return end;
        }

        // Damage like a normal RoR2 bullet, but without its surface hit effects (dust, RoR2 "spheres"):
        // RoR2 draws them from the hit collider, so the collider is removed for the duration of the call.
        // Returns true if the bullet flies on (like BulletAttack.DefaultHitCallback).
        public static bool HitWithoutRoR2Effects(BulletAttack attack, ref BulletAttack.BulletHit hit)
        {
            Collider col = hit.collider;
            bool keepGoing = col && ((1 << col.gameObject.layer) & (int)attack.stopperMask) == 0;
            hit.collider = null;
            BulletAttack.DefaultHitCallback(attack, ref hit);
            hit.collider = col;
            return keepGoing;
        }

        public static void FovKick(GameObject owner, float amount)
        {
            V1CameraEffects fx = owner.GetComponent<V1CameraEffects>();
            if (fx)
            {
                fx.fov += amount;
            }
        }
    }

    // RMB charge state shared by both skills
    public class PiercerData : MonoBehaviour
    {
        public float pierceShotCharge;
        public bool chargingPierce;
        private CharacterBody body;
        private SoundHandle chargeAudio;
        private SoundHandle rechargeAudio;
        private bool wasReady = true;

        private void Start()
        {
            body = GetComponent<CharacterBody>();
        }

        private void Update()
        {
            if (!chargingPierce && pierceShotCharge > 0f)
            {
                pierceShotCharge = Mathf.Max(0f, pierceShotCharge - Piercer.ChargeRate * Time.deltaTime);
            }
            UpdateChargeSound();
            UpdateReadySound();
        }

        // charge sound: pitch 0.005 x charge, volume 0.25 + 0.005 x charge
        private void UpdateChargeSound()
        {
            if (pierceShotCharge > 0f)
            {
                if (chargeAudio == null)
                {
                    chargeAudio = V1Sounds.Play("PierceCharge", gameObject, 0f, 0.01f, body && body.hasEffectiveAuthority, loop: true);
                    if (chargeAudio == null)
                    {
                        return;
                    }
                }
                chargeAudio.Pitch = pierceShotCharge * 0.005f;
                chargeAudio.Volume = 0.25f + pierceShotCharge * 0.005f;
            }
            else if (chargeAudio != null)
            {
                chargeAudio.Stop();
                chargeAudio = null;
            }
        }

        // "beep-beep" when RMB is ready again
        private void UpdateReadySound()
        {
            if (!body || !body.skillLocator || !body.skillLocator.secondary)
            {
                return;
            }
            bool ready = body.skillLocator.secondary.stock > 0;
            // while RMB recharges, a looping charge sound plays at the screen (screenAud, volume 0.55)
            if (!ready && rechargeAudio == null)
            {
                rechargeAudio = V1Sounds.Play("Charging", gameObject, 0.55f, 1f, body.hasEffectiveAuthority, loop: true);
            }
            if (ready && !wasReady)
            {
                rechargeAudio?.Stop();
                rechargeAudio = null;
                V1Sounds.Play("BeepBeep_high", gameObject, 0.35f, Random.Range(1f, 1.1f), body.hasEffectiveAuthority);
            }
            wasReady = ready;
        }

        private void OnDestroy()
        {
            chargeAudio?.Stop();
            rechargeAudio?.Stop();
        }

        public static PiercerData Get(GameObject go)
        {
            PiercerData d = go.GetComponent<PiercerData>();
            return d ? d : go.AddComponent<PiercerData>();
        }
    }

    // LMB: normal shot
    public class PiercerShoot : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            StartAimMode(1f);
            if (isAuthority)
            {
                Ray aim = GetAimRay();
                // a shot at a Marksman coin: the beam goes into the coin, which takes it from there
                Vector3 muzzlePos = V1ViewModel.Get(gameObject)?.ShootPointPosition ?? aim.origin;
                // a shot at your own explosives (shotgun core, rocket, S.R.S. cannonball) detonates them
                if (V1Coin.TryShootCoin(characterBody, aim, muzzlePos) || V1Explosives.TryShoot(characterBody, aim, muzzlePos, 0.75f))
                {
                    Piercer.FovKick(gameObject, V1Camera.DefaultFov / 40f);
                    V1ViewModel.Get(gameObject)?.Shoot();
                    V1Sounds.Play("Shoot1c4", gameObject, 0.55f, Random.Range(0.9f, 1.1f), true);
                    outer.SetNextStateToMain();
                    return;
                }
                Piercer.ShotEnd end = Piercer.FireBullet(gameObject, characterBody, aim, false, Piercer.HeadshotMultiplier);
                V1Effects.SpawnBeam(V1Assets.revolverBeam, V1ViewModel.Get(gameObject)?.ShootPointPosition ?? aim.origin, end.point, V1ViewModel.Get(gameObject)?.ShootPoint);
                // one burst of sparks at the hit point (RevolverBeam: hitParticle at the end of the beam)
                V1Effects.SpawnHit(V1Assets.laserHit, end);
                Piercer.FovKick(gameObject, V1Camera.DefaultFov / 40f);
                V1ViewModel.Get(gameObject)?.Shoot();
            }
            V1Sounds.Play("Shoot1c4", gameObject, 0.55f, Random.Range(0.9f, 1.1f), isAuthority);
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority()
        {
            return InterruptPriority.Any;
        }
    }

    // RMB: hold to charge, fire on release or on LMB
    public class PiercerCharge : BaseSkillState
    {
        private PiercerData data;

        public override void OnEnter()
        {
            base.OnEnter();
            data = PiercerData.Get(gameObject);
            // charging is only possible while the revolver is ready to fire (shootReady)
            if (skillLocator.primary.stock < 1)
            {
                outer.SetNextStateToMain();
                return;
            }
            data.chargingPierce = true;
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority)
            {
                return;
            }
            StartAimMode(1f);

            bool full = data.pierceShotCharge >= 100f;
            bool released = !inputBank.skill2.down;

            if (full && (released || inputBank.skill1.down) && skillLocator.primary.stock >= 1)
            {
                FireSuper();
                outer.SetNextStateToMain();
                return;
            }
            if (released)
            {
                outer.SetNextStateToMain();
                return;
            }
            data.pierceShotCharge = Mathf.Min(100f, data.pierceShotCharge + Piercer.ChargeRate * Time.fixedDeltaTime);
        }

        private void FireSuper()
        {
            Ray aim = GetAimRay();
            // piercing beam: 3 hits on enemies along the beam, the beam itself reaches the wall
            Vector3 muzzle = V1ViewModel.Get(gameObject)?.ShootPointPosition ?? aim.origin;
            GameObject owner = gameObject;
            CharacterBody b = characterBody;
            // a coin on the path: the beam reflects off it into a target and gets stronger (RevolverBeam: Coin, addedDamage += power / 4)
            if (!V1Coin.TryBeamCoin(b, aim, muzzle, V1Assets.revolverBeamSuper, (o, d, bonus) =>
                {
                    Vector3 e = PiercingShot.Fire(owner, b, new Ray(o, d), 1f + bonus);
                    V1Effects.SpawnBeam(V1Assets.revolverBeamSuper, o, e);
                }))
            {
                Vector3 end = PiercingShot.Fire(gameObject, characterBody, aim);
                V1Effects.SpawnBeam(V1Assets.revolverBeamSuper, muzzle, end, V1ViewModel.Get(gameObject)?.ShootPoint);
            }
            data.pierceShotCharge = 0f;
            skillLocator.secondary.DeductStock(1);
            // the shot resets the readiness of the normal shot (shootReady = false)
            skillLocator.primary.DeductStock(1);
            Piercer.FovKick(gameObject, V1Camera.DefaultFov / 20f);
            V1ViewModel.Get(gameObject)?.ChargeShoot();
            V1Sounds.Play("Shoot2b3", gameObject, 0.5f, 1f, true);
        }

        public override void OnExit()
        {
            if (data)
            {
                data.chargingPierce = false;
            }
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority()
        {
            return InterruptPriority.PrioritySkill;
        }
    }
}
