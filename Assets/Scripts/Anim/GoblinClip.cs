using UnityEngine;

// ============================================================================================
// GoblinClip -- Blender からベイクした全身アニメーション (GoblinClipData_*) のランタイム表現。
//
// 2026-08-15 追加。GoblinWalk/GoblinStagger と同じ「フレームごとのワールド姿勢を C# に焼く」
// 方式を全ボーン + 位置 + 壺に拡張したもの (bake_clip_to_cs.py が生成)。
// データ配列はボーン優先 (index = bone * FrameCount + frame)。
// 方向は root ローカルの単位ベクトル、位置は root ローカル (m)。GroundY を引くと
// 「足が root の y=0 に接地する」座標になる。
// ============================================================================================
public class GoblinClip
{
    public string name;
    public int frameCount;
    public float fps;
    public bool loop;
    public float groundY;
    public int potReleaseFrame;
    public string[] bones;
    public Vector3[] pos, ydir, xdir;
    public Vector3[] potPos, potY, potX;

    public float Duration => frameCount / fps;
    public bool HasPot => potPos != null && potPos.Length > 0;

    public int BoneIndex(string unityName)
    {
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] == unityName) return i;
        return -1;
    }

    // frame は小数フレーム。ループならラップ、そうでなければ端でクランプ。
    public void SampleBone(int b, float frame, out Vector3 p, out Vector3 y, out Vector3 x)
    {
        int i0, i1; float t;
        FrameIndices(frame, out i0, out i1, out t);
        int o0 = b * frameCount + i0, o1 = b * frameCount + i1;
        p = Vector3.LerpUnclamped(pos[o0], pos[o1], t);
        y = Vector3.Slerp(ydir[o0], ydir[o1], t).normalized;
        x = Vector3.Slerp(xdir[o0], xdir[o1], t).normalized;
    }

    public void SamplePot(float frame, out Vector3 p, out Quaternion rot)
    {
        SamplePotMirrorable(frame, false, out p, out rot);
    }

    // mirror: X 反転 (横転倒の左右反転再生用)
    public void SamplePotMirrorable(float frame, bool mirror, out Vector3 p, out Quaternion rot)
    {
        int i0, i1; float t;
        FrameIndices(frame, out i0, out i1, out t);
        p = Vector3.LerpUnclamped(potPos[i0], potPos[i1], t);
        Vector3 y = Vector3.Slerp(potY[i0], potY[i1], t).normalized;
        Vector3 x = Vector3.Slerp(potX[i0], potX[i1], t).normalized;
        // 鏡映: 位置・Y 軸は (-x,y,z)、X 軸 (ロール基準) は (x,-y,-z)。GoblinClipAnimator の注記参照。
        if (mirror) { p.x = -p.x; y.x = -y.x; x.y = -x.y; x.z = -x.z; }
        Vector3 z = Vector3.Cross(x, y).normalized;
        rot = Quaternion.LookRotation(z, y);
    }

    void FrameIndices(float frame, out int i0, out int i1, out float t)
    {
        if (loop)
        {
            frame = Mathf.Repeat(frame, frameCount);
            i0 = Mathf.FloorToInt(frame) % frameCount;
            i1 = (i0 + 1) % frameCount;
        }
        else
        {
            frame = Mathf.Clamp(frame, 0f, frameCount - 1.0001f);
            i0 = Mathf.FloorToInt(frame);
            i1 = Mathf.Min(i0 + 1, frameCount - 1);
        }
        t = frame - Mathf.Floor(frame);
    }

    // ---- 生成データから作る静的インスタンス ----
    static GoblinClip _potDown, _fallOver, _idle, _walk, _run, _jump, _rope;

    public static GoblinClip PotDown => _potDown ??= new GoblinClip
    {
        name = "PotDown", frameCount = GoblinClipData_PotDown.FrameCount, fps = GoblinClipData_PotDown.Fps,
        loop = GoblinClipData_PotDown.Loop, groundY = GoblinClipData_PotDown.GroundY,
        potReleaseFrame = GoblinClipData_PotDown.PotReleaseFrame, bones = GoblinClipData_PotDown.Bones,
        pos = GoblinClipData_PotDown.Pos, ydir = GoblinClipData_PotDown.YDir, xdir = GoblinClipData_PotDown.XDir,
        potPos = GoblinClipData_PotDown.PotPos, potY = GoblinClipData_PotDown.PotYDir, potX = GoblinClipData_PotDown.PotXDir,
    };

    public static GoblinClip FallOver => _fallOver ??= new GoblinClip
    {
        name = "FallOver", frameCount = GoblinClipData_FallOver.FrameCount, fps = GoblinClipData_FallOver.Fps,
        loop = GoblinClipData_FallOver.Loop, groundY = GoblinClipData_FallOver.GroundY,
        potReleaseFrame = GoblinClipData_FallOver.PotReleaseFrame, bones = GoblinClipData_FallOver.Bones,
        pos = GoblinClipData_FallOver.Pos, ydir = GoblinClipData_FallOver.YDir, xdir = GoblinClipData_FallOver.XDir,
        potPos = GoblinClipData_FallOver.PotPos, potY = GoblinClipData_FallOver.PotYDir, potX = GoblinClipData_FallOver.PotXDir,
    };

    public static GoblinClip NoPotIdle => _idle ??= new GoblinClip
    {
        name = "NoPotIdle", frameCount = GoblinClipData_NoPotIdle.FrameCount, fps = GoblinClipData_NoPotIdle.Fps,
        loop = true, groundY = GoblinClipData_NoPotIdle.GroundY, potReleaseFrame = -1,
        bones = GoblinClipData_NoPotIdle.Bones,
        pos = GoblinClipData_NoPotIdle.Pos, ydir = GoblinClipData_NoPotIdle.YDir, xdir = GoblinClipData_NoPotIdle.XDir,
    };

    public static GoblinClip NoPotWalk => _walk ??= new GoblinClip
    {
        name = "NoPotWalk", frameCount = GoblinClipData_NoPotWalk.FrameCount, fps = GoblinClipData_NoPotWalk.Fps,
        loop = true, groundY = GoblinClipData_NoPotWalk.GroundY, potReleaseFrame = -1,
        bones = GoblinClipData_NoPotWalk.Bones,
        pos = GoblinClipData_NoPotWalk.Pos, ydir = GoblinClipData_NoPotWalk.YDir, xdir = GoblinClipData_NoPotWalk.XDir,
    };

    public static GoblinClip NoPotRun => _run ??= new GoblinClip
    {
        name = "NoPotRun", frameCount = GoblinClipData_NoPotRun.FrameCount, fps = GoblinClipData_NoPotRun.Fps,
        loop = true, groundY = GoblinClipData_NoPotRun.GroundY, potReleaseFrame = -1,
        bones = GoblinClipData_NoPotRun.Bones,
        pos = GoblinClipData_NoPotRun.Pos, ydir = GoblinClipData_NoPotRun.YDir, xdir = GoblinClipData_NoPotRun.XDir,
    };

    // 2026-08-16: 静的ポーズのジャンプをやめ、ストック由来の歩き/走りジャンプ (腕は
    // アイドル腕に差し替え済み) に置き換え。非ループ (最終ポーズ保持)。
    public static GoblinClip NoPotJumpWalk => _jump ??= new GoblinClip
    {
        name = "NoPotJumpWalk", frameCount = GoblinClipData_NoPotJumpWalk.FrameCount, fps = GoblinClipData_NoPotJumpWalk.Fps,
        loop = false, groundY = GoblinClipData_NoPotJumpWalk.GroundY, potReleaseFrame = -1,
        bones = GoblinClipData_NoPotJumpWalk.Bones,
        pos = GoblinClipData_NoPotJumpWalk.Pos, ydir = GoblinClipData_NoPotJumpWalk.YDir, xdir = GoblinClipData_NoPotJumpWalk.XDir,
    };

    static GoblinClip _jumpRun;
    public static GoblinClip NoPotJumpRun => _jumpRun ??= new GoblinClip
    {
        name = "NoPotJumpRun", frameCount = GoblinClipData_NoPotJumpRun.FrameCount, fps = GoblinClipData_NoPotJumpRun.Fps,
        loop = false, groundY = GoblinClipData_NoPotJumpRun.GroundY, potReleaseFrame = -1,
        bones = GoblinClipData_NoPotJumpRun.Bones,
        pos = GoblinClipData_NoPotJumpRun.Pos, ydir = GoblinClipData_NoPotJumpRun.YDir, xdir = GoblinClipData_NoPotJumpRun.XDir,
    };

    static GoblinClip _hotJump;
    // 熱い床 (マグマ) を踏んだときの「あちち」ジャンプ (2026-08-16 ギミック 9)。
    public static GoblinClip HotJump => _hotJump ??= new GoblinClip
    {
        name = "HotJump", frameCount = GoblinClipData_HotJump.FrameCount, fps = GoblinClipData_HotJump.Fps,
        loop = false, groundY = GoblinClipData_HotJump.GroundY, potReleaseFrame = -1,
        bones = GoblinClipData_HotJump.Bones,
        pos = GoblinClipData_HotJump.Pos, ydir = GoblinClipData_HotJump.YDir, xdir = GoblinClipData_HotJump.XDir,
        potPos = GoblinClipData_HotJump.PotPos, potY = GoblinClipData_HotJump.PotYDir, potX = GoblinClipData_HotJump.PotXDir,
    };

    static GoblinClip _cushion, _cushionDeep;
    // 着地クッション (2026-08-16 追補 15)。着地に Space を合わせると膝で衝撃を吸収する。
    public static GoblinClip LandCushion => _cushion ??= new GoblinClip
    {
        name = "LandCushion", frameCount = GoblinClipData_LandCushion.FrameCount, fps = GoblinClipData_LandCushion.Fps,
        loop = false, groundY = GoblinClipData_LandCushion.GroundY, potReleaseFrame = -1,
        bones = GoblinClipData_LandCushion.Bones,
        pos = GoblinClipData_LandCushion.Pos, ydir = GoblinClipData_LandCushion.YDir, xdir = GoblinClipData_LandCushion.XDir,
        potPos = GoblinClipData_LandCushion.PotPos, potY = GoblinClipData_LandCushion.PotYDir, potX = GoblinClipData_LandCushion.PotXDir,
    };
    // 走り着地用の深いバリエーション (左足前スタンス)
    public static GoblinClip LandCushionDeep => _cushionDeep ??= new GoblinClip
    {
        name = "LandCushionDeep", frameCount = GoblinClipData_LandCushionDeep.FrameCount, fps = GoblinClipData_LandCushionDeep.Fps,
        loop = false, groundY = GoblinClipData_LandCushionDeep.GroundY, potReleaseFrame = -1,
        bones = GoblinClipData_LandCushionDeep.Bones,
        pos = GoblinClipData_LandCushionDeep.Pos, ydir = GoblinClipData_LandCushionDeep.YDir, xdir = GoblinClipData_LandCushionDeep.XDir,
        potPos = GoblinClipData_LandCushionDeep.PotPos, potY = GoblinClipData_LandCushionDeep.PotYDir, potX = GoblinClipData_LandCushionDeep.PotXDir,
    };

    static GoblinClip _drown;
    // 川に流されている間のおぼれもがき (2026-08-17)。Blender action 'NoPot_Drown'。
    // 交互の水面叩き + バタ足 + のけぞり首振り。RiverFlowController の sweep 中に
    // GoblinPotActions.sweptByRiver 経由でループ再生される。
    public static GoblinClip Drown => _drown ??= new GoblinClip
    {
        name = "Drown", frameCount = GoblinClipData_Drown.FrameCount, fps = GoblinClipData_Drown.Fps,
        loop = true, groundY = GoblinClipData_Drown.GroundY, potReleaseFrame = -1,
        bones = GoblinClipData_Drown.Bones,
        pos = GoblinClipData_Drown.Pos, ydir = GoblinClipData_Drown.YDir, xdir = GoblinClipData_Drown.XDir,
    };

    public static GoblinClip RopeWalk => _rope ??= new GoblinClip
    {
        name = "RopeWalk", frameCount = GoblinClipData_RopeWalk.FrameCount, fps = GoblinClipData_RopeWalk.Fps,
        loop = true, groundY = GoblinClipData_RopeWalk.GroundY, potReleaseFrame = -1,
        bones = GoblinClipData_RopeWalk.Bones,
        pos = GoblinClipData_RopeWalk.Pos, ydir = GoblinClipData_RopeWalk.YDir, xdir = GoblinClipData_RopeWalk.XDir,
    };
}
