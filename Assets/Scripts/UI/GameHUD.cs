using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Oyun ici ust bilgi: baglanti turu, oda kodu, oyuncu sayisi, davet ve ayril butonlari.
public class GameHUD : MonoBehaviour
{
    public TMP_Text infoText;
    public Button inviteButton;
    public Button leaveButton;

    float nextRefresh;

    void Start()
    {
        inviteButton.onClick.AddListener(() => MarketNetworkManager.Instance?.InviteFriends());
        leaveButton.onClick.AddListener(() => MarketNetworkManager.Instance?.LeaveGame());
        Refresh();
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;
        Refresh();
    }

    void Refresh()
    {
        MarketNetworkManager m = MarketNetworkManager.Instance;
        if (m == null) return;

        string role = NetworkServer.active ? "Host" : "İstemci";
        string mode = m.UsingSteam ? "Steam" : "Yerel";
        int players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None).Length;

        string text = $"{mode} - {role}\nOyuncu: {players}";

        // Item gucu (yerel oyuncu) + ortak kasa
        NetworkIdentity local = NetworkClient.localPlayer;
        PlayerInventory inv = local != null ? local.GetComponent<PlayerInventory>() : null;
        GameState gs = GameState.Instance;
        if (inv != null)
        {
            ItemRules.Stats st = ItemRules.CharacterStats(inv.equipment);
            text += $"\nItem gücü: <b>{inv.gearScore}</b>   {ItemRules.ClassName(st.weapon)}";
            text += $"\n<size=80%>Can {st.maxHp} · Zırh {st.armor} · Saldırı {st.attack}</size>";
        }
        if (gs != null) text += $"   Kasa: <color=#ffd257>{gs.gold} altın</color>";
        if (gs != null)
            text += $"\nÜn: <b>{gs.reputation:0}</b>/100   Satış: {gs.salesCount}" + (gs.lastSale > 0 ? $"  (son +{gs.lastSale})" : "");
        if (m.UsingSteam && !string.IsNullOrEmpty(m.RoomCode))
            text += $"\nOda kodu: <b>{m.RoomCode}</b>";
        text += "\n<size=70%>WASD hareket · Shift koş · Space zıpla · Tab çanta · Esc imleç</size>";
        if (Debug.isDebugBuild)
            text += "\n<size=60%><color=#9fd8ff>Geliştirici: F5 karakteri sıfırla · F6 test ekipmanı · F9 altın + taş</color></size>";
        infoText.text = text;

        inviteButton.gameObject.SetActive(m.UsingSteam && m.CurrentLobby.IsValid());
    }
}
