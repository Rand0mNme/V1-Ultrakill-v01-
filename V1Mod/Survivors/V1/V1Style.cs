using System.Collections.Generic;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;
using V1Mod.Modules;
using V1Mod.Survivors.V1.Weapons;

namespace V1Mod.Survivors.V1
{
    // Style points (P) as in ULTRAKILL (StyleHUD.AddPoints): the SmileOS shop currency instead of RoR2 gold,
    // which is lost between stages. Points accumulate on the CharacterMaster and last the whole run.
    // Awards from ULTRAKILL: kill 45, big kill (elite) 100, boss 500, airborne +50, double +25, triple and more +50,
    // parry 100, coin ricochet 50, parried projectile/core 50, hit 4 (at most once per 0.1 s per target).
    // On the right of the screen: a feed of bonuses like "+ KILL" and a points counter, like the StyleHUD list.
    public static class V1Style
    {
        public class Points : MonoBehaviour
        {
            public int total;
        }

        private class Tracked
        {
            public float lastHit;
        }

        private static readonly Dictionary<HealthComponent, Tracked> recentVictims = new Dictionary<HealthComponent, Tracked>();
        private static readonly Dictionary<HealthComponent, float> hitCooldown = new Dictionary<HealthComponent, float>();
        private static readonly List<float> recentKills = new List<float>();
        private static float nextHudCheck;

        public static void Init()
        {
            GlobalEventManager.onClientDamageNotified += OnDamage;
            Run.onRunStartGlobal += _ => ResetFreshness();
            RoR2Application.onUpdate += Update;
        }

        public static Points PointsOf(CharacterBody body)
        {
            CharacterMaster master = body ? body.master : null;
            if (!master)
            {
                return null;
            }
            Points p = master.GetComponent<Points>();
            return p ? p : master.gameObject.AddComponent<Points>();
        }

        public static int Total(CharacterBody body)
        {
            Points p = PointsOf(body);
            return p ? p.total : 0;
        }

        public static bool Spend(CharacterBody body, int cost)
        {
            Points p = PointsOf(body);
            if (!p || p.total < cost)
            {
                return false;
            }
            p.total -= cost;
            return true;
        }

        // award points to your own V1 (only on the player's machine) and show a line in the feed
        // Weapon freshness (StyleHUD freshness): each weapon has 0..10, its points are multiplied:
        // from 7.5 FRESH x1.5, from 5 USED x1, from 1 STALE x0.5, below that DULL x0.
        // A point for a weapon lowers its freshness by 0.7 x the action multiplier (kill 0.3, hit 0.15, etc.),
        // and adds 0.5 x the same multiplier to the other weapons (from other slots); out of hand +0.1 per second.
        public const float FreshMax = 10f;
        private static readonly Dictionary<int, float> freshness = new Dictionary<int, float>();

        public static float Freshness(int weapon)
        {
            return freshness.TryGetValue(weapon, out float f) ? f : FreshMax;
        }

        public static float FreshMultiplier(float f) => f >= 7.5f ? 1.5f : f >= 5f ? 1f : f >= 1f ? 0.5f : 0f;
        public static string FreshName(float f) => f >= 7.5f ? "FRESH" : f >= 5f ? "USED" : f >= 1f ? "STALE" : "DULL";

        private static int Slot(int weapon)
        {
            if (V1ViewModel.IsNailgun(weapon)) return 2;
            switch (weapon)
            {
                case V1ViewModel.WeaponShotgun: case V1ViewModel.WeaponShotgunPump: case V1ViewModel.WeaponShotgunSaw: return 1;
                case V1ViewModel.WeaponRailElectric: case V1ViewModel.WeaponRailScrewdriver: case V1ViewModel.WeaponRailMalicious: return 3;
                case V1ViewModel.WeaponRocketFreeze: case V1ViewModel.WeaponRocketCannonball: case V1ViewModel.WeaponRocketNapalm: return 4;
                default: return 0;
            }
        }

        // freshness is on only when you have weapons in at least two slots: otherwise there's nothing to switch to and it would just run dry
        public static bool FreshnessActive(CharacterBody body)
        {
            if (!body)
            {
                return false;
            }
            int firstSlot = -1;
            for (int w = 0; w < V1ViewModel.WeaponViewCount; w++)
            {
                if (!Weapons.V1Shop.Owns(body, w))
                {
                    continue;
                }
                int s = Slot(w);
                if (firstSlot < 0)
                {
                    firstSlot = s;
                }
                else if (s != firstSlot)
                {
                    return true;
                }
            }
            return false;
        }

