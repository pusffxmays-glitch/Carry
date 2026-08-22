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
    [Tooltip("遮蔽で寄るときの速さ。大きいほど機敏。")]
    public float pullInLerp = 10f;
    [Tooltip("遮蔽が消えて戻るときの速さ。ゆっくりめにすると出戻りがバタつかない。")]
    public float releaseLerp = 3f;
    float smoothedDistance = -1f;
    /// <summary>デバッグ: 最後にカメラを遮った物と時刻 (「急に寄る」調査用)。</summary>
    public string LastBlocker { get; private set; } = "";
    public float LastBlockTime { get; private set; } = -1f;

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

        // FIXED 2026-08-22 (バグ報告「橋から道へ移るあたりでカメラがゴブリンに寄る」):
        // トリガー (Checkpoint_Start など、通過判定用の見えない箱) を遮蔽物として
        // 拾っていた。遮蔽判定は実体のあるコライダーのみにする。
        // 2026-08-22 追補: 距離の変化を平滑化する。従来は遮蔽した瞬間に距離が
        // 1 フレームで飛び「急に寄ってくる」カットになっていた。寄りは速め、
        // 戻りはゆっくりのドリーにする。木の枝や岩を掠めた 1-2 フレームの誤遮蔽も
        // これで目立たなくなる。
        // FIXED 2026-08-22: 自分自身 (ゴブリンの CharacterController) を遮蔽物として拾い、
        // 歩行中に散発的へ最小距離まで飛び付く「急に寄ってくる」バグがあった (実測で
        // LastBlocker='Goblin' を確認)。ターゲット配下のコライダーは遮蔽判定から除外する。
        float targetDist = distance;
        Vector3 dir = desiredPos - focus;
        float span = dir.magnitude;
        var occluders = Physics.RaycastAll(focus, dir / Mathf.Max(span, 1e-5f), span,
                                           Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (var h in occluders)
        {
            if (h.collider.transform == target || h.collider.transform.IsChildOf(target)) continue;
            float d2 = Mathf.Clamp(h.distance, minDistance, maxDistance);
            if (d2 < targetDist)
            {
                targetDist = d2;
                LastBlocker = h.collider.name;   // デバッグ: 何に遮られて寄ったか
                LastBlockTime = Time.time;
            }
        }
        if (smoothedDistance < 0f) smoothedDistance = targetDist;
        float distRate = targetDist < smoothedDistance ? pullInLerp : releaseLerp;
        smoothedDistance = Mathf.Lerp(smoothedDistance, targetDist,
                                      1f - Mathf.Exp(-distRate * Time.deltaTime));
        desiredPos = focus - rot * Vector3.forward * smoothedDistance;

        transform.position = Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-followLerp * Time.deltaTime));
        transform.rotation = rot;
    }
}
