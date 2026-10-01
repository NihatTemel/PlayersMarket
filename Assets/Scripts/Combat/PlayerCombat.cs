using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

// Savas: duz vurus (R / 1, basili tutunca seri) + 2 skill (2, 3). Yetenekler silaha gore (AbilityBook).
// Yerel oyuncu: bekleme suresini kendisi tahmin eder (skill cubugu), efekti ve hareketi (atilma vb.) gecikmesiz
// oynar, sunucuya CmdUse gonderir. Sunucu: bekleme/konum dogrular, hasari hesaplar (statlar, kritik, zirh,
// kombo, arkadan vurus) ve hedefe uygular; efekti diger makinelere RpcFx ile yayar.
// Elde esya varken ya da bir panel acikken saldiri yok.
public class PlayerCombat : NetworkBehaviour
{
    public const int Slots = 3;

    PlayerInventory inventory;
    PlayerCarry carry;
    PlayerMovement movement;
    SkillBarUI bar;

    readonly float[] localReadyAt = new float[Slots];   // istemci (UI)
    readonly double[] serverReadyAt = new double[Slots]; // sunucu (gercek)
    int comboCount;
    double lastBasicTime;

    static readonly Collider[] overlap = new Collider[64];
    static readonly RaycastHit[] castHits = new RaycastHit[32];

