using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-08-16 (StagePlayマージ後): stage branch で FluidCore.compute に追加した「傾斜に沿って
// 流れ落ちる滝」機能(SlopeProfileBuf等)と「滝リサイクル」機能(Retired粒子を水源付近へ
// 再スポーンして循環させ続ける、SpawnBoxMin/SpawnBoxSize/SpawnVelocity)を、StagePlay側の
// 共有Player-branch流体システムへ移植した(FluidCore.cs/FluidCore.compute側の変更、
// 既存のポーション運搬物理には無条件で無影響)。
// このスクリプトは、既存の PotionWaterfallFluid オブジェクト(groundY等は変更しない)へ、
// 実測Terrain断面から作った slopeProfileHeights/slopeZStart/slopeZEnd と、
// stage版 CarrySetupWaterfallFluid.cs と同じ spawnBoxMin/spawnBoxSize/spawnVelocity を設定する。
public static class CarryEnableWaterfallSlope
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const float RecessX = -3.3f;
    const float SlopeZStart = -28f, SlopeZEnd = -45f; // CarrySetupWaterfallFluid.cs(stage版)と同じ範囲
    const int SlopeSamples = 30;

    // stage版 CarrySetupWaterfallFluid.cs と同じ値(水源=recessの口、+Zへ押し出して池側へ向ける)。
    static readonly Vector3 SpawnBoxMin = new Vector3(-4.0f, 14.8f, -42.9f);
    static readonly Vector3 SpawnBoxSize = new Vector3(1.6f, 0.3f, 0.5f);
    static readonly Vector3 SpawnVelocity = new Vector3(0f, -2.6f, 4.2f);

    [MenuItem("Carry/Enable Waterfall Slope + Recycle (StagePlay)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainGo = GameObject.Find("ForestStage_Terrain/Terrain");
            var terrain = terrainGo.GetComponent<Terrain>();
            float oy = terrainGo.transform.position.y;

            var go = GameObject.Find("ForestStage_Terrain/Waterfalls/PotionWaterfallFluid");
            if (go == null) { Debug.LogError("PotionWaterfallFluid not found."); return; }
            var core = go.GetComponent("FluidCore");
            if (core == null) { Debug.LogError("FluidCore component not found on PotionWaterfallFluid."); return; }

            var heights = new float[SlopeSamples];
            for (int i = 0; i < SlopeSamples; i++)
            {
                float t = i / (float)(SlopeSamples - 1);
                float z = Mathf.Lerp(SlopeZStart, SlopeZEnd, t);
                heights[i] = terrain.SampleHeight(new Vector3(RecessX, 0f, z)) + oy;
            }

            var so = new SerializedObject(core);
            var heightsProp = so.FindProperty("slopeProfileHeights");
            heightsProp.arraySize = heights.Length;
            for (int i = 0; i < heights.Length; i++) heightsProp.GetArrayElementAtIndex(i).floatValue = heights[i];
            so.FindProperty("slopeZStart").floatValue = SlopeZStart;
            so.FindProperty("slopeZEnd").floatValue = SlopeZEnd;
            so.FindProperty("spawnBoxMin").vector3Value = SpawnBoxMin;
            so.FindProperty("spawnBoxSize").vector3Value = SpawnBoxSize;
            so.FindProperty("spawnVelocity").vector3Value = SpawnVelocity;
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            log.AppendLine($"Slope profile set: {heights.Length} samples, z=[{SlopeZStart},{SlopeZEnd}], height=[{Mathf.Min(heights):F2},{Mathf.Max(heights):F2}].");
            log.AppendLine($"Recycle spawn box set: min={SpawnBoxMin}, size={SpawnBoxSize}, velocity={SpawnVelocity}. SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }
}
