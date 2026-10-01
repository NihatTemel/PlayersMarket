using System;
using System.Collections.Generic;
using System.IO;
using Steamworks;
using UnityEngine;

// Karakter kaydi (envanter + ekipman) OYUNCUNUN bilgisayarinda: her dunyaya ayni karakterle girilir.
// Dosya: persistentDataPath/characters/<steamId|local>[_editor].json
// (editor ve build ayri dosya: ayni bilgisayarda iki kopyayla test ederken birbirini ezmesin)
public static class CharacterSave
{
    [Serializable]
    class SlotDto
    {
        public string id;
        public int plus;
        public int count;
    }

    [Serializable]
    class CharacterFile
    {
        public int version = 1;
        public List<SlotDto> bag = new List<SlotDto>();
        public List<SlotDto> equipment = new List<SlotDto>();
    }

    static string FilePath
    {
        get
        {
            string key = "local";
            try
            {
                if (SteamManager.Initialized) key = SteamUser.GetSteamID().m_SteamID.ToString();
            }
            catch (Exception)
            {
                // Steam yoksa yerel
            }
            if (Application.isEditor) key += "_editor";
            return Path.Combine(Application.persistentDataPath, "characters", key + ".json");
        }
    }

    public static bool TryLoad(out ItemStack[] bag, out ItemStack[] equipment)
    {
        bag = null;
        equipment = null;
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return false;
            CharacterFile f = JsonUtility.FromJson<CharacterFile>(File.ReadAllText(path));
            if (f == null) return false;
            bag = ToStacks(f.bag);
            equipment = ToStacks(f.equipment);
            Debug.Log($"[CharacterSave] Karakter yuklendi: {path}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CharacterSave] Karakter okunamadi, yeni karakter: " + e.Message);
            return false;
        }
    }

    public static string ToJson(IList<ItemStack> bag, IList<ItemStack> equipment)
    {
        var f = new CharacterFile();
        foreach (ItemStack s in bag) f.bag.Add(ToDto(s));
        foreach (ItemStack s in equipment) f.equipment.Add(ToDto(s));
        return JsonUtility.ToJson(f, true);
    }

    public static bool Write(string json)
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CharacterSave] Karakter kaydedilemedi: " + e.Message);
            return false;
        }
    }

    static SlotDto ToDto(ItemStack s) =>
        s.IsEmpty ? new SlotDto { id = "", plus = 0, count = 0 } : new SlotDto { id = s.id, plus = s.plus, count = s.count };

    static ItemStack[] ToStacks(List<SlotDto> list)
    {
        if (list == null) return Array.Empty<ItemStack>();
        var result = new ItemStack[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            SlotDto d = list[i];
            result[i] = d == null || string.IsNullOrEmpty(d.id) ? ItemStack.Empty : new ItemStack(d.id, d.count, d.plus);
        }
        return result;
    }
}
