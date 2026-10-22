using HarmonyLib;
using HG.BlendableTypes;
using RoR2;
using System.Reflection;
using UnityEngine;
using V1Mod.Survivors.V1.SkillStates;

namespace V1Mod.Survivors.V1
{
    // First-person view as in ULTRAKILL (CameraController):
    // - camera 1.4 m above the player's center, i.e. 2.9 m above the feet (capsule 0.5 x 3.5)
    // - FOV 105 (the default fieldOfView)
    // - forward dash/slide: FOV -1/20, backwards: +1/10, then it returns at 300 per second
    // - camera tilt when strafing (cameraTilt is on by default), 5 times stronger while dashing
    // - walking bob: the camera dips by 0.1 m and comes back
    public static class V1Camera
    {
        public const float CapsuleRadius = 0.5f;
        public const float CapsuleHeight = 3.5f;
        public const float EyeAboveFeet = 2.9f;
        public const float DefaultFov = 105f;

        public static void Init(GameObject bodyPrefab)
        {
            SetupCapsule(bodyPrefab, out float feetY);

            Transform pivot = new GameObject("V1CameraPivot").transform;
            pivot.SetParent(bodyPrefab.transform, false);
            pivot.localPosition = new Vector3(0f, feetY + EyeAboveFeet, 0f);

            CharacterCameraParams cameraParams = ScriptableObject.CreateInstance<CharacterCameraParams>();
            (cameraParams as ScriptableObject).name = "ccpV1FirstPerson";
            cameraParams.data.minPitch = -89.9f;
            cameraParams.data.maxPitch = 89.9f;
            cameraParams.data.wallCushion = 0.1f;
            cameraParams.data.pivotVerticalOffset = 0f;
            cameraParams.data.idealLocalCameraPos = new BlendableVector3 { value = Vector3.zero, alpha = 1f };
            cameraParams.data.fov = DefaultFov;
            cameraParams.data.isFirstPerson = new BlendableBool { value = true, alpha = 1f };

            CameraTargetParams targetParams = bodyPrefab.GetComponent<CameraTargetParams>();
            targetParams.cameraParams = cameraParams;
            targetParams.cameraPivotTransform = pivot;

            bodyPrefab.AddComponent<V1CameraEffects>().pivot = pivot;

            new Harmony(V1Plugin.MODUID + ".camera").PatchAll(typeof(CameraPatch));
        }

