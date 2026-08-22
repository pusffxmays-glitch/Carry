using UnityEditor;
using UnityEngine;

// ============================================================================================
// CarryStartupProfilerHook -- 「プレイボタンを押した時刻」を記録する (2026-08-23)。
//
// プレイに入るときドメインリロードが走ると static は全部消えるので、押した時刻を
// SessionState (リロードを跨いで残る) に置いておき、プレイ側の最初の Awake で差を取る。
// これで **ドメインリロードとシーンロードの実時間** が計測に入る。
// ============================================================================================
[InitializeOnLoad]
static class CarryStartupProfilerHook
{
    const string Key = "Carry.PlayPressedAt";

    static CarryStartupProfilerHook()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
            SessionState.SetFloat(Key, (float)EditorApplication.timeSinceStartup);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void CaptureAtPlayStart()
    {
        float pressed = SessionState.GetFloat(Key, 0f);
        if (pressed > 0f)
            CarryStartupProfile.PrePlaySeconds = EditorApplication.timeSinceStartup - pressed;
    }
}
