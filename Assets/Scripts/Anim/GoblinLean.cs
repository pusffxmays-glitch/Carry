using UnityEngine;

// バランスのカウンター姿勢。GoblinCarryRig.ApplyBalanceLean が引く。
// 生成: bake_lean_cs.py (2026-08-24)
//
// 壺が傾いた方と **逆** へ上体を倒す動き。荷を頭上に担いだ人が実際にやることで、
// これが無いとキャラは硬い台車のままで、その上の壺だけが傾いている絵になる。
//
// 既製アクションから単一軸に倒れたポーズを抜き、担ぎの基準姿勢からのズレとして持つ:
//   SideP  Boxing_Practice F105
//   SideN  Boxing_Practice F60
//   Fore   Slow_Orc_Walk F128
//   Back   Agree_Gesture F250
//
// しきい値で切り替えるのではなく、傾き量で identity からこのズレへ補間する。
// 段階的に切り替わると「モード」に見えてしまうため。
public static class GoblinLean
{
    /// <summary>Y をボーンの軸、X を捻りの基準として回転を組む (BlendAimFull と同じ規約)。</summary>
    static Quaternion Basis(Vector3 y, Vector3 x)
    {
        y = y.normalized;
        x = (x - y * Vector3.Dot(x, y)).normalized;
        return Quaternion.LookRotation(Vector3.Cross(x, y), y);
    }

    static readonly Vector3 SpineNeutralY = new Vector3(-0.00547f,0.90458f,0.42627f);
    static readonly Vector3 SpineNeutralX = new Vector3(0.99994f,0.00086f,0.01101f);
    static readonly Vector3 SpineSidePY = new Vector3(0.42704f,0.90294f,-0.04836f);
    static readonly Vector3 SpineSidePX = new Vector3(-0.23135f,0.05740f,-0.97118f);
    static readonly Vector3 SpineSideNY = new Vector3(-0.39990f,0.86607f,-0.29999f);
    static readonly Vector3 SpineSideNX = new Vector3(0.91166f,0.34207f,-0.22772f);
    static readonly Vector3 SpineForeY = new Vector3(-0.04680f,0.68833f,0.72389f);
    static readonly Vector3 SpineForeX = new Vector3(0.86992f,-0.32811f,0.36822f);
    static readonly Vector3 SpineBackY = new Vector3(0.03199f,0.99184f,-0.12338f);
    static readonly Vector3 SpineBackX = new Vector3(0.99458f,-0.01937f,0.10218f);
    static readonly Vector3 Spine01NeutralY = new Vector3(-0.00503f,0.94221f,0.33498f);
    static readonly Vector3 Spine01NeutralX = new Vector3(0.99995f,0.00174f,0.01011f);
    static readonly Vector3 Spine01SidePY = new Vector3(0.49394f,0.86755f,-0.05815f);
    static readonly Vector3 Spine01SidePX = new Vector3(-0.22845f,0.06496f,-0.97139f);
    static readonly Vector3 Spine01SideNY = new Vector3(-0.37025f,0.90697f,-0.20079f);
    static readonly Vector3 Spine01SideNX = new Vector3(0.91884f,0.32580f,-0.22271f);
    static readonly Vector3 Spine01ForeY = new Vector3(-0.07746f,0.64537f,0.75994f);
    static readonly Vector3 Spine01ForeX = new Vector3(0.87066f,-0.32758f,0.36694f);
    static readonly Vector3 Spine01BackY = new Vector3(0.02193f,0.99951f,-0.02251f);
    static readonly Vector3 Spine01BackX = new Vector3(0.99450f,-0.01950f,0.10291f);
    static readonly Vector3 Spine02NeutralY = new Vector3(-0.00652f,0.99591f,0.09012f);
    static readonly Vector3 Spine02NeutralX = new Vector3(0.99997f,0.00609f,0.00501f);
    static readonly Vector3 Spine02SidePY = new Vector3(0.38012f,0.91704f,0.12065f);
    static readonly Vector3 Spine02SidePX = new Vector3(0.04680f,0.11120f,-0.99270f);
    static readonly Vector3 Spine02SideNY = new Vector3(-0.40937f,0.90346f,-0.12717f);
    static readonly Vector3 Spine02SideNX = new Vector3(0.91174f,0.41025f,-0.02045f);
    static readonly Vector3 Spine02ForeY = new Vector3(-0.02662f,0.62167f,0.78283f);
    static readonly Vector3 Spine02ForeX = new Vector3(0.88029f,-0.35650f,0.31304f);
    static readonly Vector3 Spine02BackY = new Vector3(0.03496f,0.97857f,-0.20294f);
    static readonly Vector3 Spine02BackX = new Vector3(0.99240f,-0.01002f,0.12264f);
    static readonly Vector3 NeckNeutralY = new Vector3(-0.01978f,0.85178f,0.52353f);
    static readonly Vector3 NeckNeutralX = new Vector3(0.99949f,0.00381f,0.03156f);
    static readonly Vector3 NeckSidePY = new Vector3(0.53308f,0.84025f,0.09902f);
    static readonly Vector3 NeckSidePX = new Vector3(0.12309f,0.03877f,-0.99164f);
    static readonly Vector3 NeckSideNY = new Vector3(-0.54586f,0.83632f,0.05103f);
    static readonly Vector3 NeckSideNX = new Vector3(0.82259f,0.54649f,-0.15715f);
    static readonly Vector3 NeckForeY = new Vector3(-0.02191f,0.65372f,0.75642f);
    static readonly Vector3 NeckForeX = new Vector3(0.94207f,-0.23978f,0.23452f);
    static readonly Vector3 NeckBackY = new Vector3(-0.02144f,0.98283f,-0.18327f);
    static readonly Vector3 NeckBackX = new Vector3(0.98866f,0.04810f,0.14229f);
    static readonly Vector3 HeadNeutralY = new Vector3(-0.00679f,0.08166f,0.99664f);
    static readonly Vector3 HeadNeutralX = new Vector3(0.99994f,-0.00818f,0.00748f);
    static readonly Vector3 HeadSidePY = new Vector3(0.85051f,0.27564f,0.44796f);
    static readonly Vector3 HeadSidePX = new Vector3(0.44249f,0.08543f,-0.89269f);
    static readonly Vector3 HeadSideNY = new Vector3(-0.31036f,0.64123f,0.70178f);
    static readonly Vector3 HeadSideNX = new Vector3(0.81264f,0.56201f,-0.15413f);
    static readonly Vector3 HeadForeY = new Vector3(-0.22094f,0.09869f,0.97028f);
    static readonly Vector3 HeadForeX = new Vector3(0.97301f,-0.04562f,0.22620f);
    static readonly Vector3 HeadBackY = new Vector3(-0.18173f,0.43497f,0.88192f);
    static readonly Vector3 HeadBackX = new Vector3(0.98330f,0.07142f,0.16740f);

