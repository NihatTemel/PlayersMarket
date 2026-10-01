using UnityEngine;

// Sahnede kukla dogma noktasi (TownBlockoutBuilder koyar; sunucu GameState dogurur)
public class TrainingDummyPoint : MonoBehaviour
{
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.3f, 0.8f);
        Gizmos.DrawWireCube(transform.position + Vector3.up, new Vector3(0.6f, 2f, 0.6f));
    }
}
