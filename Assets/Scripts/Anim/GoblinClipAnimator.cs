using UnityEngine;

// ============================================================================================
// GoblinClipAnimator -- ベイク済み全身クリップ (GoblinClip) の再生機。
//
// 2026-08-15 追加。GoblinCarryRig の LateUpdate 冒頭から ApplyBody() が呼ばれ、
// 再生中は true を返して通常の運搬パイプライン (BasePose + IK + 壺配置) を丸ごと差し替える。
//
//  * ワンショット (ツボおろし / 転倒): 逆再生に対応。壺の軌跡も焼いてあり、
//    PotReleaseFrame を跨いだ瞬間にコールバック (手を離した / 掴んだ) を発火する。
//  * ロコモーションループ (壺なしの Idle/Walk/Run/Jump): クロスフェード付きで切り替え。
//    歩行系は stride 同期 (位相速度 = 移動速度 / 1 周の移動距離) で足滑りを防ぐ。
//
// ボーンの適用は ApplyBasePose と同じ「位置 + AimLocalY + RollAroundY」方式。
// ============================================================================================
public class GoblinClipAnimator : MonoBehaviour
{
    [Tooltip("ループクリップ切り替え時のクロスフェード秒数。")]
    public float crossfadeSeconds = 0.15f;
    [Tooltip("ワンショット開始時のフェードイン秒数。よろけ中の姿勢から転倒クリップ等へ滑らかに繋ぐ。")]
    public float oneShotFadeInSeconds = 0.15f;
    [Tooltip("ワンショット終了時に最終ポーズから通常ポーズへ受け渡すブレンド秒数 (追補 25)。")]
    public float handoverFadeSeconds = 0.25f;
    GoblinClip fadeOutClip;
    float fadeOutFrame, fadeOutT;
    // 受け渡しにかける秒数。クリップごとに上書きできる (着地クッションだけ長くする)。
    float fadeOutDuration;
    bool fadeOutMirror;

    /// <summary>GoblinCarryRig がボーンを書き終えた直後 (壺配置の前) に呼ぶ。
    /// 直前に終わったワンショットの最終ポーズを減衰ウェイトで上書きブレンドする。</summary>
    public void ApplyHandoverBlend()
    {
        if (fadeOutClip == null) return;
        fadeOutT -= Time.deltaTime;
        float dur = fadeOutDuration > 0.001f ? fadeOutDuration : handoverFadeSeconds;
        float u = dur > 0.001f ? Mathf.Clamp01(fadeOutT / dur) : 0f;
        // 2026-08-25: 線形の重みだと **抜け際** に折れ目が残る。実測では w が 0 になる
        // フレームで両手の高さが 6cm 飛び、壺が 174 度/秒でロールしていた (パリー成功後に
        // 液体が横へ持っていかれる正体)。smoothstep にして両端の傾きを 0 にする。
        float w = u * u * (3f - 2f * u);
        if (w <= 0.0001f) { fadeOutClip = null; return; }
        ApplyClipFrame(fadeOutClip, fadeOutFrame, 1f, null, 0f, w);
    }

    Transform root;
    Transform[] boneCache;      // GoblinClip.Bones の並び (全クリップ共通) に対応
    string[] boneNames;
    Transform pot;

    // ワンショット
    GoblinClip oneShot;
    float oneShotFrame;
    float oneShotSpeed = 1f;
    bool oneShotReverse;
    bool oneShotDrivePotToEnd;
    float oneShotEaseOutFrames;   // 終端付近で再生速度を落とす幅 (0 = なし)
    // 折り返し以降だけ再生速度を落とす (着地クッションの伸び上がり)。-1 = なし。
    float oneShotSlowFromFrame = -1f;
    float oneShotSlowSpeed = 1f;
    float oneShotElapsed;         // フェードイン用の経過秒
    float oneShotHandoverFade = -1f;   // このワンショット専用の受け渡し秒数 (-1 = 既定)
    bool oneShotAdditive;              // 担ぎ姿勢からの差分として乗せる (下の注記)
    bool additiveFinishPending;        // 終端の差分 0 を適用したので次フレームで解放する
    Vector3[] addPos; Quaternion[] addRot;   // 加算適用の一時退避 (親を動かす前の値)
    bool oneShotMirror;           // 左右反転再生 (横転倒の向き)。ボーン L/R 入替 + X 反転
    int[] mirrorIdx;
    bool potEventFired;
    System.Action onPotEvent;      // PotReleaseFrame を跨いだ (手を離した / 掴んだ)
    System.Action onOneShotDone;

