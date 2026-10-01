using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sanat paketinin LOD esikleri cok agresif (0.6 / 0.35 / 0.18): 0.5 m'lik bir tabure 1-2 m'de "culled"
// olup kayboluyor. Proje hafif → gecisler gec, gizleme neredeyse hic. Paket dosyalarina DOKUNMAZ;
// sahnedeki kopyalara (prefab instance override) uygulanir. TownBlockoutBuilder her model icin cagirir.
// Menu: PlayersMarket > LOD Esiklerini Duzelt (acik sahne)
public static class LodTuner
{
    // Ekran yuksekligine oran: LOD0→LOD1, LOD1→LOD2, LOD2→... ; son LOD'un esigi = gizleme
    static readonly float[] Transitions = { 0.12f, 0.04f, 0.012f };
    const float CullHeight = 0.004f;

    public static int Apply(GameObject root)
    {
        int n = 0;
        foreach (LODGroup g in root.GetComponentsInChildren<LODGroup>(true))
        {
            LOD[] lods = g.GetLODs();
            if (lods.Length == 0) continue;
            for (int i = 0; i < lods.Length; i++)
            {
                float h = i == lods.Length - 1 ? CullHeight : i < Transitions.Length ? Transitions[i] : CullHeight * 2f;
                if (i > 0 && h >= lods[i - 1].screenRelativeTransitionHeight)
                    h = lods[i - 1].screenRelativeTransitionHeight * 0.5f; // azalan sira zorunlu
                lods[i].screenRelativeTransitionHeight = h;
            }
            g.SetLODs(lods);
            if (PrefabUtility.IsPartOfPrefabInstance(g)) PrefabUtility.RecordPrefabInstancePropertyModifications(g);
            n++;
        }
        return n;
    }

    [MenuItem("PlayersMarket/LOD Esiklerini Duzelt (acik sahne)")]
    static void FixOpenScenes()
    {
        int total = 0;
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            int n = 0;
            foreach (GameObject go in scene.GetRootGameObjects()) n += Apply(go);
            if (n > 0) EditorSceneManager.MarkSceneDirty(scene);
            total += n;
        }
        Debug.Log($"[LodTuner] {total} LODGroup duzeltildi. Sahneyi kaydet (Ctrl+S).");
    }
}
