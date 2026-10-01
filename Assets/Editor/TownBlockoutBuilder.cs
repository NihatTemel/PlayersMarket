using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Kasaba GREYBOX'i (gri kutu taslak): Game sahnesine "Town (Greybox)" kokunu kurar.
// Amac olcu ve mesafeleri karakterle test etmek; sanat paketi secilince kutular modellerle degisecek.
// Menu: PlayersMarket > Kasaba Greybox Olustur. Tekrar calistirinca "Town (Greybox)" SILINIP yeniden
// kurulur → kokun ICINDE elle yapilan degisiklikler gider (elle eklenecekleri kokun disina koy).
//
// Olculer (metre), 3. sahis kamera icin bilerek genis: tavan 4.2, kapi 2.4x3, koridor >= 2.6.
// Koordinatlar: meydan merkezi (0,0,0), +Z kuzey. Dukkan meydanin kuzeyinde, kapisi guneye bakar.
//
//                  [arka kapi]
//   +-----------+------------------+ - - - - - +
//   |  DEPO     |  RAFLAR          | GENISLEME |
//   |  degerl.  |  ----   ----     | (Sv.2)    |
//   |           |  KASA    VITRIN  |           |
//   +-----------+---[kapi]---------+ - - - - - +      z = 22
//           [  MEYDAN 40x40  (cesme)  ]                z = -20..20
//  DEMIRCI / SIMYACI (bati)    LONCA (dogu) ----yol---- ZINDAN KAPISI (x=52)
//               TEFECI (guney-dogu)    | guney yolu | KASABA GIRISI (z=-56)
public static class TownBlockoutBuilder
{
    const string GameScenePath = "Assets/Scenes/Game.unity";
    const string RootName = "Town (Greybox)";
    const string Dir = "Assets/Greybox";
    const string MatDir = "Assets/Greybox/Materials";
    const string MeshAssetPath = "Assets/Greybox/GreyboxMeshes.asset";
    const string GridPath = "Assets/Greybox/GreyboxGrid.png";

    // Sanat paketi (EmaceArt Slavic Medieval Village). Varsa binalar/doga/sus objeleri modelle kurulur,
    // yoksa gri kutular. Dukkan kabugu ve oynanis objeleri (raf, kasa, yuvalar) her zaman greybox.
    // Olculer/kucuk resimler: Logs/PackInventory (AssetPackInventory).
    const string Pack = "Assets/EmaceArt/Slavic World Free/Prefabs/";
    static bool HasPack => AssetDatabase.IsValidFolder("Assets/EmaceArt/Slavic World Free/Prefabs");
    static readonly List<Rect> reserved = new List<Rect>(); // XZ (Rect.y = z): doga konmayacak alanlar
    static System.Random rng;

    const float WallT = 0.3f;       // duvar kalinligi
    const float WallH = 5f;         // dukkan duvar yuksekligi (= tavan)

    // ---- calisma durumu ----
    static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();
    static bool meshAssetCreated;
    static Texture2D grid;
    static Material mGrass, mPlaza, mRoad, mWall, mShopWall, mCeiling, mRoof, mWood, mShopFloor,
        mBreakable, mDark, mStone, mGate, mFoliage, mTrunk, mWater, mLamp, mGlass,
        zCounter, zDisplay, zStorage, zExpansion, zSpawn, zPortal;

    struct Opening
    {
        public float center, width, bottom, top;
        public static Opening Door(float center, float width = 2.4f, float height = 3f) =>
            new Opening { center = center, width = width, bottom = 0f, top = height };
        public static Opening Window(float center, float width = 3f, float bottom = 1f, float top = 2.6f) =>
            new Opening { center = center, width = width, bottom = bottom, top = top };
    }

    [MenuItem("PlayersMarket/Kasaba Greybox Olustur")]
    static void BuildFromMenu()
    {
        if (!EditorUtility.DisplayDialog("PlayersMarket",
                "Game sahnesine kasaba greybox'i kurulacak.\n\n" +
                "- '" + RootName + "' varsa silinip yeniden kurulur (icindeki elle degisiklikler gider)\n" +
                "- Eski test zemini (Ground, Props) silinir\n" +
                "- Dogus noktalari dukkanin onune tasinir\n\nDevam?",
                "Kur", "Vazgec"))
            return;
        Build();
    }