    // ロコモーションループ (クロスフェード 2 スロット)
    GoblinClip locoClip, prevClip;
    float locoPhase, prevPhase;    // 0..1
    float fade;                    // 1 = locoClip 完全適用
    public float locoStride;       // 1 周で進む距離 (m)。0 なら fps ベースの時間再生
    public float locoSpeed;        // 現在の移動速度 (stride 同期用)。呼び出し側が毎フレーム設定

    public bool OneShotActive => oneShot != null;
    /// <summary>クリップが焼いた壺の姿勢に上乗せするワールド回転。既定は無回転。
    /// 転倒でこぼす演出のように、**クリップが壺を駆動している最中に**姿勢を足したいときに使う
    /// (2026-08-22)。ワンショットを開始するたび無回転へ戻すので、他のクリップへ漏れない。</summary>
    public Quaternion PotExtraRotation { get; set; } = Quaternion.identity;
    // 加算再生は運搬パイプラインの上に乗るだけなので「体を乗っ取っている」とは数えない
    // (バランス入力を殺さない)。
    public bool IsDrivingBody => (oneShot != null && !oneShotAdditive) || locoClip != null;
    /// <summary>再生中のワンショットクリップ (無ければ null)。種別判定用。</summary>
    public GoblinClip CurrentOneShot => oneShot;
    /// <summary>再生中のワンショットの現在フレーム (小数)。</summary>
    public float OneShotFrame => oneShotFrame;

    /// <summary>再生中のワンショットを残り時間 seconds で終端まで早送りする
    /// (あちちジャンプの早着地用: 着地後もバタバタし続けないように)。</summary>
    public void FinishOneShotFast(float seconds = 0.15f)
    {
        if (oneShot == null || oneShotReverse) return;
        float remaining = oneShot.frameCount - 1 - oneShotFrame;
        if (remaining <= 0f) return;
        oneShotSpeed = Mathf.Max(oneShotSpeed, remaining / (oneShot.fps * Mathf.Max(0.02f, seconds)));
        oneShotEaseOutFrames = 0f;
    }

    /// <summary>再生中のワンショットを現在フレームから逆再生に切り替える (転倒キャンセル用)。
    /// 完了コールバックを差し替え、potReleaseFrame イベントは以後発火しない。</summary>
    public void ReverseOneShot(System.Action newDone, float speed = 1f)
    {
        if (oneShot == null) return;
        oneShotReverse = true;
        oneShotSpeed = speed;
        oneShotEaseOutFrames = 0f;
        potEventFired = true;      // 逆再生で potReleaseFrame を跨いでも手離しイベントを出さない
        onPotEvent = null;
        onOneShotDone = newDone;
    }
    public GoblinClip CurrentLoco => locoClip;

    void Awake()
    {
        root = transform;
        pot = root.Find("Carry_Pot");
        boneNames = GoblinClipData_PotDown.Bones;   // 全クリップ同一の並び
        boneCache = new Transform[boneNames.Length];
        mirrorIdx = new int[boneNames.Length];
        for (int i = 0; i < boneNames.Length; i++)
        {
            boneCache[i] = GoblinBoneUtil.FindDeep(root, boneNames[i]);
            // ミラー用: Left <-> Right を入れ替えた行のインデックス (無ければ自分)
            string nm = boneNames[i];
            string sw = nm.StartsWith("Left") ? "Right" + nm.Substring(4)
                      : nm.StartsWith("Right") ? "Left" + nm.Substring(5) : nm;
            mirrorIdx[i] = i;
            for (int j = 0; j < boneNames.Length; j++)
                if (boneNames[j] == sw) { mirrorIdx[i] = j; break; }
        }
    }

    public void PlayOneShot(GoblinClip clip, bool reverse, bool drivePotToEnd,
                            System.Action potEvent, System.Action done, float speed = 1f,
                            float easeOutFrames = 0f, bool mirror = false,
                            float slowFromFrame = -1f, float slowSpeed = 1f,
                            float handoverFade = -1f, bool additive = false)
    {
        oneShot = clip;
        oneShotReverse = reverse;
        oneShotMirror = mirror;
        oneShotFrame = reverse ? clip.frameCount - 1.0001f : 0f;
        oneShotSpeed = speed;
        oneShotEaseOutFrames = easeOutFrames;
        oneShotSlowFromFrame = slowFromFrame;
        oneShotSlowSpeed = Mathf.Max(0.05f, slowSpeed);
        oneShotElapsed = 0f;
        oneShotHandoverFade = handoverFade;
        oneShotAdditive = additive;
        additiveFinishPending = false;
        oneShotDrivePotToEnd = drivePotToEnd;
        PotExtraRotation = Quaternion.identity;
        potEventFired = false;
        onPotEvent = potEvent;
        onOneShotDone = done;
        locoClip = prevClip = null;   // ワンショット優先
        fadeOutClip = null;           // 受け渡しブレンド中の再突入は破棄
        fade = 1f;
    }

