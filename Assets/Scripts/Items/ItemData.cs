using UnityEngine;

public enum ItemSize
{
    Small, // tek elle / envantere girer, raflara sigar
    Large, // iki elle, envantere GIRMEZ, yavaslatir, ziplatmaz; rafa degil kaideye/yere
}

public enum FallbackShape { Cube, Sphere, Cylinder, Capsule }

public enum ItemCategory { TradeGood, Material, Weapon, Armor, Accessory, Consumable }

// Ekipman yuvalari. Sira = PlayerInventory.equipment indeksi + 1 (None haric). SONA EKLE.
public enum EquipSlot { None, Weapon, Head, Body, Hands, Feet, Necklace, Earring, Ring }

// Silah turu = oyuncunun o anki sinifi (Kilic=Savasci, Hancer=Hancerci, Yay=Okcu, Asa=Buyucu). SONA EKLE.
public enum WeaponType { None, Sword, Bow, Dagger, Staff }

// Bir esya turunun tanimi. Sahnedeki/envanterdeki her esya = bu verinin id'si + "+" seviyesi + adet (ItemStack).
// Gorsel: visualPrefab (sanat paketinden model, NetworkIdentity/collider gerekmez); yoksa yedek sekil.
[CreateAssetMenu(menuName = "PlayersMarket/Item Data", fileName = "Item_")]
public class ItemData : ScriptableObject
{
    [Tooltip("Kalici kimlik (agda ve kayitta bu gider). Sonradan DEGISTIRME.")]
    public string id;
    public string displayName;
    [Tooltip("Gizli taban deger (altin). Satis fiyati = bu x basma carpani x dukkan carpani.")]
    public int baseValue = 10;
    public ItemSize size = ItemSize.Small;
    public float mass = 1f;
    [Tooltip("Dusunce/carpinca deger kaybeder (ileride).")]
    public bool fragile;

    [Header("Tur")]
    public ItemCategory category = ItemCategory.TradeGood;
    public EquipSlot equipSlot = EquipSlot.None;
    public WeaponType weaponType = WeaponType.None;
    [Tooltip("Ekipmanin taban item gucu. Basma (+) bunu carpar (ItemRules).")]
    public int basePower;
    [Tooltip("Envanterde ayni slotta en fazla kac tane (1 = yiginlanmaz). Ekipman her zaman 1.")]
    public int maxStack = 1;

    [Header("Ikon (envanter)")]
    public Texture2D icon;

    [Header("Gorsel (model)")]
    public GameObject visualPrefab;
    public Vector3 visualOffset;
    public Vector3 visualRotation;
    public float visualScale = 1f;

    [Header("Yedek gorsel (model yoksa)")]
    public FallbackShape fallbackShape = FallbackShape.Cube;
    public Color fallbackColor = Color.white;
    [Tooltip("Metre cinsinden genislik/yukseklik/derinlik.")]
    public Vector3 fallbackSize = new Vector3(0.3f, 0.3f, 0.3f);

    public bool IsEquipment => equipSlot != EquipSlot.None;
    public bool Upgradeable => IsEquipment;
    public int StackLimit => IsEquipment || size == ItemSize.Large ? 1 : Mathf.Max(1, maxStack);
}
