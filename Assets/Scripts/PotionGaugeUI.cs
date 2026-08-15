using UnityEngine;
using UnityEngine.UI;

// ポーション残量ゲージ。FLUID_DESIGN.md §17: 経路は Fluid -> PotMass -> FillFraction01 の
// 一方向だけで、ゲージは観測するのみ。
//
// REWRITE 2026-08-15 (要望「画角内に入るように残量ゲージを置いてほしい。既存のゲージは
// うまく動いていないため削除。増減・残量がわかりやすい形式で」):
// 旧実装は毎フレーム Camera.main.pixelRect から画面端の位置を自前計算しており
// (ConstantPixelSize + 絶対ピクセル座標)、解像度や Game ビューの表示スケールとの
// 組み合わせで壊れやすかった。この版は UI の標準機構だけで組む:
//   * Screen Space Overlay + CanvasScaler(ScaleWithScreenSize, 1920x1080 基準)
//     → 描画される画そのものに対して常に同じ比率で乗るので、Game ビューの
//       Scale (0.41x など) や解像度によらず必ず画角内の同じ場所に出る。
//   * アンカーは Canvas の左上。位置の自前計算は一切しない。
// 見せ方 (増減と残量が一目でわかる形式):
//   * 横バー + パーセント数字。25/50/75% に目盛り。
//   * 減少: 失った分が赤い帯としてバー先端に残り、ゆっくり縮んで消える
//     (格闘ゲームの HP バー方式)。どれだけこぼしたかが見える。
//   * 増加 (ワープでの補充など): バーが緑にフラッシュする。
//   * 残量 20% 未満: バーが黄色く点滅して警告。
// 旧実装同様、Canvas 階層はランタイムで組み立てる (シーン YAML の手編集を避ける)。
// シーン側の PotionGaugeUI オブジェクトと potionSourceBehaviour (= Carry_Pot) の配線は
// そのまま使う。未配線でも FluidCore を自動で探す。
[DefaultExecutionOrder(150)]
public class PotionGaugeUI : MonoBehaviour
{
    [Header("Target (未指定なら FluidCore を自動で探す)")]
    public MonoBehaviour potionSourceBehaviour;
    IPotionVolumeSource source;

    // 2026-08-15: 「ゴブリンの下は見づらい」→ 縦バーにしてキャラの左脇へ (ユーザー指定)。
    [Header("Layout (1920x1080 基準の仮想ピクセル)")]
    [Tooltip("縦バーの大きさ (x=太さ, y=高さ)。")]
    public Vector2 gaugeBarSize = new Vector2(30f, 220f);
    [Tooltip("キャラ追従が使えないときの、画面左上からの余白。")]
    public Vector2 gaugeInset = new Vector2(48f, 48f);

    // 2026-08-15 (報告「Scale 0.41x での実行では画面外に存在している」): Game ビューは
    // ズーム/パン次第で描画結果の **中央付近の一部だけ** を表示する。画面の隅は
    // 最初に切り落とされる場所なので、隅に固定する限りどんな座標計算でも確実には
    // 見せられない。プレイヤーが必ず見ている場所はキャラクターなので、ゲージを
    // ゴブリンの画面上の位置 (足元の少し下) に毎フレーム追従させる。キャラが
    // 見えている限りゲージも見える。Canvas は Screen Space Overlay のままで、
    // WorldToScreenPoint の結果を CanvasScaler の scaleFactor で割って置くだけ。
    [Header("Follow (キャラ追従)")]
    [Tooltip("ゲージを追従させる対象。未指定なら GoblinLocomotion を自動で探す。見つからなければ画面左上に固定表示する。")]
    public Transform followTarget;
    [Tooltip("追従対象のどの高さ (m) を基準点にするか。既定はキャラの胴のあたり。")]
    public Vector3 followWorldOffset = new Vector3(0f, 1.0f, 0f);
    [Tooltip("基準点からの画面上のずらし量 (1920x1080 基準の仮想ピクセル)。負の x でキャラの左脇。")]
    // -170 は「ゴブリンに近すぎる」(2026-08-15) → -300 に離した。
    public Vector2 followScreenOffset = new Vector2(-300f, 0f);