        // ULTRAKILL player capsule: radius 0.5, height 3.5. The feet stay where they were.
        private static void SetupCapsule(GameObject bodyPrefab, out float feetY)
        {
            var motor = bodyPrefab.GetComponent<KinematicCharacterController.KinematicCharacterMotor>();
            FieldInfo radiusField = typeof(KinematicCharacterController.KinematicCharacterMotor).GetField("CapsuleRadius", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo heightField = typeof(KinematicCharacterController.KinematicCharacterMotor).GetField("CapsuleHeight", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo offsetField = typeof(KinematicCharacterController.KinematicCharacterMotor).GetField("CapsuleYOffset", BindingFlags.NonPublic | BindingFlags.Instance);

            float oldHeight = (float)heightField.GetValue(motor);
            float oldOffset = (float)offsetField.GetValue(motor);
            feetY = oldOffset - oldHeight * 0.5f;
            float newOffset = feetY + CapsuleHeight * 0.5f;

            radiusField.SetValue(motor, CapsuleRadius);
            heightField.SetValue(motor, CapsuleHeight);
            offsetField.SetValue(motor, newOffset);

            CapsuleCollider capsule = bodyPrefab.GetComponent<CapsuleCollider>();
            if (capsule)
            {
                capsule.radius = CapsuleRadius;
                capsule.height = CapsuleHeight;
                capsule.center = new Vector3(0f, newOffset, 0f);
            }
        }

        [HarmonyPatch(typeof(CameraRigController), nameof(CameraRigController.SetCameraState))]
        private static class CameraPatch
        {
            private static void Prefix(CameraRigController __instance, ref CameraState cameraState)
            {
                CharacterBody body = __instance.targetBody;
                if (!body || __instance.localUserViewer == null || body.currentVehicle)
                {
                    return;
                }
                V1CameraEffects fx = body.GetComponent<V1CameraEffects>();
                if (!fx)
                {
                    return;
                }
                cameraState.fov = fx.fov;
                cameraState.rotation = cameraState.rotation * Quaternion.AngleAxis(fx.tilt, Vector3.forward);
            }
        }
    }

    public class V1CameraEffects : MonoBehaviour
    {
        public Transform pivot;
        public float fov = V1Camera.DefaultFov;
        public float tilt;
        // zoom (railcannon RMB: CameraController.Zoom(defaultFov / 2))
        public bool zooming;

        private float tiltSmooth;
        private Vector3 basePos;
        private Vector3 defaultPos;
        private Vector3 targetPos;
        private CharacterBody body;
        private InputBankTest inputBank;
        private V1MovementData data;

        private void Start()
        {
            body = GetComponent<CharacterBody>();
            inputBank = GetComponent<InputBankTest>();
            data = V1MovementData.Get(gameObject);
            if (pivot)
            {
                basePos = pivot.localPosition;
                defaultPos = basePos;
                targetPos = defaultPos - new Vector3(0f, 0.1f, 0f);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !body || !inputBank)
            {
                return;
            }
            bool boost = data.dashing || data.sliding;

            // FOV
            if (boost)
            {
                if (data.dodgeDirectionType == 0)
                {
                    fov = V1Camera.DefaultFov - V1Camera.DefaultFov / 20f;
                }
                else if (data.dodgeDirectionType == 1)
                {
                    fov = V1Camera.DefaultFov + V1Camera.DefaultFov / 10f;
                }
            }
            else
            {
                fov = Mathf.MoveTowards(fov, zooming ? V1Camera.DefaultFov / 2f : V1Camera.DefaultFov, dt * 300f);
            }

            // tilt
            Vector3 forward = V1CharacterMain.Flat(inputBank.aimDirection);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float movementHor = Mathf.Clamp(Vector3.Dot(inputBank.moveVector, right), -1f, 1f);
            tilt = Mathf.SmoothDampAngle(tilt, 0f, ref tiltSmooth, 0.5f);
            float current = tilt > 180f ? tilt - 360f : tilt;
            float target = -movementHor;
            tilt = boost
                ? Mathf.MoveTowards(current, target * 5f, dt * 100f * (Mathf.Abs(current - target * 5f) + 0.01f))
                : Mathf.MoveTowards(current, target, dt * 25f * (Mathf.Abs(current - target) + 0.01f));

            // walking bob and lowering the camera while sliding (by 0.625 m)
            if (!pivot)
            {
                return;
            }
            Vector3 defaultTarget = basePos - (data.sliding ? new Vector3(0f, 0.625f, 0f) : Vector3.zero);
            if (defaultPos != defaultTarget)
            {
                defaultPos = Vector3.MoveTowards(defaultPos, defaultTarget, ((defaultTarget - defaultPos).magnitude + 0.5f) * dt * 10f);
            }
            CharacterMotor motor = body.characterMotor;
            bool walking = motor && motor.isGrounded && inputBank.moveVector.sqrMagnitude > 0.01f;
            if (walking && !boost && defaultPos == defaultTarget)
            {
                Vector3 p = pivot.localPosition;
                float speedFactor = Mathf.Min(motor.velocity.magnitude, 15f) / 15f;
                p = new Vector3(
                    Mathf.MoveTowards(p.x, targetPos.x, dt * 0.5f),
                    Mathf.MoveTowards(p.y, targetPos.y, dt * 0.5f * speedFactor),
                    Mathf.MoveTowards(p.z, targetPos.z, dt * 0.5f));
                pivot.localPosition = p;
                if (p == targetPos)
                {
                    targetPos = targetPos != defaultPos ? defaultPos : defaultPos - new Vector3(0f, 0.1f, 0f);
                }
            }
            else
            {
                pivot.localPosition = defaultPos;
                targetPos = defaultPos - new Vector3(0f, 0.1f, 0f);
            }
        }
    }
}
