using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 歩行パラメータは GoblinCarryRig がプレハブ化されておらず **シーンに直接** 保存されている
// ため、ステージを増やすたびに値がバラける。歩行クリップを焼き直したら必ずここを通して
// 全ステージへ同じ値を配る (2026-08-24 に Slow_Orc_Walk へ差し替えた際に必要になった)。
public static class CarryWalkParamSync
{
    static readonly string[] Stages = { "CastleStage", "ForestStage_Realistic" };

    [MenuItem("Carry/歩行パラメータを全ステージへ配る")]
    public static void Run() { Debug.Log(Sync(3.375f, 0.4531f, 0.2f, 0.35f, 0.08f, 7f, 0.15f, 0.12f, 0.08f, 5.5f, 18f)); }

    public static string Sync(float cycle, float strideRef, float upper, float shoulder, float head, float jumpSpeed, float jumpTakeoff, float antiStand, float antiMove,
        float staggerThreshold, float staggerRamp)
    {
        string opened = EditorSceneManager.GetActiveScene().path;
        if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveOpenScenes();

        var log = new System.Text.StringBuilder();
        foreach (var name in Stages)
        {
            string path = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Scene " + name))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == name) { path = p; break; }
            }
            if (path == null) { log.Append(name).Append(": シーンが無い / "); continue; }

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var rig = Object.FindFirstObjectByType<GoblinCarryRig>();
            if (rig == null) { log.Append(name).Append(": GoblinCarryRig が無い / "); continue; }

            var so = new SerializedObject(rig);
            so.FindProperty("walkCycleDuration").floatValue = cycle;
            so.FindProperty("walkStrideRefSpeed").floatValue = strideRef;
            so.FindProperty("walkUpperBodyWeight").floatValue = upper;
            so.FindProperty("walkShoulderWeight").floatValue = shoulder;
            so.FindProperty("walkHeadWeight").floatValue = head;
            so.FindProperty("jumpTakeoffTime").floatValue = jumpTakeoff;
            // よろけは 2026-08-24 に元の値へ戻した (ユーザー指示)。
            so.FindProperty("staggerThresholdDeg").floatValue = staggerThreshold;
            so.FindProperty("staggerRampDeg").floatValue = staggerRamp;
            // よろけ再設計 (2026-08-24)。浅い〜中は歩容の変調 (案A)、深いところだけ
            // 短い割り込み (案B)。同じ骨を奪い合わないので歩行と両立する。
            so.FindProperty("staggerEnabled").boolValue = true;
            so.FindProperty("staggerStrideShrink").floatValue = 0.5f;
            so.FindProperty("staggerCadenceBoost").floatValue = 1.7f;
            so.FindProperty("staggerStanceWidenDeg").floatValue = 8f;
            so.FindProperty("staggerHipDrop").floatValue = 0.07f;
            so.FindProperty("staggerLeanWeight").floatValue = 0.45f;
            // 深いよろけは割り込みではなく「傾いた方向へ引っぱられる」連続的な形にした。
            so.FindProperty("staggerDriftStartDeg").floatValue = 10f;
            so.FindProperty("staggerDriftFullDeg").floatValue = 22f;
            so.FindProperty("staggerDriftSpeed").floatValue = 0.45f;
            // 担ぎ姿勢そのものの左右の偏り。差し引かないと片側だけ判定が早く始まる。
            so.FindProperty("potNeutralRollDeg").floatValue = 3.7f;
            so.FindProperty("potTiltBiasDeg").floatValue = 1.2f;
            // 壺の左右可動域を広げ (2026-08-24)、その先にもう一段強いよろけを置いた。
            so.FindProperty("heightRange").floatValue = 0.24f;
            so.FindProperty("staggerHeavyStartDeg").floatValue = 16f;
            so.FindProperty("staggerHeavyFullDeg").floatValue = 24f;
            so.FindProperty("staggerHeavyStrideShrink").floatValue = 0.4f;
            so.FindProperty("staggerHeavyHipDrop").floatValue = 0.06f;
            so.FindProperty("staggerHeavyLurch").floatValue = 0.15f;
            so.FindProperty("staggerHeavyDriftSpeed").floatValue = 0.45f;
            // パリー後の壺の戻し (2026-08-25)。クリップ終端と通常担ぎ位置の 15cm 差を
            // 1 フレームで埋めていたのを、緩やかな追従で吸収する。
            so.FindProperty("potHandoverSeconds").floatValue = 0.45f;
            so.FindProperty("potHandoverFollowRate").floatValue = 3.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            // ジャンプ初速は GoblinLocomotion 側。歩行と同じくシーン直書きなので一緒に配る。
            var loco = rig.GetComponent<GoblinLocomotion>();
            if (loco != null)
            {
                var lso = new SerializedObject(loco);
                lso.FindProperty("jumpSpeed").floatValue = jumpSpeed;
                lso.FindProperty("jumpAnticipation").floatValue = antiStand;
                lso.FindProperty("jumpAnticipationMoving").floatValue = antiMove;
                // 歩行ジャンプの飛距離 (2026-08-24 実機修正)。1.4 では 1.5m しか出ず
                // 「足りない」と指摘された。2.0 で 2.1m。
                lso.FindProperty("walkJumpBoost").floatValue = 3.0f;
                lso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(loco);
            }
            EditorUtility.SetDirty(rig);

            // パリーの演出 (2026-08-25)。伸び上がりの再生速度。
            // スローモーション (cushionSlowMo) は削除済み。ここに戻さないこと。
            var acts = rig.GetComponent<GoblinPotActions>();
            if (acts != null)
            {
                var aso = new SerializedObject(acts);
                aso.FindProperty("cushionRiseSpeed").floatValue = 0.5f;
                aso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(acts);
            }

            // UI の目盛りも同じ角度に合わせる (危険円 = 秒読み開始、端 = 危険度最大)。
            var gauge = Object.FindFirstObjectByType<PotionGaugeUI>();
            if (gauge != null)
            {
                var gso = new SerializedObject(gauge);
                gso.FindProperty("worldTiltWarnDeg").floatValue = staggerThreshold;
                gso.FindProperty("worldTiltFullDeg").floatValue = 18f;
                gso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(gauge);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.Append(name).Append(": 更新 / ");
        }

        if (!string.IsNullOrEmpty(opened)) EditorSceneManager.OpenScene(opened, OpenSceneMode.Single);
        return log.ToString();
    }
}