    [Header("Feel")]
    [Tooltip("減少時の赤い帯 (失った分) が縮む速さ (割合/秒)。")]
    public float trailFallPerSecond = 0.18f;
    [Tooltip("増加フラッシュの長さ (秒)。")]
    public float gainFlashSeconds = 0.6f;
    [Range(0f, 1f)] public float lowWarnThreshold = 0.2f;

    [Header("Colors")]
    public Color gaugeFrameColor = new Color(0f, 0f, 0f, 0.60f);
    public Color gaugeTrackColor = new Color(1f, 1f, 1f, 0.10f);
    public Color gaugeFillColor = new Color(0.20f, 0.52f, 1.00f, 0.95f);   // ポーションの青
    public Color gaugeTrailColor = new Color(0.90f, 0.20f, 0.15f, 0.90f);  // 失った分
    public Color gaugeGainColor = new Color(0.25f, 0.95f, 0.40f, 0.95f);   // 増加フラッシュ
    public Color gaugeWarnColor = new Color(0.95f, 0.80f, 0.10f, 0.95f);   // 低残量警告

    Image fillImage, trailImage;
    RectTransform fillRect, trailRect, frameRect;
    Canvas gaugeCanvas;
    Text percentText;
    float trailValue = 1f;
    float prevValue = 1f;
    float gainFlashTimer;

