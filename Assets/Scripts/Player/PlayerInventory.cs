using System.Collections.Generic;
using Mirror;
using UnityEngine;

// Oyuncu envanteri (24 slot) + ekipman (8 yuva) + item gucu. Sunucu yetkili: tum degisiklikler Command ile.
// Karakter (envanter+ekipman) OYUNCUNUN bilgisayarinda saklanir (CharacterSave): yerel oyuncu baglaninca
// kaydini sunucuya yollar; degistikce kendi dosyasina yazar. Ilk kez oynayana baslangic seti verilir.
// Buyuk esyalar (ItemSize.Large) envantere GIRMEZ, elde tasinir (PlayerCarry).
public class PlayerInventory : NetworkBehaviour
{
    public readonly SyncList<ItemStack> bag = new SyncList<ItemStack>();
    public readonly SyncList<ItemStack> equipment = new SyncList<ItemStack>();

    [SyncVar] public int gearScore;
    [SyncVar] public bool loaded;

    [Tooltip("Zindan dususlerini (autoPickup) kendiliginden toplama yaricapi.")]
    public float autoPickupRadius = 1.8f;

    PlayerCarry carry;
    InventoryUI ui;
    float nextAutoPickup;
    float nextSaveCheck;
    string lastSavedJson;
    readonly List<WorldItem> pickupBuffer = new List<WorldItem>();

    void Awake() => carry = GetComponent<PlayerCarry>();

    public override void OnStartServer()
    {
        for (int i = 0; i < ItemRules.BagSize; i++) bag.Add(ItemStack.Empty);
        for (int i = 0; i < ItemRules.EquipSlots.Length; i++) equipment.Add(ItemStack.Empty);
    }

    public override void OnStartLocalPlayer()
    {
        if (CharacterSave.TryLoad(out ItemStack[] savedBag, out ItemStack[] savedEquip))
            CmdLoadCharacter(savedBag, savedEquip);
        else
            CmdNewCharacter();
        ui = InventoryUI.Create(this);
    }

    public override void OnStopLocalPlayer()
    {
        SaveIfChanged();
        if (ui != null) Destroy(ui.gameObject);
    }

    void OnApplicationQuit()
    {
        if (isLocalPlayer) SaveIfChanged();
    }

    void Update()
    {
        if (isLocalPlayer && loaded && Time.unscaledTime >= nextSaveCheck)
        {
            nextSaveCheck = Time.unscaledTime + 1f;
            SaveIfChanged();
        }
        if (isServer && loaded && Time.time >= nextAutoPickup)
        {
            nextAutoPickup = Time.time + 0.15f;
            ServerAutoPickup();
        }
    }

    void SaveIfChanged()
    {
        if (!loaded || bag.Count == 0) return;
        string json = CharacterSave.ToJson(bag, equipment);
        if (json == lastSavedJson) return;
        if (CharacterSave.Write(json)) lastSavedJson = json;
    }

    // ---------------- Yukleme ----------------

    [Command]
    void CmdLoadCharacter(ItemStack[] savedBag, ItemStack[] savedEquip)
    {
        if (loaded) return;
        var overflow = new List<ItemStack>();

        for (int i = 0; i < ItemRules.EquipSlots.Length; i++)
        {
            ItemStack s = i < savedEquip.Length ? Sanitize(savedEquip[i]) : ItemStack.Empty;
            // Yanlis yuvadaki esya (veri degismis olabilir) cantaya
            if (!s.IsEmpty && s.Data.equipSlot != ItemRules.EquipSlots[i]) { overflow.Add(s); s = ItemStack.Empty; }
            equipment[i] = s;
        }
        for (int i = 0; i < ItemRules.BagSize; i++)
            bag[i] = i < savedBag.Length ? Sanitize(savedBag[i]) : ItemStack.Empty;
        foreach (ItemStack s in overflow)
        {
            ItemStack rest = s;
            TryAdd(ref rest);
        }

        loaded = true;
        RecalcGearScore();
    }

    [Command]
    void CmdNewCharacter()
    {
        if (loaded) return;
        ServerGiveStarter();
        loaded = true;
        RecalcGearScore();
        Debug.Log($"[Inventory] Yeni karakter: baslangic seti verildi (guc {gearScore}).");
    }

