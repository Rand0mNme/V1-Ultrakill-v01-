using System.Linq;
using System.IO;
using UnityEngine;

namespace V1Mod.Modules
{
    // The v1assets bundle is built in the V1Unity Unity project (Assets/Editor/BuildV1.cs) from an ULTRAKILL export.
    // Its materials are already converted to built-in Unity shaders (Standard and Sprites/Default).
    public static class V1Assets
    {
        public static AssetBundle bundle;
        public static GameObject piercerViewmodel;
        public static GameObject revolverBeam;
        public static GameObject revolverBeamSuper;
        public static GameObject laserHit;
        public static GameObject superLaserHit;
        public static GameObject slideParticle;
        public static GameObject slideScrape;
        public static GameObject wallScrape;
        public static GameObject fallParticle;
        public static GameObject dodgeParticle;
        public static GameObject dustBig;
        public static GameObject shotgunCoreViewmodel;
        public static GameObject shotgunPumpViewmodel;
        public static GameObject shotgunSawViewmodel;
        public static GameObject chainsaw;
        public static GameObject sawSparks;
        public static GameObject nailgunMagnetViewmodel;
        public static GameObject nailgunOverheatViewmodel;
        public static GameObject nailgunZapperViewmodel;
        public static GameObject nail;
        public static GameObject nailHeated;
        public static GameObject magnet;
        public static GameObject zapper;
        public static GameObject nailMuzzleFlash;
        // sawblade launchers
        public static GameObject sawLauncherMagnetViewmodel;
        public static GameObject sawLauncherOverheatViewmodel;
        public static GameObject sawLauncherZapperViewmodel;
        public static GameObject sawNail;
        public static GameObject sawNailFodder;
        public static GameObject sawNailHeated;
        public static GameObject sawMuzzleFlash;
        public static GameObject superSawMuzzleFlash;
        public static GameObject railElectricViewmodel;
        public static GameObject railHarpoonViewmodel;
        public static GameObject railMaliciousViewmodel;
        public static GameObject railBeam;
        public static GameObject railBeamMalicious;
        public static GameObject screwdriver;
        public static GameObject maliciousExplosion;
        public static GameObject rocketFreezeViewmodel;
        public static GameObject rocketCannonballViewmodel;
        public static GameObject rocketNapalmViewmodel;
        public static GameObject rocket;
        public static GameObject cannonball;
        public static GameObject gasoline;
        public static GameObject rocketMuzzleFlash;
        public static GameObject marksmanViewmodel;
        public static GameObject sharpshooterViewmodel;
        public static GameObject coin;
        public static GameObject coinFlash;
        public static GameObject coinCharge;
        public static GameObject revolverBeamSharp;
        public static GameObject armFeedbacker;
        public static GameObject armKnuckleblaster;
        public static GameObject blastWave;
        public static GameObject armWhiplash;
        public static GameObject shotgunPellet;
        public static GameObject core;
        public static GameObject explosion;
        public static GameObject explosionChunk;
        public static GameObject slamShockwave;
        public static GameObject shotgunMuzzleFlash;

        public static Sprite crosshairDot;
        public static Sprite crosshairCircle;
        public static GameObject ukCrosshair;
        // SmileOS shop
        public static GameObject shop;
        public static GameObject firstRoom;
        public static Font shopFont;
        public static readonly System.Collections.Generic.Dictionary<string, Sprite> styleRanks = new System.Collections.Generic.Dictionary<string, Sprite>();
        public static readonly System.Collections.Generic.Dictionary<string, Sprite> weaponIcons = new System.Collections.Generic.Dictionary<string, Sprite>();
        public static Texture2D batteryFull;
        public static Texture2D batteryMid;
        public static Texture2D batteryLow;
        public static Texture2D[] batteryCharges;

