// Quick viewer for a scene/prefab from the ULTRAKILL export.
// UkScene <file> <guidmap.txt> show <id>...      show objects (with script name and GameObject)
// UkScene <file> <guidmap.txt> refs <id>...      who references these objects
using System.Text.RegularExpressions;

var file = args[0];
var map = File.ReadLines(args[1]).Select(l => l.Split('\t')).Where(p => p.Length == 2).ToDictionary(p => p[0], p => Path.GetFileName(p[1]));
var mode = args[2];
var ids = args.Skip(3).ToHashSet();
var objs = new Dictionary<string, string>();
string? cur = null; var sb = new System.Text.StringBuilder();
foreach (var line in File.ReadLines(file))
{
    if (line.StartsWith("--- !u!"))
    {
        if (cur != null) objs[cur] = sb.ToString();
        cur = line.Split('&')[1].Split(' ')[0]; sb.Clear();
    }
    sb.AppendLine(line);
}
if (cur != null) objs[cur] = sb.ToString();

string Describe(string id)
{
    var t = objs[id];
    var script = Regex.Match(t, @"m_Script: \{fileID: \d+, guid: ([0-9a-f]{32})");
    var go = Regex.Match(t, @"m_GameObject: \{fileID: (\d+)\}");
    string name = "";
    if (go.Success && objs.TryGetValue(go.Groups[1].Value, out var g)) name = Regex.Match(g, @"m_Name: (.*)").Groups[1].Value;
    return $"[{id}] {t.Split('\n')[1].Trim()} script={(script.Success && map.TryGetValue(script.Groups[1].Value, out var s) ? s : "-")} go='{name}'";
}

