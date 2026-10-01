using System.Collections.Generic;
using UnityEngine;

// Esya kurallari tek yerde: basma (+) carpanlari, item gucu, fiyat, basma sansi/maliyeti, yuva adlari.
// Knight Online / Metin2 tarzi: tur basina sabit taban guc, demircide "+" basilarak artar.
// Basarisiz basma = esya YOK OLUR (kullanici karari). Dengeleme icin sadece buradaki tablolari degistir.
public static class ItemRules
{
    public const int MaxPlus = 10;
    public const int BagSize = 24;
    public const string UpgradeStoneId = "upgrade_stone";

    // Ekipman yuvalari (PlayerInventory.equipment sirasi)
    public static readonly EquipSlot[] EquipSlots =
    {
        EquipSlot.Weapon, EquipSlot.Head, EquipSlot.Body, EquipSlot.Hands,
        EquipSlot.Feet, EquipSlot.Necklace, EquipSlot.Earring, EquipSlot.Ring,
    };

    //                                       +0    +1    +2    +3    +4    +5    +6    +7    +8    +9    +10
    static readonly float[] PowerMult = { 1.00f, 1.06f, 1.13f, 1.21f, 1.30f, 1.40f, 1.52f, 1.66f, 1.82f, 2.00f, 2.25f };
    static readonly float[] ValueMult = { 1.0f, 1.3f, 1.7f, 2.2f, 2.9f, 3.8f, 5.0f, 6.8f, 9.5f, 13.5f, 20f };
    // +N -> +N+1 basari sansi (index = mevcut seviye). Orta zorluk: +1..+3 guvenli, sonra hizla duser.
    //                                         +1     +2     +3     +4     +5     +6     +7     +8     +9    +10
    static readonly float[] UpgradeChance = { 1.00f, 1.00f, 1.00f, 0.85f, 0.75f, 0.62f, 0.50f, 0.38f, 0.28f, 0.20f };

    // Demirci orsu: 8 slot, her basmada biri rastgele "kesin gecis" slotu (esya oradaysa sans bakilmaz)
    public const int AnvilSlots = 8;
    public static float ChanceWithLuckySlot(ItemStack s)
    {
        float c = UpgradeSuccessChance(s);
        return c + (1f - c) / AnvilSlots;
    }

    public static int SlotIndex(EquipSlot slot) => System.Array.IndexOf(EquipSlots, slot);

    static int P(ItemStack s) => Mathf.Clamp(s.plus, 0, MaxPlus);

    public static int Power(ItemStack s)
    {
        ItemData d = s.Data;
        return d == null || !d.IsEquipment ? 0 : Mathf.RoundToInt(d.basePower * PowerMult[P(s)]);
    }

    // Tek adet satis degeri (dukkan carpani dahil)
    public static int UnitValue(ItemStack s, float shopMultiplier = 1f)
    {
        ItemData d = s.Data;
        if (d == null) return 0;
        return Mathf.Max(1, Mathf.RoundToInt(d.baseValue * ValueMult[P(s)] * shopMultiplier));
    }

    // Ekipman item gucu: takili esyalarin gucu toplami / yuva sayisi (bos yuva = 0)
    public static int GearScore(IList<ItemStack> equipment)
    {
        int sum = 0;
        foreach (ItemStack s in equipment) sum += Power(s);
        return Mathf.RoundToInt(sum / (float)EquipSlots.Length);
    }

    public static bool CanUpgrade(ItemStack s) => s.Data != null && s.Data.Upgradeable && s.plus < MaxPlus;
    public static float UpgradeSuccessChance(ItemStack s) => UpgradeChance[Mathf.Clamp(s.plus, 0, MaxPlus - 1)];
    public static int UpgradeGold(ItemStack s)
    {
        ItemData d = s.Data;
        int n = s.plus + 1;
        return d == null ? 0 : Mathf.Max(10, Mathf.RoundToInt(d.baseValue * 0.4f * n * n));
    }
    public static int UpgradeStones(ItemStack s) => s.plus < 4 ? 1 : s.plus < 7 ? 2 : 3;

    public static string DisplayName(ItemStack s)
    {
        ItemData d = s.Data;
        string name = d != null ? d.displayName : s.id;
        // Basilabilir esya (ekipman) +0 dahil her zaman seviyesiyle gorunur
        return (d != null && d.Upgradeable) || s.plus > 0 ? $"{name} +{s.plus}" : name;
    }

    // ================= Karakter statlari (esyadan otomatik) =================
    // Her esyaya ayri stat yazilmaz: item gucu x yuva katsayisi. "+" basmak her seyi orantili buyutur.
    // Sinif = elindeki silah: Kilic=Savasci, Hancer=Hancerci, Yay=Okcu, Asa=Buyucu. Ayni item gucunde
    // saniyedeki hasar yakin (±%15); fark risk/odul: hancer yakin+yuksek, yay uzak+dusuk, asa alan.

    public struct Stats
    {
        public int maxHp, armor, attack;
        public float crit;          // 0..1, kritik = 1.5x hasar
        public WeaponType weapon;   // sinif
    }

    public const int BaseHp = 100;
    public const float BaseCrit = 0.03f;
    public const int FistAttack = 5;

