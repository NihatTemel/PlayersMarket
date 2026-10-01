using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Ana menu: Steam host/join (oda kodu) ve yerel host/join (test).
// Butonlar MarketNetworkManager.Instance'i cagirir; yonetici DDOL oldugu icin
// referansi sahneye degil, her tiklamada singleton'a bakiyoruz.
public class MainMenuUI : MonoBehaviour
{
    [Header("Steam")]
    public Button hostSteamButton;
    public Button joinSteamButton;
    public TMP_InputField roomCodeInput;

    [Header("Yerel")]
    public Button hostLocalButton;
    public Button joinLocalButton;
    public TMP_InputField addressInput;

    [Header("Bilgi")]
    public TMP_Text statusText;
    public TMP_Text steamText;

    void Start()
    {
        hostSteamButton.onClick.AddListener(() => Manager()?.HostSteam());
        joinSteamButton.onClick.AddListener(() => Manager()?.JoinSteamByCode(roomCodeInput.text));
        hostLocalButton.onClick.AddListener(() => Manager()?.HostLocal());
        joinLocalButton.onClick.AddListener(() => Manager()?.JoinLocal(addressInput.text));

        MarketNetworkManager m = Manager();
        bool steam = m != null && m.SteamReady;
        hostSteamButton.interactable = steam;
        joinSteamButton.interactable = steam;
        roomCodeInput.interactable = steam;

        if (steamText != null)
            steamText.text = steam
                ? "Steam: " + Steamworks.SteamFriends.GetPersonaName()
                : "Steam kapalı: sadece yerel test";

        MarketNetworkManager.StatusChanged += OnStatus;
        OnStatus(MarketNetworkManager.LastStatus);
    }

    void OnDestroy()
    {
        MarketNetworkManager.StatusChanged -= OnStatus;
    }

    void OnStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    static MarketNetworkManager Manager()
    {
        MarketNetworkManager m = MarketNetworkManager.Instance;
        if (m == null) Debug.LogError("[MainMenuUI] MarketNetworkManager bulunamadi.");
        return m;
    }
}
