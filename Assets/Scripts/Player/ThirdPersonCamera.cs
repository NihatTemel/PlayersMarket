using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 3. sahis yorunge kamerasi (Main Camera uzerinde).
// Fare / sag cubuk ile doner, tekerlek ile yakinlasir, duvara girmemek icin SphereCast ile yaklasir.
// Imlec: oyunda kilitli; Esc ile serbest, arayuz disinda bir yere tiklayinca tekrar kilitli.
// Yerel oyuncu AttachToMain ile baglar; sahnede bilesen yoksa kendisi ekler
// (ince ayar icin Game sahnesindeki Main Camera'ya elle de eklenebilir).
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Hedef")]
    public Transform target;
    public Vector3 pivotOffset = new Vector3(0f, 1.6f, 0f);
    public float followSmoothTime = 0.04f;

    [Header("Donus")]
    public float mouseSensitivity = 0.12f;   // derece / piksel
    public float stickSensitivity = 180f;    // derece / saniye
    public bool invertY;
    public float minPitch = -35f;
    public float maxPitch = 70f;
    public float startPitch = 15f;

    [Header("Mesafe")]
    public float distance = 5f;
    public float minDistance = 2f;
    public float maxDistance = 9f;
    public float zoomStep = 0.6f;            // tekerlek centigi basina metre
    public float zoomSmooth = 10f;

    [Header("Carpisma")]
    public LayerMask collisionMask = ~0;
    public float collisionRadius = 0.25f;
    public float collisionPadding = 0.1f;
    public float minCollisionDistance = 0.4f;

    public float Yaw => yaw;
    public bool CursorLocked => cursorLocked;

    float yaw;
    float pitch;
    float wantedDistance;
    float currentDistance;
    Vector3 pivot;
    Vector3 pivotVelocity;
    bool cursorLocked;
    bool uiWasOpen;
    readonly RaycastHit[] hits = new RaycastHit[16];

    public static ThirdPersonCamera AttachToMain(Transform target)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[ThirdPersonCamera] Main Camera bulunamadi.");
            return null;
        }
        ThirdPersonCamera tpc = cam.GetComponent<ThirdPersonCamera>();
        if (tpc == null) tpc = cam.gameObject.AddComponent<ThirdPersonCamera>();
        tpc.SetTarget(target);
        return tpc;
    }

    public void SetTarget(Transform t)
    {
        target = t;
        if (t == null)
        {
            SetCursorLocked(false);
            return;
        }

        yaw = t.eulerAngles.y;
        pitch = startPitch;
        wantedDistance = currentDistance = Mathf.Clamp(distance, minDistance, maxDistance);
        pivot = t.position + pivotOffset;
        pivotVelocity = Vector3.zero;
        SetCursorLocked(true);
        ApplyTransform(Quaternion.Euler(pitch, yaw, 0f), currentDistance);
    }

    void OnDisable()
    {
        SetCursorLocked(false);
    }

    void LateUpdate()
    {
        if (target == null) return;
        float dt = Time.deltaTime;

        if (UIState.Open)
        {
            // Panel acik: imlec serbest, kamera donmez
            if (cursorLocked) SetCursorLocked(false);
            uiWasOpen = true;
        }
        else if (uiWasOpen)
        {
            // Panel bu kare kapandi: tekrar kilitle (ayni karedeki Esc'i imlec icin sayma)
            uiWasOpen = false;
            SetCursorLocked(true);
        }
        else
        {
            HandleCursor();
            ReadLook(dt);
            ReadZoom();
        }

        pivot = Vector3.SmoothDamp(pivot, target.position + pivotOffset, ref pivotVelocity, followSmoothTime);
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        float allowed = CollisionDistance(rot * Vector3.back, wantedDistance);

        // Engele dogru ani yaklas, engel kalkinca yumusak uzaklas
        currentDistance = allowed < currentDistance
            ? allowed
            : Mathf.Lerp(currentDistance, allowed, 1f - Mathf.Exp(-zoomSmooth * dt));

        ApplyTransform(rot, currentDistance);
    }

    void ReadLook(float dt)
    {
        Vector2 look = PlayerInputs.Look(out bool fromStick);
        if (fromStick)
        {
            look *= stickSensitivity * dt;
        }
        else
        {
            if (!cursorLocked) return; // imlec serbestken fare kamerayi dondurmesin
            look *= mouseSensitivity;
        }

        yaw = Mathf.Repeat(yaw + look.x, 360f);
        pitch += invertY ? look.y : -look.y;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    void ReadZoom()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !cursorLocked) return;
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.01f) return;
        // Windows'ta bir centik ~120 birim
        wantedDistance = Mathf.Clamp(wantedDistance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
    }

    float CollisionDistance(Vector3 dir, float maxDist)
    {
        float allowed = maxDist;
        int n = Physics.SphereCastNonAlloc(pivot, collisionRadius, dir, hits, maxDist, collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = hits[i];
            // Oyuncular (CharacterController) kamerayi itmesin; baslangicta icinde olunan collider'lar da sayilmaz
            if (h.collider is CharacterController) continue;
            if (h.collider.GetComponentInParent<WorldItem>() != null) continue; // esyalar kamerayi itmesin
            if (h.distance <= 0f) continue;
            if (h.transform.IsChildOf(target)) continue;
            allowed = Mathf.Min(allowed, h.distance - collisionPadding);
        }
        return Mathf.Max(allowed, minCollisionDistance);
    }

    void ApplyTransform(Quaternion rot, float dist)
    {
        transform.SetPositionAndRotation(pivot + rot * Vector3.back * dist, rot);
    }

    void HandleCursor()
    {
        Keyboard k = Keyboard.current;
        Mouse m = Mouse.current;

        if (cursorLocked)
        {
            if (k != null && k.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
        }
        else if (m != null && m.leftButton.wasPressedThisFrame && !PointerOverUI())
        {
            SetCursorLocked(true);
        }
    }

    static bool PointerOverUI()
    {
        EventSystem es = EventSystem.current;
        return es != null && es.IsPointerOverGameObject();
    }

    void SetCursorLocked(bool locked)
    {
        cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
