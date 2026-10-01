using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Esya sistemi varliklarini kurar:
//   Assets/Resources/ItemDatabase.asset       (tum esyalar)
//   Assets/Data/Items/Item_*.asset            (esya tanimlari)
//   Assets/Data/Items/Icons/*.png             (envanter ikonlari: modelden ya da kodla cizilmis)
//   Assets/Data/Items/ItemFallback.mat        (modelsiz esyalar icin URP Lit)
//   Assets/Resources/Network/WorldItem.prefab (esya ag prefab'i)
//   Assets/Resources/Network/GameState.prefab (ortak kasa / dukkan seviyesi)
// Eksik bir sey varsa acilista kendiliginden calisir. Var olan esya tanimlarinin ELLE degistirilen
// alanlarina dokunmaz; sadece eksikleri ekler, bos modelleri/ikonlari doldurur.
// Menu: PlayersMarket > Esya Verilerini Guncelle
[InitializeOnLoad]
public static class ItemSetupBuilder
{
    const string DataDir = "Assets/Data/Items";
    const string IconDir = "Assets/Data/Items/Icons";
    const string DatabasePath = "Assets/Resources/ItemDatabase.asset";
    const string FallbackMatPath = "Assets/Data/Items/ItemFallback.mat";
    const string WorldItemPath = "Assets/Resources/Network/WorldItem.prefab";
    const string GameStatePath = "Assets/Resources/Network/GameState.prefab";
    const string CustomerPath = "Assets/Resources/Network/Customer.prefab";
    const string DummyPath = "Assets/Resources/Network/TrainingDummy.prefab";
    const int CustomerVersion = 2; // prefab duzeni degisince artir (eski prefab yeniden uretilir)
    // EmaceArt "Slavic Medieval Village Free" paketi (aktarildiysa)
    const string Pack = "Assets/EmaceArt/Slavic World Free/Prefabs/";

    struct Def
    {
        public string id, name, model;
        public int value, power, stack;
        public ItemSize size;
        public ItemCategory category;
        public EquipSlot slot;
        public WeaponType weapon;
        public float mass;
        public bool fragile;
        public FallbackShape shape;
        public Color color;
        public Vector3 dims;
    }

    // Ticari mal (raf esyasi)
    static Def Good(string id, string name, int value, ItemSize size, float mass, bool fragile,
        FallbackShape shape, Color color, Vector3 dims, string model, int stack = 1) =>
        new Def { id = id, name = name, value = value, size = size, mass = mass, fragile = fragile, shape = shape,
                  color = color, dims = dims, model = model, category = ItemCategory.TradeGood, stack = stack };

    // Ekipman
    static Def Gear(string id, string name, EquipSlot slot, int power, int value, Color color,
        WeaponType weapon = WeaponType.None)
    {
        ItemCategory cat = slot == EquipSlot.Weapon ? ItemCategory.Weapon
            : slot == EquipSlot.Necklace || slot == EquipSlot.Earring || slot == EquipSlot.Ring ? ItemCategory.Accessory
            : ItemCategory.Armor;
        Vector3 dims = slot == EquipSlot.Weapon ? new Vector3(0.12f, 0.9f, 0.12f)
            : cat == ItemCategory.Accessory ? new Vector3(0.1f, 0.1f, 0.1f) : new Vector3(0.4f, 0.35f, 0.3f);
        FallbackShape shape = slot == EquipSlot.Weapon ? FallbackShape.Cube
            : cat == ItemCategory.Accessory ? FallbackShape.Sphere : FallbackShape.Cube;
        return new Def { id = id, name = name, slot = slot, weapon = weapon, power = power, value = value, color = color,
                         category = cat, size = ItemSize.Small, mass = cat == ItemCategory.Accessory ? 0.1f : 2f,
                         shape = shape, dims = dims, stack = 1 };
    }

    // Malzeme (yiginlanir)
    static Def Mat(string id, string name, int value, Color color, int stack = 99) =>
        new Def { id = id, name = name, value = value, color = color, category = ItemCategory.Material,
                  size = ItemSize.Small, mass = 0.2f, shape = FallbackShape.Sphere, dims = new Vector3(0.15f, 0.15f, 0.15f),
                  stack = stack };

