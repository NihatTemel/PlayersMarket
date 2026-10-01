using Mirror;
using UnityEngine;

// TEST: sunucu sahne acilinca bu noktanin ustune esya dokar (zindan gelene kadar).
// ItemDatabase'teki esyalardan sirayla secer; alanin icine dagitip biraz yukaridan birakir.
public class TestLootSpawner : MonoBehaviour
{
    public int count = 16;
    public Vector2 area = new Vector2(3f, 2f);
    public float dropHeight = 1f;

    void Start()
    {
        if (!NetworkServer.active) return;

        ItemDatabase db = ItemDatabase.Instance;
        if (db == null || db.items.Count == 0)
        {
            Debug.LogWarning("[TestLootSpawner] ItemDatabase bos ya da yok.");
            return;
        }

        int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
        for (int i = 0; i < count; i++)
        {
            ItemData data = db.items[i % db.items.Count];
            if (data == null) continue;

            float fx = cols > 1 ? (i % cols) / (float)(cols - 1) - 0.5f : 0f;
            float fz = cols > 1 ? (i / cols) / (float)(cols - 1) - 0.5f : 0f;
            Vector3 pos = transform.position + transform.rotation * new Vector3(fx * area.x, dropHeight + (i % 3) * 0.3f, fz * area.y);

            WorldItem.ServerSpawn(new ItemStack(data.id), pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        }
        Debug.Log($"[TestLootSpawner] {count} test esyasi dokuldu.");
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.7f, 0.3f, 1f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.up * dropHeight, new Vector3(area.x, 0.1f, area.y));
    }
}
