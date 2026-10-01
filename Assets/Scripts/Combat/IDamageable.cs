using UnityEngine;

// Hasar alabilen her sey (egitim kuklasi; ileride zindan yaratiklari). Sadece SUNUCUDA cagrilir.
public interface IDamageable
{
    int Armor { get; }
    bool IsAlive { get; }
    Transform Transform { get; }
    // amount: zirh uygulanmis son hasar. Hedef kendi gorsel geri bildirimini (hasar sayisi) RPC ile yayar.
    void ServerTakeDamage(int amount, bool crit, uint attackerNetId, Vector3 hitPoint);
}
