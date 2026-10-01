using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Satici penceresi (KO tarzi; demirci gibi envanter yaninda acilir). Stok: VendorRules.StarterStock.
// Tikla = sec (detay + "Satin Al"), sag tik = direkt satin al. Odeme ortak kasadan (PlayerInventory.CmdBuy).
public class VendorUI : MonoBehaviour
{
    static VendorUI instance;

    const float SlotSize = 96f;
    static readonly Color Gold = new Color(0.68f, 0.53f, 0.24f);
    static readonly Color GoldDark = new Color(0.42f, 0.32f, 0.15f);
    static readonly Color Inner = new Color(0.07f, 0.055f, 0.045f, 0.97f);
    static readonly Color SlotInner = new Color(0.13f, 0.1f, 0.08f, 1f);

    PlayerInventory inv;
    NpcStation station;
    bool open;
    int selected = -1;
    float nextRefresh;
    float messageUntil;

    GameObject panel;
    Image[] slotFrame;
    RawImage[] slotIcon;
    TMP_Text[] slotShort, slotPrice, slotPlus;
    TMP_Text title, stats, priceText, message, kasaText;
    Button buyBtn;

    public static void Show(PlayerInventory inventory, NpcStation npc)
    {
        if (instance == null)
        {
            var go = new GameObject("VendorUI");
            instance = go.AddComponent<VendorUI>();
            instance.Build();
        }
        instance.inv = inventory;
        instance.station = npc;
        instance.SetOpen(true);
    }

    void OnEnable() => PlayerInventory.VendorMessage += OnMessage;
    void OnDisable() => PlayerInventory.VendorMessage -= OnMessage;

    void OnDestroy()
    {
        if (open) { UIState.Pop(); ReleaseInventory(); }
        if (instance == this) instance = null;
    }

    void SetOpen(bool value)
    {
        if (open == value) return;
        open = value;
        panel.SetActive(open);
        if (open)
        {
            UIState.Push();
            selected = -1;
            message.text = "";
            InventoryUI ui = InventoryUI.Local;
            if (ui != null)
            {
                ui.LockedOpen = true;
                ui.SetOpen(true, true);
            }
            Refresh();
        }
        else
        {
            UIState.Pop();
            ReleaseInventory();
        }
    }

    static void ReleaseInventory()
    {
        InventoryUI ui = InventoryUI.Local;
        if (ui == null) return;
        ui.LockedOpen = false;
        ui.SetOpen(false);
    }

