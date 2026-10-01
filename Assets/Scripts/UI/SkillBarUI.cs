using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Ekranin altinda skill cubugu: 3 slot (R/1 duz vurus, 2, 3 skill), bekleme suresi dairesel dolum + saniye.
// Yerel oyuncunun PlayerCombat'i kurar. Silah degisince slotlar kendiliginden guncellenir.
public class SkillBarUI : MonoBehaviour
{
    const float Size = 92f;
    static readonly string[] Keys = { "R / 1", "2", "3" };

    PlayerCombat combat;
    CanvasGroup group;
    TMP_Text classText;
    Image[] frame, icon, cooldown;
    TMP_Text[] shortName, keyText, secText, nameText;
    static Sprite whiteSprite;

    public static SkillBarUI Create(PlayerCombat combat)
    {
        var go = new GameObject("SkillBarUI");
        SkillBarUI ui = go.AddComponent<SkillBarUI>();
        ui.combat = combat;
        ui.Build();
        return ui;
    }

    void Update()
    {
        if (combat == null) { Destroy(gameObject); return; }
        WeaponType w = combat.CurrentWeapon;
        string weaponName = ItemRules.WeaponTypeName(w);
        classText.text = w == WeaponType.None
            ? "<b>Silahsız</b>"
            : $"<b>{ItemRules.ClassName(w)}</b>  <size=80%>{weaponName}  ·  {ItemRules.ClassPassive(w)}</size>";
        group.alpha = combat.CanAttack ? 1f : 0.45f;

        for (int i = 0; i < PlayerCombat.Slots; i++)
        {
            AbilityDef a = combat.GetAbility(i);
            bool has = a != null;
            frame[i].gameObject.SetActive(has);
            if (!has) continue;

            icon[i].color = a.color * 0.55f + new Color(0f, 0f, 0f, 0.45f);
            shortName[i].text = a.shortName;
            nameText[i].text = a.name;

            float total = combat.CooldownTotal(i);
            float left = combat.CooldownRemaining(i);
            cooldown[i].fillAmount = total > 0f ? left / total : 0f;
            secText[i].text = left > 0.05f && total > 1.2f ? (left >= 1f ? $"{left:0}" : $"{left:0.0}") : "";
            frame[i].color = left > 0f ? new Color(0.35f, 0.28f, 0.15f) : new Color(0.75f, 0.6f, 0.3f);
        }
    }

    void Build()
    {
        if (whiteSprite == null)
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));

        var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        group = canvasGo.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        // Alt orta (ekran altina yasli)
        var root = new GameObject("Bar", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvasGo.transform, false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
        root.pivot = new Vector2(0.5f, 0f);
        root.anchoredPosition = new Vector2(0f, 40f);
        root.sizeDelta = new Vector2(420f, 170f);

        classText = Text(root, "", 20, TextAlignmentOptions.Center, new Vector2(0f, 72f), new Vector2(520f, 28f));

        int n = PlayerCombat.Slots;
        frame = new Image[n];
        icon = new Image[n];
        cooldown = new Image[n];
        shortName = new TMP_Text[n];
        keyText = new TMP_Text[n];
        secText = new TMP_Text[n];
        nameText = new TMP_Text[n];
        for (int i = 0; i < n; i++)
        {
            var pos = new Vector2((i - 1) * 112f, 0f);
            frame[i] = Img(root, $"Slot {i + 1}", pos, new Vector2(Size + 6f, Size + 6f), new Color(0.75f, 0.6f, 0.3f));
            Img(frame[i].transform, "Bg", Vector2.zero, new Vector2(Size, Size), new Color(0.08f, 0.06f, 0.05f, 0.95f));
            icon[i] = Img(frame[i].transform, "Icon", Vector2.zero, new Vector2(Size - 10f, Size - 10f), Color.gray);
            shortName[i] = Text(frame[i].transform, "", 18, TextAlignmentOptions.Center, Vector2.zero, new Vector2(Size, 30f));
            shortName[i].fontStyle = FontStyles.Bold;

            cooldown[i] = Img(frame[i].transform, "Cooldown", Vector2.zero, new Vector2(Size, Size), new Color(0f, 0f, 0f, 0.7f));
            cooldown[i].sprite = whiteSprite;
            cooldown[i].type = Image.Type.Filled;
            cooldown[i].fillMethod = Image.FillMethod.Radial360;
            cooldown[i].fillOrigin = (int)Image.Origin360.Top;
            cooldown[i].fillClockwise = false;
            cooldown[i].fillAmount = 0f;

            secText[i] = Text(frame[i].transform, "", 30, TextAlignmentOptions.Center, Vector2.zero, new Vector2(Size, Size));
            secText[i].fontStyle = FontStyles.Bold;
            keyText[i] = Text(frame[i].transform, Keys[i], 15, TextAlignmentOptions.TopLeft, new Vector2(4f, -3f), new Vector2(Size - 6f, Size - 6f));
            keyText[i].color = new Color(1f, 0.9f, 0.6f);
            nameText[i] = Text(root, "", 14, TextAlignmentOptions.Center, pos + new Vector2(0f, -62f), new Vector2(110f, 22f));
            nameText[i].color = new Color(1f, 1f, 1f, 0.75f);
        }
    }

    static Image Img(Transform parent, string name, Vector2 pos, Vector2 size, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image img = go.GetComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    static TMP_Text Text(Transform parent, string text, float size, TextAlignmentOptions align, Vector2 pos, Vector2 box)
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
}