    void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        carry = GetComponent<PlayerCarry>();
        movement = GetComponent<PlayerMovement>();
    }

    public override void OnStartLocalPlayer() => bar = SkillBarUI.Create(this);

    public override void OnStopLocalPlayer()
    {
        if (bar != null) Destroy(bar.gameObject);
    }

    // ---------------- Istemci ----------------

    public WeaponType CurrentWeapon =>
        inventory != null && inventory.equipment.Count > 0 ? ItemRules.CharacterStats(inventory.equipment).weapon : WeaponType.None;

    public AbilityDef GetAbility(int i) => AbilityBook.Get(CurrentWeapon, i);
    public float CooldownTotal(int i) => AbilityBook.Cooldown(CurrentWeapon, i);
    public float CooldownRemaining(int i) => Mathf.Max(0f, localReadyAt[i] - Time.time);
    public bool CanAttack => !UIState.Open && (carry == null || carry.Held == null);

    void Update()
    {
        if (!isLocalPlayer || !CanAttack) return;
        WeaponType w = CurrentWeapon;
        if (PlayerInputs.BasicAttackHeld) TryUse(0, w);
        if (PlayerInputs.Skill1Pressed) TryUse(1, w);
        if (PlayerInputs.Skill2Pressed) TryUse(2, w);
    }

    void TryUse(int i, WeaponType w)
    {
        AbilityDef a = AbilityBook.Get(w, i);
        if (a == null || Time.time < localReadyAt[i]) return;
        localReadyAt[i] = Time.time + AbilityBook.Cooldown(w, i);

        Vector3 aim = AimDirection();
        Vector3 flat = Flat(aim);
        transform.rotation = Quaternion.LookRotation(flat); // saldiri yonune don
        Vector3 origin = transform.position;

        // Hareketli yetenekler yerelde (konum istemcide yetkili)
        if (movement != null)
        {
            switch (a.kind)
            {
                case AbilityKind.Dash: movement.Dash(flat * (a.moveDistance / 0.3f), 0.3f); break;
                case AbilityKind.Blink: movement.Dash(flat * (a.moveDistance / 0.12f), 0.12f); break;
                case AbilityKind.LeapBack: movement.Dash(-flat * (a.moveDistance / 0.45f), 0.45f, 6f); break;
            }
        }

        CombatFx.Play(a, origin, aim);
        CmdUse((byte)i, origin, aim);
    }

    // Kamera merkezinden nisan: 60 m icinde ilk carpan nokta (kendi collider'imiz haric), gogusten oraya yon
    Vector3 AimDirection()
    {
        Camera c = Camera.main;
        Vector3 chest = transform.position + Vector3.up * 1.2f;
        if (c == null) return transform.forward;
        Ray ray = c.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 target = ray.GetPoint(40f);
        int n = Physics.RaycastNonAlloc(ray, castHits, 60f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int k = 0; k < n; k++)
        {
            RaycastHit h = castHits[k];
            if (h.collider.transform.IsChildOf(transform) || h.distance >= best) continue;
            // kameranin oyuncunun arkasinda kalan engellerine nisan alma
            if (Vector3.Dot(h.point - chest, ray.direction) < 0.5f) continue;
            best = h.distance;
            target = h.point;
        }
        Vector3 d = target - chest;
        return d.sqrMagnitude > 0.01f ? d.normalized : transform.forward;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    // ---------------- Sunucu ----------------

    [Command]
    void CmdUse(byte index, Vector3 origin, Vector3 aim)
    {
        if (index >= Slots || inventory == null) return;
        ItemRules.Stats st = ItemRules.CharacterStats(inventory.equipment);
        WeaponType w = st.weapon;
        AbilityDef a = AbilityBook.Get(w, index);
        if (a == null) return;
        if (carry != null && carry.ServerHeld() != null) return;

        double now = NetworkTime.time;
        if (now < serverReadyAt[index] - 0.15) return; // gecikme payi
        serverReadyAt[index] = now + AbilityBook.Cooldown(w, index);

        // Hile/gecikme siniri: bildirilen baslangic sunucudaki konuma yakin olmali
        if ((origin - transform.position).sqrMagnitude > 3f * 3f) origin = transform.position;
        aim = aim.sqrMagnitude > 0.001f ? aim.normalized : transform.forward;

        RpcFx((byte)w, index, origin, aim);
        Execute(a, w, index, st, origin, aim);
    }

    [ClientRpc]
    void RpcFx(byte weapon, byte index, Vector3 origin, Vector3 aim)
    {
        if (isLocalPlayer) return; // yerel oyuncu zaten oynatti
        try { CombatFx.Play(AbilityBook.Get((WeaponType)weapon, index), origin, aim); }
        catch (System.Exception e) { Debug.LogException(e); }
    }

    [Server]
    void Execute(AbilityDef a, WeaponType w, int index, ItemRules.Stats st, Vector3 origin, Vector3 aim)
    {
        Vector3 flat = Flat(aim);
        Vector3 chest = origin + Vector3.up * 1.2f;
        float dmg = st.attack * a.damage;

        // Kilic kombosu: her 3. duz vurus
        if (w == WeaponType.Sword && index == 0)
        {
            double now = NetworkTime.time;
            if (now - lastBasicTime > 2.0) comboCount = 0;
            lastBasicTime = now;
            if (++comboCount % 3 == 0) dmg *= AbilityBook.SwordComboMultiplier;
        }
        if (w == WeaponType.Bow && AbilityBook.IsRanged(a.kind)) dmg *= AbilityBook.RangedPassive;

        var hit = new HashSet<IDamageable>();
        switch (a.kind)
        {
            case AbilityKind.Melee:
                foreach (IDamageable t in Overlap(chest, a.range + 0.6f))
                {
                    Vector3 to = Flat(t.Transform.position - origin);
                    float dist = Vector3.Distance(Flatten(t.Transform.position), Flatten(origin));
                    if (dist > a.range + 0.4f) continue;
                    if (dist > 0.8f && Vector3.Angle(flat, to) > a.angle * 0.5f) continue;
                    float d = dmg;
                    if (a.backstab && IsBehind(t.Transform, origin)) d *= 2f;
                    Apply(t, d, st, t.Transform.position + Vector3.up * 1.4f, hit);
                }
                break;

            case AbilityKind.Spin:
            case AbilityKind.Nova:
                foreach (IDamageable t in Overlap(origin + Vector3.up, a.radius))
                    Apply(t, dmg, st, t.Transform.position + Vector3.up * 1.4f, hit);
                break;

            case AbilityKind.Dash:
            {
                float dist = ClearDistance(chest, flat, a.moveDistance);
                Vector3 end = chest + flat * dist;
                int n = Physics.OverlapCapsuleNonAlloc(chest, end, a.radius, overlap, ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    IDamageable t = overlap[k].GetComponentInParent<IDamageable>();
                    if (t != null) Apply(t, dmg, st, t.Transform.position + Vector3.up * 1.4f, hit);
                }
                break;
            }

            case AbilityKind.Blink:
            {
                Vector3 dest = chest + flat * ClearDistance(chest, flat, a.moveDistance);
                foreach (IDamageable t in Overlap(dest, a.radius))
                    Apply(t, dmg, st, t.Transform.position + Vector3.up * 1.4f, hit);
                break;
            }

            case AbilityKind.Projectile:
            case AbilityKind.LeapBack:
                int count = Mathf.Max(1, a.projectiles);
                for (int k = 0; k < count; k++)
                {
                    float yaw = count > 1 ? Mathf.Lerp(-a.spread * 0.5f, a.spread * 0.5f, k / (float)(count - 1)) : 0f;
                    StartCoroutine(ProjectileHit(chest, Quaternion.AngleAxis(yaw, Vector3.up) * aim, a, dmg, st));
                }
                break;
        }
    }

    // Mermi: ucus suresi kadar bekleyip carpar; alanliysa carpma noktasinda patlar
    IEnumerator ProjectileHit(Vector3 from, Vector3 dir, AbilityDef a, float dmg, ItemRules.Stats st)
    {
        float dist = a.range;
        IDamageable primary = null;
        int n = Physics.SphereCastNonAlloc(from, 0.2f, dir, castHits, a.range, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int k = 0; k < n; k++)
        {
            RaycastHit h = castHits[k];
            if (h.distance <= 0f || h.distance >= best) continue;
            if (h.collider.GetComponentInParent<PlayerCombat>() != null) continue; // oyuncular (takim) engel degil
            best = h.distance;
            dist = h.distance;
            primary = h.collider.GetComponentInParent<IDamageable>();
        }

        yield return new WaitForSeconds(dist / Mathf.Max(1f, a.speed));

        Vector3 point = from + dir * dist;
        var hit = new HashSet<IDamageable>();
        if (primary != null && primary.IsAlive) Apply(primary, dmg, st, point, hit);
        if (a.radius > 0f)
            foreach (IDamageable t in Overlap(point, a.radius))
                Apply(t, dmg * a.splashFactor, st, t.Transform.position + Vector3.up * 1.4f, hit);
    }

    [Server]
    void Apply(IDamageable t, float dmg, ItemRules.Stats st, Vector3 point, HashSet<IDamageable> already)
    {
        if (t == null || !t.IsAlive || already.Contains(t)) return;
        already.Add(t);
        bool crit = Random.value < st.crit;
        if (crit) dmg *= AbilityBook.CritMultiplier;
        int final = Mathf.Max(1, Mathf.RoundToInt(dmg * ItemRules.DamageTakenMultiplier(t.Armor)));
        t.ServerTakeDamage(final, crit, netId, point);
    }

    static List<IDamageable> Overlap(Vector3 center, float radius)
    {
        var list = new List<IDamageable>();
        int n = Physics.OverlapSphereNonAlloc(center, radius, overlap, ~0, QueryTriggerInteraction.Ignore);
        for (int k = 0; k < n; k++)
        {
            IDamageable t = overlap[k].GetComponentInParent<IDamageable>();
            if (t != null && !list.Contains(t)) list.Add(t);
        }
        return list;
    }

    // Atilma/isinlanma yolunda duvara kadar olan mesafe (oyuncular ve hedefler engel sayilmaz)
    static float ClearDistance(Vector3 from, Vector3 dir, float max)
    {
        float dist = max;
        int n = Physics.SphereCastNonAlloc(from, 0.3f, dir, castHits, max, ~0, QueryTriggerInteraction.Ignore);
        for (int k = 0; k < n; k++)
        {
            Collider c = castHits[k].collider;
            if (castHits[k].distance <= 0f) continue;
            if (c.GetComponentInParent<IDamageable>() != null || c.GetComponentInParent<PlayerCombat>() != null) continue;
            dist = Mathf.Min(dist, castHits[k].distance);
        }
        return dist;
    }

    static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // Hedefin arkasinda miyiz (hedefin baktigi yonun tersinden geliyor muyuz)
    static bool IsBehind(Transform target, Vector3 attackerPos)
    {
        Vector3 toAttacker = Flat(attackerPos - target.position);
        return Vector3.Dot(Flat(target.forward), toAttacker) < -0.3f;
    }
}