        public static void Load(string pluginDir)
        {
            string path = Path.Combine(pluginDir, "v1assets");
            if (!File.Exists(path))
            {
                Log.Warning("Asset bundle not found: " + path);
                return;
            }
            bundle = AssetBundle.LoadFromFile(path);
            if (!bundle)
            {
                Log.Error("Failed to load asset bundle " + path);
                return;
            }
            piercerViewmodel = bundle.LoadAsset<GameObject>("V1Piercer");
            revolverBeam = bundle.LoadAsset<GameObject>("V1RevolverBeam");
            revolverBeamSuper = bundle.LoadAsset<GameObject>("V1RevolverBeamSuper");
            laserHit = bundle.LoadAsset<GameObject>("V1LaserHit");
            superLaserHit = bundle.LoadAsset<GameObject>("V1SuperLaserHit");
            slideParticle = bundle.LoadAsset<GameObject>("V1SlideParticle");
            slideScrape = bundle.LoadAsset<GameObject>("V1SlideScrape");
            wallScrape = bundle.LoadAsset<GameObject>("V1WallScrape");
            fallParticle = bundle.LoadAsset<GameObject>("V1FallParticle");
            dodgeParticle = bundle.LoadAsset<GameObject>("V1DodgeParticle");
            dustBig = bundle.LoadAsset<GameObject>("V1DustBig");
            shotgunCoreViewmodel = bundle.LoadAsset<GameObject>("V1ShotgunCore");
            shotgunPumpViewmodel = bundle.LoadAsset<GameObject>("V1ShotgunPump");
            shotgunSawViewmodel = bundle.LoadAsset<GameObject>("V1ShotgunSaw");
            chainsaw = bundle.LoadAsset<GameObject>("V1Chainsaw");
            sawSparks = bundle.LoadAsset<GameObject>("V1SawSparks");
            nailgunMagnetViewmodel = bundle.LoadAsset<GameObject>("V1NailgunMagnet");
            nailgunOverheatViewmodel = bundle.LoadAsset<GameObject>("V1NailgunOverheat");
            nailgunZapperViewmodel = bundle.LoadAsset<GameObject>("V1NailgunZapper");
            nail = bundle.LoadAsset<GameObject>("V1Nail");
            nailHeated = bundle.LoadAsset<GameObject>("V1NailHeated");
            magnet = bundle.LoadAsset<GameObject>("V1Magnet");
            zapper = bundle.LoadAsset<GameObject>("V1Zapper");
            nailMuzzleFlash = bundle.LoadAsset<GameObject>("V1NailMuzzleFlash");
            sawLauncherMagnetViewmodel = bundle.LoadAsset<GameObject>("V1SawLauncherMagnet");
            sawLauncherOverheatViewmodel = bundle.LoadAsset<GameObject>("V1SawLauncherOverheat");
            sawLauncherZapperViewmodel = bundle.LoadAsset<GameObject>("V1SawLauncherZapper");
            sawNail = bundle.LoadAsset<GameObject>("V1SawNail");
            sawNailFodder = bundle.LoadAsset<GameObject>("V1SawNailFodder");
            sawNailHeated = bundle.LoadAsset<GameObject>("V1SawNailHeated");
            sawMuzzleFlash = bundle.LoadAsset<GameObject>("V1SawMuzzleFlash");
            superSawMuzzleFlash = bundle.LoadAsset<GameObject>("V1SuperSawMuzzleFlash");
            railElectricViewmodel = bundle.LoadAsset<GameObject>("V1RailElectric");
            railHarpoonViewmodel = bundle.LoadAsset<GameObject>("V1RailHarpoon");
            railMaliciousViewmodel = bundle.LoadAsset<GameObject>("V1RailMalicious");
            railBeam = bundle.LoadAsset<GameObject>("V1RailBeam");
            railBeamMalicious = bundle.LoadAsset<GameObject>("V1RailBeamMalicious");
            screwdriver = bundle.LoadAsset<GameObject>("V1Screwdriver");
            maliciousExplosion = bundle.LoadAsset<GameObject>("V1MaliciousExplosion");
            rocketFreezeViewmodel = bundle.LoadAsset<GameObject>("V1RocketFreeze");
            rocketCannonballViewmodel = bundle.LoadAsset<GameObject>("V1RocketCannonball");
            rocketNapalmViewmodel = bundle.LoadAsset<GameObject>("V1RocketNapalm");
            rocket = bundle.LoadAsset<GameObject>("V1Rocket");
            cannonball = bundle.LoadAsset<GameObject>("V1Cannonball");
            gasoline = bundle.LoadAsset<GameObject>("V1Gasoline");
            rocketMuzzleFlash = bundle.LoadAsset<GameObject>("V1RocketMuzzleFlash");
            marksmanViewmodel = bundle.LoadAsset<GameObject>("V1Marksman");
            sharpshooterViewmodel = bundle.LoadAsset<GameObject>("V1Sharpshooter");
            coin = bundle.LoadAsset<GameObject>("V1Coin");
            coinFlash = bundle.LoadAsset<GameObject>("V1CoinFlash");
            coinCharge = bundle.LoadAsset<GameObject>("V1CoinCharge");
            revolverBeamSharp = bundle.LoadAsset<GameObject>("V1RevolverBeamSharp");
            armFeedbacker = bundle.LoadAsset<GameObject>("V1ArmFeedbacker");
            armKnuckleblaster = bundle.LoadAsset<GameObject>("V1ArmKnuckleblaster");
            blastWave = bundle.LoadAsset<GameObject>("V1BlastWave");
            armWhiplash = bundle.LoadAsset<GameObject>("V1ArmWhiplash");
            Log.Info("Arms: Feedbacker " + (armFeedbacker != null) + ", Knuckleblaster " + (armKnuckleblaster != null) + ", wave " + (blastWave != null) + ", Whiplash " + (armWhiplash != null));
            Log.Info("Revolvers: Marksman " + (marksmanViewmodel != null) + ", Sharpshooter " + (sharpshooterViewmodel != null) + ", coin " + (coin != null) + ", beam " + (revolverBeamSharp != null));
            Log.Info("Rocket launchers: " + (rocketFreezeViewmodel != null) + (rocketCannonballViewmodel != null) + (rocketNapalmViewmodel != null) + ", rocket " + (rocket != null) + ", cannonball " + (cannonball != null) + ", gasoline " + (gasoline != null));
            Log.Info("Railcannons: " + (railElectricViewmodel != null) + (railHarpoonViewmodel != null) + (railMaliciousViewmodel != null) + ", beams " + (railBeam != null) + (railBeamMalicious != null) + ", screwdriver " + (screwdriver != null) + ", explosion " + (maliciousExplosion != null));
            Log.Info("Nailguns: " + (nailgunMagnetViewmodel != null) + (nailgunOverheatViewmodel != null) + (nailgunZapperViewmodel != null) + ", nail " + (nail != null) + ", magnet " + (magnet != null) + ", zapper " + (zapper != null) + "; saw " + (shotgunSawViewmodel != null) + (chainsaw != null));
            shotgunPellet = bundle.LoadAsset<GameObject>("V1ShotgunPellet");
            core = bundle.LoadAsset<GameObject>("V1Core");
            explosion = bundle.LoadAsset<GameObject>("V1Explosion");
            explosionChunk = bundle.LoadAsset<GameObject>("V1ExplosionChunk");
            slamShockwave = bundle.LoadAsset<GameObject>("V1SlamShockwave");
            shotgunMuzzleFlash = bundle.LoadAsset<GameObject>("V1ShotgunMuzzleFlash");

            shop = bundle.LoadAsset<GameObject>("V1Shop");
            firstRoom = bundle.LoadAsset<GameObject>("V1FirstRoom");
            shopFont = bundle.LoadAsset<Font>("fs-tahoma-8px");
            foreach (string r in new[] { "RankD", "RankC", "RankB", "RankA", "RankS", "RankSS", "RankSSS", "RankU" })
            {
                styleRanks[r] = bundle.LoadAsset<Sprite>(r);
            }
            foreach (string icon in new[] { "SingleRevolver", "RevolverSpecial", "RevolverSharp", "Shotgun", "Shotgun1", "ShotgunSaw", "Nailgun", "NailgunOverheat", "NailgunZapper", "Railcannon", "railcannonscrew", "railcannonmalicious", "rocketlauncher", "rocketlaunchercannon", "rocketlaunchergasoline" })
            {
                weaponIcons[icon] = bundle.LoadAsset<Sprite>("V1Icon_" + icon);
            }
            crosshairDot = bundle.LoadAsset<Sprite>("meter");
            crosshairCircle = bundle.LoadAsset<Sprite>("circlethick");
            ukCrosshair = bundle.LoadAsset<GameObject>("UKCrosshair");
            batteryFull = bundle.LoadAsset<Texture2D>("batteryfull");
            batteryMid = bundle.LoadAsset<Texture2D>("batterymid");
            batteryLow = bundle.LoadAsset<Texture2D>("batterylow");
            batteryCharges = new[]
            {
                bundle.LoadAsset<Texture2D>("batterycharging1"),
                bundle.LoadAsset<Texture2D>("batterycharging2"),
                bundle.LoadAsset<Texture2D>("batterycharging3"),
            };

            Log.Info("Asset bundle loaded: arm " + Has(piercerViewmodel) + ", beams " + Has(revolverBeam) + "/" + Has(revolverBeamSuper)
                + ", sparks " + Has(laserHit) + "/" + Has(superLaserHit) + ", battery " + Has(batteryFull) + ", crosshair " + Has(ukCrosshair) + ", shotgun " + Has(shotgunCoreViewmodel) + Has(shotgunPellet) + Has(core) + Has(explosion) + Has(shotgunMuzzleFlash) + ", movement particles " + Has(slideParticle) + Has(slideScrape) + Has(wallScrape) + Has(fallParticle) + Has(dodgeParticle) + Has(dustBig) + ", shop " + Has(shop) + ", room " + Has(firstRoom) + (shopFont ? "yes" : "no") + " icons " + weaponIcons.Values.Count(s => s));
        }

