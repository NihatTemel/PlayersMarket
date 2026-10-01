using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Demirci "Esya Yukseltme" penceresi (Knight Online tarzi). Acilinca envanter de yaninda (sagda) acilir.
// Esya envanterden (canta ya da ustundekiler) 4x2 ors slotlarindan ISTENEN birine surukle-birakilir;
// envanterde sag tik = rastgele slota koyar; ors slotunda sag tik = cikarir; slotlar arasi surukleme = tasir.
// "Yukselt" → onay → isik slotlarda doner → sunucunun sectigi "kesin gecis" slotu acilir. Esya o slottaysa
// kesin gecer; degilse normal sans. Sonuc ne olursa olsun kesin gecis slotu gosterilir. Basarisiz = esya YOK OLUR.
// Tum karar sunucuda (PlayerInventory.CmdUpgrade); burasi sadece gorunum.
public class BlacksmithUI : MonoBehaviour
{
    static BlacksmithUI instance;

    const float SlotSize = 100f;
    static readonly Color Gold = new Color(0.68f, 0.53f, 0.24f);
    static readonly Color GoldDark = new Color(0.42f, 0.32f, 0.15f);
    static readonly Color Inner = new Color(0.07f, 0.055f, 0.045f, 0.97f);
    static readonly Color SlotInner = new Color(0.13f, 0.1f, 0.08f, 1f);

    enum Phase { Idle, Confirm, Waiting, Revealing, Result }

    PlayerInventory inv;
    NpcStation station;
    bool open;
    Phase phase;

    GameObject panel;
    Image[] slotFrame;
    Image[] slotBg;
    RawImage[] slotIcon;
    TMP_Text[] slotPlus, slotShort;
    RawImage stoneIcon;
    TMP_Text stoneCount, stoneHave, costText, infoText, resultTitle, resultSub, confirmText;
    Button upgradeBtn;
    GameObject confirmBox;
    RawImage dragGhost;

    // Orsteki esya
    bool hasItem;
    (bool equip, int index) itemRef;
    string itemId;           // ref'in hala ayni esyayi gosterdigini kontrol icin
    int itemSlot = -1;

    // Animasyon / sonuc
    ItemStack pendingStack;
    int spinIndex = -1, revealedLucky = -1;
    bool resultArrived;
    PlayerInventory.UpgradeResult lastResult;
    float nextRefresh;
    int dragFromSlot = -1;

    bool Animating => phase == Phase.Waiting || phase == Phase.Revealing;

    // ---------------- Ac / kapat ----------------

    public static void Show(PlayerInventory inventory, NpcStation npc)
    {
        if (instance == null)
        {
            var go = new GameObject("BlacksmithUI");
            instance = go.AddComponent<BlacksmithUI>();
            instance.Build();
        }
        instance.inv = inventory;
        instance.station = npc;
        instance.SetOpen(true);
    }

    void OnEnable()
    {
        PlayerInventory.UpgradeFinished += OnUpgradeFinished;
        PlayerInventory.UpgradeRejected += OnUpgradeRejected;
    }

    void OnDisable()
    {
        PlayerInventory.UpgradeFinished -= OnUpgradeFinished;
        PlayerInventory.UpgradeRejected -= OnUpgradeRejected;
    }