        // points for an action with the weapon in hand: with freshness
        public static void AddWeapon(CharacterBody body, int points, string label, float decayMultiplier)
        {
            Weapons.V1Arsenal arsenal = body ? body.GetComponent<Weapons.V1Arsenal>() : null;
            if (!arsenal || !body.hasEffectiveAuthority || !FreshnessActive(body))
            {
                Add(body, points, label);
                return;
            }
            int weapon = arsenal.Current;
            float f = Freshness(weapon);
            int scaled = Mathf.RoundToInt(points * FreshMultiplier(f));
            freshness[weapon] = Mathf.Clamp(f - 0.7f * decayMultiplier, 0f, FreshMax);
            for (int w = 0; w < V1ViewModel.WeaponViewCount; w++)
            {
                if (w != weapon && Slot(w) != Slot(weapon))
                {
                    freshness[w] = Mathf.Clamp(Freshness(w) + 0.5f * decayMultiplier, 0f, FreshMax);
                }
            }
            Add(body, scaled, label);
        }

        // out of hand: freshness comes back
        public static void TickFreshness(int current, float dt)
        {
            for (int w = 0; w < V1ViewModel.WeaponViewCount; w++)
            {
                if (w != current)
                {
                    freshness[w] = Mathf.Min(FreshMax, Freshness(w) + 0.1f * dt);
                }
            }
        }

        public static void ResetFreshness()
        {
            freshness.Clear();
        }

        public static void Add(CharacterBody body, int points, string label)
        {
            if (!body || !body.hasEffectiveAuthority || !body.GetComponent<V1GreyHealth>())
            {
                return;
            }
            Points p = PointsOf(body);
            if (!p)
            {
                return;
            }
            p.total += points;
            V1StyleHud hud = V1StyleHud.Get(body);
            if (hud)
            {
                hud.AddMeter(points);
                if (label != null)
                {
                    hud.Push("+ " + label);
                }
            }
        }

        private static CharacterBody LocalV1()
        {
            LocalUser user = LocalUserManager.GetFirstLocalUser();
            CharacterBody body = user != null ? user.cachedBody : null;
            return body && body.GetComponent<V1GreyHealth>() ? body : null;
        }

        private static void OnDamage(DamageDealtMessage msg)
        {
            CharacterBody me = LocalV1();
            if (!me || !msg.attacker || msg.attacker != me.gameObject || !msg.victim || msg.isSilent)
            {
                return;
            }
            HealthComponent hc = msg.victim.GetComponent<HealthComponent>();
            if (!hc || hc.body == me)
            {
                return;
            }
            if (!recentVictims.TryGetValue(hc, out Tracked t))
            {
                t = new Tracked();
                recentVictims[hc] = t;
            }
            t.lastHit = Time.time;
            if (!hitCooldown.TryGetValue(hc, out float next) || Time.time >= next)
            {
                hitCooldown[hc] = Time.time + 0.1f;
                AddWeapon(me, 4, null, 0.15f);
            }
            CheckKill(me, hc, t);
        }

        // on a client the victim's health arrives slightly after the damage message, so death is checked every frame
        private static void Update()
        {
            if (Time.unscaledTime >= nextHudCheck)
            {
                nextHudCheck = Time.unscaledTime + 1f;
                CharacterBody local = LocalV1();
                if (local)
                {
                    V1StyleHud.Get(local);
                }
            }
            if (recentVictims.Count == 0)
            {
                return;
            }
            CharacterBody me = LocalV1();
            foreach (var kv in new List<KeyValuePair<HealthComponent, Tracked>>(recentVictims))
            {
                if (!kv.Key || Time.time - kv.Value.lastHit > 2f)
                {
                    recentVictims.Remove(kv.Key);
                    hitCooldown.Remove(kv.Key);
                    continue;
                }
                if (me)
                {
                    CheckKill(me, kv.Key, kv.Value);
                }
            }
        }

        private static void CheckKill(CharacterBody me, HealthComponent hc, Tracked t)
        {
            if (hc.alive)
            {
                return;
            }
            recentVictims.Remove(hc);
            hitCooldown.Remove(hc);
            CharacterBody victim = hc.body;
            if (victim && (victim.isChampion || victim.isBoss))
            {
                AddWeapon(me, 500, "BIG KILL", 0.3f);
            }
            else if (victim && victim.isElite)
            {
                AddWeapon(me, 100, "BIG KILL", 0.3f);
            }
            else
            {
                AddWeapon(me, 45, "KILL", 0.3f);
            }
            if (me.characterMotor && !me.characterMotor.isGrounded)
            {
                AddWeapon(me, 50, "AIRSHOT", 1f);
            }
            float now = Time.time;
            recentKills.RemoveAll(k => now - k > 0.75f);
            recentKills.Add(now);
            if (recentKills.Count == 2)
            {
                AddWeapon(me, 25, "DOUBLE KILL", 0.1f);
            }
            else if (recentKills.Count >= 3)
            {
                AddWeapon(me, 50, recentKills.Count == 3 ? "TRIPLE KILL" : "MULTIKILL x" + recentKills.Count, 0.1f);
            }
        }
    }

