using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

public enum CustomerState : byte { ToShop, Browsing, ToItem, Deciding, ToCounter, AtCounter, Leaving }
public enum CustomerMood : byte { None, Happy, Fair, Thinking, Angry, Empty, Paid, Grumble, Impatient, Refused, JustLooking, MaybeLater, Scared }

// Musteri (ag objesi, Resources/Network/Customer). Beyin SUNUCUDA (NavMeshAgent), istemciler NetworkTransform.
// Akis: kasaba girisi → dukkan kapisi → raftan bir urun sec → fiyati degerlendir (PricingRules.Judge):
//   ucuz/normal: alir, kasa kuyruguna girer · pahali: alir, kasada pazarlik teklif eder · cok pahali: kizip gider.
// Kasada bir oyuncu E ile satar (pazarlikta E kabul / Q ret). Sabir biterse urunu rafa birakip kizgin gider.
// Odemeden sonra urunle birlikte kasabadan cikar (urun yok edilir).
// Vurulabilir (IDamageable): vurulan musteri bagirip KOSARAK kacar. Odemedigi urunu rafina birakir; odemisse
// urun elinde kalir. Un cezasi YOK (kullanici karari: musteriyi dovup kovmak serbest).
public class CustomerAI : NetworkBehaviour, IItemHolder, IDamageable
{
    [SyncVar(hook = nameof(OnTypeChanged))] public CustomerType type;
    [SyncVar] public CustomerState state;
    [SyncVar] public CustomerMood mood;
    [SyncVar] public double moodTime;      // NetworkTime.time
    [SyncVar] public int payPrice;          // etiket fiyati (odenecek)
    [SyncVar] public int offerPrice;        // pazarlik teklifi (0 = pazarlik yok)
    [SyncVar] public double patienceEnd;    // kasada sabir bitisi (NetworkTime.time)
    [SyncVar] public string itemName = "";
    [SyncVar] public int queuePos = -1;     // kasa kuyrugundaki sira (0 = en on); istemciler de bilsin

    public const float Patience = 45f;      // kasada bekleme suresi (sn)
    [HideInInspector] public int prefabVersion; // ItemSetupBuilder eski prefab'i tanisin diye

    public static readonly List<CustomerAI> All = new List<CustomerAI>();
    static readonly List<CustomerAI> queue = new List<CustomerAI>();      // sunucu: kasa kuyrugu
    static readonly HashSet<uint> reservedItems = new HashSet<uint>();     // sunucu: baska musterinin hedefi

    public bool AtCounterFront => state == CustomerState.AtCounter && queuePos == 0;
    int QueueIndex => queue.IndexOf(this);
    public bool Haggling => offerPrice > 0;

    NavMeshAgent agent;
    Transform holdPoint;
    MeshRenderer bodyRenderer;
    GUIStyle bubbleStyle, nameStyle;

    // Sunucu durumu
    WorldItem target;
    string originalSlot = "";
    float pendingRep;
    float decideAt;
    int browseTries;
    bool bought;
    bool windowShopper;                // sadece gezip cikar
    int looksLeft;                     // bakacagi urun sayisi
    readonly HashSet<uint> seen = new HashSet<uint>();

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        holdPoint = transform.Find("HoldPoint");
        Transform body = transform.Find("Body");
        if (body != null) bodyRenderer = body.GetComponent<MeshRenderer>();
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public Transform HoldPoint(ItemSize size) => holdPoint != null ? holdPoint : transform;

    // ---------------- Dovulme (IDamageable) ----------------

    bool fled;
    public int Armor => 0;
    public bool IsAlive => true;
    public Transform Transform => transform;

    [Server]
    public void ServerTakeDamage(int amount, bool crit, uint attackerNetId, Vector3 hitPoint)
    {
        RpcHit(amount, crit, hitPoint);

        // Vurandan uzaga itil
        if (agent != null && agent.enabled && NetworkServer.spawned.TryGetValue(attackerNetId, out NetworkIdentity attacker))
        {
            Vector3 away = transform.position - attacker.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.01f) agent.velocity = away.normalized * 6f;
        }

