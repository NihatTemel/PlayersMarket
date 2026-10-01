using System.IO;
using Mirror;
using Mirror.FizzySteam;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Ag kurulumunu koddan uretir: Player prefab'i, MainMenu ve Game sahneleri, Build Settings.
// MainMenu sahnesi yoksa proje acilisinda BIR KEZ kendiliginden calisir.
// Elle: ust menu "PlayersMarket > Ag Kurulumunu Yeniden Olustur" (mevcutlari EZER).
// Sahneleri sonra elle duzenleyeceksen bu builder'i tekrar calistirma.
[InitializeOnLoad]
public static class NetworkSetupBuilder
{
    const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
    const string GameScenePath = "Assets/Scenes/Game.unity";
    const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
    // Kullanici vazgecerse ya da TMP alinamazsa bu oturumda tekrar otomatik deneme
    const string DeclinedKey = "PlayersMarket.NetworkSetupDeclined";

    static NetworkSetupBuilder()
    {
        // Kurulum varsa sadece eski Player prefab'ina eksik bilesenleri ekle (sahnelere dokunmaz)
        if (File.Exists(MenuScenePath))
        {
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) UpgradePlayerPrefab();
            };
            return;
        }
        if (SessionState.GetBool(DeclinedKey, false)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(MenuScenePath)) return;
            Build();
        };
    }

    [MenuItem("PlayersMarket/Player Prefab'ini Guncelle")]
    static void UpgradePlayerPrefabFromMenu() => UpgradePlayerPrefab();

    [MenuItem("PlayersMarket/Ag Kurulumunu Yeniden Olustur")]
    static void BuildFromMenu()
    {
        if (!EditorUtility.DisplayDialog("PlayersMarket",
                "MainMenu ve Game sahneleri ile Player prefab'i yeniden olusturulacak.\nMevcut olanlar EZILIR. Devam?",
                "Olustur", "Vazgec"))
            return;
        Build();
    }

    static void Build()
    {
        // TMP Essentials yoksa once onu iceri al, bitince tekrar gel
        if (!File.Exists(TmpSettingsPath))
        {
            Debug.Log("[NetworkSetup] TMP Essential Resources iceri aliniyor...");
            AssetDatabase.importPackageCompleted += OnTmpImported;
            AssetDatabase.importPackageFailed += OnTmpFailed;
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            SessionState.SetBool(DeclinedKey, true);
            Debug.Log("[NetworkSetup] Iptal edildi. Menu: PlayersMarket > Ag Kurulumunu Yeniden Olustur");
            return;
        }

        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Assets/Prefabs");

        GameObject playerPrefab = BuildGameScene();
        BuildMenuScene(playerPrefab);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(MenuScenePath, true),
            new EditorBuildSettingsScene(GameScenePath, true),
        };
        AssetDatabase.SaveAssets();
        Debug.Log("[NetworkSetup] Hazir: MainMenu + Game sahneleri, Player prefab'i, Build Settings.");
    }

    static void OnTmpImported(string packageName)
    {
        UnsubscribeTmp();
        EditorApplication.delayCall += Build;
    }

    static void OnTmpFailed(string packageName, string error)
    {
        UnsubscribeTmp();
        SessionState.SetBool(DeclinedKey, true);
        Debug.LogError("[NetworkSetup] TMP Essentials alinamadi: " + error +
                       "\nWindow > TextMeshPro > Import TMP Essential Resources yapip menuden tekrar calistir.");
    }

    static void UnsubscribeTmp()
    {
        AssetDatabase.importPackageCompleted -= OnTmpImported;
        AssetDatabase.importPackageFailed -= OnTmpFailed;
    }

    // ---------------- Player ----------------

    static GameObject BuildPlayerPrefab()
    {
        var root = new GameObject("Player");

        var cc = root.AddComponent<CharacterController>();
        cc.center = new Vector3(0f, 1f, 0f);
        cc.height = 2f;
        cc.radius = 0.4f;

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        body.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
        Object.DestroyImmediate(body.GetComponent<Collider>());

        // Bakis yonu gostergesi
        GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nose.name = "Nose";
        nose.transform.SetParent(root.transform, false);
        nose.transform.localPosition = new Vector3(0f, 1.5f, 0.38f);
        nose.transform.localScale = new Vector3(0.25f, 0.12f, 0.2f);
        Object.DestroyImmediate(nose.GetComponent<Collider>());

        root.AddComponent<NetworkIdentity>();
        var nt = root.AddComponent<NetworkTransformReliable>();
        nt.target = root.transform;
        nt.syncDirection = SyncDirection.ClientToServer;

        var player = root.AddComponent<NetworkPlayer>();
        player.bodyRenderer = body.GetComponent<Renderer>();
        AddMissingPlayerComponents(root);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        Object.DestroyImmediate(root);

        // assetId build icin prefab'a yazilsin (getter bos ise SetupIDs ile atar)
        uint assetId = prefab.GetComponent<NetworkIdentity>().assetId;
        EditorUtility.SetDirty(prefab);
        AssetDatabase.SaveAssets();
        Debug.Log($"[NetworkSetup] Player prefab assetId={assetId}");
        return prefab;
    }

    // Sonradan eklenen oyuncu bilesenleri. Yeni bilesen gelince BURAYA ekle: hem yeni kurulum
    // hem de mevcut prefab'in yukseltmesi bunu kullanir. Degisiklik yaptiysa true doner.
    static bool AddMissingPlayerComponents(GameObject root)
    {
        bool changed = false;
        if (root.GetComponent<PlayerMovement>() == null)
        {
            root.AddComponent<PlayerMovement>();
            changed = true;
        }
        if (root.GetComponent<PlayerCarry>() == null)
        {
            root.AddComponent<PlayerCarry>();
            changed = true;
        }
        if (root.GetComponent<PlayerInventory>() == null)
        {
            root.AddComponent<PlayerInventory>();
            changed = true;
        }
        if (root.GetComponent<PlayerCombat>() == null)
        {
            root.AddComponent<PlayerCombat>();
            changed = true;
        }
        return changed;
    }

    // Mevcut prefab'i yerinde acip eksikleri ekler; elle yapilan degisiklikler korunur
    static void UpgradePlayerPrefab()
    {
        if (!File.Exists(PlayerPrefabPath)) return;
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            if (AddMissingPlayerComponents(root))
            {
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log("[NetworkSetup] Player prefab'i guncellendi (eksik bilesenler eklendi).");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---------------- Game sahnesi ----------------

    static GameObject BuildGameScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        GameObject playerPrefab = BuildPlayerPrefab();

        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 8f, -10f);
            cam.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
        }

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(5f, 1f, 5f);

        // Yon/olcek hissi icin birkac kutu
        var props = new GameObject("Props").transform;
        Vector3[] crates =
        {
            new Vector3(-6f, 0.5f, 6f), new Vector3(6f, 0.5f, 6f), new Vector3(0f, 0.5f, 10f),
            new Vector3(-10f, 0.5f, -4f), new Vector3(10f, 0.5f, -4f),
        };
        for (int i = 0; i < crates.Length; i++)
        {
            GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = "Crate " + (i + 1);
            c.transform.SetParent(props, false);
            c.transform.position = crates[i];
        }

        var spawns = new GameObject("SpawnPoints").transform;
        for (int i = 0; i < 4; i++)
        {
            var sp = new GameObject("Spawn " + (i + 1));
            sp.transform.SetParent(spawns, false);
            sp.transform.position = new Vector3(-3f + i * 2f, 0f, 0f);
            sp.AddComponent<NetworkStartPosition>();
        }

        // HUD
        GameObject canvas = CreateCanvas("HUD");
        var hud = canvas.AddComponent<GameHUD>();

        hud.infoText = CreateText(canvas.transform, "Info", "", 30, TextAlignmentOptions.TopLeft,
            new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(600f, 160f));
        hud.leaveButton = CreateButton(canvas.transform, "LeaveButton", "Ayrıl", 30,
            new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(220f, 70f), new Color(0.75f, 0.25f, 0.25f));
        hud.inviteButton = CreateButton(canvas.transform, "InviteButton", "Arkadaş Davet Et", 30,
            new Vector2(1f, 1f), new Vector2(-270f, -30f), new Vector2(320f, 70f), new Color(0.25f, 0.5f, 0.8f));

        CreateEventSystem();
        EditorSceneManager.SaveScene(scene, GameScenePath);
        return playerPrefab;
    }

    // ---------------- MainMenu sahnesi ----------------

    static void BuildMenuScene(GameObject playerPrefab)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.16f, 0.2f);
        }

        // NetworkManager: transportlari ONCE ekle (yonetici bos transport gorup KCP eklemesin)
        var nmGo = new GameObject("NetworkManager");
        var fizzy = nmGo.AddComponent<FizzySteamworks>();
        var telepathy = nmGo.AddComponent<TelepathyTransport>();
        var nm = nmGo.AddComponent<MarketNetworkManager>();
        nm.transport = fizzy;
        nm.steamTransport = fizzy;
        nm.localTransport = telepathy;
        nm.playerPrefab = playerPrefab;
        nm.offlineScene = MenuScenePath;
        nm.onlineScene = GameScenePath;
        nm.maxConnections = 8;
        nm.playerSpawnMethod = PlayerSpawnMethod.RoundRobin;
        nm.dontDestroyOnLoad = true;
        nm.runInBackground = true;

        // Arayuz
        GameObject canvas = CreateCanvas("MenuCanvas");
        var ui = canvas.AddComponent<MainMenuUI>();
        Transform ct = canvas.transform;
        Vector2 center = new Vector2(0.5f, 0.5f);

        CreateText(ct, "Title", "PlayersMarket", 110, TextAlignmentOptions.Center,
            center, new Vector2(0f, 380f), new Vector2(1200f, 150f));
        ui.steamText = CreateText(ct, "SteamInfo", "", 30, TextAlignmentOptions.Center,
            center, new Vector2(0f, 280f), new Vector2(1200f, 50f));

        Color green = new Color(0.25f, 0.65f, 0.4f);
        Color blue = new Color(0.25f, 0.5f, 0.8f);
        Vector2 btnSize = new Vector2(480f, 80f);
        Vector2 inputSize = new Vector2(480f, 70f);

        // Sol: Steam
        Transform steam = CreatePanel(ct, "SteamPanel", new Vector2(-300f, -40f), new Vector2(560f, 500f));
        CreateText(steam, "Header", "Steam", 44, TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(500f, 60f));
        ui.hostSteamButton = CreateButton(steam, "HostSteamButton", "Oda Kur (Host)", 34,
            new Vector2(0.5f, 1f), new Vector2(0f, -100f), btnSize, green);
        ui.roomCodeInput = CreateInput(steam, "RoomCodeInput", "Oda kodu", 34,
            new Vector2(0.5f, 1f), new Vector2(0f, -240f), inputSize);
        ui.roomCodeInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        ui.roomCodeInput.characterLimit = 5;
        ui.joinSteamButton = CreateButton(steam, "JoinSteamButton", "Kodla Katıl", 34,
            new Vector2(0.5f, 1f), new Vector2(0f, -330f), btnSize, blue);

        // Sag: Yerel
        Transform local = CreatePanel(ct, "LocalPanel", new Vector2(300f, -40f), new Vector2(560f, 500f));
        CreateText(local, "Header", "Yerel (Test)", 44, TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(500f, 60f));
        ui.hostLocalButton = CreateButton(local, "HostLocalButton", "Yerel Kur (Host)", 34,
            new Vector2(0.5f, 1f), new Vector2(0f, -100f), btnSize, green);
        ui.addressInput = CreateInput(local, "AddressInput", "IP (boş = localhost)", 34,
            new Vector2(0.5f, 1f), new Vector2(0f, -240f), inputSize);
        ui.joinLocalButton = CreateButton(local, "JoinLocalButton", "Yerel Katıl", 34,
            new Vector2(0.5f, 1f), new Vector2(0f, -330f), btnSize, blue);

        ui.statusText = CreateText(ct, "Status", "", 32, TextAlignmentOptions.Center,
            center, new Vector2(0f, -380f), new Vector2(1400f, 60f));

        CreateEventSystem();
        EditorSceneManager.SaveScene(scene, MenuScenePath);
    }

    // ---------------- UI yardimcilari ----------------

    static TMP_DefaultControls.Resources UiResources() => new TMP_DefaultControls.Resources
    {
        standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
        background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
        inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
    };

    static GameObject CreateCanvas(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        return go;
    }

    static void CreateEventSystem()
    {
        // AssignDefaultActions CAGIRMA: gecici bir asset uretir, sahne degisince yok olur ve
        // ikinci sahnede istisna atar. Modul bos kalirsa varsayilan eylemleri kendisi alir
        // (editorde Reset paket asset'ini atar, oyunda OnEnable).
        var go = new GameObject("EventSystem", typeof(EventSystem));
        go.AddComponent<InputSystemUIInputModule>();
    }

    static void Place(RectTransform rt, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.SetParent(parent, false);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static Transform CreatePanel(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var img = go.GetComponent<Image>();
        img.sprite = UiResources().background;
        img.type = Image.Type.Sliced;
        img.color = new Color(0f, 0f, 0f, 0.35f);
        var rt = (RectTransform)go.transform;
        Place(rt, parent, new Vector2(0.5f, 0.5f), pos, size);
        return go.transform;
    }

    static TMP_Text CreateText(Transform parent, string name, string text, float fontSize,
        TextAlignmentOptions align, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        GameObject go = TMP_DefaultControls.CreateText(UiResources());
        go.name = name;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        Place((RectTransform)go.transform, parent, anchor, pos, size);
        return tmp;
    }

    static Button CreateButton(Transform parent, string name, string label, float fontSize,
        Vector2 anchor, Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = TMP_DefaultControls.CreateButton(UiResources());
        go.name = name;
        go.GetComponent<Image>().color = color;
        var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        tmp.fontStyle = FontStyles.Bold;
        Place((RectTransform)go.transform, parent, anchor, pos, size);
        return go.GetComponent<Button>();
    }

    static TMP_InputField CreateInput(Transform parent, string name, string placeholder, float fontSize,
        Vector2 anchor, Vector2 pos, Vector2 size)
    {
        GameObject go = TMP_DefaultControls.CreateInputField(UiResources());
        go.name = name;
        var input = go.GetComponent<TMP_InputField>();
        input.pointSize = fontSize;
        if (input.placeholder is TMP_Text ph) ph.text = placeholder;
        input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
        if (input.placeholder is TMP_Text ph2) ph2.alignment = TextAlignmentOptions.MidlineLeft;
        Place((RectTransform)go.transform, parent, anchor, pos, size);
        return input;
    }
}