    static readonly Color Leather = new Color(0.55f, 0.36f, 0.2f);
    static readonly Color Iron = new Color(0.62f, 0.65f, 0.7f);
    static readonly Color Copper = new Color(0.85f, 0.5f, 0.25f);
    static readonly Color Silver = new Color(0.85f, 0.88f, 0.92f);
    static readonly Color Wood = new Color(0.6f, 0.42f, 0.22f);

    // id'leri DEGISTIRME (agda/kayitta kullaniliyor); yenisini SONA ekle.
    static readonly Def[] Defaults =
    {
        // --- Ticari mallar (ilk test esyalari) ---
        Good("apple", "Elma", 4, ItemSize.Small, 0.2f, false, FallbackShape.Sphere, new Color(0.85f, 0.15f, 0.12f), new Vector3(0.12f, 0.12f, 0.12f), "Prop/EA03_Items_House_Apple_01a__PRE.prefab"),
        Good("carrot", "Havuç", 3, ItemSize.Small, 0.15f, false, FallbackShape.Capsule, new Color(1f, 0.5f, 0.1f), new Vector3(0.07f, 0.22f, 0.07f), "Prop/EA03_Items_House_Carrot_01a__PRE.prefab"),
        Good("corn", "Mısır", 5, ItemSize.Small, 0.3f, false, FallbackShape.Capsule, new Color(1f, 0.85f, 0.2f), new Vector3(0.09f, 0.28f, 0.09f), "Prop/EA03_Items_House_Corn_01a_PRE.prefab"),
        Good("bottle", "Şişe", 12, ItemSize.Small, 0.5f, true, FallbackShape.Cylinder, new Color(0.25f, 0.6f, 0.35f), new Vector3(0.1f, 0.3f, 0.1f), "Items/Crockery/EA03_Items_House_Bottle_01a_PRE.prefab"),
        Good("cup", "Bardak", 8, ItemSize.Small, 0.2f, true, FallbackShape.Cylinder, new Color(0.92f, 0.92f, 0.88f), new Vector3(0.1f, 0.12f, 0.1f), "Items/Crockery/EA03_Items_House_Cup_01a_PRE.prefab"),
        Good("mug", "Kupa", 10, ItemSize.Small, 0.3f, false, FallbackShape.Cylinder, new Color(0.55f, 0.35f, 0.2f), new Vector3(0.12f, 0.14f, 0.12f), "Items/Crockery/EA_Items_House_mug_01a_PRE.prefab"),
        Good("bowl", "Tahta Kase", 9, ItemSize.Small, 0.3f, false, FallbackShape.Cylinder, new Color(0.7f, 0.5f, 0.3f), new Vector3(0.28f, 0.1f, 0.28f), "Items/Crockery/EA_Items_House_bowl_wod_PRE.prefab"),
        Good("kettle", "Çaydanlık", 25, ItemSize.Small, 1f, false, FallbackShape.Sphere, new Color(0.6f, 0.62f, 0.65f), new Vector3(0.3f, 0.28f, 0.3f), "Items/Crockery/EA_Items_House_kettle_01a_PRE.prefab"),
        Good("pickaxe", "Kazma", 30, ItemSize.Small, 2f, false, FallbackShape.Cube, new Color(0.45f, 0.45f, 0.5f), new Vector3(0.55f, 0.12f, 0.12f), "Items/Tool/EA03_Prop_Tool_Pick_01a_PRE.prefab"),
        Good("artifact", "Antik Eser", 120, ItemSize.Small, 1.5f, true, FallbackShape.Cube, new Color(1f, 0.8f, 0.25f), new Vector3(0.22f, 0.3f, 0.22f), "Prop/EA_Artefact_PRE.prefab"),
        Good("cauldron", "Kazan", 60, ItemSize.Large, 8f, false, FallbackShape.Cylinder, new Color(0.2f, 0.2f, 0.22f), new Vector3(0.7f, 0.5f, 0.7f), "Items/Crockery/EA_Items_House_cauldron_01a_PRE.prefab"),
        Good("basket", "Sepet", 15, ItemSize.Large, 2f, false, FallbackShape.Cylinder, new Color(0.75f, 0.6f, 0.35f), new Vector3(0.6f, 0.4f, 0.6f), "Prop/Container/EA03_Prop_Container_Basket_01a_PRE.prefab"),
        Good("crate", "Sandık", 40, ItemSize.Large, 10f, false, FallbackShape.Cube, new Color(0.6f, 0.42f, 0.25f), new Vector3(0.8f, 0.6f, 0.6f), "Prop/Container/EA03_Prop_Container_Crate_01a_PRE.prefab"),

        // --- Silahlar ---
        Gear("sword_rusty", "Paslı Kılıç", EquipSlot.Weapon, 60, 20, new Color(0.6f, 0.45f, 0.35f), WeaponType.Sword),
        Gear("sword_iron", "Demir Kılıç", EquipSlot.Weapon, 100, 90, Iron, WeaponType.Sword),
        Gear("bow_short", "Kısa Yay", EquipSlot.Weapon, 60, 25, Wood, WeaponType.Bow),
        Gear("bow_long", "Uzun Yay", EquipSlot.Weapon, 100, 95, new Color(0.45f, 0.3f, 0.15f), WeaponType.Bow),

        // --- Zirhlar ---
        Gear("helm_leather", "Deri Başlık", EquipSlot.Head, 45, 15, Leather),
        Gear("helm_iron", "Demir Miğfer", EquipSlot.Head, 95, 80, Iron),
        Gear("armor_leather", "Deri Zırh", EquipSlot.Body, 50, 25, Leather),
        Gear("armor_iron", "Demir Zırh", EquipSlot.Body, 100, 120, Iron),
        Gear("gloves_leather", "Deri Eldiven", EquipSlot.Hands, 45, 12, Leather),
        Gear("gloves_iron", "Demir Eldiven", EquipSlot.Hands, 90, 70, Iron),
        Gear("boots_leather", "Deri Çizme", EquipSlot.Feet, 45, 14, Leather),
        Gear("boots_iron", "Demir Çizme", EquipSlot.Feet, 90, 75, Iron),

        // --- Takilar ---
        Gear("necklace_copper", "Bakır Kolye", EquipSlot.Necklace, 50, 30, Copper),
        Gear("necklace_silver", "Gümüş Kolye", EquipSlot.Necklace, 100, 140, Silver),
        Gear("earring_copper", "Bakır Küpe", EquipSlot.Earring, 50, 25, Copper),
        Gear("earring_silver", "Gümüş Küpe", EquipSlot.Earring, 100, 120, Silver),
        Gear("ring_copper", "Bakır Yüzük", EquipSlot.Ring, 50, 25, Copper),
        Gear("ring_silver", "Gümüş Yüzük", EquipSlot.Ring, 100, 120, Silver),

        // --- Malzemeler (zindan dususu) ---
        Mat(ItemRules.UpgradeStoneId, "Basma Taşı", 15, new Color(0.4f, 0.75f, 1f)),
        Mat("slime_gel", "Balçık Jölesi", 3, new Color(0.45f, 0.85f, 0.35f), 50),
        Mat("bone", "Kemik", 2, new Color(0.92f, 0.9f, 0.82f), 50),
        Mat("wolf_fang", "Kurt Dişi", 6, new Color(0.95f, 0.95f, 0.9f), 50),

        // --- Hancer / Asa (Hancerci, Buyucu siniflari) ---
        Gear("dagger_rusty", "Paslı Hançer", EquipSlot.Weapon, 60, 18, new Color(0.6f, 0.45f, 0.35f), WeaponType.Dagger),
        Gear("dagger_steel", "Çelik Hançer", EquipSlot.Weapon, 100, 85, Iron, WeaponType.Dagger),
        Gear("staff_apprentice", "Çırak Asası", EquipSlot.Weapon, 60, 22, new Color(0.55f, 0.4f, 0.75f), WeaponType.Staff),
        Gear("staff_oak", "Meşe Asa", EquipSlot.Weapon, 100, 95, new Color(0.45f, 0.3f, 0.2f), WeaponType.Staff),
    };