    // Baslangic seti: item gucu ~50 (kusanili deri/bakir set + Kisa Yay + 3 Basma Tasi)
    [Server]
    void ServerGiveStarter()
    {
        string[] starter =
        {
            "sword_rusty", "helm_leather", "armor_leather", "gloves_leather",
            "boots_leather", "necklace_copper", "earring_copper", "ring_copper",
        };
        foreach (string id in starter)
        {
            ItemStack s = Sanitize(new ItemStack(id));
            if (s.IsEmpty) continue;
            int slot = ItemRules.SlotIndex(s.Data.equipSlot);
            if (slot >= 0) equipment[slot] = s;
        }
        ItemStack bow = Sanitize(new ItemStack("bow_short"));
        TryAdd(ref bow);
        ItemStack stones = Sanitize(new ItemStack(ItemRules.UpgradeStoneId, 3));
        TryAdd(ref stones);
    }

    // Bilinmeyen id'yi at, + ve adedi sinirla
    static ItemStack Sanitize(ItemStack s)
    {
        ItemData d = s.Data;
        if (d == null || d.size == ItemSize.Large) return ItemStack.Empty;
        int plus = d.Upgradeable ? Mathf.Clamp(s.plus, 0, ItemRules.MaxPlus) : 0;
        int count = Mathf.Clamp(s.count, 1, d.StackLimit);
        return new ItemStack(d.id, count, plus);
    }

    [Server]
    void RecalcGearScore() => gearScore = ItemRules.GearScore(equipment);

    // ---------------- Sunucu API ----------------

    public static bool Accepts(ItemStack s) => s.Data != null && s.Data.size == ItemSize.Small;

    // Yiginlara ekler, sonra bos slotlara. stack.count kalan adede iner; hepsi girdiyse true.
    [Server]
    public bool TryAdd(ref ItemStack stack)
    {
        if (stack.IsEmpty) return true;
        if (!loaded && bag.Count == 0) return false;
        ItemData d = stack.Data;
        if (d == null || !Accepts(stack)) return false;
        int limit = d.StackLimit;
        int left = stack.count;

        for (int i = 0; i < bag.Count && left > 0; i++)
        {
            ItemStack s = bag[i];
            if (!s.CanStackWith(stack) || s.count >= limit) continue;
            int add = Mathf.Min(limit - s.count, left);
            bag[i] = s.WithCount(s.count + add);
            left -= add;
        }
        for (int i = 0; i < bag.Count && left > 0; i++)
        {
            if (!bag[i].IsEmpty) continue;
            int add = Mathf.Min(limit, left);
            bag[i] = stack.WithCount(add);
            left -= add;
        }

        stack = stack.WithCount(left);
        return left == 0;
    }

    [Server]
    public int Count(string id)
    {
        int n = 0;
        foreach (ItemStack s in bag) if (!s.IsEmpty && s.id == id) n += s.count;
        return n;
    }

    // Cantadan id'ye gore adet duser (basma tasi gibi). Yetmezse hic dokunmaz.
    [Server]
    public bool TryConsume(string id, int amount)
    {
        if (Count(id) < amount) return false;
        for (int i = bag.Count - 1; i >= 0 && amount > 0; i--)
        {
            ItemStack s = bag[i];
            if (s.IsEmpty || s.id != id) continue;
            int take = Mathf.Min(s.count, amount);
            bag[i] = s.count - take > 0 ? s.WithCount(s.count - take) : ItemStack.Empty;
            amount -= take;
        }
        return true;
    }

    // Dunyadaki esyayi cantaya al (kismen sigarsa kalan yerde kalir). Hepsi girdiyse esya yok edilir.
    [Server]
    public bool ServerPickupToBag(WorldItem it)
    {
        if (it == null || it.IsHeld || !loaded) return false;
        ItemStack s = it.Stack;
        if (!Accepts(s)) return false;
        int before = s.count;
        TryAdd(ref s);
        if (s.count == before) return false; // hic girmedi (canta dolu)
        if (s.count == 0) NetworkServer.Destroy(it.gameObject);
        else it.count = s.count;
        return true;
    }