        private static string Has(Object o) => o ? "yes" : "NO";

        // World objects (terminal, room): the weapon shader V1/Viewmodel is made to draw on top of the frame
        // and looks translucent in the world, so swap it for the standard RoR2 shader (HGStandard): lighting, shadows, fog.
        private static Shader hgStandard;
        private static readonly System.Collections.Generic.Dictionary<Material, Material> worldMaterials = new System.Collections.Generic.Dictionary<Material, Material>();

        public static void ToWorldMaterials(GameObject go)
        {
            if (!hgStandard)
            {
                hgStandard = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<Shader>("RoR2/Base/Shaders/HGStandard.shader").WaitForCompletion();
            }
            if (!hgStandard)
            {
                return;
            }
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (!m || m.shader.name != "V1/Viewmodel")
                    {
                        continue;
                    }
                    if (!worldMaterials.TryGetValue(m, out Material w))
                    {
                        w = new Material(hgStandard);
                        w.SetTexture("_MainTex", m.GetTexture("_MainTex"));
                        w.SetColor("_Color", m.GetColor("_Color"));
                        Texture em = m.GetTexture("_EmissionMap");
                        if (em)
                        {
                            w.SetTexture("_EmTex", em);
                            w.SetColor("_EmColor", Color.white);
                            w.SetFloat("_EmPower", 1f);
                        }
                        w.SetFloat("_Smoothness", 0.1f);
                        worldMaterials[m] = w;
                    }
                    mats[i] = w;
                    changed = true;
                }
                if (changed)
                {
                    r.sharedMaterials = mats;
                }
            }
        }
    }
}
