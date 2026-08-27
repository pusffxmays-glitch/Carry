using UnityEditor;
using UnityEngine;

// ============================================================================================
// Carry 統計ウィンドウ (2026-08-22 ユーザー要望「プレイを押したときに別ウィンドウを出して
// FPS とか数値系を出してほしい」)。
//
// Game ビューの上に GUI を重ねるのではなく **独立した Editor ウィンドウ** なので、
// サブモニタへ出したりドッキングしたりできる。ゲーム画面を一切汚さない。
//
//   Tools/Carry/統計ウィンドウ で開く。
//   「プレイ開始時に自動で開く」を ON にしておくと、再生ボタンを押すたびに出てくる。
//
// 表示するのは「見た目の不具合を数字で追える」ものに絞ってある:
//   * FPS / フレーム時間
//   * シミュ時間比 (1.00 未満 = 流体がスローモーション。落下が遅く見える原因)
//   * サブステップ数 (要求/実行)。要求 > 実行 が続くなら CFL 不足
//   * 残量ゲージの内訳 (壺の中 / 落下中 / 地面) — 見た目と残量の突き合わせ用
//   * 容器と流体の速度 — 「中身が追い付かない」系の切り分け用
// ============================================================================================
public class CarryStatsWindow : EditorWindow
{
    const string AutoOpenKey = "Carry.StatsWindow.AutoOpen";

    [MenuItem("Tools/Carry/統計ウィンドウ")]
    public static void Open()
    {
        var w = GetWindow<CarryStatsWindow>(false, "Carry 統計", true);
        w.minSize = new Vector2(320, 380);
        w.Show();
    }

    static bool AutoOpen
    {
        get => EditorPrefs.GetBool(AutoOpenKey, true);
        set => EditorPrefs.SetBool(AutoOpenKey, value);
    }

