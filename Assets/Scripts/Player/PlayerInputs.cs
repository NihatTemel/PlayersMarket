using UnityEngine;
using UnityEngine.InputSystem;

// Oyuncu girdileri: proje genelindeki InputSystem_Actions asset'inin "Player" haritasi
// (Project Settings > Input System Package). Tus atamalari o asset'ten degistirilir,
// klavye/fare ve gamepad ayni eylemlerden okunur.
public static class PlayerInputs
{
    static InputActionAsset cachedAsset;
    static InputAction move, look, jump, sprint, interact, drop, attack, inventory, price;
    static InputAction basicAttack, skill1, skill2;

    static bool Ready()
    {
        InputActionAsset asset = InputSystem.actions;
        if (asset == null) return false;
        if (asset != cachedAsset)
        {
            cachedAsset = asset;
            move = asset.FindAction("Player/Move");
            look = asset.FindAction("Player/Look");
            jump = asset.FindAction("Player/Jump");
            sprint = asset.FindAction("Player/Sprint");
            interact = asset.FindAction("Player/Interact");
            drop = asset.FindAction("Player/Drop");
            attack = asset.FindAction("Player/Attack");
            inventory = asset.FindAction("Player/Inventory");
            price = asset.FindAction("Player/Price");
            basicAttack = asset.FindAction("Player/BasicAttack");
            skill1 = asset.FindAction("Player/Skill1");
            skill2 = asset.FindAction("Player/Skill2");
            if (basicAttack == null || skill1 == null || skill2 == null)
                Debug.LogError("[PlayerInputs] InputSystem_Actions icinde Player/BasicAttack, Skill1, Skill2 olmali.");
            if (move == null || look == null || jump == null || sprint == null)
                Debug.LogError("[PlayerInputs] InputSystem_Actions icinde Player/Move, Look, Jump, Sprint olmali.");
            if (interact == null || drop == null || attack == null || inventory == null)
                Debug.LogError("[PlayerInputs] InputSystem_Actions icinde Player/Interact, Drop, Attack, Inventory olmali.");
        }
        return true;
    }

    public static Vector2 Move => Ready() && move != null ? Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f) : Vector2.zero;
    public static bool JumpPressed => Ready() && jump != null && jump.WasPressedThisFrame();
    public static bool JumpHeld => Ready() && jump != null && jump.IsPressed();
    public static bool SprintHeld => Ready() && sprint != null && sprint.IsPressed();
    public static bool InteractPressed => Ready() && interact != null && interact.WasPressedThisFrame();
    public static bool DropPressed => Ready() && drop != null && drop.WasPressedThisFrame();
    public static bool ThrowPressed => Ready() && attack != null && attack.WasPressedThisFrame();
    public static bool InventoryPressed => Ready() && inventory != null && inventory.WasPressedThisFrame();
    public static bool PricePressed => Ready() && price != null && price.WasPressedThisFrame();
    // Savas: duz vurus (R / 1, basili tutunca seri), skill (2, 3)
    public static bool BasicAttackHeld => Ready() && basicAttack != null && basicAttack.IsPressed();
    public static bool Skill1Pressed => Ready() && skill1 != null && skill1.WasPressedThisFrame();
    public static bool Skill2Pressed => Ready() && skill2 != null && skill2.WasPressedThisFrame();

    // Bakis: fare piksel farki (kare basina) ile cubuk degeri (-1..1, saniye basina hiz)
    // farkli olcekte; fromStick ile hangisi oldugunu soyler.
    public static Vector2 Look(out bool fromStick)
    {
        fromStick = false;
        if (!Ready() || look == null) return Vector2.zero;
        Vector2 v = look.ReadValue<Vector2>();
        InputDevice device = look.activeControl?.device;
        fromStick = device is Gamepad || device is Joystick;
        return v;
    }
}