    public void SetLocomotion(GoblinClip clip, float stride)
    {
        if (oneShot != null) return;
        if (locoClip == clip) { locoStride = stride; return; }
        prevClip = locoClip;
        prevPhase = locoPhase;
        locoClip = clip;
        locoPhase = 0f;
        locoStride = stride;
        fade = prevClip != null ? 0f : 1f;
    }

    // 終了処理。fadeOut = true のときだけ最終ポーズを受け渡しブレンドへ渡す
    // (加算再生は終端で差分ゼロなので渡すものが無い)。
    void FinishOneShot(bool fadeOut)
    {
        if (fadeOut)
        {
            fadeOutClip = oneShot;
            fadeOutFrame = oneShotFrame;
            fadeOutMirror = oneShotMirror;
            fadeOutDuration = oneShotHandoverFade > 0.001f ? oneShotHandoverFade : handoverFadeSeconds;
            fadeOutT = fadeOutDuration;
        }
        var cb = onOneShotDone;
        oneShot = null;
        onOneShotDone = null;
        cb?.Invoke();
    }

    /// <summary>加算ワンショットの差分を、リグが姿勢を書き終えた後に乗せる。
    /// **壺を置く前**に呼ぶこと (壺は手ボーンの位置から置かれるため、手を動かせば壺も付いてくる)。
    ///
    /// 差分は「今のフレーム - 終端フレーム」。終端では 0 になるので、クリップが終わった
    /// 瞬間に姿勢が飛ばない。地形の傾き・バランス入力・歩容はそのまま生きる。</summary>
    public void ApplyAdditive()
    {
        if (oneShot == null || !oneShotAdditive) return;
        int n = boneCache.Length;
        if (addPos == null || addPos.Length != n) { addPos = new Vector3[n]; addRot = new Quaternion[n]; }
        // 先に全部退避する。親を動かすと子も動くので、書きながら読むと二重に効く。
        for (int i = 0; i < n; i++)
        {
            var b = boneCache[i];
            if (b == null) continue;
            addPos[i] = b.position; addRot[i] = b.rotation;
        }
        int last = oneShot.frameCount - 1;
        for (int i = 0; i < n; i++)
        {
            var b = boneCache[i];
            if (b == null) continue;
            oneShot.SampleBone(i, oneShotFrame, out Vector3 p, out Vector3 y, out Vector3 x);
            oneShot.SampleBone(i, last, out Vector3 pr, out Vector3 yr, out Vector3 xr);
            b.position = addPos[i] + root.rotation * (p - pr);
            Quaternion dq = AxesToRot(y, x) * Quaternion.Inverse(AxesToRot(yr, xr));
            b.rotation = (root.rotation * dq * Quaternion.Inverse(root.rotation)) * addRot[i];
        }
    }

    // クリップの (ローカル +Y, ローカル +X) から回転を組む。Unity は左手系なので
    // 前方 = Cross(right, up)。
    static Quaternion AxesToRot(Vector3 y, Vector3 x)
    {
        Vector3 f = Vector3.Cross(x, y);
        if (f.sqrMagnitude < 1e-8f || y.sqrMagnitude < 1e-8f) return Quaternion.identity;
        return Quaternion.LookRotation(f.normalized, y.normalized);
    }

    public void StopAll()
    {
        oneShot = null;
        locoClip = prevClip = null;
        fadeOutClip = null;
    }

