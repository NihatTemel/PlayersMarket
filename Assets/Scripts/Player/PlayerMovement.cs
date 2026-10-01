using System;
using Mirror;
using UnityEngine;

// 3. sahis hareket: kameraya gore yurume/kosma, ziplama, zemin kontrolu.
// Sadece yerel oyuncuda calisir; konum NetworkTransformReliable (ClientToServer) ile diger
// oyunculara gider. Diger oyuncularda bu bilesen bir sey yapmaz.
//
// Ziplama: coyote time (kenardan dustukten hemen sonra da ziplayabilme) + jump buffer
// (yere inmeden hemen once basilan tus kaybolmasin) + degisken yukseklik (tusu erken birakinca
// daha alcak). Zemin: CharacterController.isGrounded yerine ayak altinda kucuk bir kure kontrolu.
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : NetworkBehaviour
{
    [Header("Hareket")]
    public float walkSpeed = 4.5f;
    public float sprintSpeed = 7.5f;
    [Tooltip("Hedef hiza ulasma hizi (saniyede hizin kac kati). Yuksek = daha keskin.")]
    public float groundAcceleration = 12f;
    public float airAcceleration = 4f;
    public float turnSmoothTime = 0.08f;

    [Header("Ziplama")]
    public float jumpHeight = 1.3f;
    public float gravity = -25f;
    public float fallGravityMultiplier = 1.8f;
    public float lowJumpGravityMultiplier = 2.2f;
    public float maxFallSpeed = 30f;
    public float coyoteTime = 0.12f;
    public float jumpBufferTime = 0.15f;

    [Header("Zemin kontrolu")]
    public LayerMask groundMask = ~0;
    [Tooltip("CharacterController yaricapindan biraz kucuk olmali (duvarlari zemin sanmasin).")]
    public float groundCheckRadius = 0.35f;
    [Tooltip("Kurenin ayak altina ne kadar tastigi.")]
    public float groundCheckDepth = 0.12f;

    public bool IsGrounded { get; private set; }
    public bool IsSprinting { get; private set; }
    public Vector3 Velocity => velocity;
    public float HorizontalSpeed => new Vector2(velocity.x, velocity.z).magnitude;

    // Ileride animasyon/ses icin
    public event Action Jumped;
    public event Action Landed;

    CharacterController controller;
    PlayerCarry carry;
    ThirdPersonCamera cam;
    Vector3 velocity;
    float turnVelocity;
    float lastGroundedTime = -10f;
    float lastJumpPressedTime = -10f;
    bool jumping;
    readonly Collider[] groundHits = new Collider[8];

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        carry = GetComponent<PlayerCarry>();
    }

    public override void OnStartLocalPlayer()
    {
        cam = ThirdPersonCamera.AttachToMain(transform);
    }

    public override void OnStopLocalPlayer()
    {
        if (cam != null) cam.SetTarget(null);
        cam = null;
    }

    void Update()
    {
        if (!isLocalPlayer) return;
        float dt = Time.deltaTime;
        float now = Time.time;

        // --- Zemin ---
        bool wasGrounded = IsGrounded;
        IsGrounded = CheckGround();
        if (IsGrounded)
        {
            lastGroundedTime = now;
            if (!wasGrounded)
            {
                jumping = false;
                Landed?.Invoke();
            }
        }

        // --- Yetenek hareketi (atilma / golge adimi / geri sicrama): girdiyi ezer ---
        if (now < dashUntil)
        {
            if (!IsGrounded || dashVelocity.y > 0f) velocity.y = Mathf.Max(velocity.y + gravity * dt, -maxFallSpeed);
            else velocity.y = -2f;
            Vector3 dv = new Vector3(dashVelocity.x, 0f, dashVelocity.z);
            controller.Move((dv + Vector3.up * velocity.y) * dt);
            velocity.x = velocity.z = 0f;
            return;
        }

        // --- Yatay hareket (kameraya gore) ---
        Vector2 input = PlayerInputs.Move;
        float yaw = cam != null ? cam.Yaw : transform.eulerAngles.y;
        Vector3 wish = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y);

        IsSprinting = PlayerInputs.SprintHeld && wish.sqrMagnitude > 0.01f;
        float speed = IsSprinting ? sprintSpeed : walkSpeed;
        if (carry != null) speed *= carry.SpeedMultiplier; // buyuk esya tasirken yavas
        float accel = (IsGrounded ? groundAcceleration : airAcceleration) * speed;

        Vector3 horizontal = Vector3.MoveTowards(new Vector3(velocity.x, 0f, velocity.z), wish * speed, accel * dt);
        velocity.x = horizontal.x;
        velocity.z = horizontal.z;

        if (wish.sqrMagnitude > 0.01f)
        {
            float targetAngle = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnVelocity, turnSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);
        }

        // --- Ziplama ---
        if (PlayerInputs.JumpPressed) lastJumpPressedTime = now;

        bool buffered = now - lastJumpPressedTime <= jumpBufferTime;
        bool coyote = now - lastGroundedTime <= coyoteTime;
        bool canJump = carry == null || carry.CanJump;
        if (buffered && coyote && !jumping && canJump)
        {
            velocity.y = Mathf.Sqrt(2f * -gravity * jumpHeight);
            jumping = true;
            IsGrounded = false;
            lastJumpPressedTime = -10f;
            lastGroundedTime = -10f;
            Jumped?.Invoke();
        }

        // --- Yercekimi ---
        if (IsGrounded && velocity.y <= 0f)
        {
            velocity.y = -2f; // zemine yapisik kal (yokus asagi zipzip olmasin)
        }
        else
        {
            float g = gravity;
            if (velocity.y < 0f) g *= fallGravityMultiplier;
            else if (!PlayerInputs.JumpHeld) g *= lowJumpGravityMultiplier;
            velocity.y = Mathf.Max(velocity.y + g * dt, -maxFallSpeed);
        }

        CollisionFlags flags = controller.Move(velocity * dt);

        // Kafa tavana degdi: yukari hizi kes
        if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f;
    }

    bool CheckGround()
    {
        // Yukselirken zemin sayma (ziplamanin ilk karesinde kure hala yere degiyor)
        if (velocity.y > 0.01f) return false;

        Vector3 center = transform.position + Vector3.up * (groundCheckRadius - groundCheckDepth);
        int n = Physics.OverlapSphereNonAlloc(center, groundCheckRadius, groundHits, groundMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = groundHits[i];
            if (c == controller || c.transform.IsChildOf(transform)) continue;
            return true;
        }
        return false;
    }

    // Isinlama: CharacterController acikken konum degisikligi ezilir → kapat, konumla, ac
    // Yetenek hareketi (sadece yerel oyuncu): duration boyunca yatay hiz sabit, carpismalar gecerli.
    // upward > 0 ise sicrama (geri sicrama).
    Vector3 dashVelocity;
    float dashUntil;
    public bool IsDashing => Time.time < dashUntil;

    public void Dash(Vector3 horizontalVelocity, float duration, float upward = 0f)
    {
        dashVelocity = new Vector3(horizontalVelocity.x, upward, horizontalVelocity.z);
        dashUntil = Time.time + duration;
        if (upward > 0f) { velocity.y = upward; jumping = true; IsGrounded = false; }
    }

    public void Teleport(Vector3 position, Quaternion rotation)
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        controller.enabled = true;
        velocity = Vector3.zero;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * (groundCheckRadius - groundCheckDepth), groundCheckRadius);
    }
}