        if (fled) return; // zaten kaciyor
        fled = true;
        SetMood(CustomerMood.Scared);
        Unreserve();
        target = null;
        // Odemediyse urunu birak; odediyse (bought) elinde kalir
        if (!bought)
        {
            WorldItem held = Carried();
            if (held != null) ReturnItem(held);
        }
        if (agent != null) agent.speed = 6.5f; // kosarak kac
        Leave(ShopLayout.Instance);
    }

    [ClientRpc]
    void RpcHit(int amount, bool crit, Vector3 point)
    {
        try { DamageNumbers.Show(point, amount, crit); }
        catch (System.Exception e) { Debug.LogException(e); }
    }

    public override void OnStartServer()
    {
        if (agent != null)
        {
            agent.enabled = true;
            agent.Warp(transform.position);
        }
        windowShopper = Random.value < PricingRules.WindowShopChance(type);
        looksLeft = PricingRules.RollLooks();
        state = CustomerState.ToShop;
        GoTo(ShopLayout.Instance != null ? ShopLayout.Instance.DoorInside : transform.position);
    }

    public override void OnStartClient()
    {
        ApplyColor();
        if (!isServer && agent != null) agent.enabled = false; // istemcide hareket NetworkTransform'dan
    }

    public override void OnStopServer()
    {
        Unreserve();
        queue.Remove(this);
        if (!NetworkServer.active) return; // sunucu kapaniyor: esyalar zaten yok edilecek
        WorldItem held = Carried();
        if (held == null) return;
        if (bought) NetworkServer.Destroy(held.gameObject);
        else ReturnItem(held);
    }

    void OnTypeChanged(CustomerType oldType, CustomerType newType) => ApplyColor();

    void ApplyColor()
    {
        if (bodyRenderer == null) return;
        Color c = type switch
        {
            CustomerType.Adventurer => new Color(0.3f, 0.5f, 0.85f),
            CustomerType.Noble => new Color(0.6f, 0.3f, 0.75f),
            _ => new Color(0.55f, 0.6f, 0.35f),
        };
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", c);
        bodyRenderer.SetPropertyBlock(block);
    }

    // ---------------- Sunucu beyni ----------------

    void Update()
    {
        if (!isServer || agent == null || !agent.enabled) return;
        ShopLayout layout = ShopLayout.Instance;
        if (layout == null) return;

        switch (state)
        {
            case CustomerState.ToShop:
                if (Arrived()) { state = CustomerState.Browsing; decideAt = Time.time + Random.Range(0.5f, 1.5f); }
                break;

            case CustomerState.Browsing:
                if (Time.time >= decideAt) PickItem(layout);
                break;

            case CustomerState.ToItem:
                if (!TargetStillOnShelf()) { Unreserve(); Retry(); break; }
                if (Arrived())
                {
                    state = CustomerState.Deciding;
                    decideAt = Time.time + Random.Range(1f, 2f); // bakiyor, dusunuyor
                }
                break;

            case CustomerState.Deciding:
                FaceTowards(target != null ? target.transform.position : transform.position + transform.forward);
                if (!TargetStillOnShelf()) { Unreserve(); Retry(); break; }
                if (Time.time >= decideAt) Decide();
                break;

            case CustomerState.ToCounter:
            case CustomerState.AtCounter:
                UpdateQueue(layout);
                break;

            case CustomerState.Leaving:
                if (Arrived()) NetworkServer.Destroy(gameObject);
                break;
        }
    }

    void PickItem(ShopLayout layout)
    {
        var options = new List<WorldItem>();
        foreach (WorldItem it in WorldItem.All)
            if (it.OnSlot && !it.IsHeld && !reservedItems.Contains(it.netId) && !seen.Contains(it.netId)) options.Add(it);

        if (options.Count == 0)
        {
            if (seen.Count > 0) { Leave(layout); return; } // baktiklari disinda urun kalmadi
            SetMood(CustomerMood.Empty);
            Rep(PricingRules.RepEmptyShop);
            Leave(layout);
            return;
        }

        target = options[Random.Range(0, options.Count)];
        reservedItems.Add(target.netId);
        seen.Add(target.netId);
        originalSlot = target.slotId;
        state = CustomerState.ToItem;
        GoTo(StandPoint(target));
    }

    void Retry()
    {
        if (++browseTries >= 3) { Leave(ShopLayout.Instance); return; }
        state = CustomerState.Browsing;
        decideAt = Time.time + 0.5f;
    }

    bool TargetStillOnShelf() => target != null && target.OnSlot && !target.IsHeld;

    void Decide()
    {
        ItemData d = target.Data;
        int market = target.MarketPrice;
        int tag = target.TagPrice;
        PriceVerdict v = PricingRules.Judge(target.priceRatio, PricingRules.Tolerance(type, d));

        if (v == PriceVerdict.TooPricey)
        {
            SetMood(CustomerMood.Angry);
            Rep(PricingRules.RepTooPricey);
            Unreserve();
            Leave(ShopLayout.Instance);
            return;
        }

        // Vitrin gezgini ya da "belki sonra": almaz, varsa baska urune bakar
        if (windowShopper || Random.value > PricingRules.BuyChance(v))
        {
            SetMood(windowShopper ? CustomerMood.JustLooking : CustomerMood.MaybeLater);
            Unreserve();
            target = null;
            if (--looksLeft > 0)
            {
                state = CustomerState.Browsing;
                decideAt = Time.time + Random.Range(1f, 2.5f);
            }
            else Leave(ShopLayout.Instance);
            return;
        }

        // Al: esya musterinin eline gecer (raftan kalkar)
        Unreserve();
        itemName = target.DisplayName;
        target.ServerPickup(netId);
        payPrice = tag;
        if (v == PriceVerdict.Haggle)
        {
            offerPrice = PricingRules.HaggleOffer(market, tag);
            SetMood(CustomerMood.Thinking);
        }
        else
        {
            offerPrice = 0;
            pendingRep = v == PriceVerdict.Cheap ? PricingRules.RepCheap : PricingRules.RepFair;
            SetMood(v == PriceVerdict.Cheap ? CustomerMood.Happy : CustomerMood.Fair);
        }
        target = null;
        queue.Add(this);
        state = CustomerState.ToCounter;
    }

    void UpdateQueue(ShopLayout layout)
    {
        int i = QueueIndex;
        if (i < 0) { Leave(layout); return; }
        if (queuePos != i) queuePos = i;
        Vector3 spot = layout.QueuePoint(i);
        if ((agent.destination - spot).sqrMagnitude > 0.04f) GoTo(spot);

        if (state == CustomerState.ToCounter && Arrived())
        {
            state = CustomerState.AtCounter;
            patienceEnd = NetworkTime.time + Patience;
        }
        if (state == CustomerState.AtCounter)
        {
            FaceTowards(layout.CounterFacing);
            if (NetworkTime.time > patienceEnd)
            {
                // Kimse ilgilenmedi: urunu birak, kizgin git
                SetMood(CustomerMood.Impatient);
                Rep(PricingRules.RepNotServed);
                WorldItem held = Carried();
                if (held != null) ReturnItem(held);
                Leave(layout);
            }
        }
    }

    // Kasadaki oyuncu (PlayerCarry.CmdServe): accept = sat / pazarlikta kabul; false = pazarligi reddet
    [Server]
    public void ServerServe(bool accept)
    {
        if (!AtCounterFront) return;
        GameState gs = GameState.Instance;
        WorldItem held = Carried();

        if (Haggling && !accept)
        {
            if (Random.value < PricingRules.BuyAfterRejectChance)
            {
                Pay(gs, payPrice, 0f, CustomerMood.Grumble); // "peki..." etiket fiyatina alir
            }
            else
            {
                SetMood(CustomerMood.Refused);
                Rep(PricingRules.RepHaggleRejectedLeft);
                if (held != null) ReturnItem(held);
                Leave(ShopLayout.Instance);
            }
            return;
        }

        if (Haggling) Pay(gs, offerPrice, PricingRules.RepHaggleAccepted, CustomerMood.Paid);
        else Pay(gs, payPrice, pendingRep, CustomerMood.Paid);
    }

    void Pay(GameState gs, int amount, float rep, CustomerMood m)
    {
        if (gs != null)
        {
            gs.AddGold(amount);
            gs.AddReputation(rep);
            gs.RecordSale(amount);
        }
        bought = true;
        payPrice = amount;
        SetMood(m);
        Debug.Log($"[Shop] {PricingRules.TypeName(type)} {itemName} icin {amount} altin odedi.");
        Leave(ShopLayout.Instance);
    }

    // Urunu eski rafina (bossa) geri koy; degilse yere birak
    [Server]
    void ReturnItem(WorldItem held)
    {
        if (ItemSlot.TryGet(originalSlot, out ItemSlot slot) && slot.IsFree()) held.ServerPlace(slot);
        else held.ServerRelease(transform.position + transform.forward * 0.6f + Vector3.up, transform.rotation, Vector3.zero);
    }

    void Leave(ShopLayout layout)
    {
        queue.Remove(this);
        queuePos = -1;
        state = CustomerState.Leaving;
        GoTo(layout != null ? layout.SpawnPoint : transform.position);
    }

    void Unreserve()
    {
        if (target != null) reservedItems.Remove(target.netId);
    }

    WorldItem Carried()
    {
        foreach (WorldItem it in WorldItem.All)
            if (it.holderNetId == netId && netId != 0) return it;
        return null;
    }

    static void Rep(float delta)
    {
        GameState gs = GameState.Instance;
        if (gs != null) gs.AddReputation(delta);
    }

    void SetMood(CustomerMood m)
    {
        mood = m;
        moodTime = NetworkTime.time;
    }

    void GoTo(Vector3 p)
    {
        if (agent == null || !agent.enabled) return;
        if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas)) p = hit.position;
        agent.SetDestination(p);
    }

    bool Arrived() =>
        !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.15f;

    void FaceTowards(Vector3 p)
    {
        Vector3 d = p - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 360f * Time.deltaTime);
    }

    // Rafin onunde durulacak nokta (yuvanin -z yonu = raf onu)
    static Vector3 StandPoint(WorldItem it)
    {
        if (ItemSlot.TryGet(it.slotId, out ItemSlot s))
        {
            Vector3 p = s.transform.position + s.transform.rotation * new Vector3(0f, 0f, -0.9f);
            p.y = 0f;
            return p;
        }
        return it.transform.position;
    }

    // Satis yapabilecegi musteri: kasada kuyrugun basinda, oyuncunun onunde ve yakininda
    public static CustomerAI FindServable(Vector3 origin, Vector3 flatForward, float maxDistance)
    {
        CustomerAI best = null;
        float bestDist = maxDistance;
        foreach (CustomerAI c in All)
        {
            if (!c.AtCounterFront) continue; // sadece kuyrugun basindakine satilir
            Vector3 to = c.transform.position - origin;
            to.y = 0f;
            float d = to.magnitude;
            if (d > bestDist || (d > 1f && Vector3.Angle(flatForward, to) > 80f)) continue;
            best = c;
            bestDist = d;
        }
        return best;
    }

    // ---------------- Gorunum (tum makineler) ----------------

    void OnGUI()
    {
        Camera c = Camera.main;
        if (c == null) return;
        Vector3 head = transform.position + Vector3.up * 1.9f;
        if ((head - c.transform.position).sqrMagnitude > 30f * 30f) return;
        Vector3 p = c.WorldToScreenPoint(head);
        if (p.z < 0f) return;

        if (bubbleStyle == null)
        {
            bubbleStyle = new GUIStyle(GUI.skin.box) { fontSize = 17, alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = false };
            nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            nameStyle.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
        }

        float y = Screen.height - p.y;
        GUI.Label(new Rect(p.x - 80f, y + 2f, 160f, 20f), PricingRules.TypeName(type), nameStyle);

        string bubble = BubbleText();
        if (string.IsNullOrEmpty(bubble)) return;
        Vector2 size = bubbleStyle.CalcSize(new GUIContent(bubble)) + new Vector2(14f, 8f);
        GUI.Box(new Rect(p.x - size.x * 0.5f, y - size.y - 4f, size.x, size.y), bubble, bubbleStyle);
    }

    string BubbleText()
    {
        if (state == CustomerState.AtCounter)
        {
            int left = Mathf.Max(0, (int)(patienceEnd - NetworkTime.time));
            if (queuePos > 0) return $"Sırada ({queuePos + 1}.)  ⏳{left}";
            return Haggling
                ? $"<color=#ffd257>Teklif: {offerPrice} altın</color> <color=#bbbbbb>(etiket {payPrice})</color>  ⏳{left}"
                : $"Ödeme: <color=#ffd257>{payPrice} altın</color>  ⏳{left}";
        }
        if (NetworkTime.time - moodTime > 3.5) return "";
        return mood switch
        {
            CustomerMood.Happy => "<color=#7CFC7C>Ucuzmuş, alıyorum!</color>",
            CustomerMood.Fair => "İyi fiyat.",
            CustomerMood.Thinking => "<color=#ffd257>Biraz pahalı...</color>",
            CustomerMood.Angry => "<color=#ff5050>Çok pahalı!</color>",
            CustomerMood.Empty => "<color=#bbbbbb>Raflar boş...</color>",
            CustomerMood.Paid => "<color=#7CFC7C>Teşekkürler!</color>",
            CustomerMood.Grumble => "<color=#ffd257>Peki... alayım.</color>",
            CustomerMood.Impatient => "<color=#ff5050>Bu kadar beklenmez!</color>",
            CustomerMood.Refused => "<color=#ff5050>O zaman almıyorum!</color>",
            CustomerMood.JustLooking => "<color=#bbbbbb>Sadece bakıyorum.</color>",
            CustomerMood.MaybeLater => "<color=#bbbbbb>Hmm, belki sonra...</color>",
            CustomerMood.Scared => "<color=#ff5050>Ayy! Gidiyorum buradan!</color>",
            _ => "",
        };
    }
}
