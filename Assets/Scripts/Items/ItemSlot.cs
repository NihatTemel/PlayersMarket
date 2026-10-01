using System.Collections.Generic;
using UnityEngine;

// Esya konabilecek nokta (raf gozu, kaide, tezgah). Sahne objesi, ag bileseni DEGIL:
// doluluk, esyalardaki WorldItem.slotId SyncVar'indan hesaplanir. id tum makinelerde ayni olmali
// (builder "RafAdi/L2/3" gibi verir). Esya, alt yuzu bu noktaya oturacak sekilde yerlesir.
public class ItemSlot : MonoBehaviour
{
    public string id;
    public ItemSize maxSize = ItemSize.Small;

    static readonly Dictionary<string, ItemSlot> all = new Dictionary<string, ItemSlot>();
    public static IEnumerable<ItemSlot> All => all.Values;

    void OnEnable()
    {
        if (string.IsNullOrEmpty(id)) id = name + "@" + transform.position.ToString("F2");
        if (all.TryGetValue(id, out ItemSlot other) && other != this)
            Debug.LogWarning($"[ItemSlot] Ayni id iki kez: {id}", this);
        all[id] = this;
    }

    void OnDisable()
    {
        if (all.TryGetValue(id, out ItemSlot s) && s == this) all.Remove(id);
    }

    public static bool TryGet(string slotId, out ItemSlot slot)
    {
        slot = null;
        return !string.IsNullOrEmpty(slotId) && all.TryGetValue(slotId, out slot) && slot != null;
    }

    public bool Accepts(ItemData data) => data != null && data.size <= maxSize;

    public bool IsFree()
    {
        foreach (WorldItem it in WorldItem.All)
            if (it.slotId == id) return false;
        return true;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = maxSize == ItemSize.Large ? new Color(1f, 0.6f, 0.1f, 0.6f) : new Color(0.2f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.05f, new Vector3(0.4f, 0.1f, 0.4f));
    }
}
