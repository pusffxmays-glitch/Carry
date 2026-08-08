using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CarryGameplaySetup
{
    private const string ScenePath = "Assets/Scenes/CastleStage.unity";
    private const string GoblinFbxPath = "Assets/Goblin/Grimfang_Goblin.fbx";
    private const string ControllerPath = "Assets/Goblin/GoblinAnimator.controller";

    [MenuItem("Carry/Setup Gameplay (Movement, Camera, Animator)")]
    public static void Run()
    {
        var log = new StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var stageRoot = GameObject.Find("Stage");
            var goblin = GameObject.Find("Goblin");
            var mainCam = GameObject.Find("Main Camera");
            if (stageRoot == null || goblin == null || mainCam == null)
                throw new System.Exception("Missing Stage/Goblin/Main Camera in scene.");

            int colliderCount = 0;
            foreach (var mf in stageRoot.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.GetComponent<Collider>() == null)
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    colliderCount++;
                }
            }
            log.AppendLine("Added " + colliderCount + " MeshColliders under Stage.");

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsRunning", AnimatorControllerParameterType.Bool);

            var loaded = AssetDatabase.LoadAllAssetsAtPath(GoblinFbxPath);
            AnimationClip idleClip = FindClip(loaded, "Idle");
            AnimationClip walkClip = FindClip(loaded, "Carry_Walk_Low");
            AnimationClip runClip = FindClip(loaded, "Carry_Run");
            if (idleClip == null || walkClip == null || runClip == null)
                throw new System.Exception("Missing clip(s). idle=" + idleClip + " walk=" + walkClip + " run=" + runClip);

            var sm = controller.layers[0].stateMachine;
            var idleState = sm.AddState("Idle", new Vector3(280, 0, 0));
            idleState.motion = idleClip;
            var walkState = sm.AddState("Walk", new Vector3(280, 120, 0));
            walkState.motion = walkClip;
            var runState = sm.AddState("Run", new Vector3(280, 240, 0));
            runState.motion = runClip;
            sm.defaultState = idleState;

            AnimatorStateTransition t;

            t = idleState.AddTransition(walkState);
            t.hasExitTime = false; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsRunning");

            t = idleState.AddTransition(runState);
            t.hasExitTime = false; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
            t.AddCondition(AnimatorConditionMode.If, 0, "IsRunning");

            t = walkState.AddTransition(idleState);
            t.hasExitTime = false; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");

            t = walkState.AddTransition(runState);
            t.hasExitTime = false; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.If, 0, "IsRunning");

            t = runState.AddTransition(walkState);
            t.hasExitTime = false; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsRunning");

            t = runState.AddTransition(idleState);
            t.hasExitTime = false; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");

            log.AppendLine("Rebuilt AnimatorController: Idle/Walk/Run + IsMoving/IsRunning params.");

            var animator = goblin.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            var cc = goblin.GetComponent<CharacterController>();
            if (cc == null) cc = goblin.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 1.0f, 0f);
            cc.height = 1.9f;
            cc.radius = 0.35f;
            cc.skinWidth = 0.03f;
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 50f;

            var locomotion = goblin.GetComponent<GoblinLocomotion>();
            if (locomotion == null) locomotion = goblin.AddComponent<GoblinLocomotion>();

            var rig = mainCam.GetComponent<CarryCameraRig>();
            if (rig == null) rig = mainCam.AddComponent<CarryCameraRig>();
            rig.target = goblin.transform;

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