    [Server]
    void ServerAutoPickup()
    {
        Vector3 center = transform.position + Vector3.up;
        float r2 = autoPickupRadius * autoPickupRadius;
        pickupBuffer.Clear();
        foreach (WorldItem it in WorldItem.All)
            if (it.autoPickup && !it.IsHeld && !it.OnSlot && (it.WorldCenter - center).sqrMagnitude <= r2)
                pickupBuffer.Add(it);
        foreach (WorldItem it in pickupBuffer) ServerPickupToBag(it);
    }

    // ---------------- Komutlar (InventoryUI) ----------------

    static bool ValidBag(int i) => i >= 0 && i < ItemRules.BagSize;

    // Slot degistir; ayni esyaysa yigini birlestir
    [Command]
    public void CmdMove(int from, int to)
    {
        if (!ValidBag(from) || !ValidBag(to) || from == to) return;
        ItemStack a = bag[from], b = bag[to];
        if (a.IsEmpty) return;
        if (a.CanStackWith(b))
        {
            int limit = a.Data.StackLimit;
            int move = Mathf.Min(limit - b.count, a.count);
            if (move > 0)
            {
                bag[to] = b.WithCount(b.count + move);
                bag[from] = a.count - move > 0 ? a.WithCount(a.count - move) : ItemStack.Empty;
                return;
            }
        }
        bag[from] = b;
        bag[to] = a;
    }

    [Command]
    public void CmdEquip(int bagIndex)
    {
        if (!ValidBag(bagIndex)) return;
        ItemStack s = bag[bagIndex];
        if (s.IsEmpty || !s.Data.IsEquipment) return;
        int slot = ItemRules.SlotIndex(s.Data.equipSlot);
        if (slot < 0) return;
        bag[bagIndex] = equipment[slot]; // eskisi cantaya, ayni slota
        equipment[slot] = s;
        RecalcGearScore();
    }

    [Command]
    public void CmdUnequip(int slot)
    {
        if (slot < 0 || slot >= equipment.Count || equipment[slot].IsEmpty) return;
        for (int i = 0; i < bag.Count; i++)
        {
            if (!bag[i].IsEmpty) continue;
            bag[i] = equipment[slot];
            equipment[slot] = ItemStack.Empty;
            RecalcGearScore();
            return;
        }
    }

    // Tum yigini yere birak
    [Command]
    public void CmdDrop(int bagIndex)
    {
        if (!ValidBag(bagIndex) || bag[bagIndex].IsEmpty) return;
        ItemStack s = bag[bagIndex];
        bag[bagIndex] = ItemStack.Empty;
        Vector3 pos = transform.position + transform.forward * 0.9f + Vector3.up * 1.0f;
        WorldItem it = WorldItem.ServerSpawn(s, pos, transform.rotation);
        if (it != null) it.ServerRelease(pos, transform.rotation, transform.forward * 1.5f);
    }

    // 1 adedini ele al (rafa koymak icin). Eller doluysa olmaz.
    [Command]
    public void CmdTakeToHands(int bagIndex)
    {
        if (!ValidBag(bagIndex) || bag[bagIndex].IsEmpty || carry == null || carry.ServerHeld() != null) return;
        ItemStack s = bag[bagIndex];
        bag[bagIndex] = s.count > 1 ? s.WithCount(s.count - 1) : ItemStack.Empty;
        Transform hp = carry.HoldPoint(ItemSize.Small);
        WorldItem.ServerSpawn(s.WithCount(1), hp.position, hp.rotation, false, netId);
    }

    // Cantadan 1 adedini dogrudan raf gozune diz (eller bos olmasa da olur)
    [Command]
    public void CmdPlaceFromBag(int bagIndex, string slotId, float priceRatio)
    {
        if (!ValidBag(bagIndex) || bag[bagIndex].IsEmpty) return;
        ItemStack s = bag[bagIndex];
        if (!ItemSlot.TryGet(slotId, out ItemSlot slot) || !slot.Accepts(s.Data) || !slot.IsFree()) return;
        float reach = (carry != null ? carry.reach : 2.4f) + PlaceReachBonus + 1.5f; // gecikme payi
        if (Vector3.Distance(transform.position + Vector3.up, slot.transform.position) > reach) return;

        bag[bagIndex] = s.count > 1 ? s.WithCount(s.count - 1) : ItemStack.Empty;
        WorldItem.ServerSpawn(s.WithCount(1), slot.transform.position, slot.transform.rotation, false, 0, slot.id, priceRatio);
    }

