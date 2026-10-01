using System;
using System.IO;
using Mirror;
using UnityEngine;

// Dunya durumu (herkes icin ortak): kasa (ortak para) + dukkan seviyesi.
// Sunucu Game sahnesi yuklenince Resources/Network/GameState prefab'ini dogurur (MarketNetworkManager).
// Kayit HOST'ta: persistentDataPath/world.json (editorde world_editor.json). Karakterler ayri (CharacterSave).
public class GameState : NetworkBehaviour
{
    public static GameState Instance { get; private set; }

    public const int StartGold = 100;
    public const int MaxShopLevel = 5;
    // index = mevcut seviye: o seviyeden bir sonrakine gecis maliyeti
    static readonly int[] ShopUpgradeCost = { 0, 500, 1500, 4000, 10000 };

    [SyncVar] public int gold = StartGold;
    [SyncVar] public int shopLevel = 1;
    [Tooltip("Dukkan unu (0..100): musteri sikligi ve tipi. Ucuz satis artirir, pahali/beklenen musteri dusurur.")]
    [SyncVar] public float reputation = StartReputation;
    [SyncVar] public int salesCount;         // toplam satis (istatistik)
    [SyncVar] public int lastSale;           // son satis tutari (HUD)

    public const float StartReputation = 50f;

    // Dukkan seviyesi satis fiyatlarini carpar (Sv1 x1.00 → Sv5 x2.00)
    public float PriceMultiplier => 1f + 0.25f * (Mathf.Clamp(shopLevel, 1, MaxShopLevel) - 1);
    public int NextUpgradeCost => shopLevel < MaxShopLevel ? ShopUpgradeCost[shopLevel] : 0;

    public static float CurrentPriceMultiplier => Instance != null ? Instance.PriceMultiplier : 1f;

    bool dirty;
    float nextSave;

    public override void OnStartServer()
    {
        Instance = this;
        Load();
        SpawnTrainingDummies();
    }

    // Sahnedeki TrainingDummyPoint'lere egitim kuklasi dogur (Resources/Network/TrainingDummy)
    [Server]
    void SpawnTrainingDummies()
    {
        GameObject prefab = Resources.Load<GameObject>("Network/TrainingDummy");
        if (prefab == null) return;
        foreach (TrainingDummyPoint p in FindObjectsByType<TrainingDummyPoint>(FindObjectsSortMode.None))
            NetworkServer.Spawn(Instantiate(prefab, p.transform.position, p.transform.rotation));
    }

    public override void OnStartClient() => Instance = this;

    public override void OnStopServer()
    {
        Save();
        if (Instance == this) Instance = null;
    }

    public override void OnStopClient()
    {
        if (Instance == this) Instance = null;
    }

    // ---------------- Sunucu ----------------

    [Server]
    public void AddGold(int amount)
    {
        if (amount <= 0) return;
        gold += amount;
        dirty = true;
    }

    [Server]
    public bool TrySpend(int amount)
    {
        if (amount < 0 || gold < amount) return false;
        gold -= amount;
        dirty = true;
        return true;
    }

    [Server]
    public void AddReputation(float delta)
    {
        if (Mathf.Approximately(delta, 0f)) return;
        reputation = Mathf.Clamp(reputation + delta, 0f, 100f);
        dirty = true;
    }

    [Server]
    public void RecordSale(int amount)
    {
        salesCount++;
        lastSale = amount;
        dirty = true;
    }

    [Server]
    public bool TryUpgradeShop()
    {
        if (shopLevel >= MaxShopLevel || !TrySpend(NextUpgradeCost)) return false;
        shopLevel++;
        dirty = true;
        Debug.Log($"[GameState] Dukkan seviye {shopLevel} (fiyat x{PriceMultiplier:0.00})");
        return true;
    }

    void Update()
    {
        if (!isServer) return;
        UpdateCustomerSpawning();
        if (!dirty || Time.unscaledTime < nextSave) return;
        nextSave = Time.unscaledTime + 2f;
        Save();
    }

    // ---------------- Musteriler (sunucu) ----------------

    float nextCustomer = 5f;

    // Raflarda urun varken, une ve dukkan seviyesine gore araliklarla musteri dogurur
    void UpdateCustomerSpawning()
    {
        if (Time.time < nextCustomer) return;
        nextCustomer = Time.time + PricingRules.SpawnInterval(reputation, shopLevel) * UnityEngine.Random.Range(0.8f, 1.2f);

        ShopLayout layout = ShopLayout.Instance;
        if (layout == null || CustomerAI.All.Count >= PricingRules.MaxCustomers(shopLevel)) return;
        bool anyOnShelf = false;
        foreach (WorldItem it in WorldItem.All)
            if (it.OnSlot) { anyOnShelf = true; break; }
        if (!anyOnShelf) return;

        GameObject prefab = Resources.Load<GameObject>("Network/Customer");
        if (prefab == null) { Debug.LogWarning("[GameState] Resources/Network/Customer yok."); return; }
        Vector3 p = layout.SpawnPoint + new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 0f, UnityEngine.Random.Range(-0.5f, 0.5f));
        GameObject go = Instantiate(prefab, p, Quaternion.LookRotation(Vector3.forward));
        go.GetComponent<CustomerAI>().type = PricingRules.RollType(reputation);
        NetworkServer.Spawn(go);
    }

    // ---------------- Kayit (host) ----------------

    [Serializable]
    class WorldFile
    {
        public int version = 1;
        public int gold = StartGold;
        public int shopLevel = 1;
        public float reputation = StartReputation;
        public int salesCount;
    }

    static string SavePath =>
        Path.Combine(Application.persistentDataPath, Application.isEditor ? "world_editor.json" : "world.json");

    [Server]
    void Load()
    {
        try
        {
            if (!File.Exists(SavePath)) return;
            WorldFile w = JsonUtility.FromJson<WorldFile>(File.ReadAllText(SavePath));
            if (w == null) return;
            gold = Mathf.Max(0, w.gold);
            shopLevel = Mathf.Clamp(w.shopLevel, 1, MaxShopLevel);
            reputation = Mathf.Clamp(w.reputation, 0f, 100f);
            salesCount = Mathf.Max(0, w.salesCount);
            Debug.Log($"[GameState] Dunya yuklendi: {gold} altin, dukkan Sv.{shopLevel}, un {reputation:0}");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GameState] Dunya kaydi okunamadi: " + e.Message);
        }
    }

    [Server]
    void Save()
    {
        dirty = false;
        try
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(new WorldFile { gold = gold, shopLevel = shopLevel, reputation = reputation, salesCount = salesCount }, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GameState] Dunya kaydedilemedi: " + e.Message);
        }
    }
}
