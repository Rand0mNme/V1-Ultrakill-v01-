using System.Collections.Generic;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // Weapon shop like the SmileOS terminal from ULTRAKILL.
    // A run starts with the Piercer, the other variants are bought with RoR2 gold at the terminal
    // that stands at V1's landing spot on every stage. Prices come from the ULTRAKILL shop (300 P = 1 gold)
    // and go up with stages, like chests. Base variants, which you find in levels in ULTRAKILL, are cheaper.
    // Purchases are stored on the player's CharacterMaster and last until the end of the run.
    // V1Config.UnlockAllWeapons = true: everything is unlocked at once, no terminal.
    public static class V1Shop
    {
        public class Entry
        {
            public int weapon;
            public string name;
            public string icon;
            public int points;
            public Color color;
        }

        private static readonly Color Blue = V1ViewModel.PiercerColor;
        private static readonly Color Green = V1ViewModel.MarksmanColor;
        private static readonly Color Red = V1ViewModel.SharpshooterColor;

        public static readonly string[] Categories = { "Revolver", "Shotgun", "Nailgun", "Railcannon", "Rocket Launcher" };

        public static readonly Entry[][] Entries =
        {
            new[]
            {
                new Entry { weapon = V1ViewModel.WeaponPiercer, name = "Piercer", icon = "SingleRevolver", points = 0, color = Blue },
                new Entry { weapon = V1ViewModel.WeaponMarksman, name = "Marksman", icon = "RevolverSpecial", points = 7500, color = Green },
                new Entry { weapon = V1ViewModel.WeaponSharpshooter, name = "Sharpshooter", icon = "RevolverSharp", points = 25000, color = Red },
            },
            new[]
            {
                new Entry { weapon = V1ViewModel.WeaponShotgun, name = "Core Eject", icon = "Shotgun", points = 7500, color = Blue },
                new Entry { weapon = V1ViewModel.WeaponShotgunPump, name = "Pump Charge", icon = "Shotgun1", points = 12500, color = Green },
                new Entry { weapon = V1ViewModel.WeaponShotgunSaw, name = "Sawed-On", icon = "ShotgunSaw", points = 25000, color = Red },
            },
            new[]
            {
                new Entry { weapon = V1ViewModel.WeaponNailMagnet, name = "Attractor", icon = "Nailgun", points = 15000, color = Blue },
                new Entry { weapon = V1ViewModel.WeaponNailOverheat, name = "Overheat", icon = "NailgunOverheat", points = 25000, color = Green },
                new Entry { weapon = V1ViewModel.WeaponNailZapper, name = "Jumpstart", icon = "NailgunZapper", points = 35000, color = Red },
            },
            new[]
            {
                new Entry { weapon = V1ViewModel.WeaponRailElectric, name = "Electric", icon = "Railcannon", points = 50000, color = Blue },
                new Entry { weapon = V1ViewModel.WeaponRailScrewdriver, name = "Screwdriver", icon = "railcannonscrew", points = 100000, color = Green },
                new Entry { weapon = V1ViewModel.WeaponRailMalicious, name = "Malicious", icon = "railcannonmalicious", points = 100000, color = Red },
            },
            new[]
            {
                new Entry { weapon = V1ViewModel.WeaponRocketFreeze, name = "Freezeframe", icon = "rocketlauncher", points = 40000, color = Blue },
                new Entry { weapon = V1ViewModel.WeaponRocketCannonball, name = "S.R.S. Cannon", icon = "rocketlaunchercannon", points = 75000, color = Green },
                new Entry { weapon = V1ViewModel.WeaponRocketNapalm, name = "Firestarter", icon = "rocketlaunchergasoline", points = 75000, color = Red },
            },
        };

        // price in style points: ULTRAKILL price (P) x the config multiplier (0.1 by default: a RoR2 stage is shorter than an ULTRAKILL level)
        public static int Cost(Entry e)
        {
            return Mathf.Max(1, Mathf.RoundToInt(e.points * V1Config.StylePriceMultiplier.Value));
        }

        // the player's purchases (on CharacterMaster: survives stage changes, a new run means a new master)
        public class Owned : MonoBehaviour
        {
            public readonly HashSet<int> weapons = new HashSet<int> { V1ViewModel.WeaponPiercer };
            // nailgun slot: false = nailguns, true = sawblade launchers (Sawblade Launcher)
            public bool sawMode;
        }

        public static Owned OwnedOf(CharacterBody body)
        {
            CharacterMaster master = body ? body.master : null;
            if (!master)
            {
                return null;
            }
            Owned o = master.GetComponent<Owned>();
            return o ? o : master.gameObject.AddComponent<Owned>();
        }

        public static bool Owns(CharacterBody body, int weapon)
        {
            // a sawblade launcher is the same purchase as the nailgun of the same variant
            if (V1ViewModel.IsSawLauncher(weapon))
            {
                weapon = V1ViewModel.WeaponNailMagnet + V1ViewModel.NailVariant(weapon);
            }
            if (V1Config.UnlockAllWeapons.Value || FullArsenal(body))
            {
                return true;
            }
            Owned o = OwnedOf(body);
            return !o || o.weapons.Contains(weapon);
        }

        // "Full Arsenal" is selected in the loadout
        public static bool FullArsenal(CharacterBody body)
        {
            GenericSkill s = body ? V1Skills.ArsenalSkill(body) : null;
            return s && s.baseSkill == V1Skills.arsenalFullDef;
        }

        public static bool SawMode(CharacterBody body)
        {
            Owned o = OwnedOf(body);
            return o && o.sawMode;
        }

        public static void ToggleSawMode(CharacterBody body)
        {
            Owned o = OwnedOf(body);
            if (!o)
            {
                return;
            }
            o.sawMode = !o.sawMode;
            body.GetComponent<V1Arsenal>()?.RefreshNailForm();
        }

        // --- purchase: the server takes the gold ---
        public const short MsgBuy = 3741;

        public class BuyMessage : MessageBase
        {
            public NetworkInstanceId master;
            public int cost;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(master);
                writer.Write(cost);
            }

            public override void Deserialize(NetworkReader reader)
            {
                master = reader.ReadNetworkId();
                cost = reader.ReadInt32();
            }
        }

        public static void Init()
        {
            RoR2.Networking.NetworkManagerSystem.onStartServerGlobal += () => NetworkServer.RegisterHandler(MsgBuy, OnBuyMessage);
        }

        private static void OnBuyMessage(NetworkMessage msg)
        {
            BuyMessage m = msg.ReadMessage<BuyMessage>();
            GameObject go = NetworkServer.FindLocalObject(m.master);
            CharacterMaster master = go ? go.GetComponent<CharacterMaster>() : null;
            if (master && m.cost > 0 && master.money >= m.cost)
            {
                master.money -= (uint)m.cost;
            }
        }

        public static bool TryBuy(CharacterBody body, Entry e)
        {
            CharacterMaster master = body ? body.master : null;
            Owned owned = OwnedOf(body);
            if (!master || !owned || owned.weapons.Contains(e.weapon))
            {
                return false;
            }
            if (!V1Style.Spend(body, Cost(e)))
            {
                return false;
            }
            owned.weapons.Add(e.weapon);
            return true;
        }

        // --- terminal at the landing spot ---
        private static readonly Dictionary<CharacterBody, GameObject> terminals = new Dictionary<CharacterBody, GameObject>();

        public static void SpawnTerminal(CharacterBody body)
        {
            if (!body || V1Config.UnlockAllWeapons.Value || !V1Assets.shop)
            {
                return;
            }
            if (terminals.TryGetValue(body, out GameObject old) && old)
            {
                Object.Destroy(old);
            }
            Vector3 forward = body.inputBank ? body.inputBank.aimDirection : body.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
            Vector3 pos = body.footPosition + forward * 5f;
            if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                pos = hit.point;
            }
            else
            {
                pos = body.footPosition;
            }
            // the screen faces the player (the model's front is +Z)
            Vector3 toPlayer = body.footPosition - pos;
            toPlayer.y = 0f;
            Quaternion rot = Quaternion.LookRotation(toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : -forward);
            GameObject go = Object.Instantiate(V1Assets.shop, pos, rot);
            V1Assets.ToWorldMaterials(go);
            go.transform.localScale = Vector3.one * 0.75f;
            V1ShopTerminal t = go.AddComponent<V1ShopTerminal>();
            t.owner = body;
            // the booth is solid (only for this player: the terminal isn't networked)
            Bounds b = new Bounds(pos + Vector3.up, Vector3.one);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                b.Encapsulate(r.bounds);
            }
            GameObject col = new GameObject("Collider");
            col.layer = LayerIndex.world.intVal;
            col.transform.SetParent(go.transform, false);
            BoxCollider box = col.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center);
            box.size = new Vector3(b.size.x, b.size.y, b.size.z) / 0.75f * 0.8f;
            terminals[body] = go;
        }
    }

    // terminal: walk up, look at the screen and press E (the RoR2 interact button)
    public class V1ShopTerminal : MonoBehaviour
    {
        public CharacterBody owner;
        private V1ShopWindow window;
        private Text prompt;

        private Vector3 ScreenPoint => transform.position + Vector3.up * 2f * transform.localScale.y;

        private void Update()
        {
            if (!owner)
            {
                Destroy(gameObject);
                return;
            }
            if (!owner.hasEffectiveAuthority)
            {
                return;
            }
            Vector3 to = ScreenPoint - owner.corePosition;
            Vector3 aim = owner.inputBank ? owner.inputBank.aimDirection : owner.transform.forward;
            bool near = to.magnitude < 5f && Vector3.Dot(aim, to.normalized) > 0.5f;
            if (window)
            {
                if (to.magnitude > 7f || !owner.healthComponent || !owner.healthComponent.alive)
                {
                    window.Close();
                }
                return;
            }
            Prompt(near);
            if (near && owner.inputBank && owner.inputBank.interact.justPressed && Time.unscaledTime - V1ShopWindow.lastClose > 0.4f)
            {
                window = V1ShopWindow.Open(owner);
            }
        }

        private void Prompt(bool show)
        {
            if (!show)
            {
                if (prompt)
                {
                    prompt.gameObject.SetActive(false);
                }
                return;
            }
            if (!prompt)
            {
                HUD hud = V1ShopWindow.FindHud(owner);
                if (!hud)
                {
                    return;
                }
                GameObject go = new GameObject("V1ShopPrompt", typeof(RectTransform));
                go.transform.SetParent(hud.mainContainer.transform, false);
                RectTransform rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, -80f);
                rt.sizeDelta = new Vector2(400f, 40f);
                prompt = V1ShopWindow.MakeText(go.transform, "[E] SmileOS 2.0", 24, Color.white, TextAnchor.MiddleCenter);
                prompt.rectTransform.anchorMin = Vector2.zero;
                prompt.rectTransform.anchorMax = Vector2.one;
                prompt.rectTransform.sizeDelta = Vector2.zero;
                prompt = go.GetComponentInChildren<Text>();
            }
            prompt.gameObject.SetActive(true);
            prompt.transform.parent.gameObject.SetActive(true);
        }

        private void OnDestroy()
        {
            if (prompt)
            {
                Destroy(prompt.transform.parent.gameObject);
            }
            if (window)
            {
                window.Close();
            }
        }
    }

    // SmileOS 2.0 window: weapons on the left, their three variants with prices on the right
    public class V1ShopWindow : MonoBehaviour
    {
        private static readonly Color WindowColor = new Color(0.16f, 0.16f, 0.16f, 0.97f);
        private static readonly Color PanelColor = new Color(0.09f, 0.09f, 0.09f, 1f);
        private static readonly Color TitleColor = new Color(0.62f, 0.09f, 0.07f, 1f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.22f, 0.22f, 1f);
        private static readonly Color ButtonSelected = new Color(0.36f, 0.36f, 0.36f, 1f);

        private CharacterBody body;
        private int category;
        private Text money;
        private readonly List<Image> categoryButtons = new List<Image>();
        private RectTransform variantPanel;
        private readonly List<(V1Shop.Entry entry, Text price, Image button)> rows = new List<(V1Shop.Entry, Text, Image)>();

        public static HUD FindHud(CharacterBody body)
        {
            foreach (HUD hud in HUD.readOnlyInstanceList)
            {
                if (hud && hud.targetBodyObject == body.gameObject)
                {
                    return hud;
                }
            }
            return null;
        }

        public static V1ShopWindow Open(CharacterBody body)
        {
            HUD hud = FindHud(body);
            if (!hud || !hud.mainContainer)
            {
                return null;
            }
            GameObject root = new GameObject("V1SmileOS", typeof(RectTransform));
            root.transform.SetParent(hud.mainContainer.transform, false);
            RectTransform rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            // cursor and mouse clicks through the RoR2 event system; the character can't be controlled while the window is open
            root.AddComponent<MPEventSystemLocator>();
            root.AddComponent<CursorOpener>();
            V1ShopWindow w = root.AddComponent<V1ShopWindow>();
            w.body = body;
            w.Build();
            V1Sounds.Play("SmileOSStartup", body.gameObject, 0.5f, 1f, true);
            return w;
        }

        public static float lastClose;

        public void Close()
        {
            lastClose = Time.unscaledTime;
            Destroy(gameObject);
        }

        public static Text MakeText(Transform parent, string text, int size, Color color, TextAnchor anchor)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Text t = go.AddComponent<Text>();
            t.font = V1Assets.shopFont ? V1Assets.shopFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            Image img = go.AddComponent<Image>();
            img.color = color;
            return rt;
        }

        private static void Fill(RectTransform t)
        {
            t.anchorMin = Vector2.zero;
            t.anchorMax = Vector2.one;
            t.sizeDelta = Vector2.zero;
            t.anchoredPosition = Vector2.zero;
        }

        private Image MakeButton(Transform parent, Vector2 pos, Vector2 size, string label, int fontSize, System.Action onClick)
        {
            RectTransform rt = Rect(parent, "Button", pos, size, ButtonColor);
            Button b = rt.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => onClick());
            Text t = MakeText(rt, label, fontSize, Color.white, TextAnchor.MiddleCenter);
            Fill(t.rectTransform);
            return rt.GetComponent<Image>();
        }

        private void Build()
        {
            RectTransform window = Rect(transform, "Window", Vector2.zero, new Vector2(760f, 470f), WindowColor);
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(0.5f, 0.5f);

            RectTransform title = Rect(window, "Title", new Vector2(4f, -4f), new Vector2(752f, 36f), TitleColor);
            Text titleText = MakeText(title, "  SmileOS 2.0", 24, Color.white, TextAnchor.MiddleLeft);
            Fill(titleText.rectTransform);
            money = MakeText(title, "", 24, new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleRight);
            Fill(money.rectTransform);
            money.rectTransform.offsetMax = new Vector2(-50f, 0f);
            MakeButton(title, new Vector2(716f, -4f), new Vector2(32f, 28f), "X", 24, Close);

            RectTransform left = Rect(window, "Weapons", new Vector2(8f, -48f), new Vector2(220f, 414f), PanelColor);
            Text logo = MakeText(left, "Smile", 48, Color.white, TextAnchor.UpperLeft);
            logo.rectTransform.anchorMin = logo.rectTransform.anchorMax = logo.rectTransform.pivot = new Vector2(0f, 1f);
            logo.rectTransform.anchoredPosition = new Vector2(14f, -6f);
            logo.rectTransform.sizeDelta = new Vector2(200f, 60f);
            for (int i = 0; i < V1Shop.Categories.Length; i++)
            {
                int c = i;
                categoryButtons.Add(MakeButton(left, new Vector2(10f, -80f - i * 64f), new Vector2(200f, 54f), V1Shop.Categories[i], 22, () => SelectCategory(c)));
            }

            variantPanel = Rect(window, "Variations", new Vector2(236f, -48f), new Vector2(516f, 414f), PanelColor);
            SelectCategory(0);
        }

        private void SelectCategory(int c)
        {
            if (category != c || rows.Count == 0)
            {
                V1Sounds.Play("SmileOSClick", body ? body.gameObject : null, 0.5f, 1f, true);
            }
            category = c;
            for (int i = 0; i < categoryButtons.Count; i++)
            {
                categoryButtons[i].color = i == c ? ButtonSelected : ButtonColor;
            }
            foreach (Transform child in variantPanel)
            {
                Destroy(child.gameObject);
            }
            rows.Clear();
            Text header = MakeText(variantPanel, V1Shop.Categories[c], 32, Color.white, TextAnchor.UpperLeft);
            header.rectTransform.anchorMin = header.rectTransform.anchorMax = header.rectTransform.pivot = new Vector2(0f, 1f);
            header.rectTransform.anchoredPosition = new Vector2(16f, -10f);
            header.rectTransform.sizeDelta = new Vector2(480f, 40f);
            if (c == 2)
            {
                // alternative: sawblade launchers (Sawblade Launcher) instead of nailguns
                Image toggle = MakeButton(variantPanel, new Vector2(256f, -8f), new Vector2(250f, 36f), "", 20, () =>
                {
                    V1Shop.ToggleSawMode(body);
                    V1Sounds.Play("SmileOSClick", body ? body.gameObject : null, 0.5f, 1.2f, true);
                    SelectCategory(2);
                });
                toggle.GetComponentInChildren<Text>().text = V1Shop.SawMode(body) ? "Sawblade Launcher: ON" : "Sawblade Launcher: OFF";
                header.text = V1Shop.SawMode(body) ? "Sawblade Launcher" : "Nailgun";
            }
            V1Shop.Entry[] entries = V1Shop.Entries[c];
            for (int i = 0; i < entries.Length; i++)
            {
                V1Shop.Entry e = entries[i];
                RectTransform row = Rect(variantPanel, "Row", new Vector2(10f, -60f - i * 116f), new Vector2(496f, 106f), WindowColor);
                RectTransform iconBox = Rect(row, "Icon", new Vector2(8f, -8f), new Vector2(90f, 90f), PanelColor);
                if (V1Assets.weaponIcons.TryGetValue(e.icon, out Sprite sprite) && sprite)
                {
                    RectTransform icon = Rect(iconBox, "Sprite", Vector2.zero, Vector2.zero, e.color);
                    Fill(icon);
                    Image img = icon.GetComponent<Image>();
                    img.sprite = sprite;
                    img.preserveAspect = true;
                }
                Text name = MakeText(row, e.name, 26, e.color, TextAnchor.UpperLeft);
                name.rectTransform.anchorMin = name.rectTransform.anchorMax = name.rectTransform.pivot = new Vector2(0f, 1f);
                name.rectTransform.anchoredPosition = new Vector2(112f, -12f);
                name.rectTransform.sizeDelta = new Vector2(370f, 34f);
                Image button = MakeButton(row, new Vector2(112f, -54f), new Vector2(370f, 42f), "", 22, () => Buy(e));
                Text price = button.GetComponentInChildren<Text>();
                rows.Add((e, price, button));
            }
            Refresh();
        }

        private void Buy(V1Shop.Entry e)
        {
            if (V1Shop.TryBuy(body, e))
            {
                V1Sounds.Play("ShopDrawerOpen", body.gameObject, 0.6f, 1f, true);
            }
            else
            {
                V1Sounds.Play("StaminaFail", body ? body.gameObject : null, 0.5f, 0.5f, true);
            }
            Refresh();
        }

        private void Refresh()
        {
            CharacterMaster master = body ? body.master : null;
            int gold = V1Style.Total(body);
            if (money)
            {
                money.text = gold + " P";
            }
            foreach (var (entry, price, button) in rows)
            {
                bool owned = V1Shop.Owns(body, entry.weapon);
                int cost = V1Shop.Cost(entry);
                price.text = owned ? "Already Owned" : cost + " P";
                price.color = owned ? Color.gray : cost > gold ? new Color(1f, 0.3f, 0.3f) : Color.white;
                button.color = owned ? PanelColor : ButtonColor;
            }
        }

        private void Update()
        {
            if (!body)
            {
                Close();
                return;
            }
            Refresh();
            if (V1Config.Down(KeyCode.Escape) || V1Config.Down(KeyCode.E))
            {
                Close();
            }
        }
    }
}
