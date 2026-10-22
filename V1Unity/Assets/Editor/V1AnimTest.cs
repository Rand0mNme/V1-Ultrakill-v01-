using UnityEditor;
using UnityEngine;

// Check: does the animator move the model's bones (run from the command line with -executeMethod V1AnimTest.Run)
public static class V1AnimTest
{
    public static void Run()
    {
        foreach (string path in new[] { "Assets/V1/V1ArmFeedbacker.prefab", "Assets/V1/V1ArmKnuckleblaster.prefab" })
        {
            GameObject go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            foreach (Animator a in go.GetComponentsInChildren<Animator>(true))
            {
                Debug.Log($"V1TEST {path} animator on {a.name} enabled={a.enabled} ctrl={(a.runtimeAnimatorController ? a.runtimeAnimatorController.name : "-")} avatar={(a.avatar ? a.avatar.name + " valid=" + a.avatar.isValid + " human=" + a.avatar.isHuman : "-")}");
            }
            Transform hand = null;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Hand" || t.name == "hand")
                {
                    hand = t;
                }
            }
            Animator anim = null;
            foreach (Animator a in go.GetComponentsInChildren<Animator>(true))
            {
                if (a.runtimeAnimatorController)
                {
                    anim = a;
                }
            }
            go.SetActive(true);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.SetActive(true);
            }
            Vector3 before = hand ? hand.position : Vector3.zero;
            if (anim)
            {
                if (path.Contains("Feedbacker")) { anim.avatar = null; }
                anim.Rebind();
                for (int i = 0; i < 60; i++)
                {
                    anim.Update(0.05f);
                }
                Debug.Log($"V1TEST {path} state={anim.GetCurrentAnimatorClipInfo(0).Length} clip={(anim.GetCurrentAnimatorClipInfo(0).Length > 0 ? anim.GetCurrentAnimatorClipInfo(0)[0].clip.name : "-")}");
            }
            Debug.Log($"V1TEST {path} hand before={before} after={(hand ? hand.position : Vector3.zero)}");
            if (anim)
            {
                foreach (var clip in anim.runtimeAnimatorController.animationClips)
                {
                    int ok = 0, bad = 0;
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (anim.transform.Find(b.path) != null) ok++; else bad++;
                    }
                    Debug.Log($"V1TEST CLIP {path} {clip.name} file={AssetDatabase.GetAssetPath(clip)} ok={ok} bad={bad}");
                }
                foreach (var clip in anim.runtimeAnimatorController.animationClips)
                {
                    if (!clip.name.StartsWith("Idle"))
                    {
                        continue;
                    }
                    var bindings = AnimationUtility.GetCurveBindings(clip);
                    Debug.Log($"V1TEST {path} clip {clip.name} curves={bindings.Length} legacy={clip.legacy}");
                    foreach (var b in bindings)
                    {
                        if (b.path.EndsWith("Hand") || b.path == "Armature" || b.path.EndsWith("UpperArm"))
                        {
                            var curve = AnimationUtility.GetEditorCurve(clip, b);
                            Debug.Log($"V1TEST   {b.path} {b.propertyName} {b.type.Name} found={(anim.transform.Find(b.path) != null)} v0={(curve != null && curve.length > 0 ? curve.keys[0].value : float.NaN)}");
                        }
                    }
                    clip.SampleAnimation(anim.gameObject, 0.1f);
                    Debug.Log($"V1TEST {path} sampled hand={(hand ? hand.position : Vector3.zero)}");
                }
            }
            Object.DestroyImmediate(go);
        }
    }
}
