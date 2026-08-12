using UnityEngine;
using UnityEngine.InputSystem;

// Third-person orbit camera around the goblin.
// LOCKED to third-person 2026-08-10 per explicit request (the free-fly spectator mode this
// used to toggle into via Tab was removed entirely): E/Q are now the arm-balance controls
// (GoblinCarryRig), so a camera mode that also read E/Q for fly-up/down would conflict with them.
public class CarryCameraRig : MonoBehaviour
{
    public static CarryCameraRig Instance { get; private set; }

    public Transform target;
    // Pulled back and re-centered 2026-08-10 ("カメラが近すぎる、全身映るところまで") --
    // the goblin is only ~1.4m tall, so the old lookOffset.y=1.5 aimed above its head, and
    // distance=4.5 was too close to fit the full body in frame.
    public Vector3 lookOffset = new Vector3(0f, 0.8f, 0f);
    public float distance = 6.5f;
    public float minDistance = 1.0f;
    public float maxDistance = 10f;
    public float mouseSensitivity = 2.5f;
    public float minPitch = -30f;
    public float maxPitch = 60f;
    public float followLerp = 15f;

    // Always true now -- kept as a property (rather than removed outright) because
    // GoblinLocomotion.cs reads it to decide whether to process movement input.
    public bool IsThirdPerson => true;
    public float Yaw { get; private set; }
    public float defaultPitch = 15f;
    float pitch = 15f;

    void Awake()
    {
        Instance = this;
        Yaw = transform.eulerAngles.y;
        pitch = defaultPitch;
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;

        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = locked;
        }
        // カメラリセット(R): アニメーション遷移図.png "カメラをプレイヤー後方にリセット".
        if (kb != null && kb.rKey.wasPressedThisFrame && target != null)
        {
            Yaw = target.eulerAngles.y;
            pitch = defaultPitch;
        }

        Vector2 mouseDelta = (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            ? mouse.delta.ReadValue() : Vector2.zero;
        Yaw += mouseDelta.x * mouseSensitivity * 0.02f;
        pitch -= mouseDelta.y * mouseSensitivity * 0.02f;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        if (target == null) return;
        Quaternion rot = Quaternion.Euler(pitch, Yaw, 0f);
        Vector3 focus = target.position + lookOffset;
        Vector3 desiredPos = focus - rot * Vector3.forward * distance;

        RaycastHit hit;
        if (Physics.Linecast(focus, desiredPos, out hit))
        {
            float hitDist = Mathf.Clamp(hit.distance, minDistance, maxDistance);
            desiredPos = focus - rot * Vector3.forward * hitDist;
        }

        transform.position = Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-followLerp * Time.deltaTime));
        transform.rotation = rot;
    }
}
