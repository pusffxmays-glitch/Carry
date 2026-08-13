using System.Collections.Generic;
using UnityEngine;

// A grabbable spot along the river (rock / log / root) that lets a swept
// player stop early and rejoin the stage close to where they fell, instead
// of being carried all the way back to the last checkpoint.
public class RecoveryPoint : MonoBehaviour
{
    public static readonly List<RecoveryPoint> All = new List<RecoveryPoint>();

    public Vector3 standOffset = new Vector3(0f, 0.5f, 0f);

    public Vector3 StandPosition => transform.position + standOffset;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);
}