    void Awake()
    {
        source = potionSourceBehaviour as IPotionVolumeSource;
        if (source == null)
        {
            var core = FindFirstObjectByType<FluidCore>();
            source = core;
            if (core != null) potionSourceBehaviour = core;
        }
        if (source == null)
            Debug.LogError("PotionGaugeUI: IPotionVolumeSource が見つかりません。ゲージは満タン表示のまま動きません。", this);
        if (followTarget == null)
        {
            var loco = FindFirstObjectByType<GoblinLocomotion>();
            if (loco != null) followTarget = loco.transform;
        }
        BuildUI();
        float v = source != null ? source.FillFraction01 : 1f;
        trailValue = prevValue = v;
        Apply(v);
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("PotionGaugeCanvas");
        canvasGo.transform.SetParent(transform, false);
        gaugeCanvas = canvasGo.AddComponent<Canvas>();
        gaugeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        gaugeCanvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        // 基準解像度に対する比率で乗せる。これで Game ビューの Scale や実解像度に
        // よらず「描画された画のこの場所・この大きさ」が保証される (クラス冒頭の注記)。
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // 枠。キャラ追従時は毎フレーム PositionGauge() が置く (アンカーは左下 = スクリーン
        // ピクセル座標そのまま、ピボットは上中央 = 指定点からバーが下へぶら下がる)。
        // 追従対象が無いときだけ左上に固定する。
        var frame = MakeRect("GaugeFrame", canvasGo.transform, gaugeBarSize + new Vector2(10f, 10f));
        frameRect = (RectTransform)frame.transform;
        if (followTarget != null)
        {
            // 追従時: 基準点にバーの中心が来る (ずらしは followScreenOffset が担当)。
            frameRect.anchorMin = frameRect.anchorMax = new Vector2(0f, 0f);
            frameRect.pivot = new Vector2(0.5f, 0.5f);
        }
        else
        {
            frameRect.anchorMin = frameRect.anchorMax = frameRect.pivot = new Vector2(0f, 1f);
            frameRect.anchoredPosition = new Vector2(gaugeInset.x, -gaugeInset.y);
        }
        frame.AddComponent<Image>().color = gaugeFrameColor;

        var track = MakeRect("GaugeTrack", frame.transform, gaugeBarSize);
        track.AddComponent<Image>().color = gaugeTrackColor;

        // バーの長さは Image.fillAmount ではなく **アンカー幅** で表す。
        // スプライト未指定の Image では fillAmount が効かず常に全幅で描かれる
        // (旧ゲージが「動いていない」ように見えた原因)。anchorMax.x = 残量 なら
        // RectTransform の機構だけで確実に横幅が変わる。
        // 赤い帯 (失った分) は fill の後ろに置く。
        var trail = MakeStretchBar("GaugeTrail", track.transform);
        trailImage = trail.GetComponent<Image>();
        trailImage.color = gaugeTrailColor;
        trailRect = (RectTransform)trail.transform;

        var fill = MakeStretchBar("GaugeFill", track.transform);
        fillImage = fill.GetComponent<Image>();
        fillImage.color = gaugeFillColor;
        fillRect = (RectTransform)fill.transform;

        // 25/50/75% の目盛り (縦バーなので水平線)
        for (int i = 1; i <= 3; i++)
        {
            var tick = MakeRect("Tick" + (i * 25), track.transform, new Vector2(gaugeBarSize.x * 0.55f, 2f));
            var tr = (RectTransform)tick.transform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, i * 0.25f);
            tr.pivot = new Vector2(0.5f, 0.5f);
            tr.anchoredPosition = Vector2.zero;
            var img = tick.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.35f);
        }

        // パーセント数字 (バーの下)
        var txt = MakeRect("GaugePercent", frame.transform, new Vector2(120f, 34f));
        var txtRect = (RectTransform)txt.transform;
        txtRect.anchorMin = txtRect.anchorMax = new Vector2(0.5f, 0f);
        txtRect.pivot = new Vector2(0.5f, 1f);
        txtRect.anchoredPosition = new Vector2(0f, -8f);
        percentText = txt.AddComponent<Text>();
        percentText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        percentText.fontSize = 28;
        percentText.fontStyle = FontStyle.Bold;
        percentText.alignment = TextAnchor.UpperCenter;
        percentText.color = Color.white;
        var outline = txt.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);
    }

    static GameObject MakeRect(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = size;
        r.anchoredPosition = Vector2.zero;
        return go;
    }

    // 親 (track) の左端に貼り付き、anchorMax.x で横幅が決まるバー。
    static GameObject MakeStretchBar(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = new Vector2(0f, 0f);
        r.anchorMax = new Vector2(1f, 1f);
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
        go.AddComponent<Image>();
        return go;
    }

    // 縦バー: 下から上へ伸びる。高さは anchorMax.y で決まる (fillAmount を使わない理由は上記)。
    static void SetBarHeight(RectTransform r, float fraction01)
    {
        r.anchorMax = new Vector2(1f, Mathf.Clamp01(fraction01));
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }

    void Update()
    {
        if (source == null || fillImage == null) return;
        float v = Mathf.Clamp01(source.FillFraction01);

        // 減少: 赤い帯 (trailValue) は即座には追わず、ゆっくり v まで下りる。
        // 増加: 帯はためずに即追随し、緑フラッシュで知らせる。
        if (v >= trailValue) trailValue = v;
        else trailValue = Mathf.MoveTowards(trailValue, v, trailFallPerSecond * Time.deltaTime);
        // 閾値 +1%: 跳ねた液体が壺へ戻るときの微小な増減で毎フレーム点滅しないように。
        if (v > prevValue + 0.01f) gainFlashTimer = gainFlashSeconds;
        prevValue = v;
        if (gainFlashTimer > 0f) gainFlashTimer -= Time.deltaTime;

        Apply(v);
    }

    // カメラ (CarryCameraRig) が LateUpdate で動いた後に置く。
    void LateUpdate() { PositionGauge(); }

    void PositionGauge()
    {
        if (frameRect == null || followTarget == null || gaugeCanvas == null) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(followTarget.position + followWorldOffset);
        if (sp.z <= 0f) return;   // カメラの後ろにいる間は前回位置のまま
        float s = Mathf.Max(1e-4f, gaugeCanvas.scaleFactor);
        frameRect.anchoredPosition = new Vector2(sp.x / s, sp.y / s) + followScreenOffset;
    }

    void Apply(float v)
    {
        SetBarHeight(fillRect, v);
        if (trailRect != null) SetBarHeight(trailRect, trailValue);

        Color c = gaugeFillColor;
        if (v < lowWarnThreshold)
            c = Color.Lerp(gaugeFillColor, gaugeWarnColor, Mathf.PingPong(Time.unscaledTime * 2.5f, 1f));
        if (gainFlashTimer > 0f)
            c = Color.Lerp(c, gaugeGainColor, Mathf.Clamp01(gainFlashTimer / Mathf.Max(0.01f, gainFlashSeconds)));
        fillImage.color = c;

        if (percentText != null)
        {
            percentText.text = Mathf.RoundToInt(v * 100f) + "%";
            percentText.color = v < lowWarnThreshold ? gaugeWarnColor : Color.white;
        }
    }
}
