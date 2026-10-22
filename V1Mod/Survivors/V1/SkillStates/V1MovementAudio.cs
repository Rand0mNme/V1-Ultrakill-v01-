using RoR2;
using UnityEngine;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.SkillStates
{
    // ULTRAKILL movement sounds (NewMovement, PlayerFootsteps, SlideScrape, WallScrape, wind sound on WallCheck).
    // Continuous sounds (footsteps, falling wind, slide and wall-slide scraping, landing) live here,
    // one-shots (dash, jumps, slide stop) are triggered by the states through static methods.
    public class V1MovementAudio : MonoBehaviour
    {
        private CharacterBody body;
        private V1MovementData data;
        private SoundHandle woosh;
        private SoundHandle slideScrape;
        private SoundHandle wallScrape;
        private float footstepTimer = 1f;
        private int lastFootstep = -1;
        private bool wasGrounded = true;
        private float fallSpeed;

        public bool clinging; // set by V1CharacterMain while V1 slides down a wall

        private bool Local => body && body.hasEffectiveAuthority;

        private void Start()
        {
            body = GetComponent<CharacterBody>();
            data = V1MovementData.Get(gameObject);
        }

        private void Update()
        {
            if (!body || !body.characterMotor)
            {
                return;
            }
            CharacterMotor motor = body.characterMotor;
            bool grounded = motor.isGrounded;
            Vector3 v = motor.velocity;

            // footsteps: timer 1 decreases by speed/15 x 3 per second
            bool walking = grounded && body.inputBank && body.inputBank.moveVector.sqrMagnitude > 0.01f && !data.sliding && !data.dashing;
            if (walking)
            {
                footstepTimer = Mathf.MoveTowards(footstepTimer, 0f, Mathf.Min(v.magnitude, 15f) / 15f * Time.deltaTime * 3f);
            }
            if (footstepTimer <= 0f)
            {
                Footstep(0.25f);
            }

            // falling wind: pitch |speed| / 120, volume |speed| / 80
            if (!grounded && v.y < -2f)
            {
                fallSpeed = v.y;
            }
            if (woosh == null)
            {
                woosh = V1Sounds.Play("Whoosh", gameObject, 0f, 0f, Local, loop: true);
            }
            if (woosh != null)
            {
                if (!grounded && v.y < -20f)
                {
                    woosh.Pitch = Mathf.Abs(fallSpeed) / 120f;
                    woosh.Volume = Mathf.Abs(fallSpeed) / 80f;
                }
                else
                {
                    woosh.Pitch = 0f;
                    woosh.Volume = 0f;
                }
            }

            // slide scraping on the ground
            Loop(ref slideScrape, "Scrape", 1.8f, data.sliding && grounded);
            // wall slide
            Loop(ref wallScrape, "Scrape", 0.7f, clinging && !grounded);
            clinging = false;

            // landing: normal or heavy (faster than 50 m/s)
            if (grounded && !wasGrounded)
            {
                if (fallSpeed > -50f)
                {
                    V1Sounds.Play("Landing", gameObject, 0.5f + fallSpeed * -0.01f, Random.Range(0.9f, 1.1f), Local);
                    Footstep(0.5f);
                }
                else
                {
                    V1Sounds.Play("LandingHeavy", gameObject, 0.9f, 1f, Local);
                    V1MovementFx.HeavyLanding(gameObject);
                    Footstep(1f);
                }
                fallSpeed = 0f;
            }
            wasGrounded = grounded;
        }

        private void Loop(ref SoundHandle handle, string clip, float volume, bool on)
        {
            if (on && handle == null)
            {
                handle = V1Sounds.Play(clip, gameObject, volume, 1f, Local, loop: true);
            }
            else if (!on && handle != null)
            {
                handle.Stop();
                handle = null;
            }
        }

        public void Footstep(float volume)
        {
            footstepTimer = 1f;
            int n = Random.Range(0, 4);
            if (n == lastFootstep)
            {
                n = (n + 1) % 4;
            }
            lastFootstep = n;
            V1Sounds.Play("Footstep" + (n + 1), gameObject, volume, 1f, Local);
        }

        private void OnDestroy()
        {
            woosh?.Stop();
            slideScrape?.Stop();
            wallScrape?.Stop();
        }

        public static V1MovementAudio Get(GameObject go)
        {
            V1MovementAudio a = go.GetComponent<V1MovementAudio>();
            return a ? a : go.AddComponent<V1MovementAudio>();
        }

        // --- one-shot sounds ---

        private static bool IsLocal(GameObject go)
        {
            CharacterBody b = go.GetComponent<CharacterBody>();
            return b && b.hasEffectiveAuthority;
        }

        // TryDash: aud.clip = dodgeSound, volume 1, pitch 1
        public static void Dash(GameObject go) => V1Sounds.Play("Dodge3", go, 1f, 1f, IsLocal(go));

        // out of stamina: StaminaFail (volume 0.6, pitch 0.5)
        public static void StaminaFail(GameObject go) => V1Sounds.Play("StaminaFail", go, 0.6f, 0.5f, IsLocal(go));

        // Jump: volume 0.75, pitch 1 (after a ground slam: 0.85 and 2)
        public static void Jump(GameObject go, bool super = false) => V1Sounds.Play("Jump", go, super ? 0.85f : 0.75f, super ? 2f : 1f, IsLocal(go));

        // dash jump: normal jump + DashJumpSound (volume 1, pitch 1.5)
        public static void DashJump(GameObject go)
        {
            Jump(go);
            V1Sounds.Play("DashJump", go, 1f, 1.5f, IsLocal(go));
        }

        // WallJump: pitch 1 + 0.25 x jump number; on the third also FinalWallJump; plus a footstep at volume 0.5
        public static void WallJump(GameObject go, int count)
        {
            V1Sounds.Play("Jump", go, 0.75f, 1f + 0.25f * count, IsLocal(go));
            if (count == 3)
            {
                V1Sounds.Play("FinalWallJump", go, 0.75f, 1f, IsLocal(go));
            }
            Get(go).Footstep(0.5f);
        }

        // StopSlide: SlideStopSound (volume 0.5, pitch 1.5)
        public static void SlideStop(GameObject go) => V1Sounds.Play("SlideStop", go, 0.5f, 1.5f, IsLocal(go));
    }
}
