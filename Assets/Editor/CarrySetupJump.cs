using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CarrySetupJump
{
    private const string ScenePath = "Assets/Scenes/CastleStage.unity";
    private const string GoblinFbxPath = "Assets/Goblin/Grimfang_Goblin.fbx";
    private const string ControllerPath = "Assets/Goblin/GoblinAnimator.controller";

    [MenuItem("Carry/Setup Jump (Walk and Run)")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            // 1) loop settings: everything loops except the two one-shot jump clips
            var importer = AssetImporter.GetAtPath(GoblinFbxPath) as ModelImporter;
            if (importer == null) throw new System.Exception("Goblin importer not found.");
            var clips = importer.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                bool isJump = clips[i].name.EndsWith("Carry_Jump_Walk") || clips[i].name.EndsWith("Carry_Jump_Run");
                clips[i].loopTime = !isJump;
                clips[i].loopPose = !isJump;
            }
            importer.clipAnimations = clips;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.SaveAndReimport();
            log.AppendLine("Configured clip looping (jump clips = one-shot, rest = loop).");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var goblin = GameObject.Find("Goblin");
            if (goblin == null) throw new System.Exception("Goblin not found in scene.");

            var loaded = AssetDatabase.LoadAllAssetsAtPath(GoblinFbxPath);
            AnimationClip walkClip = FindClip(loaded, "Carry_Walk_Low");
            AnimationClip runClip = FindClip(loaded, "Carry_Run");
            AnimationClip jumpWalkClip = FindClip(loaded, "Carry_Jump_Walk");
            AnimationClip jumpRunClip = FindClip(loaded, "Carry_Jump_Run");
            if (walkClip == null || runClip == null || jumpWalkClip == null || jumpRunClip == null)
                throw new System.Exception("Missing clip(s). walk=" + walkClip + " run=" + runClip +
                    " jumpWalk=" + jumpWalkClip + " jumpRun=" + jumpRunClip);

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("IsRunning", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;
            var walkState = sm.AddState("Walk", new Vector3(280, 0, 0));
            walkState.motion = walkClip;
            var runState = sm.AddState("Run", new Vector3(280, 140, 0));
            runState.motion = runClip;
            var jumpWalkState = sm.AddState("JumpFromWalk", new Vector3(560, 0, 0));
            jumpWalkState.motion = jumpWalkClip;
            var jumpRunState = sm.AddState("JumpFromRun", new Vector3(560, 140, 0));
            jumpRunState.motion = jumpRunClip;
            sm.defaultState = walkState;

            AnimatorStateTransition t;

            t = walkState.AddTransition(runState);
            t.hasExitTime = false; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.If, 0, "IsRunning");

            t = runState.AddTransition(walkState);
            t.hasExitTime = false; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsRunning");

            t = walkState.AddTransition(jumpWalkState);
            t.hasExitTime = false; t.duration = 0.08f;
            t.AddCondition(AnimatorConditionMode.If, 0, "Jump");

            t = runState.AddTransition(jumpRunState);
            t.hasExitTime = false; t.duration = 0.08f;
            t.AddCondition(AnimatorConditionMode.If, 0, "Jump");

            t = jumpWalkState.AddTransition(runState);
            t.hasExitTime = true; t.exitTime = 0.92f; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.If, 0, "IsRunning");
            t = jumpWalkState.AddTransition(walkState);
            t.hasExitTime = true; t.exitTime = 0.92f; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsRunning");

            t = jumpRunState.AddTransition(runState);
            t.hasExitTime = true; t.exitTime = 0.92f; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.If, 0, "IsRunning");
            t = jumpRunState.AddTransition(walkState);
            t.hasExitTime = true; t.exitTime = 0.92f; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsRunning");

            log.AppendLine("Rebuilt AnimatorController: Walk/Run/JumpFromWalk/JumpFromRun.");

            var animator = goblin.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

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
