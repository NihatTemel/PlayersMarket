using UnityEngine;

// Satici (NPC) stogu ve fiyatlari. Satis fiyati = taban deger x carpan (dukkan fiyat carpanindan BAGIMSIZ):
// saticidan alip dukkanda satarak kar edilemesin diye piyasanin ustunde.
public static class VendorRules
{
    public const float Markup = 2f;

    // Baslangic ekipmanlari (her sinif icin bir silah) + basma tasi
    public static readonly string[] StarterStock =
    {
        "sword_rusty", "dagger_rusty", "bow_short", "staff_apprentice",
        "helm_leather", "armor_leather", "gloves_leather", "boots_leather",
        "necklace_copper", "earring_copper", "ring_copper",
        ItemRules.UpgradeStoneId,
    };

    public static bool Sells(string id) => System.Array.IndexOf(StarterStock, id) >= 0;

    public static int Price(string id)
    {
        ItemData d = ItemDatabase.Get(id);
        return d == null ? 0 : Mathf.Max(1, Mathf.RoundToInt(d.baseValue * Markup));
    }
}
