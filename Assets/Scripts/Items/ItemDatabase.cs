using System.Collections.Generic;
using UnityEngine;

// Tum esya tanimlari. Resources/ItemDatabase.asset olarak durur; koddan ItemDatabase.Get(id).
[CreateAssetMenu(menuName = "PlayersMarket/Item Database", fileName = "ItemDatabase")]
public class ItemDatabase : ScriptableObject
{
    public List<ItemData> items = new List<ItemData>();
    [Tooltip("Modeli olmayan esyalarin yedek sekli icin (URP Lit).")]
    public Material fallbackMaterial;

    static ItemDatabase instance;
    Dictionary<string, ItemData> map;

    public static ItemDatabase Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<ItemDatabase>("ItemDatabase");
                if (instance == null) Debug.LogError("[ItemDatabase] Resources/ItemDatabase.asset bulunamadi.");
            }
            return instance;
        }
    }

    public static ItemData Get(string id)
    {
        ItemDatabase db = Instance;
        if (db == null || string.IsNullOrEmpty(id)) return null;
        if (db.map == null)
        {
            db.map = new Dictionary<string, ItemData>();
            foreach (ItemData d in db.items)
                if (d != null && !string.IsNullOrEmpty(d.id)) db.map[d.id] = d;
        }
        db.map.TryGetValue(id, out ItemData data);
        return data;
    }

    void OnValidate()
    {
        map = null; // editorde liste degisince yeniden kur
    }
}