    // Rafa dizme menzili: elle koymadan biraz daha genis (panel acikken kamera donmuyor)
    public const float PlaceReachBonus = 0.6f;

    // Eldeki kucuk esyayi cantaya koy
    [Command]
    public void CmdStowHeld()
    {
        WorldItem held = carry != null ? carry.ServerHeld() : null;
        if (held == null || !Accepts(held.Stack)) return;
        ItemStack s = held.Stack;
        if (!TryAdd(ref s)) return; // yer yoksa elde kalsin (kismi eklenen olmaz: tek adet)
        NetworkServer.Destroy(held.gameObject);
    }

    // ---------------- Demirci: + basma ----------------

    public struct UpgradeResult
    {
        public bool success;     // gecti mi
        public int luckySlot;    // kesin gecis slotu (0..7)
        public int chosenSlot;   // oyuncunun koydugu slot
        public bool luckyHit;    // sansli slottan kesin gecti
        public string itemName;  // sonuctaki ad (basarisizsa eski ad)
    }

    // Yerel oyuncunun basma sonucu / hatasi (BlacksmithUI dinler)
    public static event System.Action<UpgradeResult> UpgradeFinished;
    public static event System.Action<string> UpgradeRejected;

    // fromEquip: ustundeki esya mi (true) cantadaki mi; chosenSlot: orsteki 8 slottan secilen
    [Command]
    public void CmdUpgrade(bool fromEquip, int index, int chosenSlot)
    {
        if (chosenSlot < 0 || chosenSlot >= ItemRules.AnvilSlots) return;
        if (NpcStation.Nearest(NpcType.Blacksmith, transform.position, 5f) == null)
        {
            TargetUpgradeRejected("Demirciye çok uzaksın.");
            return;
        }

        SyncList<ItemStack> list = fromEquip ? equipment : bag;
        if (index < 0 || index >= list.Count) return;
        ItemStack s = list[index];
        if (!ItemRules.CanUpgrade(s))
        {
            TargetUpgradeRejected("Bu eşya yükseltilemez.");
            return;
        }

        int gold = ItemRules.UpgradeGold(s);
        int stones = ItemRules.UpgradeStones(s);
        GameState gs = GameState.Instance;
        if (gs == null || gs.gold < gold)
        {
            TargetUpgradeRejected($"Kasada yeterli altın yok ({gold} gerekli).");
            return;
        }
        if (Count(ItemRules.UpgradeStoneId) < stones)
        {
            TargetUpgradeRejected($"Basma Taşı yetmiyor ({stones} gerekli).");
            return;
        }

        // Odeme (ikisi de yetiyor, kontrol edildi)
        gs.TrySpend(gold);
        TryConsume(ItemRules.UpgradeStoneId, stones);

        int lucky = Random.Range(0, ItemRules.AnvilSlots);
        bool luckyHit = lucky == chosenSlot;
        bool success = luckyHit || Random.value < ItemRules.UpgradeSuccessChance(s);

        ItemStack after = success ? new ItemStack(s.id, 1, s.plus + 1) : ItemStack.Empty;
        list[index] = after;
        if (fromEquip) RecalcGearScore();

        string name = ItemRules.DisplayName(success ? after : s);
        Debug.Log($"[Upgrade] {name}: {(success ? "BASARILI" : "KIRILDI")} (slot {chosenSlot}, sansli {lucky}, -{gold} altin, -{stones} tas)");
        TargetUpgradeResult(success, lucky, chosenSlot, luckyHit, name);
    }

    [TargetRpc]
    void TargetUpgradeResult(bool success, int luckySlot, int chosenSlot, bool luckyHit, string itemName)
    {
        try
        {
            UpgradeFinished?.Invoke(new UpgradeResult
            {
                success = success, luckySlot = luckySlot, chosenSlot = chosenSlot, luckyHit = luckyHit, itemName = itemName,
            });
        }
        catch (System.Exception e)
        {
            Debug.LogException(e); // RPC isleyicisinde istisna baglantiyi koparir
        }
    }

