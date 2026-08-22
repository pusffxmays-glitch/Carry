using UnityEngine;

// 足元が細い足場かどうかを毎フレーム調べるセンサー。
// GoblinCarryRig (歩容の切り替え = 細道渡りモーション) と GoblinLocomotion (減速) の
// 両方から OnBeam / SpeedMultiplier を読まれる。
//
// 判定は 2 通り:
//   (1) NarrowBeamSurface が付いた足場 -- 明示指定。速度倍率も足場ごとに持てる。
//   (2) 幅の自動判定 (2026-08-22 追加) -- アセットをつなぎ合わせた道のうち、丸太を渡る
//       ような細い区間で、コンポーネントを貼らなくても自動的に細道渡りへ切り替える。
//       足元の接地点から左右へ autoNarrowWidth/2 だけ離れた位置に「同じ高さの足場」が
//       あるかを撃ち、片側でも無ければ細道とみなす。左右は進行方向に対する横 (transform.right)
//       なので、道に沿って歩いている限り道幅を測っていることになる。
public class NarrowBeamSensor : MonoBehaviour
{
    [Tooltip("足元判定のレイ長さ (m)。CharacterController の足元から下へ。")]
    public float rayLength = 0.6f;

    [Header("幅の自動判定")]
    // 2026-08-22: いったん **既定オフ (0)** にしてある。コースの飛び石と渡る丸太が同じ
    // 幅域にあり、ステージの仕様と合わせて条件を決め直すことになったため。判定ロジック
    // 自体は残してあるので、値を入れれば (1.1 前後) そのまま復活する。
    [Tooltip("足元の幅がこれ以下なら、NarrowBeamSurface が無くても細道とみなす (m)。0 で自動判定を切る。")]
    public float autoNarrowWidth = 0f;
    [Tooltip("左右の足場がこれ以上下へ落ちていたら「そこは道の外」とみなす段差の許容 (m)。")]
    public float edgeDropTolerance = 0.35f;
    [Tooltip("判定のばたつき防止。細道と判定されてからこの秒数は維持する。")]
    public float narrowHoldSeconds = 0.25f;
    // 2026-08-22: コースの幅を実測すると、渡る丸太 (PathLog / 倒木、4.6m 続く) と
    // 単発の飛び石 (PathRock / boulder、0.5m 程度) がどちらも 0.2-1.0m 幅で、**幅だけでは
    // 区別できない**。細道渡りのモーションを出したいのは前者なので、「細い状態が続いた
    // 時間」で切り分ける。歩き 1.5m/s なら飛び石は 0.3 秒ほどで通過するが、丸太は 3 秒
    // 掛かるので、0.4 秒ほど連続を要求すれば飛び石ではモーションが出ない。
    [Tooltip("細いと判定され続けてからモーションを切り替えるまでの時間 (s)。単発の飛び石で切り替わらないようにする。0 で即時。")]
    public float narrowEngageSeconds = 0.4f;
    [Tooltip("自動判定で細道になったときの速度倍率。NarrowBeamSurface 付きの足場ではそちらの値が優先される。")]
    [Range(0.1f, 1f)] public float autoSpeedMultiplier = 0.55f;

    public bool OnBeam { get; private set; }
    public float SpeedMultiplier { get; private set; } = 1f;
    /// <summary>自動判定 (NarrowBeamSurface ではなく幅) で細道になっているか。デバッグ表示用。</summary>
    public bool AutoNarrow { get; private set; }
    /// <summary>直近に足場ありと判定できた左右の距離 (m)。デバッグ表示用。</summary>
    public float LastLeftSupport { get; private set; }
    public float LastRightSupport { get; private set; }

    float narrowUntil;
    float narrowSince = -1f;   // 細いと判定され続けている開始時刻。-1 = 途切れている

    void Update()
    {
        OnBeam = false;
        AutoNarrow = false;
        SpeedMultiplier = 1f;
        // 少し上から下へ。自分の Collider は無視 (CharacterController は Raycast に当たるので回避)。
        Vector3 origin = transform.position + Vector3.up * 0.3f;
        // トリガー (水ボリューム等) は足場ではないので無視する
        var hits = Physics.RaycastAll(origin, Vector3.down, rayLength + 0.3f,
                                      Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        NarrowBeamSurface beam = null;
        float groundY = 0f;
        bool grounded = false;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;   // 自分自身
            if (h.distance < best)
            {
                best = h.distance;
                beam = h.collider.GetComponent<NarrowBeamSurface>();
                groundY = h.point.y;
                grounded = true;
            }
        }
        if (beam != null)
        {
            OnBeam = true;
            SpeedMultiplier = beam.speedMultiplier;
            return;   // 明示指定が最優先
        }

        if (grounded && autoNarrowWidth > 0f)
        {
            float half = autoNarrowWidth * 0.5f;
            bool rightOK = HasSupport(origin, transform.right * half, groundY);
            bool leftOK = HasSupport(origin, -transform.right * half, groundY);
            LastRightSupport = rightOK ? half : 0f;
            LastLeftSupport = leftOK ? half : 0f;

            bool narrow;
            if (rightOK && leftOK) narrow = false;         // 左右とも足場あり = 十分広い
            else if (!rightOK && !leftOK) narrow = true;   // 両側とも無い = 丸太の上
            else
            {
                // 片側だけ無い場合。「細い足場」なのか「広い道の端に寄っているだけ」なのかを、
                // 反対側をもう一段遠く (autoNarrowWidth) まで見て切り分ける。そこまで足場が
                // 続いていれば道幅は autoNarrowWidth を超えているので細道ではない。
                Vector3 far = (rightOK ? transform.right : -transform.right) * autoNarrowWidth;
                narrow = !HasSupport(origin, far, groundY);
                if (rightOK) LastRightSupport = narrow ? half : autoNarrowWidth;
                else LastLeftSupport = narrow ? half : autoNarrowWidth;
            }
            if (narrow)
            {
                if (narrowSince < 0f) narrowSince = Time.time;
                // 連続して細い時間が narrowEngageSeconds を超えてから発動する
                if (Time.time - narrowSince >= narrowEngageSeconds)
                    narrowUntil = Time.time + narrowHoldSeconds;
            }
            else narrowSince = -1f;
        }
        else narrowSince = -1f;   // 空中では計測を切る (ジャンプで繋がって見えないように)

        if (Time.time < narrowUntil)
        {
            OnBeam = true;
            AutoNarrow = true;
            SpeedMultiplier = autoSpeedMultiplier;
        }
    }

    /// <summary>足元から横 <paramref name="offset"/> ずらした位置に、接地面と同じ高さの足場があるか。</summary>
    bool HasSupport(Vector3 origin, Vector3 offset, float groundY)
    {
        var hits = Physics.RaycastAll(origin + offset, Vector3.down, rayLength + 0.3f + edgeDropTolerance,
                                      Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            // 低すぎる = 道の外へ落ちている。高すぎる (壁) は足場ではないので無視しない
            // (壁際を歩くときに「細い」と誤判定しないよう、支えとして数える)。
            if (h.point.y >= groundY - edgeDropTolerance) return true;
        }
        return false;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (autoNarrowWidth <= 0f) return;
        Vector3 o = transform.position + Vector3.up * 0.3f;
        float half = autoNarrowWidth * 0.5f;
        Gizmos.color = AutoNarrow ? Color.red : Color.green;
        Gizmos.DrawLine(o + transform.right * half, o + transform.right * half + Vector3.down * (rayLength + 0.3f));
        Gizmos.DrawLine(o - transform.right * half, o - transform.right * half + Vector3.down * (rayLength + 0.3f));
    }
#endif
}
