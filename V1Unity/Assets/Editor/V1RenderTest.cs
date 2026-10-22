using System.IO;
using UnityEditor;
using UnityEngine;

// Renders the first-person view like the ULTRAKILL HUD camera (90 degrees): weapon in "Guns", arms in "Punch" (0.225 lower)
public static class V1RenderTest
{
    public static void Run()
    {
        string gun = System.Environment.GetEnvironmentVariable("V1_GUN") ?? "Assets/V1/V1Piercer.prefab";
        string arm = System.Environment.GetEnvironmentVariable("V1_ARM") ?? "Assets/V1/V1ArmFeedbacker.prefab";
        string outPath = System.Environment.GetEnvironmentVariable("V1_OUT") ?? "render.png";
        float time = float.Parse(System.Environment.GetEnvironmentVariable("V1_TIME") ?? "2", System.Globalization.CultureInfo.InvariantCulture);

        var camGo = new GameObject("Cam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 90f;
        cam.nearClipPlane = 0.01f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.4f, 0.5f, 0.4f);
        var light = new GameObject("L").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50, -30, 0);

        Transform guns = new GameObject("Guns").transform;
        guns.SetParent(camGo.transform, false);
        Transform punch = new GameObject("Punch").transform;
        punch.SetParent(camGo.transform, false);
        punch.localPosition = new Vector3(0, -0.225f, 0);

        foreach (var (path, parent) in new[] { (gun, guns), (arm, punch) })
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab)
            {
                continue;
            }
            var go = (GameObject)Object.Instantiate(prefab, parent, false);
            go.transform.localPosition = prefab.transform.localPosition;
            string posEnv = System.Environment.GetEnvironmentVariable("V1_POS");
            if (!string.IsNullOrEmpty(posEnv) && path == gun)
            {
                var p = posEnv.Split(',');
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                go.transform.localPosition = new Vector3(float.Parse(p[0], ci), float.Parse(p[1], ci), float.Parse(p[2], ci));
            }
            go.transform.localRotation = prefab.transform.localRotation;
            string rotEnv = System.Environment.GetEnvironmentVariable("V1_ROTY");
            if (!string.IsNullOrEmpty(rotEnv) && path == gun) { go.transform.localRotation = Quaternion.Euler(0f, float.Parse(rotEnv, System.Globalization.CultureInfo.InvariantCulture), 0f) * go.transform.localRotation; }
            go.transform.localScale = prefab.transform.localScale;
            go.SetActive(true);
            string quadEnv = System.Environment.GetEnvironmentVariable("V1_QUAD");
            if (!string.IsNullOrEmpty(quadEnv) && path == gun)
            {
                // screen check: a quad under the object "name" of size w x h with offset (x, y): name;w;h;x;y
                var q = quadEnv.Split(';');
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                Transform canvas = null;
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == q[0])
                    {
                        canvas = t;
                    }
                }
                if (canvas)
                {
                    float w = float.Parse(q[1], ci) / 2f, h = float.Parse(q[2], ci) / 2f, ox = float.Parse(q[3], ci), oy = float.Parse(q[4], ci);
                    var quad = new GameObject("TestQuad");
                    quad.transform.SetParent(canvas, false);
                    var mesh = new Mesh();
                    mesh.vertices = new[] { new Vector3(ox - w, oy - h, 0f), new Vector3(ox + w, oy - h, 0f), new Vector3(ox + w, oy + h, 0f), new Vector3(ox - w, oy + h, 0f) };
                    mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
                    quad.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mat = new Material(Shader.Find("Unlit/Color"));
                    mat.color = Color.magenta;
                    quad.AddComponent<MeshRenderer>().sharedMaterial = mat;
                }
            }
            {
                Bounds bb = new Bounds(go.transform.position, Vector3.zero);
                foreach (Renderer rr in go.GetComponentsInChildren<Renderer>()) { bb.Encapsulate(rr.bounds); }
                Debug.Log("V1BOUNDS " + go.name + " center=" + bb.center.ToString("F2") + " size=" + bb.size.ToString("F2") + " root=" + go.transform.position.ToString("F2"));
            }
            if (System.Environment.GetEnvironmentVariable("V1_HEAT") != null)
            {
                foreach (Renderer hr in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (hr.name.EndsWith("(1)") && hr.sharedMaterial && hr.sharedMaterial.shader.name == "V1/HeatGlow")
                    {
                        Color hc = hr.sharedMaterial.color; hc.a = 1f; hr.material.color = hc;
                    }
                }
            }
            // V1_HIDE: hide objects by name (comma-separated); all renderers with their materials are written to the log
            string hide = System.Environment.GetEnvironmentVariable("V1_HIDE") ?? "";
            foreach (Renderer rr in go.GetComponentsInChildren<Renderer>(true))
            {
                string mats = "";
                foreach (var mm in rr.sharedMaterials) { mats += (mm ? mm.name + "(" + mm.shader.name + ")" : "null") + " "; }
                Debug.Log("V1REND " + rr.name + " active=" + rr.gameObject.activeInHierarchy + " " + rr.GetType().Name + " " + mats);
                if (System.Array.IndexOf(hide.Split(','), rr.name) >= 0) { rr.enabled = false; }
            }
            foreach (Animator a in go.GetComponentsInChildren<Animator>(true))
            {
                if (a.runtimeAnimatorController && a.enabled)
                {
                    a.Rebind();
                    for (float t0 = 0; t0 < 2f; t0 += 0.02f) { a.Update(0.02f); }
                    string trig = System.Environment.GetEnvironmentVariable("V1_TRIGGER");
                    if (!string.IsNullOrEmpty(trig) && path == arm) { a.SetTrigger(trig); }
                    for (float t = 0; t < time; t += 0.02f)
                    {
                        a.Update(0.02f);
                    }
                }
            }
        }

        // close-up: the camera looks at the object with this name with a narrow angle
        string focus = System.Environment.GetEnvironmentVariable("V1_FOCUS");
        if (!string.IsNullOrEmpty(focus))
        {
            foreach (Transform t in Object.FindObjectsOfType<Transform>())
            {
                if (t.name == focus)
                {
                    guns.SetParent(null, true);
                    punch.SetParent(null, true);
                    cam.transform.LookAt(t.position);
                    cam.fieldOfView = 12f;
                    break;
                }
            }
        }
        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        tex.Apply();
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
        Debug.Log("V1RENDER " + outPath);
    }
}
