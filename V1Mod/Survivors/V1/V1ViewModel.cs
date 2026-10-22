using RoR2;
using UnityEngine;
using V1Mod.Modules;
using V1Mod.Survivors.V1.Weapons;

namespace V1Mod.Survivors.V1
{
    // V1's arm with the revolver in first-person view, as in ULTRAKILL.
    // In ULTRAKILL the weapon is drawn by a separate camera with FOV 90 (hudCamera). A second camera doesn't render in RoR2,
    // so the arm is drawn by the main camera, and to match the FOV 90 picture the arm is stretched
    // in x and y by tan(fov/2) / tan(45). The arm position is the same as the weapon prefab's in ULTRAKILL.
    public class V1ViewModel : MonoBehaviour
    {
        public const float ViewmodelFov = 90f;

        private CharacterBody body;
        private GameObject root;
        private GameObject gun;
        private Animator animator;
        private Transform cylinder;
        private Transform shootPoint;
        private GameObject chargeEffect;
        private MeshRenderer monitor;
        private Vector3 gunDefaultPos;
        private Transform guns;
        // the arms hang on the "Punch" object: 0.225 below the weapon (Player.prefab)
        private Transform fists;

        // WalkingBob (on Guns): while walking the weapon sways left-right by 0.08 and down by 0.025
        private Vector3 bobTarget = new Vector3(0.08f, -0.025f, 0f);
        private bool bobBackToStart;

        // color of the "Piercer" variant (ColorBlindSettings.variationColors[0])
        public static readonly Color PiercerColor = new Color(0.2509804f, 0.9058824f, 1f, 1f);
        // the screen and glow are brighter than the plain color so RoR2 bloom picks them up, as in ULTRAKILL
        public const float ScreenBoost = 1f;

        public static V1ViewModel Get(GameObject go) => go ? go.GetComponent<V1ViewModel>() : null;

        // the beam origin (muzzle) in the world; null if the arm isn't on screen right now
        public Vector3? ShootPointPosition => ShootPoint ? ShootPoint.position : (Vector3?)null;
        public Transform ShootPoint => root && root.activeInHierarchy && shootPoint ? shootPoint : null;

        // muzzles in turn (the nailgun has two)
        public Transform NextShootPoint(int index)
        {
            WeaponView v = currentWeapon >= 0 && currentWeapon < views.Length ? views[currentWeapon] : null;
            if (!root || !root.activeInHierarchy || v == null || v.shootPoints.Count == 0)
            {
                return ShootPoint;
            }
            return v.shootPoints[index % v.shootPoints.Count];
        }

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
        }

