using RoR2;
using UnityEngine;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.SkillStates
{
    // ULTRAKILL movement particles (NewMovement): dash (dodgeParticle), slide (slideParticle + slideScrape),
    // wall slide (wallScrape), ground slam (fallParticle) and heavy landing (impactDust).
    public class V1MovementFx : MonoBehaviour
    {
        private CharacterBody body;
        private V1MovementData data;
        private GameObject slideParticle;
        private GameObject slideScrape;
        private GameObject wallScrape;
        private GameObject fallParticle;
        private Vector3 slideDirection;

        // set by V1CharacterMain while V1 slides down a wall
        public bool clinging;
        public Vector3 clingPoint;
        public Vector3 clingNormal;

        public static V1MovementFx Get(GameObject go)
        {
            V1MovementFx fx = go.GetComponent<V1MovementFx>();
            return fx ? fx : go.AddComponent<V1MovementFx>();
        }

        private void Start()
        {
            body = GetComponent<CharacterBody>();
            data = V1MovementData.Get(gameObject);
        }

        private Vector3 Center => body ? body.corePosition : transform.position;

        private void LateUpdate()
        {
            if (!body || !body.characterMotor)
            {
                return;
            }
            bool grounded = body.characterMotor.isGrounded;
            Vector3 flatVel = body.characterMotor.velocity;
            flatVel.y = 0f;
            Vector3 normalized = flatVel.sqrMagnitude > 0.01f ? flatVel.normalized : slideDirection;

            // slide: speed lines 10 m ahead, turned backwards, and scraping at the feet
            if (data.sliding)
            {
                if (!slideParticle && V1Assets.slideParticle)
                {
                    slideDirection = normalized.sqrMagnitude > 0f ? normalized : transform.forward;
                    slideParticle = Instantiate(V1Assets.slideParticle, Center + slideDirection * 10f, Quaternion.LookRotation(-slideDirection));
                    slideScrape = V1Assets.slideScrape ? Instantiate(V1Assets.slideScrape, Center + slideDirection * 2f, Quaternion.LookRotation(-slideDirection)) : null;
                }
                if (normalized.sqrMagnitude > 0f)
                {
                    slideDirection = normalized;
                }
                if (slideParticle)
                {
                    slideParticle.transform.position = Center + normalized * 10f;
                    slideParticle.transform.forward = -slideDirection;
                }
                if (slideScrape)
                {
                    // in the air the scrape is moved far away, as in ULTRAKILL
                    slideScrape.transform.position = grounded ? body.footPosition + normalized : Vector3.one * 5000f;
                    slideScrape.transform.forward = -normalized;
                }
            }
            else
            {
                if (slideParticle)
                {
                    Destroy(slideParticle);
                }
                Detach(ref slideScrape);
            }

            // wall slide
            if (clinging && !grounded)
            {
                if (!wallScrape && V1Assets.wallScrape)
                {
                    wallScrape = Instantiate(V1Assets.wallScrape, clingPoint + Vector3.up, Quaternion.identity);
                }
                if (wallScrape)
                {
                    wallScrape.transform.position = clingPoint + Vector3.up;
                    if (clingNormal.sqrMagnitude > 0f)
                    {
                        wallScrape.transform.forward = clingNormal;
                    }
                }
            }
            else
            {
                Detach(ref wallScrape);
            }
            clinging = false;

            // ground slam: the fall particles are attached to the player while falling
            if (data.heavyFall)
            {
                if (!fallParticle && V1Assets.fallParticle)
                {
                    fallParticle = Instantiate(V1Assets.fallParticle, transform);
                    fallParticle.transform.position = Center;
                }
            }
            else if (fallParticle)
            {
                Destroy(fallParticle);
            }
        }

        // DetachScrape: particles stop spawning, the ones already flying burn out
        private static void Detach(ref GameObject scrape)
        {
            if (!scrape)
            {
                return;
            }
            foreach (ParticleSystem ps in scrape.GetComponentsInChildren<ParticleSystem>())
            {
                ps.Stop();
            }
            Destroy(scrape, 3f);
            scrape = null;
        }

        private void OnDestroy()
        {
            if (slideParticle) Destroy(slideParticle);
            if (slideScrape) Destroy(slideScrape);
            if (wallScrape) Destroy(wallScrape);
            if (fallParticle) Destroy(fallParticle);
        }

        // TryDash: dodgeParticle 10 m along the dash direction, turned backwards
        public static void Dash(GameObject go, Vector3 direction)
        {
            if (!V1Assets.dodgeParticle || direction.sqrMagnitude < 0.0001f)
            {
                return;
            }
            CharacterBody b = go.GetComponent<CharacterBody>();
            Vector3 center = b ? b.corePosition : go.transform.position;
            GameObject p = Instantiate(V1Assets.dodgeParticle, center + direction.normalized * 10f, Quaternion.LookRotation(-direction));
            Destroy(p, 3f);
        }

        // LandingImpact: dust cloud at the feet
        public static void HeavyLanding(GameObject go)
        {
            if (!V1Assets.dustBig)
            {
                return;
            }
            CharacterBody b = go.GetComponent<CharacterBody>();
            Vector3 feet = b ? b.footPosition : go.transform.position;
            GameObject p = Instantiate(V1Assets.dustBig, feet, Quaternion.identity);
            p.transform.forward = Vector3.up;
            Destroy(p, 4f);
        }
    }
}