    /// <summary>GoblinCarryRig.LateUpdate 冒頭から呼ぶ。体を駆動したら true。</summary>
    public bool ApplyBody()
    {
        float dt = Time.deltaTime;
        // 前フレームで終端の差分 0 を適用済み。ここで解放する。
        if (additiveFinishPending) { additiveFinishPending = false; FinishOneShot(false); }
        if (oneShot != null)
        {
            // 終端イーズ: 逆再生の拾い上げなどで「速く動いて急停止」すると中身の液体が
            // 慣性で吹き上がる (2026-08-16 バグ報告)。終端 easeOutFrames の範囲で
            // 再生速度を滑らかに落とし、静かに止める。
            float speedScale = 1f;
            if (oneShotEaseOutFrames > 0.5f)
            {
                float remaining = oneShotReverse ? oneShotFrame : (oneShot.frameCount - 1 - oneShotFrame);
                speedScale = Mathf.Lerp(0.18f, 1f, Mathf.Clamp01(remaining / oneShotEaseOutFrames));
            }
            // 折り返し以降の減速。切り替え点は壺の速度が 0 になるフレームを指定するので、
            // 速度が段で変わっても動きに段差は出ない (GoblinClip.LowestPotFrame)。
            if (oneShotSlowFromFrame >= 0f && !oneShotReverse && oneShotFrame >= oneShotSlowFromFrame)
                speedScale *= oneShotSlowSpeed;
            oneShotFrame += (oneShotReverse ? -1f : 1f) * oneShotSpeed * speedScale * oneShot.fps * dt;
            bool finished = oneShotReverse ? oneShotFrame <= 0f : oneShotFrame >= oneShot.frameCount - 1;
            oneShotFrame = Mathf.Clamp(oneShotFrame, 0f, oneShot.frameCount - 1.0001f);

            // フェードイン: 現在のボーン姿勢 (よろけ・歩行の続き) からクリップ姿勢へ寄せていく。
            // ボーンは毎フレーム上書きされる仕組みなので「現在値→目標」の重み付き適用で滑らかに繋がる。
            oneShotElapsed += dt;

            // 加算再生 (2026-08-25)。骨はここでは書かない。運搬パイプラインをそのまま
            // 走らせ、リグが姿勢を書き終えた後に ApplyAdditive() で
            // 「終端フレームからの差分」だけを乗せる。終端では差分が定義上ゼロなので、
            // **担ぎ姿勢へ戻す受け渡しが存在しない**。
            // 従来 (絶対姿勢で再生) は、クリップの終端姿勢と担ぎ姿勢が
            // 手で 23cm・前腕で 32cm 離れており、それを 0.25〜0.55 秒で埋めていた。
            // これが「パリーで腕を曲げた後の伸ばし方がおかしい・左右差が出る」の正体。
            if (oneShotAdditive)
            {
                if (finished)
                {
                    // **終端フレームちょうどで差分 0 を 1 回適用してから**終わる。
                    // ここで即 null にすると、最後に適用された差分 (終端の一歩手前) が
                    // 残ったまま消えて 1 フレームだけ姿勢が跳ぶ (実測: ロール 3.8 度)。
                    oneShotFrame = oneShot.frameCount - 1;
                    additiveFinishPending = true;
                }
                return false;   // 体は運搬パイプラインが書く
            }

            float w = oneShotFadeInSeconds > 0.001f ? Mathf.Clamp01(oneShotElapsed / oneShotFadeInSeconds) : 1f;
            ApplyClipFrame(oneShot, oneShotFrame, 1f, null, 0f, w);
            ApplyPot(finished, w);

            // PotReleaseFrame 跨ぎイベント (順再生: 手を離す / 逆再生: 掴む)
            if (!potEventFired && oneShot.potReleaseFrame >= 0)
            {
                bool crossed = oneShotReverse ? oneShotFrame <= oneShot.potReleaseFrame
                                              : oneShotFrame >= oneShot.potReleaseFrame;
                if (crossed) { potEventFired = true; onPotEvent?.Invoke(); }
            }

            if (finished) FinishOneShot(true);
            return true;
        }

        if (locoClip != null)
        {
            AdvancePhase(locoClip, ref locoPhase, dt);
            if (prevClip != null)
            {
                AdvancePhase(prevClip, ref prevPhase, dt);
                fade = Mathf.MoveTowards(fade, 1f, dt / Mathf.Max(0.01f, crossfadeSeconds));
                if (fade >= 1f) prevClip = null;
            }
            ApplyClipFrame(locoClip, locoPhase * locoClip.frameCount, fade, prevClip,
                           prevClip != null ? prevPhase * prevClip.frameCount : 0f);
            return true;
        }
        return false;
    }

    void AdvancePhase(GoblinClip clip, ref float phase, float dt)
    {
        float rate = locoStride > 0.001f
            ? Mathf.Max(0.15f, locoSpeed) / locoStride           // stride 同期
            : clip.fps / clip.frameCount;                        // 実時間再生
        phase += rate * dt;
        // 非ループのロコモーションクリップ (ジャンプ) は最終ポーズで止まる
        phase = clip.loop ? Mathf.Repeat(phase, 1f) : Mathf.Min(phase, 0.9999f);
    }

