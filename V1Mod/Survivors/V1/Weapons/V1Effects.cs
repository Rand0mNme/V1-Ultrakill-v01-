using UnityEngine;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // Shot visual effects from ULTRAKILL (RevolverBeam): beam line, muzzle flash, hit sparks.
    public static class V1Effects
    {
        // RevolverBeam.FakeShoot: a line from the muzzle to the hit point, the first child (the flash) at the muzzle
        public static void SpawnBeam(GameObject prefab, Vector3 start, Vector3 end, Transform follow = null)
        {
            if (!prefab)
            {
                return;
            }
            Vector3 dir = end - start;
            Quaternion rot = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : Quaternion.identity;
            GameObject beam = Object.Instantiate(prefab, start, rot);
            LineRenderer lr = beam.GetComponent<LineRenderer>();
            if (lr)
            {
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.SetPosition(0, start);
                lr.SetPosition(1, end);
            }
            if (beam.transform.childCount > 0)
            {
                beam.transform.GetChild(0).SetPositionAndRotation(start, rot);
            }
            BeamFade fade = beam.AddComponent<BeamFade>();
            // the muzzle flash stays at the muzzle even while the player moves
            fade.flash = beam.transform.childCount > 0 ? beam.transform.GetChild(0) : null;
            fade.Init();
            fade.follow = follow;
        }

        // flying projectile trail (an ULTRAKILL pellet flies at 75): from the muzzle to the hit point
        public static void SpawnTracer(GameObject prefab, Vector3 start, Vector3 end, float speed)
        {
            if (!prefab)
            {
                return;
            }
            Vector3 dir = end - start;
            if (dir.sqrMagnitude < 0.0001f)
            {
                return;
            }
            GameObject t = Object.Instantiate(prefab, start, Quaternion.LookRotation(dir));
            TracerMover m = t.AddComponent<TracerMover>();
            m.end = end;
            m.speed = speed;
        }

        // Sparks take the color of what was hit (RoR2 SurfaceDef).
        public static void SpawnHit(GameObject prefab, Piercer.ShotEnd end)
        {
            if (!prefab || !end.hit)
            {
                return;
            }
            Vector3 normal = end.normal;
            Quaternion rot = normal.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(normal) : Quaternion.identity;
            GameObject hit = Object.Instantiate(prefab, end.point, rot);
            // surface color (enemies too: stone for the Golem, its own for the Wisp); otherwise normal sparks
            Color tint = Color.white;
            if (end.collider)
            {
                RoR2.SurfaceDef surface = RoR2.SurfaceDefProvider.GetObjectSurfaceDef(end.collider, end.point);
                if (surface)
                {
                    Color c = surface.approximateColor;
                    // make the surface color bright so the sparks still look like sparks
                    float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b), 0.01f);
                    tint = Color.Lerp(Color.white, new Color(c.r / max, c.g / max, c.b / max, 1f), 0.75f);
                }
            }
            foreach (ParticleSystem ps in hit.GetComponentsInChildren<ParticleSystem>(true))
            {
                // half as many sparks as in the prefab
                ParticleSystem.EmissionModule em = ps.emission;
                em.rateOverTimeMultiplier *= 0.5f;
                for (int b = 0; b < em.burstCount; b++)
                {
                    ParticleSystem.Burst burst = em.GetBurst(b);
                    burst.count = new ParticleSystem.MinMaxCurve(Mathf.Max(1f, burst.count.constantMax * 0.5f));
                    em.SetBurst(b, burst);
                }
                ParticleSystem.MainModule main = ps.main;
                ParticleSystem.MinMaxGradient g = main.startColor;
                g.color *= tint;
                g.colorMin *= tint;
                g.colorMax *= tint;
                main.startColor = g;
            }
            foreach (Light l in hit.GetComponentsInChildren<Light>(true))
            {
                l.color *= tint;
            }
            // ScaleNFade: the light shrinks (range -100 per second) and disappears; RemoveOnTime: everything goes away after 1 s
            foreach (Light l in hit.GetComponentsInChildren<Light>(true))
            {
                l.gameObject.AddComponent<LightRangeFade>();
            }
            Object.Destroy(hit, 1f);
        }
    }

    // RevolverBeam.Update: line width drops by 1.5 per second, the light by 100 per second; at zero the object is removed
    public class BeamFade : MonoBehaviour
    {
        public Transform flash;
        public Transform follow;
        private LineRenderer lr;
        private Light muzzleLight;

        private void Awake()
        {
            lr = GetComponent<LineRenderer>();
            muzzleLight = GetComponent<Light>();
        }

        // flash SpriteController: random rotation around the z axis (0..360), then transparency -10 per second
        // and size -10 per second (for the sprite's child flash)
        private SpriteRenderer[] flashSprites;

        public void Init()
        {
            if (!flash)
            {
                return;
            }
            flashSprites = flash.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (SpriteRenderer sr in flashSprites)
            {
                sr.transform.Rotate(new Vector3(0f, 0f, 1f) * Random.Range(0, 360));
            }
        }

        private void LateUpdate()
        {
            if (flash && follow)
            {
                flash.SetPositionAndRotation(follow.position, flash.rotation);
            }
            if (flashSprites != null)
            {
                foreach (SpriteRenderer sr in flashSprites)
                {
                    if (!sr)
                    {
                        continue;
                    }
                    Color c = sr.color;
                    c.a -= 10f * Time.deltaTime;
                    sr.color = c;
                    Vector3 s = sr.transform.localScale - Vector3.one * 10f * Time.deltaTime;
                    sr.transform.localScale = Vector3.Max(s, Vector3.zero);
                }
            }
        }

        private void Update()
        {
            if (muzzleLight)
            {
                muzzleLight.intensity -= Time.deltaTime * 100f;
            }
            if (!lr)
            {
                Destroy(gameObject, 1f);
                enabled = false;
                return;
            }
            lr.widthMultiplier -= Time.deltaTime * 1.5f;
            if (lr.widthMultiplier <= 0f)
            {
                Destroy(gameObject);
            }
        }
    }
}