    public static float WeaponDamageFactor(WeaponType w) => w switch
    {
        WeaponType.Sword => 1.0f,
        WeaponType.Dagger => 0.75f,
        WeaponType.Bow => 0.85f,
        WeaponType.Staff => 1.15f,
        _ => 0.5f,
    };

    public static float AttacksPerSecond(WeaponType w) => w switch
    {
        WeaponType.Sword => 1.0f,
        WeaponType.Dagger => 1.6f,
        WeaponType.Bow => 0.9f,
        WeaponType.Staff => 0.75f,
        _ => 1.0f,
    };

    public static string ClassName(WeaponType w) => w switch
    {
        WeaponType.Sword => "Savaşçı",
        WeaponType.Dagger => "Hançerci",
        WeaponType.Bow => "Okçu",
        WeaponType.Staff => "Büyücü",
        _ => "Yumruk",
    };

    public static string WeaponTypeName(WeaponType w) => w switch
    {
        WeaponType.Sword => "Kılıç",
        WeaponType.Dagger => "Hançer",
        WeaponType.Bow => "Yay",
        WeaponType.Staff => "Asa",
        _ => "",
    };

    public static string ClassPassive(WeaponType w) => w switch
    {
        WeaponType.Sword => "Zırh +%20",
        WeaponType.Dagger => "Kritik +%10",
        WeaponType.Bow => "Uzaktan hasar +%10",
        WeaponType.Staff => "Skill bekleme −%15",
        _ => "",
    };

    // Tek esyanin katkisi
    public static Stats ItemStats(ItemStack s)
    {
        var st = new Stats();
        ItemData d = s.Data;
        if (d == null || !d.IsEquipment) return st;
        float p = Power(s);
        switch (d.equipSlot)
        {
            case EquipSlot.Weapon:
                st.attack = Mathf.RoundToInt(p * WeaponDamageFactor(d.weaponType));
                st.weapon = d.weaponType;
                break;
            case EquipSlot.Head: st.armor = R(p * 0.25f); st.maxHp = R(p * 0.3f); break;
            case EquipSlot.Body: st.armor = R(p * 0.45f); st.maxHp = R(p * 0.6f); break;
            case EquipSlot.Hands: st.armor = R(p * 0.15f); st.attack = R(p * 0.1f); break;
            case EquipSlot.Feet: st.armor = R(p * 0.15f); st.maxHp = R(p * 0.2f); break;
            case EquipSlot.Necklace: st.maxHp = R(p * 0.8f); break;
            case EquipSlot.Earring: st.attack = R(p * 0.15f); st.crit = p * 0.0004f; break;
            case EquipSlot.Ring: st.crit = p * 0.0008f; st.maxHp = R(p * 0.2f); break;
        }
        return st;
    }

    static int R(float v) => Mathf.RoundToInt(v);

    // Tum ekipman + taban + sinif pasifi
    public static Stats CharacterStats(IList<ItemStack> equipment)
    {
        var t = new Stats { maxHp = BaseHp, crit = BaseCrit };
        foreach (ItemStack s in equipment)
        {
            Stats i = ItemStats(s);
            t.maxHp += i.maxHp;
            t.armor += i.armor;
            t.attack += i.attack;
            t.crit += i.crit;
            if (i.weapon != WeaponType.None) t.weapon = i.weapon;
        }
        if (t.weapon == WeaponType.Sword) t.armor = R(t.armor * 1.2f);
        if (t.weapon == WeaponType.Dagger) t.crit += 0.10f;
        if (t.weapon == WeaponType.None) t.attack += FistAttack;
        t.crit = Mathf.Clamp(t.crit, 0f, 0.75f);
        return t;
    }

    // Alinan hasar carpani: 100 / (100 + zirh)
    public static float DamageTakenMultiplier(int armor) => 100f / (100f + Mathf.Max(0, armor));

    // Tahmini saniyedeki hasar (kritik ortalamasi dahil)
    public static float Dps(Stats s) => s.attack * AttacksPerSecond(s.weapon) * (1f + s.crit * 0.5f);

    // Detay panelinde esyanin stat satiri
    public static string StatLine(ItemStack s)
    {
        Stats i = ItemStats(s);
        var parts = new List<string>();
        if (i.attack > 0) parts.Add($"+{i.attack} Saldırı");
        if (i.armor > 0) parts.Add($"+{i.armor} Zırh");
        if (i.maxHp > 0) parts.Add($"+{i.maxHp} Can");
        if (i.crit > 0f) parts.Add($"+%{i.crit * 100f:0.#} Kritik");
        return string.Join("  ·  ", parts);
    }

    public static string CategoryName(ItemCategory c) => c switch
    {
        ItemCategory.Material => "Malzeme",
        ItemCategory.Weapon => "Silah",
        ItemCategory.Armor => "Zırh",
        ItemCategory.Accessory => "Takı",
        ItemCategory.Consumable => "Tüketilebilir",
        _ => "Ticari mal",
    };

    public static string SlotName(EquipSlot s) => s switch
    {
        EquipSlot.Weapon => "Silah",
        EquipSlot.Head => "Kafa",
        EquipSlot.Body => "Gövde",
        EquipSlot.Hands => "Eldiven",
        EquipSlot.Feet => "Bot",
        EquipSlot.Necklace => "Kolye",
        EquipSlot.Earring => "Küpe",
        EquipSlot.Ring => "Yüzük",
        _ => "",
    };
}
