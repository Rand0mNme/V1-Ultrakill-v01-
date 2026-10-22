using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Builds the v1assets bundle for the RoR2 mod from the ULTRAKILL export (the Assets/UK folder, filled by _tools/UkDeps):
// V1's arm with the "Piercer" revolver, shot beams, hit sparks, movement particles, battery screen textures.
// Run: Unity.exe -batchmode -quit -projectPath V1Unity -executeMethod BuildV1.Build
public static class BuildV1
{
    const string BundleName = "v1assets";
    const string OutDir = "AssetBundles";
    // material name -> its shader name in ULTRAKILL (the ULTRAKILL shaders themselves aren't in the export)
    const string ShaderMapPath = "Assets/V1/shadermap.txt";

    static readonly (string src, string dst, bool keepLights)[] Prefabs =
    {
        ("Assets/UK/Revolver Pierce.prefab", "Assets/V1/V1Piercer.prefab", false),
        ("Assets/UK/Revolver Beam.prefab", "Assets/V1/V1RevolverBeam.prefab", true),
        ("Assets/UK/Revolver Beam Super.prefab", "Assets/V1/V1RevolverBeamSuper.prefab", true),
        ("Assets/UK/LaserHitParticle.prefab", "Assets/V1/V1LaserHit.prefab", true),
        ("Assets/UK/SuperLaserHitParticle.prefab", "Assets/V1/V1SuperLaserHit.prefab", true),
        ("Assets/UK/SlideParticle.prefab", "Assets/V1/V1SlideParticle.prefab", true),
        ("Assets/UK/SlideScrape.prefab", "Assets/V1/V1SlideScrape.prefab", true),
        ("Assets/UK/WallScrape.prefab", "Assets/V1/V1WallScrape.prefab", true),
        ("Assets/UK/FallParticle.prefab", "Assets/V1/V1FallParticle.prefab", true),
        ("Assets/UK/DodgeParticle.prefab", "Assets/V1/V1DodgeParticle.prefab", true),
        ("Assets/UK/DustBig.prefab", "Assets/V1/V1DustBig.prefab", true),
        // shotgun (Core Eject variant) and its projectiles
        ("Assets/UK/Shotgun Grenade.prefab", "Assets/V1/V1ShotgunCore.prefab", false),
        ("Assets/UK/Shotgun Pump.prefab", "Assets/V1/V1ShotgunPump.prefab", false),
        ("Assets/UK/Shotgun Saw.prefab", "Assets/V1/V1ShotgunSaw.prefab", false),
        ("Assets/UK/ChainsawProjectile.prefab", "Assets/V1/V1Chainsaw.prefab", true),
        ("Assets/UK/SparksSawblade.prefab", "Assets/V1/V1SawSparks.prefab", true),
        // nailguns and their projectiles
        ("Assets/UK/Nailgun Overheat.prefab", "Assets/V1/V1NailgunOverheat.prefab", false),
        ("Assets/UK/Nailgun Magnet.prefab", "Assets/V1/V1NailgunMagnet.prefab", false),
        ("Assets/UK/Nailgun Zapper.prefab", "Assets/V1/V1NailgunZapper.prefab", false),
        ("Assets/UK/Nail_0.prefab", "Assets/V1/V1Nail.prefab", true),
        ("Assets/UK/NailHeated.prefab", "Assets/V1/V1NailHeated.prefab", true),
        ("Assets/UK/Harpoon_0.prefab", "Assets/V1/V1Magnet.prefab", true),
        ("Assets/UK/Zapper.prefab", "Assets/V1/V1Zapper.prefab", true),
        ("Assets/UK/NailgunMuzzleFlash.prefab", "Assets/V1/V1NailMuzzleFlash.prefab", true),
        // sawblade launchers (Sawblade Launcher, alternate nailguns) and their saws
        ("Assets/UK/Sawblade Launcher Magnet.prefab", "Assets/V1/V1SawLauncherMagnet.prefab", false),
        ("Assets/UK/Sawblade Launcher Overheat.prefab", "Assets/V1/V1SawLauncherOverheat.prefab", false),
        ("Assets/UK/Sawblade Launcher Zapper.prefab", "Assets/V1/V1SawLauncherZapper.prefab", false),
        ("Assets/UK/NailAlt.prefab", "Assets/V1/V1SawNail.prefab", true),
        ("Assets/UK/NailAltFodder.prefab", "Assets/V1/V1SawNailFodder.prefab", true),
        ("Assets/UK/NailAltHeated.prefab", "Assets/V1/V1SawNailHeated.prefab", true),
        ("Assets/UK/NailgunMuzzleFlash 3.prefab", "Assets/V1/V1SawMuzzleFlash.prefab", true),
        ("Assets/UK/NailgunMuzzleFlash 4.prefab", "Assets/V1/V1SuperSawMuzzleFlash.prefab", true),
        // railcannons
        ("Assets/UK/Railcannon Electric.prefab", "Assets/V1/V1RailElectric.prefab", false),
        ("Assets/UK/Railcannon Harpoon.prefab", "Assets/V1/V1RailHarpoon.prefab", false),
        ("Assets/UK/Railcannon Malicious.prefab", "Assets/V1/V1RailMalicious.prefab", false),
        ("Assets/UK/Railcannon Beam.prefab", "Assets/V1/V1RailBeam.prefab", true),
        ("Assets/UK/Railcannon Beam Malicious.prefab", "Assets/V1/V1RailBeamMalicious.prefab", true),
        ("Assets/UK/Harpoon Malicious.prefab", "Assets/V1/V1Screwdriver.prefab", true),
        ("Assets/UK/Explosion Malicious Railcannon.prefab", "Assets/V1/V1MaliciousExplosion.prefab", true),
        // rocket launchers
        ("Assets/UK/Rocket Launcher Freeze.prefab", "Assets/V1/V1RocketFreeze.prefab", false),
        ("Assets/UK/Rocket Launcher Cannonball.prefab", "Assets/V1/V1RocketCannonball.prefab", false),
        ("Assets/UK/Rocket Launcher Napalm.prefab", "Assets/V1/V1RocketNapalm.prefab", false),
        ("Assets/UK/Rocket_0.prefab", "Assets/V1/V1Rocket.prefab", true),
        ("Assets/UK/Cannonball_0.prefab", "Assets/V1/V1Cannonball.prefab", true),
        ("Assets/UK/GasolineProjectile.prefab", "Assets/V1/V1Gasoline.prefab", true),
        ("Assets/UK/RocketLauncherFire.prefab", "Assets/V1/V1RocketMuzzleFlash.prefab", true),
        // Marksman and Sharpshooter revolvers
        ("Assets/UK/Revolver Ricochet.prefab", "Assets/V1/V1Marksman.prefab", false),
        ("Assets/UK/Revolver Twirl.prefab", "Assets/V1/V1Sharpshooter.prefab", false),
        ("Assets/UK/Coin_0.prefab", "Assets/V1/V1Coin.prefab", true),
        ("Assets/UK/Revolver Beam Sharp.prefab", "Assets/V1/V1RevolverBeamSharp.prefab", true),
        ("Assets/UK/CoinFlash.prefab", "Assets/V1/V1CoinFlash.prefab", true),
        ("Assets/UK/CoinCharge.prefab", "Assets/V1/V1CoinCharge.prefab", true),
        // arms
        ("Assets/UK/Arm Blue_0.prefab", "Assets/V1/V1ArmFeedbacker.prefab", false),
        ("Assets/UK/Arm Red.prefab", "Assets/V1/V1ArmKnuckleblaster.prefab", false),
        ("Assets/UK/Explosion Wave Knuckleblaster.prefab", "Assets/V1/V1BlastWave.prefab", true),
        // Whiplash arm (cut out of the scene with UkScene extract)
        ("Assets/UK/_V1WhiplashArm.prefab", "Assets/V1/V1ArmWhiplash.prefab", false),
        ("Assets/UK/Shotgun Projectile.prefab", "Assets/V1/V1ShotgunPellet.prefab", true),
        ("Assets/UK/Grenade.prefab", "Assets/V1/V1Core.prefab", true),
        ("Assets/UK/Explosion.prefab", "Assets/V1/V1Explosion.prefab", true),
        ("Assets/UK/ExplosionChunk.prefab", "Assets/V1/V1ExplosionChunk.prefab", true),
        // ground slam shockwave (PhysicalShockwavePlayer)
        ("Assets/UK/PhysicalShockwavePlayer.prefab", "Assets/V1/V1SlamShockwave.prefab", false),
        // SmileOS shop terminal (only the booth model; the mod draws the UI itself)
        ("Assets/UK/Wrapped Shop.prefab", "Assets/V1/V1Shop.prefab", true),
        // ULTRAKILL start room (FirstRoom: room with the shop, hallway, door and pit), with collisions
        ("Assets/UK/V1FirstRoomRoom.prefab", "Assets/V1/V1FirstRoom.prefab", true),
        ("Assets/UK/ShotgunMuzzleFlash.prefab", "Assets/V1/V1ShotgunMuzzleFlash.prefab", true),
    };

