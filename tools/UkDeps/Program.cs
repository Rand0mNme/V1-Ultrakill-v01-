// Copies an asset from the ULTRAKILL export together with everything it references (meshes, materials, textures, animations)
// into a Unity project. Doesn't copy scripts, sounds or nested prefabs (beams, effects).
// Usage: UkDeps <ExportedProject\Assets> <dest Assets folder> <root asset relative path>...
using System.Text.RegularExpressions;

var src = args[0];
var dst = args[1];
var cache = Path.Combine(AppContext.BaseDirectory, "guidmap.txt");
var map = new Dictionary<string, string>();
if (File.Exists(cache))
{
    foreach (var line in File.ReadLines(cache)) { var p = line.Split('\t'); map[p[0]] = p[1]; }
}
else
{
    foreach (var meta in Directory.EnumerateFiles(src, "*.meta", SearchOption.AllDirectories))
    {
        using var r = new StreamReader(meta);
        for (int i = 0; i < 3; i++)
        {
            var l = r.ReadLine();
            if (l == null) break;
            if (l.StartsWith("guid: ")) { map[l.Substring(6).Trim()] = meta.Substring(0, meta.Length - 5); break; }
        }
    }
    File.WriteAllLines(cache, map.Select(kv => kv.Key + "\t" + kv.Value));
}
Console.WriteLine($"guids: {map.Count}");

var textExt = new HashSet<string> { ".prefab", ".mat", ".controller", ".anim", ".asset", ".overrideController", ".mask", ".playable" };
var skipExt = new HashSet<string> { ".cs", ".dll", ".ogg", ".wav", ".mp3", ".shader", ".unity" };
var guidRe = new Regex(@"guid: ([0-9a-f]{32}), type: (\d)");
var queue = new Queue<string>(args.Skip(2).Select(a => Path.Combine(src, a)));
var done = new HashSet<string>();
bool first = true;
long bytes = 0;
while (queue.Count > 0)
{
    var file = queue.Dequeue();
    if (!done.Add(file)) continue;
    var ext = Path.GetExtension(file);
    var rel = Path.GetRelativePath(src, file);
    var outFile = Path.Combine(dst, rel);
    Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
    File.Copy(file, outFile, true);
    if (File.Exists(file + ".meta")) File.Copy(file + ".meta", outFile + ".meta", true);
    bytes += new FileInfo(file).Length;
    if (textExt.Contains(ext))
    {
        string text;
        try { text = File.ReadAllText(file); } catch { continue; }
        if (!text.StartsWith("%YAML")) { first = false; continue; }
        foreach (Match m in guidRe.Matches(text))
        {
            if (!map.TryGetValue(m.Groups[1].Value, out var dep)) continue;
            var dext = Path.GetExtension(dep);
            if (skipExt.Contains(dext)) continue;
            if (dext == ".prefab") continue; // beams, sounds, effects aren't needed
            queue.Enqueue(dep);
        }
    }
    first = false;
}
Console.WriteLine($"copied {done.Count} files, {bytes / 1048576.0:F1} MB");
foreach (var f in done.OrderBy(x => x)) Console.WriteLine("  " + Path.GetRelativePath(src, f));
