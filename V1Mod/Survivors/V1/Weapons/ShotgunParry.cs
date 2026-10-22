using RoR2;
using UnityEngine;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // Shotgun parry (Punch.ParryProjectile for playerBullet, Projectile.boosted):
    // if the first active frame of a Feedbacker punch lands while the pellets are still at your face
    // (they fly at 75 m/s, the fist reaches 4 m, so about 0.05 s after the shot), one pellet is parried:
    // speed x2 (150 m/s), collider x4, its own damage 0, but it explodes on impact (Explosion: 35 / 10 = 3.5, radius 6),
    // which gives a "boosted" launch of at least 60 m/s when it knocks back the player.
    public static class ShotgunParry
    {
        public static float lastShotTime = -10f;
        public static CharacterBody lastShotBody;
        public const float Window = 0.06f;

        public static bool TryBoost(CharacterBody body, Ray aim)
        {
            float since = Time.time - lastShotTime;
            if (lastShotBody != body || since < 0f || since > Window)
            {
                return false;
            }
            lastShotTime = -10f;
            GameObject go = V1Assets.shotgunPellet ? Object.Instantiate(V1Assets.shotgunPellet, aim.origin + aim.direction, Quaternion.LookRotation(aim.direction)) : new GameObject("V1BoostedPellet");
            go.transform.position = aim.origin + aim.direction;
            // the parried pellet is orange (Color(1, 0.35, 0))
            Color orange = new Color(1f, 0.35f, 0f);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.material.HasProperty("_Color"))
                {
                    r.material.color = orange;
                }
            }
            foreach (TrailRenderer t in go.GetComponentsInChildren<TrailRenderer>(true))
            {
                t.startColor = orange;
                t.endColor = new Color(orange.r, orange.g, orange.b, 0f);
            }
            foreach (Light l in go.GetComponentsInChildren<Light>(true))
            {
                l.color = orange;
            }
            V1BoostedPellet p = go.AddComponent<V1BoostedPellet>();
            p.body = body;
            p.velocity = aim.direction * 150f;
            V1ParryFlash.Flash(body);
            V1Sounds.Play("PunchHit", body.gameObject, 0.6f, 1.4f, true);
            return true;
        }
    }

    public class V1BoostedPellet : MonoBehaviour
    {
        public CharacterBody body;
        public Vector3 velocity;
        private float age;

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            age += dt;
            if (!body || age > 10f)
            {
                Destroy(gameObject);
                return;
            }
            Vector3 step = velocity * dt;
            if (V1Physics.Cast(body, transform.position, 0.5f, step, step.magnitude, out RaycastHit h, out _))
            {
                Vector3 pos = h.point - velocity.normalized * 0.3f;
                RocketLauncher.Explode(body, pos, 3.5f, 6f, V1Assets.explosion, 1f, 35f, boosted: true, source: DamageSource.Special);
                Destroy(gameObject);
                return;
            }
            transform.position += step;
        }
    }
}

namespace V1Mod.Survivors.V1.Weapons
{
    // TimeController.ParryFlash: full-screen white flash for 0.1 s, time stop for 0.25 s, camera shake
    public static class V1ParryFlash
    {
        public static void Flash(CharacterBody body)
        {
            V1HitStop.HitStop(0.25f);
            if (body)
            {
                RoR2.ShakeEmitter.CreateSimpleShakeEmitter(body.corePosition, new Wave { amplitude = 0.5f, frequency = 30f, cycleOffset = 0f }, 0.2f, 5f, true);
            }
            RoR2.UI.HUD hud = body ? V1ShopWindow.FindHud(body) : null;
            if (!hud || !hud.mainContainer)
            {
                return;
            }
            GameObject go = new GameObject("V1ParryFlash", typeof(RectTransform));
            go.transform.SetParent(hud.mainContainer.transform, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            UnityEngine.UI.Image img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(1f, 1f, 1f, 0.85f);
            img.raycastTarget = false;
            go.AddComponent<V1RealtimeDestroy>().life = 0.1f;
        }
    }

    // destroys the object after life seconds of real time (works while time is stopped too)
    public class V1RealtimeDestroy : MonoBehaviour
    {
        public float life;
        private float start;

        private void Start()
        {
            start = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup - start >= life)
            {
                Destroy(gameObject);
            }
        }
    }
}
