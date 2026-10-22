using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using V1Mod.Survivors.V1.SkillStates;

namespace V1Mod.Survivors.V1
{
    // Placeholder version: V1 is a copy of Commando with all of his skills.
    // The model and V1's own skills come in later steps.
    public static class V1Survivor
    {
        public const string TOKEN_PREFIX = V1Plugin.DEVELOPER_PREFIX + "_V1_";

        public static GameObject bodyPrefab;
        public static GameObject displayPrefab;
        public static SurvivorDef survivorDef;

        public static void Init()
        {
            GameObject commandoBody = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion();
            GameObject commandoDisplay = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoDisplay.prefab").WaitForCompletion();

            bodyPrefab = PrefabAPI.InstantiateClone(commandoBody, "V1Body", true);
            displayPrefab = PrefabAPI.InstantiateClone(commandoDisplay, "V1Display", false);

            CharacterBody body = bodyPrefab.GetComponent<CharacterBody>();
            body.baseNameToken = TOKEN_PREFIX + "NAME";
            body.subtitleNameToken = TOKEN_PREFIX + "SUBTITLE";
            body.bodyColor = new Color(0.35f, 0.65f, 1f);

            // Walking as in ULTRAKILL: walkSpeed 750 * step 0.008 * 2.75 = 16.5 m/s, no sprint.
            // Horizontal speed is computed by V1CharacterMain, so the RoR2 motor acceleration = 0.
            body.baseMoveSpeed = V1CharacterMain.WalkSpeed;
            body.levelMoveSpeed = 0f;
            body.sprintingSpeedMultiplier = 1f;
            body.baseAcceleration = 0f;
            body.baseJumpCount = 1;

            // health as in the other V1 mod: 100 (+30 per level); damage from Commando
            body.baseMaxHealth = 100f;
            body.levelMaxHealth = 30f;

            // ULTRAKILL has no fall damage
            body.bodyFlags |= CharacterBody.BodyFlags.IgnoreFallDamage;

            // ULTRAKILL gravity: -40
            CharacterMotor motor = bodyPrefab.GetComponent<CharacterMotor>();
            motor.useCustomGravity = true;
            motor.CustomGravity = -40f;

            ContentAddition.AddEntityState<V1CharacterMain>(out _);
            ContentAddition.AddEntityState<V1SpawnFall>(out _);
            // no drop pod: spawn by falling from above, like at the start of an ULTRAKILL level
            body.preferredPodPrefab = null;
            body.preferredInitialStateType = new EntityStates.SerializableEntityStateType(typeof(V1SpawnFall));
            foreach (EntityStateMachine esm in bodyPrefab.GetComponents<EntityStateMachine>())
            {
                if (esm.customName == "Body")
                {
                    esm.mainStateType = new EntityStates.SerializableEntityStateType(typeof(V1CharacterMain));
                }
            }

            survivorDef = ScriptableObject.CreateInstance<SurvivorDef>();
            survivorDef.cachedName = "V1";
            survivorDef.bodyPrefab = bodyPrefab;
            survivorDef.displayPrefab = displayPrefab;
            survivorDef.primaryColor = body.bodyColor;
            survivorDef.displayNameToken = TOKEN_PREFIX + "NAME";
            survivorDef.descriptionToken = TOKEN_PREFIX + "DESCRIPTION";
            survivorDef.outroFlavorToken = TOKEN_PREFIX + "OUTRO_FLAVOR";
            survivorDef.mainEndingEscapeFailureFlavorToken = TOKEN_PREFIX + "OUTRO_FAILURE";
            survivorDef.desiredSortPosition = 100f;

            V1Skills.Init(bodyPrefab);
            V1Camera.Init(bodyPrefab);
            V1Crosshair.Init(bodyPrefab);
            V1GreyHealth.Init();
            V1Style.Init();
            Weapons.V1Shop.Init();
            V1StartRoom.Init();
            Weapons.V1Arsenal.InitEquipPatch();
            bodyPrefab.AddComponent<V1ViewModel>();
            bodyPrefab.AddComponent<SkillStates.V1MovementAudio>();
            bodyPrefab.AddComponent<SkillStates.V1MovementFx>();
            bodyPrefab.AddComponent<Weapons.V1Arsenal>();
            bodyPrefab.AddComponent<Weapons.V1Arms>();
            bodyPrefab.AddComponent<V1GreyHealth>();

            // a separate state machine for the Whiplash hook: you can shoot and punch while the hook flies
            EntityStateMachine hook = bodyPrefab.AddComponent<EntityStateMachine>();
            hook.customName = "Hook";
            hook.initialStateType = new EntityStates.SerializableEntityStateType(typeof(EntityStates.Idle));
            hook.mainStateType = new EntityStates.SerializableEntityStateType(typeof(EntityStates.Idle));
            NetworkStateMachine nsm = bodyPrefab.GetComponent<NetworkStateMachine>();
            if (nsm)
            {
                var field = typeof(NetworkStateMachine).GetField("stateMachines", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (field != null && field.GetValue(nsm) is EntityStateMachine[] machines)
                {
                    var list = new System.Collections.Generic.List<EntityStateMachine>(machines) { hook };
                    field.SetValue(nsm, list.ToArray());
                }
            }

            AddTokens();

            ContentAddition.AddBody(bodyPrefab);
            ContentAddition.AddSurvivorDef(survivorDef);

            Log.Info("V1 added (placeholder based on Commando)");
        }

        private static void AddTokens()
        {
            LanguageAPI.Add(TOKEN_PREFIX + "NAME", "V1");
            LanguageAPI.Add(TOKEN_PREFIX + "SUBTITLE", "Machine");
            LanguageAPI.Add(TOKEN_PREFIX + "DESCRIPTION", "V1 is a combat machine. For now it is a placeholder that uses Commando's model and skills.");
            LanguageAPI.Add(TOKEN_PREFIX + "OUTRO_FLAVOR", "..and so it left, still hungry for blood.");
            LanguageAPI.Add(TOKEN_PREFIX + "OUTRO_FAILURE", "..and so it vanished, its fuel spent.");
        }
    }
}
