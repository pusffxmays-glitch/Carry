using UnityEngine;

// Third-person camera behind the goblin.
// LOCKED to third-person 2026-08-10 per explicit request (the free-fly spectator mode this
// used to toggle into via Tab was removed entirely): E/Q are now the arm-balance controls
// (GoblinCarryRig), so a camera mode that also read E/Q for fly-up/down would conflict with them.
//
// FIXED 2026-08-12 per explicit request ("カメラをゴブリンの後ろ斜め上に固定したい。ツボの中身と
// ゴブリンの全身が見えるバランスがいいところで"): mouse-driven free orbit removed entirely. The
// camera now always sits at a fixed pitch/distance/offset directly behind whatever direction the
// goblin is currently facing (yaw continuously matches target.eulerAngles.y, smoothed so a sharp
// turn doesn't snap-cut the view) -- not player-steerable. Pitch/distance/lookOffset are tuned so
// the pot's held-up interior (where the potion liquid sim lives) and the goblin's feet are both in
// frame at once; see WORKLOG.md for the measurements this was tuned against.
public class CarryCameraRig : MonoBehaviour
{
    public static CarryCameraRig Instance { get; private set; }

    public Transform target;
    [Tooltip("World-space offset added to target.position for what the camera looks at.")]
    public Vector3 lookOffset = new Vector3(0f, 1.2f, 0f);
    [Tooltip("Fixed downward pitch (degrees) -- high enough to look down into the pot while the goblin's feet stay in frame. Lowered 2026-08-12 per request (\"カメラをもう少し下げて\") from 38 -- less steep top-down angle.")]
    public float pitch = 26f;
    [Tooltip("Pulled back further 2026-08-12 per request (\"カメラもっと引きじゃないと全然見えない\", then \"もっと引いていい\") -- 2.7 was too tight, cropping the character; 5.5 was still not far enough.")]
    public float distance = 8f;
    public float minDistance = 1.0f;
    public float maxDistance = 10f;
    [Tooltip("How quickly the camera's yaw catches up to the goblin's current facing direction.")]
    public float yawFollowLerp = 6f;
    public float followLerp = 15f;

    // Always true now -- kept as a property (rather than removed outright) because
    // GoblinLocomotion.cs reads it to decide whether to process movement input.
    public bool IsThirdPerson => true;
    public float Yaw { get; private set; }

    void Awake()
    {
        Instance = this;
        Yaw = transform.eulerAngles.y;
    }

    void Update()
    {
        if (target == null) return;

        float targetYaw = target.eulerAngles.y;
        Yaw = Mathf.LerpAngle(Yaw, targetYaw, 1f - Mathf.Exp(-yawFollowLerp * Time.deltaTime));

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
