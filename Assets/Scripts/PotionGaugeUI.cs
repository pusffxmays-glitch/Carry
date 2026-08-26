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
    // 追補 20: パリー (着地クッション) 成功のフラッシュ。グッド = シアン / ジャスト = 金
    public Color gaugeParryGoodColor = new Color(0.30f, 0.95f, 1.00f, 0.95f);
    public Color gaugeParryJustColor = new Color(1.00f, 0.85f, 0.25f, 0.95f);
    [Tooltip("パリーフラッシュの長さ (秒)。")]
    public float parryFlashSeconds = 0.7f;
    float parryFlashTimer;
    Color parryFlashColor;

    /// <summary>パリー成功をゲージ色で知らせる (GoblinPotActions が呼ぶ)。</summary>
    public void FlashParry(bool just)
    {
        parryFlashColor = just ? gaugeParryJustColor : gaugeParryGoodColor;
        parryFlashTimer = parryFlashSeconds;
    }

    // 2026-08-15 (要望「ゲージの上に、壺が今どっちに傾いているのかわかる表示が欲しい。
    // 上下左右キーでどれくらい動かしたのかわからなくなる」): バーの上に正方形の
    // バランスパッドを置き、armBalance / pitchBalance をドット位置で示す。
    // ドット = 壺が傾いている方向 (右キー -> 右、上キー = 前傾 -> 上)。中心 = ニュートラル。
    // 傾きが深いほどドットが白 -> 黄 -> 赤になり、よろけ危険域が読める。
    [Header("Balance pad (壺の傾きインジケーター)")]
    [Tooltip("バランスパッドの一辺 (1920x1080 基準の仮想ピクセル)。")]
    public float balancePadSize = 96f;
    public Color balanceDotSafeColor = Color.white;
    public Color balanceDotDangerColor = new Color(1f, 0.25f, 0.2f, 1f);

    // 2026-08-15 (要望「操作量に加えて、絶対世界での液体の傾き (水平器のイメージ) も
    // 表したい」): 同じパッドに 2 層で重ねる。
    //   * ドット (白→赤)      = 操作量 (armBalance / pitchBalance)
    //   * 輪 (気泡リング)     = 壺のワールド傾き。水平器の気泡。よろけ判定 (ApplyStagger)
    //                          と同じ「世界基準でどれだけ傾いているか」を同じ軸で描く。
    //   * 薄い赤の円          = よろけ開始角 (staggerThresholdDeg)。輪がこの円を出たら危険。
    // 平地では輪はドットに重なり、坂では輪だけがズレる。「坂ではドットを逆に倒して
    // 輪を中心に戻す」という斜面バランスの本質が UI からそのまま読める。
    [Header("Spirit level (水平器: 壺のワールド傾き)")]
    [Tooltip("この角度 (度) でパッドの端に達する。転倒の危険度が最大になる角 (staggerThresholdDeg + staggerRampDeg)。")]
    public float worldTiltFullDeg = 18f;
    [Tooltip("転倒の秒読みが始まる角 (度)。パッドに危険円として描く。GoblinCarryRig.staggerThresholdDeg と合わせること。")]
    public float worldTiltWarnDeg = 5.5f;
    [Tooltip("気泡リングの色 (安全域)。危険円を超えると赤へ寄る。")]
    public Color bubbleColor = new Color(0.35f, 0.9f, 1f, 0.95f);

    Image fillImage, trailImage;
    RectTransform fillRect, trailRect, frameRect;
    Canvas gaugeCanvas;
    Text percentText;
    RectTransform balanceDotRect;
    Image balanceDotImage;
    RectTransform bubbleRect;
    Image bubbleImage;
    Transform potTransform, rigRoot;
    GoblinCarryRig carryRig;
    float balanceDotRange;
    float trailValue = 1f;
    float prevValue = 1f;
    float gainFlashTimer;

    void Awake()
    {
        source = potionSourceBehaviour as IPotionVolumeSource;
        if (source == null)
        {
            // FIXED 2026-08-22: FluidCore が複数 (壺と滝) あるシーンで FindFirst が滝を掴み、
            // **ゲージが滝の残量を表示していた**。壺 (ゴブリンの子) を優先して取る。
            var gobLoco = FindFirstObjectByType<GoblinLocomotion>();
            var core = gobLoco != null ? gobLoco.GetComponentInChildren<FluidCore>() : null;
            if (core == null) core = FluidCore.FindPotFluid();
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
        carryRig = FindFirstObjectByType<GoblinCarryRig>();
        if (carryRig != null)
        {
            rigRoot = carryRig.transform;
            potTransform = rigRoot.Find("Carry_Pot");
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

        // バランスパッド (バーの上)。ドット位置 = (armBalance, -pitchBalance)。
        if (carryRig != null)
        {
            var pad = MakeRect("BalancePad", frame.transform, new Vector2(balancePadSize, balancePadSize));
            var padRect = (RectTransform)pad.transform;
            padRect.anchorMin = padRect.anchorMax = new Vector2(0.5f, 1f);
            padRect.pivot = new Vector2(0.5f, 0f);
            padRect.anchoredPosition = new Vector2(0f, 10f);
            pad.AddComponent<Image>().color = gaugeFrameColor;

            // 十字線 (中心 = ニュートラルの目印)
            var hLine = MakeRect("PadAxisH", pad.transform, new Vector2(balancePadSize - 10f, 2f));
            hLine.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);
            var vLine = MakeRect("PadAxisV", pad.transform, new Vector2(2f, balancePadSize - 10f));
            vLine.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);

            float dotSize = 14f;
            balanceDotRange = (balancePadSize - dotSize) * 0.5f - 4f;

            // よろけ開始角の危険円 (静的)。輪 (気泡) がここを出たら危険、の目標線。
            float warnR = balanceDotRange * Mathf.Clamp01(worldTiltWarnDeg / Mathf.Max(1f, worldTiltFullDeg));
            var warn = MakeRect("PadWarnCircle", pad.transform, Vector2.one * (warnR * 2f + 10f));
            var warnImg = warn.AddComponent<Image>();
            warnImg.sprite = MakeRingSprite(64, 2.5f);
            warnImg.color = new Color(1f, 0.4f, 0.35f, 0.45f);

            // 水平器の気泡リング (壺のワールド傾き)
            var bub = MakeRect("PadBubble", pad.transform, new Vector2(24f, 24f));
            bubbleRect = (RectTransform)bub.transform;
            bubbleImage = bub.AddComponent<Image>();
            bubbleImage.sprite = MakeRingSprite(32, 4f);
            bubbleImage.color = bubbleColor;

            var dot = MakeRect("PadDot", pad.transform, new Vector2(dotSize, dotSize));
            balanceDotRect = (RectTransform)dot.transform;
            balanceDotImage = dot.AddComponent<Image>();
            balanceDotImage.color = balanceDotSafeColor;
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
        UpdateBalancePad();
    }

    // ドット = 壺が傾いている方向。右キー (armBalance>0 = 右へ傾く) -> 右、
    // 上キー (pitchBalance<0 = 前傾) -> 上。振れ幅はバランス値そのもの (-1..1)。
    void UpdateBalancePad()
    {
        if (balanceDotRect == null || carryRig == null) return;
        float x = Mathf.Clamp(carryRig.armBalance, -1f, 1f);
        float y = Mathf.Clamp(-carryRig.pitchBalance, -1f, 1f);
        balanceDotRect.anchoredPosition = new Vector2(x, y) * balanceDotRange;
        float mag = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
        balanceDotImage.color = Color.Lerp(balanceDotSafeColor, balanceDotDangerColor, mag);

        // 水平器: 壺のワールド傾きを、よろけ判定 (ApplyStagger) と同じ分解で描く。
        // 右に傾く -> 輪が右、前傾 -> 輪が上 (ドットと同じ向きの約束)。
        if (bubbleRect != null && potTransform != null && rigRoot != null)
        {
            Vector3 up = potTransform.up;
            float latDeg = Mathf.Asin(Mathf.Clamp(Vector3.Dot(up, rigRoot.right), -1f, 1f)) * Mathf.Rad2Deg;
            float foreDeg = Mathf.Asin(Mathf.Clamp(Vector3.Dot(up, rigRoot.forward), -1f, 1f)) * Mathf.Rad2Deg;
            Vector2 t = new Vector2(latDeg, foreDeg) / Mathf.Max(1f, worldTiltFullDeg);
            if (t.sqrMagnitude > 1f) t.Normalize();   // 坂などで振り切れたら端に張り付く
            bubbleRect.anchoredPosition = t * balanceDotRange;
            float tiltDeg = new Vector2(latDeg, foreDeg).magnitude;
            bubbleImage.color = Color.Lerp(bubbleColor, balanceDotDangerColor,
                Mathf.InverseLerp(worldTiltWarnDeg, worldTiltFullDeg, tiltDeg));
        }
    }

    // 中抜きの円スプライトをコードで生成する (Resources にスプライト無しで済ませるため)。
    // 1px のアンチエイリアスつき。
    static Sprite MakeRingSprite(int size, float thickness)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float rOuter = size * 0.5f - 1f;
        float rInner = rOuter - thickness;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - (size - 1) * 0.5f, dy = y - (size - 1) * 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(Mathf.Min(rOuter - d + 1f, d - rInner + 1f));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
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
        // パリーフラッシュは最優先 (追補 20)。成立の瞬間を見逃させない
        if (parryFlashTimer > 0f)
        {
            parryFlashTimer -= Time.deltaTime;
            c = Color.Lerp(c, parryFlashColor, Mathf.Clamp01(parryFlashTimer / Mathf.Max(0.01f, parryFlashSeconds)));
        }
        fillImage.color = c;

        if (percentText != null)
        {
            percentText.text = Mathf.RoundToInt(v * 100f) + "%";
            percentText.color = v < lowWarnThreshold ? gaugeWarnColor : Color.white;
        }
    }
}