        private void LateUpdate()
        {
            Camera sceneCam = FindCamera();
            // don't show the arm in the drop pod (or any other vehicle)
            bool show = sceneCam && body && body.healthComponent && body.healthComponent.alive && !body.currentVehicle && V1Assets.piercerViewmodel;
            SetOwnModelHidden(show);
            if (!show)
            {
                if (root)
                {
                    root.SetActive(false);
                }
                DetachBuffer();
                return;
            }
            if (!root)
            {
                Create();
            }
            root.SetActive(true);
            if (root.transform.parent != sceneCam.transform)
            {
                root.transform.SetParent(sceneCam.transform, false);
            }
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            float k = Mathf.Tan(sceneCam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(ViewmodelFov * 0.5f * Mathf.Deg2Rad);
            root.transform.localScale = new Vector3(k, k, 1f);

            UpdateWalkingBob();
            UpdateArmVisibility();
            if (currentWeapon == WeaponPiercer)
            {
                UpdateChargeEffects();
            }
            else if (currentWeapon == WeaponMarksman || currentWeapon == WeaponSharpshooter)
            {
                UpdateAltScreen();
                if (currentWeapon == WeaponSharpshooter)
                {
                    UpdateTwirl();
                }
            }
            if (IsNailgun(currentWeapon))
            {
                UpdateNailgun();
            }
            if (currentWeapon == WeaponShotgun || currentWeapon == WeaponShotgunPump || currentWeapon == WeaponShotgunSaw)
            {
                UpdateShotgunScreen();
            }
            else if (barrelSpinSound != null)
            {
                barrelSpinSound.Stop();
                barrelSpinSound = null;
            }
            if (currentWeapon != WeaponPiercer && gun)
            {
                gun.transform.localPosition = gunDefaultPos + new Vector3(Random.Range(-shake, shake), Random.Range(-shake, shake), Random.Range(-shake, shake));
            }
            DrawOnTop(sceneCam);
        }

        private void UpdateWalkingBob()
        {
            Vector3 right = new Vector3(0.08f, -0.025f, 0f);
            Vector3 left = new Vector3(-0.08f, -0.025f, 0f);
            CharacterMotor motor = body.characterMotor;
            InputBankTest input = body.inputBank;
            var move = GetComponent<SkillStates.V1MovementData>();
            bool walking = motor && input && motor.isGrounded && input.moveVector.sqrMagnitude > 0.01f && !(move && (move.sliding || move.dashing));
            Vector3 p = guns.localPosition;
            if (walking)
            {
                float speed = Time.deltaTime * (2f - p.magnitude * 3f) * (Mathf.Min(motor.velocity.magnitude, 15f) / 15f);
                p = Vector3.MoveTowards(p, bobBackToStart ? Vector3.zero : bobTarget, speed * 0.25f);
                if (p == Vector3.zero)
                {
                    bobBackToStart = false;
                }
                else if (p == right)
                {
                    bobBackToStart = true;
                    bobTarget = left;
                }
                else if (p == left)
                {
                    bobBackToStart = true;
                    bobTarget = right;
                }
            }
            else
            {
                p = Vector3.MoveTowards(p, Vector3.zero, Time.deltaTime);
            }
            guns.localPosition = p;
        }

        // The arm is drawn like ULTRAKILL's separate camera: on top of the finished frame, after the fog and all
        // RoR2 post-processing, with its own depth (so it doesn't clip into walls or dim from fog/lighting).
        public const int HiddenLayer = 31;
        private readonly System.Collections.Generic.List<Renderer> drawList = new System.Collections.Generic.List<Renderer>();
        private UnityEngine.Rendering.CommandBuffer buffer;
        private Camera bufferCamera;
        private const UnityEngine.Rendering.CameraEvent DrawEvent = UnityEngine.Rendering.CameraEvent.AfterImageEffects;

        private void DrawOnTop(Camera cam)
        {
            if (buffer == null)
            {
                buffer = new UnityEngine.Rendering.CommandBuffer { name = "V1 viewmodel" };
            }
            if (bufferCamera != cam)
            {
                DetachBuffer();
                cam.AddCommandBuffer(DrawEvent, buffer);
                bufferCamera = cam;
            }
            buffer.Clear();
            // own depth: the weapon is always on top of the world
            buffer.ClearRenderTarget(true, false, Color.clear);
            // opaque first, then transparent (barrel heat glow and the like), otherwise the transparent parts get covered
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (Renderer r in drawList)
                {
                    if (!r || !r.gameObject.activeInHierarchy || !r.enabled)
                    {
                        continue;
                    }
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] && (mats[i].renderQueue >= 3000) == (pass == 1))
                        {
                            buffer.DrawRenderer(r, mats[i], i, 0);
                        }
                    }
                }
            }
        }

        private void DetachBuffer()
        {
            if (bufferCamera && buffer != null)
            {
                bufferCamera.RemoveCommandBuffer(DrawEvent, buffer);
            }
            bufferCamera = null;
        }

        // In ULTRAKILL you can't see your own body. RoR2, however, shows the character model
        // translucent near the camera when looking up, so for the local player the Commando model is hidden while the arm is visible.
        private CharacterModel ownModel;
        private bool ownModelHidden;

        private void SetOwnModelHidden(bool hide)
        {
            if (!body)
            {
                return;
            }
            if (!ownModel && body.modelLocator && body.modelLocator.modelTransform)
            {
                ownModel = body.modelLocator.modelTransform.GetComponent<CharacterModel>();
            }
            if (!ownModel || hide == ownModelHidden)
            {
                return;
            }
            ownModel.invisibilityCount += hide ? 1 : -1;
            ownModelHidden = hide;
        }

        // this player's RoR2 camera that follows this character
        private Camera FindCamera()
        {
            foreach (CameraRigController rig in CameraRigController.readOnlyInstancesList)
            {
                if (rig && rig.localUserViewer != null && rig.target == gameObject && rig.sceneCam)
                {
                    return rig.sceneCam;
                }
            }
            return null;
        }

        private void Create()
        {
            root = new GameObject("V1Viewmodel");
            guns = new GameObject("Guns").transform;
            guns.SetParent(root.transform, false);
            fists = new GameObject("Punch").transform;
            fists.SetParent(root.transform, false);
            fists.localPosition = new Vector3(0f, -0.225f, 0f);

            // all weapon models are created at once, one is visible (like GunControl in ULTRAKILL)
            piercerView = SetupWeapon(V1Assets.piercerViewmodel, "ShootPoint");
            views[WeaponPiercer] = piercerView;
            views[WeaponShotgun] = SetupWeapon(V1Assets.shotgunCoreViewmodel, "ShootPoint R");
            views[WeaponShotgunPump] = SetupWeapon(V1Assets.shotgunPumpViewmodel, "ShootPoint R");
            views[WeaponShotgunSaw] = SetupWeapon(V1Assets.shotgunSawViewmodel, "ShootPointR");
            views[WeaponNailMagnet] = SetupWeapon(V1Assets.nailgunMagnetViewmodel, "ShootPoint");
            views[WeaponNailOverheat] = SetupWeapon(V1Assets.nailgunOverheatViewmodel, "ShootPoint");
            views[WeaponNailZapper] = SetupWeapon(V1Assets.nailgunZapperViewmodel, "ShootPoint");
            views[WeaponSawMagnet] = SetupWeapon(V1Assets.sawLauncherMagnetViewmodel, "Shootpoint");
            views[WeaponSawOverheat] = SetupWeapon(V1Assets.sawLauncherOverheatViewmodel, "Shootpoint");
            views[WeaponSawZapper] = SetupWeapon(V1Assets.sawLauncherZapperViewmodel, "Shootpoint");
            views[WeaponRailElectric] = SetupWeapon(V1Assets.railElectricViewmodel, "Shootpoint");
            views[WeaponRailScrewdriver] = SetupWeapon(V1Assets.railHarpoonViewmodel, "Shootpoint");
            views[WeaponRailMalicious] = SetupWeapon(V1Assets.railMaliciousViewmodel, "Shootpoint");
            views[WeaponRocketFreeze] = SetupWeapon(V1Assets.rocketFreezeViewmodel, "Shootpoint");
            views[WeaponRocketCannonball] = SetupWeapon(V1Assets.rocketCannonballViewmodel, "Shootpoint");
            views[WeaponRocketNapalm] = SetupWeapon(V1Assets.rocketNapalmViewmodel, "Shootpoint");
            views[WeaponMarksman] = SetupWeapon(V1Assets.marksmanViewmodel, "ShootPoint");
            views[WeaponSharpshooter] = SetupWeapon(V1Assets.sharpshooterViewmodel, "ShootPoint");
            SetupAltRevolver(WeaponMarksman, MarksmanColor);
            SetupAltRevolver(WeaponSharpshooter, SharpshooterColor);
            SetupShotgunScreen(WeaponShotgun);
            SetupShotgunScreen(WeaponShotgunPump);
            SetupShotgunScreen(WeaponShotgunSaw);
            SetupNailScreen(WeaponNailMagnet);
            SetupNailScreen(WeaponNailOverheat);
            SetupNailScreen(WeaponNailZapper);
            SetupNailScreen(WeaponSawMagnet);
            SetupNailScreen(WeaponSawOverheat);
            SetupNailScreen(WeaponSawZapper);
            arms[ArmFeedbacker] = SetupWeapon(V1Assets.armFeedbacker, "Hand", fists);
            arms[ArmKnuckleblaster] = SetupWeapon(V1Assets.armKnuckleblaster, "Hand", fists);
            whipView = SetupWeapon(V1Assets.armWhiplash, "hand", fists);
            if (whipView != null)
            {
                // the hook's rope and head fly through the world and are drawn by the hook itself (WhiplashThrow), hidden in the hand
                foreach (LineRenderer lr in whipView.go.GetComponentsInChildren<LineRenderer>(true))
                {
                    if (!WhipWireMaterial)
                    {
                        WhipWireMaterial = lr.sharedMaterial;
                    }
                    lr.enabled = false;
                }
                foreach (string part in new[] { "Hook", "Hook1", "WireEnd" })
                {
                    Transform t = FindDeep(whipView.go.transform, part);
                    if (t)
                    {
                        foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                        {
                            r.enabled = false;
                        }
                    }
                }
                whipView.go.SetActive(false);
            }
            if (views[WeaponShotgunSaw] != null)
            {
                // saw sparks and break shards are turned on by a script on an event in ULTRAKILL, not right away
                foreach (ParticleSystem ps in views[WeaponShotgunSaw].go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = ps.main;
                    main.playOnAwake = false;
                }
            }

            if (piercerView != null)
            {
                Transform pg = piercerView.go.transform;
                cylinder = FindDeep(pg, "Cylinder_Bone");
                Transform ce = FindDeep(pg, "ChargeEffect");
                chargeEffect = ce ? ce.gameObject : null;
                Transform mon = FindDeep(pg, "Monitor");
                monitor = mon ? mon.GetComponent<MeshRenderer>() : null;
                InitCylinder();
                // WeaponIcon.variationColoredRenderers: the cylinder glows in the variant color (_EmissiveColor = Piercer light blue)
                Transform cyl = FindDeep(pg, "Revolver_Cylinder");
                Renderer cylRenderer = cyl ? cyl.GetComponent<Renderer>() : null;
                if (cylRenderer)
                {
                    cylRenderer.material.SetColor("_EmissionColor", PiercerColor * ScreenBoost);
                }
            }
            ApplyWeapon();
            Log.Info("V1 arm created, weapons: revolver " + (piercerView != null) + ", shotgun " + (views[WeaponShotgun] != null) + ", pump " + (views[WeaponShotgunPump] != null));
        }

        // --- several weapon views ---
        public class WeaponView
        {
            public GameObject go;
            public Animator animator;
            public Transform shootPoint;
            public readonly System.Collections.Generic.List<Transform> shootPoints = new System.Collections.Generic.List<Transform>();
            public readonly System.Collections.Generic.Dictionary<string, Transform> parts = new System.Collections.Generic.Dictionary<string, Transform>();
            public readonly System.Collections.Generic.Dictionary<Transform, Quaternion> baseRotations = new System.Collections.Generic.Dictionary<Transform, Quaternion>();
            public Vector3 defaultPos;
            public readonly System.Collections.Generic.List<Renderer> renderers = new System.Collections.Generic.List<Renderer>();
        }

        public const int WeaponPiercer = 0;
        public const int WeaponShotgun = 1;
        public const int WeaponShotgunPump = 2;
        public const int WeaponShotgunSaw = 3;
        public const int WeaponNailMagnet = 4;
        public const int WeaponNailOverheat = 5;
        public const int WeaponNailZapper = 6;
        public const int WeaponRailElectric = 7;
        public const int WeaponRailScrewdriver = 8;
        public const int WeaponRailMalicious = 9;
        public const int WeaponRocketFreeze = 10;
        public const int WeaponRocketCannonball = 11;
        public const int WeaponRocketNapalm = 12;
        public const int WeaponMarksman = 13;
        public const int WeaponSharpshooter = 14;
        // sawblade launchers (alternate nailguns)
        public const int WeaponSawMagnet = 15;
        public const int WeaponSawOverheat = 16;
        public const int WeaponSawZapper = 17;
        public const int WeaponViewCount = 18;

        public static bool IsNailgun(int w) => (w >= WeaponNailMagnet && w <= WeaponNailZapper) || IsSawLauncher(w);
        public static bool IsSawLauncher(int w) => w >= WeaponSawMagnet && w <= WeaponSawZapper;
        // 0 Attractor, 1 Overheat, 2 JumpStart
        public static int NailVariant(int w) => IsSawLauncher(w) ? w - WeaponSawMagnet : w - WeaponNailMagnet;
        private WeaponView piercerView;
        private readonly WeaponView[] views = new WeaponView[WeaponViewCount];
        private int currentWeapon = WeaponPiercer;
        private float shake;

        private WeaponView SetupWeapon(GameObject prefab, string shootPointName, Transform parent = null)
        {
            if (!prefab)
            {
                return null;
            }
            var v = new WeaponView();
            v.go = Instantiate(prefab, parent ? parent : guns, false);
            Transform src = prefab.transform;
            v.go.transform.localPosition = src.localPosition;
            v.go.transform.localRotation = src.localRotation;
            v.go.transform.localScale = src.localScale;
            v.defaultPos = src.localPosition;
            // in ULTRAKILL a weapon lies disabled until selected: only the root is enabled,
            // parts disabled inside it (the spare Arm2 on the Feedbacker, etc.) stay disabled
            v.go.SetActive(true);
            foreach (Transform t in v.go.GetComponentsInChildren<Transform>(true))
            {
                // a layer the main camera doesn't render: the weapon is drawn by a command buffer on top of the frame (see DrawOnTop)
                t.gameObject.layer = HiddenLayer;
            }
            foreach (Renderer r in v.go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.allowOcclusionWhenDynamic = false;
                if (r is SkinnedMeshRenderer smr)
                {
                    // the main camera doesn't see the weapon, but the bones still have to move
                    smr.updateWhenOffscreen = true;
                }
                if (r is MeshRenderer || r is SkinnedMeshRenderer)
                {
                    v.renderers.Add(r);
                }
                else
                {
                    // particles are drawn by the game itself
                    r.gameObject.layer = 0;
                }
            }
            // an animator with a controller (some models have empty animators)
            foreach (Animator a in v.go.GetComponentsInChildren<Animator>(true))
            {
                if (a.runtimeAnimatorController && (!v.animator || !v.animator.runtimeAnimatorController))
                {
                    v.animator = a;
                }
                else if (!v.animator)
                {
                    v.animator = a;
                }
            }
            if (v.animator)
            {
                v.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            v.shootPoint = FindDeep(v.go.transform, shootPointName);
            // several muzzles: "ShootPoint", "ShootPoint (1)", ...
            foreach (Transform t in v.go.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == shootPointName || t.name.StartsWith(shootPointName + " ("))
                {
                    v.shootPoints.Add(t);
                }
            }
            return v;
        }

        // GunControl: only the selected weapon is shown
        public void SetWeapon(int weapon)
        {
            nailSinkFill = -1f;
            currentWeapon = weapon;
            ApplyWeapon();
        }

        private void ApplyWeapon()
        {
            WeaponView v = currentWeapon >= 0 && currentWeapon < views.Length ? views[currentWeapon] : null;
            foreach (WeaponView w in views)
            {
                if (w != null)
                {
                    w.go.SetActive(w == v);
                }
            }
            WeaponView arm = whipVisible ? null : currentArm >= 0 && currentArm < arms.Length ? arms[currentArm] : null;
            foreach (WeaponView a in arms)
            {
                if (a != null)
                {
                    a.go.SetActive(a == arm);
                }
            }
            drawList.Clear();
            if (whipView != null)
            {
                whipView.go.SetActive(whipVisible);
                if (whipVisible)
                {
                    drawList.AddRange(whipView.renderers);
                }
            }
            // the blue arm is visible only during a punch (the red one handles this in its animation: at rest it is off screen)
            if (arm != null && (currentArm != ArmFeedbacker || armShown))
            {
                drawList.AddRange(arm.renderers);
            }
            if (v == null)
            {
                return;
            }
            drawList.AddRange(v.renderers);
            gun = v.go;
            animator = v.animator;
            shootPoint = v.shootPoint;
            gunDefaultPos = v.defaultPos;
        }

        // --- arms (the ULTRAKILL left arm is always visible, next to the weapon) ---
        public const int ArmFeedbacker = 0;
        public const int ArmKnuckleblaster = 1;
        private readonly WeaponView[] arms = new WeaponView[2];
        private int currentArm = ArmFeedbacker;


        // --- Whiplash arm: visible while the hook is in use (HookArm.model) ---
        private WeaponView whipView;
        // hook origin: the hand of the on-screen Whiplash arm (the arm is drawn over the world, but sits by the camera in the world)
        public Vector3? WhipHand => whipView != null && whipView.go && whipView.go.activeInHierarchy && whipView.shootPoint ? whipView.shootPoint.position : (Vector3?)null;
        private bool whipVisible;
        public Material WhipWireMaterial { get; private set; }

        public void SetWhip(bool visible, string animState = null)
        {
            if (whipVisible != visible)
            {
                whipVisible = visible;
                ApplyWeapon();
            }
            if (visible && animState != null && whipView != null && whipView.animator)
            {
                whipView.animator.Play(animState, -1, 0f);
            }
        }
        public void SetArm(int arm)
        {
            currentArm = arm;
            ApplyWeapon();
        }

        private float armVisibleUntil;
        private bool armShown;

        // Feedbacker: show for the duration of the punch (Punch.ReadyToPunch, 0.75 s)
        private void UpdateArmVisibility()
        {
            bool show = Time.time < armVisibleUntil;
            if (show != armShown)
            {
                armShown = show;
                ApplyWeapon();
            }
        }

        public void ArmTrigger(string name)
        {
            WeaponView a = currentArm >= 0 && currentArm < arms.Length ? arms[currentArm] : null;
            if (a != null && a.animator)
            {
                a.animator.SetTrigger(name);
                armVisibleUntil = Time.time + 0.75f;
            }
        }

        public void Trigger(string name)
        {
            if (animator)
            {
                animator.SetTrigger(name);
            }
        }

        public void SetBool(string name, bool value)
        {
            if (animator)
            {
                animator.SetBool(name, value);
            }
        }

        public void SetFloat(string name, float value)
        {
            if (animator)
            {
                animator.SetFloat(name, value);
            }
        }

        public void PlayState(string state)
        {
            if (animator)
            {
                animator.Play(state, 0, 0f);
            }
        }

        // turn a part of the weapon model on/off (e.g. the saw on the Sawed-on barrel)
        public void SetPartActive(int weapon, string part, bool active)
        {
            WeaponView v = weapon >= 0 && weapon < views.Length ? views[weapon] : null;
            if (v == null)
            {
                return;
            }
            // searching the whole model is expensive and this is called every frame, so cache what was found
            if (!v.parts.TryGetValue(part, out Transform t))
            {
                t = FindDeep(v.go.transform, part);
                v.parts[part] = t;
            }
            if (t && t.gameObject.activeSelf != active)
            {
                t.gameObject.SetActive(active);
            }
        }

        // weapon shake while building force (Shotgun: +- grenadeForce / 3000)
        public void SetShake(float amount)
        {
            shake = amount;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
            {
                if (c.name == name)
                {
                    return c;
                }
            }
            return null;
        }

        // Revolver.Update for the "Piercer"
        private void UpdateChargeEffects()
        {
            PiercerData data = GetComponent<PiercerData>();
            float charge = data ? data.pierceShotCharge : 0f;
            GenericSkill secondary = body.skillLocator ? body.skillLocator.secondary : null;
            bool pierceReady = !secondary || secondary.stock > 0;
            // pierceCharge 0..100: how much has built up since the charged shot (+40 per second)
            float pierceCharge = pierceReady ? 100f
                : Mathf.Clamp01(secondary.rechargeStopwatch / Mathf.Max(0.01f, secondary.GetFinalRechargeInterval())) * 100f;

            // shake while charging: each axis separately, +- charge / 250 * 0.05
            if (gun)
            {
                gun.transform.localPosition = charge != 0f
                    ? gunDefaultPos + new Vector3(Random.Range(-0.05f, 0.05f), Random.Range(-0.05f, 0.05f), Random.Range(-0.05f, 0.05f)) * (charge / 250f)
                    : gunDefaultPos;
            }

            // charge glow on the barrel: size = charge x 0.02
            if (chargeEffect)
            {
                chargeEffect.SetActive(charge > 0f);
                chargeEffect.transform.localScale = Vector3.one * charge * 0.02f;
            }

            // cylinder: spins at the charge speed while charging; slows down while RMB recharges (50 per second)
            if (charge != 0f)
            {
                spinSpeed = charge;
            }
            else if (!pierceReady)
            {
                spinSpeed = Mathf.MoveTowards(spinSpeed, 0f, Time.deltaTime * 50f);
            }
            else
            {
                spinSpeed = 0f;
            }
            UpdateCylinder();

            // battery screen
            if (monitor)
            {
                Material screenMat = monitor.material;
                Texture2D tex;
                Color color;
                if (!pierceReady)
                {
                    tex = pierceCharge < 50f ? V1Assets.batteryLow : V1Assets.batteryMid;
                    color = pierceCharge < 50f ? Color.red : Color.yellow;
                }
                else
                {
                    color = PiercerColor;
                    if (charge != 0f && V1Assets.batteryCharges != null)
                    {
                        tex = V1Assets.batteryCharges[charge < 50f ? 0 : (charge < 100f ? 1 : 2)];
                    }
                    else
                    {
                        tex = V1Assets.batteryFull;
                    }
                }
                if (tex)
                {
                    screenMat.SetTexture("_MainTex", tex);
                }
                screenMat.SetColor("_Color", color * ScreenBoost);
            }
        }


        // --- Marksman and Sharpshooter screens (in ULTRAKILL this is UI on the revolver screen; here it is drawn into a texture) ---
        public static readonly Color MarksmanColor = new Color(0.26666668f, 1f, 0.27058825f, 1f);
        public static readonly Color SharpshooterColor = new Color(1f, 0.23529413f, 0.23529413f, 1f);
        private Texture2D altScreen;
        private float altScreenValue = -1f;

        private void SetupAltRevolver(int weapon, Color variationColor)
        {
            WeaponView v = views[weapon];
            if (v == null)
            {
                return;
            }
            // the cylinder glows in the variant color (WeaponIcon.variationColoredRenderers)
            Transform cyl = FindDeep(v.go.transform, "Revolver_Cylinder");
            Renderer r = cyl ? cyl.GetComponent<Renderer>() : null;
            if (r)
            {
                r.material.SetColor("_EmissionColor", variationColor * ScreenBoost);
            }
            Transform mon = FindDeep(v.go.transform, "Monitor");
            if (mon)
            {
                // the Marksman screen is disabled in the prefab (ULTRAKILL draws UI over it): enable it and draw it ourselves
                mon.gameObject.SetActive(true);
            }
            MeshRenderer mr = mon ? mon.GetComponent<MeshRenderer>() : null;
            if (!mr && r)
            {
                // the Sharpshooter has no screen mesh: in ULTRAKILL it is only UI (Canvas). Put a quad in its place
                mr = CreateScreenQuad(v, FindDeep(v.go.transform, "Canvas"), r.sharedMaterial);
            }
            if (mr)
            {
                if (!altScreen)
                {
                    altScreen = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                }
                mr.material.SetTexture("_MainTex", altScreen);
                mr.material.SetColor("_Color", Color.white * ScreenBoost);
            }
        }

        private MeshRenderer CreateScreenQuad(WeaponView v, Transform canvas, Material template)
        {
            if (!canvas || !template)
            {
                return null;
            }
            RectTransform rect = canvas as RectTransform;
            Vector2 size = rect ? rect.sizeDelta : new Vector2(15f, 15f);
            var go = new GameObject("V1Screen");
            go.layer = HiddenLayer;
            go.transform.SetParent(canvas, false);
            // a double-sided quad the size of the screen UI
            var mesh = new Mesh();
            float w = size.x / 2f, h = size.y / 2f;
            mesh.vertices = new[] { new Vector3(-w, -h, 0f), new Vector3(w, -h, 0f), new Vector3(w, h, 0f), new Vector3(-w, h, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            Material m = new Material(template);
            m.SetTexture("_EmissionMap", null);
            m.SetColor("_EmissionColor", Color.black);
            if (m.HasProperty("_Ambient"))
            {
                m.SetFloat("_Ambient", 1f);
            }
            if (m.HasProperty("_Diffuse"))
            {
                m.SetFloat("_Diffuse", 0f);
            }
            if (m.HasProperty("_OffsetFactor"))
            {
                // the UI quad already sits slightly in front of the screen; a depth offset pulled it over the frame
                m.SetFloat("_OffsetFactor", 0f);
                m.SetFloat("_OffsetUnits", 0f);
            }
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            v.renderers.Add(mr);
            return mr;
        }


        // --- Sharpshooter spin-up (Revolver.LateUpdate, gunVariation 2) ---
        private bool twirlCharging;
        private float twirlCharge;
        private float twirlLevel;
        private float latestTwirlRotation;
        private bool twirlRecovery;

        public void SetTwirl(bool charging, float charge)
        {
            if (twirlCharging && !charging)
            {
                twirlRecovery = true;
            }
            if (charging && !twirlCharging && !twirlRecovery)
            {
                latestTwirlRotation = 0f;
            }
            twirlCharging = charging;
            twirlCharge = charge;
        }

        private void UpdateTwirl()
        {
            WeaponView v = views[WeaponSharpshooter];
            if (v == null)
            {
                return;
            }
            if (!v.parts.TryGetValue("Revolver_Bone", out Transform bone))
            {
                bone = FindDeep(v.go.transform, "Revolver_Bone");
                v.parts["Revolver_Bone"] = bone;
            }
            if (!v.parts.TryGetValue("TwirlSprite", out Transform spriteT))
            {
                spriteT = FindDeep(v.go.transform, "TwirlSprite");
                v.parts["TwirlSprite"] = spriteT;
            }
            SpriteRenderer sprite = spriteT ? spriteT.GetComponent<SpriteRenderer>() : null;
            float dt = Time.deltaTime;
            if (twirlCharging || twirlRecovery)
            {
                v.animator?.SetBool("Spinning", true);
                bool wasNegative = latestTwirlRotation < 0f;
                if (twirlCharging)
                {
                    twirlLevel = Mathf.Min(3f, Mathf.Floor(twirlCharge / 25f)) + 1f;
                }
                else
                {
                    twirlLevel = Mathf.MoveTowards(twirlLevel, 0.1f, dt * 100f * twirlLevel);
                }
                latestTwirlRotation += 1200f * (twirlLevel / 3f + 0.5f) * dt;
                if (sprite)
                {
                    sprite.color = new Color(1f, 1f, 1f, Mathf.Min(2f, Mathf.Floor(twirlCharge / 25f)) / 3f);
                }
                if (twirlRecovery && wasNegative && latestTwirlRotation >= 0f)
                {
                    // the revolver has finished turning back to its rest position
                    latestTwirlRotation = 0f;
                    twirlRecovery = false;
                    if (sprite)
                    {
                        sprite.color = new Color(1f, 1f, 1f, 0f);
                    }
                }
                else
                {
                    while (latestTwirlRotation > 180f)
                    {
                        latestTwirlRotation -= 360f;
                    }
                    if (bone)
                    {
                        bone.localRotation = Quaternion.Euler(bone.localRotation.eulerAngles + Vector3.forward * latestTwirlRotation);
                    }
                }
                v.animator?.SetFloat("TwirlSpeed", twirlLevel / 3f);
            }
            else
            {
                v.animator?.SetBool("Spinning", false);
                if (sprite)
                {
                    sprite.color = new Color(1f, 1f, 1f, 0f);
                }
            }
        }
        // --- nailgun: barrel spin and heat (Nailgun.Update, Spin, SetHeat) ---
        // the barrels spin at 250 + heat * 2250 deg/s while LMB is held and stop at 1000 deg/s^2;
        // Barrel_L around -Y, Barrel_R around +Y; the Barrel_* (1) covers glow with alpha = heat
        private float barrelSpinSpeed;
        private float barrelAngle;
        private SoundHandle barrelSpinSound;

        private void UpdateNailgun()
        {
            WeaponView v = views[currentWeapon];
            Weapons.NailgunData data = body ? body.GetComponent<Weapons.NailgunData>() : null;
            if (v == null || !data)
            {
                return;
            }
            UpdateNailScreen(data);
            // Nailgun.UpdateAnimationWeight: the second animator layer (firing shake) grows with heat on the Overheat,
            // 0 on the others, otherwise the recoil pushes the nailgun off the edge of the screen
            if (v.animator && v.animator.layerCount > 1)
            {
                float w = NailVariant(currentWeapon) == 1 && !data.burnOut ? (data.Sinks < 1f ? 0.9f : data.heatUp * 0.6f) : 0f;
                v.animator.SetLayerWeight(1, w);
            }
            float dt = Time.deltaTime;
            if (data.triggerHeld || data.burnOut)
            {
                barrelSpinSpeed = 250f + data.heatUp * 2250f;
            }
            else
            {
                barrelSpinSpeed = Mathf.MoveTowards(barrelSpinSpeed, 0f, dt * 1000f);
            }
            barrelAngle = (barrelAngle + barrelSpinSpeed * dt) % 360f;
            foreach ((string name, float sign) in new[] { ("Barrel_L", -1f), ("Barrel_R", 1f) })
            {
                if (!v.parts.TryGetValue(name, out Transform t))
                {
                    t = FindDeep(v.go.transform, name);
                    v.parts[name] = t;
                    if (t)
                    {
                        v.baseRotations[t] = t.localRotation;
                    }
                }
                if (t && v.baseRotations.TryGetValue(t, out Quaternion baseRot))
                {
                    t.localRotation = baseRot * Quaternion.AngleAxis(barrelAngle * sign, Vector3.up);
                }
            }
            // barrel spin sound (barrelAud: pitch = speed / 1500 * 2)
            if (barrelSpinSpeed > 1f)
            {
                if (barrelSpinSound == null)
                {
                    barrelSpinSound = V1Sounds.Play("NailSpin", body.gameObject, 0.25f, 1f, true, loop: true);
                }
                barrelSpinSound.Pitch = Mathf.Max(0.05f, barrelSpinSpeed / 1500f * 2f);
            }
            else if (barrelSpinSound != null)
            {
                barrelSpinSound.Stop();
                barrelSpinSound = null;
            }
            // barrel heat
            // on all three: in ULTRAKILL SetHeat(heatUp) is shared (on the Attractor and JumpStart the heat holds while firing)
            if (v.renderers.Count > 0)
            {
                foreach (string name in new[] { "Barrel_L (1)", "Barrel_R (1)", "Blade (1)" })
                {
                    if (!v.parts.TryGetValue(name, out Transform t))
                    {
                        t = FindDeep(v.go.transform, name);
                        v.parts[name] = t;
                    }
                    Renderer r = t ? t.GetComponent<Renderer>() : null;
                    if (r && r.material.HasProperty("_Color"))
                    {
                        Color c = r.material.color;
                        c.a = data.heatUp;
                        r.material.color = c;
                    }
                }
            }
        }

        // --- nailgun screens (Nailgun.RefreshHeatSinkFill / heatSlider / UpdateZapHud): 64x24 UI on "Canvas" ---
        // Attractor: nail count and 3 magnet bars (full is blue, charging is grey);
        // Overheat: heat bar (orange) and 2 heatsinks (full is green);
        // JumpStart: clamp cooldown, then "READY" / "NO TARGET" / distance to the clamp and its charge.
        private readonly System.Collections.Generic.Dictionary<int, Texture2D> nailScreens = new System.Collections.Generic.Dictionary<int, Texture2D>();
        private float nailSinkFill = -1f;
        private const int NailW = 64, NailH = 24;
        private static readonly Color NailEmpty = new Color(0.15f, 0.15f, 0.15f, 1f);
        private static readonly Color NailOrange = new Color(1f, 0.5f, 0f, 1f);

        private void SetupNailScreen(int weapon)
        {
            WeaponView v = views[weapon];
            if (v == null)
            {
                return;
            }
            Renderer template = null;
            foreach (Renderer r in v.renderers)
            {
                if (r && r.sharedMaterial)
                {
                    template = r;
                    break;
                }
            }
            MeshRenderer mr = CreateScreenQuad(v, FindDeep(v.go.transform, IsSawLauncher(weapon) ? "Canvas (1)" : "Canvas"), template ? template.sharedMaterial : null);
            if (!mr)
            {
                return;
            }
            var tex = new Texture2D(NailW, NailH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            mr.material.SetTexture("_MainTex", tex);
            mr.material.SetColor("_Color", Color.white * ScreenBoost);
            nailScreens[weapon] = tex;
        }

        private void UpdateNailScreen(Weapons.NailgunData data)
        {
            if (!nailScreens.TryGetValue(currentWeapon, out Texture2D tex))
            {
                return;
            }
            Color[] px = new Color[NailW * NailH];
            for (int i = 0; i < px.Length; i++)
            {
                px[i] = Color.black;
            }
            int variant = NailVariant(currentWeapon);
            if (variant == 0)
            {
                ScreenText(px, Mathf.RoundToInt(data.alt ? data.saws : data.ammo).ToString(), NailW / 2, 8, PiercerColor);
                SinkFill(data.magnetCharge);
                for (int i = 0; i < 3; i++)
                {
                    float f = Mathf.Clamp01(nailSinkFill - i);
                    Color c = f >= 1f ? PiercerColor : Color.gray;
                    int x0 = 3 + i * 20;
                    FillRect(px, x0, 3, 3, 3, f > 0f ? c : NailEmpty);
                    Bar(px, x0 + 4, 3, 13, 4, f, c);
                }
            }
            else if (variant == 1)
            {
                Color heat = data.Sinks < 1f && data.heatUp <= 0f ? NailEmpty : Color.Lerp(Color.gray, NailOrange, data.heatUp);
                Bar(px, 4, 14, 56, 6, data.heatUp, heat);
                SinkFill(data.Sinks);
                int sinks = data.alt ? 1 : 2;
                for (int i = 0; i < sinks; i++)
                {
                    float f = Mathf.Clamp01(nailSinkFill - i);
                    Bar(px, 4 + i * 29, 3, sinks == 1 ? 56 : 27, 7, f, f >= 1f ? MarksmanColor : Color.gray);
                }
            }
            else
            {
                Weapons.V1Zapper z = data.zapper;
                if (data.zapperRecharge < 5f)
                {
                    ScreenText(px, "RECHARGING...", NailW / 2, 13, Color.white, 1);
                    Bar(px, 4, 9, 56, 6, data.zapperRecharge / 5f, Color.gray);
                }
                else if (!z)
                {
                    ScreenText(px, "READY", NailW / 2, 7, Color.white, 2);
                }
                else if (!z.attached)
                {
                    ScreenText(px, "NO TARGET", NailW / 2, 13, Color.white, 1);
                    // distance scale with tick marks
                    Bar(px, 4, 4, 56, 6, 0f, Color.red);
                    for (int k = 1; k < 4; k++) FillRect(px, 4 + k * 14, 4, 1, 6, k == 3 ? Color.red : Color.gray);
                }
                else
                {
                    float d = z.Distance;
                    float frac = Mathf.Clamp01(d / Weapons.Nailgun.ZapperMaxDistance);
                    Color c = Color.Lerp(Color.white, Color.red, frac);
                    ScreenText(px, "DISTANCE: " + d.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), NailW / 2, 17, c, 1);
                    Bar(px, 4, 9, 56, 4, frac, new Color(0.5f, 0.12f, 0.12f));
                    Bar(px, 4, 3, 56, 4, z.Charge / 5f, Color.white);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false);
        }

        // smooth filling of the segments, like heatSinkFill
        private void SinkFill(float charge)
        {
            if (nailSinkFill < 0f)
            {
                nailSinkFill = charge;
            }
            nailSinkFill = Mathf.MoveTowards(nailSinkFill, charge, Time.deltaTime * (Mathf.Abs((charge - nailSinkFill) * 20f) + 1f));
        }

        private static void FillRect(Color[] px, int x0, int y0, int w, int h, Color c)
        {
            for (int y = y0; y < y0 + h; y++)
            {
                for (int x = x0; x < x0 + w; x++)
                {
                    if (x >= 0 && x < NailW && y >= 0 && y < NailH)
                    {
                        px[y * NailW + x] = c;
                    }
                }
            }
        }

        // bar: dark background and fill from left to right
        private static void Bar(Color[] px, int x0, int y0, int w, int h, float fill, Color c)
        {
            FillRect(px, x0, y0, w, h, NailEmpty);
            FillRect(px, x0, y0, Mathf.RoundToInt(Mathf.Clamp01(fill) * w), h, c);
        }

        // 3x5 pixel font (rows top to bottom)
        private static readonly System.Collections.Generic.Dictionary<char, string> Font3x5 = new System.Collections.Generic.Dictionary<char, string>
        {
            { '0', "111101101101111" }, { '1', "010110010010111" }, { '2', "111001111100111" }, { '3', "111001111001111" }, { '4', "101101111001001" },
            { '5', "111100111001111" }, { '6', "111100111101111" }, { '7', "111001010010010" }, { '8', "111101111101111" }, { '9', "111101111001111" },
            { 'A', "010101111101101" }, { 'C', "111100100100111" }, { 'D', "110101101101110" }, { 'E', "111100111100111" }, { 'G', "111100101101111" },
            { 'I', "111010010010111" }, { 'N', "110101101101101" }, { 'O', "111101101101111" }, { 'R', "110101110101101" }, { 'S', "111100111001111" },
            { 'T', "111010010010010" }, { 'Y', "101101010010010" }, { '.', "000000000000010" }, { ' ', "000000000000000" }, { ':', "000010000010000" },
            { 'H', "101101111101101" }, { 'L', "100100100100111" }, { 'U', "101101101101111" }, { 'B', "110101110101110" }, { 'K', "101101110101101" }, { 'P', "110101110100100" }, { 'F', "111100110100100" }, { 'M', "101111111101101" }, { 'W', "101101111111101" },
        };

        // text across the whole screen width, as in ULTRAKILL: the scale is fitted to the length (no more than maxScale)
        private static void ScreenText(Color[] px, string text, int centerX, int y, Color c, int maxScale = 3)
        {
            int s = Mathf.Clamp((NailW - 4) / Mathf.Max(1, text.Length * 4 - 1), 1, maxScale);
            int x0 = centerX - (text.Length * 4 - 1) * s / 2;
            for (int i = 0; i < text.Length; i++)
            {
                if (!Font3x5.TryGetValue(char.ToUpperInvariant(text[i]), out string g))
                {
                    continue;
                }
                for (int row = 0; row < 5; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        if (g[row * 3 + col] == '1')
                        {
                            FillRect(px, x0 + (i * 4 + col) * s, y + (4 - row) * s, s, s, c);
                        }
                    }
                }
            }
        }

        // --- shotgun screen (Shotgun.UpdateMeter): a 0..60 slider on the Canvas (2) UI with three dividers ---
        // Core Eject: full light blue; while charging a core it shows the throw force, the color shifts to red;
        // Pump Charge: charge * 20, color from green to red; on overpump it is full and blinks red/black with a beep every 0.2 s;
        // Sawed-on: saw charge (grey until full; full is red); while charging it shows the throw force.
        private readonly System.Collections.Generic.Dictionary<int, Texture2D> shotgunScreens = new System.Collections.Generic.Dictionary<int, Texture2D>();
        private float pumpBeepTimer;
        private bool pumpBlinkRed;

        private void SetupShotgunScreen(int weapon)
        {
            WeaponView v = views[weapon];
            if (v == null)
            {
                return;
            }
            Renderer template = null;
            foreach (Renderer r in v.renderers)
            {
                if (r && r.sharedMaterial)
                {
                    template = r;
                    break;
                }
            }
            MeshRenderer mr = CreateScreenQuad(v, FindDeep(v.go.transform, "Canvas (2)"), template ? template.sharedMaterial : null);
            if (!mr)
            {
                return;
            }
            var tex = new Texture2D(120, 40, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            mr.material.SetTexture("_MainTex", tex);
            mr.material.SetColor("_Color", Color.white * ScreenBoost);
            shotgunScreens[weapon] = tex;
        }

        private void UpdateShotgunScreen()
        {
            if (!body || !shotgunScreens.TryGetValue(currentWeapon, out Texture2D tex))
            {
                return;
            }
            float force = body.GetComponent<Weapons.ShotgunMeter>()?.force ?? 0f;
            Color danger = new Color(1f, 0.25f, 0.25f);
            float value;
            Color color;
            if (currentWeapon == WeaponShotgunPump)
            {
                int charge = body.GetComponent<Weapons.PumpData>()?.charge ?? 0;
                if (charge >= 3)
                {
                    value = 60f;
                    pumpBeepTimer = Mathf.MoveTowards(pumpBeepTimer, 0f, Time.deltaTime * 5f);
                    if (pumpBeepTimer == 0f)
                    {
                        pumpBeepTimer = 1f;
                        pumpBlinkRed = true;
                        V1Sounds.Play("WarningBeep", body.gameObject, 0.6f, 1f, true);
                    }
                    else if (pumpBeepTimer < 0.5f)
                    {
                        pumpBlinkRed = false;
                    }
                    color = pumpBlinkRed ? Color.red : Color.black;
                }
                else
                {
                    pumpBeepTimer = 0f;
                    value = charge * 20f;
                    color = Color.Lerp(MarksmanColor, danger, charge / 2f);
                }
            }
            else if (force > 0f)
            {
                value = force;
                color = Color.Lerp(currentWeapon == WeaponShotgun ? PiercerColor : SharpshooterColor, danger, force / 60f);
            }
            else if (currentWeapon == WeaponShotgun)
            {
                value = 60f;
                color = PiercerColor;
            }
            else
            {
                float saw = body.GetComponent<Weapons.SawData>()?.charge ?? 1f;
                value = saw * 60f;
                color = saw >= 1f ? SharpshooterColor : Color.gray;
            }
            const int W = 120, H = 40;
            Color[] px = new Color[W * H];
            float filled = Mathf.Clamp01(value / 60f);
            // as in ULTRAKILL (Shotgun Screen Canvas 60x20): a full-screen slider and three straight black lines 2 wide at 15/30/45
            Color empty = Color.Lerp(Color.black, color, 0.08f);
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    // coordinate along the screen (0..1); the slant on the model comes only from perspective
                    float xs = (x + 0.5f) / W;
                    bool gap = Mathf.Abs(xs - 0.25f) < 1f / 60f || Mathf.Abs(xs - 0.5f) < 1f / 60f || Mathf.Abs(xs - 0.75f) < 1f / 60f;
                    Color c = Color.black;
                    if (!gap && xs > 0f && xs < 1f)
                    {
                        // the bar fills from right to left, as in ULTRAKILL
                        c = xs >= 1f - filled ? color : empty;
                        c.a = 1f;
                    }
                    px[y * W + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false);
        }

        private void UpdateAltScreen()
        {
            if (!altScreen || !body)
            {
                return;
            }
            Weapons.RevolverAltData data = body.GetComponent<Weapons.RevolverAltData>();
            if (!data)
            {
                return;
            }
            bool marksman = currentWeapon == WeaponMarksman;
            float value = marksman ? data.coinCharge : data.sharpCharge + 1000f;
            // redraw on a noticeable change or when the charge reaches full (otherwise the last coin stayed red)
            bool full = marksman ? data.coinCharge >= 400f : data.sharpCharge >= 300f;
            bool wasFull = marksman ? altScreenValue >= 400f && altScreenValue < 1000f : altScreenValue >= 1300f;
            if (Mathf.Abs(value - altScreenValue) < 0.5f && full == wasFull)
            {
                return;
            }
            altScreenValue = value;
            Color[] px = new Color[32 * 32];
            for (int i = 0; i < px.Length; i++)
            {
                px[i] = Color.black;
            }
            if (marksman)
            {
                // 4 coins in 2x2, each fills radially; full is green, partial is red
                Vector2[] centers = { new Vector2(9f, 23f), new Vector2(23f, 23f), new Vector2(9f, 9f), new Vector2(23f, 9f) };
                for (int c = 0; c < 4; c++)
                {
                    float fill = Mathf.Clamp01(data.coinCharge / 100f - c);
                    Color col = fill >= 1f ? MarksmanColor : Color.red;
                    for (int y = 0; y < 32; y++)
                    {
                        for (int x = 0; x < 32; x++)
                        {
                            Vector2 d = new Vector2(x + 0.5f, y + 0.5f) - centers[c];
                            if (d.magnitude > 6f)
                            {
                                continue;
                            }
                            // angle from the bottom, clockwise (Radial360, origin Bottom)
                            float angle = Mathf.Atan2(-d.x, -d.y) * Mathf.Rad2Deg;
                            if (angle < 0f)
                            {
                                angle += 360f;
                            }
                            if (angle / 360f <= fill)
                            {
                                px[y * 32 + x] = col;
                            }
                        }
                    }
                }
            }
            else
            {
                // 3 columns: dark red background, red fill from bottom to top
                Color bg = new Color(0.20754719f, 0.04209684f, 0.04209684f, 1f);
                for (int b = 0; b < 3; b++)
                {
                    float fill = Mathf.Clamp01(data.sharpCharge / 100f - b);
                    int x0 = 4 + b * 9;
                    for (int y = 4; y < 28; y++)
                    {
                        for (int x = x0; x < x0 + 6; x++)
                        {
                            px[y * 32 + x] = (y - 4) / 24f < fill ? SharpshooterColor : bg;
                        }
                    }
                }
            }
            altScreen.SetPixels(px);
            altScreen.Apply(false);
        }
        // RevolverCylinder (3 rounds, axis (0,-1,0), speed 240): fast spin while charging,
        // otherwise turn to the nearest round; after every shot turn to the next one
        private const int CylinderBullets = 3;
        private static readonly Vector3 CylinderAxis = new Vector3(0f, -1f, 0f);
        private const float CylinderSpeed = 240f;
        private Quaternion[] cylinderRotations;
        private Quaternion cylinderCurrent;
        private int cylinderTarget;
        private bool cylinderFreeSpin;
        private float spinSpeed;

        private void InitCylinder()
        {
            if (!cylinder)
            {
                return;
            }
            cylinderCurrent = cylinder.localRotation;
            cylinderRotations = new Quaternion[CylinderBullets];
            for (int i = 0; i < CylinderBullets; i++)
            {
                cylinderRotations[i] = cylinder.localRotation * Quaternion.Euler(CylinderAxis * (360 / CylinderBullets) * i);
            }
        }

        private void UpdateCylinder()
        {
            if (!cylinder || cylinderRotations == null)
            {
                return;
            }
            if (spinSpeed * 10f > CylinderSpeed)
            {
                cylinderFreeSpin = true;
                cylinderCurrent *= Quaternion.Euler(CylinderAxis * Time.deltaTime * spinSpeed * 10f);
            }
            else if (cylinderFreeSpin)
            {
                cylinderFreeSpin = false;
                float best = float.MaxValue;
                for (int i = 0; i < CylinderBullets; i++)
                {
                    float a = Quaternion.Angle(cylinderCurrent, cylinderRotations[i]);
                    if (a < best)
                    {
                        best = a;
                        cylinderTarget = i;
                    }
                }
            }
            if (!cylinderFreeSpin)
            {
                cylinderCurrent = Quaternion.RotateTowards(cylinderCurrent, cylinderRotations[cylinderTarget], Time.deltaTime * CylinderSpeed);
            }
            cylinder.localRotation = cylinderCurrent;
        }

        private void TurnCylinder()
        {
            if (cylinderRotations != null)
            {
                cylinderTarget = (cylinderTarget + 1) % CylinderBullets;
            }
        }

        public void Shoot()
        {
            TurnCylinder();
            if (animator)
            {
                animator.SetFloat("RandomChance", Random.Range(0f, 1f));
                animator.SetTrigger("Shoot");
            }
        }

        public void ChargeShoot()
        {
            TurnCylinder();
            if (animator)
            {
                animator.SetFloat("RandomChance", Random.Range(0f, 1f));
                animator.SetTrigger("ChargeShoot");
            }
        }

        private void OnDestroy()
        {
            DetachBuffer();
            if (root)
            {
                Destroy(root);
            }
        }
    }
}
