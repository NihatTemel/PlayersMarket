using System;
using System.Collections;
using Mirror;
using Steamworks;
using UnityEngine;

// Ag yoneticisi: Steam (FizzySteamworks) ve yerel (Telepathy) host/join.
// BrawAnimals SteamLobbyManager'dan uyarlandi. Farki: arayuz referansi TUTMAZ.
// Menu sahnesi (MainMenuUI) buradaki metodlari cagirir, durumu StatusChanged ile dinler.
// Boylece DDOL yonetici menuye geri donuldugunde yok olmus butonlara bagli kalmaz.
public class MarketNetworkManager : NetworkManager
{
    // Gelistirme icin Steam test uygulamasi (Spacewar). Kendi App ID alininca degistir
    // (steam_appid.txt de ayni olmali).
    public const uint SteamAppId = 480;

    // 480 baska oyunlarla paylasiliyor: lobileri bu etiketle ayiriyoruz
    const string KeyGame = "game";
    const string GameTag = "PlayersMarket";
    const string KeyHostAddress = "HostAddress";
    const string KeyRoomCode = "RoomCode";
    const string KeyVersion = "GameVersion";

    [Header("Transportlar")]
    public Transport steamTransport;
    public Transport localTransport;

    [Header("Lobi")]
    public int maxLobbyMembers = 4;
    public string localAddress = "localhost";

    // Unity "sahte null" icin acik kontrol: yok edilmis yonetici null doner
    public static MarketNetworkManager Instance
    {
        get
        {
            MarketNetworkManager m = singleton as MarketNetworkManager;
            return m != null ? m : null;
        }
    }

    // Menu/HUD bu olayi dinler; sonradan acilan arayuz LastStatus'u gosterir
    public static event Action<string> StatusChanged;
    public static string LastStatus { get; private set; } = "";

    public CSteamID CurrentLobby { get; private set; } = CSteamID.Nil;
    public string RoomCode { get; private set; } = "";
    public bool UsingSteam { get; private set; }
    public bool SteamBusy { get; private set; }

    public bool SteamReady => steamInitOk && SteamManager.Initialized;

    bool steamInitOk;
    CSteamID expectedLobby = CSteamID.Nil;
    bool joiningByCode;

    Callback<LobbyCreated_t> lobbyCreated;
    Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;
    Callback<LobbyEnter_t> lobbyEntered;
    Callback<LobbyMatchList_t> lobbyMatchList;

