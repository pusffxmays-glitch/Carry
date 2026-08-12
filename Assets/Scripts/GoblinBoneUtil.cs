using UnityEngine;

// Shared helper for the Generic-rig goblin skeleton: Unity's Humanoid Avatar / OnAnimatorIK
// is not available (the FBX is imported as Generic, see CarrySetupJump.cs), so every system
// that needs a bone (arm IK, pot anchor, wobble) finds it by name through the transform tree.
public static class GoblinBoneUtil
{
    public static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
