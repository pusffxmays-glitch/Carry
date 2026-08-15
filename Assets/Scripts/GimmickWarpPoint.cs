using UnityEngine;

// ============================================================================================
// GimmickWarpPoint -- デバッグ用ワープ先。数字キーの番号と対応する。
//
// ギミックを作るときに、その手前へ 1 つ置いておく。位置と向きをそのまま使うので、
// Scene ビューで動かせば飛び先も変わる。
// ============================================================================================
public class GimmickWarpPoint : MonoBehaviour
{
    [Tooltip("対応する数字キー (1..9)。")]
    [Range(1, 9)] public int number = 1;
    [Tooltip("ログ表示用の名前。")]
    public string label = "";

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.1f, 0.4f);
        Gizmos.DrawLine(transform.position + Vector3.up * 0.1f,
                        transform.position + Vector3.up * 0.1f + transform.forward * 1.2f);
    }
}