    // style bonus feed on the right of the screen and the points counter
    public class V1StyleHud : MonoBehaviour
    {
        private CharacterBody body;
        private Text total;
        private Image panel;
        private Image rankImage;
        private RectTransform barFill;
        private Image freshBar;
        private Text freshText;
        private float meter;
        private int rank;
        private bool combo;
        private float rankScale;
        // StyleHUD.ranks: meter to the next rank and drain speed (x15 per second)
        private static readonly int[] MaxMeter = { 200, 300, 400, 500, 700, 850, 1000, 1500 };
        private static readonly float[] Drain = { 1f, 1.25f, 1.5f, 2f, 3f, 4f, 6f, 8f };
        private static readonly string[] RankSprites = { "RankD", "RankC", "RankB", "RankA", "RankS", "RankSS", "RankSSS", "RankU" };
        private readonly List<(Text text, float time)> lines = new List<(Text, float)>();
        private RectTransform feed;

        public static V1StyleHud Get(CharacterBody body)
        {
            HUD hud = V1ShopWindow.FindHud(body);
            if (!hud || !hud.mainContainer)
            {
                return null;
            }
            V1StyleHud h = hud.mainContainer.GetComponentInChildren<V1StyleHud>();
            if (h)
            {
                return h;
            }
            GameObject go = new GameObject("V1Style", typeof(RectTransform));
            go.transform.SetParent(hud.mainContainer.transform, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-40f, -60f);
            rt.sizeDelta = new Vector2(320f, 300f);
            h = go.AddComponent<V1StyleHud>();
            h.body = body;
            // like StyleHUD: a translucent panel, the rank on top, the meter below it, the bonus feed below that
            h.panel = go.AddComponent<Image>();
            h.panel.color = new Color(0f, 0f, 0f, 0.35f);
            GameObject ri = new GameObject("Rank", typeof(RectTransform));
            ri.transform.SetParent(rt, false);
            h.rankImage = ri.AddComponent<Image>();
            h.rankImage.preserveAspect = true;
            RectTransform rr = (RectTransform)ri.transform;
            rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(0.5f, 1f);
            rr.anchoredPosition = new Vector2(0f, -6f);
            rr.sizeDelta = new Vector2(300f, 100f);
            GameObject bg = new GameObject("Bar", typeof(RectTransform));
            bg.transform.SetParent(rt, false);
            bg.AddComponent<Image>().color = Color.black;
            RectTransform br = (RectTransform)bg.transform;
            br.anchorMin = br.anchorMax = br.pivot = new Vector2(0.5f, 1f);
            br.anchoredPosition = new Vector2(0f, -110f);
            br.sizeDelta = new Vector2(290f, 16f);
            GameObject fill = new GameObject("Fill", typeof(RectTransform));
            fill.transform.SetParent(br, false);
            fill.AddComponent<Image>().color = Color.white;
            h.barFill = (RectTransform)fill.transform;
            h.barFill.anchorMin = new Vector2(0f, 0f);
            h.barFill.anchorMax = new Vector2(0f, 1f);
            h.barFill.pivot = new Vector2(0f, 0.5f);
            h.barFill.offsetMin = new Vector2(2f, 2f);
            h.barFill.offsetMax = new Vector2(2f, -2f);
            // freshness bar of the weapon in hand (FRESH: 1.50x)
            GameObject fb = new GameObject("Fresh", typeof(RectTransform));
            fb.transform.SetParent(rt, false);
            h.freshBar = fb.AddComponent<Image>();
            RectTransform fr = (RectTransform)fb.transform;
            fr.anchorMin = fr.anchorMax = fr.pivot = new Vector2(0.5f, 0f);
            fr.anchoredPosition = new Vector2(0f, 40f);
            fr.sizeDelta = new Vector2(280f, 30f);
            h.freshText = V1ShopWindow.MakeText(fr, "", 24, Color.white, TextAnchor.MiddleCenter);
            h.freshText.rectTransform.anchorMin = Vector2.zero;
            h.freshText.rectTransform.anchorMax = Vector2.one;
            h.freshText.rectTransform.sizeDelta = Vector2.zero;
            h.total = V1ShopWindow.MakeText(rt, "", 24, Color.white, TextAnchor.LowerRight);
            h.total.rectTransform.anchorMin = h.total.rectTransform.anchorMax = h.total.rectTransform.pivot = new Vector2(1f, 0f);
            h.total.rectTransform.anchoredPosition = new Vector2(-8f, 4f);
            h.total.rectTransform.sizeDelta = new Vector2(300f, 30f);
            GameObject f = new GameObject("Feed", typeof(RectTransform));
            f.transform.SetParent(rt, false);
            h.feed = (RectTransform)f.transform;
            h.feed.anchorMin = h.feed.anchorMax = h.feed.pivot = new Vector2(1f, 1f);
            h.feed.anchoredPosition = new Vector2(-8f, -134f);
            h.SetVisible(false);
            return h;
        }

