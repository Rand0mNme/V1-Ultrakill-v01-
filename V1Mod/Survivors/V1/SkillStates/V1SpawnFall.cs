using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace V1Mod.Survivors.V1.SkillStates
{
    // Spawning on a stage like at the start of an ULTRAKILL level: no drop pod.
    // 1) V1 falls down a shaft into the start room with the SmileOS shop (V1StartRoom, high above the map).
    // 2) Behind the door at the end of the hallway this state runs again (skipRoom): V1 appears above the spawn point
    //    (up to 200 m: as much as the ceiling and the map bounds allow) and falls onto the map.
    // Controls work in flight (this is a normal V1 state), no damage until landing.
    // If there is no room (it didn't load), the shop terminal is placed next to the landing spot.
    public class V1SpawnFall : V1CharacterMain
    {
        // drop height onto the map after the room; capped by the ceiling and the map bounds (MapZone)
        public const float DropHeight = 200f;
        public const float MaxDuration = 20f;

        public bool skipRoom;
        public Vector3? dropFrom;
        private bool inRoom;

        public override void OnEnter()
        {
            base.OnEnter();
            if (NetworkServer.active)
            {
                characterBody.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, MaxDuration);
            }
            if (isAuthority)
            {
                V1StyleHud.Get(characterBody);
            }
            if (!isAuthority || !characterMotor)
            {
                return;
            }
            if (!skipRoom && V1StartRoom.TryCreate(characterBody, out Vector3 roomStart))
            {
                inRoom = true;
                Teleport(roomStart);
                return;
            }
            Vector3 start = dropFrom ?? characterBody.footPosition;
            Teleport(FindDropPoint(start));
        }

        // A point high in the sky above the spawn: search nearby (up to 40 m) for a column with open sky overhead
        // and rise by DropHeight, but not above the ceiling and not past the top of the map bounds "box" (MapZone TriggerExit),
        // otherwise RoR2 would put V1 straight back on the ground. Under a roof (caves), as much as the ceiling allows.
        private Vector3 FindDropPoint(Vector3 start)
        {
            float bodyHeight = characterBody.corePosition.y - characterBody.footPosition.y;
            Vector3 best = start;
            float bestHeight = -1f;
            for (int ring = 0; ring <= 4 && bestHeight < DropHeight * 0.75f; ring++)
            {
                int samples = ring == 0 ? 1 : 8;
                for (int i = 0; i < samples; i++)
                {
                    float a = i * Mathf.PI * 2f / samples;
                    Vector3 p = start + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring * 10f;
                    float h = ClearHeight(p, bodyHeight);
                    if (h > bestHeight)
                    {
                        bestHeight = h;
                        best = p;
                    }
                }
            }
            Log.Info("Drop onto the map: height " + bestHeight.ToString("0") + " m above " + best);
            return best + Vector3.up * Mathf.Max(bestHeight, 0.5f);
        }

        private float ClearHeight(Vector3 foot, float bodyHeight)
        {
            float height = DropHeight;
            Vector3 core = foot + Vector3.up * bodyHeight;
            if (Physics.SphereCast(core, characterBody.radius, Vector3.up, out RaycastHit hit, DropHeight + 2f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                height = hit.distance - 2f;
            }
            foreach (MapZone zone in Object.FindObjectsOfType<MapZone>())
            {
                Collider c = zone.GetComponent<Collider>();
                if (!c || zone.zoneType != MapZone.ZoneType.OutOfBounds)
                {
                    continue;
                }
                Bounds b = c.bounds;
                bool column = foot.x > b.min.x && foot.x < b.max.x && foot.z > b.min.z && foot.z < b.max.z;
                if (zone.triggerType == MapZone.TriggerType.TriggerExit && column && b.max.y > foot.y)
                {
                    // map bounds: stay inside the box with a margin
                    height = Mathf.Min(height, b.max.y - foot.y - bodyHeight * 2f - 5f);
                }
                else if (zone.triggerType == MapZone.TriggerType.TriggerEnter && column && b.min.y > foot.y)
                {
                    // out-of-bounds zone above the map: stay below it
                    height = Mathf.Min(height, b.min.y - foot.y - bodyHeight * 2f - 5f);
                }
            }
            return height;
        }

        private void Teleport(Vector3 foot)
        {
            TeleportHelper.TeleportBody(characterBody, foot, false);
            characterMotor.velocity = Vector3.zero;
            characterMotor.Motor.ForceUnground();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority || outer.state != this)
            {
                return;
            }
            if ((fixedAge > 0.3f && characterMotor && characterMotor.isGrounded) || fixedAge > MaxDuration)
            {
                if (!inRoom && !skipRoom)
                {
                    // no room: SmileOS shop terminal next to the landing spot
                    Weapons.V1Shop.SpawnTerminal(characterBody);
                }
                outer.SetNextStateToMain();
            }
        }

        public override void OnExit()
        {
            if (NetworkServer.active && characterBody)
            {
                characterBody.ClearTimedBuffs(RoR2Content.Buffs.HiddenInvincibility);
            }
            base.OnExit();
        }
    }
}