    [InitializeOnLoadMethod]
    static void Hook()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && AutoOpen) Open();
    }

    // ---- 表示用の平滑化 (生の deltaTime は暴れるので指数平均にする) ----
    float smoothedDt = 1f / 60f;
    float worstDt;
    float worstDtResetAt;

    void OnEnable() { EditorApplication.update += Tick; }
    void OnDisable() { EditorApplication.update -= Tick; }

    void Tick()
    {
        if (!Application.isPlaying) return;
        float dt = Time.unscaledDeltaTime;
        if (dt > 1e-5f) smoothedDt = Mathf.Lerp(smoothedDt, dt, 0.1f);
        // 直近 3 秒の最悪フレームを出す (平均だけだとカクつきが見えない)
        if (Time.unscaledTime - worstDtResetAt > 3f) { worstDt = 0f; worstDtResetAt = Time.unscaledTime; }
        if (dt > worstDt) worstDt = dt;
        Repaint();
    }

    // シーンを毎フレーム検索すると重いので掴んだ参照を持ち回る
    FluidCore pot;
    GoblinLocomotion loco;
    GoblinPotActions actions;
    GoblinCarryRig rig;

    void Reacquire()
    {
        if (loco == null) loco = FindAnyObjectByType<GoblinLocomotion>();
        if (loco != null)
        {
            if (pot == null) pot = loco.GetComponentInChildren<FluidCore>();
            if (actions == null) actions = loco.GetComponent<GoblinPotActions>();
            if (rig == null) rig = loco.GetComponent<GoblinCarryRig>();
        }
    }

    static void Row(string label, string value, Color? tint = null)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(150));
        var prev = GUI.color;
        if (tint.HasValue) GUI.color = tint.Value;
        EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
        GUI.color = prev;
        EditorGUILayout.EndHorizontal();
    }

    static Color Judge(bool good, bool warn) =>
        good ? new Color(0.5f, 1f, 0.5f) : warn ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.5f, 0.5f);

    void OnGUI()
    {
        AutoOpen = EditorGUILayout.ToggleLeft("プレイ開始時に自動で開く", AutoOpen);
        EditorGUILayout.Space(4);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("再生中に数値が出ます。", MessageType.Info);
            pot = null; loco = null; actions = null; rig = null;
            return;
        }
        Reacquire();

        // ---- フレーム ----
        EditorGUILayout.LabelField("フレーム", EditorStyles.boldLabel);
        float fps = 1f / Mathf.Max(1e-5f, smoothedDt);
        Row("FPS", $"{fps:F1}", Judge(fps >= 50f, fps >= 25f));
        Row("フレーム時間", $"{smoothedDt * 1000f:F1} ms");
        Row("直近3秒の最悪", $"{worstDt * 1000f:F0} ms", Judge(worstDt < 0.05f, worstDt < 0.12f));

        if (pot == null)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("ゴブリンの壺 (FluidCore) が見つかりません。", MessageType.Warning);
            return;
        }

        // ---- 流体シミュレーション ----
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("流体シミュレーション", EditorStyles.boldLabel);
        float ratio = pot.LastWallDt > 1e-5f ? pot.LastSimDt / pot.LastWallDt : 1f;
        Row("シミュ時間比 (今)", $"{ratio:F2}", Judge(ratio > 0.95f, ratio > 0.8f));
        Row("シミュ時間比 (累計)", $"{pot.SimTimeRatio:F2}", Judge(pot.SimTimeRatio > 0.95f, pot.SimTimeRatio > 0.8f));
        EditorGUILayout.LabelField("  1.00 未満 = 流体が実時間より遅い (落下がスローに見える)",
                                   EditorStyles.miniLabel);
        Row("サブステップ", $"{pot.LastSubStepCount} (要求 {pot.LastRequiredSubSteps})",
            Judge(pot.LastRequiredSubSteps <= pot.LastSubStepCount, true));
        Row("壺の流体コスト", $"{pot.LastStepMs:F1} ms (平均 {pot.AvgStepMs:F1})",
            Judge(pot.AvgStepMs < 5f, pot.AvgStepMs < 12f));
        Row("流体の最大速さ", $"{pot.MeasuredMaxSpeed:F2} m/s");
        if (pot.Boundary != null)
        {
            Row("壺の速度", $"{pot.Boundary.LinearVelocity.magnitude:F2} m/s");
            Row("壺の角速度", $"{pot.Boundary.AngularVelocity.magnitude:F2} rad/s");
            Row("壺とのずれ", $"{Vector3.Distance(pot.Boundary.Container.position, pot.Boundary.SimPosition) * 100f:F1} cm",
                Judge(Vector3.Distance(pot.Boundary.Container.position, pot.Boundary.SimPosition) < 0.02f, true));
        }
        Row("壁の貫通補正", $"{pot.SafetyCorrectionCount} 粒子 (連続 {pot.SafetyConsecutiveFrames})",
            Judge(pot.SafetyCorrectionCount == 0, pot.SafetyConsecutiveFrames < 10));
        Row("速度クランプ(壺内)", pot.maxSpeedInPot > 0f ? $"{pot.maxSpeedInPot:F2} m/s (calm中)" : "なし");

        // ---- 残量の内訳 ----
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("ポーション残量の内訳", EditorStyles.boldLabel);
        Row("ゲージ", $"{pot.FillFraction01 * 100f:F1} %");
        Row("壺の中", $"{pot.InsideCount} 粒子");
        Row("リム付近", $"{pot.RimCount} 粒子");
        Row("空中 (戻りうる)", $"{pot.AirborneCount - pot.EscapedCount} 粒子");
        Row("こぼれて落下中", $"{pot.EscapedCount} 粒子");
        Row("地面", $"{pot.GroundCount} 粒子");
        Row("収支誤差", $"{pot.MassBalanceError}", Judge(pot.MassBalanceError == 0, true));
        EditorGUILayout.LabelField("  ゲージ = (壺の中 + リム + 空中) / 全粒子", EditorStyles.miniLabel);

        // ---- 検証用の操作 (2026-08-27 ユーザー要望) ----
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("検証", EditorStyles.boldLabel);
        if (actions != null)
        {
            // パリー判定窓の変更 (プレイ中の実体を直接いじる。停止すると元に戻る)
            EditorGUI.BeginChangeCheck();
            float justW = EditorGUILayout.Slider("金の窓 (秒)", actions.cushionJustWindow, 0.02f, 0.9f);
            float goodW = EditorGUILayout.Slider("青の窓 (秒)", actions.cushionWindow, 0.05f, 1.2f);
            if (EditorGUI.EndChangeCheck())
            {
                actions.cushionJustWindow = justW;
                actions.cushionWindow = Mathf.Max(goodW, justW);
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("金を出しやすく (0.35)"))
            { actions.cushionJustWindow = 0.35f; actions.cushionWindow = Mathf.Max(actions.cushionWindow, 0.5f); }
            if (GUILayout.Button("既定に戻す (0.12/0.35)"))
            { actions.cushionJustWindow = 0.12f; actions.cushionWindow = 0.35f; }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("  プレイ中のみ有効。停止でシーンの値に戻る", EditorStyles.miniLabel);
        }
        EditorGUILayout.Space(2);
        if (GUILayout.Button("ポーション残量をリセット (満タンに戻す)"))
            pot.ResetFluid();
        EditorGUILayout.LabelField("  リセット直後は数秒間、開始時の鎮静クランプが掛かる", EditorStyles.miniLabel);

        // ---- ゴブリン ----
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("ゴブリン", EditorStyles.boldLabel);
        if (actions != null) Row("状態", actions.Current.ToString());
        if (loco != null)
        {
            Row("移動速度", $"{loco.CurrentSpeed:F2} m/s{(loco.IsRunning ? " (走り)" : "")}");
            Row("接地", (loco.GetComponent<CharacterController>()?.isGrounded ?? false) ? "○" : "×");
        }
        if (rig != null)
            Row("バランス", $"左右 {rig.armBalance:+0.00;-0.00} / 前後 {rig.pitchBalance:+0.00;-0.00}");

        // ---- カメラ ----
        var cam = CarryCameraRig.Instance;
        if (cam != null && cam.target != null)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("カメラ", EditorStyles.boldLabel);
            float d = Vector3.Distance(cam.transform.position, cam.target.position + cam.lookOffset);
            Row("距離", $"{d:F2} m", Judge(d > cam.distance - 0.5f, d > 3f));
            Row("直近の遮蔽物", string.IsNullOrEmpty(cam.LastBlocker)
                ? "なし" : $"{cam.LastBlocker} ({Time.time - cam.LastBlockTime:F1} 秒前)");
        }
    }
}