    static readonly string[] Textures =
    {
        "Assets/UK/batteryfull.png", "Assets/UK/batterymid.png", "Assets/UK/batterylow.png",
        "Assets/UK/batterycharging1.png", "Assets/UK/batterycharging2.png", "Assets/UK/batterycharging3.png",
        // ULTRAKILL crosshair sprites (the dot and the ring of health and stamina bars)
        "Assets/UK/circlethick.asset", "Assets/UK/meter.asset",
        // the whole crosshair, cut out of the ULTRAKILL scene with _tools/UkScene (extract mode)
        "Assets/V1/UI/UKCrosshair.prefab",
        // SmileOS shop: weapon icons and the pixel font
        // weapon icons: white copies (in ULTRAKILL the lines are black and the UI shader colors them; see MakeWhiteIcons)
        "Assets/V1/UI/Icons/V1Icon_SingleRevolver.png", "Assets/V1/UI/Icons/V1Icon_RevolverSpecial.png", "Assets/V1/UI/Icons/V1Icon_RevolverSharp.png",
        "Assets/V1/UI/Icons/V1Icon_Shotgun.png", "Assets/V1/UI/Icons/V1Icon_Shotgun1.png", "Assets/V1/UI/Icons/V1Icon_ShotgunSaw.png",
        "Assets/V1/UI/Icons/V1Icon_Nailgun.png", "Assets/V1/UI/Icons/V1Icon_NailgunOverheat.png", "Assets/V1/UI/Icons/V1Icon_NailgunZapper.png",
        "Assets/V1/UI/Icons/V1Icon_Railcannon.png", "Assets/V1/UI/Icons/V1Icon_railcannonscrew.png", "Assets/V1/UI/Icons/V1Icon_railcannonmalicious.png",
        "Assets/V1/UI/Icons/V1Icon_rocketlauncher.png", "Assets/V1/UI/Icons/V1Icon_rocketlaunchercannon.png", "Assets/V1/UI/Icons/V1Icon_rocketlaunchergasoline.png",
        "Assets/UK/fs-tahoma-8px.ttf",
        // style ranks (StyleHUD)
        "Assets/UK/RankD.asset", "Assets/UK/RankC.asset", "Assets/UK/RankB.asset", "Assets/UK/RankA.asset",
        "Assets/UK/RankS.asset", "Assets/UK/RankSS.asset", "Assets/UK/RankSSS.asset", "Assets/UK/RankU.asset",
    };