    [TargetRpc]
    void TargetUpgradeRejected(string reason)
    {
        try { UpgradeRejected?.Invoke(reason); }
        catch (System.Exception e) { Debug.LogException(e); }
    }

    // ---------------- Satici ----------------

    public static event System.Action<string> VendorMessage; // yerel oyuncu: satin alma sonucu

    [Command]
    public void CmdBuy(string id)
    {
        if (NpcStation.Nearest(NpcType.Vendor, transform.position, 5f) == null) { TargetVendorMessage("Satıcıya çok uzaksın."); return; }
        if (!VendorRules.Sells(id)) return;
        ItemData d = ItemDatabase.Get(id);
        if (d == null) return;

        int price = VendorRules.Price(id);
        GameState gs = GameState.Instance;
        if (gs == null || gs.gold < price) { TargetVendorMessage($"<color=#ff5050>Kasada yeterli altın yok ({price} gerekli).</color>"); return; }

        var s = new ItemStack(id);
        if (!HasFreeSlotFor(s)) { TargetVendorMessage("<color=#ff5050>Çantan dolu.</color>"); return; }
        gs.TrySpend(price);
        TryAdd(ref s);
        TargetVendorMessage($"<color=#7CFC7C>Satın alındı:</color> {ItemRules.DisplayName(new ItemStack(id))}  (−{price} altın)");
    }

    [TargetRpc]
    void TargetVendorMessage(string msg)
    {
        try { VendorMessage?.Invoke(msg); }
        catch (System.Exception e) { Debug.LogException(e); }
    }

    // ---------------- Gelistirici (editor / development build) ----------------

    void LateUpdate()
    {
        if (!isLocalPlayer || !Debug.isDebugBuild) return;
        UnityEngine.InputSystem.Keyboard k = UnityEngine.InputSystem.Keyboard.current;
        if (k == null) return;
        if (k.f9Key.wasPressedThisFrame) CmdDevGrant();
        if (k.f5Key.wasPressedThisFrame) CmdDevResetCharacter();
        if (k.f6Key.wasPressedThisFrame) CmdDevGiveGear();
    }

    [Command]
    void CmdDevGrant()
    {
        if (!Debug.isDebugBuild) return;
        if (GameState.Instance != null) GameState.Instance.AddGold(1000);
        ItemStack stones = new ItemStack(ItemRules.UpgradeStoneId, 5);
        TryAdd(ref stones);
        Debug.Log("[Dev] +1000 altin, +5 Basma Tasi");
    }

    // F5: karakteri sifirla (bos canta + baslangic seti). Kayit dosyasi da kendiliginden guncellenir.
    [Command]
    void CmdDevResetCharacter()
    {
        if (!Debug.isDebugBuild) return;
        for (int i = 0; i < bag.Count; i++) bag[i] = ItemStack.Empty;
        for (int i = 0; i < equipment.Count; i++) equipment[i] = ItemStack.Empty;
        ServerGiveStarter();
        loaded = true;
        RecalcGearScore();
        Debug.Log($"[Dev] Karakter sifirlandi (guc {gearScore}).");
    }

    // F6: basmak icin rastgele 6 ekipman (+0) ver
    [Command]
    void CmdDevGiveGear()
    {
        if (!Debug.isDebugBuild) return;
        ItemDatabase db = ItemDatabase.Instance;
        if (db == null) return;
        var gear = new List<ItemData>();
        foreach (ItemData d in db.items) if (d != null && d.IsEquipment) gear.Add(d);
        if (gear.Count == 0) return;
        int given = 0;
        for (int i = 0; i < 6; i++)
        {
            ItemStack s = new ItemStack(gear[Random.Range(0, gear.Count)].id);
            if (TryAdd(ref s)) given++;
        }
        Debug.Log($"[Dev] {given} test ekipmani verildi" + (given < 6 ? " (canta doldu)" : ""));
    }

    // ---------------- Istemci yardimcilari ----------------

    public bool HasFreeSlotFor(ItemStack s)
    {
        if (!Accepts(s)) return false;
        foreach (ItemStack b in bag)
            if (b.IsEmpty || (b.CanStackWith(s) && b.count < s.Data.StackLimit)) return true;
        return false;
    }
}
