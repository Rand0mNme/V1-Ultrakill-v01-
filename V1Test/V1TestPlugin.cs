using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Path = System.IO.Path;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace V1Test
{
    // V1 mod autotest: starts a single-player run as V1 by itself and runs commands from a file, so an agent can
    // test the mod in game without a human. Turned on only by the file BepInEx/V1Test/enabled; otherwise it does nothing.
    //
    // BepInEx/V1Test/cmd.txt: one command per line (the file is picked up and renamed to cmd.done):
    //   wait 1.5             pause
    //   shot name            screenshot to V1Test/shots/name.png
    //   move x z             movement relative to the view (-1..1), move 0 0 stops
    //   look yaw pitch       set the view (degrees), turn dyaw dpitch is relative
    //   hold/release button  skill1..skill4, interact, jump, sprint
    //   tap button           press for 0.1 s
    //   weapon N             V1 weapon by model id (V1ViewModel.Weapon*)
    //   tp x y z / up dy     teleport
    //   god on|off, enemies on|off, money N
    //   console text         RoR2 console command
    //   status               write the state to the log
    //   quit                 quit the game
    // BepInEx/V1Test/status.txt: state every 0.25 s; BepInEx/V1Test/log.txt: command log.
    [BepInPlugin("com.oleksandr.V1Test", "V1Test", "1.0.0")]
    public class V1TestPlugin : BaseUnityPlugin
    {
        public static string Dir;
        public static bool Active;
        private static StreamWriter log;

        // injected input
        public static Vector2 move;
        public static readonly HashSet<string> held = new HashSet<string>();
        private static readonly Dictionary<string, float> tapUntil = new Dictionary<string, float>();

        private readonly Queue<string> queue = new Queue<string>();
        private float waitUntil;
        private float statusTimer;
        private float sceneTime;
        private string lastScene;
        private bool runStarted;
        private bool godMode = true;

        private void Awake()
        {
            Dir = Path.Combine(Paths.BepInExRootPath, "V1Test");
            Active = File.Exists(Path.Combine(Dir, "enabled"));
            if (!Active)
            {
                return;
            }
            Directory.CreateDirectory(Path.Combine(Dir, "shots"));
            log = new StreamWriter(Path.Combine(Dir, "log.txt"), false, Encoding.UTF8) { AutoFlush = true };
            Log("autotest enabled");
            Application.runInBackground = true;
            var harmony = new Harmony("com.oleksandr.V1Test");
            harmony.PatchAll(typeof(InputPatches));
        }

        public static void Log(string s)
        {
            log?.WriteLine($"[{Time.realtimeSinceStartup:0.00}] {s}");
        }

        private void Update()
        {
            if (!Active)
            {
                return;
            }
            string scene = SceneManager.GetActiveScene().name;
            if (scene != lastScene)
            {
                lastScene = scene;
                sceneTime = Time.realtimeSinceStartup;
                Log("scene " + scene);
            }
            AutoStart(scene);

            CharacterBody body = LocalBody();
            if (body && body.healthComponent)
            {
                body.healthComponent.godMode = godMode;
            }

            statusTimer -= Time.unscaledDeltaTime;
            if (statusTimer <= 0f)
            {
                statusTimer = 0.25f;
                File.WriteAllText(Path.Combine(Dir, "status.txt"), Status(body));
            }

            string cmdPath = Path.Combine(Dir, "cmd.txt");
            if (File.Exists(cmdPath))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(cmdPath))
                    {
                        if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                        {
                            queue.Enqueue(line.Trim());
                        }
                    }
                    File.Copy(cmdPath, Path.Combine(Dir, "cmd.done"), true);
                    File.Delete(cmdPath);
                }
                catch (IOException)
                {
                }
            }
            while (queue.Count > 0 && Time.realtimeSinceStartup >= waitUntil)
            {
                string c = queue.Dequeue();
                try
                {
                    Execute(c, body);
                }
                catch (Exception e)
                {
                    Log("error in '" + c + "': " + e.Message);
                }
            }
            foreach (var kv in new List<KeyValuePair<string, float>>(tapUntil))
            {
                if (Time.realtimeSinceStartup >= kv.Value)
                {
                    held.Remove(kv.Key);
                    tapUntil.Remove(kv.Key);
                }
            }
        }

        // title screen -> single-player lobby -> pick V1 -> start
        private void AutoStart(string scene)
        {
            float t = Time.realtimeSinceStartup - sceneTime;
            if (scene == "intro" && RoR2Application.loadFinished && t > 0.5f && Time.realtimeSinceStartup >= nextSkip && RoR2.Console.instance)
            {
                // the command may fail while the scene is loading, so retry once a second
                nextSkip = Time.realtimeSinceStartup + 1f;
                Log("skipping intro");
                RoR2.Console.instance.SubmitCmd((NetworkUser)null, "set_scene title");
            }
            if (scene == "title" && !runStarted && t > 4f && RoR2.Console.instance)
            {
                runStarted = true;
                Log("starting lobby");
                RoR2.Console.instance.SubmitCmd((NetworkUser)null, "transition_command \"gamemode ClassicRun; host 0;\"");
            }
            if (scene == "lobby" && t > 3f && PreGameController.instance && !lobbyDone)
            {
                lobbyDone = true;
                // which survivor to pick: V1Test/survivor.txt (part of the body name), otherwise our V1
                SurvivorDef v1 = SurvivorCatalog.FindSurvivorDef("V1");
                string want = File.Exists(Path.Combine(Dir, "survivor.txt")) ? File.ReadAllText(Path.Combine(Dir, "survivor.txt")).Trim() : "";
                if (want.Length > 0)
                {
                    foreach (SurvivorDef sd in SurvivorCatalog.allSurvivorDefs)
                    {
                        if (sd.bodyPrefab && sd.bodyPrefab.name.Contains(want))
                        {
                            v1 = sd;
                        }
                    }
                }
                LocalUser user = LocalUserManager.GetFirstLocalUser();
                if (v1 && user != null)
                {
                    user.userProfile.SetSurvivorPreference(v1);
                    if (user.currentNetworkUser)
                    {
                        user.currentNetworkUser.CmdSetBodyPreference(BodyCatalog.FindBodyIndex(v1.bodyPrefab));
                    }
                }
                Log("V1 selected: " + (v1 != null) + ", starting run");
                Invoke(nameof(Launch), 1.5f);
            }
            if (Run.instance)
            {
                CombatDirector.cvDirectorCombatDisable.SetBool(!enemiesOn);
            }
        }

        private bool lobbyDone;
        private float nextSkip;
        private bool enemiesOn;

        private void Launch()
        {
            PreGameController.instance?.StartLaunch();
        }

        public static CharacterBody LocalBody()
        {
            LocalUser user = LocalUserManager.GetFirstLocalUser();
            return user != null ? user.cachedBody : null;
        }

        private static string Status(CharacterBody body)
        {
            var sb = new StringBuilder();
            sb.AppendLine("time " + Time.realtimeSinceStartup.ToString("0.00", CultureInfo.InvariantCulture));
            sb.AppendLine("scene " + SceneManager.GetActiveScene().name);
            if (!body)
            {
                sb.AppendLine("no body");
                return sb.ToString();
            }
            EntityStateMachine m = EntityStateMachine.FindByCustomName(body.gameObject, "Body");
            EntityStateMachine w = EntityStateMachine.FindByCustomName(body.gameObject, "Weapon");
            sb.AppendLine("body " + body.name + " state " + (m && m.state != null ? m.state.GetType().Name : "-") + " weapon(ESM) " + (w && w.state != null ? w.state.GetType().Name : "-"));
            sb.AppendLine("position " + V(body.footPosition) + " velocity " + (body.characterMotor ? V(body.characterMotor.velocity) : "-") + " grounded " + (body.characterMotor && body.characterMotor.isGrounded));
            sb.AppendLine("health " + (body.healthComponent ? body.healthComponent.health.ToString("0") + "/" + body.healthComponent.fullHealth.ToString("0") : "-") + " money " + (body.master ? body.master.money : 0));
            sb.AppendLine("aim " + (body.inputBank ? V(body.inputBank.aimDirection) : "-"));
            Component arsenal = body.GetComponent("V1Arsenal");
            if (arsenal)
            {
                sb.AppendLine("weapon " + arsenal.GetType().GetProperty("Current")?.GetValue(arsenal));
            }
            return sb.ToString();
        }

        private static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.00}, {1:0.00}, {2:0.00})", v.x, v.y, v.z);

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        private void Execute(string line, CharacterBody body)
        {
            string[] a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Log("> " + line);
            switch (a[0])
            {
                case "wait":
                    waitUntil = Time.realtimeSinceStartup + F(a[1]);
                    break;
                case "shot":
                    ScreenCapture.CaptureScreenshot(Path.Combine(Path.Combine(Dir, "shots"), a[1] + ".png"));
                    break;
                case "camshot":
                    {
                        // snapshot through the scene camera into a texture (works even when the screen is locked; no UI)
                        CameraRigController rig = null;
                        foreach (CameraRigController c in CameraRigController.readOnlyInstancesList) { rig = c; break; }
                        Camera cam = rig ? rig.sceneCam : null;
                        if (cam)
                        {
                            var rt = new RenderTexture(1600, 900, 24);
                            var old = cam.targetTexture;
                            cam.targetTexture = rt;
                            cam.Render();
                            cam.targetTexture = old;
                            RenderTexture.active = rt;
                            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                            tex.Apply();
                            RenderTexture.active = null;
                            File.WriteAllBytes(Path.Combine(Path.Combine(Dir, "shots"), a[1] + ".png"), ImageConversion.EncodeToPNG(tex));
                            rt.Release();
                        }
                    }
                    break;
                case "move":
                    move = new Vector2(F(a[1]), F(a[2]));
                    break;
                case "stop":
                    move = Vector2.zero;
                    held.Clear();
                    break;
                case "look":
                    Look(F(a[1]), F(a[2]), false);
                    break;
                case "turn":
                    Look(F(a[1]), F(a[2]), true);
                    break;
                case "hold":
                    held.Add(a[1]);
                    break;
                case "release":
                    held.Remove(a[1]);
                    break;
                case "tap":
                    held.Add(a[1]);
                    tapUntil[a[1]] = Time.realtimeSinceStartup + (a.Length > 2 ? F(a[2]) : 0.1f);
                    break;
                case "weapon":
                    if (body)
                    {
                        Component arsenal = body.GetComponent("V1Arsenal");
                        arsenal?.GetType().GetMethod("Switch", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(arsenal, new object[] { int.Parse(a[1]) });
                    }
                    break;
                case "tp":
                    if (body)
                    {
                        TeleportHelper.TeleportBody(body, new Vector3(F(a[1]), F(a[2]), F(a[3])), false);
                    }
                    break;
                case "up":
                    if (body)
                    {
                        TeleportHelper.TeleportBody(body, body.footPosition + Vector3.up * F(a[1]), false);
                    }
                    break;
                case "god":
                    godMode = a[1] == "on";
                    break;
                case "enemies":
                    enemiesOn = a[1] == "on";
                    break;
                case "money":
                    if (body && body.master)
                    {
                        body.master.money = uint.Parse(a[1]);
                    }
                    break;
                case "style":
                    if (body && body.master)
                    {
                        Type pt = null;
                        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            pt = pt ?? asm.GetType("V1Mod.Survivors.V1.V1Style+Points");
                        }
                        if (pt != null)
                        {
                            Component c = body.master.GetComponent(pt) ?? body.master.gameObject.AddComponent(pt);
                            pt.GetField("total").SetValue(c, int.Parse(a[1]));
                        }
                    }
                    break;
                case "explode":
                    if (body)
                    {
                        Type anim = null, assets = null;
                        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            anim = anim ?? asm.GetType("V1Mod.Survivors.V1.Weapons.V1ExplosionAnim");
                            assets = assets ?? asm.GetType("V1Mod.Modules.V1Assets");
                        }
                        object prefab = assets?.GetField(a.Length > 2 ? a[2] : "explosion")?.GetValue(null);
                        Vector3 at = body.inputBank.aimOrigin + body.inputBank.aimDirection * F(a[1]);
                        anim?.GetMethod("Spawn")?.Invoke(null, new object[] { prefab, at, Quaternion.identity, 1f });
                    }
                    break;
                case "addstyle":
                    {
                        Type st = null;
                        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            st = st ?? asm.GetType("V1Mod.Survivors.V1.V1Style");
                        }
                        st?.GetMethod("Add")?.Invoke(null, new object[] { body, int.Parse(a[1]), a.Length > 2 ? a[2] : "KILL" });
                    }
                    break;
                case "otherweapon":
                    if (body)
                    {
                        Component wm = body.GetComponent("V1WeaponManager");
                        if (wm)
                        {
                            foreach (MethodInfo mi in wm.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                            {
                                ParameterInfo[] ps = mi.GetParameters();
                                if (ps.Length >= 1 && ps[0].ParameterType.IsEnum && ps[0].ParameterType.Name == "WeaponType")
                                {
                                    Log("  method " + mi.Name + "(" + ps.Length + ")");
                                    if (a.Length > 2 && mi.Name == a[2])
                                    {
                                        object[] args = new object[ps.Length];
                                        args[0] = Enum.Parse(ps[0].ParameterType, a[1]);
                                        for (int k = 1; k < ps.Length; k++) args[k] = ps[k].HasDefaultValue ? ps[k].DefaultValue : (ps[k].ParameterType.IsValueType ? Activator.CreateInstance(ps[k].ParameterType) : null);
                                        mi.Invoke(wm, args);
                                        Log("  called " + mi.Name);
                                    }
                                }
                            }
                            Type wt = wm.GetType().GetNestedType("WeaponType");
                            if (wt != null) Log("  kinds: " + string.Join(",", Enum.GetNames(wt)));
                        }
                    }
                    break;
                case "console":
                    RoR2.Console.instance?.SubmitCmd((NetworkUser)null, line.Substring(8));
                    break;
                case "count":
                    {
                        int n = 0;
                        foreach (GameObject g in UnityEngine.Object.FindObjectsOfType<GameObject>())
                        {
                            if (g.name.Contains(a[1]))
                            {
                                n++;
                                Log("  " + g.name + " " + V(g.transform.position) + " rotY " + g.transform.eulerAngles.y.ToString("0"));
                            }
                        }
                        Log("found " + n);
                    }
                    break;
                case "near":
                    if (body)
                    {
                        foreach (Collider c in Physics.OverlapSphere(body.corePosition, F(a[1]), ~0, QueryTriggerInteraction.Collide))
                        {
                            Renderer rr = c.GetComponent<Renderer>();
                            Log("  " + c.name + " (" + c.GetType().Name + ") trigger " + c.isTrigger + " layer " + LayerMask.LayerToName(c.gameObject.layer) + " center " + V(c.bounds.center) + " size " + V(c.bounds.size) + " visible " + (rr && rr.enabled) + " parent " + (c.transform.parent ? c.transform.parent.name : "-"));
                        }
                    }
                    break;
                case "step":
                    if (body && body.characterMotor)
                    {
                        var m = body.characterMotor.Motor;
                        m.MaxStepHeight = F(a[1]);
                        m.StepHandling = KinematicCharacterController.StepHandlingMethod.Extra;
                        m.AllowSteppingWithoutStableGrounding = true;
                        Log("step " + m.MaxStepHeight);
                    }
                    break;
                case "convex":
                    foreach (MeshCollider mc in UnityEngine.Object.FindObjectsOfType<MeshCollider>())
                    {
                        if (mc.name == a[1] && !mc.isTrigger)
                        {
                            mc.convex = a[2] == "1";
                            Log("convex " + mc.name + " " + mc.convex);
                        }
                    }
                    break;
                case "lookat":
                    if (body)
                    {
                        GameObject target = null;
                        foreach (GameObject g in UnityEngine.Object.FindObjectsOfType<GameObject>())
                        {
                            if (g.name == a[1]) { target = g; break; }
                        }
                        if (target)
                        {
                            Vector3 d = target.transform.position + Vector3.up * (a.Length > 2 ? F(a[2]) : 1.5f) - body.inputBank.aimOrigin;
                            Look(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, false);
                            Log("looking at " + a[1] + ", distance " + d.magnitude.ToString("0.0"));
                        }
                    }
                    break;
                case "probe":
                    foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                    {
                        if (r.name.Contains(a[1]) || (r.transform.parent && r.transform.parent.name.Contains(a[1])))
                        {
                            Material m = r.sharedMaterial;
                            Log("  " + r.name + " on " + r.enabled + "/" + r.gameObject.activeInHierarchy + " layer " + r.gameObject.layer + " pos " + V(r.transform.position) + " scale " + V(r.transform.lossyScale) + " bounds " + V(r.bounds.size) + " shader " + (m ? m.shader.name + " supported " + m.shader.isSupported + " queue " + m.renderQueue + " color " + (m.HasProperty("_Color") ? m.color.ToString() : "-") + " texture " + (m.mainTexture ? m.mainTexture.name : "none") + " name " + m.name : "none"));
                        }
                    }
                    break;
                case "status":
                    Log(Status(body));
                    break;
                case "quit":
                    Application.Quit();
                    break;
                default:
                    Log("unknown command " + a[0]);
                    break;
            }
        }

        private static float yaw, pitch;
        private static bool lookSet;

        private static void Look(float y, float p, bool relative)
        {
            CameraRigController rig = null;
            foreach (CameraRigController c in CameraRigController.readOnlyInstancesList)
            {
                rig = c;
                break;
            }
            if (!lookSet && rig)
            {
                Vector3 e = rig.transform.eulerAngles;
                yaw = e.y;
                pitch = e.x > 180f ? e.x - 360f : e.x;
            }
            yaw = relative ? yaw + y : y;
            pitch = Mathf.Clamp(relative ? pitch + p : p, -89f, 89f);
            lookSet = true;
            CharacterBody body = LocalBody();
            PlayerCharacterMasterController pc = body && body.master ? body.master.playerCharacterMasterController : null;
            pc?.RedirectCamera(new Vector3(pitch, yaw, 0f));
        }

        public static bool Held(string name) => held.Contains(name);
    }

    // input from commands instead of keyboard/mouse (buttons are combined with the real ones)
    public static class InputPatches
    {
        private static readonly FieldInfo bodyInputsField = AccessTools.Field(typeof(PlayerCharacterMasterController), "bodyInputs");

        [HarmonyPatch(typeof(PlayerCharacterMasterController), "Update")]
        [HarmonyPostfix]
        public static void UpdatePostfix(PlayerCharacterMasterController __instance)
        {
            InputBankTest bank = bodyInputsField.GetValue(__instance) as InputBankTest;
            if (!bank || V1TestPlugin.move == Vector2.zero)
            {
                return;
            }
            Vector3 aim = bank.aimDirection;
            aim.y = 0f;
            Quaternion q = Quaternion.LookRotation(aim.sqrMagnitude > 0.001f ? aim.normalized : Vector3.forward);
            bank.moveVector = q * new Vector3(V1TestPlugin.move.x, 0f, V1TestPlugin.move.y);
        }

        [HarmonyPatch(typeof(PlayerCharacterMasterController), "PollButtonInput")]
        [HarmonyPostfix]
        public static void PollPostfix(PlayerCharacterMasterController __instance)
        {
            if (V1TestPlugin.held.Count == 0)
            {
                return;
            }
            InputBankTest bank = bodyInputsField.GetValue(__instance) as InputBankTest;
            if (!bank)
            {
                return;
            }
            // PushState was already called: add presses on top (down = true) without touching the previous state
            Or(ref bank.skill1, "skill1");
            Or(ref bank.skill2, "skill2");
            Or(ref bank.skill3, "skill3");
            Or(ref bank.skill4, "skill4");
            Or(ref bank.interact, "interact");
            Or(ref bank.jump, "jump");
            Or(ref bank.rawSprintInput, "sprint");
        }

        private static void Or(ref InputBankTest.ButtonState b, string name)
        {
            if (V1TestPlugin.Held(name))
            {
                b.down = true;
            }
        }
    }
}
