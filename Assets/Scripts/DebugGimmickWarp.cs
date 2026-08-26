using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================================================
// DebugGimmickWarp -- 数字キーで各ギミックの手前へ飛び、ポーションを満タンに戻す。
//
// 1..9 が GimmickWarpPoint.number に対応する。ワープ先はシーンから集めるので、
// ギミックを増やしたら WarpPoint を置くだけでキーが増える。
//
// 実行順に注意:
//   GoblinCarryRig.LateUpdate(0) が手のボーンから壺を置き、FluidCore.LateUpdate(100) が
//   流体を進める。ワープでゴブリンを飛ばした直後は壺がまだ前フレームの位置なので、
//   **その両方が終わったあと(150)** に流体をリセットする。そうしないと、
//   古い壺の姿勢で種を置いてしまい、液体が壺からずれた場所に生まれる。
// ============================================================================================
[DefaultExecutionOrder(150)]
public class DebugGimmickWarp : MonoBehaviour
{
    [Tooltip("飛ばす対象。未設定なら同じ GameObject の CharacterController を探す。")]
    public CharacterController target;
    [Tooltip("飛んだあとポーションを満タンに戻す。")]
    public bool refillOnWarp = true;
    [Tooltip("ワープ時にログを出す。")]
    public bool logWarp = true;

    GimmickWarpPoint[] points;
    FluidCore fluid;
    GoblinGroundSlide slide;
    bool pendingRefill;

    void Awake()
    {
        if (target == null) target = GetComponent<CharacterController>();
        // FIXED 2026-08-22: 複数 FluidCore シーンでは壺 (自分の子) を優先 (滝を掴んでいた)
        fluid = GetComponentInChildren<FluidCore>();
        if (fluid == null) fluid = FluidCore.FindPotFluid();
        slide = GetComponent<GoblinGroundSlide>();
        RefreshPoints();
    }

    public void RefreshPoints()
    {
        points = FindObjectsOfType<GimmickWarpPoint>();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || target == null) return;

        int pressed = 0;
        if (kb.digit1Key.wasPressedThisFrame) pressed = 1;
        else if (kb.digit2Key.wasPressedThisFrame) pressed = 2;
        else if (kb.digit3Key.wasPressedThisFrame) pressed = 3;
        else if (kb.digit4Key.wasPressedThisFrame) pressed = 4;
        else if (kb.digit5Key.wasPressedThisFrame) pressed = 5;
        else if (kb.digit6Key.wasPressedThisFrame) pressed = 6;
        else if (kb.digit7Key.wasPressedThisFrame) pressed = 7;
        else if (kb.digit8Key.wasPressedThisFrame) pressed = 8;
        else if (kb.digit9Key.wasPressedThisFrame) pressed = 9;
        if (pressed == 0) return;

        if (points == null || points.Length == 0) RefreshPoints();
        GimmickWarpPoint dst = null;
        for (int i = 0; i < points.Length; i++)
            if (points[i] != null && points[i].number == pressed) { dst = points[i]; break; }

        if (dst == null)
        {
            if (logWarp) Debug.Log($"DebugGimmickWarp: {pressed} 番のワープ先が無い。");
            return;
        }

        Warp(dst.transform.position, dst.transform.rotation, dst.label);
    }

    public void Warp(Vector3 pos, Quaternion rot, string label)
    {
        // CharacterController は Transform を直接書き換えると内部状態とずれるので、
        // 一度無効にしてから位置を入れる。
        bool wasEnabled = target.enabled;
        target.enabled = false;
        target.transform.position = pos;
        target.transform.rotation = Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
        target.enabled = wasEnabled;

        if (slide != null) slide.ResetSlide();   // 前の場所の滑りを持ち越さない

        // 2026-08-15: 壺を下ろした状態やツボおろし再生中にワープしても詰まないよう、
        // ワープ時は必ず「壺を担いだ状態」へ戻す (ポーションも満タンに戻すのだから一貫する)。
        var potActions = GetComponent<GoblinPotActions>();
        if (potActions != null) potActions.ForceCarry();

        pendingRefill = refillOnWarp;
        if (logWarp)
            Debug.Log($"DebugGimmickWarp: 「{label}」へワープ {pos}");
    }

    void LateUpdate()
    {
        if (!pendingRefill) return;
        pendingRefill = false;
        // ここは実行順 150。リグ(0) が壺を新しい位置へ置き、FluidCore(100) が
        // 1 ステップ進めた **後**なので、壺の姿勢は既に正しい。
        if (fluid != null) fluid.ResetFluid();
    }
}
