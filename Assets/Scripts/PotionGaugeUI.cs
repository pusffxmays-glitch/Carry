using UnityEngine;
using UnityEngine.UI;

// Screen-edge UI gauge showing the pot's remaining potion volume, added per request
// ("画面のはじのほうにツボの中の残量とリンクするゲージを用意"). Builds its own Canvas/Image
// hierarchy at runtime (same reasoning as PotionLiquid's own mesh/VFX children: avoids hand-editing
// scene YAML for new UI GameObjects) and reads PotionLiquid.FillFraction01 every frame. No changes
// to PotionLiquid itself -- FillFraction01 already existed as a public property.
[DefaultExecutionOrder(150)]
public class PotionGaugeUI : MonoBehaviour
{
    [Header("Target (auto-found if empty)")]
    public PotionLiquid potionLiquid;

    [Header("Layout")]
    public Vector2 barSize = new Vector2(48f, 340f);
    [Tooltip("Inset from the corner (x = horizontal, y = vertical), not just the single edge -- moved 2026-08-12 (\"カメラ外にあるね\": anchored dead-center on the left edge, right where a fixed-aspect Game view's zoom/pan is most likely to crop) to a generous bottom-corner inset, a much safer conventional HUD spot.")]
    public Vector2 screenEdgeOffset = new Vector2(110f, 110f);
    [Tooltip("Anchor to the bottom-left corner of the screen (unchecked = bottom-right).")]
    public bool anchorLeft = true;

    [Header("Colors")]
    public Color frameColor = new Color(0f, 0f, 0f, 0.55f);
    public Color emptyTrackColor = new Color(1f, 1f, 1f, 0.12f);
    public Color fillColor = new Color(0.22f, 0.75f, 0.28f, 0.95f);
    [Tooltip("Fill color blended in as the gauge runs low, to read as an urgent warning.")]
    public Color lowFillColor = new Color(0.85f, 0.75f, 0.15f, 0.95f);
    [Range(0f, 1f)] public float lowThreshold = 0.2f;

    Image fillImage;

    // 2026-08-12: user reports the gauge does not appear when they press Play in the Editor, but
    // every check made here (direct RectTransform/Canvas inspection, a giant sanity-check red
    // square added to the same Canvas) shows it rendering correctly. Since the failure can't be
    // reproduced this session, Awake() now logs loudly (Debug.LogError, which shows in the Console
    // with a red icon and pauses on error if "Error Pause" is on) so that if it silently fails or
    // throws in the user's actual session, that will be directly visible instead of invisible.
    void Awake()
    {
        try
        {
            if (potionLiquid == null) potionLiquid = FindFirstObjectByType<PotionLiquid>();
            if (potionLiquid == null)
                Debug.LogError("PotionGaugeUI: no PotionLiquid found in the scene -- the gauge will build but always show empty/default fill.");
            BuildUI();
            Debug.Log("PotionGaugeUI: gauge built successfully. potionLiquid=" + (potionLiquid != null ? potionLiquid.name : "NULL") +
                " Screen=" + Screen.width + "x" + Screen.height);
        }
        catch (System.Exception e)
        {
            Debug.LogError("PotionGaugeUI: BuildUI() threw an exception, the gauge did NOT get created: " + e);
        }
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("PotionGaugeCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        // Switched to ConstantPixelSize 2026-08-12 while diagnosing the gauge-not-visible report --
        // ScaleWithScreenSize's per-frame scale-factor computation from Screen.width/height and
        // referenceResolution is one more moving part than a fixed pixel size needs, so removing it
        // rules out a scaler math issue as a variable (the bar will just be a constant 48x340 px
        // regardless of resolution, which is simpler to reason about while this is unresolved).
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Bottom corner instead of mid-left-edge (2026-08-12, see screenEdgeOffset comment above).
        Vector2 anchor = anchorLeft ? new Vector2(0f, 0f) : new Vector2(1f, 0f);

        var frameGo = new GameObject("GaugeFrame", typeof(RectTransform));
        frameGo.transform.SetParent(canvasGo.transform, false);
        var frameRect = frameGo.GetComponent<RectTransform>();
        frameRect.anchorMin = anchor; frameRect.anchorMax = anchor; frameRect.pivot = anchor;
        frameRect.sizeDelta = barSize + new Vector2(8f, 8f);
        frameRect.anchoredPosition = new Vector2(anchorLeft ? screenEdgeOffset.x : -screenEdgeOffset.x, screenEdgeOffset.y);
        var frameImg = frameGo.AddComponent<Image>();
        frameImg.color = frameColor;

        var trackGo = new GameObject("GaugeTrack", typeof(RectTransform));
        trackGo.transform.SetParent(frameGo.transform, false);
        var trackRect = trackGo.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0.5f, 0.5f); trackRect.anchorMax = new Vector2(0.5f, 0.5f); trackRect.pivot = new Vector2(0.5f, 0.5f);
        trackRect.sizeDelta = barSize;
        trackRect.anchoredPosition = Vector2.zero;
        var trackImg = trackGo.AddComponent<Image>();
        trackImg.color = emptyTrackColor;

        var fillGo = new GameObject("GaugeFill", typeof(RectTransform));
        fillGo.transform.SetParent(trackGo.transform, false);
        var fillRect = fillGo.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0.5f, 0.5f); fillRect.anchorMax = new Vector2(0.5f, 0.5f); fillRect.pivot = new Vector2(0.5f, 0.5f);
        fillRect.sizeDelta = barSize;
        fillRect.anchoredPosition = Vector2.zero;
        fillImage = fillGo.AddComponent<Image>();
        fillImage.color = fillColor;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Vertical;
        fillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
        fillImage.fillAmount = potionLiquid != null ? potionLiquid.FillFraction01 : 1f;
    }

    void Update()
    {
        if (potionLiquid == null || fillImage == null) return;
        float f = potionLiquid.FillFraction01;
        fillImage.fillAmount = f;
        fillImage.color = f <= lowThreshold ? Color.Lerp(lowFillColor, fillColor, f / Mathf.Max(0.0001f, lowThreshold)) : fillColor;
    }
}
