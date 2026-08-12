using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Reflection;
using System.IO;
using System.Text;

// Headless numeric verification for GoblinCarryRig's leg-chain math, since interactive Play Mode
// / screenshot verification isn't available in this session (no Unity MCP connection). Runs via
// `Unity.exe -batchmode -nographics -executeMethod GoblinRigVerifier.RunCheck -quit`, drives the
// SAME private methods LateUpdate calls (via reflection) across a full walk-cycle and both
// stagger directions, and dumps root-relative foot/leg positions to a text file so the actual
// numbers can be inspected directly instead of guessed at.
public static class GoblinRigVerifier
{
    const string OutPath = @"C:\work\Git\Carry\verify_output.txt";

    public static void RunCheck()
    {
        var sb = new StringBuilder();
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/CastleStage.unity");
            var rig = Object.FindFirstObjectByType<GoblinCarryRig>();
            if (rig == null)
            {
                sb.AppendLine("ERROR: GoblinCarryRig not found in CastleStage.unity");
                File.WriteAllText(OutPath, sb.ToString());
                return;
            }

            var T = typeof(GoblinCarryRig);
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

            MethodInfo mAwake = T.GetMethod("Awake", F);
            MethodInfo mApplyBasePose = T.GetMethod("ApplyBasePose", F);
            MethodInfo mApplyLegChain = T.GetMethod("ApplyLegChain", F);
            MethodInfo mBlendAimFull = T.GetMethod("BlendAimFull", F);

            if (mAwake == null || mApplyBasePose == null || mApplyLegChain == null || mBlendAimFull == null)
            {
                sb.AppendLine("ERROR: one or more expected private methods not found via reflection " +
                    $"(Awake={mAwake != null}, ApplyBasePose={mApplyBasePose != null}, " +
                    $"ApplyLegChain={mApplyLegChain != null}, BlendAimFull={mBlendAimFull != null})");
                File.WriteAllText(OutPath, sb.ToString());
                return;
            }

            mAwake.Invoke(rig, null);

            Transform Get(string name) => (Transform)T.GetField(name, F).GetValue(rig);
            float GetF(string name) => (float)T.GetField(name, F).GetValue(rig);

            Transform hipsBone = Get("hipsBone");
            Transform leftUpLegBone = Get("leftUpLegBone");
            Transform leftLegBone = Get("leftLegBone");
            Transform leftFootBone = Get("leftFootBone");
            Transform leftToeBone = Get("leftToeBone");
            Transform rightUpLegBone = Get("rightUpLegBone");
            Transform rightLegBone = Get("rightLegBone");
            Transform rightFootBone = Get("rightFootBone");
            Transform rightToeBone = Get("rightToeBone");

            float leftUpLegLen = GetF("leftUpLegLen");
            float leftLegLen = GetF("leftLegLen");
            float leftFootLen = GetF("leftFootLen");
            float rightUpLegLen = GetF("rightUpLegLen");
            float rightLegLen = GetF("rightLegLen");
            float rightFootLen = GetF("rightFootLen");

            Transform root = rig.transform;

            sb.AppendLine("bone lengths: leftUpLeg=" + leftUpLegLen + " leftLeg=" + leftLegLen + " leftFoot=" + leftFootLen +
                " rightUpLeg=" + rightUpLegLen + " rightLeg=" + rightLegLen + " rightFoot=" + rightFootLen);
            sb.AppendLine();

            sb.AppendLine("== WALK CYCLE (root-local coords) ==");
            sb.AppendLine("i,phase,Lfoot.x,Lfoot.y,Lfoot.z,Rfoot.x,Rfoot.y,Rfoot.z,Lleg.x,Lleg.y,Lleg.z,Rleg.x,Rleg.y,Rleg.z");

            for (int i = 0; i <= 60; i += 3)
            {
                float phase = (i % 60) / 60f;

                mApplyBasePose.Invoke(rig, null);

                GoblinWalk.SampleHips(phase, out Vector3 hy, out Vector3 hx);
                GoblinWalk.SampleLeftUpLeg(phase, out Vector3 luy, out Vector3 lux);
                GoblinWalk.SampleLeftLeg(phase, out Vector3 lly, out Vector3 llx);
                GoblinWalk.SampleRightUpLeg(phase, out Vector3 ruy, out Vector3 rux);
                GoblinWalk.SampleRightLeg(phase, out Vector3 rly, out Vector3 rlx);
                GoblinWalk.SampleLeftFoot(phase, out Vector3 lfy, out Vector3 lfx);
                GoblinWalk.SampleRightFoot(phase, out Vector3 rfy, out Vector3 rfx);

                mBlendAimFull.Invoke(rig, new object[] { hipsBone, hy, hx, 1f });
                mApplyLegChain.Invoke(rig, new object[] { leftUpLegBone, leftLegBone, leftFootBone, leftToeBone,
                    luy, lux, lly, llx, lfy, lfx, leftUpLegLen, leftLegLen, leftFootLen, 1f });
                mApplyLegChain.Invoke(rig, new object[] { rightUpLegBone, rightLegBone, rightFootBone, rightToeBone,
                    ruy, rux, rly, rlx, rfy, rfx, rightUpLegLen, rightLegLen, rightFootLen, 1f });

                Vector3 lf = root.InverseTransformPoint(leftFootBone.position);
                Vector3 rf = root.InverseTransformPoint(rightFootBone.position);
                Vector3 ll = root.InverseTransformPoint(leftLegBone.position);
                Vector3 rl = root.InverseTransformPoint(rightLegBone.position);

                sb.AppendLine($"{i},{phase:F2},{lf.x:F3},{lf.y:F3},{lf.z:F3},{rf.x:F3},{rf.y:F3},{rf.z:F3}," +
                    $"{ll.x:F3},{ll.y:F3},{ll.z:F3},{rl.x:F3},{rl.y:F3},{rl.z:F3}");
            }

            sb.AppendLine();
            sb.AppendLine("== STAGGER (leanRight=true), root-local coords ==");
            sb.AppendLine("i,phase,Lfoot.x,Lfoot.y,Lfoot.z,Rfoot.x,Rfoot.y,Rfoot.z");
            DumpStagger(sb, rig, T, F, mApplyBasePose, mApplyLegChain, mBlendAimFull,
                hipsBone, leftUpLegBone, leftLegBone, leftFootBone, leftToeBone,
                rightUpLegBone, rightLegBone, rightFootBone, rightToeBone,
                leftUpLegLen, leftLegLen, leftFootLen, rightUpLegLen, rightLegLen, rightFootLen,
                root, true);

            sb.AppendLine();
            sb.AppendLine("== STAGGER (leanRight=false), root-local coords ==");
            sb.AppendLine("i,phase,Lfoot.x,Lfoot.y,Lfoot.z,Rfoot.x,Rfoot.y,Rfoot.z");
            DumpStagger(sb, rig, T, F, mApplyBasePose, mApplyLegChain, mBlendAimFull,
                hipsBone, leftUpLegBone, leftLegBone, leftFootBone, leftToeBone,
                rightUpLegBone, rightLegBone, rightFootBone, rightToeBone,
                leftUpLegLen, leftLegLen, leftFootLen, rightUpLegLen, rightLegLen, rightFootLen,
                root, false);

            sb.AppendLine();
            sb.AppendLine("== BasePose-only (idle) foot reference, root-local ==");
            mApplyBasePose.Invoke(rig, null);
            Vector3 idleLf = root.InverseTransformPoint(leftFootBone.position);
            Vector3 idleRf = root.InverseTransformPoint(rightFootBone.position);
            sb.AppendLine($"Lfoot={idleLf} Rfoot={idleRf}");

            File.WriteAllText(OutPath, sb.ToString());
        }
        catch (System.Exception e)
        {
            sb.AppendLine("EXCEPTION: " + e);
            File.WriteAllText(OutPath, sb.ToString());
        }
    }

    static void DumpStagger(StringBuilder sb, GoblinCarryRig rig, System.Type T, BindingFlags F,
        MethodInfo mApplyBasePose, MethodInfo mApplyLegChain, MethodInfo mBlendAimFull,
        Transform hipsBone, Transform leftUpLegBone, Transform leftLegBone, Transform leftFootBone, Transform leftToeBone,
        Transform rightUpLegBone, Transform rightLegBone, Transform rightFootBone, Transform rightToeBone,
        float leftUpLegLen, float leftLegLen, float leftFootLen, float rightUpLegLen, float rightLegLen, float rightFootLen,
        Transform root, bool leanRight)
    {
        for (int i = 0; i <= 60; i += 3)
        {
            float phase = (i % 60) / 60f;

            mApplyBasePose.Invoke(rig, null);

            GoblinStagger.SampleHips(phase, leanRight, out Vector3 hy, out Vector3 hx);
            GoblinStagger.SampleLeftUpLeg(phase, out Vector3 luy, out Vector3 lux);
            GoblinStagger.SampleLeftLeg(phase, out Vector3 lly, out Vector3 llx);
            GoblinStagger.SampleRightUpLeg(phase, out Vector3 ruy, out Vector3 rux);
            GoblinStagger.SampleRightLeg(phase, out Vector3 rly, out Vector3 rlx);
            GoblinStagger.SampleLeftFoot(phase, out Vector3 lfy, out Vector3 lfx);
            GoblinStagger.SampleRightFoot(phase, out Vector3 rfy, out Vector3 rfx);

            mBlendAimFull.Invoke(rig, new object[] { hipsBone, hy, hx, 1f });
            mApplyLegChain.Invoke(rig, new object[] { leftUpLegBone, leftLegBone, leftFootBone, leftToeBone,
                luy, lux, lly, llx, lfy, lfx, leftUpLegLen, leftLegLen, leftFootLen, 1f });
            mApplyLegChain.Invoke(rig, new object[] { rightUpLegBone, rightLegBone, rightFootBone, rightToeBone,
                ruy, rux, rly, rlx, rfy, rfx, rightUpLegLen, rightLegLen, rightFootLen, 1f });

            Vector3 lf = root.InverseTransformPoint(leftFootBone.position);
            Vector3 rf = root.InverseTransformPoint(rightFootBone.position);

            sb.AppendLine($"{i},{phase:F2},{lf.x:F3},{lf.y:F3},{lf.z:F3},{rf.x:F3},{rf.y:F3},{rf.z:F3}");
        }
    }
}
