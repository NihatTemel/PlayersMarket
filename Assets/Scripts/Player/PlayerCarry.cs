using Mirror;
using UnityEngine;

// Esya alma / tasima / rafa koyma / birakma / firlatma.
// Yerel oyuncu hedefi secer ve Command gonderir; sunucu dogrular ve WorldItem durumunu degistirir.
//
// Hedef secimi: oyuncunun onundeki koni (kameranin baktigi yon), menzil icindeki en yakin/en duz
// esya ya da (elde esya varken) bos ve uygun yuva. Nisangah gerektirmez.
//
// Tuslar (InputSystem_Actions): E = al / rafa koy / yere koy, Q = birak, Sol tik = firlat.
// Dukkan: kasadaki musteriye E = sat / pazarlikta kabul, Q = reddet; raftaki urune F = fiyat etiketi. R / 1 = duz vurus (PlayerCombat).
public class PlayerCarry : NetworkBehaviour, IItemHolder
{
    [Header("Erisim")]
    public float reach = 2.4f;
    [Tooltip("Bakis yonunden en fazla bu kadar derece sapan hedefler secilir.")]
    public float aimAngle = 65f;
    [Tooltip("Sunucu dogrulamasinda gecikme payi (metre).")]
    public float serverReachTolerance = 1.5f;
    [Tooltip("Yerdeki esya yazisi (Al: ...) erisimin kac kati uzaktan gorunsun. Alma menzili degismez.")]
    public float promptDistanceMultiplier = 5f;

    [Header("Tutma noktalari (oyuncuya gore)")]
    public Vector3 smallHoldOffset = new Vector3(0.25f, 1.1f, 0.55f);
    public Vector3 largeHoldOffset = new Vector3(0f, 1.0f, 0.75f);

    [Header("Birakma / firlatma")]
    public float dropForwardSpeed = 1.5f;
    public float throwSpeed = 9f;
    public float throwUpSpeed = 2.5f;

    [Header("Buyuk esya")]
    public float largeSpeedMultiplier = 0.6f;

    public WorldItem Held { get; private set; }
    public float SpeedMultiplier => Held != null && HeldSize == ItemSize.Large ? largeSpeedMultiplier : 1f;
    public bool CanJump => Held == null || HeldSize != ItemSize.Large;

    ItemSize HeldSize => Held != null && Held.Data != null ? Held.Data.size : ItemSize.Small;

    PlayerInventory inventory;
    Transform holdSmall;
    Transform holdLarge;
    WorldItem targetItem;
    bool targetInReach;        // hedef esya alinabilir mesafede mi (degilse sadece yazi)
    NpcStation targetNpc;
    CustomerAI targetCustomer;
    GUIStyle tagStyle, tagShadow;
    ItemSlot targetSlot;
    float waitUntil;           // Command cevabi beklenirken tekrar gonderme
    GUIStyle promptStyle;
    GUIStyle hintStyle;

