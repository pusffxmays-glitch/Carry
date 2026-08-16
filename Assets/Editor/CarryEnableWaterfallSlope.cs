using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-08-16 (StagePlayマージ後): stage branch で FluidCore.compute に追加した「傾斜に沿って
// 流れ落ちる滝」機能(SlopeProfileBuf等)を、StagePlay側の共有Player-branch流体システムへ
// 移植した(FluidCore.cs/FluidCore.compute側の変更、既存のポーション運搬物理には無影響)。
// このスクリプトは、既存の PotionWaterfallFluid オブジェクト(groundY等は変更しない)へ、
// 実測Terrain断面から作った slopeProfileHeights/slopeZStart/slopeZEnd だけを設定する。
// CarrySetupWaterfallFluid.cs(#if falseで無効化中、まだ移植していない滝リサイクル機能に依存)
// とは独立して動作する。
public static class CarryEnableWaterfallSlope
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const float RecessX = -3.3f;
    const float SlopeZStart = -28f, SlopeZEnd = -45f; // CarrySetupWaterfallFluid.cs(stage版)と同じ範囲
    const int SlopeSamples = 30;

    [MenuItem("Carry/Enable Waterfall Slope Collision (StagePlay)")]
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
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            log.AppendLine($"Slope profile set: {heights.Length} samples, z=[{SlopeZStart},{SlopeZEnd}], height=[{Mathf.Min(heights):F2},{Mathf.Max(heights):F2}]. SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }
}
