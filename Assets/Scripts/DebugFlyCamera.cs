using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================================================
// DebugFlyCamera -- F8 でトグルする確認用フリーカメラ (2026-08-21)。
//
// ヒートマップなどのデバッグ表示を Game ビューで自由に見て回るためのもの。
// ON にすると Time.timeScale = 0 で世界を止め (ゴブリンも流体も静止)、
// カメラだけが unscaled 時間で動く。もう一度 F8 で元のカメラとゲームに戻る。
//
//   F8         : ON/OFF
//   W/A/S/D    : 前後左右   E/Q : 上昇/下降   左Shift : 高速
//   右ドラッグ  : 視点回転
//
// timeScale を止めるので WASD がゴブリンの移動と衝突しない (locomotion は dt=0 で不動)。
// ============================================================================================
public class DebugFlyCamera : MonoBehaviour
{
    public Key toggleKey = Key.F8;
    public float moveSpeed = 8f;
    public float fastMultiplier = 4f;
    [Tooltip("右ドラッグの視点回転速度 (deg / mouse px)。")]
    public float lookSpeed = 0.15f;

    Camera cam;
    bool active;
    float prevTimeScale = 1f;
    float yaw, pitch;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = gameObject.AddComponent<Camera>();
        cam.enabled = false;
        cam.depth = 99;   // ON の間はメインカメラより手前に描く (メインは無効化しない)
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb[toggleKey].wasPressedThisFrame) Toggle();
        if (!active) return;

        float dt = Time.unscaledDeltaTime;   // timeScale 0 でも動けるように unscaled
        var move = Vector3.zero;
        if (kb.wKey.isPressed) move += transform.forward;
        if (kb.sKey.isPressed) move -= transform.forward;
        if (kb.aKey.isPressed) move -= transform.right;
        if (kb.dKey.isPressed) move += transform.right;
        if (kb.eKey.isPressed) move += Vector3.up;
        if (kb.qKey.isPressed) move -= Vector3.up;
        float sp = moveSpeed * (kb.leftShiftKey.isPressed ? fastMultiplier : 1f);
        transform.position += move * (sp * dt);

        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 d = mouse.delta.ReadValue();
            yaw += d.x * lookSpeed;
            pitch = Mathf.Clamp(pitch - d.y * lookSpeed, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }

    /// <summary>ON/OFF 切替。ON でメインカメラ位置から開始し、世界を一時停止する。</summary>
    public void Toggle()
    {
        active = !active;
        if (active)
        {
            var main = Camera.main;
            if (main != null && main.transform != transform)
            {
                transform.position = main.transform.position;
                transform.rotation = main.transform.rotation;
            }
            var e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
            prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            cam.enabled = true;
        }
        else
        {
            Time.timeScale = prevTimeScale;
            cam.enabled = false;
        }
    }
}