    // 基準姿勢からのズレ。static フィールドは宣言順に初期化されるので上の後に置く。
    static readonly Quaternion SpineN = Basis(SpineNeutralY, SpineNeutralX);
    static readonly Quaternion Spine01N = Basis(Spine01NeutralY, Spine01NeutralX);
    static readonly Quaternion Spine02N = Basis(Spine02NeutralY, Spine02NeutralX);
    static readonly Quaternion NeckN = Basis(NeckNeutralY, NeckNeutralX);
    static readonly Quaternion HeadN = Basis(HeadNeutralY, HeadNeutralX);
    public static readonly Quaternion SpineSideP = Basis(SpineSidePY, SpineSidePX) * Quaternion.Inverse(SpineN);
    public static readonly Quaternion SpineSideN = Basis(SpineSideNY, SpineSideNX) * Quaternion.Inverse(SpineN);
    public static readonly Quaternion SpineFore = Basis(SpineForeY, SpineForeX) * Quaternion.Inverse(SpineN);
    public static readonly Quaternion SpineBack = Basis(SpineBackY, SpineBackX) * Quaternion.Inverse(SpineN);
    public static readonly Quaternion Spine01SideP = Basis(Spine01SidePY, Spine01SidePX) * Quaternion.Inverse(Spine01N);
    public static readonly Quaternion Spine01SideN = Basis(Spine01SideNY, Spine01SideNX) * Quaternion.Inverse(Spine01N);
    public static readonly Quaternion Spine01Fore = Basis(Spine01ForeY, Spine01ForeX) * Quaternion.Inverse(Spine01N);
    public static readonly Quaternion Spine01Back = Basis(Spine01BackY, Spine01BackX) * Quaternion.Inverse(Spine01N);
    public static readonly Quaternion Spine02SideP = Basis(Spine02SidePY, Spine02SidePX) * Quaternion.Inverse(Spine02N);
    public static readonly Quaternion Spine02SideN = Basis(Spine02SideNY, Spine02SideNX) * Quaternion.Inverse(Spine02N);
    public static readonly Quaternion Spine02Fore = Basis(Spine02ForeY, Spine02ForeX) * Quaternion.Inverse(Spine02N);
    public static readonly Quaternion Spine02Back = Basis(Spine02BackY, Spine02BackX) * Quaternion.Inverse(Spine02N);
    public static readonly Quaternion NeckSideP = Basis(NeckSidePY, NeckSidePX) * Quaternion.Inverse(NeckN);
    public static readonly Quaternion NeckSideN = Basis(NeckSideNY, NeckSideNX) * Quaternion.Inverse(NeckN);
    public static readonly Quaternion NeckFore = Basis(NeckForeY, NeckForeX) * Quaternion.Inverse(NeckN);
    public static readonly Quaternion NeckBack = Basis(NeckBackY, NeckBackX) * Quaternion.Inverse(NeckN);
    public static readonly Quaternion HeadSideP = Basis(HeadSidePY, HeadSidePX) * Quaternion.Inverse(HeadN);
    public static readonly Quaternion HeadSideN = Basis(HeadSideNY, HeadSideNX) * Quaternion.Inverse(HeadN);
    public static readonly Quaternion HeadFore = Basis(HeadForeY, HeadForeX) * Quaternion.Inverse(HeadN);
    public static readonly Quaternion HeadBack = Basis(HeadBackY, HeadBackX) * Quaternion.Inverse(HeadN);
}
