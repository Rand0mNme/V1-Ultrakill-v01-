using System.Text;
using UnityEditor;
using UnityEngine;

// Start room collision check: downward rays on a grid around the landing spot, a hit map in the log
public static class V1RoomProbe
{
    public static void Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/V1/V1FirstRoom.prefab");
        var go = (GameObject)Object.Instantiate(prefab);
        go.transform.position = Vector3.zero;
        Physics.SyncTransforms();
        var sb = new StringBuilder();
        float cx = float.Parse(System.Environment.GetEnvironmentVariable("V1_CX") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        float cz = float.Parse(System.Environment.GetEnvironmentVariable("V1_CZ") ?? "-47", System.Globalization.CultureInfo.InvariantCulture);
        float top = float.Parse(System.Environment.GetEnvironmentVariable("V1_TOP") ?? "20", System.Globalization.CultureInfo.InvariantCulture);
        for (float z = cz + 14; z >= cz - 14; z -= 1f)
        {
            sb.Append(z.ToString("000")).Append(' ');
            for (float x = cx - 14; x <= cx + 14; x += 1f)
            {
                if (Physics.Raycast(new Vector3(x, top, z), Vector3.down, out RaycastHit h, 60f))
                {
                    int y = Mathf.RoundToInt(h.point.y);
                    sb.Append(y >= 0 && y < 10 ? (char)('0' + y) : y < 0 ? '-' : '+');
                }
                else
                {
                    sb.Append('.');
                }
            }
            sb.AppendLine();
        }
        Debug.Log("V1PROBE\n" + sb);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            Bounds b = r.bounds;
            if (Mathf.Abs(b.center.z - cz) < 8 && b.max.y > -1.5f && b.min.y < 1.5f)
            {
                Collider own = r.GetComponent<Collider>();
                Debug.Log("V1REN " + r.name + " center " + b.center.ToString("F1") + " size " + b.size.ToString("F1") + " collider " + (own ? own.GetType().Name + " enabled " + own.enabled + " trig " + own.isTrigger + " bounds " + own.bounds.size.ToString("F1") : "NONE") + " parent " + (r.transform.parent ? r.transform.parent.name : "-"));
            }
        }
        foreach (Collider c in go.GetComponentsInChildren<Collider>())
        {
            Bounds b = c.bounds;
            if (Mathf.Abs(b.center.z - cz) < 20 && b.min.y < 12)
            {
                Debug.Log("V1COL " + c.name + " " + c.GetType().Name + " trig " + c.isTrigger + " convex " + (c is MeshCollider mc && mc.convex) + " center " + b.center.ToString("F1") + " size " + b.size.ToString("F1"));
            }
        }
    }
}