namespace V1Mod.Survivors.V1.Weapons
{
    public class LightRangeFade : UnityEngine.MonoBehaviour
    {
        private UnityEngine.Light lght;

        private void Awake()
        {
            lght = GetComponent<UnityEngine.Light>();
        }

        private void Update()
        {
            if (!lght)
            {
                return;
            }
            lght.range -= 100f * UnityEngine.Time.deltaTime;
            if (lght.range <= 0f)
            {
                Destroy(lght);
                enabled = false;
            }
        }
    }
}

namespace V1Mod.Survivors.V1.Weapons
{
    public class TracerMover : UnityEngine.MonoBehaviour
    {
        public UnityEngine.Vector3 end;
        public float speed;

        private void Update()
        {
            transform.position = UnityEngine.Vector3.MoveTowards(transform.position, end, speed * UnityEngine.Time.deltaTime);
            if ((transform.position - end).sqrMagnitude < 0.01f)
            {
                Destroy(gameObject);
            }
        }
    }
}

namespace V1Mod.Survivors.V1.Weapons
{
    // ULTRAKILL explosion sphere (Explosion.cs): every physics tick scale += 0.05 * speed; when the radius
    // (lossyScale * 0.2236, the sphere collider radius) exceeds maxSize the sphere fades by alpha 2/s and growth slows 4 times.
    public class V1ExplosionAnim : MonoBehaviour
    {
        public float speed = 1f;
        public float maxSize = 6f;
        private const float ColliderRadius = 0.2236069f;
        private bool fading;
        private Renderer[] renderers;
        private Light[] lights;
        private float alpha = 1f;

        // which spheres grow in which explosions and with what parameters (from the ULTRAKILL prefabs)
        private static readonly System.Collections.Generic.Dictionary<string, (string sphere, float speed, float maxSize)[]> Configs =
            new System.Collections.Generic.Dictionary<string, (string, float, float)[]>
            {
                { "V1Explosion", new[] { ("Sphere_8", 1f, 6f), ("Sphere_8 (1)", 2.5f, 8f) } },
                { "V1MaliciousExplosion", new[] { ("Sphere_8", 1.5f, 13.5f), ("Sphere_8 (1)", 4f, 18f) } },
                { "V1BlastWave", new[] { ("Sphere_8 (1)", 2.5f, 12f) } },
            };

