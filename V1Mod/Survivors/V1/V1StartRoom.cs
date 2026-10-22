using System.Collections.Generic;
using RoR2;
using UnityEngine;
using V1Mod.Modules;
using V1Mod.Survivors.V1.SkillStates;

namespace V1Mod.Survivors.V1
{
    // Start room like at the start of an ULTRAKILL level (FirstRoom): V1 falls down a shaft into the red room
    // with the SmileOS shop, buys gear and walks down the hallway to the door; behind the door is the drop onto the stage.
    // The room hangs high above the spawn point and exists only for this player (not networked), like the shop.
    // Coordinates below are in ULTRAKILL FirstRoom space (the room prefab root is rotated as it is there).
    public class V1StartRoom : MonoBehaviour
    {
        public const float Height = 1000f;
        private static readonly Vector3 PlayerStart = new Vector3(0f, 105f, -47f);
        // the FinalDoor sits at z = 0: it opens when you approach, behind it is the exit onto the map
        private const float DoorZ = 0f;
        private const float DoorOpenDistance = 10f;
        private const float ExitZ = 0.5f;
        // the room's steps and ledges are higher than RoR2's step (0.5 m): inside the room V1 steps up to 1.5 m
        private const float RoomStepHeight = 1.5f;
        private float savedStep = -1f;
        private const float FallOutY = -30f;

        private static readonly Dictionary<CharacterBody, V1StartRoom> rooms = new Dictionary<CharacterBody, V1StartRoom>();

        public CharacterBody owner;
        public Vector3 stageSpawn;
        private Quaternion yaw;
        private Transform doorLeft;
        private Transform doorRight;
        private Vector3 doorLeftClosed;
        private Vector3 doorRightClosed;
        private float doorWidth;
        private float doorOpen;

        public Vector3 ToWorld(Vector3 p) => transform.position + yaw * p;
        public Vector3 ToRoom(Vector3 w) => Quaternion.Inverse(yaw) * (w - transform.position);

        // create the room above the spawn point and return where to put V1 (the top of the shaft)
        public static bool TryCreate(CharacterBody body, out Vector3 start)
        {
            start = Vector3.zero;
            if (!body || !V1Assets.firstRoom)
            {
                return false;
            }
            Remove(body);
            Vector3 forward = body.characterDirection ? body.characterDirection.forward : body.transform.forward;
            forward.y = 0f;
            Quaternion yaw = Quaternion.LookRotation(forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward);
            Vector3 pos = body.footPosition + Vector3.up * Height;
            GameObject go = Object.Instantiate(V1Assets.firstRoom, pos, yaw * V1Assets.firstRoom.transform.rotation);
            V1Assets.ToWorldMaterials(go);
            V1StartRoom room = go.AddComponent<V1StartRoom>();
            room.owner = body;
            room.stageSpawn = body.footPosition;
            room.yaw = yaw;
            // walls, floor and shaft are world (collisions, wall jumps, slide)
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            {
                // ULTRAKILL triggers (script zones) aren't needed here and would block weapon rays
                if (c.isTrigger)
                {
                    c.enabled = false;
                    continue;
                }
                c.gameObject.layer = LayerIndex.world.intVal;
            }
            // SmileOS machine in the room
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Shop")
                {
                    t.gameObject.AddComponent<Weapons.V1ShopTerminal>().owner = body;
                    break;
                }
            }
            room.FindDoor();
            room.SetStep(true);
            rooms[body] = room;
            start = room.ToWorld(PlayerStart);
            Log.Info("Start room created above " + room.stageSpawn + ", rotation " + yaw.eulerAngles.y.ToString("0"));
            return true;
        }

        private void SetStep(bool room)
        {
            var motor = owner && owner.characterMotor ? owner.characterMotor.Motor : null;
            if (!motor)
            {
                return;
            }
            if (room && savedStep < 0f)
            {
                savedStep = motor.MaxStepHeight;
                motor.MaxStepHeight = RoomStepHeight;
            }
            else if (!room && savedStep >= 0f)
            {
                motor.MaxStepHeight = savedStep;
                savedStep = -1f;
            }
        }

