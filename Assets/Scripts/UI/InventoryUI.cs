using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Envanter paneli (Tab / I). Yerel oyuncunun PlayerInventory'si kodla kurar; prefab/sahne gerekmez.
// Ust: item gucu, kasa, dukkan + sinif ve statlar · Sol: 8 ekipman yuvasi · Orta: 24 slot canta ·
// Sag: secili esya detayi, rafa etiket orani, butonlar.
// Surukle-birak: canta<->canta tasi/birlestir, canta->yuva kusan, yuva->canta cikar, hicbir pencere ustu
// degilse = yere at. Sag tik: ekipmansa kusan/cikar, degilse yakin rafa diz.
// Diger paneller (demirci, satici) bu paneli yanlarinda acar: LockedOpen, RightClickHandler, IsMarked, DragItem.
// Tum islemler sunucuya Command (PlayerInventory).
public class InventoryUI : MonoBehaviour
{
    const float SlotSize = 92f;

    PlayerInventory inv;
    PlayerCarry carry;
    RectTransform canvasRect;
    GameObject panel;
    RectTransform panelRect;
    TMP_Text header, detailTitle, detailBody;
    SlotView[] bagViews, equipViews;
    Button primaryBtn, handsBtn, dropBtn, stowBtn, placeBtn;
    TMP_Text primaryLabel, placeLabel;
    ItemSlot placeTarget;                                   // secili esya icin en yakin bos raf gozu
    // Rafa koyarken kullanilan etiket orani (fiyat / piyasa). Hem envanterden dizme hem elle koyma.
    public static float PlaceRatio = 1f;
    TMP_Text priceLabel;
    Image[] ratioButtons;
    readonly Dictionary<string, float> pendingSlots = new Dictionary<string, float>(); // az once dizilen (cevap bekleniyor)
    GUIStyle markerStyle;
    RawImage dragGhost;

    bool open;
    int selBag = -1, selEquip = -1;
    float nextRefresh;
    SlotHandler dragSource;
    bool dropHandled;

    // ---- Diger paneller icin (demirci vb.) ----
    public static InventoryUI Local { get; private set; }
    // Surukleme suruyorsa kaynak (equip = ekipman yuvasi mi, index)
    public static bool IsDragging => Local != null && Local.dragSource != null;
    public static (bool equip, int index) DragItem =>
        IsDragging ? (Local.dragSource.isEquip, Local.dragSource.index) : (false, -1);
    public Func<bool, int, bool> RightClickHandler; // true donerse varsayilan sag tik calismaz
    public Func<bool, int, bool> IsMarked;          // vurgulanacak slot (orn. orste duran esya)
    public bool LockedOpen;                         // baska panel yonetiyor: Tab/Esc kapatmaz
    public bool IsOpen => open;
    public void MarkDropHandled() => dropHandled = true;

    class SlotView
    {
        public Image bg;
        public RawImage icon;
        public TMP_Text plus, count, shortName;
    }

    public static InventoryUI Create(PlayerInventory inventory)
    {
        var go = new GameObject("InventoryUI");
        InventoryUI ui = go.AddComponent<InventoryUI>();
        ui.inv = inventory;
        ui.carry = inventory.GetComponent<PlayerCarry>();
        ui.Build();
        Local = ui;
        return ui;
    }

    void OnDestroy()
    {
        if (open) UIState.Pop();
        if (Local == this) Local = null;
    }

