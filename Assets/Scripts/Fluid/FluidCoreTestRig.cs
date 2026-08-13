using UnityEngine;

// Phase 1 の検証ハーネス。箱を動かして「粘性のある流体が安定して動くか」を見る。
// 壺はまだ無い (§37 Phase 1)。
//
// エディタが非フォーカスだと Player Loop が進まないため、SimulateSeconds() による
// 決定論的ステップ実行を用意してある（前セッションで実測した制約）。
[RequireComponent(typeof(FluidCore))]
public class FluidCoreTestRig : MonoBehaviour
{
    public enum Case
    {
        Settle = 1,     // 静止して落ち着くか（漏れ・爆発が無いか）
        TiltBox = 2,    // 箱を傾ける。液面が World Gravity 基準で水平を保つか
        ShakeX = 3,     // 横揺れ。粘性で遅れて追従するか
        HardStop = 4,   // 急停止。慣性で前方へ寄るか
        SpinY = 5,      // 回転。境界粘性で中身が引きずられるか
        Pour = 6,       // 傾け続けて実際に Overflow させる (§37 Phase 7)
    }

    public Case current = Case.Settle;
    public float caseTime;
    public bool autoDrive = true;
    public Vector3 restPosition = new Vector3(0f, 0.75f, 0f);
    public float travelSpeed = 2.2f;
    public float tiltAmplitude = 25f;

    FluidCore core;

    void Awake() { core = GetComponent<FluidCore>(); }

    void Start() { SetCase(current); }

    public void SetCase(Case c)
    {
        current = c;
        caseTime = 0f;
        transform.SetPositionAndRotation(restPosition, Quaternion.identity);
        if (core == null) core = GetComponent<FluidCore>();
        core.SeedFluid();
        core.ResetOverflowCounters();
    }

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) SetCase(Case.Settle);
            if (kb.digit2Key.wasPressedThisFrame) SetCase(Case.TiltBox);
            if (kb.digit3Key.wasPressedThisFrame) SetCase(Case.ShakeX);
            if (kb.digit4Key.wasPressedThisFrame) SetCase(Case.HardStop);
            if (kb.digit5Key.wasPressedThisFrame) SetCase(Case.SpinY);
            if (kb.digit6Key.wasPressedThisFrame) SetCase(Case.Pour);
            if (kb.rKey.wasPressedThisFrame) core.SeedFluid();
        }
        if (autoDrive) Drive(Time.deltaTime);
    }

    public void SimulateSeconds(float seconds, float dt = 1f / 60f)
    {
        bool prevDrive = autoDrive;
        bool prevStep = core.autoStep;
        autoDrive = false;
        core.autoStep = false;

        int steps = Mathf.Max(1, Mathf.RoundToInt(seconds / dt));
        for (int i = 0; i < steps; i++) { Drive(dt); core.Step(dt); }

        autoDrive = prevDrive;
        core.autoStep = prevStep;
    }

    public void Drive(float dt)
    {
        caseTime += dt;
        float t = caseTime;
        Vector3 pos = restPosition;
        Quaternion rot = Quaternion.identity;

        switch (current)
        {
            case Case.Settle:
                break;
            case Case.TiltBox:
                rot = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.9f) * tiltAmplitude);
                break;
            case Case.ShakeX:
                pos += Vector3.right * (Mathf.Sin(t * 1.9f) * 0.4f);
                break;
            case Case.HardStop:
            {
                float cycle = Mathf.Repeat(t, 3f);
                float dist;
                if (cycle < 1.2f) dist = travelSpeed * cycle;
                else if (cycle < 1.36f) { float u = (cycle - 1.2f) / 0.16f; dist = travelSpeed * (1.2f + 0.16f * (u - 0.5f * u * u)); }
                else dist = travelSpeed * (1.2f + 0.08f);
                pos += Vector3.forward * dist;
                break;
            }
            case Case.SpinY:
                rot = Quaternion.Euler(0f, t * 90f, 0f);
                break;
            case Case.Pour:
                // カメラ側へ傾け続けて、実際にリムから溢れさせる。
                // 横移動は入れない: 連続した液柱を見るのが目的で、横移動は流れを散らす。
                rot = Quaternion.Euler(-Mathf.SmoothStep(0f, 62f, Mathf.Clamp01(t / 1.4f)), 0f, 0f);
                break;
        }

        transform.SetPositionAndRotation(pos, rot);
    }

#if UNITY_EDITOR
    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = Color.white } };
        GUI.Label(new Rect(12, 8, 900, 24), "PHASE 1  case " + (int)current + " : " + current, style);
        if (core != null && core.IsReady)
            GUI.Label(new Rect(12, 32, 900, 24),
                "particles " + core.FluidCount + "  boundary " + core.BoundaryCount +
                "  spacing " + core.ParticleSpacing.ToString("F4") + "  h " + core.KernelRadius.ToString("F4") +
                "  substeps " + core.LastSubStepCount, style);
        if (core != null && core.IsReady)
            GUI.Label(new Rect(12, 56, 900, 24),
                "inside " + core.InsideCount + "  rim " + core.RimCount + "  air " + core.AirborneCount +
                "  ground " + core.GroundCount + "   overflow " + core.OverflowEvents +
                "  penetration " + core.PenetrationEvents + "   fill " + (core.FillFraction01 * 100f).ToString("F1") + "%", style);
        GUI.Label(new Rect(12, 80, 900, 24), "1..6 = case,  R = reseed", style);
    }
#endif
}
