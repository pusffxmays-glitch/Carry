using UnityEngine;
using UnityEngine.InputSystem;

// Third-person orbit camera around the goblin, with a free-fly "spectator" mode
// (toggle with Tab) for inspecting the goblin's movement from any angle.
public class CarryCameraRig : MonoBehaviour
{
    public static CarryCameraRig Instance { get; private set; }

    public Transform target;
    public Vector3 lookOffset = new Vector3(0f, 1.5f, 0f);
    public float distance = 4.5f;
    public float minDistance = 1.0f;
    public float maxDistance = 8f;
    public float mouseSensitivity = 2.5f;
    public float minPitch = -30f;
    public float maxPitch = 60f;
    public float followLerp = 15f;

    public float freeMoveSpeed = 8f;
    public float freeFastMultiplier = 3f;

    public bool IsThirdPerson { get; private set; } = true;
    public float Yaw { get; private set; }
    float pitch = 15f;

    void Awake()
    {
        Instance = this;
        Yaw = transform.eulerAngles.y;
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

        if (kb != null && kb.tabKey.wasPressedThisFrame)
        {
            IsThirdPerson = !IsThirdPerson;
        }
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = locked;
        }

        Vector2 mouseDelta = (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            ? mouse.delta.ReadValue() : Vector2.zero;
        Yaw += mouseDelta.x * mouseSensitivity * 0.02f;
        pitch -= mouseDelta.y * mouseSensitivity * 0.02f;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        if (IsThirdPerson) UpdateThirdPerson();
        else UpdateFreeFly(kb);
    }

    void UpdateThirdPerson()
    {
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

    void UpdateFreeFly(Keyboard kb)
    {
        transform.rotation = Quaternion.Euler(pitch, Yaw, 0f);

        Vector3 move = Vector3.zero;
        if (kb != null)
        {
            if (kb.wKey.isPressed) move += transform.forward;
            if (kb.sKey.isPressed) move -= transform.forward;
            if (kb.aKey.isPressed) move -= transform.right;
            if (kb.dKey.isPressed) move += transform.right;
            if (kb.eKey.isPressed) move += Vector3.up;
            if (kb.qKey.isPressed) move -= Vector3.up;
        }

        float speed = freeMoveSpeed;
        if (kb != null && kb.leftShiftKey.isPressed) speed *= freeFastMultiplier;

        transform.position += move.normalized * speed * Time.deltaTime;
    }
}
