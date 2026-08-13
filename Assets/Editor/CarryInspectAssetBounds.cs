using System.Text;
using UnityEditor;
using UnityEngine;

// One-off diagnostic: logs the world-space render bounds of a fixed list of
// external assets so the forest dressing pass can scale/space them correctly
// instead of guessing. Not part of the stage build pipeline.
public static class CarryInspectAssetBounds
{
    static readonly string[] Paths =
    {
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/bridge_stoneNarrow.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/bridge_center_stone.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/bridge_side_stone.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/bridge_center_stoneRound.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/bridge_stone.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/log.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/log_large.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/log_stack.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/stump_round.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/stump_old.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/cliff_block_stone.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/cliff_block_rock.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/cliff_half_rock.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/cliff_top_rock.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/rock_largeA.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/rock_tallA.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/rock_smallA.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/ground_pathTile.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/ground_pathOpen.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/ground_pathStraight.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/ground_pathSide.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/ground_grass.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/platform_grass.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/platform_stone.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/path_stone.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/stone_largeA.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/stone_tallA.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/cliff_blockHalf_stone.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/cliff_blockQuarter_stone.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/cliff_half_stone.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/cliff_large_stone.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/ground_riverRocks.fbx",
        "Assets/ExternalAssets/KenneyNatureKit/Models/FBX format/ground_pathRocks.fbx",
        "Assets/ExternalAssets/KayKitForest/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx(unity)/Rock_2_A_Color1.fbx",
        "Assets/ExternalAssets/KayKitForest/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx(unity)/Rock_1_O_Color1.fbx",
        "Assets/ExternalAssets/KayKitForest/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx(unity)/Rock_3_N_Color1.fbx",
        "Assets/ExternalAssets/KayKitForest/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx(unity)/Tree_2_C_Color1.fbx",
        "Assets/ExternalAssets/KayKitForest/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx(unity)/Bush_2_B_Color1.fbx",
        "Assets/ExternalAssets/KayKitForest/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx(unity)/Bush_4_C_Color1.fbx",
        "Assets/ExternalAssets/KayKitForest/KayKit_Forest_Nature_Pack_1.0_FREE/Assets/fbx(unity)/Grass_1_A_Color1.fbx",
        "Assets/ExternalAssets/QuaterniusNatureMegaKit/FBX (Unity)/RockPath_Round_Wide.fbx",
        "Assets/ExternalAssets/QuaterniusNatureMegaKit/FBX (Unity)/RockPath_Round_Small_1.fbx",
        "Assets/ExternalAssets/QuaterniusNatureMegaKit/FBX (Unity)/Rock_Medium_1.fbx",
        "Assets/ExternalAssets/QuaterniusNatureMegaKit/FBX (Unity)/DeadTree_1.fbx",
        "Assets/ExternalAssets/QuaterniusNatureMegaKit/FBX (Unity)/CommonTree_1.fbx",
        "Assets/ExternalAssets/PolyHaven/rock_moss_set_01/rock_moss_set_01_2k.fbx",
        "Assets/ExternalAssets/PolyHaven/moss_01/moss_01_2k.fbx",
        "Assets/ExternalAssets/PolyHaven/boulder_01/boulder_01_2k.fbx",
        "Assets/ExternalAssets/PolyHaven/rock_moss_set_02/rock_moss_set_02_2k.fbx",
        "Assets/ExternalAssets/PolyHaven/dead_tree_trunk/dead_tree_trunk_2k.fbx",
        "Assets/ExternalAssets/PolyHaven/dead_tree_trunk_02/dead_tree_trunk_02_2k.fbx",
        "Assets/ExternalAssets/PolyHaven/dry_branches_medium_01/dry_branches_medium_01_2k.fbx",
        "Assets/ExternalAssets/PolyHaven/fern_02/fern_02_2k.fbx",
        "Assets/ExternalAssets/PolyHaven/grass_medium_01/grass_medium_01_2k.fbx",
        "Assets/ExternalAssets/PolyHaven/pine_roots/pine_roots_2k.fbx",
        "Assets/ExternalAssets/PolyHaven/tree_stump_01/tree_stump_01_2k.fbx",
        "Assets/ExternalAssets/PolyHaven/tree_stump_02/tree_stump_02_2k.fbx",
        "Assets/ExternalAssets/PolyHaven/fir_sapling/fir_sapling_2k.fbx",
        "Assets/ExternalAssets/PolyHaven/pine_sapling_small/pine_sapling_small_2k.fbx",
    };

    [MenuItem("Carry/Debug/Log External Asset Bounds")]
    public static void Run()
    {
        var log = new StringBuilder();
        foreach (var path in Paths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                log.AppendLine(path + " => NOT FOUND");
                continue;
            }
            var instance = (GameObject)Object.Instantiate(prefab);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                log.AppendLine(path + " => NO RENDERERS");
                Object.DestroyImmediate(instance);
                continue;
            }
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            log.AppendLine(System.IO.Path.GetFileName(path) + " => size=" + bounds.size.ToString("F3") +
                " center=" + bounds.center.ToString("F3") + " min=" + bounds.min.ToString("F3") + " max=" + bounds.max.ToString("F3"));

            Object.DestroyImmediate(instance);
        }
        Debug.Log(log.ToString());
    }
}
