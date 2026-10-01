using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Raftaki urunun fiyat etiketi (rafa bakip F). Oran = etiket / piyasa (%50-%200).
// Her degisiklik aninda sunucuya gider (PlayerCarry.CmdSetPrice). Musteri tiplerine gore beklenen tepkiyi gosterir.
public class PriceTagUI : MonoBehaviour
{
    static PriceTagUI instance;

    PlayerCarry carry;
    WorldItem item;
    bool open;
    float ratio;
    int openedFrame;

    GameObject panel;
    TMP_Text title, body;
    Image[] quick;

    public static void Show(PlayerCarry carry, WorldItem item)
    {
        if (item == null || !item.OnSlot) return;
        if (instance == null)
        {
            var go = new GameObject("PriceTagUI");
            instance = go.AddComponent<PriceTagUI>();
            instance.Build();
        }
        instance.carry = carry;
        instance.item = item;
        instance.ratio = item.priceRatio;
        instance.openedFrame = Time.frameCount;
        instance.SetOpen(true);
    }

    void OnDestroy()
    {
        if (open) UIState.Pop();
        if (instance == this) instance = null;
    }

    void SetOpen(bool value)
    {
        if (open == value) { if (open) Refresh(); return; }
        open = value;
        panel.SetActive(open);
        if (open) { UIState.Push(); Refresh(); }
        else UIState.Pop();
    }

    void Update()
    {
        if (!open) return;
        if (item == null || !item.OnSlot || carry == null) { SetOpen(false); return; }
        if ((carry.transform.position - item.transform.position).sqrMagnitude > 6f * 6f) { SetOpen(false); return; }
        Keyboard k = Keyboard.current;
        bool closeKey = (k != null && k.escapeKey.wasPressedThisFrame) || PlayerInputs.PricePressed;
        if (closeKey && Time.frameCount != openedFrame) { SetOpen(false); return; } // acan R basisini sayma
    }

    void SetRatio(float r)
    {
        ratio = PricingRules.ClampRatio(r);
        if (item != null && carry != null) carry.CmdSetPrice(item.netIdentity, ratio);
        Refresh();
    }

    void ApplyAll()
    {
        if (carry != null) carry.CmdSetPriceAll(ratio);
        Refresh();
    }

    void Refresh()
    {
        if (item == null) return;
        ItemStack s = item.Stack;
        int market = PricingRules.MarketPrice(s);
        int tag = PricingRules.TagPrice(s, ratio);
        Color rc = PricingRules.RatioColor(ratio);
        title.text = $"<b>{item.DisplayName}</b>";

        string t = $"Piyasa: <color=#ffd257>{market} altın</color>\n";
        t += $"Etiket: <size=130%><b><color=#{ColorUtility.ToHtmlStringRGB(rc)}>{tag} altın</color></b></size>  (%{ratio * 100f:0})\n\n";
        t += "<size=90%>Beklenen tepki:</size>\n";
        foreach (CustomerType ct in new[] { CustomerType.Villager, CustomerType.Adventurer, CustomerType.Noble })
        {
            PriceVerdict v = PricingRules.Judge(ratio, PricingRules.Tolerance(ct, s.Data));
            t += $"<size=90%>{PricingRules.TypeName(ct)}: {PricingRules.VerdictText(v)}</size>\n";
        }
        body.text = t;

        for (int i = 0; i < quick.Length; i++)
            quick[i].color = Mathf.Abs(PricingRules.QuickRatios[i] - ratio) < 0.001f
                ? new Color(0.8f, 0.6f, 0.2f) : new Color(0.3f, 0.28f, 0.25f);
    }

    // ---------------- Kurulum ----------------

    void Build()
    {
        var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 55;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        Color gold = new Color(0.68f, 0.53f, 0.24f);
        panel = NewImage("Frame", canvasGo.transform, new Vector2(560f, 40f), new Vector2(470f, 560f), gold).gameObject;
        Transform p = NewImage("Inner", panel.transform, Vector2.zero, new Vector2(458f, 548f), new Color(0.08f, 0.06f, 0.05f, 0.96f)).transform;

        NewText(p, "FİYAT ETİKETİ", 24, TextAlignmentOptions.Center, new Vector2(0f, 245f), new Vector2(430f, 36f)).color = gold;
        title = NewText(p, "", 22, TextAlignmentOptions.Center, new Vector2(0f, 205f), new Vector2(430f, 34f));
        body = NewText(p, "", 20, TextAlignmentOptions.TopLeft, new Vector2(0f, 80f), new Vector2(400f, 220f));
        body.textWrappingMode = TextWrappingModes.Normal;

        quick = new Image[PricingRules.QuickRatios.Length];
        for (int i = 0; i < quick.Length; i++)
        {
            float r = PricingRules.QuickRatios[i];
            string lbl = Mathf.Approximately(r, 1f) ? "Piyasa" : (r > 1f ? "+" : "") + Mathf.RoundToInt((r - 1f) * 100f) + "%";
            quick[i] = NewButton(p, lbl, new Vector2(-175f + i * 70f, -70f), new Vector2(64f, 40f), new Color(0.3f, 0.28f, 0.25f), () => SetRatio(r), 15);
        }
        NewButton(p, "−5%", new Vector2(-110f, -125f), new Vector2(120f, 42f), new Color(0.35f, 0.25f, 0.22f), () => SetRatio(ratio - 0.05f), 20);
        NewButton(p, "+5%", new Vector2(110f, -125f), new Vector2(120f, 42f), new Color(0.25f, 0.35f, 0.22f), () => SetRatio(ratio + 0.05f), 20);
        NewButton(p, "Tüm raflara uygula", new Vector2(0f, -185f), new Vector2(330f, 46f), new Color(0.45f, 0.35f, 0.15f), ApplyAll, 20);
        NewButton(p, "Kapat (F / Esc)", new Vector2(0f, -240f), new Vector2(240f, 42f), new Color(0.3f, 0.25f, 0.2f), () => SetOpen(false), 18);

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

    static Image NewButton(Transform parent, string label, Vector2 pos, Vector2 size, Color color, System.Action onClick, float fontSize)
    {
        Image img = NewImage("Button " + label, parent, pos, size, color);
        Button b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(() => onClick());
        TMP_Text t = NewText(img.transform, label, fontSize, TextAlignmentOptions.Center, Vector2.zero, size - new Vector2(4f, 2f));
        t.fontStyle = FontStyles.Bold;
        return img;
    }
}
