using Mirror;
using UnityEngine;

// Egitim kuklasi (ag objesi, Resources/Network/TrainingDummy). Sunucu GameState ile TrainingDummyPoint'lerde dogurur.
// 3 sn vurulmazsa can dolar; can biterse "devrilir" ve dolup kalkar. Son 5 sn'deki saniyedeki hasari gosterir
// (silah/skill dengesini denemek icin).
public class TrainingDummy : NetworkBehaviour, IDamageable
{
    public int maxHp = 1000;
    public int armor = 20;
    public float regenDelay = 3f;

    [SyncVar] public int hp = 1000;
    [SyncVar] public float dps;           // son 5 sn ortalama (sunucu hesaplar)

    float lastHitTime = -10f;
    float windowStart;
    int windowDamage;
    GUIStyle style;

    public int Armor => armor;
    public bool IsAlive => true;
    public Transform Transform => transform;

    public override void OnStartServer()
    {
        hp = maxHp;
        windowStart = Time.time;
    }

    [Server]
    public void ServerTakeDamage(int amount, bool crit, uint attackerNetId, Vector3 hitPoint)
    {
        if (amount <= 0) return;
        hp -= amount;
        lastHitTime = Time.time;
        windowDamage += amount;
        if (hp <= 0) hp = maxHp; // devrildi: hemen dolar
        RpcHit(amount, crit, hitPoint);
    }

    [ClientRpc]
    void RpcHit(int amount, bool crit, Vector3 point)
    {
        try
        {
            DamageNumbers.Show(point, amount, crit);
            Shake();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e); // RPC'de istisna baglantiyi koparir
        }
    }

    float shakeUntil;
    Vector3 basePos;
    void Shake()
    {
        if (shakeUntil < Time.time) basePos = transform.GetChild(0).localPosition;
        shakeUntil = Time.time + 0.15f;
    }

    void Update()
    {
        // Sallanma (sadece gorsel, govde cocugu)
        if (transform.childCount > 0)
        {
            Transform body = transform.GetChild(0);
            body.localPosition = Time.time < shakeUntil
                ? basePos + new Vector3(Mathf.Sin(Time.time * 80f) * 0.05f, 0f, 0f)
                : (shakeUntil > 0f ? basePos : body.localPosition);
        }

        if (!isServer) return;
        if (hp < maxHp && Time.time - lastHitTime > regenDelay) hp = maxHp;
        if (Time.time - windowStart >= 5f)
        {
            dps = windowDamage / (Time.time - windowStart);
            windowDamage = 0;
            windowStart = Time.time;
        }
    }

    void OnGUI()
    {
        Camera c = Camera.main;
        if (c == null) return;
        Vector3 top = transform.position + Vector3.up * 2.3f;
        if ((top - c.transform.position).sqrMagnitude > 25f * 25f) return;
        Vector3 p = c.WorldToScreenPoint(top);
        if (p.z < 0f) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, richText = true };
            style.normal.textColor = Color.white;
        }
        float y = Screen.height - p.y;
        const float w = 110f;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(p.x - w * 0.5f, y, w, 8f), Texture2D.whiteTexture);
        GUI.color = new Color(0.9f, 0.3f, 0.25f, 0.9f);
        GUI.DrawTexture(new Rect(p.x - w * 0.5f, y, w * Mathf.Clamp01(hp / (float)maxHp), 8f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(p.x - 90f, y - 20f, 180f, 20f), $"Eğitim Kuklası  <color=#ffd257>{dps:0} hasar/sn</color>", style);
    }
}