    static Dictionary<string, string> shaderMap;

    // a material with a missing shader doesn't expose its properties through GetTexture, so read the saved data directly
    static Texture SavedTexture(Material m, string name)
    {
        var so = new SerializedObject(m);
        var envs = so.FindProperty("m_SavedProperties.m_TexEnvs");
        for (int i = 0; i < envs.arraySize; i++)
        {
            var e = envs.GetArrayElementAtIndex(i);
            if (e.FindPropertyRelative("first").stringValue == name)
            {
                return e.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
            }
        }
        return null;
    }

    static Color SavedColor(Material m, string name, Color fallback)
    {
        var so = new SerializedObject(m);
        var cols = so.FindProperty("m_SavedProperties.m_Colors");
        for (int i = 0; i < cols.arraySize; i++)
        {
            var e = cols.GetArrayElementAtIndex(i);
            if (e.FindPropertyRelative("first").stringValue == name)
            {
                return e.FindPropertyRelative("second").colorValue;
            }
        }
        return fallback;
    }

    static void SetUnlit(Material m, Texture main, Color color, bool cullOff, float cutoff)
    {
        m.shader = Shader.Find("V1/Unlit");
        m.SetTexture("_MainTex", main);
        m.SetColor("_Color", color);
        m.SetFloat("_Cull", cullOff ? 0f : 2f);
        m.SetFloat("_Cutoff", cutoff);
    }

