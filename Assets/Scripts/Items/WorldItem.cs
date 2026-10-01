using System.Collections.Generic;
using Mirror;
using UnityEngine;

// Dunyadaki bir esya (ag objesi). Tek bir prefab (Resources/Network/WorldItem) her esya turu icin
// kullanilir: itemId SyncVar'i ItemDatabase'ten veriyi ve gorseli secer.
//
// Uc durum (hepsi sunucuda karar verilir, SyncVar ile herkese gider):
//   Serbest : sunucuda fizik (Rigidbody), istemcilerde kinematik; konum NetworkTransform ile gelir.
//   Elde    : holderNetId != 0. Fizik/collider/NetworkTransform KAPALI; her makine esyayi tasiyanin
//             el noktasina kendisi oturtur (LateUpdate). Ek ag trafigi yok, tasiyan icin gecikme yok.
//             Esya oyuncuya child YAPILMAZ: oyuncu cikinca istemcide esya da silinirdi.
//   Rafta   : slotId != "". Kinematik, collider acik; konum yuvadan hesaplanir.
// Esya tutabilen (oyuncu, musteri): el noktasini verir
public interface IItemHolder
{
    Transform HoldPoint(ItemSize size);
}

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
public class WorldItem : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnItemIdChanged))] public string itemId = "";
    [SyncVar(hook = nameof(OnHolderChanged))] public uint holderNetId;
    [SyncVar(hook = nameof(OnSlotChanged))] public string slotId = "";
    [SyncVar] public byte plus;              // basma seviyesi (+0..+10)
    [SyncVar] public ushort count = 1;       // yerde yigin olarak durabilir (envanterden "Yere At")
    [Tooltip("Zindan dususu: oyuncu yakindan gecince kendiliginden envantere girer.")]
    [SyncVar] public bool autoPickup;
    [Tooltip("Rafta: etiket fiyati / piyasa fiyati (PricingRules). 1 = piyasa.")]
    [SyncVar] public float priceRatio = 1f;
    [Tooltip("Ileride: kirilganlik (1 = saglam).")]
    [SyncVar] public float condition = 1f;

    public static readonly List<WorldItem> All = new List<WorldItem>();

    public ItemData Data { get; private set; }
    public bool IsHeld => holderNetId != 0;
    public bool OnSlot => !string.IsNullOrEmpty(slotId);
    public Vector3 WorldCenter => transform.TransformPoint(box.center);
    public ItemStack Stack => new ItemStack(itemId, count, plus);
    public string DisplayName => count > 1 ? $"{ItemRules.DisplayName(Stack)} x{count}" : ItemRules.DisplayName(Stack);
    public int MarketPrice => PricingRules.MarketPrice(Stack);
    public int TagPrice => PricingRules.TagPrice(Stack, priceRatio);

    Rigidbody body;
    BoxCollider box;
    NetworkTransformBase netTransform;
    Transform followTarget;
    GameObject visual;
    string builtFor;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        box = GetComponent<BoxCollider>();
        netTransform = GetComponent<NetworkTransformBase>();
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public override void OnStartServer()
    {
        BuildVisual();
        ApplyState();
    }

    public override void OnStartClient()
    {
        BuildVisual();
        ApplyState();
    }

    void OnItemIdChanged(string oldId, string newId)
    {
        BuildVisual();
        ApplyState();
    }

    void OnHolderChanged(uint oldHolder, uint newHolder) => ApplyState();
    void OnSlotChanged(string oldSlot, string newSlot) => ApplyState();

    // ---------------- Sunucu islemleri (PlayerCarry Command'larindan) ----------------

    // Sunucuda esya dogurur (dusus, envanterden atma, ele alma, rafa dizme). holder verilirse elde, slotId verilirse rafta baslar.
    [Server]
    public static WorldItem ServerSpawn(ItemStack stack, Vector3 position, Quaternion rotation,
        bool autoPickup = false, uint holderNetId = 0, string slotId = "", float priceRatio = 1f)
    {
        GameObject prefab = Resources.Load<GameObject>("Network/WorldItem");
        if (prefab == null || stack.IsEmpty) return null;
        GameObject go = Instantiate(prefab, position, rotation);
        WorldItem it = go.GetComponent<WorldItem>();
        it.itemId = stack.id;
        it.plus = stack.plus;
        it.count = (ushort)Mathf.Max(1, (int)stack.count);
        it.autoPickup = autoPickup;
        it.holderNetId = holderNetId;
        it.slotId = slotId ?? "";
        it.priceRatio = PricingRules.ClampRatio(priceRatio);
        NetworkServer.Spawn(go);
        return it;
    }

    [Server]
    public void ServerPickup(uint carrierNetId)
    {
        slotId = "";
        holderNetId = carrierNetId;
        ApplyState();
    }

    [Server]
    public void ServerRelease(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        holderNetId = 0;
        slotId = "";
        transform.SetPositionAndRotation(position, rotation);
        ApplyState();
        body.linearVelocity = velocity;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();
    }

    [Server]
    public void ServerPlace(ItemSlot slot, float ratio = -1f)
    {
        holderNetId = 0;
        slotId = slot.id;
        if (ratio > 0f) priceRatio = PricingRules.ClampRatio(ratio);
        ApplyState();
    }

    // ---------------- Durum ----------------

    void ApplyState()
    {
        if (body == null) return;

        if (IsHeld)
        {
            body.isKinematic = true;
            box.enabled = false;
            if (netTransform != null) netTransform.enabled = false;
            followTarget = FindHoldPoint();
            return;
        }

        followTarget = null;
        box.enabled = true;
        if (netTransform != null) netTransform.enabled = true;

        if (OnSlot)
        {
            body.isKinematic = true;
            SnapToSlot();
        }
        else
        {
            // Fizik sadece sunucuda; istemciler NetworkTransform'dan konum alir
            body.isKinematic = !isServer;
        }
    }

    void SnapToSlot()
    {
        if (!ItemSlot.TryGet(slotId, out ItemSlot slot)) return;
        Quaternion rot = slot.transform.rotation;
        // Alt yuz yuvaya otursun
        float bottom = box.center.y - box.size.y * 0.5f;
        Vector3 pos = slot.transform.position - rot * new Vector3(box.center.x, bottom, box.center.z);
        transform.SetPositionAndRotation(pos, rot);
        Physics.SyncTransforms();
    }

    Transform FindHoldPoint()
    {
        Dictionary<uint, NetworkIdentity> spawned = isServer ? NetworkServer.spawned : NetworkClient.spawned;
        if (spawned.TryGetValue(holderNetId, out NetworkIdentity holder) && holder != null &&
            holder.TryGetComponent(out IItemHolder h))
            return h.HoldPoint(Data != null ? Data.size : ItemSize.Small);
        return null;
    }

    void LateUpdate()
    {
        if (!IsHeld) return;

        // Tasiyan henuz spawn olmamis olabilir (istemcide sira) → tekrar dene
        if (followTarget == null) followTarget = FindHoldPoint();
        if (followTarget == null)
        {
            // Sunucuda tasiyan yok olduysa (cikti) esyayi birak
            if (isServer && !NetworkServer.spawned.ContainsKey(holderNetId))
                ServerRelease(transform.position, transform.rotation, Vector3.zero);
            return;
        }

        Quaternion rot = followTarget.rotation;
        transform.SetPositionAndRotation(followTarget.position - rot * box.center, rot);
    }

    // ---------------- Gorsel ----------------

    void BuildVisual()
    {
        if (builtFor == itemId && visual != null) return;
        builtFor = itemId;
        if (visual != null) Destroy(visual);

        Data = ItemDatabase.Get(itemId);
        if (Data == null) Debug.LogWarning($"[WorldItem] Bilinmeyen esya id: '{itemId}'", this);

        if (Data != null && Data.visualPrefab != null)
        {
            visual = Instantiate(Data.visualPrefab, transform);
            visual.transform.localPosition = Data.visualOffset;
            visual.transform.localRotation = Quaternion.Euler(Data.visualRotation);
            visual.transform.localScale = Vector3.one * Data.visualScale;
            // Modelin kendi fizigi olmasin; carpisma kokteki BoxCollider'da
            foreach (Collider c in visual.GetComponentsInChildren<Collider>(true)) { c.enabled = false; Destroy(c); }
            foreach (Rigidbody r in visual.GetComponentsInChildren<Rigidbody>(true)) Destroy(r);
            StripLods(visual);
        }
        else
        {
            visual = CreateFallback(Data);
        }
        visual.name = "Visual";

        Bounds b = LocalBounds(visual);
        box.center = b.center;
        box.size = Vector3.Max(b.size, Vector3.one * 0.05f);
        body.mass = Data != null ? Mathf.Max(0.05f, Data.mass) : 1f;
        gameObject.name = $"Item [{DisplayName}]";
    }

    // Paket modellerinin LODGroup'u bina olcegine gore ayarli: kucuk esya yakindan bile "culled" olup
    // kayboluyor. Esyalarda sadece LOD0 kalsin (en detayli), digerleri ve LODGroup silinsin.
    static void StripLods(GameObject root)
    {
        foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true))
        {
            LOD[] lods = group.GetLODs();
            var keep = new HashSet<Renderer>();
            if (lods.Length > 0)
                foreach (Renderer r in lods[0].renderers)
                    if (r != null) keep.Add(r);

            for (int i = 1; i < lods.Length; i++)
                foreach (Renderer r in lods[i].renderers)
                    if (r != null && !keep.Contains(r)) r.enabled = false; // LocalBounds saymasin

            Destroy(group);
        }
    }

    GameObject CreateFallback(ItemData data)
    {
        FallbackShape shape = data != null ? data.fallbackShape : FallbackShape.Cube;
        Vector3 size = data != null ? data.fallbackSize : Vector3.one * 0.3f;
        Color color = data != null ? data.fallbackColor : Color.magenta;

        string meshName = shape switch
        {
            FallbackShape.Sphere => "Sphere.fbx",
            FallbackShape.Cylinder => "Cylinder.fbx",
            FallbackShape.Capsule => "Capsule.fbx",
            _ => "Cube.fbx",
        };
        // Silindir ve kapsul Unity'de 2 m yuksekliginde
        if (shape == FallbackShape.Cylinder || shape == FallbackShape.Capsule) size.y *= 0.5f;

        var go = new GameObject("Visual");
        go.transform.SetParent(transform, false);
        go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>(meshName);
        var mr = go.AddComponent<MeshRenderer>();
        ItemDatabase db = ItemDatabase.Instance;
        if (db != null && db.fallbackMaterial != null) mr.sharedMaterial = db.fallbackMaterial;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        mr.SetPropertyBlock(block);
        return go;
    }

    // Gorselin kok uzayindaki sinirlari (collider boyutu icin)
    Bounds LocalBounds(GameObject root)
    {
        bool any = false;
        var b = new Bounds();
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(false))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.TryGetComponent(out Renderer rend) && !rend.enabled) continue;
            Bounds mb = mf.sharedMesh.bounds;
            Vector3 c = mb.center, e = mb.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        return any ? b : new Bounds(Vector3.zero, Vector3.one * 0.3f);
    }
}
