using System.Collections.Generic;
using UnityEngine;

// Savas gorselleri (gecici, ilkel sekiller; ileride VFX paketi). Tum makinelerde PlayerCombat RPC'si ile oynar,
// hasara etkisi yok. Materyal: ItemDatabase.fallbackMaterial (URP Lit; build'de pembe olmasin) + renk.
public static class CombatFx
{
    static MaterialPropertyBlock block;

    static Material Mat
    {
        get
        {
            ItemDatabase db = ItemDatabase.Instance;
            return db != null ? db.fallbackMaterial : null;
        }
    }

    static GameObject Shape(string mesh, Vector3 pos, Quaternion rot, Vector3 scale, Color c)
    {
        var go = new GameObject("Fx");
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>(mesh);
        var mr = go.AddComponent<MeshRenderer>();
        Material m = Mat;
        if (m != null) mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (block == null) block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", c);
        mr.SetPropertyBlock(block);
        return go;
    }

    public static void Play(AbilityDef a, Vector3 origin, Vector3 dir)
    {
        if (a == null) return;
        Vector3 flat = new Vector3(dir.x, 0f, dir.z);
        if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
        flat.Normalize();
        Vector3 chest = origin + Vector3.up * 1.2f;

        switch (a.kind)
        {
            case AbilityKind.Melee:
                Swing(chest, flat, a.range, a.angle, a.color);
                break;
            case AbilityKind.Spin:
                Swing(chest, flat, a.radius, 360f, a.color);
                Burst(origin + Vector3.up * 0.3f, a.radius, a.color, 0.3f, true);
                break;
            case AbilityKind.Nova:
                Burst(origin + Vector3.up * 0.3f, a.radius, a.color, 0.4f, true);
                break;
            case AbilityKind.Dash:
            case AbilityKind.Blink:
                Trail(chest, chest + flat * a.moveDistance, a.color);
                if (a.kind == AbilityKind.Blink) Burst(origin + flat * a.moveDistance + Vector3.up, a.radius, a.color, 0.25f, false);
                break;
            case AbilityKind.Projectile:
            case AbilityKind.LeapBack:
                int n = Mathf.Max(1, a.projectiles);
                for (int i = 0; i < n; i++)
                {
                    float yaw = n > 1 ? Mathf.Lerp(-a.spread * 0.5f, a.spread * 0.5f, i / (float)(n - 1)) : 0f;
                    Projectile(chest, Quaternion.AngleAxis(yaw, Vector3.up) * dir.normalized, a);
                }
                break;
        }
    }

    // Kilic savurmasi: oyuncunun etrafinda yay ciziyor
    static void Swing(Vector3 center, Vector3 flatDir, float range, float angle, Color c)
    {
        GameObject pivot = new GameObject("FxSwing");
        pivot.transform.position = center;
        float startYaw = Quaternion.LookRotation(flatDir).eulerAngles.y - angle * 0.5f;
        pivot.transform.rotation = Quaternion.Euler(0f, startYaw, 0f);
        GameObject blade = Shape("Cube.fbx", center, pivot.transform.rotation, new Vector3(0.08f, 0.06f, range), c);
        blade.transform.SetParent(pivot.transform, true);
        blade.transform.localPosition = new Vector3(0f, 0f, range * 0.5f);
        FxAnim anim = pivot.AddComponent<FxAnim>();
        anim.life = Mathf.Clamp(angle / 900f, 0.12f, 0.35f);
        anim.spinDegrees = angle;
    }

    static void Burst(Vector3 pos, float radius, Color c, float life, bool flat)
    {
        Vector3 end = flat ? new Vector3(radius * 2f, 0.1f, radius * 2f) : Vector3.one * radius * 2f;
        GameObject g = Shape("Sphere.fbx", pos, Quaternion.identity, Vector3.one * 0.2f, c);
        FxAnim anim = g.AddComponent<FxAnim>();
        anim.life = life;
        anim.scaleFrom = flat ? new Vector3(0.2f, 0.1f, 0.2f) : Vector3.one * 0.2f;
        anim.scaleTo = end;
    }