    // RoR2 has no ULTRAKILL shaders, so each one is replaced with its counterpart from Assets/V1/Shaders.
    static void ConvertMaterial(Material m, Renderer r)
    {
        // RoR2 has no explosion air distortion (Particles/Standard Surface), so make it transparent
        if (m && m.shader && (m.shader.name == "Particles/Standard Surface" || Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(m)).StartsWith("ExplosionShockwave") && !Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(m)).EndsWith(" 6")))
        {
            m.shader = Shader.Find("V1/Explosion");
            m.SetColor("_Color", new Color(1f, 1f, 1f, 0f));
            EditorUtility.SetDirty(m);
            return;
        }
        // only touch materials whose ULTRAKILL shader is missing (leave Unity built-in shaders alone)
        if (!m || (m.shader != null && m.shader.isSupported && m.shader.name != "Hidden/InternalErrorShader" && !m.shader.name.StartsWith("V1/")))
        {
            return;
        }
        // material file name (the name inside the file can differ, e.g. Additive_0.mat -> "Additive")
        string key = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(m));
        if (!shaderMap.TryGetValue(key, out string original))
        {
            shaderMap.TryGetValue(m.name, out original);
        }
        original = original ?? "";
        Texture main = SavedTexture(m, "_MainTex");
        Color color = SavedColor(m, "_Color", Color.white);
        bool particle = r is ParticleSystemRenderer || r is TrailRenderer;

        if (original.Contains("Particles/Additive"))
        {
            m.shader = Shader.Find("V1/ParticlesAdditive");
            m.SetTexture("_MainTex", main);
            m.SetColor("_TintColor", SavedColor(m, "_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f)));
        }
        else if (original.Contains("Multiply"))
        {
            m.shader = Shader.Find("V1/ParticlesMultiplyDouble");
            m.SetTexture("_MainTex", main);
        }
        else if (original.Contains("HideVertices"))
        {
            // ProBuilder helper material: draws nothing
            SetUnlit(m, null, Color.clear, true, 1.01f);
        }
        else if (particle && original == "ULTRAKILL/Master")
        {
            // smoke and dust of movement particles are translucent
            m.shader = Shader.Find("V1/ParticlesAlpha");
            m.SetTexture("_MainTex", main);
            m.SetColor("_Color", color);
        }
        else if (!particle && key.StartsWith("BarrelHeat"))
        {
            // barrel heat: the edges glow (V1HeatGlow), the mod sets the brightness through alpha
            m.shader = Shader.Find("V1/HeatGlow");
            m.SetTexture("_MainTex", main);
            m.SetColor("_Color", new Color(1f, 0.5f, 0.1f, color.a));
        }
        else if (!particle && (key.StartsWith("Explosion") || key.StartsWith("BarrelHeat")) && original == "ULTRAKILL/Master")
        {
            // explosion sphere: grows and fades by alpha (V1ExplosionAnim in the mod)
            m.shader = Shader.Find("V1/Explosion");
            m.SetTexture("_MainTex", main);
            m.SetColor("_Color", color);
        }
        else if (original.StartsWith("psx/vertexlit/transparent"))
        {
            // translucent and lit (ground slam shockwave and the like)
            m.shader = Shader.Find("V1/ParticlesAlpha");
            m.SetTexture("_MainTex", main);
            m.SetColor("_Color", color);
        }
        else if (original.StartsWith("psx/unlit/transparent") && original.Contains("fresnel"))
        {
            // translucent (gasoline and the like): a dark film you can see through
            m.shader = Shader.Find("V1/ParticlesAlpha");
            m.SetTexture("_MainTex", main);
            m.SetColor("_Color", new Color(color.r, color.g, color.b, original.Contains("fresnel") ? Mathf.Min(color.a, 0.6f) : color.a));
        }
        else if (original.StartsWith("psx/unlit/alphatest"))
        {
            SetUnlit(m, main, color, true, 0.5f);
        }
        else if (original == "psx/unlit/ambient" && !particle)
        {
            // battery screen on the weapon: unlit, but through the G-buffer like the arm itself (otherwise fog shows through)
            m.shader = Shader.Find("V1/Viewmodel");
            m.SetTexture("_MainTex", main);
            m.SetColor("_Color", color);
            m.SetTexture("_EmissionMap", null);
            m.SetColor("_EmissionColor", Color.black);
            m.SetFloat("_Ambient", 1f);
            m.SetFloat("_Diffuse", 0f);
            m.SetFloat("_OffsetFactor", -1f);
            m.SetFloat("_OffsetUnits", -10f);
        }
        else if (original.StartsWith("psx/unlit") || particle || r is LineRenderer || r is SpriteRenderer)
        {
            SetUnlit(m, main, color, particle || r is LineRenderer || r is SpriteRenderer, 0f);
        }
        else
        {
            // weapons and arm: ULTRAKILL/Master
            Texture emissive = SavedTexture(m, "_EmissiveTex") ?? SavedTexture(m, "_EmissionMap");
            m.shader = Shader.Find("V1/Viewmodel");
            m.SetTexture("_MainTex", main);
            m.SetColor("_Color", color);
            m.SetTexture("_EmissionMap", emissive);
            m.SetColor("_EmissionColor", emissive ? Color.white : Color.black);
        }
        EditorUtility.SetDirty(m);
    }

    static void PreparePrefab(string srcPath, string dstPath, bool keepLights)
    {
        GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
        if (!src)
        {
            throw new System.Exception("Not found " + srcPath);
        }
        GameObject go = (GameObject)Object.Instantiate(src);
        go.name = Path.GetFileNameWithoutExtension(dstPath);
        if (go.name == "V1Shop" || go.name == "V1FirstRoom")
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t && (t.name == "Canvas" || t.name == "Music" || t.name == "Jingle Music" || t.name == "Cube (4)" || t.name == "ShopTerminalProxy"))
                {
                    Object.DestroyImmediate(t.gameObject);
                }
            }
        }

        // the room's walls, floor and shaft are ProBuilder models: the ULTRAKILL files only hold their data, the mesh is built at runtime.
        // Build the meshes here, save them as assets and hand them to MeshFilter/MeshCollider; RoR2 doesn't need ProBuilder itself.
        var pbMeshes = go.GetComponentsInChildren<UnityEngine.ProBuilder.ProBuilderMesh>(true);
        if (pbMeshes.Length > 0)
        {
            string meshDir = "Assets/V1/Meshes_" + go.name;
            if (AssetDatabase.IsValidFolder(meshDir))
            {
                AssetDatabase.DeleteAsset(meshDir);
            }
            AssetDatabase.CreateFolder("Assets/V1", "Meshes_" + go.name);
            int index = 0;
            foreach (var pb in pbMeshes)
            {
                pb.ToMesh();
                pb.Refresh();
                MeshFilter mf = pb.GetComponent<MeshFilter>();
                Mesh mesh = mf ? mf.sharedMesh : null;
                if (mesh)
                {
                    Mesh copy = Object.Instantiate(mesh);
                    copy.name = pb.name + "_" + index;
                    AssetDatabase.CreateAsset(copy, meshDir + "/" + index + ".asset");
                    mf.sharedMesh = copy;
                    MeshCollider mc = pb.GetComponent<MeshCollider>();
                    if (mc)
                    {
                        mc.sharedMesh = copy;
                    }
                    // ProBuilder fits the BoxCollider to the mesh itself (RefreshCollisions); the saved data still has size 1x1x1
                    BoxCollider bc = pb.GetComponent<BoxCollider>();
                    if (bc)
                    {
                        bc.center = copy.bounds.center;
                        bc.size = copy.bounds.size;
                    }
                }
                index++;
            }
            foreach (var pb in pbMeshes)
            {
                Object.DestroyImmediate(pb);
            }
            Debug.Log("V1: ProBuilder meshes built: " + index);
            // some ULTRAKILL walls and slabs have no colliders of their own (other level objects hold the player there), so give each visible part an exact collider
            int added = 0;
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh && !mf.GetComponent<Collider>() && mf.GetComponent<MeshRenderer>() && !mf.name.StartsWith("ShopTerminal"))
                {
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    added++;
                }
            }
            Debug.Log("V1: colliders added: " + added);
            // in ULTRAKILL the walls are convex: the convex hull of a whole room wall fills the room and pushes the player out
            foreach (MeshCollider mc in go.GetComponentsInChildren<MeshCollider>(true))
            {
                // big hulls (walls, floor, ceiling) get the exact shape; small things (stairs and the like) stay convex, as in ULTRAKILL:
                // a convex staircase works as a ramp, while an exact one has steps higher than the character's step
                if (!mc.isTrigger && mc.sharedMesh && mc.sharedMesh.bounds.size.magnitude > 20f)
                {
                    mc.convex = false;
                }
            }
        }

        // remove ULTRAKILL scripts (RoR2 doesn't have them), audio (the mod plays its own) and physics
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            // the RoR2 camera doesn't render ULTRAKILL layers (23, 27 etc.), so everything goes on Default (the mod moves V1's arm to its own layer)
            t.gameObject.layer = 0;
            foreach (var c in t.GetComponents<Component>())
            {
                if (c is AudioSource || c is MonoBehaviour || (c is Collider && go.name != "V1FirstRoom") || (!keepLights && c is Light))
                {
                    Object.DestroyImmediate(c);
                }
            }
            Rigidbody rb = t.GetComponent<Rigidbody>();
            if (rb)
            {
                Object.DestroyImmediate(rb);
            }
        }

        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var m in r.sharedMaterials)
            {
                ConvertMaterial(m, r);
            }
        }

        // AssetRipper sometimes puts the controller on the wrong object: the animation paths ("Armature/...") aren't found from it
        // and the model stands in its default pose (that happened with the Feedbacker arm). Move the controller to where the paths resolve.
        foreach (Animator animator in go.GetComponentsInChildren<Animator>(true).ToArray())
        {
            if (!animator || !animator.runtimeAnimatorController)
            {
                continue;
            }
            string firstPath = null;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!string.IsNullOrEmpty(b.path))
                    {
                        firstPath = b.path;
                        break;
                    }
                }
                if (firstPath != null)
                {
                    break;
                }
            }
            if (firstPath == null || animator.transform.Find(firstPath))
            {
                continue;
            }
            Transform owner = null;
            foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
            {
                if (t != animator.transform && t.Find(firstPath))
                {
                    owner = t;
                    break;
                }
            }
            if (!owner)
            {
                Debug.LogWarning("V1: animation paths not found: " + go.name + " / " + firstPath);
                continue;
            }
            Animator target = owner.GetComponent<Animator>();
            if (!target)
            {
                target = owner.gameObject.AddComponent<Animator>();
            }
            target.runtimeAnimatorController = animator.runtimeAnimatorController;
            if (!target.avatar)
            {
                target.avatar = animator.avatar;
            }
            animator.runtimeAnimatorController = null;
            animator.enabled = false;
            Debug.Log("V1: animator moved " + go.name + ": " + animator.name + " -> " + owner.name);
        }

        // animation events call ULTRAKILL script methods that RoR2 doesn't have
        foreach (Animator animator in go.GetComponentsInChildren<Animator>(true))
        {
            if (!animator.runtimeAnimatorController)
            {
                continue;
            }
            foreach (var clip in animator.runtimeAnimatorController.animationClips.Distinct())
            {
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(clip)))
                {
                    AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);
                    EditorUtility.SetDirty(clip);
                }
            }
        }

        PrefabUtility.SaveAsPrefabAsset(go, dstPath);
        Object.DestroyImmediate(go);
    }

    // white copies of the weapon icons: alpha = line opacity (dark lines on a transparent background), the mod sets the color
    static void MakeWhiteIcons()
    {
        Directory.CreateDirectory("Assets/V1/UI/Icons");
        foreach (string t in Textures)
        {
            string name = Path.GetFileNameWithoutExtension(t);
            if (!name.StartsWith("V1Icon_"))
            {
                continue;
            }
            string src = "Assets/UK/" + name.Substring(7) + ".png";
            var imp = (TextureImporter)AssetImporter.GetAtPath(src);
            if (imp == null)
            {
                Debug.LogWarning("V1: no icon " + src);
                continue;
            }
            if (!imp.isReadable)
            {
                imp.isReadable = true;
                imp.SaveAndReimport();
            }
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(src);
            Color[] px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                Color c = px[i];
                float lum = (c.r + c.g + c.b) / 3f;
                px[i] = new Color(1f, 1f, 1f, c.a * (1f - lum));
            }
            var outTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            outTex.SetPixels(px);
            outTex.Apply();
            File.WriteAllBytes(t, outTex.EncodeToPNG());
            AssetDatabase.ImportAsset(t);
            var outImp = (TextureImporter)AssetImporter.GetAtPath(t);
            outImp.textureType = TextureImporterType.Sprite;
            outImp.alphaIsTransparency = true;
            outImp.mipmapEnabled = false;
            outImp.SaveAndReimport();
        }
    }

    public static void Build()
    {
        Directory.CreateDirectory("Assets/V1");
        AssetDatabase.Refresh();
        MakeWhiteIcons();

        shaderMap = new Dictionary<string, string>();
        if (File.Exists(ShaderMapPath))
        {
            foreach (string line in File.ReadAllLines(ShaderMapPath))
            {
                string[] p = line.Split('\t');
                if (p.Length == 2)
                {
                    shaderMap[p[0]] = p[1];
                }
            }
        }

        foreach (var p in Prefabs)
        {
            PreparePrefab(p.src, p.dst, p.keepLights);
        }
        AssetDatabase.SaveAssets();

        // files from the export still have ULTRAKILL bundle names; clear them so everything ends up in one bundle
        foreach (string name in AssetDatabase.GetAllAssetBundleNames())
        {
            AssetDatabase.RemoveAssetBundleName(name, true);
        }
        foreach (var p in Prefabs)
        {
            AssetImporter.GetAtPath(p.dst).SetAssetBundleNameAndVariant(BundleName, "");
        }
        foreach (string t in Textures)
        {
            AssetImporter.GetAtPath(t).SetAssetBundleNameAndVariant(BundleName, "");
        }
        if (Directory.Exists(OutDir))
        {
            Directory.Delete(OutDir, true);
        }
        Directory.CreateDirectory(OutDir);
        BuildPipeline.BuildAssetBundles(OutDir, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        Debug.Log("V1 BUILD OK: " + Path.GetFullPath(Path.Combine(OutDir, BundleName)));
    }
}
