using System;

// Envanterdeki/dunyadaki bir esya yigini: tur id + basma seviyesi (+0..+10) + adet.
// Ag uzerinden (SyncList, Command) gider; Mirror struct'i otomatik serilestirir.
[Serializable]
public struct ItemStack : IEquatable<ItemStack>
{
    public string id;
    public byte plus;
    public ushort count;

    public ItemStack(string id, int count = 1, int plus = 0)
    {
        this.id = id;
        this.count = (ushort)Math.Max(0, Math.Min(count, ushort.MaxValue));
        this.plus = (byte)Math.Max(0, Math.Min(plus, ItemRules.MaxPlus));
    }

    public static ItemStack Empty => default;
    public bool IsEmpty => string.IsNullOrEmpty(id) || count == 0;
    public ItemData Data => IsEmpty ? null : ItemDatabase.Get(id);

    public bool CanStackWith(ItemStack other) =>
        !IsEmpty && !other.IsEmpty && id == other.id && plus == other.plus;

    public ItemStack WithCount(int n) => new ItemStack(id, n, plus);

    public bool Equals(ItemStack o) =>
        (IsEmpty && o.IsEmpty) || (id == o.id && plus == o.plus && count == o.count);

    public override bool Equals(object obj) => obj is ItemStack o && Equals(o);
    public override int GetHashCode() => IsEmpty ? 0 : HashCode.Combine(id, plus, count);
    public override string ToString() => IsEmpty ? "(bos)" : $"{id}+{plus}x{count}";
}