    void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        holdSmall = CreatePoint("HoldSmall", smallHoldOffset);
        holdLarge = CreatePoint("HoldLarge", largeHoldOffset);
    }

    Transform CreatePoint(string n, Vector3 offset)
    {
        var t = new GameObject(n).transform;
        t.SetParent(transform, false);
        t.localPosition = offset;
        t.localRotation = Quaternion.identity;
        return t;
    }

    public Transform HoldPoint(ItemSize size) => size == ItemSize.Large ? holdLarge : holdSmall;

    void Update()
    {
        Held = FindHeld();
        if (!isLocalPlayer) return;

        targetItem = null;
        targetSlot = null;
        targetCustomer = null;
        targetNpc = null;
        if (UIState.Open) return;

        if (Held == null)
        {
            // Oncelik: kasadaki musteri > NPC (demirci vb.) > yerdeki/raftaki esya
            targetCustomer = CustomerAI.FindServable(Origin, AimForward(true), 3.5f);
            if (targetCustomer == null) targetNpc = NpcStation.FindInteractable(Origin, AimForward(true));
            if (targetCustomer == null && targetNpc == null)
            {
                // Once erisimdeki; yoksa uzaktakinin sadece yazisi (5x mesafe)
                targetItem = FindTargetItem(reach);
                targetInReach = targetItem != null;
                if (targetItem == null) targetItem = FindTargetItem(reach * promptDistanceMultiplier);
            }
        }
        else targetSlot = FindTargetSlot(Held.Data);

        if (Time.time < waitUntil) return;

        if (Held == null)
        {
            if (targetCustomer != null && (PlayerInputs.InteractPressed || PlayerInputs.DropPressed))
            {
                // E = sat / teklifi kabul, Q = teklifi reddet (sadece pazarlikta)
                bool accept = PlayerInputs.InteractPressed;
                if (accept || targetCustomer.Haggling) CmdServe(targetCustomer.netIdentity, accept);
                waitUntil = Time.time + 0.4f;
                return;
            }
            if (PlayerInputs.PricePressed && targetItem != null && targetInReach && targetItem.OnSlot)
            {
                PriceTagUI.Show(this, targetItem);
                return;
            }
            if (PlayerInputs.InteractPressed && targetNpc != null)
            {
                OpenStation(targetNpc);
                waitUntil = Time.time + 0.3f;
                return;
            }
            if (PlayerInputs.InteractPressed && targetItem != null && targetInReach)
            {
                CmdPickup(targetItem.netIdentity);
                waitUntil = Time.time + 0.3f;
            }
            return;
        }

        if (PlayerInputs.InteractPressed)
        {
            if (targetSlot != null) CmdPlace(Held.netIdentity, targetSlot.id, InventoryUI.PlaceRatio);
            else Release(dropForwardSpeed * Flat(transform.forward));
            waitUntil = Time.time + 0.3f;
        }
        else if (PlayerInputs.DropPressed)
        {
            Release(dropForwardSpeed * Flat(transform.forward));
            waitUntil = Time.time + 0.3f;
        }
        else if (PlayerInputs.ThrowPressed && CursorLocked())
        {
            Vector3 dir = AimForward(false);
            Release(dir * throwSpeed + Vector3.up * throwUpSpeed);
            waitUntil = Time.time + 0.3f;
        }
    }

    void OpenStation(NpcStation station)
    {
        switch (station.type)
        {
            case NpcType.Blacksmith:
                if (inventory != null) BlacksmithUI.Show(inventory, station);
                break;
            case NpcType.Vendor:
                if (inventory != null) VendorUI.Show(inventory, station);
                break;
            default:
                Debug.Log("[PlayerCarry] Bu istasyon henuz yok: " + station.type);
                break;
        }
    }

    void Release(Vector3 velocity)
    {
        Transform hp = HoldPoint(HeldSize);
        CmdRelease(Held.netIdentity, Held.transform.position, hp.rotation, velocity);
    }

    WorldItem FindHeld()
    {
        foreach (WorldItem it in WorldItem.All)
            if (it.holderNetId == netId && netId != 0) return it;
        return null;
    }

    // ---------------- Hedef secimi (yerel) ----------------

    Vector3 Origin => transform.position + Vector3.up * 1.0f;

    Vector3 AimForward(bool flat)
    {
        Camera c = Camera.main;
        Vector3 f = c != null ? c.transform.forward : transform.forward;
        return flat ? Flat(f) : f.normalized;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    WorldItem FindTargetItem(float maxDistance)
    {
        Vector3 fwd = AimForward(true);
        WorldItem best = null;
        float bestScore = float.MaxValue;
        foreach (WorldItem it in WorldItem.All)
        {
            if (it.IsHeld) continue;
            float score = Score(it.WorldCenter, fwd, maxDistance);
            if (score < bestScore) { bestScore = score; best = it; }
        }
        return best;
    }

    ItemSlot FindTargetSlot(ItemData data)
    {
        if (data == null) return null;
        Vector3 fwd = AimForward(true);

        // Dolu yuvalar (her kare bir kez)
        var occupied = new System.Collections.Generic.HashSet<string>();
        foreach (WorldItem it in WorldItem.All)
            if (it.OnSlot) occupied.Add(it.slotId);

        ItemSlot best = null;
        float bestScore = float.MaxValue;
        foreach (ItemSlot s in ItemSlot.All)
        {
            if (s == null || !s.Accepts(data) || occupied.Contains(s.id)) continue;
            float score = Score(s.transform.position, fwd);
            if (score < bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    // Envanterden rafa dizme: menzildeki en yakin bos ve uygun yuva. Panel acikken kamera donmedigi icin
    // aci siniri YOK; aci sadece esit uzakliktakiler arasinda one ceker (baktigin raf once dolar).
    public ItemSlot FindNearestFreeSlot(ItemData data, float maxDistance,
        System.Collections.Generic.ICollection<string> exclude = null)
    {
        if (data == null) return null;
        Vector3 fwd = AimForward(true);
        var occupied = new System.Collections.Generic.HashSet<string>();
        foreach (WorldItem it in WorldItem.All)
            if (it.OnSlot) occupied.Add(it.slotId);

        ItemSlot best = null;
        float bestScore = float.MaxValue;
        foreach (ItemSlot s in ItemSlot.All)
        {
            if (exclude != null && exclude.Contains(s.id)) continue;
            if (s == null || !s.Accepts(data) || occupied.Contains(s.id)) continue;
            Vector3 to = s.transform.position - Origin;
            float dist = to.magnitude;
            if (dist > maxDistance) continue;
            float score = dist / maxDistance + Vector3.Angle(fwd, Flat(to)) / 180f * 0.6f;
            if (score < bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    // Kucuk = daha iyi. Menzil/aci disi = MaxValue.
    float Score(Vector3 point, Vector3 flatForward) => Score(point, flatForward, reach);

    float Score(Vector3 point, Vector3 flatForward, float maxDistance)
    {
        Vector3 to = point - Origin;
        float dist = to.magnitude;
        if (dist > maxDistance) return float.MaxValue;
        float angle = Vector3.Angle(flatForward, Flat(to));
        if (dist > 0.6f && angle > aimAngle) return float.MaxValue;
        return angle / aimAngle + dist / maxDistance;
    }

    static bool CursorLocked()
    {
        Camera c = Camera.main;
        if (c == null) return true;
        ThirdPersonCamera tpc = c.GetComponent<ThirdPersonCamera>();
        return tpc == null || tpc.CursorLocked;
    }

    // ---------------- Sunucu ----------------

    public WorldItem ServerHeld()
    {
        foreach (WorldItem it in WorldItem.All)
            if (it.holderNetId == netId) return it;
        return null;
    }

    bool InReach(Vector3 p) => Vector3.Distance(Origin, p) <= reach + serverReachTolerance;

    [Command]
    void CmdPickup(NetworkIdentity itemIdentity)
    {
        if (itemIdentity == null || !itemIdentity.TryGetComponent(out WorldItem it)) return;
        if (it.IsHeld || !InReach(it.WorldCenter)) return;
        // Kucuk esya once cantaya; sigmazsa (ya da buyukse) ele
        if (inventory != null && inventory.ServerPickupToBag(it)) return;
        if (ServerHeld() != null) return;
        it.ServerPickup(netId);
    }

    [Command]
    void CmdPlace(NetworkIdentity itemIdentity, string slotId, float priceRatio)
    {
        if (itemIdentity == null || !itemIdentity.TryGetComponent(out WorldItem it)) return;
        if (it.holderNetId != netId) return;
        if (!ItemSlot.TryGet(slotId, out ItemSlot slot) || !slot.Accepts(it.Data) || !slot.IsFree()) return;
        if (!InReach(slot.transform.position)) return;
        it.ServerPlace(slot, priceRatio);
    }

    // Raftaki urunun fiyat etiketi (PriceTagUI)
    [Command]
    public void CmdSetPrice(NetworkIdentity itemIdentity, float ratio)
    {
        if (itemIdentity == null || !itemIdentity.TryGetComponent(out WorldItem it) || !it.OnSlot) return;
        if (Vector3.Distance(Origin, it.WorldCenter) > reach + serverReachTolerance + 2f) return;
        it.priceRatio = PricingRules.ClampRatio(ratio);
    }

    // Tum raflardaki urunlere ayni oran
    [Command]
    public void CmdSetPriceAll(float ratio)
    {
        float r = PricingRules.ClampRatio(ratio);
        foreach (WorldItem it in WorldItem.All)
            if (it.OnSlot) it.priceRatio = r;
    }

    // Kasadaki musteriye satis: accept = sat / pazarligi kabul, false = pazarligi reddet
    [Command]
    void CmdServe(NetworkIdentity customerIdentity, bool accept)
    {
        if (customerIdentity == null || !customerIdentity.TryGetComponent(out CustomerAI c)) return;
        if (Vector3.Distance(transform.position, c.transform.position) > 5f) return;
        c.ServerServe(accept);
    }

    [Command]
    void CmdRelease(NetworkIdentity itemIdentity, Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        if (itemIdentity == null || !itemIdentity.TryGetComponent(out WorldItem it)) return;
        if (it.holderNetId != netId) return;
        // Hile/gecikme sinirlari: oyuncunun yakininda, makul hiz
        if (Vector3.Distance(Origin, position) > 3f) position = transform.TransformPoint(smallHoldOffset);
        velocity = Vector3.ClampMagnitude(velocity, 20f);
        it.ServerRelease(position, rotation, velocity);
    }

    // Oyuncu oyundan cikarken elindekini dusur
    public override void OnStopServer()
    {
        WorldItem it = ServerHeld();
        if (it != null) it.ServerRelease(it.transform.position, it.transform.rotation, Vector3.zero);
    }

    // ---------------- Arayuz (gecici, IMGUI) ----------------

    void OnGUI()
    {
        if (!isLocalPlayer) return;
        Camera c = Camera.main;
        if (c == null) return;

        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter, richText = true };
            promptStyle.normal.textColor = Color.white;
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, richText = true };
            hintStyle.normal.textColor = Color.white;
        }

        DrawShelfPriceTags(c);

        if (targetCustomer != null)
        {
            string text = targetCustomer.Haggling
                ? $"<b>[E]</b> Teklifi kabul et: <color=#ffd257>{targetCustomer.offerPrice} altın</color>   <b>[Q]</b> Reddet <color=#bbbbbb>(etiket {targetCustomer.payPrice})</color>"
                : $"<b>[E]</b> Sat: {targetCustomer.itemName}  <color=#ffd257>{targetCustomer.payPrice} altın</color>";
            WorldPrompt(c, targetCustomer.transform.position + Vector3.up * 2.9f, text);
        }
        else if (targetNpc != null)
        {
            WorldPrompt(c, targetNpc.transform.position + Vector3.up * 2.3f, $"<b>[E]</b> {targetNpc.Prompt}");
        }
        else if (targetItem != null)
        {
            ItemData d = targetItem.Data;
            int unit = ItemRules.UnitValue(targetItem.Stack, GameState.CurrentPriceMultiplier);
            string value = d != null ? $"  <color=#ffd257>{unit} altın</color>" : "";
            if (targetItem.OnSlot)
            {
                // Raftaki urun: etiket fiyati + oran
                Color rc = PricingRules.RatioColor(targetItem.priceRatio);
                value = $"  Etiket: <color=#{ColorUtility.ToHtmlStringRGB(rc)}>{targetItem.TagPrice} altın (%{targetItem.priceRatio * 100f:0})</color>" +
                        $"  <color=#bbbbbb>piyasa {targetItem.MarketPrice}</color>";
            }
            string size = d == null ? "" : d.size == ItemSize.Large ? "  <i>(büyük, elde taşınır)</i>"
                : inventory != null && inventory.HasFreeSlotFor(targetItem.Stack) ? "  <i>(çantaya)</i>" : "  <i>(çanta dolu: ele)</i>";
            if (targetInReach && targetItem.OnSlot)
                WorldPrompt(c, targetItem.WorldCenter + Vector3.up * 0.4f, $"<b>[E]</b> Al: {targetItem.DisplayName}{value}   <b>[F]</b> Fiyat");
            else if (targetInReach)
                WorldPrompt(c, targetItem.WorldCenter + Vector3.up * 0.4f, $"<b>[E]</b> Al: {targetItem.DisplayName}{value}{size}");
            else // uzakta: soluk, alinmaz
                WorldPrompt(c, targetItem.WorldCenter + Vector3.up * 0.4f,
                    $"<color=#c8c8c8>{targetItem.DisplayName}</color>{value}  <color=#a0a0a0><i>(yaklaş)</i></color>");
        }
        else if (targetSlot != null)
        {
            string tag = Held != null ? $"  etiket {PricingRules.TagPrice(Held.Stack, InventoryUI.PlaceRatio)} altın (%{InventoryUI.PlaceRatio * 100f:0})" : "";
            WorldPrompt(c, targetSlot.transform.position + Vector3.up * 0.25f, $"<b>[E]</b> Koy{tag}");
        }

        if (Held != null)
        {
            string text = $"Elinde: <b>{Held.DisplayName}</b>     [E] {(targetSlot != null ? "rafa koy" : "yere koy")}  ·  [Q] bırak  ·  [Sol tık] fırlat  ·  [Tab] çanta";
            GUI.Label(new Rect(0f, Screen.height - 70f, Screen.width, 30f), text, hintStyle);
        }
    }

    // Raflardaki urunlerin ustunde kucuk fiyat yazisi (renk = fiyat orani: yesil ucuz ... kirmizi cok pahali)
    void DrawShelfPriceTags(Camera c)
    {
        if (tagStyle == null)
        {
            tagStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            tagShadow = new GUIStyle(tagStyle);
            tagShadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        }
        Vector3 cam = c.transform.position;
        foreach (WorldItem it in WorldItem.All)
        {
            if (!it.OnSlot || it == targetItem) continue;
            Vector3 w = it.WorldCenter + Vector3.up * 0.22f;
            if ((w - cam).sqrMagnitude > 9f * 9f) continue;
            Vector3 p = c.WorldToScreenPoint(w);
            if (p.z < 0f) continue;
            var r = new Rect(p.x - 40f, Screen.height - p.y - 10f, 80f, 20f);
            string s = it.TagPrice.ToString();
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), s, tagShadow);
            tagStyle.normal.textColor = PricingRules.RatioColor(it.priceRatio);
            GUI.Label(r, s, tagStyle);
        }
    }

    void WorldPrompt(Camera c, Vector3 world, string text)
    {
        Vector3 p = c.WorldToScreenPoint(world);
        if (p.z < 0f) return;
        Vector2 size = promptStyle.CalcSize(new GUIContent(text)) + new Vector2(16f, 8f);
        GUI.Box(new Rect(p.x - size.x * 0.5f, Screen.height - p.y - size.y * 0.5f, size.x, size.y), text, promptStyle);
    }
}