if (mode == "tree")
{
    // UI tree from RectTransform/Transform: name, rotation, size, position, image (sprite, color, fill), SliderToFillAmount
    string M(string text, string pattern) => Regex.Match(text, pattern).Groups[1].Value;
    void Walk(string tid, int depth)
    {
        var t = objs[tid];
        var go = M(t, @"m_GameObject: \{fileID: (\d+)\}");
        var g = objs[go];
        string pad = new string(' ', depth * 2);
        Console.WriteLine($"{pad}{M(g, @"m_Name: (.*)")} [go {go}] active={M(g, @"m_IsActive: (\d)")} rot={M(t, @"m_LocalEulerAnglesHint: (\{[^}]*\})")} size={M(t, @"m_SizeDelta: (\{[^}]*\})")} pos={M(t, @"m_AnchoredPosition: (\{[^}]*\})")} scale={M(t, @"m_LocalScale: (\{[^}]*\})")}");
        foreach (Match cm in Regex.Matches(g, @"component: \{fileID: (\d+)\}"))
        {
            var c = objs[cm.Groups[1].Value];
            var sp = M(c, @"m_Sprite: \{fileID: \d+, guid: ([0-9a-f]{32})");
            if (sp != "")
                Console.WriteLine($"{pad}  image sprite={(map.TryGetValue(sp, out var sn) ? sn : sp)} color={M(c, @"m_Color: (\{[^}]*\})")} type={M(c, @"m_Type: (\d)")} fill={M(c, @"m_FillAmount: (\S+)")} method={M(c, @"m_FillMethod: (\d)")} origin={M(c, @"m_FillOrigin: (\d)")} cw={M(c, @"m_FillClockwise: (\d)")}");
            if (c.Contains("maxFill:"))
                Console.WriteLine($"{pad}  slider maxFill={M(c, @"maxFill: (\S+)")} copyColor={M(c, @"copyColor: (\d)")} target={M(c, @"targetSlider: \{fileID: (\d+)")}");
        }
        var children = Regex.Match(t, @"m_Children:\s*\n((?:\s*- \{fileID: \d+\}\s*\n)*)").Groups[1].Value;
        foreach (Match ch in Regex.Matches(children, @"fileID: (\d+)"))
            if (objs.ContainsKey(ch.Groups[1].Value)) Walk(ch.Groups[1].Value, depth + 1);
    }
    foreach (var id in ids) Walk(id, 0);
}
else if (mode == "extract")
{
    // Cuts a UI branch out of a scene into a separate .prefab for the V1Unity Unity project:
    // keeps GameObject, RectTransform/Transform, CanvasRenderer and UI images (Image);
    // ULTRAKILL scripts are dropped, the Image reference is pointed at the project's UI package.
    // UkScene <scene> <guidmap> extract <rootTransformId> <out.prefab>
    var argList = args.Skip(3).ToArray();
    string rootT = argList[0];
    string outPath = argList[1];
    const string ukImage = "fileID: -765806418, guid: d3e719b59ab71ba3f6b398058c866280, type: 3";
    const string projImage = "fileID: 11500000, guid: fe87c0e1cc204ed48ad3b37840f39efc, type: 3";
    var keep = new List<string>();
    void Collect(string tid)
    {
        var t = objs[tid];
        var go = Regex.Match(t, @"m_GameObject: \{fileID: (\d+)\}").Groups[1].Value;
        var g = objs[go];
        var kept = new List<string>();
        foreach (Match cm in Regex.Matches(g, @"component: \{fileID: (\d+)\}"))
        {
            var cid = cm.Groups[1].Value;
            var c = objs[cid];
            string header = c.Split('\n')[0];
            bool isImage = c.Contains(ukImage);
            // ProBuilderMesh: the wall model is stored as ProBuilder data; the V1Unity project has the ProBuilder 5.2.2 package
            const string ukPb = "m_Script: {fileID: 1326704909, guid: 53e127431f048a4dcfda837bbcdb38f6, type: 3}";
            bool isPb = c.Contains(ukPb);
            if (isPb) c = c.Replace(ukPb, "m_Script: {fileID: 11500000, guid: 8233d90336aea43098adf6dbabd606a2, type: 3}");
            // 3D: meshes, skin, animator, lines (for models like the Whiplash arm)
            bool is3d = header.Contains("!u!137 ") || header.Contains("!u!23 ") || header.Contains("!u!33 ") || header.Contains("!u!95 ") || header.Contains("!u!120 ")
                || header.Contains("!u!64 ") || header.Contains("!u!65 ") || header.Contains("!u!135 ") || header.Contains("!u!136 ") || header.Contains("!u!108 ");
            if (header.Contains("!u!224 ") || header.Contains("!u!4 ") || header.Contains("!u!222 ") || isImage || is3d || isPb)
            {
                kept.Add(cid);
                keep.Add(cid == tid && cid == rootT
                    ? Regex.Replace(c, @"m_Father: \{fileID: \d+\}", "m_Father: {fileID: 0}")
                    : c.Replace(ukImage, projImage));
            }
        }
        // keep only the saved components in GameObject
        var gNew = Regex.Replace(g, @"  m_Component:\n(?:  - component: \{fileID: \d+\}\n)+",
            "  m_Component:\n" + string.Concat(kept.Select(k => $"  - component: {{fileID: {k}}}\n")));
        keep.Add(gNew);
        var children = Regex.Match(t, @"m_Children:\s*\n((?:\s*- \{fileID: \d+\}\s*\n)*)").Groups[1].Value;
        foreach (Match ch in Regex.Matches(children, @"fileID: (\d+)"))
            if (objs.ContainsKey(ch.Groups[1].Value)) Collect(ch.Groups[1].Value);
    }
    Collect(rootT);
    // root: clear the parent (the Regex above was applied only to the root itself)
    for (int i = 0; i < keep.Count; i++)
        if (keep[i].StartsWith("--- !u!224 &" + rootT) || keep[i].StartsWith("--- !u!4 &" + rootT))
            keep[i] = Regex.Replace(keep[i], @"m_Father: \{fileID: \d+\}", "m_Father: {fileID: 0}");
    // remove references to dropped objects (components in GameObject lists and fields)
    var keptIds = keep.Select(k => Regex.Match(k, @"^--- !u!\d+ &(\d+)").Groups[1].Value).ToHashSet();
    string text = string.Concat(keep).Replace("\r\n", "\n");
    text = Regex.Replace(text, @"  - component: \{fileID: (\d+)\}\n", m => keptIds.Contains(m.Groups[1].Value) ? m.Value : "");
    text = Regex.Replace(text, @"\{fileID: (\d+)\}", m => m.Groups[1].Value == "0" || keptIds.Contains(m.Groups[1].Value) ? m.Value : "{fileID: 0}");
    File.WriteAllText(outPath, "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n" + text);
    Console.WriteLine($"objects written: {keep.Count} -> {outPath}");
}
else if (mode == "show")
{
    foreach (var id in ids)
    {
        if (!objs.ContainsKey(id)) { Console.WriteLine($"[{id}] missing"); continue; }
        Console.WriteLine(Describe(id));
        foreach (var l in objs[id].Split('\n').Skip(2).Where(l => !Regex.IsMatch(l, "m_ObjectHideFlags|m_Corresponding|m_PrefabInstance|m_PrefabAsset|m_EditorHideFlags|m_EditorClassIdentifier")).Take(60))
            Console.WriteLine("   " + l.TrimEnd());
    }
}
else
{
    foreach (var kv in objs)
        foreach (var id in ids)
            if (kv.Value.Contains("fileID: " + id + "}"))
                Console.WriteLine($"{id} <- {Describe(kv.Key)}");
}