        // StyleHUD.AddPoints / AscendRank: points fill the meter, when full the next rank (with a 1/4 carry-over)
        public void AddMeter(int points)
        {
            if (!combo)
            {
                combo = true;
                rank = 0;
                meter = 0f;
                SetVisible(true);
            }
            meter += points;
            rankScale = 0.2f;
            while (meter >= MaxMeter[rank] && rank < MaxMeter.Length - 1)
            {
                meter -= MaxMeter[rank];
                rank++;
                meter = Mathf.Max(meter, MaxMeter[rank] / 4f);
            }
            meter = Mathf.Min(meter, MaxMeter[rank]);
        }

        private void SetVisible(bool on)
        {
            panel.enabled = on;
            if (!on)
            {
                freshBar.gameObject.SetActive(false);
            }
            rankImage.enabled = on && rankImage.sprite;
            barFill.parent.gameObject.SetActive(on);
        }

        public void Push(string label)
        {
            Text t = V1ShopWindow.MakeText(feed, label, 22, Color.white, TextAnchor.UpperRight);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = t.rectTransform.pivot = new Vector2(1f, 1f);
            t.rectTransform.sizeDelta = new Vector2(320f, 26f);
            lines.Insert(0, (t, Time.time));
            while (lines.Count > 8)
            {
                Destroy(lines[lines.Count - 1].text.gameObject);
                lines.RemoveAt(lines.Count - 1);
            }
        }

        private void Update()
        {
            if (!body)
            {
                Destroy(gameObject);
                return;
            }
            total.text = V1Style.Total(body) + " P";
            if (combo)
            {
                meter -= Time.deltaTime * Drain[rank] * 15f;
                if (meter < 0f)
                {
                    // DescendRank: one rank down with the meter at 3/4; from the lowest rank the combo is over
                    if (rank > 0)
                    {
                        rank--;
                        meter = MaxMeter[rank] * 0.75f;
                    }
                    else
                    {
                        combo = false;
                        meter = 0f;
                        SetVisible(false);
                    }
                }
                Weapons.V1Arsenal arsenal = body.GetComponent<Weapons.V1Arsenal>();
                bool freshOn = combo && arsenal && V1Style.FreshnessActive(body);
                if (freshBar.gameObject.activeSelf != freshOn)
                {
                    freshBar.gameObject.SetActive(freshOn);
                }
                if (freshOn)
                {
                    V1Style.TickFreshness(arsenal.Current, Time.deltaTime);
                    float f = V1Style.Freshness(arsenal.Current);
                    freshText.text = V1Style.FreshName(f) + ": " + V1Style.FreshMultiplier(f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "x";
                    freshBar.color = f >= 7.5f ? new Color(1f, 0.65f, 0f, 0.9f) : f >= 5f ? new Color(0.9f, 0.9f, 0.9f, 0.9f) : f >= 1f ? new Color(0.6f, 0.4f, 0.2f, 0.9f) : new Color(0.3f, 0.3f, 0.3f, 0.9f);
                }
                if (combo)
                {
                    V1Assets.styleRanks.TryGetValue(RankSprites[rank], out Sprite s);
                    rankImage.sprite = s;
                    rankImage.enabled = s;
                    rankScale = Mathf.MoveTowards(rankScale, 0f, Time.deltaTime);
                    rankImage.rectTransform.localScale = Vector3.one * (1f + rankScale);
                    barFill.sizeDelta = new Vector2(286f * Mathf.Clamp01(meter / MaxMeter[rank]), barFill.sizeDelta.y);
                }
            }
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                float age = Time.time - lines[i].time;
                if (age > 4f || !lines[i].text)
                {
                    if (lines[i].text)
                    {
                        Destroy(lines[i].text.gameObject);
                    }
                    lines.RemoveAt(i);
                    continue;
                }
                lines[i].text.rectTransform.anchoredPosition = new Vector2(0f, -i * 26f);
                Color c = lines[i].text.color;
                c.a = Mathf.Clamp01(4f - age);
                lines[i].text.color = c;
            }
        }
    }
}
