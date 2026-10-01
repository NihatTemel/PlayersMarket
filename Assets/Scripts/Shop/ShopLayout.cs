using UnityEngine;

// Dukkan/musteri yol noktalari (sahne objesi; TownBlockoutBuilder kurar). Musteriler (CustomerAI) kullanir.
// Kuyruk: kasanin onunden (counterFront) queueDirection yonunde queueSpacing araliklarla dizilir.
public class ShopLayout : MonoBehaviour
{
    public static ShopLayout Instance { get; private set; }

    public Transform spawnPoint;    // musteri dogusu / cikisi (kasaba girisi)
    public Transform doorInside;    // dukkan kapisinin hemen ici
    public Transform counterFront;  // kasanin onu (kuyrugun basi)
    public Transform counterFacing; // kuyruktakilerin baktigi nokta (kasa)
    public Vector3 queueDirection = Vector3.back;
    public float queueSpacing = 1.0f;
    [Tooltip("Kuyruk bu kadar kisiden sonra yana kivrilir (on duvara dayanmasin).")]
    public int queueTurnAfter = 4;
    public Vector3 queueTurnDirection = Vector3.left;

    public Vector3 SpawnPoint => spawnPoint != null ? spawnPoint.position : transform.position;
    public Vector3 DoorInside => doorInside != null ? doorInside.position : transform.position;
    public Vector3 CounterFacing => counterFacing != null ? counterFacing.position : transform.position;

    public Vector3 QueuePoint(int index)
    {
        Vector3 front = counterFront != null ? counterFront.position : transform.position;
        index = Mathf.Max(0, index);
        int straight = Mathf.Min(index, queueTurnAfter - 1);
        Vector3 p = front + queueDirection.normalized * queueSpacing * straight;
        if (index >= queueTurnAfter) p += queueTurnDirection.normalized * queueSpacing * (index - queueTurnAfter + 1);
        return p;
    }

    void OnEnable() => Instance = this;

    void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        for (int i = 0; i < 5; i++) Gizmos.DrawWireSphere(QueuePoint(i) + Vector3.up * 0.1f, 0.25f);
        Gizmos.color = Color.green;
        if (spawnPoint != null) Gizmos.DrawWireSphere(spawnPoint.position, 0.6f);
        if (doorInside != null) Gizmos.DrawWireSphere(doorInside.position, 0.4f);
    }
}