        public static GameObject Spawn(GameObject prefab, Vector3 pos, Quaternion rot, float sizeMul = 1f)
        {
            if (!prefab)
            {
                return null;
            }
            GameObject go = Object.Instantiate(prefab, pos, rot);
            float innerSize = 6f * sizeMul;
            if (Configs.TryGetValue(prefab.name, out var spheres))
            {
                innerSize = spheres[0].maxSize * sizeMul;
                foreach (var (sphere, speed, maxSize) in spheres)
                {
                    foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == sphere)
                        {
                            V1ExplosionAnim a = t.gameObject.AddComponent<V1ExplosionAnim>();
                            // globalSizeMulti in ULTRAKILL multiplies both speed and size
                            a.speed = speed * sizeMul;
                            a.maxSize = maxSize * sizeMul;
                            // Explosion.Start: for the first 0.1 s the inner sphere is pure white (blankMaterial)
                            a.whiteFlash = sphere == spheres[0].sphere && prefab.name != "V1BlastWave";
                        }
                    }
                }
            }
            if (prefab.name == "V1Explosion" || prefab.name == "V1MaliciousExplosion")
            {
                // explosion sprites (ScaleNFade and AlwaysLookAtCamera in ULTRAKILL): the glow fades at 1/s and faces the camera,
                // flat rings on the ground grow and fade (normal explosion: 50/2.5 and 15/1.5; railcannon explosion: 75/1)
                bool malicious = prefab.name == "V1MaliciousExplosion";
                foreach (SpriteRenderer sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    switch (sr.gameObject.name)
                    {
                        case "Glow":
                            V1SpriteFade.Add(sr, 0f, 1f, true);
                            break;
                        case "Shockwave":
                            V1SpriteFade.Add(sr, (malicious ? 75f : 50f) * sizeMul, malicious ? 1f : 2.5f, false);
                            break;
                        case "Shockwave (1)":
                            V1SpriteFade.Add(sr, 15f * sizeMul, 1.5f, false);
                            break;
                    }
                }
                // ExplosionController.toActivate: smoke and sparks turn on when the explosion appears
                foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.gameObject.SetActive(true);
                    ps.Play(true);
                }
                SpawnChunks(pos);
                // CameraShake: in ULTRAKILL shake 1.5 next to the explosion, weaker further away (up to 85 m)
                RoR2.ShakeEmitter.CreateSimpleShakeEmitter(pos, new Wave { amplitude = 1.5f, frequency = 25f, cycleOffset = 0f }, 0.35f, Mathf.Max(3f * innerSize, 40f), true);
            }
            Object.Destroy(go, 5f);
            return go;
        }

        // Explosion.Start: 24-30 chunks (ExplosionChunk) fly off very fast (random vector x 250) with gravity
        private static void SpawnChunks(Vector3 pos)
        {
            if (!V1Assets.explosionChunk)
            {
                return;
            }
            int n = Random.Range(24, 30);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = pos + new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f));
                GameObject c = Object.Instantiate(V1Assets.explosionChunk, p, Random.rotation);
                V1Assets.ToWorldMaterials(c);
                V1ExplosionChunk chunk = c.AddComponent<V1ExplosionChunk>();
                chunk.velocity = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 2f), Random.Range(-1f, 1f)) * 250f;
            }
        }

        public bool whiteFlash;
        private float age;
        private Material[][] originalMaterials;
        private float[] baseAlpha;

        private void Start()
        {
            renderers = GetComponents<Renderer>();
            lights = GetComponentsInChildren<Light>();
            baseAlpha = new float[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Material sm = renderers[i] ? renderers[i].sharedMaterial : null;
                baseAlpha[i] = sm && sm.HasProperty("_Color") ? sm.color.a : 1f;
            }
            if (whiteFlash)
            {
                originalMaterials = new Material[renderers.Length][];
                for (int i = 0; i < renderers.Length; i++)
                {
                    originalMaterials[i] = renderers[i].materials;
                    Material[] white = new Material[originalMaterials[i].Length];
                    for (int j = 0; j < white.Length; j++)
                    {
                        white[j] = new Material(originalMaterials[i][j]);
                        white[j].SetTexture("_MainTex", Texture2D.whiteTexture);
                        white[j].color = Color.white;
                    }
                    renderers[i].materials = white;
                }
            }
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (originalMaterials != null && age > 0.1f)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i])
                    {
                        renderers[i].materials = originalMaterials[i];
                    }
                }
                originalMaterials = null;
            }
            foreach (Light l in lights)
            {
                if (l)
                {
                    l.range += 5f * Time.deltaTime * speed;
                    if (fading)
                    {
                        l.intensity -= 65f * Time.deltaTime;
                    }
                }
            }
            if (fading)
            {
                alpha -= 2f * Time.deltaTime;
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer r = renderers[i];
                    if (r && r.material.HasProperty("_Color"))
                    {
                        Color c = r.material.color;
                        c.a = Mathf.Max(0f, alpha) * baseAlpha[i];
                        r.material.color = c;
                    }
                }
                if (alpha <= 0f)
                {
                    gameObject.SetActive(false);
                }
            }
        }

        private void FixedUpdate()
        {
            // ULTRAKILL physics tick is 0.02 s
            transform.localScale += Vector3.one * 0.05f * speed * (Time.fixedDeltaTime / 0.02f);
            if (!fading && transform.lossyScale.x * ColliderRadius > maxSize)
            {
                fading = true;
                speed /= 4f;
            }
        }
    }
}