    void OnDestroy()
    {
        if (open)
        {
            UIState.Pop();
            ReleaseInventory();
        }
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
            phase = Phase.Idle;
            hasItem = false;
            ClearResult();
            confirmBox.SetActive(false);

            // Envanter yaninda acik, kontrol bizde
            InventoryUI ui = InventoryUI.Local;
            if (ui != null)
            {
                ui.LockedOpen = true;
                ui.RightClickHandler = OnInventoryRightClick;
                ui.IsMarked = (eq, idx) => hasItem && itemRef == (eq, idx);
                ui.SetOpen(true, true);
            }
            Refresh();
        }
        else
        {
            UIState.Pop();
            StopAllCoroutines();
            dragGhost.enabled = false;
            ReleaseInventory();
        }
    }

    static void ReleaseInventory()
    {
        InventoryUI ui = InventoryUI.Local;
        if (ui == null) return;
        ui.LockedOpen = false;
        ui.RightClickHandler = null;
        ui.IsMarked = null;
        ui.SetOpen(false);
    }

    void Update()
    {
        if (!open) return;
        if (inv == null) { SetOpen(false); return; }

        // Uzaklasinca kapan (animasyon sirasinda degil)
        if (station != null && !Animating)
        {
            Vector3 d = inv.transform.position - station.transform.position;
            d.y = 0f;
            if (d.magnitude > station.interactRadius + 2f) { SetOpen(false); return; }
        }

        Keyboard k = Keyboard.current;
        bool esc = k != null && k.escapeKey.wasPressedThisFrame;
        if (esc || PlayerInputs.InventoryPressed)
        {
            if (phase == Phase.Confirm) OnCancel();
            else if (!Animating) { SetOpen(false); return; }
        }

        if (!Animating && Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.15f;
            Refresh();
        }
    }

    // ---------------- Yenileme ----------------

    ItemStack Get((bool equip, int index) r)
    {
        var list = r.equip ? inv.equipment : inv.bag;
        return r.index >= 0 && r.index < list.Count ? list[r.index] : ItemStack.Empty;
    }

    void Refresh()
    {
        // Esya yerinden oynadiysa / yok olduysa orsten dussun
        if (hasItem && !Animating)
        {
            ItemStack cur = Get(itemRef);
            if (cur.IsEmpty || cur.id != itemId || !ItemRules.CanUpgrade(cur)) hasItem = false;
        }
        RefreshSlots();
        RefreshInfo();
    }

    void RefreshSlots()
    {
        ItemStack s = Animating ? pendingStack : hasItem ? Get(itemRef) : ItemStack.Empty;
        for (int i = 0; i < ItemRules.AnvilSlots; i++)
        {
            Color frame = GoldDark, bg = SlotInner;
            if (i == spinIndex) { frame = Color.white; bg = new Color(0.45f, 0.42f, 0.35f, 1f); }
            if (i == revealedLucky) { frame = new Color(0.3f, 1f, 0.4f); bg = new Color(0.12f, 0.35f, 0.15f, 1f); }
            slotFrame[i].color = frame;
            slotBg[i].color = bg;

            bool show = i == itemSlot && !s.IsEmpty && (hasItem || Animating) && i != dragFromSlot;
            SetIcon(slotIcon[i], slotShort[i], show ? s : ItemStack.Empty);
            slotPlus[i].text = show ? $"+{s.plus}" : "";
            slotPlus[i].color = PlusColor(s.plus);
        }
    }

    void RefreshInfo()
    {
        ItemData stone = ItemDatabase.Get(ItemRules.UpgradeStoneId);
        stoneIcon.texture = stone != null ? stone.icon : null;
        stoneIcon.enabled = stone != null && stone.icon != null;
        int have = 0;
        foreach (ItemStack b in inv.bag) if (!b.IsEmpty && b.id == ItemRules.UpgradeStoneId) have += b.count;
        GameState gs = GameState.Instance;
        int kasa = gs != null ? gs.gold : 0;

        upgradeBtn.interactable = false;
        if (Animating) return;

        ItemStack s = hasItem ? Get(itemRef) : ItemStack.Empty;
        if (!hasItem || s.IsEmpty)
        {
            stoneCount.text = "";
            stoneHave.text = $"Sende: {have}";
            costText.text = $"<size=80%>Ücret</size>\n—\n<size=75%>Kasa: {kasa}</size>";
            infoText.text = "<color=#bba67a>Yükseltilecek eşyayı envanterden bir slota sürükle\nya da envanterde sağ tıkla (rastgele slot).</color>";
            return;
        }

        var next = new ItemStack(s.id, 1, s.plus + 1);
        float chance = ItemRules.UpgradeSuccessChance(s);
        int gold = ItemRules.UpgradeGold(s);
        int stones = ItemRules.UpgradeStones(s);
        bool goldOk = kasa >= gold, stonesOk = have >= stones;

        stoneCount.text = $"x{stones}";
        stoneCount.color = stonesOk ? Color.white : new Color(1f, 0.35f, 0.3f);
        stoneHave.text = $"Sende: {have}";
        costText.text = $"<size=80%>Ücret</size>\n<color={(goldOk ? "#ffd257" : "#ff5050")}><b>{gold}</b> altın</color>\n<size=75%>Kasa: {kasa}</size>";

        string t = $"<b>{ItemRules.DisplayName(s)}</b>  →  <color=#7CFC7C><b>+{next.plus}</b></color>\n";
        t += $"Güç: {ItemRules.Power(s)} → <color=#7CFC7C>{ItemRules.Power(next)}</color>\n";
        if (chance >= 0.999f)
            t += "Geçme ihtimali: <color=#7CFC7C><b>%100</b></color>";
        else
            t += $"Geçme ihtimali: <b>%{chance * 100f:0}</b>   ·   Şanslı slotla: <b>%{ItemRules.ChanceWithLuckySlot(s) * 100f:0}</b>\n" +
                 "<color=#ff5050>Başarısız olursa eşya YOK OLUR!</color>";
        infoText.text = t;

        upgradeBtn.interactable = goldOk && stonesOk && phase != Phase.Confirm;
    }

    static void SetIcon(RawImage icon, TMP_Text shortName, ItemStack s)
    {
        ItemData d = s.Data;
        if (s.IsEmpty || d == null) { icon.enabled = false; if (shortName != null) shortName.text = ""; return; }
        icon.enabled = true;
        if (d.icon != null)
        {
            icon.texture = d.icon;
            icon.color = Color.white;
            if (shortName != null) shortName.text = "";
        }
        else
        {
            icon.texture = null;
            icon.color = d.fallbackColor * 0.8f;
            if (shortName != null) shortName.text = d.displayName.Length > 6 ? d.displayName.Substring(0, 6) : d.displayName;
        }
    }

    static Color PlusColor(int plus) =>
        plus >= 9 ? new Color(1f, 0.55f, 0.1f) :
        plus >= 7 ? new Color(0.8f, 0.4f, 1f) :
        plus >= 4 ? new Color(0.35f, 0.7f, 1f) : Color.white;

    // ---------------- Esyayi orse koyma ----------------

    void PutOnAnvil((bool equip, int index) r, int slot)
    {
        ItemStack s = Get(r);
        if (!ItemRules.CanUpgrade(s))
        {
            ShowMessage("<color=#ff9050>Bu eşya yükseltilemez.</color>", s.IsEmpty ? "" : "(Sadece silah, zırh ve takılar; en fazla +10)");
            return;
        }
        itemRef = r;
        itemId = s.id;
        hasItem = true;
        itemSlot = slot;
        ClearResult();
        Refresh();
    }

    // Envanterde sag tik: yukseltilebilirse rastgele slota koy (degilse envanterin kendi sag tiki)
    bool OnInventoryRightClick(bool equip, int index)
    {
        if (Animating || phase == Phase.Confirm) return true;
        ItemStack s = Get((equip, index));
        if (!ItemRules.CanUpgrade(s)) return false;
        PutOnAnvil((equip, index), Random.Range(0, ItemRules.AnvilSlots));
        return true;
    }

    void OnSlotDrop(int slot)
    {
        if (Animating || phase == Phase.Confirm) return;
        if (InventoryUI.IsDragging)
        {
            InventoryUI.Local.MarkDropHandled();
            PutOnAnvil(InventoryUI.DragItem, slot);
        }
        else if (dragFromSlot >= 0 && hasItem)
        {
            itemSlot = slot; // ors icinde tasima
            ClearResult();
        }
        Refresh();
    }

    void OnSlotRightClick(int slot)
    {
        if (Animating || phase == Phase.Confirm) return;
        if (hasItem && slot == itemSlot)
        {
            hasItem = false;
            ClearResult();
            Refresh();
        }
    }

    void OnSlotBeginDrag(int slot, PointerEventData e)
    {
        if (Animating || phase == Phase.Confirm || !hasItem || slot != itemSlot) return;
        dragFromSlot = slot;
        dragGhost.texture = slotIcon[slot].texture;
        dragGhost.color = slotIcon[slot].color;
        dragGhost.enabled = true;
        dragGhost.rectTransform.position = e.position;
        RefreshSlots();
    }

    void OnSlotDrag(PointerEventData e)
    {
        if (dragFromSlot >= 0) dragGhost.rectTransform.position = e.position;
    }

    void OnSlotEndDrag(PointerEventData e)
    {
        if (dragFromSlot < 0) return;
        // Ors disina birakildi: orsten cikar (esya envanterde zaten duruyor)
        GameObject over = e.pointerCurrentRaycast.gameObject;
        if (over == null || over.GetComponentInParent<AnvilSlot>() == null) hasItem = false;
        dragFromSlot = -1;
        dragGhost.enabled = false;
        Refresh();
    }

    class AnvilSlot : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        public BlacksmithUI ui;
        public int index;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) ui.OnSlotRightClick(index);
        }
        public void OnBeginDrag(PointerEventData e) => ui.OnSlotBeginDrag(index, e);
        public void OnDrag(PointerEventData e) => ui.OnSlotDrag(e);
        public void OnEndDrag(PointerEventData e) => ui.OnSlotEndDrag(e);
        public void OnDrop(PointerEventData e) => ui.OnSlotDrop(index);
    }

    // ---------------- Yukseltme ----------------

    void ClearResult()
    {
        if (phase == Phase.Result) phase = Phase.Idle;
        revealedLucky = -1;
        spinIndex = -1;
        resultTitle.text = resultSub.text = "";
    }

    void ShowMessage(string title, string sub)
    {
        resultTitle.text = title;
        resultSub.text = sub;
    }

    void OnUpgradeClick()
    {
        if (!hasItem || (phase != Phase.Idle && phase != Phase.Result)) return;
        ItemStack s = Get(itemRef);
        if (!ItemRules.CanUpgrade(s)) return;
        ClearResult();
        float chance = ItemRules.UpgradeSuccessChance(s);
        confirmText.text =
            "<b>Yükseltmeyi onaylıyor musunuz?</b>\n\n" +
            $"{ItemRules.DisplayName(s)}  →  <color=#7CFC7C>+{s.plus + 1}</color>\n" +
            (chance >= 0.999f
                ? "Geçme ihtimali: %100"
                : $"Geçme ihtimali: %{chance * 100f:0}   (şanslı slotla %{ItemRules.ChanceWithLuckySlot(s) * 100f:0})\n" +
                  "<color=#ff5050>Başarısız olursa eşya yok olacak.</color>") +
            $"\n<size=85%>Eşya {itemSlot + 1}. slotta  ·  {ItemRules.UpgradeGold(s)} altın  ·  {ItemRules.UpgradeStones(s)} Basma Taşı</size>";
        confirmBox.SetActive(true);
        phase = Phase.Confirm;
        RefreshInfo();
    }

    void OnConfirm()
    {
        confirmBox.SetActive(false);
        if (!hasItem) { phase = Phase.Idle; return; }
        pendingStack = Get(itemRef);
        phase = Phase.Waiting;
        resultArrived = false;
        inv.CmdUpgrade(itemRef.equip, itemRef.index, itemSlot);
        StartCoroutine(Spin());
    }

    void OnCancel()
    {
        confirmBox.SetActive(false);
        phase = Phase.Idle;
        RefreshInfo();
    }

    void OnUpgradeFinished(PlayerInventory.UpgradeResult r)
    {
        if (!open) return;
        lastResult = r;
        resultArrived = true;
    }

    void OnUpgradeRejected(string reason)
    {
        if (!open) return;
        StopAllCoroutines();
        phase = Phase.Idle;
        spinIndex = -1;
        ShowMessage("<color=#ff9050>Yükseltilemedi</color>", reason);
        Refresh();
    }

    // Isik slotlarda (okuma sirasiyla) doner; sonuc gelince yavaslayip kesin gecis slotunda durur
    IEnumerator Spin()
    {
        int n = ItemRules.AnvilSlots;
        int i = itemSlot;
        float waited = 0f;
        while (!resultArrived && waited < 6f)
        {
            spinIndex = i = (i + 1) % n;
            RefreshSlots();
            yield return new WaitForSecondsRealtime(0.05f);
            waited += 0.05f;
        }
        if (!resultArrived)
        {
            OnUpgradeRejected("Sunucudan cevap gelmedi.");
            yield break;
        }

        phase = Phase.Revealing;
        int target = lastResult.luckySlot;
        int steps = n + (target - i + n) % n; // en az bir tur + hedefe kadar
        for (int k = 0; k < steps; k++)
        {
            spinIndex = i = (i + 1) % n;
            RefreshSlots();
            float t = k / (float)Mathf.Max(1, steps - 1);
            yield return new WaitForSecondsRealtime(Mathf.Lerp(0.05f, 0.32f, t * t));
        }

        spinIndex = -1;
        revealedLucky = target;
        phase = Phase.Result;
        ShowResult();
    }

    void ShowResult()
    {
        PlayerInventory.UpgradeResult r = lastResult;
        string luckyInfo = r.luckyHit
            ? $"Eşya kesin geçiş slotundaydı ({r.luckySlot + 1}. slot)!"
            : $"Kesin geçiş slotu: {r.luckySlot + 1}. slot   (eşya {r.chosenSlot + 1}. slottaydı)";
        if (r.success)
        {
            ShowMessage($"<color=#7CFC7C>Yükseltme başarılı!</color>  {r.itemName}", luckyInfo);
        }
        else
        {
            ShowMessage($"<color=#ff5050>Yükseltme başarısız!</color>  {r.itemName} yok oldu.", luckyInfo);
            hasItem = false;
        }
        RefreshSlots();
        RefreshInfo();
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

        // Cerceve (altin) + ic (koyu) + baslik bandi
        panel = NewImage("Panel", canvasGo.transform, new Vector2(-640f, 0f), new Vector2(640f, 840f), Gold).gameObject;
        Transform p = NewImage("Inner", panel.transform, Vector2.zero, new Vector2(626f, 826f), Inner).transform;
        NewImage("TitleBar", p, new Vector2(0f, 385f), new Vector2(626f, 56f), new Color(0.2f, 0.13f, 0.07f, 1f));
        NewText(p, "EŞYA YÜKSELTME", 30, TextAlignmentOptions.Center, new Vector2(0f, 385f), new Vector2(600f, 50f)).color = Gold;
        NewText(p, "Eşyayı envanterden bir slota sürükle  ·  Envanterde sağ tık: rastgele slot", 17,
            TextAlignmentOptions.Center, new Vector2(0f, 338f), new Vector2(600f, 26f)).color = new Color(0.8f, 0.72f, 0.55f);

        // 4x2 ors slotlari
        slotFrame = new Image[ItemRules.AnvilSlots];
        slotBg = new Image[ItemRules.AnvilSlots];
        slotIcon = new RawImage[ItemRules.AnvilSlots];
        slotPlus = new TMP_Text[ItemRules.AnvilSlots];
        slotShort = new TMP_Text[ItemRules.AnvilSlots];
        for (int i = 0; i < ItemRules.AnvilSlots; i++)
        {
            var pos = new Vector2(-183f + (i % 4) * 122f, 245f - (i / 4) * 122f);
            Image frame = NewImage("Slot " + (i + 1), p, pos, new Vector2(SlotSize + 8f, SlotSize + 8f), GoldDark);
            AnvilSlot h = frame.gameObject.AddComponent<AnvilSlot>();
            h.ui = this;
            h.index = i;
            Image bg = NewImage("Bg", frame.transform, Vector2.zero, new Vector2(SlotSize, SlotSize), SlotInner);
            bg.raycastTarget = false;
            slotIcon[i] = NewRaw(bg.transform, SlotSize - 12f);
            slotShort[i] = NewText(bg.transform, "", 16, TextAlignmentOptions.Center, Vector2.zero, new Vector2(SlotSize, 36f));
            slotPlus[i] = NewText(bg.transform, "", 20, TextAlignmentOptions.TopLeft, new Vector2(4f, -4f), new Vector2(SlotSize - 8f, SlotSize - 8f));
            slotPlus[i].fontStyle = FontStyles.Bold;
            NewText(bg.transform, (i + 1).ToString(), 17, TextAlignmentOptions.BottomRight, new Vector2(-4f, 4f),
                new Vector2(SlotSize - 8f, SlotSize - 8f)).color = new Color(1f, 1f, 1f, 0.4f);
            slotFrame[i] = frame;
            slotBg[i] = bg;
        }

        // Malzeme + ucret
        NewImage("Divider", p, new Vector2(0f, 42f), new Vector2(560f, 2f), GoldDark);
        NewText(p, "Malzeme", 20, TextAlignmentOptions.Left, new Vector2(-120f, 20f), new Vector2(330f, 30f)).color = Gold;
        Image sFrame = NewImage("StoneSlot", p, new Vector2(-220f, -55f), new Vector2(SlotSize + 8f, SlotSize + 8f), GoldDark);
        Image sBg = NewImage("Bg", sFrame.transform, Vector2.zero, new Vector2(SlotSize, SlotSize), SlotInner);
        stoneIcon = NewRaw(sBg.transform, SlotSize - 16f);
        stoneCount = NewText(sBg.transform, "", 22, TextAlignmentOptions.BottomRight, new Vector2(-4f, 4f), new Vector2(SlotSize - 8f, SlotSize - 8f));
        stoneCount.fontStyle = FontStyles.Bold;
        NewText(p, "Basma Taşı", 18, TextAlignmentOptions.Left, new Vector2(-55f, -35f), new Vector2(190f, 26f));
        stoneHave = NewText(p, "", 17, TextAlignmentOptions.Left, new Vector2(-55f, -62f), new Vector2(190f, 26f));
        stoneHave.color = new Color(0.75f, 0.85f, 1f);

        NewImage("CostFrame", p, new Vector2(165f, -55f), new Vector2(208f, 108f), GoldDark);
        NewImage("CostBg", p, new Vector2(165f, -55f), new Vector2(200f, 100f), SlotInner).raycastTarget = false;
        costText = NewText(p, "", 22, TextAlignmentOptions.Center, new Vector2(165f, -55f), new Vector2(196f, 96f));

        // Bilgi + sonuc
        infoText = NewText(p, "", 20, TextAlignmentOptions.Center, new Vector2(0f, -170f), new Vector2(580f, 110f));
        infoText.textWrappingMode = TextWrappingModes.Normal;
        resultTitle = NewText(p, "", 26, TextAlignmentOptions.Center, new Vector2(0f, -252f), new Vector2(600f, 36f));
        resultSub = NewText(p, "", 18, TextAlignmentOptions.Center, new Vector2(0f, -285f), new Vector2(600f, 28f));
        resultSub.color = new Color(0.85f, 0.8f, 0.65f);

        upgradeBtn = NewButton(p, "Yükselt", new Vector2(-120f, -355f), new Vector2(220f, 58f), new Color(0.6f, 0.38f, 0.12f), OnUpgradeClick);
        NewButton(p, "Kapat", new Vector2(120f, -355f), new Vector2(220f, 58f), new Color(0.3f, 0.25f, 0.2f), () => SetOpen(false));

        // Onay kutusu
        confirmBox = NewImage("Confirm", p, Vector2.zero, new Vector2(626f, 826f), new Color(0f, 0f, 0f, 0.65f)).gameObject;
        Image cFrame = NewImage("Frame", confirmBox.transform, Vector2.zero, new Vector2(570f, 330f), Gold);
        NewImage("Box", cFrame.transform, Vector2.zero, new Vector2(556f, 316f), new Color(0.12f, 0.09f, 0.07f, 1f));
        confirmText = NewText(cFrame.transform, "", 21, TextAlignmentOptions.Center, new Vector2(0f, 45f), new Vector2(520f, 210f));
        confirmText.textWrappingMode = TextWrappingModes.Normal;
        NewButton(cFrame.transform, "Evet", new Vector2(-115f, -115f), new Vector2(190f, 56f), new Color(0.6f, 0.38f, 0.12f), OnConfirm);
        NewButton(cFrame.transform, "Hayır", new Vector2(115f, -115f), new Vector2(190f, 56f), new Color(0.3f, 0.25f, 0.2f), OnCancel);
        confirmBox.SetActive(false);

        // Surukleme hayaleti
        var ghostGo = new GameObject("DragGhost", typeof(RectTransform), typeof(RawImage));
        ghostGo.transform.SetParent(canvasGo.transform, false);
        dragGhost = ghostGo.GetComponent<RawImage>();
        dragGhost.raycastTarget = false;
        dragGhost.rectTransform.sizeDelta = new Vector2(SlotSize * 0.9f, SlotSize * 0.9f);
        dragGhost.enabled = false;

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
        TMP_Text text = NewText(img.transform, label, 24, TextAlignmentOptions.Center, Vector2.zero, size - new Vector2(10f, 4f));
        text.fontStyle = FontStyles.Bold;
        return b;
    }
}