    // blend: 1 = clipA のみ。clipB があれば (1-blend) で混ぜる。
    // masterWeight: 1 未満なら「現在のボーン姿勢→クリップ姿勢」を重み付きで寄せる (フェードイン)。
    void ApplyClipFrame(GoblinClip a, float frameA, float blend, GoblinClip b, float frameB, float masterWeight = 1f)
    {
        // ミラーはワンショット (と、その受け渡しブレンド) のみ
        bool mir = (oneShotMirror && a == oneShot) || (fadeOutMirror && a == fadeOutClip);
        for (int i = 0; i < boneCache.Length; i++)
        {
            var bone = boneCache[i];
            if (bone == null) continue;
            a.SampleBone(mir ? mirrorIdx[i] : i, frameA, out Vector3 p, out Vector3 y, out Vector3 x);
            p.y -= a.groundY;
            // 鏡映 R' = M·R·M (M = diag(-1,1,1)): 位置と Y 軸は (-x, y, z)、
            // X 軸 (ロール基準) は (x, -y, -z)。X 軸まで (-x,y,z) にするとロールが
            // ほぼ 180 度ずれて全身がねじれる (2026-08-16 左転倒バグの原因)。
            if (mir) { p.x = -p.x; y.x = -y.x; x.y = -x.y; x.z = -x.z; }
            if (b != null && blend < 1f)
            {
                b.SampleBone(i, frameB, out Vector3 p2, out Vector3 y2, out Vector3 x2);
                p2.y -= b.groundY;
                p = Vector3.Lerp(p2, p, blend);
                y = Vector3.Slerp(y2, y, blend);
                x = Vector3.Slerp(x2, x, blend);
            }
            Vector3 targetPos = root.position + root.rotation * p;
            Vector3 targetY = root.TransformDirection(y).normalized;
            Vector3 targetX = root.TransformDirection(x).normalized;
            if (masterWeight < 1f)
            {
                targetPos = Vector3.Lerp(bone.position, targetPos, masterWeight);
                targetY = Vector3.Slerp((bone.rotation * Vector3.up).normalized, targetY, masterWeight);
                targetX = Vector3.Slerp((bone.rotation * Vector3.right).normalized, targetX, masterWeight);
            }
            bone.position = targetPos;
            AimLocalY(bone, targetY);
            RollAroundY(bone, targetX);
        }
    }

    void ApplyPot(bool lastFrame, float masterWeight = 1f)
    {
        if (pot == null || oneShot == null || !oneShot.HasPot) return;
        // 手を離した後 (順再生) は壺を駆動しない -- ただし転倒のように最後まで焼いた壺を
        // 使うクリップは最後まで駆動する。
        if (!oneShotDrivePotToEnd && oneShot.potReleaseFrame >= 0 && !oneShotReverse
            && oneShotFrame > oneShot.potReleaseFrame + 0.5f) return;
        // 逆再生 (拾い上げ) では掴む前 (frame > release) の間も、壺は接地したままなので
        // クリップの値をそのまま使ってよい (接地区間の壺は静止で焼かれている)。
        oneShot.SamplePotMirrorable(oneShotFrame, oneShotMirror, out Vector3 p, out Quaternion rot);
        p.y -= oneShot.groundY;
        Vector3 tp = root.position + root.rotation * p;
        Quaternion tr = PotExtraRotation * (root.rotation * rot);
        if (masterWeight < 1f)
        {
            tp = Vector3.Lerp(pot.position, tp, masterWeight);
            tr = Quaternion.Slerp(pot.rotation, tr, masterWeight);
        }
        pot.position = tp;
        pot.rotation = tr;
    }

    // ---- GoblinCarryRig と同じ最小回転アプローチ (bind pose のロールを壊さない) ----
    static void AimLocalY(Transform bone, Vector3 worldDir)
    {
        Vector3 curY = bone.rotation * Vector3.up;
        bone.rotation = Quaternion.FromToRotation(curY, worldDir) * bone.rotation;
    }

    static void RollAroundY(Transform bone, Vector3 targetXWorld)
    {
        Vector3 yAxis = (bone.rotation * Vector3.up).normalized;
        Vector3 curX = (bone.rotation * Vector3.right).normalized;
        Vector3 proj = targetXWorld - Vector3.Dot(targetXWorld, yAxis) * yAxis;
        if (proj.sqrMagnitude < 1e-8f) return;
        proj.Normalize();
        float angle = Vector3.SignedAngle(curX, proj, yAxis);
        bone.rotation = Quaternion.AngleAxis(angle, yAxis) * bone.rotation;
    }
}