    static ItemSetupBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (NeedsBuild()) Build();
        };
    }

    static bool NeedsBuild()
    {
        if (!File.Exists(DatabasePath) || !File.Exists(WorldItemPath) || !File.Exists(GameStatePath) || !File.Exists(CustomerPath) || !File.Exists(DummyPath)) return true;
        if (CustomerOutdated()) return true;
        foreach (Def d in Defaults)
        {
            ItemData data = AssetDatabase.LoadAssetAtPath<ItemData>($"{DataDir}/Item_{d.id}.asset");
            if (data == null || data.icon == null) return true;
            if (data.visualPrefab == null && !string.IsNullOrEmpty(d.model) && File.Exists(Pack + d.model)) return true;
        }
        return false;
    }

    [MenuItem("PlayersMarket/Esya Verilerini Guncelle")]
    static void Build()
    {
        EnsureFolder(DataDir);
        EnsureFolder(IconDir);
        EnsureFolder("Assets/Resources/Network");

        Material fallback = AssetDatabase.LoadAssetAtPath<Material>(FallbackMatPath);
        if (fallback == null)
        {
            fallback = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            fallback.SetFloat("_Smoothness", 0.3f);
            AssetDatabase.CreateAsset(fallback, FallbackMatPath);
        }

        ItemDatabase db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DatabasePath);
        if (db == null)
        {
            db = ScriptableObject.CreateInstance<ItemDatabase>();
            AssetDatabase.CreateAsset(db, DatabasePath);
        }
        db.fallbackMaterial = fallback;

        int created = 0, linked = 0;
        var needPreview = new List<ItemData>();
        foreach (Def d in Defaults)
        {
            string path = $"{DataDir}/Item_{d.id}.asset";
            ItemData data = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<ItemData>();
                data.id = d.id;
                data.displayName = d.name;
                data.baseValue = d.value;
                data.size = d.size;
                data.mass = d.mass;
                data.fragile = d.fragile;
                data.category = d.category;
                data.equipSlot = d.slot;
                data.weaponType = d.weapon;
                data.basePower = d.power;
                data.maxStack = Mathf.Max(1, d.stack);
                data.fallbackShape = d.shape;
                data.fallbackColor = d.color;
                data.fallbackSize = d.dims;
                AssetDatabase.CreateAsset(data, path);
                created++;
            }

            // Model bossa ve paket varsa bagla (elle secilmis modeli ezme)
            if (data.visualPrefab == null && !string.IsNullOrEmpty(d.model))
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + d.model);
                if (model != null)
                {
                    data.visualPrefab = model;
                    EditorUtility.SetDirty(data);
                    linked++;
                }
            }

            if (data.icon == null)
            {
                if (data.visualPrefab != null) needPreview.Add(data);
                else data.icon = SaveIcon(data.id, DrawIcon(data));
                EditorUtility.SetDirty(data);
            }

            if (!db.items.Contains(data)) db.items.Add(data);
        }
        db.items.RemoveAll(x => x == null);
        EditorUtility.SetDirty(db);

        BuildWorldItemPrefab();
        BuildGameStatePrefab();
        BuildCustomerPrefab(fallback);
        BuildDummyPrefab();
        AssetDatabase.SaveAssets();
        Debug.Log($"[ItemSetup] Hazir: {db.items.Count} esya ({created} yeni, {linked} modele baglandi).");

        if (needPreview.Count > 0) StartModelIcons(needPreview);
    }

    // ---------------- Ikonlar ----------------

    static List<ItemData> previewQueue;
    static double previewStart;

    // Modelli esyalar: AssetPreview (asenkron) → PNG
    static void StartModelIcons(List<ItemData> items)
    {
        previewQueue = items;
        AssetPreview.SetPreviewTextureCacheSize(256);
        foreach (ItemData d in items) AssetPreview.GetAssetPreview(d.visualPrefab);
        previewStart = EditorApplication.timeSinceStartup;
        EditorApplication.update -= PollPreviews;
        EditorApplication.update += PollPreviews;
    }

    static void PollPreviews()
    {
        bool timeout = EditorApplication.timeSinceStartup - previewStart > 60;
        foreach (ItemData d in previewQueue)
            if (d != null && d.visualPrefab != null && AssetPreview.GetAssetPreview(d.visualPrefab) == null && !timeout)
                return;

        EditorApplication.update -= PollPreviews;
        int n = 0;
        foreach (ItemData d in previewQueue)
        {
            if (d == null) continue;
            Texture2D preview = d.visualPrefab != null ? AssetPreview.GetAssetPreview(d.visualPrefab) : null;
            d.icon = SaveIcon(d.id, preview != null ? Readable(preview) : DrawIcon(d));
            EditorUtility.SetDirty(d);
            n++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[ItemSetup] {n} model ikonu uretildi.");
    }

    // AssetPreview dokusu okunamayabilir: RenderTexture uzerinden kopyala
    static Texture2D Readable(Texture2D src)
    {
        RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(src, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        copy.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return copy;
    }

    static Texture2D SaveIcon(string id, Texture2D tex)
    {
        string path = $"{IconDir}/{id}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Default;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Modelsiz esya ikonu: kategoriye gore basit siluet (yer tutucu; ileride gercek ikonlar)
    static Texture2D DrawIcon(ItemData d)
    {
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * N];
        Color c = d.fallbackColor;
        Color dark = c * 0.55f;
        dark.a = 1f;

        bool Inside(float x, float y)
        {
            float cx = x - 0.5f, cy = y - 0.5f;
            switch (d.equipSlot)
            {
                case EquipSlot.Weapon:
                    if (d.weaponType == WeaponType.Bow)
                    {
                        float r = Mathf.Sqrt((cx + 0.25f) * (cx + 0.25f) + cy * cy);
                        bool arc = r > 0.36f && r < 0.43f && cx > -0.2f;
                        bool str = Mathf.Abs(cx + 0.2f) < 0.012f && Mathf.Abs(cy) < 0.39f;
                        return arc || str;
                    }
                    if (d.weaponType == WeaponType.Staff)
                    {
                        // capraz degnek + ucunda kure
                        float su = (cx + cy) * 0.7071f, sv = (cx - cy) * 0.7071f;
                        bool stick = Mathf.Abs(sv) < 0.03f && su > -0.42f && su < 0.26f;
                        float ox = cx - 0.24f, oy = cy - 0.24f;
                        return stick || ox * ox + oy * oy < 0.014f;
                    }
                    if (d.weaponType == WeaponType.Dagger)
                    {
                        // kisa bicak
                        float du = (cx + cy) * 0.7071f, dv = (cx - cy) * 0.7071f;
                        bool dBlade = Mathf.Abs(dv) < 0.04f && du > -0.05f && du < 0.3f;
                        bool dGuard = Mathf.Abs(du + 0.07f) < 0.025f && Mathf.Abs(dv) < 0.1f;
                        bool dGrip = Mathf.Abs(dv) < 0.028f && du < -0.07f && du > -0.24f;
                        return dBlade || dGuard || dGrip;
                    }
                    {
                        // capraz kilic: bicak + balcak + kabza
                        float u = (cx + cy) * 0.7071f, v = (cx - cy) * 0.7071f;
                        bool blade = Mathf.Abs(v) < 0.045f && u > -0.18f && u < 0.42f;
                        bool guard = Mathf.Abs(u + 0.2f) < 0.03f && Mathf.Abs(v) < 0.14f;
                        bool grip = Mathf.Abs(v) < 0.03f && u < -0.2f && u > -0.38f;
                        return blade || guard || grip;
                    }
                case EquipSlot.Head: return (cy > -0.1f && cx * cx + (cy + 0.1f) * (cy + 0.1f) < 0.12f) || (Mathf.Abs(cy + 0.12f) < 0.04f && Mathf.Abs(cx) < 0.42f);
                case EquipSlot.Body: return Mathf.Abs(cy) < 0.36f && Mathf.Abs(cx) < 0.28f + (cy > 0.2f ? 0.12f : 0f) && !(cy > 0.25f && Mathf.Abs(cx) < 0.1f);
                case EquipSlot.Hands: return (Mathf.Abs(cx) < 0.22f && cy > -0.35f && cy < 0.1f) || (cy >= 0.1f && cy < 0.35f && Mathf.Abs(cx) < 0.22f && Mathf.Repeat((cx + 0.22f) / 0.11f, 1f) < 0.7f);
                case EquipSlot.Feet: return (Mathf.Abs(cx + 0.08f) < 0.14f && cy > -0.1f && cy < 0.38f) || (cx > -0.22f && cx < 0.36f && cy > -0.32f && cy <= -0.1f);
                case EquipSlot.Necklace:
                    {
                        float r = Mathf.Sqrt(cx * cx + (cy - 0.1f) * (cy - 0.1f));
                        return (r > 0.26f && r < 0.31f && cy < 0.3f) || ((cx * cx + (cy + 0.25f) * (cy + 0.25f)) < 0.012f);
                    }
                case EquipSlot.Earring:
                    {
                        float r = Mathf.Sqrt(cx * cx + (cy - 0.15f) * (cy - 0.15f));
                        return (r > 0.12f && r < 0.16f && cy > 0.05f) || (cx * cx + (cy + 0.15f) * (cy + 0.15f)) < 0.02f;
                    }
                case EquipSlot.Ring:
                    {
                        float r = Mathf.Sqrt(cx * cx + cy * cy);
                        return (r > 0.22f && r < 0.3f) || (cx * cx + (cy - 0.3f) * (cy - 0.3f)) < 0.006f;
                    }
            }
            // Malzeme: tas (elmas) · diger: daire
            if (d.category == ItemCategory.Material) return Mathf.Abs(cx) + Mathf.Abs(cy) * 0.8f < 0.32f;
            return cx * cx + cy * cy < 0.1f;
        }

        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float fx = (x + 0.5f) / N, fy = (y + 0.5f) / N;
                Color col = Color.clear;
                if (Inside(fx, fy))
                {
                    // Kenar golgesi: komsu bos ise koyu
                    bool edge = !Inside(fx + 2f / N, fy) || !Inside(fx - 2f / N, fy) || !Inside(fx, fy + 2f / N) || !Inside(fx, fy - 2f / N);
                    col = edge ? dark : Color.Lerp(c, Color.white, Mathf.Clamp01((fy - 0.5f) * 0.6f));
                    col.a = 1f;
                }
                px[y * N + x] = col;
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ---------------- Ag prefab'lari ----------------

    static void BuildWorldItemPrefab()
    {
        if (File.Exists(WorldItemPath)) return; // elle ayar yapilmis olabilir; ezme

        var root = new GameObject("WorldItem");
        root.AddComponent<NetworkIdentity>();

        var rb = root.AddComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        root.AddComponent<BoxCollider>().size = Vector3.one * 0.3f;

        var nt = root.AddComponent<NetworkTransformReliable>();
        nt.target = root.transform;
        nt.syncDirection = SyncDirection.ServerToClient;
        nt.coordinateSpace = CoordinateSpace.World;
        nt.syncScale = false;

        root.AddComponent<WorldItem>();
        SavePrefab(root, WorldItemPath);
    }

    static void BuildGameStatePrefab()
    {
        if (File.Exists(GameStatePath)) return;
        var root = new GameObject("GameState");
        root.AddComponent<NetworkIdentity>();
        root.AddComponent<GameState>();
        SavePrefab(root, GameStatePath);
    }

    // Musteri: kapsul govde (renk tipe gore, CustomerAI), NavMeshAgent (sadece sunucuda acilir), el noktasi
    // Egitim kuklasi: direk + hasir govde + kollar + kirmizi hedef. Ilk cocuk "Visual" (vurulunca sallanir).
    static void BuildDummyPrefab()
    {
        if (File.Exists(DummyPath)) return;
        Material straw = SimpleMat("TrainingDummy Straw", new Color(0.85f, 0.72f, 0.4f));
        Material wood = SimpleMat("TrainingDummy Wood", new Color(0.4f, 0.28f, 0.17f));
        Material red = SimpleMat("TrainingDummy Target", new Color(0.85f, 0.2f, 0.15f));

        var root = new GameObject("TrainingDummy");
        root.AddComponent<NetworkIdentity>();
        var col = root.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 1f, 0f);
        col.height = 2f;
        col.radius = 0.35f;

        var visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        Part(visual, "Post", "Cylinder.fbx", new Vector3(0f, 0.5f, 0f), new Vector3(0.14f, 0.5f, 0.14f), wood);
        Part(visual, "Base", "Cylinder.fbx", new Vector3(0f, 0.04f, 0f), new Vector3(0.7f, 0.04f, 0.7f), wood);
        Part(visual, "Torso", "Capsule.fbx", new Vector3(0f, 1.35f, 0f), new Vector3(0.6f, 0.5f, 0.45f), straw);
        Part(visual, "Head", "Sphere.fbx", new Vector3(0f, 2.0f, 0f), new Vector3(0.38f, 0.38f, 0.38f), straw);
        Part(visual, "Arms", "Cube.fbx", new Vector3(0f, 1.55f, 0f), new Vector3(1.4f, 0.12f, 0.12f), wood);
        Part(visual, "Target", "Cylinder.fbx", new Vector3(0f, 1.4f, 0.23f), new Vector3(0.32f, 0.02f, 0.32f), red)
            .localRotation = Quaternion.Euler(90f, 0f, 0f);

        root.AddComponent<TrainingDummy>();
        SavePrefab(root, DummyPath);
    }

    static Transform Part(Transform parent, string name, string mesh, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>(mesh);
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go.transform;
    }

    static Material SimpleMat(string name, Color c)
    {
        string path = $"{DataDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.15f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static bool CustomerOutdated()
    {
        GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(CustomerPath);
        CustomerAI ai = p != null ? p.GetComponent<CustomerAI>() : null;
        return ai == null || ai.prefabVersion < CustomerVersion;
    }

    // Boy ~1.6 m (oyuncu 2 m). Esya onde, iki elle tutar gibi (govdenin disinda gorunsun).
    static void BuildCustomerPrefab(Material mat)
    {
        if (!CustomerOutdated()) return;
        var root = new GameObject("Customer");
        root.AddComponent<NetworkIdentity>();

        var nt = root.AddComponent<NetworkTransformReliable>();
        nt.target = root.transform;
        nt.syncDirection = SyncDirection.ServerToClient;
        nt.coordinateSpace = CoordinateSpace.World;
        nt.syncScale = false;

        root.AddComponent<Rigidbody>().isKinematic = true; // hareketli collider: esyalari dogru iter
        var col = root.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.8f, 0f);
        col.height = 1.6f;
        col.radius = 0.28f;

        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.3f;
        agent.height = 1.6f;
        agent.speed = 3f;
        agent.angularSpeed = 540f;
        agent.acceleration = 10f;
        agent.stoppingDistance = 0.25f;
        agent.enabled = false; // CustomerAI sunucuda acar

        var body = new GameObject("Body");
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        body.transform.localScale = new Vector3(0.55f, 0.8f, 0.55f);
        body.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Capsule.fbx");
        body.AddComponent<MeshRenderer>().sharedMaterial = mat;

        var face = new GameObject("Face"); // bakis yonu
        face.transform.SetParent(root.transform, false);
        face.transform.localPosition = new Vector3(0f, 1.3f, 0.24f);
        face.transform.localScale = new Vector3(0.25f, 0.1f, 0.1f);
        face.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        face.AddComponent<MeshRenderer>().sharedMaterial = mat;

        var hold = new GameObject("HoldPoint");
        hold.transform.SetParent(root.transform, false);
        hold.transform.localPosition = new Vector3(0f, 0.95f, 0.55f);

        root.AddComponent<CustomerAI>().prefabVersion = CustomerVersion;
        SavePrefab(root, CustomerPath); // ayni yola yazar: GUID korunur
    }

    static void SavePrefab(GameObject root, string path)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        // assetId prefab'a yazilsin (build icin)
        uint assetId = prefab.GetComponent<NetworkIdentity>().assetId;
        EditorUtility.SetDirty(prefab);
        Debug.Log($"[ItemSetup] {Path.GetFileName(path)} olusturuldu (assetId={assetId}).");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