    static void Trail(Vector3 from, Vector3 to, Color c)
    {
        Vector3 d = to - from;
        if (d.sqrMagnitude < 0.01f) return;
        GameObject g = Shape("Cube.fbx", (from + to) * 0.5f, Quaternion.LookRotation(d), new Vector3(0.25f, 0.25f, d.magnitude), c);
        FxAnim anim = g.AddComponent<FxAnim>();
        anim.life = 0.3f;
        anim.scaleFrom = g.transform.localScale;
        anim.scaleTo = new Vector3(0.02f, 0.02f, d.magnitude);
    }

    // Mermi: ayni fizik sorgusuyla (sunucu gibi) durdugu noktaya kadar ucar; alanliysa orada patlar
    static void Projectile(Vector3 from, Vector3 dir, AbilityDef a)
    {
        float dist = a.range;
        if (Physics.SphereCast(from, 0.2f, dir, out RaycastHit hit, a.range, ~0, QueryTriggerInteraction.Ignore))
            dist = hit.distance;
        bool orb = a.radius > 0f;
        GameObject g = orb
            ? Shape("Sphere.fbx", from, Quaternion.LookRotation(dir), Vector3.one * (a.radius > 2f ? 0.55f : 0.32f), a.color)
            : Shape("Cube.fbx", from, Quaternion.LookRotation(dir), new Vector3(0.05f, 0.05f, 0.7f), a.color);
        FxAnim anim = g.AddComponent<FxAnim>();
        anim.velocity = dir * a.speed;
        anim.life = dist / Mathf.Max(1f, a.speed);
        if (orb)
        {
            anim.onEnd = () => Burst(from + dir * dist, a.radius, a.color, 0.3f, false);
        }
    }
}

// Basit zamanli animasyon: hareket, olcek, donus; sure bitince yok olur
public class FxAnim : MonoBehaviour
{
    public float life = 0.3f;
    public Vector3 velocity;
    public float spinDegrees;
    public Vector3 scaleFrom, scaleTo;
    public System.Action onEnd;

    float t;
    Quaternion startRot;

    void Start()
    {
        startRot = transform.rotation;
        if (scaleFrom == Vector3.zero && scaleTo == Vector3.zero) scaleFrom = scaleTo = transform.localScale;
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / Mathf.Max(0.01f, life));
        transform.position += velocity * Time.deltaTime;
        if (spinDegrees != 0f) transform.rotation = startRot * Quaternion.Euler(0f, spinDegrees * k, 0f);
        transform.localScale = Vector3.Lerp(scaleFrom, scaleTo, k);
        if (t >= life)
        {
            onEnd?.Invoke();
            Destroy(gameObject);
        }
    }
}

// Ucan hasar sayilari (tum makineler; hedefin RPC'si cagirir). IMGUI, kendini kuran DDOL.
public class DamageNumbers : MonoBehaviour
{
    struct Entry
    {
        public Vector3 pos;
        public string text;
        public Color color;
        public float born;
        public int size;
    }

    static DamageNumbers instance;
    readonly List<Entry> entries = new List<Entry>();
    GUIStyle style;
    const float Life = 1.1f;

    public static void Show(Vector3 worldPos, int amount, bool crit)
    {
        if (instance == null)
        {
            var go = new GameObject("DamageNumbers");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DamageNumbers>();
        }
        instance.entries.Add(new Entry
        {
            pos = worldPos + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f)),
            text = crit ? $"{amount}!" : amount.ToString(),
            color = crit ? new Color(1f, 0.85f, 0.2f) : Color.white,
            born = Time.time,
            size = crit ? 30 : 22,
        });
    }

    void OnGUI()
    {
        Camera c = Camera.main;
        if (c == null) return;
        if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            Entry e = entries[i];
            float age = Time.time - e.born;
            if (age > Life) { entries.RemoveAt(i); continue; }
            Vector3 p = c.WorldToScreenPoint(e.pos + Vector3.up * (age * 1.4f));
            if (p.z < 0f) continue;
            style.fontSize = e.size;
            var r = new Rect(p.x - 60f, Screen.height - p.y - 20f, 120f, 40f);
            float a = 1f - Mathf.Clamp01((age - Life * 0.6f) / (Life * 0.4f));
            style.normal.textColor = new Color(0f, 0f, 0f, 0.8f * a);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), e.text, style);
            style.normal.textColor = new Color(e.color.r, e.color.g, e.color.b, a);
            GUI.Label(r, e.text, style);
        }
    }
}