        private void OnDestroy()
        {
            SetStep(false);
        }

        private void FindDoor()
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "DoorLeft") doorLeft = t;
                else if (t.name == "DoorRight") doorRight = t;
            }
            if (!doorLeft || !doorRight)
            {
                return;
            }
            // the left door leaf is the one further left in room space
            if (ToRoom(doorLeft.position).x > ToRoom(doorRight.position).x)
            {
                Transform tmp = doorLeft;
                doorLeft = doorRight;
                doorRight = tmp;
            }
            doorLeftClosed = doorLeft.position;
            doorRightClosed = doorRight.position;
            Renderer r = doorLeft.GetComponentInChildren<Renderer>();
            doorWidth = r ? Mathf.Max(1f, Vector3.Distance(doorLeftClosed, doorRightClosed), r.bounds.size.x, r.bounds.size.z) : 3f;
        }

        // On stages after a teleporter RoR2 doesn't run the "first spawn" (drop pod), it just puts the player on the spot.
        // For V1 we run the same spawn through the room. Not in the Bazaar (Bazaar Between Time).
        public static void Init()
        {
            new HarmonyLib.Harmony(V1Plugin.MODUID + ".startroom").PatchAll(typeof(RespawnPatch));
        }

        [HarmonyLib.HarmonyPatch(typeof(Stage), nameof(Stage.RespawnCharacter))]
        public static class RespawnPatch
        {
            public static void Postfix(Stage __instance, CharacterMaster characterMaster)
            {
                if (!characterMaster || __instance.usePod)
                {
                    return;
                }
                SceneDef scene = SceneCatalog.GetSceneDefForCurrentScene();
                if (scene && scene.cachedName == "bazaar")
                {
                    return;
                }
                CharacterBody body = characterMaster.GetBody();
                if (body && body.bodyIndex == BodyCatalog.FindBodyIndex(V1Survivor.bodyPrefab))
                {
                    body.SetBodyStateToPreferredInitialState();
                }
            }
        }

        public static void Remove(CharacterBody body)
        {
            if (body && rooms.TryGetValue(body, out V1StartRoom old))
            {
                rooms.Remove(body);
                if (old)
                {
                    Destroy(old.gameObject);
                }
            }
        }

        private void Update()
        {
            if (!owner || !owner.healthComponent || !owner.healthComponent.alive)
            {
                Destroy(gameObject);
                return;
            }
            if (!owner.hasEffectiveAuthority)
            {
                return;
            }
            Vector3 p = ToRoom(owner.footPosition);
            // the door slides apart when V1 approaches (in ULTRAKILL it opens by itself once the level has loaded)
            if (doorLeft && doorRight)
            {
                float target = DoorZ - p.z < DoorOpenDistance ? 1f : 0f;
                if (target > 0f && doorOpen == 0f)
                {
                    V1Sounds.Play("ShopDrawerOpen", owner.gameObject, 0.6f, 0.6f, false);
                }
                doorOpen = Mathf.MoveTowards(doorOpen, target, Time.deltaTime * 2f);
                Vector3 side = yaw * Vector3.right * doorWidth * Mathf.SmoothStep(0f, 1f, doorOpen);
                doorLeft.position = doorLeftClosed - side;
                doorRight.position = doorRightClosed + side;
            }
            if (p.z > ExitZ || p.y < FallOutY)
            {
                Exit();
            }
        }

        // behind the door: drop onto the map from above the spawn point (V1SpawnFall without the room)
        private void Exit()
        {
            CharacterBody body = owner;
            rooms.Remove(body);
            EntityStateMachine machine = EntityStateMachine.FindByCustomName(body.gameObject, "Body");
            if (machine)
            {
                machine.SetNextState(new V1SpawnFall { skipRoom = true, dropFrom = stageSpawn });
            }
            Destroy(gameObject);
        }
    }
}
