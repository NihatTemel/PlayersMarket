using System.Collections.Generic;
using UnityEngine;

public enum NpcType
{
    Blacksmith, // demirci: + basma
    Vendor,     // satici (ileride)
    ShopLedger, // dukkan defteri: gelistirme (ileride)
}

// Etkilesimli NPC / istasyon (sahne objesi, ag bileseni DEGIL). Yerel oyuncu yakindayken
// PlayerCarry "[E] ..." gosterir ve ilgili paneli acar. Sunucu Command'larda NpcStation.Nearest ile
// oyuncunun gercekten yakinda oldugunu dogrular.
public class NpcStation : MonoBehaviour
{
    public NpcType type = NpcType.Blacksmith;
    public string displayName = "Demirci";
    public float interactRadius = 2.8f;

    static readonly List<NpcStation> all = new List<NpcStation>();

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    public string Prompt => type switch
    {
        NpcType.Blacksmith => $"{displayName}: eşya yükselt (+ bas)",
        NpcType.Vendor => $"{displayName}: alışveriş",
        NpcType.ShopLedger => $"{displayName}: dükkanı geliştir",
        _ => displayName,
    };

    // Oyuncunun onunde (genis aci) ve menzildeki en yakin istasyon
    public static NpcStation FindInteractable(Vector3 origin, Vector3 flatForward)
    {
        NpcStation best = null;
        float bestDist = float.MaxValue;
        foreach (NpcStation s in all)
        {
            Vector3 to = s.transform.position - origin;
            to.y = 0f;
            float d = to.magnitude;
            if (d > s.interactRadius || d >= bestDist) continue;
            if (d > 0.8f && Vector3.Angle(flatForward, to) > 80f) continue;
            best = s;
            bestDist = d;
        }
        return best;
    }

    // Sunucu dogrulamasi: verilen turden, pos'a maxDistance icindeki en yakin istasyon
    public static NpcStation Nearest(NpcType type, Vector3 pos, float maxDistance)
    {
        NpcStation best = null;
        float bestDist = maxDistance;
        foreach (NpcStation s in all)
        {
            if (s.type != type) continue;
            Vector3 to = s.transform.position - pos;
            to.y = 0f;
            float d = to.magnitude;
            if (d <= bestDist) { best = s; bestDist = d; }
        }
        return best;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, interactRadius);
    }
}