    void Update()
    {
        if (inv == null) { Destroy(gameObject); return; }

        bool esc = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        if (!LockedOpen && (PlayerInputs.InventoryPressed || (open && esc))) SetOpen(!open);
        if (!open) return;

        if (dragGhost.enabled) return; // surukleme sirasinda yenileme yok
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.15f;
            Refresh();
        }
    }

    // docked: baska bir panelin yaninda, saga yasli acilir (demirci, satici)
    public void SetOpen(bool value, bool docked = false)
    {
        panelRect.anchoredPosition = docked ? new Vector2(330f, 0f) : Vector2.zero;
        if (open == value) return;
        open = value;
        panel.SetActive(open);
        if (open) { UIState.Push(); Refresh(); }
        else { UIState.Pop(); selBag = selEquip = -1; dragGhost.enabled = false; }
    }

    // ---------------- Yenileme ----------------

    void Refresh()
    {
        if (inv.bag.Count < ItemRules.BagSize || inv.equipment.Count < ItemRules.EquipSlots.Length)
        {
            header.text = "Envanter yükleniyor...";
            return;
        }

        GameState gs = GameState.Instance;
        string gold = gs != null ? $"{gs.gold}" : "-";
        string shop = gs != null ? $"Dükkan Sv.{gs.shopLevel} (fiyat x{gs.PriceMultiplier:0.00})" : "";
        ItemRules.Stats st = ItemRules.CharacterStats(inv.equipment);
        string cls = st.weapon != WeaponType.None
            ? $"<b>{ItemRules.ClassName(st.weapon)}</b> <size=80%>({ItemRules.ClassPassive(st.weapon)})</size>"
            : "<b>Silahsız</b>";
        header.text =
            $"<b>Envanter</b>      Item Gücü: <b>{inv.gearScore}</b>      Kasa: <color=#ffd257>{gold} altın</color>      {shop}\n" +
            $"<size=90%>{cls}      Can <b>{st.maxHp}</b>  ·  Zırh <b>{st.armor}</b>  ·  Saldırı <b>{st.attack}</b>  ·  " +
            $"Kritik <b>%{st.crit * 100f:0}</b>  ·  ~{ItemRules.Dps(st):0} hasar/sn</size>";

        for (int i = 0; i < bagViews.Length; i++) SetSlot(bagViews[i], inv.bag[i], i == selBag, Marked(false, i));
        for (int i = 0; i < equipViews.Length; i++) SetSlot(equipViews[i], inv.equipment[i], i == selEquip, Marked(true, i));
        RefreshDetails();
    }

    bool Marked(bool equip, int index) => IsMarked != null && IsMarked(equip, index);

    static void SetSlot(SlotView v, ItemStack s, bool selected, bool marked = false)
    {
        v.bg.color = selected ? new Color(0.95f, 0.75f, 0.25f, 0.9f)
            : marked ? new Color(0.3f, 0.6f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.12f);
        ItemData d = s.Data;
        if (s.IsEmpty || d == null)
        {
            v.icon.enabled = false;
            v.plus.text = v.count.text = v.shortName.text = "";
            return;
        }
        v.icon.enabled = true;
        if (d.icon != null)
        {
            v.icon.texture = d.icon;
            v.icon.color = Color.white;
            v.shortName.text = "";
        }
        else
        {
            v.icon.texture = null;
            v.icon.color = d.fallbackColor * 0.8f;
            v.shortName.text = d.displayName.Length > 6 ? d.displayName.Substring(0, 6) : d.displayName;
        }
        v.plus.text = d.Upgradeable || s.plus > 0 ? $"+{s.plus}" : ""; // ekipman +0 da gosterilir
        v.plus.color = PlusColor(s.plus);
        v.count.text = s.count > 1 ? s.count.ToString() : "";
    }

    static Color PlusColor(int plus) =>
        plus >= 9 ? new Color(1f, 0.55f, 0.1f) :
        plus >= 7 ? new Color(0.8f, 0.4f, 1f) :
        plus >= 4 ? new Color(0.35f, 0.7f, 1f) : Color.white;

    void RefreshDetails()
    {
        ItemStack s = selBag >= 0 ? inv.bag[selBag] : selEquip >= 0 ? inv.equipment[selEquip] : ItemStack.Empty;
        ItemData d = s.Data;
        WorldItem held = carry != null ? carry.Held : null;

        stowBtn.gameObject.SetActive(held != null && PlayerInventory.Accepts(held.Stack));

        RefreshPriceRow(selBag >= 0 && !s.IsEmpty ? s : ItemStack.Empty);
        placeTarget = selBag >= 0 && !s.IsEmpty ? FindPlaceSlot(d) : null;
        placeBtn.gameObject.SetActive(placeTarget != null);
        if (placeTarget != null) placeLabel.text = $"Rafa Diz  ({SlotLabel(placeTarget.id)})";

        if (s.IsEmpty || d == null)
        {
            detailTitle.text = selEquip >= 0 ? $"{ItemRules.SlotName(ItemRules.EquipSlots[selEquip])} (boş)" : "";
            detailBody.text = selBag < 0 && selEquip < 0 ? "Bir eşya seç." : "";
            primaryBtn.gameObject.SetActive(false);
            handsBtn.gameObject.SetActive(false);
            dropBtn.gameObject.SetActive(false);
            return;
        }

        detailTitle.text = ItemRules.DisplayName(s);
        detailTitle.color = PlusColor(s.plus);

        float mult = GameState.CurrentPriceMultiplier;
        int unit = ItemRules.UnitValue(s, mult);
        string body = $"{ItemRules.CategoryName(d.category)}";
        if (d.IsEquipment)
        {
            body += d.equipSlot == EquipSlot.Weapon
                ? $"  ·  {ItemRules.WeaponTypeName(d.weaponType)} → <b>{ItemRules.ClassName(d.weaponType)}</b>"
                : $"  ·  {ItemRules.SlotName(d.equipSlot)}";
            body += $"\nItem gücü: <b>{ItemRules.Power(s)}</b>  ·  Basma +{s.plus}/+{ItemRules.MaxPlus}";
            body += $"\n<color=#9fd8ff>{ItemRules.StatLine(s)}</color>";
        }
        body += s.count > 1
            ? $"\nDeğer: <color=#ffd257>{unit} altın</color> x{s.count} = {unit * s.count}"
            : $"\nDeğer: <color=#ffd257>{unit} altın</color>";
        detailBody.text = body;

        bool inBag = selBag >= 0;
        primaryBtn.gameObject.SetActive(d.IsEquipment);
        primaryLabel.text = inBag ? "Kuşan" : "Çıkar";
        handsBtn.gameObject.SetActive(inBag);
        handsBtn.interactable = held == null;
        dropBtn.gameObject.SetActive(inBag);
    }

    // Etiket orani satiri: secili esya icin etiket fiyati + normal musterinin beklenen tepkisi
    void RefreshPriceRow(ItemStack s)
    {
        bool show = !s.IsEmpty;
        priceLabel.gameObject.SetActive(show);
        for (int i = 0; i < ratioButtons.Length; i++)
        {
            ratioButtons[i].gameObject.SetActive(show);
            bool sel = Mathf.Abs(PricingRules.QuickRatios[i] - PlaceRatio) < 0.001f;
            ratioButtons[i].color = sel ? new Color(0.8f, 0.6f, 0.2f) : new Color(0.3f, 0.28f, 0.25f);
        }
        if (!show) return;
        int tag = PricingRules.TagPrice(s, PlaceRatio);
        priceLabel.text = $"Rafa etiket %{PlaceRatio * 100f:0}: <color=#ffd257>{tag} altın</color>  (piyasa {PricingRules.MarketPrice(s)})\n" +
                          PricingRules.VerdictText(PricingRules.Judge(PlaceRatio, 1f));
    }

    // ---------------- Etkilesim ----------------

    void OnSlotClick(SlotHandler h, PointerEventData.InputButton button)
    {
        if (button == PointerEventData.InputButton.Right)
        {
            QuickAction(h);
            return;
        }
        if (h.isEquip) { selEquip = h.index; selBag = -1; }
        else { selBag = h.index; selEquip = -1; }
        Refresh();
    }

    void QuickAction(SlotHandler h)
    {
        if (RightClickHandler != null && RightClickHandler(h.isEquip, h.index)) { nextRefresh = 0f; return; }
        if (h.isEquip)
        {
            if (!inv.equipment[h.index].IsEmpty) inv.CmdUnequip(h.index);
        }
        else
        {
            ItemData d = inv.bag[h.index].Data;
            if (d != null && d.IsEquipment) inv.CmdEquip(h.index);
            else if (d != null) PlaceOnShelf(h.index, FindPlaceSlot(d)); // ekipman degil: yakin rafa diz
        }
        nextRefresh = 0f;
    }

    // ---------------- Rafa dizme ----------------

    ItemSlot FindPlaceSlot(ItemData d)
    {
        if (carry == null || d == null) return null;
        // Sunucu cevabi gelmemis gozleri atla (hizli ust uste sag tik ayni goze gitmesin)
        var stale = new List<string>();
        foreach (var kv in pendingSlots) if (Time.unscaledTime > kv.Value) stale.Add(kv.Key);
        foreach (string k in stale) pendingSlots.Remove(k);
        return carry.FindNearestFreeSlot(d, carry.reach + PlayerInventory.PlaceReachBonus, pendingSlots.Keys);
    }

    void PlaceOnShelf(int bagIndex, ItemSlot slot)
    {
        if (slot == null || bagIndex < 0) return;
        inv.CmdPlaceFromBag(bagIndex, slot.id, PlaceRatio);
        pendingSlots[slot.id] = Time.unscaledTime + 1f;
        nextRefresh = 0f;
    }

    void OnPlace()
    {
        if (selBag < 0 || placeTarget == null) return;
        PlaceOnShelf(selBag, placeTarget); // secim kalir: art arda basarak dizilebilir
    }

    // "Shelf Row A/L2/3" → "Raf A · Kat 2", "Pedestal 1/Top" → "Kaide 1"
    static string SlotLabel(string id)
    {
        string[] parts = id.Split('/');
        string shelf = parts[0]
            .Replace("Shelf Back Wall", "Arka Raf")
            .Replace("Shelf Row ", "Raf ")
            .Replace("Pedestal ", "Kaide ");
        return parts.Length >= 2 && parts[1].StartsWith("L") ? $"{shelf} · Kat {parts[1].Substring(1)}" : shelf;
    }

    // Hedef raf gozunun ustunde isaret (panel disinda gorunuyorsa)
    void OnGUI()
    {
        if (!open || placeTarget == null || dragGhost.enabled) return;
        Camera c = Camera.main;
        if (c == null) return;
        Vector3 p = c.WorldToScreenPoint(placeTarget.transform.position + Vector3.up * 0.35f);
        if (p.z < 0f || RectTransformUtility.RectangleContainsScreenPoint(panelRect, p)) return;
        if (markerStyle == null)
        {
            markerStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            markerStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);
        }
        GUI.Label(new Rect(p.x - 30f, Screen.height - p.y - 30f, 60f, 60f), "▼", markerStyle);
    }

    void OnPrimary()
    {
        if (selBag >= 0) inv.CmdEquip(selBag);
        else if (selEquip >= 0) inv.CmdUnequip(selEquip);
        selBag = selEquip = -1;
        nextRefresh = 0f;
    }

    void OnHands()
    {
        if (selBag < 0) return;
        inv.CmdTakeToHands(selBag);
        if (!LockedOpen) SetOpen(false); // ele aldi: rafa koymaya gitsin
    }

    void OnDrop()
    {
        if (selBag < 0) return;
        inv.CmdDrop(selBag);
        selBag = -1;
        nextRefresh = 0f;
    }

    void OnStow()
    {
        inv.CmdStowHeld();
        nextRefresh = 0f;
    }

    // ---------------- Surukle-birak ----------------

    void BeginDrag(SlotHandler h, PointerEventData e)
    {
        ItemStack s = h.isEquip ? inv.equipment[h.index] : inv.bag[h.index];
        if (s.IsEmpty) return;
        dragSource = h;
        dropHandled = false;
        SlotView v = h.isEquip ? equipViews[h.index] : bagViews[h.index];
        dragGhost.texture = v.icon.texture;
        dragGhost.color = v.icon.color;
        dragGhost.enabled = true;
        dragGhost.rectTransform.position = e.position;
    }

    void Drag(PointerEventData e)
    {
        if (dragSource != null) dragGhost.rectTransform.position = e.position;
    }

    void DropOn(SlotHandler target)
    {
        if (dragSource == null || target == dragSource) return;
        dropHandled = true;
        if (!dragSource.isEquip && !target.isEquip)
        {
            inv.CmdMove(dragSource.index, target.index);
        }
        else if (!dragSource.isEquip && target.isEquip)
        {
            ItemData d = inv.bag[dragSource.index].Data;
            if (d != null && d.IsEquipment && ItemRules.SlotIndex(d.equipSlot) == target.index) inv.CmdEquip(dragSource.index);
        }
        else if (dragSource.isEquip && !target.isEquip)
        {
            inv.CmdUnequip(dragSource.index);
        }
        nextRefresh = 0f;
    }

    void EndDrag(PointerEventData e)
    {
        if (dragSource == null) return;
        // Hicbir pencerenin ustu degilse (demirci paneli vb. de sayilir) cantadan yere at
        if (!dropHandled && !dragSource.isEquip && e.pointerCurrentRaycast.gameObject == null)
            inv.CmdDrop(dragSource.index);
        dragSource = null;
        dragGhost.enabled = false;
        nextRefresh = 0f;
    }

    class SlotHandler : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        public InventoryUI ui;
        public bool isEquip;
        public int index;

        public void OnPointerClick(PointerEventData e) => ui.OnSlotClick(this, e.button);
        public void OnBeginDrag(PointerEventData e) => ui.BeginDrag(this, e);
        public void OnDrag(PointerEventData e) => ui.Drag(e);
        public void OnEndDrag(PointerEventData e) => ui.EndDrag(e);
        public void OnDrop(PointerEventData e) => ui.DropOn(this);
    }

    // ---------------- Kurulum ----------------

    void Build()
    {
        var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        canvasRect = (RectTransform)canvasGo.transform;

        panel = NewImage("Panel", canvasRect, Vector2.zero, new Vector2(1220f, 780f), new Color(0.08f, 0.07f, 0.06f, 0.92f)).gameObject;
        panelRect = (RectTransform)panel.transform;
        Transform p = panel.transform;

        header = NewText(p, "", 22, TextAlignmentOptions.Left, new Vector2(0f, 338f), new Vector2(1160f, 70f));

        // Ekipman: 2 sutun x 4 satir
        NewText(p, "Ekipman", 22, TextAlignmentOptions.Center, new Vector2(-465f, 285f), new Vector2(240f, 34f));
        equipViews = new SlotView[ItemRules.EquipSlots.Length];
        for (int i = 0; i < equipViews.Length; i++)
        {
            var pos = new Vector2(-525f + (i % 2) * 120f, 220f - (i / 2) * 135f);
            equipViews[i] = NewSlot(p, pos, true, i);
            NewText(p, ItemRules.SlotName(ItemRules.EquipSlots[i]), 18, TextAlignmentOptions.Center,
                pos + new Vector2(0f, -58f), new Vector2(110f, 24f)).color = new Color(1f, 1f, 1f, 0.7f);
        }

        // Canta: 6 sutun x 4 satir
        NewText(p, "Çanta", 22, TextAlignmentOptions.Center, new Vector2(-50f, 285f), new Vector2(240f, 34f));
        bagViews = new SlotView[ItemRules.BagSize];
        for (int i = 0; i < bagViews.Length; i++)
        {
            var pos = new Vector2(-300f + (i % 6) * 100f, 220f - (i / 6) * 100f);
            bagViews[i] = NewSlot(p, pos, false, i);
        }

        // Detay (sag)
        detailTitle = NewText(p, "", 26, TextAlignmentOptions.TopLeft, new Vector2(445f, 255f), new Vector2(300f, 60f));
        detailTitle.textWrappingMode = TextWrappingModes.Normal;
        detailBody = NewText(p, "", 18, TextAlignmentOptions.TopLeft, new Vector2(445f, 145f), new Vector2(300f, 150f));
        detailBody.textWrappingMode = TextWrappingModes.Normal;

        // Etiket orani: hizli butonlar (rafa koyarken kullanilir)
        priceLabel = NewText(p, "", 16, TextAlignmentOptions.Center, new Vector2(445f, 46f), new Vector2(300f, 44f));
        priceLabel.textWrappingMode = TextWrappingModes.Normal;
        ratioButtons = new Image[PricingRules.QuickRatios.Length];
        for (int i = 0; i < ratioButtons.Length; i++)
        {
            float r = PricingRules.QuickRatios[i];
            string lbl = Mathf.Approximately(r, 1f) ? "Piyasa" : (r > 1f ? "+" : "") + Mathf.RoundToInt((r - 1f) * 100f);
            Button rb = NewButton(p, lbl, new Vector2(320f + i * 50f, 8f), new Color(0.3f, 0.28f, 0.25f),
                () => { PlaceRatio = r; nextRefresh = 0f; }, out TMP_Text rt);
            ((RectTransform)rb.transform).sizeDelta = new Vector2(47f, 30f);
            ((RectTransform)rt.transform).sizeDelta = new Vector2(47f, 30f);
            rt.fontSize = Mathf.Approximately(r, 1f) ? 12 : 14;
            ratioButtons[i] = rb.GetComponent<Image>();
        }

        placeBtn = NewButton(p, "Rafa Diz", new Vector2(445f, -45f), new Color(0.75f, 0.55f, 0.15f), OnPlace, out placeLabel);
        placeLabel.fontSize = 19;
        primaryBtn = NewButton(p, "Kuşan", new Vector2(445f, -105f), new Color(0.25f, 0.6f, 0.35f), OnPrimary, out primaryLabel);
        handsBtn = NewButton(p, "Eline Al", new Vector2(445f, -165f), new Color(0.25f, 0.45f, 0.75f), OnHands, out _);
        dropBtn = NewButton(p, "Yere At", new Vector2(445f, -225f), new Color(0.6f, 0.3f, 0.25f), OnDrop, out _);
        stowBtn = NewButton(p, "Elindekini Çantaya Koy", new Vector2(445f, -300f), new Color(0.45f, 0.4f, 0.2f), OnStow, out _);

        NewText(p, "Sürükle: taşı / kuşan   ·   Sağ tık: kuşan / çıkar, ekipman değilse yakın rafa diz   ·   Panelin dışına sürükle: yere at   ·   Tab / Esc: kapat",
            17, TextAlignmentOptions.Center, new Vector2(0f, -355f), new Vector2(1160f, 30f)).color = new Color(1f, 1f, 1f, 0.6f);

        // Surukleme hayaleti (en ustte, tiklamayi engellemez)
        var ghostGo = new GameObject("DragGhost", typeof(RectTransform), typeof(RawImage));
        ghostGo.transform.SetParent(canvasRect, false);
        dragGhost = ghostGo.GetComponent<RawImage>();
        dragGhost.raycastTarget = false;
        dragGhost.rectTransform.sizeDelta = new Vector2(SlotSize * 0.9f, SlotSize * 0.9f);
        dragGhost.enabled = false;

        panel.SetActive(false);
    }

    SlotView NewSlot(Transform parent, Vector2 pos, bool isEquip, int index)
    {
        Image bg = NewImage(isEquip ? $"Equip {index}" : $"Bag {index}", parent, pos, new Vector2(SlotSize, SlotSize), Color.white);
        SlotHandler h = bg.gameObject.AddComponent<SlotHandler>();
        h.ui = this;
        h.isEquip = isEquip;
        h.index = index;

        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
        iconGo.transform.SetParent(bg.transform, false);
        var icon = iconGo.GetComponent<RawImage>();
        icon.raycastTarget = false;
        icon.rectTransform.sizeDelta = new Vector2(SlotSize - 12f, SlotSize - 12f);

        var v = new SlotView
        {
            bg = bg,
            icon = icon,
            shortName = NewText(bg.transform, "", 16, TextAlignmentOptions.Center, Vector2.zero, new Vector2(SlotSize - 6f, 40f)),
            plus = NewText(bg.transform, "", 20, TextAlignmentOptions.TopLeft, new Vector2(4f, -4f), new Vector2(SlotSize - 8f, SlotSize - 8f)),
            count = NewText(bg.transform, "", 20, TextAlignmentOptions.BottomRight, new Vector2(-4f, 4f), new Vector2(SlotSize - 8f, SlotSize - 8f)),
        };
        v.plus.fontStyle = FontStyles.Bold;
        v.count.fontStyle = FontStyles.Bold;
        return v;
    }

    static Image NewImage(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static TMP_Text NewText(Transform parent, string text, float size, TextAlignmentOptions align, Vector2 pos, Vector2 box)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    static Button NewButton(Transform parent, string label, Vector2 pos, Color color, Action onClick, out TMP_Text labelText)
    {
        Image img = NewImage("Button " + label, parent, pos, new Vector2(280f, 54f), color);
        Button b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(() => onClick());
        labelText = NewText(img.transform, label, 22, TextAlignmentOptions.Center, Vector2.zero, new Vector2(270f, 50f));
        labelText.fontStyle = FontStyles.Bold;
        return b;
    }
}
