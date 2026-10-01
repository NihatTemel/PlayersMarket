using UnityEngine;

public enum CustomerType { Villager, Adventurer, Noble }

public enum PriceVerdict
{
    Cheap,      // ucuz: hemen alir, un artar
    Fair,       // normal alir
    Haggle,     // pahali: kasada pazarlik teklif eder
    TooPricey,  // cok pahali: almaz, un duser
}

// Dukkan fiyat / musteri kurallari tek yerde (dengeleme sadece burada).
// Model (kullanici karari, "karma"): etiket piyasa fiyatiyla gelir; oyuncu %50-%200 arasi oran secer.
// Musteri tepkisi = fiyat / piyasa orani; esikler musteri tipinin toleransiyla carpilir.
public static class PricingRules
{
    public const float MinRatio = 0.5f, MaxRatio = 2.0f;
    public static readonly float[] QuickRatios = { 0.8f, 0.9f, 1.0f, 1.1f, 1.25f, 1.5f };

    // Normal musteri icin esikler (tolerans 1.0)
    const float CheapMax = 0.9f;
    const float FairMax = 1.1f;
    const float HaggleMax = 1.35f;

    // Pazarlik reddedilince yine de etiket fiyatina alma sansi
    public const float BuyAfterRejectChance = 0.4f;

    public static float ClampRatio(float r) => Mathf.Clamp(Mathf.Round(r * 100f) / 100f, MinRatio, MaxRatio);

    // Piyasa fiyati (1 adet): taban deger x basma carpani x dukkan carpani
    public static int MarketPrice(ItemStack s) => ItemRules.UnitValue(s, GameState.CurrentPriceMultiplier);

    public static int TagPrice(ItemStack s, float ratio) => Mathf.Max(1, Mathf.RoundToInt(MarketPrice(s) * ClampRatio(ratio)));

    public static float Tolerance(CustomerType t, ItemData d)
    {
        float tol = t switch
        {
            CustomerType.Villager => 0.95f,
            CustomerType.Adventurer => 1.0f,
            CustomerType.Noble => 1.3f,
            _ => 1f,
        };
        // Maceraci ekipmana daha cok ister
        if (t == CustomerType.Adventurer && d != null && d.IsEquipment) tol += 0.15f;
        return tol;
    }

    public static PriceVerdict Judge(float ratio, float tolerance)
    {
        if (ratio <= CheapMax * tolerance) return PriceVerdict.Cheap;
        if (ratio <= FairMax * tolerance) return PriceVerdict.Fair;
        if (ratio <= HaggleMax * tolerance) return PriceVerdict.Haggle;
        return PriceVerdict.TooPricey;
    }

    // Pazarlik teklifi: piyasa ile etiket arasi (piyasaya yakin)
    public static int HaggleOffer(int market, int tag) =>
        Mathf.Clamp(Mathf.RoundToInt(market * Random.Range(1.0f, 1.08f)), 1, Mathf.Max(1, tag - 1));

    // Un (0..100) degisimleri
    public const float RepCheap = 1.5f, RepFair = 0.5f, RepHaggleAccepted = 0.5f, RepHaggleRejectedLeft = -1f,
        RepTooPricey = -2f, RepNotServed = -3f, RepEmptyShop = -0.5f;

    // Musteri gelme araligi (sn): un ve dukkan seviyesiyle kisalir
    public static float SpawnInterval(float reputation, int shopLevel) =>
        Mathf.Lerp(60f, 15f, Mathf.Clamp01(reputation / 100f)) / (1f + 0.15f * (shopLevel - 1));

    public static int MaxCustomers(int shopLevel) => 2 + shopLevel;

    // Sadece gezip cikan (hic almayan) musteri orani
    public static float WindowShopChance(CustomerType t) => t switch
    {
        CustomerType.Adventurer => 0.35f,
        CustomerType.Noble => 0.25f,
        _ => 0.45f,
    };

    // Fiyat uygun olsa da her zaman almaz (almazsa baska urune bakar)
    public static float BuyChance(PriceVerdict v) => v switch
    {
        PriceVerdict.Cheap => 0.95f,
        PriceVerdict.Fair => 0.7f,
        PriceVerdict.Haggle => 0.85f,
        _ => 0f,
    };

    // Bir musterinin bakacagi en fazla urun sayisi
    public static int RollLooks() => Random.Range(1, 4);

    // Un yukseldikce soylu artar
    public static CustomerType RollType(float reputation)
    {
        float noble = Mathf.Lerp(0.03f, 0.2f, reputation / 100f);
        float r = Random.value;
        if (r < noble) return CustomerType.Noble;
        if (r < noble + 0.3f) return CustomerType.Adventurer;
        return CustomerType.Villager;
    }

    public static string TypeName(CustomerType t) => t switch
    {
        CustomerType.Adventurer => "Maceracı",
        CustomerType.Noble => "Soylu",
        _ => "Köylü",
    };

    // Fiyat panelinde normal musteri icin beklenen tepki
    public static string VerdictText(PriceVerdict v) => v switch
    {
        PriceVerdict.Cheap => "<color=#7CFC7C>Hemen satılır · ün artar</color>",
        PriceVerdict.Fair => "<color=#ffffff>Normal satılır</color>",
        PriceVerdict.Haggle => "<color=#ffd257>Pazarlık ister</color>",
        _ => "<color=#ff5050>Satılmaz · ün düşer</color>",
    };

    public static Color RatioColor(float ratio)
    {
        PriceVerdict v = Judge(ratio, 1f);
        return v switch
        {
            PriceVerdict.Cheap => new Color(0.5f, 1f, 0.5f),
            PriceVerdict.Fair => Color.white,
            PriceVerdict.Haggle => new Color(1f, 0.82f, 0.35f),
            _ => new Color(1f, 0.35f, 0.3f),
        };
    }
}
