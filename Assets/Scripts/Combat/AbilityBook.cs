using UnityEngine;

public enum AbilityKind
{
    Melee,      // onundeki yaya vurur (range + angle)
    Projectile, // mermi (ok / buyu); radius > 0 ise carptigi yerde alan hasari
    Spin,       // etrafindaki herkese (radius)
    Dash,       // ileri atilir, yol boyunca vurur (moveDistance, radius)
    Blink,      // ileri isinlanir, vardigi yerde vurur
    LeapBack,   // geri sicrar + ileri mermi atar
    Nova,       // etrafinda halka (radius), Spin ile ayni ama buyu
}

// Bir yetenek (duz vurus ya da skill). Hasar = karakter saldirisi x damage.
public class AbilityDef
{
    public string name;
    public string shortName;   // skill cubugunda
    public AbilityKind kind;
    public float cooldown;     // sn (duz vurusta = 1 / saldiri hizi)
    public float damage = 1f;
    public float range = 2f;
    public float angle = 100f;
    public float radius;
    public int projectiles = 1;
    public float spread;       // coklu mermide toplam aci
    public float speed = 30f;
    public float moveDistance;
    public bool backstab;      // arkadan vurursa x2
    public float splashFactor = 1f; // alan hasarinda ana hedef disindakilere carpan
    public Color color = Color.white;
}

// Tum silah yetenekleri tek yerde (dengeleme burada). index 0 = duz vurus (R / 1), 1 = skill (2), 2 = skill (3).
// Denge: ayni item gucunde duz vurus saniyedeki hasari ±%15 (ItemRules.WeaponDamageFactor x AttacksPerSecond);
// fark risk/odul: hancer yakin+yuksek, yay uzak+dusuk, asa alan, kilic dengeli+dayanikli.
public static class AbilityBook
{
    public const float RangedPassive = 1.10f;    // Okcu: uzaktan hasar +%10
    public const float StaffCooldownMult = 0.85f; // Buyucu: skill bekleme −%15
    public const float CritMultiplier = 1.5f;
    public const float SwordComboMultiplier = 1.5f; // kilicta her 3. duz vurus

    static readonly Color Steel = new Color(0.85f, 0.9f, 1f);

    static readonly AbilityDef[] Sword =
    {
        new AbilityDef { name = "Kılıç Darbesi", shortName = "Darbe", kind = AbilityKind.Melee, cooldown = 1f / ItemRules.AttacksPerSecond(WeaponType.Sword),
                         damage = 1f, range = 2.4f, angle = 110f, color = Steel },
        new AbilityDef { name = "Dönen Kılıç", shortName = "Dönen", kind = AbilityKind.Spin, cooldown = 6f,
                         damage = 1.6f, radius = 3f, color = new Color(1f, 0.85f, 0.4f) },
        new AbilityDef { name = "Atılma", shortName = "Atılma", kind = AbilityKind.Dash, cooldown = 8f,
                         damage = 1.4f, moveDistance = 5f, radius = 1.4f, color = new Color(1f, 0.6f, 0.3f) },
    };

    static readonly AbilityDef[] Dagger =
    {
        new AbilityDef { name = "Hançer Darbesi", shortName = "Darbe", kind = AbilityKind.Melee, cooldown = 1f / ItemRules.AttacksPerSecond(WeaponType.Dagger),
                         damage = 1f, range = 1.9f, angle = 80f, color = Steel },
        new AbilityDef { name = "Arkadan Vuruş", shortName = "Arkadan", kind = AbilityKind.Melee, cooldown = 7f,
                         damage = 2.2f, range = 2.1f, angle = 60f, backstab = true, color = new Color(0.8f, 0.3f, 0.9f) },
        new AbilityDef { name = "Gölge Adımı", shortName = "Gölge", kind = AbilityKind.Blink, cooldown = 9f,
                         damage = 0.8f, moveDistance = 6f, radius = 1.8f, color = new Color(0.4f, 0.2f, 0.6f) },
    };

    static readonly AbilityDef[] Bow =
    {
        new AbilityDef { name = "Ok", shortName = "Ok", kind = AbilityKind.Projectile, cooldown = 1f / ItemRules.AttacksPerSecond(WeaponType.Bow),
                         damage = 1f, range = 26f, speed = 38f, color = new Color(0.9f, 0.8f, 0.6f) },
        new AbilityDef { name = "Çoklu Atış", shortName = "Çoklu", kind = AbilityKind.Projectile, cooldown = 6f,
                         damage = 0.8f, range = 22f, speed = 38f, projectiles = 3, spread = 24f, color = new Color(0.6f, 1f, 0.6f) },
        new AbilityDef { name = "Geri Sıçrama", shortName = "Sıçrama", kind = AbilityKind.LeapBack, cooldown = 9f,
                         damage = 1.2f, range = 24f, speed = 38f, moveDistance = 5f, color = new Color(0.5f, 0.8f, 1f) },
    };

    static readonly AbilityDef[] Staff =
    {
        new AbilityDef { name = "Büyü Topu", shortName = "Büyü", kind = AbilityKind.Projectile, cooldown = 1f / ItemRules.AttacksPerSecond(WeaponType.Staff),
                         damage = 1f, range = 20f, speed = 18f, radius = 1.5f, splashFactor = 0.5f, color = new Color(0.6f, 0.5f, 1f) },
        new AbilityDef { name = "Ateş Topu", shortName = "Ateş", kind = AbilityKind.Projectile, cooldown = 8f,
                         damage = 2.2f, range = 22f, speed = 16f, radius = 3.5f, splashFactor = 1f, color = new Color(1f, 0.45f, 0.15f) },
        new AbilityDef { name = "Buz Halkası", shortName = "Buz", kind = AbilityKind.Nova, cooldown = 10f,
                         damage = 1.2f, radius = 4.5f, color = new Color(0.55f, 0.9f, 1f) },
    };

    static readonly AbilityDef[] Fists =
    {
        new AbilityDef { name = "Yumruk", shortName = "Yumruk", kind = AbilityKind.Melee, cooldown = 1f,
                         damage = 1f, range = 1.6f, angle = 80f, color = Color.white },
    };

    public static AbilityDef[] For(WeaponType w) => w switch
    {
        WeaponType.Sword => Sword,
        WeaponType.Dagger => Dagger,
        WeaponType.Bow => Bow,
        WeaponType.Staff => Staff,
        _ => Fists,
    };

    public static AbilityDef Get(WeaponType w, int index)
    {
        AbilityDef[] set = For(w);
        return index >= 0 && index < set.Length ? set[index] : null;
    }

    // Sinif pasifi dahil gercek bekleme
    public static float Cooldown(WeaponType w, int index)
    {
        AbilityDef a = Get(w, index);
        if (a == null) return 0f;
        return w == WeaponType.Staff && index > 0 ? a.cooldown * StaffCooldownMult : a.cooldown;
    }

    public static bool IsRanged(AbilityKind k) => k == AbilityKind.Projectile || k == AbilityKind.LeapBack;
}