namespace V1Mod.Survivors.V1.Weapons
{
    // ULTRAKILL muzzle flash: the sprite has a SpriteController (random rotation, fades and shrinks at 7.5/s),
    // the light has ScaleNFade (range fades at 80/s). Without these scripts the flash hung huge and opaque.
    public class V1FlashFx : MonoBehaviour
    {
        public float spriteFade = 7.5f;
        public float spriteShrink = 7.5f;
        public float lightFade = 80f;
        private SpriteRenderer[] sprites;
        private Light[] lights;

        public static GameObject Spawn(GameObject prefab, Vector3 pos, Quaternion rot, float spriteFade = 7.5f, float spriteShrink = 7.5f, float lightFade = 80f)
        {
            if (!prefab)
            {
                return null;
            }
            GameObject go = Object.Instantiate(prefab, pos, rot);
            V1FlashFx fx = go.AddComponent<V1FlashFx>();
            fx.spriteFade = spriteFade;
            fx.spriteShrink = spriteShrink;
            fx.lightFade = lightFade;
            Object.Destroy(go, 1f);
            return go;
        }

        private void Start()
        {
            sprites = GetComponentsInChildren<SpriteRenderer>(true);
            lights = GetComponentsInChildren<Light>(true);
            foreach (SpriteRenderer s in sprites)
            {
                s.transform.Rotate(Vector3.forward * Random.Range(0, 360));
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            foreach (SpriteRenderer s in sprites)
            {
                if (!s)
                {
                    continue;
                }
                Color c = s.color;
                c.a -= spriteFade * dt;
                s.color = c;
                s.transform.localScale = Vector3.Max(s.transform.localScale - Vector3.one * spriteShrink * dt, Vector3.zero);
            }
            foreach (Light l in lights)
            {
                if (l)
                {
                    l.range = Mathf.Max(0f, l.range - lightFade * dt);
                }
            }
        }
    }
}

namespace V1Mod.Survivors.V1.Weapons
{
    // Explosion chunk (ExplosionChunk.cs): flies with gravity, disappears after 3 s;
    // after hitting a wall it stops leaving a trail and disappears after 1 s
    public class V1ExplosionChunk : MonoBehaviour
    {
        public Vector3 velocity;
        private float life = 3f;
        private bool done;

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            life -= dt;
            if (life <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            if (done)
            {
                return;
            }
            velocity += Physics.gravity * dt;
            Vector3 step = velocity * dt;
            if (Physics.Raycast(transform.position, step, out RaycastHit hit, step.magnitude, RoR2.LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point;
                done = true;
                life = Mathf.Min(life, 1f);
                foreach (TrailRenderer t in GetComponentsInChildren<TrailRenderer>())
                {
                    t.emitting = false;
                }
                return;
            }
            transform.position += step;
        }
    }
}

namespace V1Mod.Survivors.V1.Weapons
{
    // ScaleNFade (sprite) from ULTRAKILL: scale grows by scaleSpeed per second, alpha drops by fadeSpeed per second;
    // lookAtCamera is AlwaysLookAtCamera (the explosion glow always faces the player)
    public class V1SpriteFade : MonoBehaviour
    {
        private SpriteRenderer sr;
        private float scaleSpeed;
        private float fadeSpeed;
        private bool lookAtCamera;

        public static void Add(SpriteRenderer sr, float scaleSpeed, float fadeSpeed, bool lookAtCamera)
        {
            V1SpriteFade f = sr.gameObject.AddComponent<V1SpriteFade>();
            f.sr = sr;
            f.scaleSpeed = scaleSpeed;
            f.fadeSpeed = fadeSpeed;
            f.lookAtCamera = lookAtCamera;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            transform.localScale += Vector3.one * scaleSpeed * dt;
            Color c = sr.color;
            c.a -= fadeSpeed * dt;
            sr.color = c;
            if (c.a <= 0f)
            {
                gameObject.SetActive(false);
                return;
            }
            if (lookAtCamera)
            {
                foreach (RoR2.CameraRigController rig in RoR2.CameraRigController.readOnlyInstancesList)
                {
                    if (rig.sceneCam)
                    {
                        transform.rotation = Quaternion.LookRotation(transform.position - rig.sceneCam.transform.position);
                    }
                    break;
                }
            }
        }
    }
}