    public override void Awake()
    {
        // Menuye geri donuldugunde sahnedeki ikinci kopya: uyari basmadan sil
        if (singleton != null && singleton != this)
        {
            enabled = false;
            Destroy(gameObject);
            return;
        }

        // Build'de steam_appid.txt olmasa da dogru App ID ile baslasin
        Environment.SetEnvironmentVariable("SteamAppId", SteamAppId.ToString());
        Environment.SetEnvironmentVariable("SteamGameId", SteamAppId.ToString());

        base.Awake();

        // Resources/Network altindaki ag prefab'lari (WorldItem vb.) otomatik kayit:
        // yeni ag objesi icin MainMenu sahnesini duzenlemek gerekmesin
        foreach (NetworkIdentity ni in Resources.LoadAll<NetworkIdentity>("Network"))
            if (!spawnPrefabs.Contains(ni.gameObject)) spawnPrefabs.Add(ni.gameObject);

        steamInitOk = SteamManager.Initialized;
        if (!steamInitOk)
        {
            Debug.LogWarning("[Network] Steam baslatilamadi (Steam acik mi?). Sadece yerel host/join calisir.");
            SetStatus("Steam kapalı: sadece yerel oyun");
            return;
        }

        lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequested);
        lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
        lobbyMatchList = Callback<LobbyMatchList_t>.Create(OnLobbyMatchList);
    }

    public override void Start()
    {
        base.Start();
        if (singleton != this) return;
        TryJoinFromCommandLine();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (singleton != this) return;
        LeaveSteamLobby();
    }

    // ---------------- Steam ----------------

    public void HostSteam()
    {
        if (!CanStart()) return;
        if (!SteamReady) { SetStatus("Steam bağlı değil."); return; }

        UsingSteam = true;
        UseTransport(steamTransport);
        RoomCode = UnityEngine.Random.Range(10000, 99999).ToString();
        SteamBusy = true;
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, maxLobbyMembers);
        SetStatus("Lobi oluşturuluyor...");
    }

    public void JoinSteamByCode(string code)
    {
        if (!CanStart()) return;
        if (!SteamReady) { SetStatus("Steam bağlı değil."); return; }

        code = (code ?? "").Trim();
        if (code.Length == 0) { SetStatus("Oda kodu gir."); return; }

        LeaveSteamLobby();
        joiningByCode = true;
        SteamBusy = true;
        SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        SteamMatchmaking.AddRequestLobbyListStringFilter(KeyGame, GameTag, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter(KeyRoomCode, code, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.RequestLobbyList();
        SetStatus("Oda aranıyor...");
    }

    // Steam arayuzunden arkadas davet penceresi
    public void InviteFriends()
    {
        if (SteamReady && CurrentLobby.IsValid())
            SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobby);
    }

    void OnLobbyCreated(LobbyCreated_t callback)
    {
        if (this == null) return;
        SteamBusy = false;

        if (callback.m_eResult != EResult.k_EResultOK)
        {
            SetStatus("Lobi oluşturulamadı: " + callback.m_eResult);
            return;
        }

        CurrentLobby = new CSteamID(callback.m_ulSteamIDLobby);
        expectedLobby = CurrentLobby;

        SteamMatchmaking.SetLobbyData(CurrentLobby, KeyGame, GameTag);
        SteamMatchmaking.SetLobbyData(CurrentLobby, KeyHostAddress, SteamUser.GetSteamID().ToString());
        SteamMatchmaking.SetLobbyData(CurrentLobby, KeyRoomCode, RoomCode);
        SteamMatchmaking.SetLobbyData(CurrentLobby, KeyVersion, Application.version);

        StartHost();
        SetStatus("Host başladı. Oda kodu: " + RoomCode);
        Debug.Log($"[Network] Steam lobisi kuruldu: {CurrentLobby} kod={RoomCode}");
    }

    void OnLobbyMatchList(LobbyMatchList_t result)
    {
        if (this == null || !joiningByCode) return;
        joiningByCode = false;

        if (result.m_nLobbiesMatching == 0)
        {
            SteamBusy = false;
            SetStatus("Oda bulunamadı.");
            return;
        }

        CSteamID lobby = SteamMatchmaking.GetLobbyByIndex(0);
        expectedLobby = lobby;
        SteamMatchmaking.JoinLobby(lobby);
        SetStatus("Oda bulundu, katılınıyor...");
    }

    // Steam arkadas listesinden "Oyuna katil" / davet kabul
    void OnLobbyJoinRequested(GameLobbyJoinRequested_t callback)
    {
        if (this == null) return;
        JoinLobbyById(callback.m_steamIDLobby);
    }

    void JoinLobbyById(CSteamID lobby)
    {
        // Baska bir oyundaysak once cik (offline sahneye doner, yonetici DDOL kalir)
        if (isNetworkActive) LeaveGame();
        LeaveSteamLobby();

        UsingSteam = true;
        SteamBusy = true;
        expectedLobby = lobby;
        SteamMatchmaking.JoinLobby(lobby);
        SetStatus("Davet alındı, katılınıyor...");
    }

    void OnLobbyEntered(LobbyEnter_t callback)
    {
        if (this == null) return;
        CSteamID lobby = new CSteamID(callback.m_ulSteamIDLobby);

        if (callback.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            SteamBusy = false;
            SetStatus("Lobiye girilemedi (dolu ya da kapalı olabilir).");
            return;
        }

        bool amOwner = SteamMatchmaking.GetLobbyOwner(lobby) == SteamUser.GetSteamID();

        // Bizim istemedigimiz bir lobiye giris: hemen cik
        if (!amOwner && lobby != expectedLobby)
        {
            Debug.LogWarning($"[Network] Beklenmeyen lobi girisi engellendi: {lobby} (beklenen {expectedLobby})");
            SteamMatchmaking.LeaveLobby(lobby);
            return;
        }

        CurrentLobby = lobby;
        expectedLobby = CSteamID.Nil;
        RoomCode = SteamMatchmaking.GetLobbyData(lobby, KeyRoomCode);

        // Host OnLobbyCreated'da zaten StartHost yapti
        if (amOwner) return;

        string hostVersion = SteamMatchmaking.GetLobbyData(lobby, KeyVersion);
        if (!string.IsNullOrEmpty(hostVersion) && hostVersion != Application.version)
        {
            SteamBusy = false;
            LeaveSteamLobby();
            SetStatus($"Sürüm farklı (host {hostVersion}, sen {Application.version}).");
            return;
        }

        StartCoroutine(ConnectToSteamHost());
    }

    IEnumerator ConnectToSteamHost()
    {
        // BrawAnimals'ta oldugu gibi kisa bekleme: lobi verisi/host hazir olsun
        yield return new WaitForSeconds(1f);
        SteamBusy = false;
        if (!CurrentLobby.IsValid() || isNetworkActive) yield break;

        string hostAddress = SteamMatchmaking.GetLobbyData(CurrentLobby, KeyHostAddress);
        if (string.IsNullOrEmpty(hostAddress))
            hostAddress = SteamMatchmaking.GetLobbyOwner(CurrentLobby).ToString();

        UsingSteam = true;
        UseTransport(steamTransport);
        networkAddress = hostAddress;
        StartClient();
        SetStatus("Sunucuya bağlanılıyor...");
    }

    // Oyun kapaliyken davet kabul edilirse Steam oyunu "+connect_lobby <id>" ile acar
    void TryJoinFromCommandLine()
    {
        if (!SteamReady) return;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong id))
            {
                JoinLobbyById(new CSteamID(id));
                return;
            }
        }
    }

    void LeaveSteamLobby()
    {
        if (steamInitOk && CurrentLobby.IsValid())
        {
            SteamMatchmaking.LeaveLobby(CurrentLobby);
            Debug.Log($"[Network] Steam lobisinden cikildi: {CurrentLobby}");
        }
        CurrentLobby = CSteamID.Nil;
        RoomCode = "";
    }

    // ---------------- Yerel (test) ----------------

    public void HostLocal()
    {
        if (!CanStart()) return;
        UsingSteam = false;
        UseTransport(localTransport);
        networkAddress = localAddress;
        StartHost();
        SetStatus("Yerel host başladı.");
    }

    // address bos ise localhost (ayni bilgisayarda ikinci kopya); LAN icin IP yazilabilir
    public void JoinLocal(string address)
    {
        if (!CanStart()) return;
        UsingSteam = false;
        UseTransport(localTransport);
        networkAddress = string.IsNullOrWhiteSpace(address) ? localAddress : address.Trim();
        StartClient();
        SetStatus($"Yerel sunucuya bağlanılıyor ({networkAddress})...");
    }

    // ---------------- Ortak ----------------

    public void LeaveGame()
    {
        if (NetworkServer.active && NetworkClient.isConnected) StopHost();
        else if (NetworkClient.active) StopClient();
        else if (NetworkServer.active) StopServer();
    }

    bool CanStart()
    {
        if (isNetworkActive) { SetStatus("Zaten bir oyundasın."); return false; }
        if (SteamBusy) { SetStatus("Lütfen bekle..."); return false; }
        return true;
    }

    void UseTransport(Transport t)
    {
        transport = t;
        Transport.active = t;
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        SetStatus("Bağlandı.");
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        SetStatus("Bağlantı kesildi.");
    }

    // Oyun sahnesi yuklenince ortak dunya durumunu (kasa, dukkan seviyesi) dogur
    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);
        if (sceneName != onlineScene || GameState.Instance != null) return;
        GameObject prefab = Resources.Load<GameObject>("Network/GameState");
        if (prefab == null)
        {
            Debug.LogError("[Network] Resources/Network/GameState yok. Menu: PlayersMarket > Esya Verilerini Guncelle");
            return;
        }
        NetworkServer.Spawn(Instantiate(prefab));
    }

    // Host icin de cagrilir (StopHost once istemciyi durdurur)
    public override void OnStopClient()
    {
        base.OnStopClient();
        SteamBusy = false;
        LeaveSteamLobby();
    }

    static void SetStatus(string msg)
    {
        LastStatus = msg;
        StatusChanged?.Invoke(msg);
    }
}