    void Update()
    {
        if (!open) return;
        if (inv == null) { SetOpen(false); return; }
        if (station != null)
        {
            Vector3 d = inv.transform.position - station.transform.position;
            d.y = 0f;
            if (d.magnitude > station.interactRadius + 2f) { SetOpen(false); return; }
        }
        Keyboard k = Keyboard.current;
        if ((k != null && k.escapeKey.wasPressedThisFrame) || PlayerInputs.InventoryPressed) { SetOpen(false); return; }

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }
        if (messageUntil > 0f && Time.unscaledTime > messageUntil) { message.text = ""; messageUntil = 0f; }
    }

    void Refresh()
    {
        GameState gs = GameState.Instance;
        int kasa = gs != null ? gs.gold : 0;
        kasaText.text = $"Kasa: <color=#ffd257>{kasa} altın</color>";

        string[] stock = VendorRules.StarterStock;
        for (int i = 0; i < slotFrame.Length; i++)
        {
            bool has = i < stock.Length;
            slotFrame[i].gameObject.SetActive(has);
            if (!has) continue;
            ItemData d = ItemDatabase.Get(stock[i]);
            int price = VendorRules.Price(stock[i]);
            slotFrame[i].color = i == selected ? new Color(0.95f, 0.75f, 0.25f) : GoldDark;
            SetIcon(slotIcon[i], slotShort[i], d);
            slotPlus[i].text = d != null && d.Upgradeable ? "+0" : "";
            slotPrice[i].text = $"<color={(kasa >= price ? "#ffd257" : "#ff5050")}>{price}</color>";
        }

        if (selected < 0 || selected >= stock.Length)
        {
            title.text = "<color=#bba67a>Bir eşya seç (sağ tık: hemen satın al)</color>";
            stats.text = priceText.text = "";
            buyBtn.interactable = false;
            return;
        }
        var s = new ItemStack(stock[selected]);
        ItemData sd = s.Data;
        int p = VendorRules.Price(stock[selected]);
        title.text = $"<b>{ItemRules.DisplayName(s)}</b>";
        string st = sd == null ? "" : ItemRules.CategoryName(sd.category);
        if (sd != null && sd.equipSlot == EquipSlot.Weapon)
            st += $"  ·  {ItemRules.WeaponTypeName(sd.weaponType)} → <b>{ItemRules.ClassName(sd.weaponType)}</b> ({ItemRules.ClassPassive(sd.weaponType)})";
        else if (sd != null && sd.IsEquipment)
            st += $"  ·  {ItemRules.SlotName(sd.equipSlot)}";
        string line = ItemRules.StatLine(s);
        if (!string.IsNullOrEmpty(line)) st += $"\n{line}";
        if (sd != null && sd.IsEquipment) st += $"   ·   Item gücü {ItemRules.Power(s)}";
        stats.text = st;
        priceText.text = $"Fiyat: <color={(kasa >= p ? "#ffd257" : "#ff5050")}><b>{p} altın</b></color>";
        buyBtn.interactable = kasa >= p;
    }

    static void SetIcon(RawImage icon, TMP_Text shortName, ItemData d)
    {
        if (d == null) { icon.enabled = false; shortName.text = ""; return; }
        icon.enabled = true;
        if (d.icon != null) { icon.texture = d.icon; icon.color = Color.white; shortName.text = ""; }
        else
        {
            icon.texture = null;
            icon.color = d.fallbackColor * 0.8f;
            shortName.text = d.displayName.Length > 6 ? d.displayName.Substring(0, 6) : d.displayName;
        }
    }

    void OnMessage(string msg)
    {
        if (!open) return;
        message.text = msg;
        messageUntil = Time.unscaledTime + 3f;
        nextRefresh = 0f;
    }

    void Buy(int i)
    {
        if (i < 0 || i >= VendorRules.StarterStock.Length) return;
        inv.CmdBuy(VendorRules.StarterStock[i]);
    }

    class StockSlot : MonoBehaviour, IPointerClickHandler
    {
        public VendorUI ui;
        public int index;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) ui.Buy(index);
            else { ui.selected = index; ui.Refresh(); }
        }
    }

    // ---------------- Kurulum ----------------

    void Build()
    {
        var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        panel = NewImage("Panel", canvasGo.transform, new Vector2(-640f, 0f), new Vector2(640f, 840f), Gold).gameObject;
        Transform p = NewImage("Inner", panel.transform, Vector2.zero, new Vector2(626f, 826f), Inner).transform;
        NewImage("TitleBar", p, new Vector2(0f, 385f), new Vector2(626f, 56f), new Color(0.2f, 0.13f, 0.07f, 1f));
        NewText(p, "SATICI", 30, TextAlignmentOptions.Center, new Vector2(0f, 385f), new Vector2(600f, 50f)).color = Gold;
        kasaText = NewText(p, "", 20, TextAlignmentOptions.Center, new Vector2(0f, 340f), new Vector2(600f, 28f));

        int n = 12;
        slotFrame = new Image[n];
        slotIcon = new RawImage[n];
        slotShort = new TMP_Text[n];
        slotPrice = new TMP_Text[n];
        slotPlus = new TMP_Text[n];
        for (int i = 0; i < n; i++)
        {
            var pos = new Vector2(-183f + (i % 4) * 122f, 255f - (i / 4) * 135f);
            Image frame = NewImage("Stock " + i, p, pos, new Vector2(SlotSize + 8f, SlotSize + 8f), GoldDark);
            StockSlot h = frame.gameObject.AddComponent<StockSlot>();
            h.ui = this;
            h.index = i;
            Image bg = NewImage("Bg", frame.transform, Vector2.zero, new Vector2(SlotSize, SlotSize), SlotInner);
            bg.raycastTarget = false;
            slotIcon[i] = NewRaw(bg.transform, SlotSize - 14f);
            slotShort[i] = NewText(bg.transform, "", 15, TextAlignmentOptions.Center, Vector2.zero, new Vector2(SlotSize, 34f));
            slotPlus[i] = NewText(bg.transform, "", 17, TextAlignmentOptions.TopLeft, new Vector2(4f, -4f), new Vector2(SlotSize - 8f, SlotSize - 8f));
            slotPlus[i].fontStyle = FontStyles.Bold;
            slotPrice[i] = NewText(frame.transform, "", 17, TextAlignmentOptions.Center, new Vector2(0f, -66f), new Vector2(SlotSize + 20f, 24f));
            slotFrame[i] = frame;
        }

        NewImage("Divider", p, new Vector2(0f, -110f), new Vector2(560f, 2f), GoldDark);
        title = NewText(p, "", 24, TextAlignmentOptions.Center, new Vector2(0f, -140f), new Vector2(590f, 34f));
        stats = NewText(p, "", 18, TextAlignmentOptions.Center, new Vector2(0f, -195f), new Vector2(590f, 60f));
        stats.textWrappingMode = TextWrappingModes.Normal;
        priceText = NewText(p, "", 22, TextAlignmentOptions.Center, new Vector2(0f, -250f), new Vector2(590f, 30f));
        message = NewText(p, "", 18, TextAlignmentOptions.Center, new Vector2(0f, -290f), new Vector2(590f, 28f));

        buyBtn = NewButton(p, "Satın Al", new Vector2(-120f, -355f), new Vector2(220f, 58f), new Color(0.6f, 0.38f, 0.12f), () => Buy(selected));
        NewButton(p, "Kapat", new Vector2(120f, -355f), new Vector2(220f, 58f), new Color(0.3f, 0.25f, 0.2f), () => SetOpen(false));

        panel.SetActive(false);
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

    static RawImage NewRaw(Transform parent, float size)
    {
        var go = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<RawImage>();
        r.raycastTarget = false;
        r.rectTransform.sizeDelta = new Vector2(size, size);
        r.enabled = false;
        return r;
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

    static Button NewButton(Transform parent, string label, Vector2 pos, Vector2 size, Color color, System.Action onClick)
    {
        Image frame = NewImage("Button " + label, parent, pos, size + new Vector2(6f, 6f), GoldDark);
        Image img = NewImage("Face", frame.transform, Vector2.zero, size, color);
        Button b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(() => onClick());
        TMP_Text t = NewText(img.transform, label, 24, TextAlignmentOptions.Center, Vector2.zero, size - new Vector2(10f, 4f));
        t.fontStyle = FontStyles.Bold;
        return b;
    }
}
