using R2API;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1
{
    // ULTRAKILL crosshair (Crosshair + HealthBars + StaminaBars), default settings:
    // - 5x5 dot (crossHair = 1, white)
    // - 32x32 ring around it: health on the left (green, up to 0.485 of the circle, clockwise from the bottom, rotated by +3 degrees),
    //   behind it a red "trail" of lost health; health dims after 2 s without changes (FadeOutBars)
    // - 3 stamina bars on the right, 0.15 of the circle each (rotations -3, -62.5, -122 degrees), light blue; while a bar
    //   is filling it is translucent
    // Sizes in ULTRAKILL canvas units (1280x720, matched half by width and half by height) are converted to the RoR2 canvas.
    public static class V1Crosshair
    {
        public static GameObject prefab;

        public static void Init(GameObject bodyPrefab)
        {
            GameObject go = new GameObject("V1Crosshair", typeof(RectTransform));
            // the object is disabled while we fill in the fields: otherwise CrosshairController.Awake fails on empty arrays
            go.SetActive(false);
            go.AddComponent<HudElement>();
            // RoR2 needs a CrosshairController to remove the crosshair on character change; the ULTRAKILL crosshair has no spread
            CrosshairController cc = go.AddComponent<CrosshairController>();
            cc.spriteSpreadPositions = new CrosshairController.SpritePosition[0];
            cc.skillStockSpriteDisplays = new CrosshairController.SkillStockSpriteDisplay[0];
            cc.remapSprites = new RawImage[0];
            go.AddComponent<V1CrosshairHud>();
            go.SetActive(true);
            prefab = PrefabAPI.InstantiateClone(go, "V1Crosshair", false);
            Object.Destroy(go);
            bodyPrefab.GetComponent<CharacterBody>().defaultCrosshairPrefab = prefab;
        }
    }

    public class V1CrosshairHud : MonoBehaviour
    {
        public static readonly Color HealthColor = new Color(0.26666668f, 1f, 0.27058825f, 1f);
        public static readonly Color AfterImageColor = new Color(1f, 0f, 0f, 1f);
        public static readonly Color StaminaColor = new Color(0f, 0.8745098f, 1f, 1f);
        public static readonly Color StaminaChargingColor = new Color(0f, 0.8745098f, 1f, 0.6f);

        private HudElement hud;
        private RectTransform root;
        private Image health;
        private Image afterImage;
        private readonly Image[] stamina = new Image[3];
        private float hp = 100f;
        private float afterHp = 100f;
        private float lastHp = -1f;
        private float staminaShown;
        private float fadeOutTime = 2f;

        private void Awake()
        {
            hud = GetComponent<HudElement>();
            root = (RectTransform)transform;
            // when copying a template the children already exist: rebuild them so references point to our own objects
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(root.GetChild(i).gameObject);
            }

            // the whole crosshair from the ULTRAKILL scene (Player/Canvas/Crosshair Filler/Crosshair), with all its sizes and sprites
            if (!V1Assets.ukCrosshair)
            {
                return;
            }
            GameObject uk = Instantiate(V1Assets.ukCrosshair, root, false);
            uk.name = "Crosshair";
            RectTransform ukRt = (RectTransform)uk.transform;
            ukRt.anchoredPosition = Vector2.zero;
            health = Find(uk, "HealthSlider");
            afterImage = Find(uk, "HealthSliderAfterImage");
            afterImage2 = Find(uk, "HealthSliderAfterImage (1)");
            for (int i = 0; i < 3; i++)
            {
                stamina[i] = Find(uk, "StaminaSlider (" + (i + 1) + ")");
            }
            // four dashes around the dot (crosshair setting "2", as in your ULTRAKILL): they are disabled in the prefab,
            // the Crosshair script turns them on from the player's setting
            for (int i = 1; i <= 4; i++)
            {
                Image line = Find(uk, "Crosshair (" + i + ")");
                if (line)
                {
                    line.enabled = true;
                }
            }
            // ULTRAKILL has no counterparts for these RoR2 things: barrier above 100 HP, buff bar
            // grey health (V1GreyHealth): a grey arc over the health bar
            antiHpArc = Find(uk, "AntiHPSlider");
            Hide(uk, "SuperchargeSlider");
            Hide(uk, "PowerUpBar");
        }

        private Image afterImage2;
        private Image antiHpArc;
        private float antiHpShown;
        // on request: the crosshair is 30% smaller than in ULTRAKILL
        public const float SizeMultiplier = 0.7f;

        private static Image Find(GameObject root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t.GetComponent<Image>();
                }
            }
            return null;
        }

        private static void Hide(GameObject root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    t.gameObject.SetActive(false);
                }
            }
        }

        private Image MakeImage(string name, RectTransform parent, Sprite sprite, Color color, Vector2 size, float rotZ)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            rt.localEulerAngles = new Vector3(0f, 0f, rotZ);
            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        private void MakeLine(string name, Vector2 size, Vector2 pos)
        {
            Image img = MakeImage(name, root, V1Assets.crosshairDot, Color.white, size, 0f);
            img.preserveAspect = false;
            img.rectTransform.anchoredPosition = pos;
        }

        private Image MakeArc(string name, RectTransform parent, Color color, float rotZ, bool clockwise, float fill)
        {
            Image img = MakeImage(name, parent, V1Assets.crosshairCircle, color, new Vector2(32f, 32f), rotZ);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Bottom;
            img.fillClockwise = clockwise;
            img.fillAmount = fill;
            return img;
        }

        private void Update()
        {
            // size: ULTRAKILL canvas 1280x720 (width/height match 0.5) -> RoR2 canvas
            Canvas canvas = GetComponentInParent<Canvas>();
            float ukScale = Mathf.Sqrt((Screen.width / 1280f) * (Screen.height / 720f));
            float ror2Scale = canvas ? canvas.scaleFactor : 1f;
            root.localScale = Vector3.one * (ukScale / Mathf.Max(0.01f, ror2Scale)) * SizeMultiplier;


            CharacterBody body = hud ? hud.targetCharacterBody : null;
            if (!body || !body.healthComponent)
            {
                return;
            }
            float dt = Time.deltaTime;

            // HealthBar: hp moves toward the real value (difference x5 + 5 per second), the trail catches up the same way
            float target = body.healthComponent.combinedHealthFraction * 100f;
            // speed uses the absolute difference: otherwise the bar "runs away" on health loss and wraps into a full ring
            hp = Mathf.MoveTowards(hp, target, dt * (Mathf.Abs(target - hp) * 5f + 5f));
            afterHp = Mathf.MoveTowards(afterHp, hp, dt * (Mathf.Abs(afterHp - hp) * 5f + 5f));
            if (!Mathf.Approximately(hp, lastHp))
            {
                fadeOutTime = 2f;
                lastHp = hp;
            }
            fadeOutTime = Mathf.MoveTowards(fadeOutTime, 0f, Time.unscaledDeltaTime);
            float alpha = Mathf.Min(fadeOutTime, 1f);
            SetArc(health, hp / 100f * 0.485f, HealthColor, alpha);
            SetArc(afterImage, afterHp / 100f * 0.485f, AfterImageColor, alpha);
            SetArc(afterImage2, afterHp / 100f * 0.485f, AfterImageColor, alpha);
            // HealthBar.antiHpSlider: moves toward the real value like the health bar; visible while there is grey health
            V1GreyHealth grey = body.GetComponent<V1GreyHealth>();
            float antiTarget = grey ? grey.antiHp * 100f : 0f;
            antiHpShown = Mathf.MoveTowards(antiHpShown, antiTarget, dt * (Mathf.Abs(antiTarget - antiHpShown) * 5f + 5f));
            SetArc(antiHpArc, antiHpShown / 100f * 0.485f, new Color(0.75f, 0.75f, 0.75f, 1f), antiHpShown > 0f ? Mathf.Max(alpha, 1f) : 0f);

            // StaminaMeter: stamina 0..300 moves toward the real value (difference x25 + 25 per second)
            GenericSkill dash = body.skillLocator ? body.skillLocator.utility : null;
            float real = 0f;
            if (dash)
            {
                float frac = dash.stock >= dash.maxStock ? 0f : Mathf.Clamp01(dash.rechargeStopwatch / Mathf.Max(0.01f, dash.GetFinalRechargeInterval()));
                real = Mathf.Min(300f, (dash.stock + frac) * 100f);
            }
            staminaShown = Mathf.MoveTowards(staminaShown, real, dt * (Mathf.Abs(real - staminaShown) * 25f + 25f));
            for (int i = 0; i < 3; i++)
            {
                float v = Mathf.Clamp01((staminaShown - i * 100f) / 100f);
                Color c = v >= 1f ? StaminaColor : StaminaChargingColor;
                SetArc(stamina[i], v * 0.15f, c, c.a);
            }
        }

        private static void SetArc(Image img, float fill, Color color, float alpha)
        {
            if (!img)
            {
                return;
            }
            img.fillAmount = fill;
            color.a = alpha;
            img.color = color;
            img.enabled = alpha > 0f && fill > 0f;
        }
    }
}