    static void Build()
    {
        if (!File.Exists(GameScenePath))
        {
            Debug.LogError("[TownBlockout] Game.unity yok. Once: PlayersMarket > Ag Kurulumunu Yeniden Olustur");
            return;
        }

        Scene scene = SceneManager.GetSceneByPath(GameScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        }
        SceneManager.SetActiveScene(scene);

        // Eski kok ve test objeleri
        foreach (GameObject go in scene.GetRootGameObjects())
            if (go.name == RootName || go.name == "Ground" || go.name == "Props")
                Object.DestroyImmediate(go);

        PrepareAssets();
        reserved.Clear();
        rng = new System.Random(20261001); // sabit tohum: her kurulumda ayni dizilim
        ReserveBaseAreas();

        var root = new GameObject(RootName).transform;
        BuildGround(root);
        BuildShop(root);
        BuildTownBuildings(root);
        BuildOutskirts(root);
        BuildPlazaProps(root);
        BuildGatesAndBounds(root);
        BuildNature(root);
        BuildOuterForest(root);
        PaintTerrain();
        BakeNavMesh(root);
        MoveSpawnPoints(scene);

        // Statik: batching icin (isik/gezinme ileride)
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.GetComponent<MeshRenderer>() != null && t.GetComponent<Light>() == null)
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root.gameObject;
        Debug.Log($"[TownBlockout] Kuruldu: {root.childCount} grup, {meshCache.Count} farkli kutu olcusu.");
    }

    // ================= Zemin, meydan, yollar =================

    // ================= Terrain =================
    // 240x240 m terrain, merkez (0,0). Oynanan alan (+-60) TAMAMEN duz, dunya y = 0 (binalar/esyalar havada
    // kalmasin). Disarida yumusak tepeler (gorunmez sinirin otesi bos gorunmesin). Yollar/meydan toprak
    // katmaniyla boyanir; cim alanlara terrain otu. Boyama, binalar/agaclar yerlestikten sonra (PaintTerrain).

    const float TerrainSize = 240f;
    const float TerrainHeight = 40f;
    const float TerrainBase = 2f;           // duz zemin terrain tabanindan 2 m yukarida (ileride gol/cukur icin pay)
    const int HeightRes = 257, AlphaRes = 256, DetailRes = 256;
    const string TerrainDataPath = "Assets/Greybox/TownTerrain.asset";

    static Terrain terrain;

    // Toprak (yol/meydan) alanlari: xMin, zMin, xMax, zMax
    static readonly Vector4[] DirtAreas =
    {
        new Vector4(-20f, -20f, 20f, 20f),     // meydan
        new Vector4(20f, -3f, 50f, 3f),        // dogu yolu (zindan)
        new Vector4(-3f, -58f, 3f, -20f),      // guney yolu (giris)
        new Vector4(-25f, -2f, -20f, 2f),      // demirci
        new Vector4(-26f, -22.5f, -20f, -19.5f), // simyaci
        new Vector4(20f, 10f, 25f, 14f),       // lonca
        new Vector4(11f, -25f, 15f, -20f),     // tefeci
        new Vector4(-9f, 18f, 9f, 22f),        // dukkan onu
        new Vector4(46f, -5f, 56f, 5f),        // zindan kapisi onu
        new Vector4(-6f, -60f, 6f, -53f),      // kasaba girisi
        new Vector4(-14.5f, 37f, -10.5f, 40f), // arka kapi
    };

    static void BuildGround(Transform root)
    {
        if (File.Exists(TerrainDataPath)) AssetDatabase.DeleteAsset(TerrainDataPath);

        var data = new TerrainData();
        data.heightmapResolution = HeightRes;
        data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
        data.alphamapResolution = AlphaRes;
        data.SetDetailResolution(DetailRes, 16);
        data.terrainLayers = TerrainLayers();
        data.SetHeights(0, 0, Heights());
        AssetDatabase.CreateAsset(data, TerrainDataPath);

        GameObject go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3(-TerrainSize * 0.5f, -TerrainBase, -TerrainSize * 0.5f);
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);

        terrain = go.GetComponent<Terrain>();
        RenderPipelineAsset rp = GraphicsSettings.defaultRenderPipeline;
        if (rp != null && rp.defaultTerrainMaterial != null) terrain.materialTemplate = rp.defaultTerrainMaterial;
        terrain.detailObjectDistance = 55f;
        terrain.detailObjectDensity = 1f;
        terrain.basemapDistance = 150f;
        terrain.heightmapPixelError = 3f;
    }

    // Kenar mesafesi (kose yuvarlatilmis kare): duz alan 62'ye kadar, 95'te tam tepe
    static float EdgeT(float x, float z)
    {
        float d = Mathf.Pow(Mathf.Pow(Mathf.Abs(x), 4f) + Mathf.Pow(Mathf.Abs(z), 4f), 0.25f);
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(62f, 95f, d));
    }

    static float[,] Heights()
    {
        var h = new float[HeightRes, HeightRes];
        float flat = TerrainBase / TerrainHeight;
        for (int zi = 0; zi < HeightRes; zi++)
            for (int xi = 0; xi < HeightRes; xi++)
            {
                float x = -TerrainSize * 0.5f + xi / (float)(HeightRes - 1) * TerrainSize;
                float z = -TerrainSize * 0.5f + zi / (float)(HeightRes - 1) * TerrainSize;
                float t = EdgeT(x, z);
                float hill = t * (6f + 12f * Mathf.PerlinNoise(x * 0.018f + 31f, z * 0.018f + 7f))
                           + t * t * 6f * Mathf.PerlinNoise(x * 0.06f + 100f, z * 0.06f + 50f);
                h[zi, xi] = flat + hill / TerrainHeight;
            }
        return h;
    }

    // 0 = cim, 1 = toprak/cakil (yol, meydan), 2 = tepe cimi (koyu)
    static TerrainLayer[] TerrainLayers()
    {
        Texture2D grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EmaceArt/Slavic World Free/Texture/Flower_Grass.png");
        Texture2D grassNm = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EmaceArt/Slavic World Free/Texture/Flower_Grass_NM.png");
        Texture2D dirtTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EmaceArt/Slavic World Free/Texture/Pobbles_Sand_Dirt.png");

        TerrainLayer grass = Layer("Terrain Grass", grassTex != null ? grassTex : grid, grassNm, 10f,
            grassTex != null ? Color.white : new Color(0.46f, 0.62f, 0.36f));
        TerrainLayer dirt = Layer("Terrain Dirt", dirtTex != null ? dirtTex : grid, null, 6f,
            dirtTex != null ? Color.white : new Color(0.62f, 0.56f, 0.47f));
        TerrainLayer hill = Layer("Terrain Hill", grassTex != null ? grassTex : grid, grassNm, 14f,
            grassTex != null ? new Color(0.72f, 0.8f, 0.68f) : new Color(0.36f, 0.5f, 0.3f));
        return new[] { grass, dirt, hill };
    }

    static TerrainLayer Layer(string name, Texture2D diffuse, Texture2D normal, float tile, Color tint)
    {
        string path = $"{Dir}/{name}.terrainlayer";
        TerrainLayer l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (l == null)
        {
            l = new TerrainLayer();
            AssetDatabase.CreateAsset(l, path);
        }
        l.diffuseTexture = diffuse;
        l.normalMapTexture = normal;
        l.normalScale = normal != null ? 1f : 0f;
        l.tileSize = new Vector2(tile, tile);
        l.smoothness = 0f;
        l.diffuseRemapMin = Vector4.zero;
        l.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f); // URP'de renk carpani
        EditorUtility.SetDirty(l);
        return l;
    }

    // Yumusak kenarli dikdortgen kaplamasi (kenara kucuk gurultu: dogal yol kenari)
    static float DirtAt(float x, float z)
    {
        float best = 0f;
        float jitter = (Mathf.PerlinNoise(x * 0.35f, z * 0.35f) - 0.5f) * 1.2f;
        foreach (Vector4 a in DirtAreas)
        {
            float dx = Mathf.Max(a.x - x, 0f, x - a.z);
            float dz = Mathf.Max(a.y - z, 0f, z - a.w);
            float dist = Mathf.Sqrt(dx * dx + dz * dz) + jitter;
            best = Mathf.Max(best, 1f - Mathf.Clamp01(dist / 1.6f));
        }
        return best;
    }

    // Binalar/agaclar yerlestikten sonra: katman boyama + terrain otu
    static void PaintTerrain()
    {
        if (terrain == null) return;
        TerrainData data = terrain.terrainData;

        var alpha = new float[AlphaRes, AlphaRes, 3];
        for (int zi = 0; zi < AlphaRes; zi++)
            for (int xi = 0; xi < AlphaRes; xi++)
            {
                float x = -TerrainSize * 0.5f + (xi + 0.5f) / AlphaRes * TerrainSize;
                float z = -TerrainSize * 0.5f + (zi + 0.5f) / AlphaRes * TerrainSize;
                float dirt = DirtAt(x, z);
                float t = EdgeT(x, z);
                alpha[zi, xi, 0] = (1f - dirt) * (1f - t);
                alpha[zi, xi, 1] = dirt;
                alpha[zi, xi, 2] = (1f - dirt) * t;
            }
        data.SetAlphamaps(0, 0, alpha);

        // Terrain otu (paketin ot dokusu; yoksa atla)
        Texture2D grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EmaceArt/Slavic World Free/Texture/Grass_Texture.png");
        if (grassTex == null) return;
        data.detailPrototypes = new[]
        {
            new DetailPrototype
            {
                prototypeTexture = grassTex,
                renderMode = DetailRenderMode.Grass,
                usePrototypeMesh = false,
                minWidth = 0.5f, maxWidth = 0.9f,
                minHeight = 0.35f, maxHeight = 0.7f,
                noiseSpread = 0.4f,
                healthyColor = new Color(0.75f, 0.85f, 0.55f),
                dryColor = new Color(0.8f, 0.75f, 0.5f),
            },
        };
        var density = new int[DetailRes, DetailRes];
        for (int zi = 0; zi < DetailRes; zi++)
            for (int xi = 0; xi < DetailRes; xi++)
            {
                float x = -TerrainSize * 0.5f + (xi + 0.5f) / DetailRes * TerrainSize;
                float z = -TerrainSize * 0.5f + (zi + 0.5f) / DetailRes * TerrainSize;
                if (DirtAt(x, z) > 0.05f) continue;
                if (Mathf.Abs(x) < 59f && Mathf.Abs(z) < 59f && !Free(new Vector2(x, z), 0.5f)) continue; // bina/agac alti
                float patch = Mathf.PerlinNoise(x * 0.08f + 5f, z * 0.08f + 9f); // obekli dagilim
                density[zi, xi] = patch > 0.35f ? (patch > 0.6f ? 4 : 2) : 0;
            }
        data.SetDetailLayer(0, 0, 0, density);
    }

    // Tepelerde orman (sinirin otesi): terrain yuksekligine oturur, carpisma onemsiz
    static void BuildOuterForest(Transform root)
    {
        if (!HasPack || terrain == null) return;
        string[] trees =
        {
            "Nature/Tree/EA03_Nature_Tree_01b_PRE.prefab", "Nature/Tree/EA03_Nature_Tree_02b_PRE.prefab",
            "Nature/Tree/EA03_Nature_Tree_03b_PRE.prefab", "Nature/Tree/EA03_Nature_Tree_03c_PRE.prefab",
            "Nature/Tree/EA03_Nature_Tree_06b_PRE.prefab",
        };
        Transform f = Group(root, "Outer Forest");
        int n = 0;
        for (int i = 0; i < 110; i++)
        {
            Vector2 p = Polar(Rand(0f, 360f), Rand(64f, 105f));
            if (Mathf.Abs(p.x) > 115f || Mathf.Abs(p.y) > 115f) continue;
            float y = terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + terrain.transform.position.y;
            if (PlaceArt(f, trees[rng.Next(trees.Length)], new Vector3(p.x, y - 0.3f, p.y), Rand(0f, 360f), Rand(0.8f, 1.2f)) != null) n++;
        }
        Debug.Log($"[TownBlockout] Dis orman: {n} agac.");
    }

    // ================= Dukkan =================
    // Ana salon x[-9,9] z[22,37] (18x15) · Depo x[-16,-9] · Genisleme alani x[9,19] (sadece isaret)
    // Tavan 5 m. Esya yuvalari (ItemSlot): raf gozleri (kucuk esya), vitrin kaideleri (buyuk esya).

    const float ShopX0 = -16f, ShopXM = -9f, ShopX1 = 9f, ShopXE = 19f, ShopZ0 = 22f, ShopZ1 = 37f;

    static void BuildShop(Transform root)
    {
        Transform s = Group(root, "Shop");
        Transform walls = Group(s, "Walls");
        Transform furn = Group(s, "Furniture");
        Transform marks = Group(s, "Zones");

        const float x0 = ShopX0, xMid = ShopXM, x1 = ShopX1, z0 = ShopZ0, z1 = ShopZ1;
        float h = WallT * 0.5f;
        const float doorW = 3f, doorH = 3.4f;

        // Zemin + veranda
        BoxMinMax(s, "Floor", new Vector3(x0, 0f, z0), new Vector3(x1, 0.08f, z1), mShopFloor);
        BoxMinMax(s, "Porch", new Vector3(-9f, 0f, 20f), new Vector3(9f, 0.06f, z0), mWood);

        // Dis duvarlar
        Wall(walls, "Wall Front", new Vector3(x0 - h, 0f, z0), new Vector3(x1 + h, 0f, z0), WallH, mShopWall,
            Opening.Door(0f, doorW, doorH), Opening.Window(-5.5f, 3.5f, 1f, 3f), Opening.Window(5.5f, 3.5f, 1f, 3f),
            Opening.Window(-12.5f, 3f, 1f, 3f));
        Wall(walls, "Wall Back", new Vector3(x0 - h, 0f, z1), new Vector3(x1 + h, 0f, z1), WallH, mShopWall,
            Opening.Door(-12.5f, doorW, doorH));
        Wall(walls, "Wall West", new Vector3(x0, 0f, z0 + h), new Vector3(x0, 0f, z1 - h), WallH, mShopWall);
        // Dogu duvari: ileride yikilip genisleme alanina acilacak
        Wall(walls, "Wall East (yikilabilir)", new Vector3(x1, 0f, z0 + h), new Vector3(x1, 0f, z1 - h), WallH, mBreakable);
        // Ara duvar: salon <-> depo
        Wall(walls, "Wall Storage", new Vector3(xMid, 0f, z0 + h), new Vector3(xMid, 0f, z1 - h), WallH, mShopWall,
            Opening.Door(z1 - 4f, doorW, doorH));

        // Cam vitrinler (gecilmez)
        Transform glass = Group(walls, "Glass");
        BoxMinMax(glass, "Glass L", new Vector3(-7.25f, 1f, z0 - 0.03f), new Vector3(-3.75f, 3f, z0 + 0.03f), mGlass);
        BoxMinMax(glass, "Glass R", new Vector3(3.75f, 1f, z0 - 0.03f), new Vector3(7.25f, 3f, z0 + 0.03f), mGlass);
        BoxMinMax(glass, "Glass Depo", new Vector3(-14f, 1f, z0 - 0.03f), new Vector3(-11f, 3f, z0 + 0.03f), mGlass);

        // Tavan + cati (ic mekan kamerasini test etmek icin kapali; gerekirse Ceiling'i kapat)
        BoxMinMax(s, "Ceiling", new Vector3(x0 - h, WallH, z0 - h), new Vector3(x1 + h, WallH + 0.25f, z1 + h), mCeiling);
        BoxMinMax(s, "Roof", new Vector3(x0 - 0.6f, WallH + 0.25f, z0 - 0.8f), new Vector3(x1 + 0.6f, WallH + 0.55f, z1 + 0.6f), mRoof);

        // Tabela (kapinin ustu)
        BoxMinMax(s, "Sign Board", new Vector3(-3f, doorH + 0.3f, z0 - h - 0.12f), new Vector3(3f, doorH + 1.3f, z0 - h), mWood);
        Label(s, "DÜKKAN", new Vector3(0f, doorH + 0.8f, z0 - h - 0.14f), 0f, 8f, Color.white);

        // --- Mobilya (yer tutucu, olcu icin) ---
        // Kasa (sag on)
        BoxMinMax(furn, "Counter", new Vector3(3.5f, 0f, 26.5f), new Vector3(7.5f, 1.05f, 27.4f), mWood);
        BoxMinMax(furn, "Register", new Vector3(6.6f, 1.05f, 26.7f), new Vector3(7.2f, 1.35f, 27.2f), mDark);
        Label(furn, "KASA", new Vector3(5.5f, 2.3f, 26.95f), 0f, 6f, Color.black);

        // Raflar: iki sira (orta koridor 3.5 m, kasaya 3.3 m) + arka duvar rafi
        Shelf(furn, "Shelf Row A", new Vector3(-4.75f, 0f, 31f), 6f, 0.6f, 2.2f, 4, 0f);
        Shelf(furn, "Shelf Row B", new Vector3(4.75f, 0f, 31f), 6f, 0.6f, 2.2f, 4, 0f);
        Shelf(furn, "Shelf Back Wall", new Vector3(0f, 0f, z1 - 0.5f), 16f, 0.6f, 2.4f, 4, 0f);
        Label(furn, "RAFLAR", new Vector3(0f, 3f, 31f), 0f, 6f, Color.black);

        // Vitrin kaideleri (sol pencere onu) → buyuk esya yuvasi
        Pedestal(furn, "Pedestal 1", new Vector3(-7f, 0f, z0 + 1.4f));
        Pedestal(furn, "Pedestal 2", new Vector3(-4f, 0f, z0 + 1.4f));

        // Siparis panosu (kapinin solu, iceriden)
        BoxMinMax(furn, "Request Board", new Vector3(-3.3f, 1.2f, z0 + h), new Vector3(-2f, 2.3f, z0 + h + 0.08f), mWood);
        Label(furn, "SİPARİŞ PANOSU", new Vector3(-2.65f, 2.55f, z0 + h + 0.12f), 180f, 3f, Color.black);

        // Depo
        BoxMinMax(furn, "Storage Chest", new Vector3(-15.5f, 0f, 22.6f), new Vector3(-13.5f, 1f, 23.8f), mWood);
        Label(furn, "DEPO SANDIĞI", new Vector3(-14.5f, 1.6f, 23.2f), 180f, 3.5f, Color.black);
        BoxMinMax(furn, "Appraisal Table", new Vector3(-12.5f, 0f, 27f), new Vector3(-10.5f, 0.95f, 28f), mWood);
        Label(furn, "DEĞERLEME", new Vector3(-11.5f, 1.6f, 27.5f), 180f, 3.5f, Color.black);
        if (HasPack)
        {
            DressShop(Group(s, "Decor"));
        }
        else
        {
            BoxMinMax(furn, "Crate 1", new Vector3(-15.8f, 0f, 28.5f), new Vector3(-14.8f, 1f, 29.5f), mWood);
            BoxMinMax(furn, "Crate 2", new Vector3(-15.8f, 0f, 29.6f), new Vector3(-14.8f, 1f, 30.6f), mWood);
            BoxMinMax(furn, "Crate 3", new Vector3(-15.8f, 1f, 29f), new Vector3(-14.8f, 2f, 30f), mWood);
        }

        // TEST: depoda ganimet bırakma alanina esya dokulur (zindan gelene kadar)
        var loot = new GameObject("Test Loot Spawner");
        loot.transform.SetParent(s, false);
        loot.transform.position = new Vector3(-12.5f, 0.1f, z1 - 1.6f);
        TestLootSpawner spawner = loot.AddComponent<TestLootSpawner>();
        spawner.area = new Vector2(3.5f, 1.8f);

        // --- Bolge isaretleri (seffaf zemin + yazi) ---
        Zone(marks, "Queue", new Vector2(2.5f, 22.8f), new Vector2(6f, 26.2f), zCounter, "SIRA");
        Zone(marks, "Display", new Vector2(-8.5f, 22.4f), new Vector2(-2.5f, 24.6f), zDisplay, "VİTRİN");
        Zone(marks, "Loot Drop", new Vector2(-14.8f, z1 - 2.8f), new Vector2(-10.2f, z1 - 0.3f), zStorage, "GANİMET BIRAKMA");
        Zone(marks, "Expansion", new Vector2(x1 + 0.2f, z0), new Vector2(ShopXE, z1), zExpansion, "GENİŞLEME ALANI (Sv.2)");
        Label(marks, "ARKA KAPI\n(zindandan ganimet)", new Vector3(-12.5f, doorH + 0.7f, z1 + h + 0.05f), 180f, 3f, Color.black);
        Label(marks, "YIKILABİLİR DUVAR", new Vector3(x1 + h + 0.05f, 3.5f, (z0 + z1) * 0.5f), -90f, 4f, Color.black);

        // Musteri yol noktalari (CustomerAI)
        var layoutGo = new GameObject("Shop Layout");
        layoutGo.transform.SetParent(s, false);
        ShopLayout layout = layoutGo.AddComponent<ShopLayout>();
        layout.spawnPoint = Point(layoutGo.transform, "Customer Spawn", new Vector3(0f, 0f, -58.5f));
        layout.doorInside = Point(layoutGo.transform, "Door Inside", new Vector3(0f, 0.08f, z0 + 2f));
        layout.counterFront = Point(layoutGo.transform, "Counter Front", new Vector3(5.5f, 0.08f, 25.8f));
        layout.counterFacing = Point(layoutGo.transform, "Counter Facing", new Vector3(5.5f, 1f, 27.5f));
        layout.queueDirection = Vector3.back;
        layout.queueTurnDirection = Vector3.left;

        // Ic isiklar (tavan kapali oldugu icin)
        Transform lights = Group(s, "Lights");
        float ly = WallH - 0.4f;
        PointLight(lights, new Vector3(-4.5f, ly, 26f));
        PointLight(lights, new Vector3(4.5f, ly, 26f));
        PointLight(lights, new Vector3(-4.5f, ly, 33f));
        PointLight(lights, new Vector3(4.5f, ly, 33f));
        PointLight(lights, new Vector3(-12.5f, ly, 29.5f));
    }

    // Sus objeleri (paket): depo kasalari/fucilari, salon koseleri, veranda. Koridor ve yuvalari tikamaz.
    static void DressShop(Transform d)
    {
        const string crateA = "Prop/Container/EA03_Prop_Container_Crate_01a_PRE.prefab";  // 0.88 x 0.64
        const string crateB = "Prop/Container/EA03_Prop_Container_Crate_02a_PRE.prefab";  // 1.04 x 0.85
        const string crateC = "Prop/Container/EA03_Prop_Container_Crate_03a_PRE.prefab";
        const string barrel = "Prop/Container/EA03_Prop_Container_Barrel_01d_PRE.prefab"; // 0.66 x 0.82
        const string bag = "Prop/Container/EA03_Prop_Container_Bag_02a_PRE.prefab";
        const string basket = "Prop/Container/EA03_Prop_Container_Basket_02_PRE.prefab";
        const string vegBasket = "Prop/Container/EA03_Prop_Vegetable_Basket_01a_PRE.prefab";
        const string stool = "Prop/Furniture/EA03_Prop_Stool_01a_PRE.prefab";
        const string bench = "Prop/Furniture/EA03_Prop_Town_Bench_01a_PRE.prefab";

        // Depo: bati duvari boyunca istif
        PlaceArt(d, crateB, new Vector3(-15.2f, 0f, 29.1f), 0f);
        PlaceArt(d, crateA, new Vector3(-15.2f, 0.853f, 29.1f), 15f);  // ustte
        PlaceArt(d, crateC, new Vector3(-15.2f, 0f, 30.3f), -8f);
        PlaceArt(d, barrel, new Vector3(-15.3f, 0f, 25.4f), 0f);
        PlaceArt(d, barrel, new Vector3(-15.3f, 0f, 26.2f), 40f);
        PlaceArt(d, bag, new Vector3(-14.4f, 0f, 25.8f), 70f);
        PlaceArt(d, basket, new Vector3(-12.8f, 0f, 23.2f), 0f);

        // Salon koseleri (koridor disi)
        PlaceArt(d, barrel, new Vector3(8.3f, 0.08f, 22.8f), 0f);
        PlaceArt(d, vegBasket, new Vector3(8.3f, 0.08f, 23.8f), 30f);
        PlaceArt(d, barrel, new Vector3(-8.4f, 0.08f, 27.6f), 10f);
        PlaceArt(d, stool, new Vector3(5.5f, 0.08f, 28.3f), 0f);         // kasanin arkasi

        // Veranda
        PlaceArt(d, barrel, new Vector3(-8.4f, 0.06f, 20.7f), 0f);
        PlaceArt(d, crateA, new Vector3(-7.4f, 0.06f, 20.7f), 20f);
        PlaceArt(d, barrel, new Vector3(8.4f, 0.06f, 20.7f), 0f);
        PlaceArt(d, bench, new Vector3(5f, 0.06f, 20.7f), 90f);
    }

    static Transform Point(Transform parent, string name, Vector3 pos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.position = pos;
        return t;
    }

    // Musteriler icin NavMesh: kasaba kokunun altindaki tum fizik collider'lari (terrain, duvar, raf, model)
    static void BakeNavMesh(Transform root)
    {
        const string navPath = "Assets/Greybox/TownNavMesh.asset";
        if (File.Exists(navPath)) AssetDatabase.DeleteAsset(navPath);
        NavMeshSurface surface = root.gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
        if (surface.navMeshData != null)
        {
            AssetDatabase.CreateAsset(surface.navMeshData, navPath);
            Debug.Log("[TownBlockout] NavMesh firinlandi: " + navPath);
        }
        else Debug.LogWarning("[TownBlockout] NavMesh firinlanamadi.");
    }

    // Vitrin kaidesi + ustunde buyuk esya yuvasi
    static void Pedestal(Transform parent, string name, Vector3 baseCenter)
    {
        Transform g = Group(parent, name);
        BoxMinMax(g, "Stand", baseCenter + new Vector3(-0.5f, 0f, -0.5f), baseCenter + new Vector3(0.5f, 0.9f, 0.5f), mStone);
        AddSlot(g, name + "/Top", baseCenter + Vector3.up * 0.9f, Quaternion.identity, ItemSize.Large);
    }

    static void AddSlot(Transform parent, string id, Vector3 position, Quaternion rotation, ItemSize maxSize)
    {
        var go = new GameObject("Slot " + id);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);
        ItemSlot slot = go.AddComponent<ItemSlot>();
        slot.id = id;
        slot.maxSize = maxSize;
    }

    // ================= Diger binalar =================

    static void BuildTownBuildings(Transform root)
    {
        Transform b = Group(root, "Buildings");
        // Son parametre: modelin Y donusu. Kapisi meydana bakmiyorsa 90/180/270 dene (paketin on yonu belirsiz).
        Building(b, "DEMİRCİ", new Vector3(-36f, 0f, -6f), new Vector3(-25f, 6f, 6f), Vector3.right,
            "Town/Building/EA03_Town_House_Comp_01a_PRE.prefab", 0f);
        Building(b, "SİMYACI", new Vector3(-35f, 0f, -26f), new Vector3(-26f, 5.5f, -16f), Vector3.right,
            "Village/Building/Other/EA03_Village_OutBuilding_Cubby_01d_PRE.prefab", 0f);
        Building(b, "LONCA", new Vector3(25f, 0f, 6f), new Vector3(36f, 7f, 18f), Vector3.left,
            "Town/Administrative/EA03_Town_Building_Administrative _01a_PRE.prefab", 0f);
        Building(b, "TEFECİ", new Vector3(8f, 0f, -34f), new Vector3(18f, 5f, -25f), Vector3.forward,
            "Village/OutBuilding/EA03_Village_OutBuilding_PentHouse_01a_PRE.prefab", 0f);

        BuildNpcs(root);
    }

    // NPC'ler (simdilik kapsul + isim; ileride karakter modeli). NpcStation etkilesimi saglar.
    static void BuildNpcs(Transform root)
    {
        Transform n = Group(root, "NPCs");

        // Demirci: binanin onunde, yolun kuzey yaninda; meydana (doguya) bakar. Yaninda ors.
        Transform smith = Npc(n, "Blacksmith NPC", "DEMİRCİ", new Vector3(-22.6f, 0f, 3.6f), 90f, NpcType.Blacksmith, "Demirci");
        Transform anvil = Group(smith, "Anvil");
        BoxMinMax(anvil, "Base", new Vector3(-21.75f, 0f, 4.25f), new Vector3(-21.25f, 0.6f, 4.75f), mStone);
        BoxMinMax(anvil, "Top", new Vector3(-22f, 0.6f, 4.35f), new Vector3(-21f, 0.85f, 4.65f), mDark);
        reserved.Add(Rect.MinMaxRect(-24.5f, 2f, -20f, 6f)); // dogaya kapali

        // Satici: meydanin bati pazar tezgahinin arkasinda, meydana (doguya) bakar. Oyuncu tezgahin onunden konusur.
        Transform vendor = Npc(n, "Vendor NPC", "SATICI", new Vector3(-15.3f, 0f, 6f), 90f, NpcType.Vendor, "Satıcı");
        vendor.GetComponent<NpcStation>().interactRadius = 3.4f;
        if (!HasPack) BoxMinMax(n, "Vendor Counter", new Vector3(-14.5f, 0f, 4.8f), new Vector3(-13.6f, 1f, 7.2f), mWood);

        // Egitim alani (meydanin guneydogusu): kukla noktalari meydan merkezine bakar; sunucu GameState dogurur
        Transform yard = Group(root, "Training Yard");
        Vector3[] dummies = { new Vector3(11f, 0f, -13f), new Vector3(14f, 0f, -15.5f), new Vector3(17f, 0f, -13f) };
        foreach (Vector3 d in dummies)
        {
            Transform pt = Point(yard, "Dummy Point", d);
            Vector3 toCenter = new Vector3(-d.x, 0f, -d.z);
            pt.rotation = Quaternion.LookRotation(toCenter);
            pt.gameObject.AddComponent<TrainingDummyPoint>();
        }
        Label(yard, "EĞİTİM ALANI", new Vector3(14f, 0.08f, -11f), 0f, 4f, Color.white, true);
    }

    static Transform Npc(Transform parent, string name, string title, Vector3 pos, float yaw, NpcType type, string displayName)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        body.transform.localScale = new Vector3(0.9f, 1f, 0.9f);
        body.GetComponent<MeshRenderer>().sharedMaterial = mTrunk;

        GameObject apron = GameObject.CreatePrimitive(PrimitiveType.Cube); // onluk = bakis yonu
        apron.name = "Apron";
        apron.transform.SetParent(root, false);
        apron.transform.localPosition = new Vector3(0f, 0.9f, 0.42f);
        apron.transform.localScale = new Vector3(0.6f, 0.9f, 0.06f);
        Object.DestroyImmediate(apron.GetComponent<Collider>());
        apron.GetComponent<MeshRenderer>().sharedMaterial = mDark;

        Label(root, title, pos + Vector3.up * 2.45f, yaw + 180f, 4f, Color.white);

        NpcStation st = root.gameObject.AddComponent<NpcStation>();
        st.type = type;
        st.displayName = displayName;
        st.interactRadius = 2.8f;
        return root;
    }

    // Bina. Paket modeli varsa: model, on yuzu greybox yuzune hizali (buyukse meydandan uzaga buyur)
    // + onunde tabela diregi. Yoksa kapali gri kutu + kapi + tabela. Girilemez (simdilik).
    static void Building(Transform parent, string title, Vector3 min, Vector3 max, Vector3 doorDir,
        string art = null, float artYaw = 0f)
    {
        Transform g = Group(parent, title);
        Vector3 c = (min + max) * 0.5f;
        Vector3 face = new Vector3(
            doorDir.x > 0 ? max.x : doorDir.x < 0 ? min.x : c.x, 0f,
            doorDir.z > 0 ? max.z : doorDir.z < 0 ? min.z : c.z);
        Vector3 across = Vector3.Cross(Vector3.up, doorDir); // kapi genisligi yonu
        float yaw = Quaternion.LookRotation(-doorDir).eulerAngles.y; // tabelayi okuyanin baktigi yon

        GameObject model = HasPack && art != null ? PlaceArt(g, art, new Vector3(c.x, 0f, c.z), artYaw) : null;
        if (model != null)
        {
            Bounds mb = WorldBounds(model);
            bool alongX = Mathf.Abs(doorDir.x) > 0.5f;
            float modelFace = alongX ? (doorDir.x > 0 ? mb.max.x : mb.min.x) : (doorDir.z > 0 ? mb.max.z : mb.min.z);
            float greyFace = alongX ? face.x : face.z;
            model.transform.position += (alongX ? Vector3.right : Vector3.forward) * (greyFace - modelFace);
            Reserve(WorldBounds(model), 1f);
            SignPost(g, title, face + doorDir * 2f + across * 3f, yaw);
            return;
        }

        BoxMinMax(g, "Body", min, max, mWall);
        BoxMinMax(g, "Roof", new Vector3(min.x - 0.5f, max.y, min.z - 0.5f), new Vector3(max.x + 0.5f, max.y + 0.4f, max.z + 0.5f), mRoof);
        Reserve(new Bounds(c, max - min), 1f);

        // Kapi: yuzeyden 0.1 tasan koyu kutu
        Vector3 doorSize = Abs(across * 2.4f) + Abs(doorDir * 0.2f) + Vector3.up * 3f;
        Box(g, "Door", face + Vector3.up * 1.5f + doorDir * 0.05f, doorSize, mDark);

        // Tabela: kapinin ustunde, disariya bakar. Okuyan kisi -doorDir yonune bakar.
        Vector3 boardSize = Abs(across * 4.5f) + Abs(doorDir * 0.12f) + Vector3.up * 1f;
        Box(g, "Sign Board", face + Vector3.up * 3.9f + doorDir * 0.06f, boardSize, mWood);
        Label(g, title, face + Vector3.up * 3.9f + doorDir * 0.14f, yaw, 7f, Color.white);
    }

    // Ayakta tabela (modelin hangi yone baktigindan bagimsiz okunur)
    static void SignPost(Transform parent, string title, Vector3 basePos, float yaw)
    {
        Transform s = Group(parent, "Sign Post");
        Quaternion r = Quaternion.Euler(0f, yaw, 0f);
        BoxMinMax(s, "Post", basePos + new Vector3(-0.08f, 0f, -0.08f), basePos + new Vector3(0.08f, 2.9f, 0.08f), mWood);
        GameObject board = Box(s, "Board", basePos + Vector3.up * 2.5f, new Vector3(2.8f, 0.8f, 0.1f), mWood);
        board.transform.rotation = r;
        Label(s, title, basePos + Vector3.up * 2.5f + r * new Vector3(0f, 0f, -0.07f), yaw, 5f, Color.white);
    }

    // Kenar mahalle: sadece sanat paketi varsa (kasabanin dolu gorunmesi icin, girilemez)
    static void BuildOutskirts(Transform root)
    {
        if (!HasPack) return;
        Transform o = Group(root, "Outskirts");

        void House(string name, string rel, float x, float z, float yaw)
        {
            GameObject m = PlaceArt(Group(o, name), rel, new Vector3(x, 0f, z), yaw);
            if (m != null) Reserve(WorldBounds(m), 1.5f);
        }

        House("House NW", "Town/Building/EA03_Town_House_Comp_03c_PRE.prefab", -38f, 38f, 180f);
        House("House N", "Town/Administrative/EA03_Town_Building_Administrative _01c_PRE.prefab", -12f, 52f, 180f);
        House("House NE", "Town/Building/EA03_Town_House_Comp_01a_PRE.prefab", 16f, 48f, 180f);
        House("Tower", "Village/Building/Other/EA03_Village_Tover_01a_PRE.prefab", 42f, 38f, 0f);
        House("House SW", "Town/Building/EA03_Town_House_Comp_02a_PRE.prefab", -38f, -42f, 0f);
        House("House SE", "Town/Building/EA03_Town_House_Comp_03a_PRE.prefab", 35f, -38f, 0f);
        House("Shed E", "Village/OutBuilding/EA03_Village_OutBuilding_Shed_01a_PRE.prefab", 40f, -12f, 0f);
    }

    // ================= Meydan esyalari =================

    static void BuildPlazaProps(Transform root)
    {
        Transform p = Group(root, "Plaza Props");

        if (HasPack)
        {
            // Kuyu (merkez), banklar, pazar tezgahlari (sus; ileride pazar gunu etkinligi)
            const string bench = "Prop/Furniture/EA03_Prop_Town_Bench_01a_PRE.prefab";
            const string stall = "Village/OutBuilding/EA03_Village_OutBuilding_WoodRoof_01b_PRE.prefab";
            const string table = "Prop/Furniture/EA03_Prop_Tabble_02a_PRE 1.prefab";
            PlaceArt(p, "Prop/Village/EA03_Prop_Village_Whell_01d_PRE.prefab", Vector3.zero, 0f);
            PlaceArt(p, bench, new Vector3(-6.5f, 0f, 0f), 0f);
            PlaceArt(p, bench, new Vector3(6.5f, 0f, 0f), 0f);
            PlaceArt(p, bench, new Vector3(0f, 0f, -6.5f), 90f);

            Vector3[] stalls = { new Vector3(-14f, 0f, -6f), new Vector3(-14f, 0f, 6f), new Vector3(14f, 0f, -6f) };
            string[] goods =
            {
                "Prop/Container/EA03_Prop_Vegetable_Basket_01a_PRE.prefab",
                "Prop/Container/EA03_Prop_Vegetable_Bag_02_PRE.prefab",
                "Prop/Container/EA03_Prop_Container_Basket_01a_PRE.prefab",
            };
            for (int i = 0; i < stalls.Length; i++)
            {
                Transform st = Group(p, "Market Stall " + (i + 1));
                PlaceArt(st, stall, stalls[i], 90f);
                PlaceArt(st, table, stalls[i], 90f);
                PlaceArt(st, goods[i], stalls[i] + new Vector3(1.2f, 0f, 1.6f), 25f * i);
            }
        }
        else
        {
            // Cesme (merkez)
            Transform f = Group(p, "Fountain");
            BoxMinMax(f, "Rim N", new Vector3(-3f, 0f, 2.6f), new Vector3(3f, 0.6f, 3f), mStone);
            BoxMinMax(f, "Rim S", new Vector3(-3f, 0f, -3f), new Vector3(3f, 0.6f, -2.6f), mStone);
            BoxMinMax(f, "Rim W", new Vector3(-3f, 0f, -2.6f), new Vector3(-2.6f, 0.6f, 2.6f), mStone);
            BoxMinMax(f, "Rim E", new Vector3(2.6f, 0f, -2.6f), new Vector3(3f, 0.6f, 2.6f), mStone);
            BoxMinMax(f, "Water", new Vector3(-2.6f, 0.05f, -2.6f), new Vector3(2.6f, 0.35f, 2.6f), mWater, false);
            BoxMinMax(f, "Pillar", new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 2.2f, 0.5f), mStone);
        }

        // Fenerler (meydan koseleri)
        float[] c = { -19f, 19f };
        foreach (float x in c)
            foreach (float z in c)
            {
                Transform l = Group(p, $"Lamp {x},{z}");
                BoxMinMax(l, "Post", new Vector3(x - 0.1f, 0f, z - 0.1f), new Vector3(x + 0.1f, 3.5f, z + 0.1f), mDark);
                BoxMinMax(l, "Head", new Vector3(x - 0.25f, 3.5f, z - 0.25f), new Vector3(x + 0.25f, 4f, z + 0.25f), mLamp);
            }

        if (!HasPack)
        {
            // Banklar
            BoxMinMax(p, "Bench S", new Vector3(-1f, 0f, -7f), new Vector3(1f, 0.5f, -6.4f), mWood);
            BoxMinMax(p, "Bench W", new Vector3(-7f, 0f, -1f), new Vector3(-6.4f, 0.5f, 1f), mWood);
            BoxMinMax(p, "Bench E", new Vector3(6.4f, 0f, -1f), new Vector3(7f, 0.5f, 1f), mWood);
        }
    }

    // ================= Kapilar ve sinirlar =================

    static void BuildGatesAndBounds(Transform root)
    {
        Transform g = Group(root, "Gates");

        // Zindan kapisi (dogu yolunun sonu)
        Transform d = Group(g, "Dungeon Gate");
        BoxMinMax(d, "Pillar N", new Vector3(51f, 0f, 2.5f), new Vector3(53f, 6f, 4f), mGate);
        BoxMinMax(d, "Pillar S", new Vector3(51f, 0f, -4f), new Vector3(53f, 6f, -2.5f), mGate);
        BoxMinMax(d, "Lintel", new Vector3(51f, 6f, -4f), new Vector3(53f, 7.5f, 4f), mGate);
        BoxMinMax(d, "Portal", new Vector3(52f, 0f, -2.5f), new Vector3(52.2f, 6f, 2.5f), zPortal);
        BoxMinMax(d, "Gate Stone", new Vector3(47.5f, 0f, -2f), new Vector3(50f, 0.15f, 2f), mGate);
        Label(d, "ZİNDAN KAPISI", new Vector3(50.9f, 6.75f, 0f), 90f, 7f, Color.white);
        Label(d, "GEÇİT TAŞI", new Vector3(48.75f, 0.17f, 0f), 90f, 4f, Color.white, true);
        if (HasPack)
        {
            const string slab = "Environment/Rock/EA03_Env_Rock_Slice_01a_PRE.prefab"; // 3.7 x 6 x 3.7
            PlaceArt(d, slab, new Vector3(53.5f, 0f, -7f), 20f);
            PlaceArt(d, slab, new Vector3(53.5f, 0f, 7f), 200f, 0.85f);
            PlaceArt(d, "Environment/Rock/EA03_Environment_Rock_Mini_CatHead_01a_PRE.prefab", new Vector3(47f, 0f, -7f), 0f);
        }

        // Kasaba girisi (musteriler buradan gelir)
        Transform e = Group(g, "Town Entrance");
        BoxMinMax(e, "Pillar W", new Vector3(-5f, 0f, -57f), new Vector3(-3.5f, 5f, -55.5f), mStone);
        BoxMinMax(e, "Pillar E", new Vector3(3.5f, 0f, -57f), new Vector3(5f, 5f, -55.5f), mStone);
        BoxMinMax(e, "Lintel", new Vector3(-5f, 5f, -57f), new Vector3(5f, 6f, -55.5f), mStone);
        Label(e, "KASABA GİRİŞİ", new Vector3(0f, 5.5f, -55.4f), 180f, 6f, Color.black);
        Zone(e, "Customer Spawn", new Vector2(-3f, -59f), new Vector2(3f, -57f), zSpawn, "MÜŞTERİ DOĞUŞ");

        // Gorunmez sinir duvarlari
        Transform b = Group(root, "Bounds (gorunmez)");
        InvisibleWall(b, new Vector3(0f, 3f, 60.5f), new Vector3(122f, 6f, 1f));
        InvisibleWall(b, new Vector3(0f, 3f, -60.5f), new Vector3(122f, 6f, 1f));
        InvisibleWall(b, new Vector3(60.5f, 3f, 0f), new Vector3(1f, 6f, 122f));
        InvisibleWall(b, new Vector3(-60.5f, 3f, 0f), new Vector3(1f, 6f, 122f));
    }

    static void BuildNature(Transform root)
    {
        if (HasPack)
        {
            DressNature(root);
            return;
        }

        Transform n = Group(root, "Trees");
        Vector2[] trees =
        {
            new Vector2(-45f, 30f), new Vector2(-50f, 15f), new Vector2(-48f, -40f), new Vector2(-40f, 45f),
            new Vector2(-20f, 45f), new Vector2(10f, 48f), new Vector2(30f, 45f), new Vector2(45f, 30f),
            new Vector2(48f, -20f), new Vector2(40f, -45f), new Vector2(-15f, -45f), new Vector2(25f, -50f),
            new Vector2(-50f, -10f), new Vector2(55f, 15f), new Vector2(-28f, 30f), new Vector2(23f, 30f),
        };
        for (int i = 0; i < trees.Length; i++)
        {
            Vector2 t = trees[i];
            Transform tr = Group(n, "Tree " + (i + 1));
            BoxMinMax(tr, "Trunk", new Vector3(t.x - 0.25f, 0f, t.y - 0.25f), new Vector3(t.x + 0.25f, 2.5f, t.y + 0.25f), mTrunk);
            BoxMinMax(tr, "Canopy", new Vector3(t.x - 1.5f, 2.3f, t.y - 1.5f), new Vector3(t.x + 1.5f, 5f, t.y + 1.5f), mFoliage);
        }
    }

    // Doga (paket): kenarda agac halkasi (orman hissi + sinir), arada cali/ot/kaya.
    // Ayrilmis alanlara (meydan, yollar, binalar) girmez. Sabit tohum → her kurulumda ayni.
    static void DressNature(Transform root)
    {
        string[] trees =
        {
            "Nature/Tree/EA03_Nature_Tree_01b_PRE.prefab", "Nature/Tree/EA03_Nature_Tree_02b_PRE.prefab",
            "Nature/Tree/EA03_Nature_Tree_02c_PRE.prefab", "Nature/Tree/EA03_Nature_Tree_03b_PRE.prefab",
            "Nature/Tree/EA03_Nature_Tree_03c_PRE.prefab", "Nature/Tree/EA03_Nature_Tree_06b_PRE.prefab",
        };
        string[] bushes =
        {
            "Nature/Bushes/EA03_Nature_Bush_01a_PRE.prefab", "Nature/Bushes/EA03_Nature_Bush_01b_PRE.prefab",
            "Nature/Bushes/EA03_Nature_Bush_01c_PRE.prefab", "Nature/Bushes/EA03_Nature_Bush_02a_PRE.prefab",
            "Nature/Bushes/EA03_Nature_Bush_03a_PRE.prefab", "Nature/Bushes/EA03_Nature_Bush_03b_PRE.prefab",
            "Nature/Bushes/EA03_Nature_Bush_03c_PRE.prefab",
        };
        string[] grass =
        {
            "Nature/Grass/EA03_Plant_Grass_01c_PRE.prefab", "Nature/Grass/EA03_Plant_Grass_01d_PRE.prefab",
            "Nature/Grass/EA03_Plant_Grass_01e_PRE.prefab", "Nature/Grass/EA03_Plant_Grass_02a_PRE.prefab",
        };
        string[] rocks =
        {
            "Environment/Rock/EA03_Environment_Rock_BIG_Head_01b_PRE.prefab",
            "Environment/Rock/EA03_Environment_Rock_Big_Head_01a_PRE.prefab",
            "Environment/Rock/EA03_Environment_Rock_Mini_Head_01a_PRE.prefab",
        };

        Transform tg = Group(root, "Trees");
        Transform bg = Group(root, "Bushes");
        Transform gg = Group(root, "Grass");
        Transform rg = Group(root, "Rocks");
        int placed = 0;

        // Dis halka (yaricap ~54-58) + seyrek ic halka (~44-50)
        for (float a = 0f; a < 360f; a += 8f)
        {
            float r = 54f + Rand(0f, 4f);
            placed += TryPlace(tg, trees, Polar(a + Rand(-3f, 3f), r), 3f, 0.75f, 1.05f, true);
        }
        for (float a = 0f; a < 360f; a += 14f)
        {
            if (rng.NextDouble() < 0.4) continue;
            placed += TryPlace(tg, trees, Polar(a + Rand(-5f, 5f), 44f + Rand(0f, 6f)), 3.5f, 0.7f, 1f, true);
        }
        for (int i = 0; i < 70; i++)
            placed += TryPlace(bg, bushes, Polar(Rand(0f, 360f), Rand(24f, 58f)), 1.5f, 0.45f, 0.75f, false);
        for (int i = 0; i < 60; i++) // terrain otu da var
            placed += TryPlace(gg, grass, new Vector2(Rand(-58f, 58f), Rand(-58f, 58f)), 0.6f, 1f, 1.6f, false);
        for (int i = 0; i < 25; i++)
            placed += TryPlace(rg, rocks, Polar(Rand(0f, 360f), Rand(24f, 57f)), 1f, 0.8f, 2f, false);

        Debug.Log($"[TownBlockout] Doga: {placed} obje.");
    }

    static float Rand(float min, float max) => min + (float)rng.NextDouble() * (max - min);
    static Vector2 Polar(float deg, float r) => new Vector2(Mathf.Sin(deg * Mathf.Deg2Rad) * r, Mathf.Cos(deg * Mathf.Deg2Rad) * r);

    // Bos ise rastgele bir model koyar; agaclar kendi yerini de ayirir (ust uste binmesin)
    static int TryPlace(Transform parent, string[] options, Vector2 p, float radius, float minScale, float maxScale, bool reserve)
    {
        if (!Free(p, radius)) return 0;
        string rel = options[rng.Next(options.Length)];
        GameObject go = PlaceArt(parent, rel, new Vector3(p.x, 0f, p.y), Rand(0f, 360f), Rand(minScale, maxScale));
        if (go == null) return 0;
        if (reserve) reserved.Add(Rect.MinMaxRect(p.x - radius, p.y - radius, p.x + radius, p.y + radius));
        return 1;
    }

    static bool Free(Vector2 p, float radius)
    {
        if (Mathf.Abs(p.x) > 59f || Mathf.Abs(p.y) > 59f) return false;
        foreach (Rect r in reserved)
            if (p.x > r.xMin - radius && p.x < r.xMax + radius && p.y > r.yMin - radius && p.y < r.yMax + radius)
                return false;
        return true;
    }

    static void Reserve(Bounds b, float margin) =>
        reserved.Add(Rect.MinMaxRect(b.min.x - margin, b.min.z - margin, b.max.x + margin, b.max.z + margin));

    // Doga konmayacak sabit alanlar: meydan, dukkan (+genisleme), yollar, kapilar
    static void ReserveBaseAreas()
    {
        reserved.Add(Rect.MinMaxRect(-21f, -21f, 21f, 21f));       // meydan
        reserved.Add(Rect.MinMaxRect(-17.5f, 19f, 20.5f, 38.5f));  // dukkan + genisleme
        reserved.Add(Rect.MinMaxRect(19f, -4f, 51f, 4f));          // dogu yolu
        reserved.Add(Rect.MinMaxRect(-4f, -60f, 4f, -19f));        // guney yolu
        reserved.Add(Rect.MinMaxRect(-27f, -3f, -19f, 3f));        // demirci yolu
        reserved.Add(Rect.MinMaxRect(-27f, -23.5f, -19f, -18.5f)); // simyaci yolu
        reserved.Add(Rect.MinMaxRect(19f, 9f, 26f, 15f));          // lonca yolu
        reserved.Add(Rect.MinMaxRect(10f, -26f, 16f, -19f));       // tefeci yolu
        reserved.Add(Rect.MinMaxRect(45f, -9f, 60f, 9f));          // zindan kapisi
        reserved.Add(Rect.MinMaxRect(-7f, -60f, 7f, -53f));        // kasaba girisi
    }

    // Paket prefab'ini (baglantisi korunarak) koyar: alt yuzu groundCenter.y'ye, XZ merkezi groundCenter'a
    static GameObject PlaceArt(Transform parent, string rel, Vector3 groundCenter, float yaw, float scale = 1f)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + rel);
        if (prefab == null)
        {
            Debug.LogWarning("[TownBlockout] Model bulunamadi: " + rel);
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale = prefab.transform.localScale * scale;
        Bounds b = WorldBounds(go);
        go.transform.position = new Vector3(groundCenter.x - b.center.x, groundCenter.y - b.min.y, groundCenter.z - b.center.z);
        LodTuner.Apply(go); // paketin agresif LOD gizlemesini gevset
        return go;
    }

    static Bounds WorldBounds(GameObject go)
    {
        bool any = false;
        var b = new Bounds(go.transform.position, Vector3.zero);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    // Dogus noktalari: dukkanin onunde, dukkana bakar
    static void MoveSpawnPoints(Scene scene)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name != "SpawnPoints") continue;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                Transform sp = go.transform.GetChild(i);
                sp.SetPositionAndRotation(new Vector3(-3f + i * 2f, 0.1f, 16f), Quaternion.identity);
            }
            return;
        }
        Debug.LogWarning("[TownBlockout] SpawnPoints bulunamadi; dogus noktalari tasinmadi.");
    }

    // ================= Yapi taslari =================

    static Transform Group(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static GameObject BoxMinMax(Transform parent, string name, Vector3 min, Vector3 max, Material mat, bool collider = true) =>
        Box(parent, name, (min + max) * 0.5f, max - min, mat, collider);

    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat, bool collider = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        if (mat.renderQueue >= (int)RenderQueue.Transparent) mr.shadowCastingMode = ShadowCastingMode.Off;
        if (collider) go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    // Eksen hizali duvar; aciklik (kapi/pencere) merkezleri DUNYA koordinatinda (duvar ekseni boyunca)
    static void Wall(Transform parent, string name, Vector3 from, Vector3 to, float height, Material mat, params Opening[] openings)
    {
        Transform g = Group(parent, name);
        bool alongX = Mathf.Abs(to.x - from.x) > Mathf.Abs(to.z - from.z);
        float a0 = alongX ? Mathf.Min(from.x, to.x) : Mathf.Min(from.z, to.z);
        float a1 = alongX ? Mathf.Max(from.x, to.x) : Mathf.Max(from.z, to.z);
        float fixedCoord = alongX ? from.z : from.x;
        float baseY = from.y;
        int n = 0;

        void Piece(float s, float e, float y0, float y1)
        {
            if (e - s < 0.01f || y1 - y0 < 0.01f) return;
            float mid = (s + e) * 0.5f;
            Vector3 center = alongX
                ? new Vector3(mid, baseY + (y0 + y1) * 0.5f, fixedCoord)
                : new Vector3(fixedCoord, baseY + (y0 + y1) * 0.5f, mid);
            Vector3 size = alongX ? new Vector3(e - s, y1 - y0, WallT) : new Vector3(WallT, y1 - y0, e - s);
            Box(g, $"{name} {++n}", center, size, mat);
        }

        var ops = new List<Opening>(openings);
        ops.Sort((p, q) => p.center.CompareTo(q.center));
        float cursor = a0;
        foreach (Opening o in ops)
        {
            float s = o.center - o.width * 0.5f;
            float e = o.center + o.width * 0.5f;
            Piece(cursor, s, 0f, height);
            Piece(s, e, 0f, o.bottom);      // pencere alti
            Piece(s, e, o.top, height);     // kapi/pencere ustu
            cursor = e;
        }
        Piece(cursor, a1, 0f, height);
    }

    // Raf: taban + seviye tahtalari + yan panolar + arka pano (esya yerlestirme olcusu icin)
    static void Shelf(Transform parent, string name, Vector3 baseCenter, float length, float depth, float height, int levels, float yaw)
    {
        Transform g = Group(parent, name);
        g.position = baseCenter;
        g.rotation = Quaternion.Euler(0f, yaw, 0f);
        const float t = 0.05f;
        float half = length * 0.5f;

        void Part(string n, Vector3 localCenter, Vector3 size)
        {
            GameObject p = Box(g, n, Vector3.zero, size, mWood);
            p.transform.localPosition = localCenter;
            p.transform.localRotation = Quaternion.identity;
        }

        Part("Side L", new Vector3(-half + t * 0.5f, height * 0.5f, 0f), new Vector3(t, height, depth));
        Part("Side R", new Vector3(half - t * 0.5f, height * 0.5f, 0f), new Vector3(t, height, depth));
        Part("Back", new Vector3(0f, height * 0.5f, depth * 0.5f - t * 0.5f), new Vector3(length, height, t));
        for (int i = 0; i < levels; i++)
        {
            float y = 0.1f + i * (height - 0.15f) / (levels - 1);
            Part("Level " + (i + 1), new Vector3(0f, y, 0f), new Vector3(length, t, depth));

            // Esya yuvalari: her 0.6 m de bir, tahtanin ustunde, biraz onde
            int count = Mathf.Max(1, Mathf.FloorToInt((length - 0.2f) / 0.6f));
            float step = (length - 0.2f) / count;
            for (int k = 0; k < count; k++)
            {
                Vector3 local = new Vector3(-half + 0.1f + step * (k + 0.5f), y + t * 0.5f, -0.05f);
                AddSlot(g, $"{name}/L{i + 1}/{k + 1}", g.TransformPoint(local), g.rotation, ItemSize.Small);
            }
        }
    }

    // Zemin isareti: seffaf ince kutu + yatay yazi (yukaridan okunur)
    static void Zone(Transform parent, string name, Vector2 minXZ, Vector2 maxXZ, Material mat, string text)
    {
        var min = new Vector3(minXZ.x, 0.09f, minXZ.y);
        var max = new Vector3(maxXZ.x, 0.11f, maxXZ.y);
        BoxMinMax(parent, "Zone " + name, min, max, mat, false);
        Vector3 c = (min + max) * 0.5f;
        float w = maxXZ.x - minXZ.x;
        Label(parent, text, new Vector3(c.x, 0.13f, c.z), 0f, Mathf.Clamp(w * 1.2f, 2.5f, 6f), Color.black, true);
    }

    // 3B yazi. yaw: okuyanin baktigi yon (0 = kuzeye bakan okur, yani yazi guneye donuk)
    static void Label(Transform parent, string text, Vector3 pos, float yaw, float size, Color color, bool flat = false)
    {
        var go = new GameObject("Label " + text.Replace('\n', ' '), typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, flat ? Quaternion.Euler(90f, yaw, 0f) : Quaternion.Euler(0f, yaw, 0f));
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(12f, 3f);
    }

    static void PointLight(Transform parent, Vector3 pos)
    {
        var go = new GameObject("Point Light");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 10f;
        l.intensity = 2.5f;
        l.color = new Color(1f, 0.92f, 0.8f);
        l.shadows = LightShadows.None;
    }

    static void InvisibleWall(Transform parent, Vector3 center, Vector3 size)
    {
        var go = new GameObject("Invisible Wall");
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.AddComponent<BoxCollider>().size = size;
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    // ================= Varliklar: doku, materyal, mesh =================

    static void PrepareAssets()
    {
        EnsureFolder(Dir);
        EnsureFolder(MatDir);

        // Eski mesh'ler: kok silindigi icin kullanilmiyor, bastan uret
        meshCache.Clear();
        meshAssetCreated = false;
        if (File.Exists(MeshAssetPath)) AssetDatabase.DeleteAsset(MeshAssetPath);

        grid = GridTexture();

        mGrass = Mat("Grass", new Color(0.46f, 0.62f, 0.36f));
        mPlaza = Mat("Plaza", new Color(0.74f, 0.73f, 0.7f));
        mRoad = Mat("Road", new Color(0.62f, 0.56f, 0.47f));
        mWall = Mat("Wall", new Color(0.86f, 0.83f, 0.76f));
        mShopWall = Mat("Shop Wall", new Color(0.97f, 0.86f, 0.62f));
        mCeiling = Mat("Ceiling", new Color(0.9f, 0.88f, 0.84f));
        mRoof = Mat("Roof", new Color(0.62f, 0.3f, 0.25f));
        mWood = Mat("Wood", new Color(0.62f, 0.46f, 0.3f));
        mShopFloor = Mat("Shop Floor", new Color(0.78f, 0.66f, 0.5f));
        mBreakable = Mat("Breakable Wall", new Color(1f, 0.58f, 0.18f));
        mDark = Mat("Dark", new Color(0.25f, 0.24f, 0.26f));
        mStone = Mat("Stone", new Color(0.6f, 0.6f, 0.62f));
        mGate = Mat("Dungeon Gate", new Color(0.36f, 0.27f, 0.46f));
        mFoliage = Mat("Foliage", new Color(0.3f, 0.52f, 0.28f));
        mTrunk = Mat("Trunk", new Color(0.42f, 0.3f, 0.2f));
        mLamp = Mat("Lamp", new Color(1f, 0.9f, 0.55f));
        mWater = Mat("Water", new Color(0.3f, 0.55f, 0.85f, 0.7f), true);
        mGlass = Mat("Glass", new Color(0.7f, 0.9f, 1f, 0.25f), true);

        zCounter = Mat("Zone Counter", new Color(0.2f, 0.5f, 1f, 0.35f), true);
        zDisplay = Mat("Zone Display", new Color(0.2f, 0.85f, 0.4f, 0.35f), true);
        zStorage = Mat("Zone Storage", new Color(0.65f, 0.3f, 0.9f, 0.35f), true);
        zExpansion = Mat("Zone Expansion", new Color(1f, 0.85f, 0.1f, 0.35f), true);
        zSpawn = Mat("Zone Spawn", new Color(1f, 0.3f, 0.3f, 0.35f), true);
        zPortal = Mat("Zone Portal", new Color(0.55f, 0.2f, 1f, 0.5f), true);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // 1 UV = 1 metre: kalin cizgi metrede bir, ince cizgi yarim metrede
    static Texture2D GridTexture()
    {
        if (!File.Exists(GridPath))
        {
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    bool major = x < 2 || x >= N - 2 || y < 2 || y >= N - 2;
                    bool minor = Mathf.Abs(x - N / 2) < 1 || Mathf.Abs(y - N / 2) < 1;
                    byte v = (byte)(major ? 175 : minor ? 215 : 245);
                    px[y * N + x] = new Color32(v, v, v, 255);
                }
            tex.SetPixels32(px);
            File.WriteAllBytes(GridPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(GridPath);

            var imp = (TextureImporter)AssetImporter.GetAtPath(GridPath);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 8;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(GridPath);
    }

    // Var olan materyali gunceller (referanslar korunur), yoksa olusturur
    static Material Mat(string name, Color color, bool transparent = false)
    {
        string path = $"{MatDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }

        m.SetColor("_BaseColor", color);
        m.SetTexture("_BaseMap", transparent ? null : grid);
        m.SetFloat("_Smoothness", transparent ? 0.6f : 0.1f);

        if (transparent)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    // Dunya olcekli UV'li kutu (1 UV = 1 m, her yuzde grid ayni boyutta gorunur).
    // Ayni olculer paylasilir; hepsi tek bir .asset dosyasinda.
    static Mesh BoxMesh(Vector3 size)
    {
        string key = $"{size.x:0.###}x{size.y:0.###}x{size.z:0.###}";
        if (meshCache.TryGetValue(key, out Mesh cached)) return cached;

        Vector3 h = size * 0.5f;
        var verts = new List<Vector3>(24);
        var norms = new List<Vector3>(24);
        var uvs = new List<Vector2>(24);
        var tris = new List<int>(36);

        void Face(Vector3 n, Vector3 v)
        {
            // Disaridan bakan icin u saga, v yukari → saat yonu (Unity on yuz)
            Vector3 u = Vector3.Cross(v, -n);
            float hu = Mathf.Abs(Vector3.Dot(h, u));
            float hv = Mathf.Abs(Vector3.Dot(h, v));
            Vector3 c = Vector3.Scale(n, h);
            int i0 = verts.Count;
            Vector3[] p =
            {
                c - u * hu - v * hv, c - u * hu + v * hv, c + u * hu + v * hv, c + u * hu - v * hv,
            };
            foreach (Vector3 q in p)
            {
                verts.Add(q);
                norms.Add(n);
                uvs.Add(new Vector2(Vector3.Dot(q, u), Vector3.Dot(q, v)));
            }
            tris.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
        }

        Face(Vector3.right, Vector3.up);
        Face(Vector3.left, Vector3.up);
        Face(Vector3.forward, Vector3.up);
        Face(Vector3.back, Vector3.up);
        Face(Vector3.up, Vector3.forward);
        Face(Vector3.down, Vector3.forward);

        var mesh = new Mesh { name = "Box " + key };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        if (!meshAssetCreated)
        {
            AssetDatabase.CreateAsset(mesh, MeshAssetPath);
            meshAssetCreated = true;
        }
        else
        {
            AssetDatabase.AddObjectToAsset(mesh, MeshAssetPath);
        }
        meshCache[key] = mesh;
        return mesh;
    }
}
