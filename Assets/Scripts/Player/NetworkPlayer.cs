using Mirror;
using Steamworks;
using UnityEngine;

// Oyuncu kimligi: Steam adi + oyuncu rengi (SyncVar), basin ustunde isim etiketi.
// Hareket/kamera PlayerMovement + ThirdPersonCamera'da.
public class NetworkPlayer : NetworkBehaviour
{
    [SyncVar] public string displayName = "";
    [SyncVar(hook = nameof(OnColorChanged))] public Color bodyColor = Color.white;

    public Renderer bodyRenderer;
    public float nameHeight = 2.4f;

    GUIStyle nameStyle;

    public override void OnStartServer()
    {
        // Her oyuncuya netId'den farkli bir renk
        bodyColor = Color.HSVToRGB((netId * 0.17f) % 1f, 0.65f, 0.95f);
    }

    public override void OnStartClient()
    {
        ApplyColor(bodyColor);
        gameObject.name = $"Player [{netId}]";
    }

    public override void OnStartLocalPlayer()
    {
        CmdSetName(SteamManager.Initialized ? SteamFriends.GetPersonaName() : "");
    }

    [Command]
    void CmdSetName(string n)
    {
        n = (n ?? "").Trim();
        if (n.Length == 0) n = "Oyuncu " + netId;
        if (n.Length > 24) n = n.Substring(0, 24);
        displayName = n;
    }

    void OnColorChanged(Color oldColor, Color newColor) => ApplyColor(newColor);

    void ApplyColor(Color c)
    {
        if (bodyRenderer != null) bodyRenderer.material.color = c;
    }

    // Basin ustunde isim (gecici, IMGUI). Yerel oyuncunun kendi adi gosterilmez.
    void OnGUI()
    {
        if (isLocalPlayer || string.IsNullOrEmpty(displayName)) return;
        Camera c = Camera.main;
        if (c == null) return;

        Vector3 p = c.WorldToScreenPoint(transform.position + Vector3.up * nameHeight);
        if (p.z < 0f) return;

        if (nameStyle == null)
        {
            nameStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold
            };
            nameStyle.normal.textColor = Color.white;
        }
        GUI.Label(new Rect(p.x - 100f, Screen.height - p.y - 15f, 200f, 30f), displayName, nameStyle);
    }
}
