using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Sanat paketi envanteri: Claude Unity'yi goremedigi icin paketteki prefab'larin olculerini ve
// kucuk resimlerini dosyaya doker. Cikti proje kokunde Logs/PackInventory/ (git'e girmez):
//   inventory.csv  : no;yol;boyut x/y/z;merkez;taban y;collider;LOD;shader uyarisi
//   sheet_N.png    : 8x6 numarali kucuk resim paftasi (numara = csv'deki no)
// Paket varsa ve cikti yoksa acilista bir kez calisir. Menu: PlayersMarket > Paket Envanteri Cikar
[InitializeOnLoad]
public static class AssetPackInventory
{
    const string PackRoot = "Assets/EmaceArt/Slavic World Free/Prefabs";
    const string OutDir = "Logs/PackInventory";
    const int Cell = 128, Cols = 8, Rows = 6;
    const double TimeoutSeconds = 120;

    static List<GameObject> pending;
    static double startTime;

    static AssetPackInventory()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.IsValidFolder(PackRoot) && !File.Exists(Path.Combine(OutDir, "inventory.csv"))) Run();
        };
    }

    [MenuItem("PlayersMarket/Paket Envanteri Cikar")]
    static void Run()
    {
        if (!AssetDatabase.IsValidFolder(PackRoot))
        {
            Debug.LogWarning("[PackInventory] Paket bulunamadi: " + PackRoot);
            return;
        }
        Directory.CreateDirectory(OutDir);

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PackRoot });
        var paths = new List<string>();
        foreach (string g in guids) paths.Add(AssetDatabase.GUIDToAssetPath(g));
        paths.Sort(System.StringComparer.Ordinal);

        var csv = new StringBuilder();
        csv.AppendLine("no;path;sizeX;sizeY;sizeZ;centerX;centerY;centerZ;minY;colliders;lod;nonUrpShaders");
        pending = new List<GameObject>();
        CultureInfo ci = CultureInfo.InvariantCulture;

        for (int i = 0; i < paths.Count; i++)
        {
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
            pending.Add(go);
            if (go == null) continue;

            Bounds b = LocalBounds(go, out bool any);
            int colliders = go.GetComponentsInChildren<Collider>(true).Length;
            bool lod = go.GetComponentInChildren<LODGroup>(true) != null;

            var bad = new HashSet<string>();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.shader != null && !m.shader.name.StartsWith("Universal Render Pipeline"))
                        bad.Add(m.shader.name);

            string rel = paths[i].Substring(PackRoot.Length + 1);
            csv.AppendLine(string.Join(";",
                i.ToString(ci), rel,
                F(b.size.x), F(b.size.y), F(b.size.z),
                F(b.center.x), F(b.center.y), F(b.center.z), F(b.min.y),
                colliders.ToString(ci), lod ? "1" : "0",
                any ? string.Join("|", bad) : "NO_MESH"));
        }

        File.WriteAllText(Path.Combine(OutDir, "inventory.csv"), csv.ToString(), new UTF8Encoding(false));
        Debug.Log($"[PackInventory] {paths.Count} prefab yazildi: {OutDir}/inventory.csv. Kucuk resimler hazirlaniyor...");

        // Kucuk resimler asenkron uretilir
        AssetPreview.SetPreviewTextureCacheSize(paths.Count + 64);
        foreach (GameObject go in pending)
            if (go != null) AssetPreview.GetAssetPreview(go);
        startTime = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    static void Poll()
    {
        bool timeout = EditorApplication.timeSinceStartup - startTime > TimeoutSeconds;
        int missing = 0;
        foreach (GameObject go in pending)
            if (go != null && AssetPreview.GetAssetPreview(go) == null) missing++;
        if (missing > 0 && !timeout) return;

        EditorApplication.update -= Poll;
        WriteSheets();
        Debug.Log($"[PackInventory] Paftalar yazildi ({missing} resim eksik). Klasor: {Path.GetFullPath(OutDir)}");
    }

    static void WriteSheets()
    {
        int perSheet = Cols * Rows;
        int sheets = Mathf.CeilToInt(pending.Count / (float)perSheet);
        RenderTexture rt = RenderTexture.GetTemporary(Cell, Cell, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture prev = RenderTexture.active;

        for (int s = 0; s < sheets; s++)
        {
            var sheet = new Texture2D(Cols * Cell, Rows * Cell, TextureFormat.RGBA32, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int k = 0; k < fill.Length; k++) fill[k] = new Color32(40, 40, 44, 255);
            sheet.SetPixels32(fill);

            for (int c = 0; c < perSheet; c++)
            {
                int index = s * perSheet + c;
                if (index >= pending.Count) break;
                int cx = (c % Cols) * Cell;
                int cy = (Rows - 1 - c / Cols) * Cell; // ust satir ustte

                GameObject go = pending[index];
                Texture2D tex = go != null ? AssetPreview.GetAssetPreview(go) : null;
                if (tex != null)
                {
                    Graphics.Blit(tex, rt);
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, Cell, Cell), cx, cy);
                }
                else
                {
                    FillRect(sheet, cx + 4, cy + 4, Cell - 8, Cell - 8, new Color32(120, 30, 30, 255));
                }
                DrawNumber(sheet, index, cx + 3, cy + Cell - 3);
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, $"sheet_{s}.png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
        }

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
    }

    // Prefab kokune gore sinirlar (sahneye koymadan, asset hiyerarsisinden)
    static Bounds LocalBounds(GameObject root, out bool any)
    {
        any = false;
        var b = new Bounds();
        Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            Add(mf.sharedMesh, mf.transform, toRoot, ref b, ref any);
        foreach (SkinnedMeshRenderer sr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            Add(sr.sharedMesh, sr.transform, toRoot, ref b, ref any);
        return b;
    }

    static void Add(Mesh mesh, Transform t, Matrix4x4 toRoot, ref Bounds b, ref bool any)
    {
        if (mesh == null) return;
        Matrix4x4 m = toRoot * t.localToWorldMatrix;
        Bounds mb = mesh.bounds;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 p = m.MultiplyPoint3x4(corner);
            if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
            else b.Encapsulate(p);
        }
    }

    // ---- Numara cizimi (3x5 piksel rakamlar, 3 kat buyuk, siyah zemin) ----

    static readonly string[] Digits =
    {
        "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
        "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111",
    };

    static void DrawNumber(Texture2D tex, int n, int left, int top)
    {
        string s = n.ToString(CultureInfo.InvariantCulture);
        const int scale = 3;
        int w = s.Length * 4 * scale + scale, h = 5 * scale + 2 * scale;
        FillRect(tex, left, top - h, w, h, new Color32(0, 0, 0, 230));
        for (int d = 0; d < s.Length; d++)
        {
            string glyph = Digits[s[d] - '0'];
            for (int gy = 0; gy < 5; gy++)
                for (int gx = 0; gx < 3; gx++)
                    if (glyph[gy * 3 + gx] == '1')
                        FillRect(tex, left + scale + d * 4 * scale + gx * scale, top - scale - (gy + 1) * scale, scale, scale,
                            new Color32(255, 230, 80, 255));
        }
    }

    static void FillRect(Texture2D tex, int x, int y, int w, int h, Color32 c)
    {
        for (int yy = Mathf.Max(0, y); yy < Mathf.Min(tex.height, y + h); yy++)
            for (int xx = Mathf.Max(0, x); xx < Mathf.Min(tex.width, x + w); xx++)
                tex.SetPixel(xx, yy, c);
    }
}
