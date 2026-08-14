using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// ============================================================================================
// CarrySetupTestSlopes -- ゴブリンの「地形による傾き」を確認するための斜面を置く。
//
// 単純なボックスを傾けただけのもの（ユーザー指定）。3 種類:
//   Slope_Up        上り勾配   … +Z へ歩くと登る
//   Slope_BankRight 右傾斜     … +Z へ歩くとき **右 (+X) 側が下がる**
//   Slope_BankLeft  左傾斜     … +Z へ歩くとき **左 (-X) 側が下がる**
//
// 置き方の要点:
//   * 傾けたあと、**上面のいちばん低い角が床の高さに来るまで沈める**。
//     こうしないと、傾けた箱は片側が床に埋まり片側が浮くので、乗れる場所が無くなる。
//   * CharacterController の slopeLimit は 50 度なので、15 度は問題なく歩ける。
//
// 何度でも実行してよい。実行のたびに古い TestSlopes を作り直す。
// ============================================================================================
public static class CarrySetupTestSlopes
{
    const string RootName = "TestSlopes";

    // 傾斜角。ここを変えれば 3 つとも変わる。
    const float AngleDeg = 15f;

    const float Thickness = 0.6f;
    const float UpLength = 7f;    // 上り勾配の長さ (Z)
    const float UpWidth = 4f;
    const float BankLength = 7f;  // 傾斜路の長さ (Z)
    const float BankWidth = 3.5f;

    [MenuItem("Carry/Setup/Test Slopes を作る")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();

        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject(RootName);

        // 部屋は 24x24、ゴブリンの開始位置は原点。手前(-Z)から歩いて乗れるよう +Z 側に並べる。
        MakeSlope(root.transform, "Slope_Up",
                  new Vector3(0f, 0f, 6f),
                  Quaternion.Euler(-AngleDeg, 0f, 0f),
                  new Vector3(UpWidth, Thickness, UpLength));

        // 右傾斜: +X（進行方向 +Z に対して右）が下がる = Z 軸まわりに負の回転。
        MakeSlope(root.transform, "Slope_BankRight",
                  new Vector3(6.5f, 0f, 6f),
                  Quaternion.Euler(0f, 0f, -AngleDeg),
                  new Vector3(BankWidth, Thickness, BankLength));

        // 左傾斜: -X が下がる。
        MakeSlope(root.transform, "Slope_BankLeft",
                  new Vector3(-6.5f, 0f, 6f),
                  Quaternion.Euler(0f, 0f, AngleDeg),
                  new Vector3(BankWidth, Thickness, BankLength));

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"CarrySetupTestSlopes: {AngleDeg} 度の斜面を 3 つ作成しました（上り / 右傾斜 / 左傾斜）。", root);
    }

    static void MakeSlope(Transform parent, string name, Vector3 centre, Quaternion rot, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.rotation = rot;
        go.transform.localScale = size;
        go.transform.position = centre;

        // 上面 4 隅のうち **いちばん低い角** を床の高さ(0)に合わせる。
        // 傾けただけだと片側が浮いて乗れないので、ここで沈める。
        float lowest = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            var local = new Vector3(((i & 1) == 0) ? -0.5f : 0.5f, 0.5f, ((i & 2) == 0) ? -0.5f : 0.5f);
            lowest = Mathf.Min(lowest, go.transform.TransformPoint(local).y);
        }
        go.transform.position = centre + Vector3.down * (lowest - centre.y) + Vector3.down * 0.02f;

        // 見分けがつくよう軽く色を付ける（URP の既定 Lit をインスタンス化）。
        var mr = go.GetComponent<MeshRenderer>();
        var mat = new Material(mr.sharedMaterial);
        mat.name = name + "_Mat";
        mat.color = name.Contains("Up") ? new Color(0.62f, 0.60f, 0.55f)
                  : name.Contains("Right") ? new Color(0.66f, 0.56f, 0.50f)
                  : new Color(0.55f, 0.60f, 0.66f);
        mr.sharedMaterial = mat;

        // CreatePrimitive が付ける BoxCollider をそのまま使う（CharacterController が乗れる）。
    }
}
