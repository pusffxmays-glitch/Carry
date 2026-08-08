using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CarryFixLocomotionAnimator
{
    private const string ScenePath = "Assets/Scenes/CastleStage.unity";
    private const string GoblinFbxPath = "Assets/Goblin/Grimfang_Goblin.fbx";
    private const string ControllerPath = "Assets/Goblin/GoblinAnimator.controller";

    [MenuItem("Carry/Fix Locomotion Animator (Walk-Run only, freeze on stop)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var loaded = AssetDatabase.LoadAllAssetsAtPath(GoblinFbxPath);
            AnimationClip walkClip = FindClip(loaded, "Carry_Walk_Low");
            AnimationClip runClip = FindClip(loaded, "Carry_Run");
            if (walkClip == null || runClip == null)
                throw new System.Exception("Missing clip(s). walk=" + walkClip + " run=" + runClip);

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("IsRunning", AnimatorControllerParameterType.Bool);

            var sm = controller.layers[0].stateMachine;
            var walkState = sm.AddState("Walk", new Vector3(280, 60, 0));
            walkState.motion = walkClip;
            var runState = sm.AddState("Run", new Vector3(280, 180, 0));
            runState.motion = runClip;
            sm.defaultState = walkState;

            AnimatorStateTransition t;
            t = walkState.AddTransition(runState);
            t.hasExitTime = false; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.If, 0, "IsRunning");

            t = runState.AddTransition(walkState);
            t.hasExitTime = false; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsRunning");

            log.AppendLine("Rebuilt AnimatorController: Walk/Run only, IsRunning param, no Idle transitions.");

            var goblin = GameObject.Find("Goblin");
            if (goblin == null) throw new System.Exception("Goblin not found in scene.");
            var animator = goblin.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            var locomotion = goblin.GetComponent<GoblinLocomotion>();
            if (locomotion != null)
            {
                locomotion.walkSpeed = 0.5f;
                log.AppendLine("Set GoblinLocomotion.walkSpeed = 0.5 (was likely 2.0, causing foot slide).");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        Debug.Log(log.ToString());
    }

    private static AnimationClip FindClip(Object[] assets, string suffix)
    {
        foreach (var a in assets)
        {
            var c = a as AnimationClip;
            if (c == null) continue;
            if (c.name.StartsWith("__preview__")) continue;
            if (c.name == suffix || c.name.EndsWith("|" + suffix)) return c;
        }
        return null;
    }
}
