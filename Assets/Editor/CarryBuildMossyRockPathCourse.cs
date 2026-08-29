using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Replaces the old per-rock "Footholds" walking surface with the MossyRockPath module kit
// (CarrySetupMossyRockPath.cs / MossyPathAnalysis.cs), chaining pieces end-to-end into ONE
// continuous route from the StoneBridge exit to ZEnd, running independently down the RIVER'S OWN
// CENTER (not along either bank) so the goblin can never jump off the course onto dry land.
//
// v3 (2026-08-23, beam search rebuild): v2 picked greedily -- whichever single piece scored best
// RIGHT NOW -- and that repeatedly walked into traps a human planner would have seen coming: three
// pieces in a row that all turn the same way (their sole locally-safe direction) compounding into a
// large drift, or two opposite corrections that happened to each look good against the other and
// settled into an endless back-and-forth that never reached the far end. v3 instead runs a beam
// search over the next several pieces at once, keeping the best K partial routes at every depth and
// only committing to the single overall-lowest-total-penalty complete route once the search reaches
// the end of the course. This is what actually fixes the oscillation/drift failure modes: a route
// that loops in place keeps accumulating penalty every extra piece it takes, so it always loses to
// any route that reaches the end in fewer, better-centered pieces.
public static class CarryBuildMossyRockPathCourse
{
    const string ScenePath = "Assets/Scenes/ForestStage_Realistic.unity";
    const string PrefabDir = "Assets/Stage/Forest/Path/MossyRockPath/Prefabs/";
    const float ZEnd = 110f;
    const float BridgeSampleZ = 8f; // only used to pick WHICH deck collider segment (nearest this X) to read the real connection point from
    const float WaterClearance = 2.0f; // walkable-surface height above the water surface -- "natural clearance," not a high floating platform
    const float BankSafetyMargin = 2.2f; // required clearance between the course's own EDGE (not centerline) and the river's bank edge -- goblin's max jump distance is ~1.8m (runSpeed 3 x 0.6s airtime), so this is still a real margin above it. (A 3.6m margin was tried first but combined with piece half-widths up to ~3-4.5m and the river's own ~9-10m half-width, it demanded the route stay within about +/-2m of centerline at all times -- tighter than these piece shapes can actually hold against the river's natural meander -- so every route the search found still had some bins over the limit.)
    const float TrimTiltDeg = 9f; // small whole-rigid-piece tilt (never a per-joint step) used once, right after the bridge, to bring the course down from the elevated deck to just above the water; capped low so it reads as "very gentle," never a slope module

    static readonly string[] NormalPieces = { "GentleStraight", "LongCurve", "WideCurve", "GentleCurve_A" };
    // Measured net yaw turn per piece: GentleStraight ~-35deg, LongCurve ~-11deg, WideCurve ~+91deg,
    // GentleCurve_A ~-118deg. Three of the five pieces turn LEFT and only WideCurve turns
    // meaningfully RIGHT -- with GentleCurve_A excluded, a long enough stretch of required
    // rightward correction had no fine-grained tool, forcing repeated WideCurve use (and its own
    // 10-25m lateral swing) or accepting the leftward drift. GentleCurve_A is included so both
    // strong-correction directions are available; both it and WideCurve carry a heavy malus so
    // they're only used when genuinely needed, not as a default filler.
    const int NarrowLinkMinIndex = 2; // never in the first two pieces
    const int MaxNarrowLinkUses = 2;
    const int MinGapBetweenNarrowLinks = 2; // at least this many other pieces between two NarrowLink uses
    const int BeamWidth = 600;
    const int MaxDepth = 24; // pieces after the fixed descent piece -- raised from 10 so the search can use more, gentler pieces (smaller individual corrections) instead of being forced into a big risky correction when a shorter route runs out of room

    public static string LastLog;

    class PieceAsset
    {
        public string name;
        public GameObject prefab;
        public MossyPathAnalysis.Profile profile;
    }

    class Placement
    {
        public string name;
        public Vector3 pos;
        public Quaternion rot;
        public bool mirrored;
    }

    // Mirrors a piece's LOCAL-space geometry across its own X axis (the direction perpendicular to
    // travel) -- paired with instantiating the prefab with localScale.x = -1, this gives every piece
    // a same-shape opposite-handed twin (confirmed to render correctly: Unity's renderer handles the
    // odd negative-scale determinant automatically, no inside-out/culling artifacts). Only 1 of the
    // 5 pieces (WideCurve, +91deg) turns meaningfully rightward and only 1 (GentleCurve_A, -118deg)
    // turns sharply leftward; without mirroring, a long enough required correction in the "wrong"
    // direction for the available pieces had no real tool, which is what previously forced the route
    // into the water's edge. With mirroring, every piece is available in both turn directions.
    static Vector3 MirrorLocal(Vector3 v, bool mirrored) => mirrored ? new Vector3(-v.x, v.y, v.z) : v;
    static Vector2 MirrorLocal(Vector2 v, bool mirrored) => mirrored ? new Vector2(-v.x, v.y) : v;

    class BeamState
    {
        public Vector3 pos;
        public Vector3 fwd;
        public List<Placement> history;
        public float cumPenalty;
        public int narrowCount;
        public int lastNarrowAt;
        public string lastName;
        public bool done;
    }

    [MenuItem("Carry/Build Mossy Rock Path Course")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainRoot = GameObject.Find("ForestStage_Terrain");

            var oldFoothold = terrainRoot.transform.Find("Footholds");
            int removedCount = oldFoothold != null ? oldFoothold.childCount : 0;
            if (oldFoothold != null) Object.DestroyImmediate(oldFoothold.gameObject);
            var oldCourseRoot = terrainRoot.transform.Find("MossyRockPath_Course");
            if (oldCourseRoot != null) Object.DestroyImmediate(oldCourseRoot.gameObject);

            var courseRoot = new GameObject("MossyRockPath_Course");
            courseRoot.transform.SetParent(terrainRoot.transform, false);

            var t = typeof(CarryBuildTerrainForest);
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            var riverXM = t.GetMethod("RiverX", flags);
            var riverHalfWidthM = t.GetMethod("RiverHalfWidth", flags);
            var waterYAtM = t.GetMethod("WaterYAt", flags);
            float RiverX(float z) => (float)riverXM.Invoke(null, new object[] { z });
            float RiverHalfWidth(float z) => (float)riverHalfWidthM.Invoke(null, new object[] { z });
            float WaterY(float z) => (float)waterYAtM.Invoke(null, new object[] { z });

            var assets = new Dictionary<string, PieceAsset>();
            foreach (var nm in new[] { "GentleStraight", "GentleCurve_A", "WideCurve", "LongCurve", "NarrowLink" })
            {
                string fullName = "MossyRockPath_" + nm;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "PF_" + fullName + ".prefab");
                if (prefab == null) { log.AppendLine("MISSING PREFAB: " + fullName); continue; }
                var mf = prefab.GetComponentInChildren<MeshFilter>();
                assets[nm] = new PieceAsset { name = nm, prefab = prefab, profile = MossyPathAnalysis.Analyze(mf.sharedMesh, nm == "NarrowLink") };
            }

            // Read the StoneBridge's REAL walkable-deck collider geometry instead of the analytical
            // BridgeDeckWorldYAt/RiverX formulas -- those are close but not exact (previously left a
            // ~0.96m Z gap and ~0.13m height mismatch between the deck's actual far edge and where the
            // course started, which is exactly the "step the goblin falls through / potion spills at
            // the start" bug reported). The deck is built from a row of WalkableColliderSeg_N
            // BoxColliders, one per X-slice, all sharing the same Z-range; the true connection point is
            // the top-far-corner of whichever segment contains the target X.
            float bridgeXTarget = RiverX(BridgeSampleZ);
            var bridgeWalkable = GameObject.Find("ForestStage_Terrain/StoneBridge_Meshy/WalkableCollider");
            BoxCollider connSeg = null; float connSegDist = float.MaxValue;
            foreach (Transform seg in bridgeWalkable.transform)
            {
                var bc = seg.GetComponent<BoxCollider>();
                if (bc == null) continue;
                if (bridgeXTarget >= bc.bounds.min.x && bridgeXTarget <= bc.bounds.max.x) { connSeg = bc; break; }
                float d = Mathf.Min(Mathf.Abs(bc.bounds.min.x - bridgeXTarget), Mathf.Abs(bc.bounds.max.x - bridgeXTarget));
                if (d < connSegDist) { connSegDist = d; connSeg = bc; }
            }
            float bridgeX = Mathf.Clamp(bridgeXTarget, connSeg.bounds.min.x, connSeg.bounds.max.x);
            float bridgeDeckYAtConnection = connSeg.bounds.max.y;
            float zStart = connSeg.bounds.max.z; // the deck's actual far edge -- no gap, no overlap

            Vector3 startPos = new Vector3(bridgeX, bridgeDeckYAtConnection, zStart);
            float initLookZ = zStart + 10f;
            Vector3 startFwd = new Vector3(RiverX(initLookZ) - startPos.x, 0f, initLookZ - startPos.z).normalized;

            var initialHistory = new List<Placement>();

            // ---- Deterministic descent: StoneBridge's deck sits well above the water here; the rest
            // of the course needs to run just above the water surface, not at deck height. That
            // one-off drop is a single dedicated tilted LongCurve (small ~11deg own yaw turn, so it
            // leaves little heading drift to correct afterward, and it's long enough that one tilted
            // piece covers the whole gap) -- kept OUTSIDE the beam search entirely so tilt can never
            // interact with the search's own decisions.
            Vector3 curPos = startPos, curFwd = startFwd;
            {
                float err = curPos.y - (WaterY(curPos.z) + WaterClearance);
                if (err > 0.6f)
                {
                    // Try both handedness AND both GentleStraight/LongCurve for the descent piece, and
                    // keep whichever combination leaves the LEAST bank-margin violation across its own
                    // footprint -- the descent piece is placed before the beam search even starts, so
                    // if it's badly off-center every downstream choice inherits that handicap (this was
                    // the single worst-offending piece before mirroring was added: -2.08m edge
                    // clearance, improved to -1.52m with LongCurve+mirror-choice alone).
                    float bestScore = float.MaxValue; Quaternion bestRot = Quaternion.identity; Vector3 bestExit = Vector3.zero; Vector3 bestFwd = Vector3.zero; bool bestMirrored = false; string bestName = "LongCurve";
                    foreach (var descName in new[] { "LongCurve", "GentleStraight" })
                    {
                        var profile = assets[descName].profile;
                        foreach (bool mir in new[] { false, true })
                        {
                            Vector2 entryDirM = MirrorLocal(profile.EntryDirXZ, mir);
                            Vector3 entryDirLocal3 = new Vector3(entryDirM.x, 0f, entryDirM.y);
                            var rotBase = Quaternion.FromToRotation(entryDirLocal3, curFwd);
                            Vector3 exitLocalM = MirrorLocal(profile.ExitLocalPos, mir);
                            Vector3 pitchAxis = rotBase * Vector3.right;
                            var rotPlus = Quaternion.AngleAxis(TrimTiltDeg, pitchAxis) * rotBase;
                            var rotMinus = Quaternion.AngleAxis(-TrimTiltDeg, pitchAxis) * rotBase;
                            var rot = ((rotMinus * exitLocalM).y < (rotPlus * exitLocalM).y) ? rotMinus : rotPlus;
                            Vector3 exitWorld = curPos + rot * exitLocalM;
                            Vector2 exitDirMV = MirrorLocal(profile.ExitDirXZ, mir);
                            Vector3 exitDirWorld = (rot * new Vector3(exitDirMV.x, 0f, exitDirMV.y)).normalized;
                            float worstOver = 0f;
                            foreach (var bin in profile.Bins)
                            {
                                Vector3 wp = curPos + rot * MirrorLocal(bin.Center, mir);
                                float hw = RiverHalfWidth(wp.z);
                                float dist = Mathf.Abs(wp.x - RiverX(wp.z));
                                worstOver = Mathf.Max(worstOver, dist + bin.Width * 0.5f - (hw - BankSafetyMargin));
                            }
                            if (worstOver < bestScore) { bestScore = worstOver; bestRot = rot; bestExit = exitWorld; bestFwd = exitDirWorld; bestMirrored = mir; bestName = descName; }
                        }
                    }
                    initialHistory.Add(new Placement { name = bestName, pos = curPos, rot = bestRot, mirrored = bestMirrored });
                    curPos = bestExit;
                    curFwd = new Vector3(bestFwd.x, 0f, bestFwd.z).normalized;
                }
            }

            // ---- Step penalty for ONE candidate piece placed at (pos, fwd). Lower is better. Bank
            // safety is evaluated across the piece's WHOLE footprint (every analyzed cross-section
            // bin), not just its entry/exit, so a piece that bulges wide mid-length can't hide it.
            // Also returns maxOver -- the worst single-bin margin violation -- so the caller can treat
            // bank safety as a near-hard constraint (reject candidates that violate it at all, unless
            // truly nothing else is available) rather than just a heavily-weighted soft preference.
            (float penalty, float maxOver) StepPenalty(string nm, bool mirrored, Vector3 pos, Vector3 fwd, Vector3 exitWorld, Vector3 exitDirWorld, MossyPathAnalysis.Profile profile, Quaternion rot, string prevName)
            {
                float lookZ = pos.z + 18f;
                Vector3 desiredFwd = new Vector3(RiverX(lookZ) - pos.x, 0f, lookZ - pos.z).normalized;
                float penalty = 0f;
                float maxOver = 0f;
                foreach (var bin in profile.Bins)
                {
                    Vector3 worldPt = pos + rot * MirrorLocal(bin.Center, mirrored);
                    float hw = RiverHalfWidth(worldPt.z);
                    float distFromCenter = worldPt.x - RiverX(worldPt.z);
                    float safeLimit = hw - BankSafetyMargin - bin.Width * 0.5f; // no floor: a wide piece bin at a point this narrow genuinely doesn't fit, and the search needs to see that as a real (if survivable) penalty, not something silently clamped to "fine"
                    float over = Mathf.Max(0f, Mathf.Abs(distFromCenter) - safeLimit);
                    maxOver = Mathf.Max(maxOver, over);
                    penalty += over * over * 40f; // steep -- this is close to a hard constraint given "never touch the bank"
                }
                float exitDist = Mathf.Abs(exitWorld.x - RiverX(exitWorld.z));
                penalty += exitDist * exitDist * 3f; // centering pull -- keeps the WHOLE route hugging the river's own centerline, not just avoiding the banks
                float headingMiss = 1f - Vector3.Dot(exitDirWorld, desiredFwd);
                penalty += headingMiss * 50f;
                if (nm == prevName) penalty += (nm == "WideCurve" || nm == "GentleCurve_A" ? 90f : 15f); // anti-repetition -- WideCurve/GentleCurve_A twice in a row means ~180-240deg combined turn
                if (nm == "GentleStraight") penalty += 10f;
                else if (nm == "WideCurve") penalty += 60f;
                else if (nm == "GentleCurve_A") penalty += 20f; // lowered from 60f (2026-08-29) -- at parity with WideCurve it never won against chains of free-malus LongCurve/GentleStraight corrections, so it was never once selected; still above GentleStraight's 10f since it's a much bigger single commitment (~-118deg)
                return (penalty, maxOver);
            }

            // ---- Beam search over the remaining pieces ----
            var beam = new List<BeamState> { new BeamState { pos = curPos, fwd = curFwd, history = initialHistory, cumPenalty = 0f, narrowCount = 0, lastNarrowAt = -99, lastName = initialHistory.Count > 0 ? "LongCurve" : null, done = curPos.z >= ZEnd - 3f } };

            for (int depth = 0; depth < MaxDepth; depth++)
            {
                if (beam.All(s => s.done)) break;
                var next = new List<BeamState>();
                foreach (var state in beam)
                {
                    if (state.done) { next.Add(state); continue; }

                    bool narrowAllowed = state.history.Count >= NarrowLinkMinIndex
                        && state.narrowCount < MaxNarrowLinkUses
                        && (state.narrowCount == 0 || state.history.Count - state.lastNarrowAt > MinGapBetweenNarrowLinks);
                    bool narrowEager = narrowAllowed && (state.history.Count - state.narrowCount) >= 2;

                    var candidates = new List<(string nm, bool mirrored)>();
                    foreach (var nm in NormalPieces) { candidates.Add((nm, false)); candidates.Add((nm, true)); }
                    if (narrowAllowed) { candidates.Add(("NarrowLink", false)); candidates.Add(("NarrowLink", true)); }

                    // Collect every viable candidate first, then apply a near-HARD bank-safety filter:
                    // drop any candidate whose worst bin overshoots the margin by more than a small
                    // tolerance, UNLESS every single candidate at this state would be dropped (in which
                    // case keep them all rather than dead-end the search) -- previous tuning found that
                    // pure soft-penalty scoring, even steeply weighted, still let the search settle for
                    // "least bad of several bank violations" instead of finding the zero-violation
                    // routes that (with MaxDepth raised to allow more, gentler pieces, AND mirroring for
                    // both turn directions) do exist.
                    const float HardOverTolerance = 0.02f;
                    var thisStepCandidates = new List<(string nm, bool mirrored, Quaternion rot, Vector3 exitWorld, Vector3 exitDirWorld, float penalty, float maxOver)>();
                    foreach (var (nm, mirrored) in candidates)
                    {
                        var asset = assets[nm];
                        var profile = asset.profile;
                        Vector3 entryDirLocal3 = new Vector3(MirrorLocal(profile.EntryDirXZ, mirrored).x, 0f, MirrorLocal(profile.EntryDirXZ, mirrored).y);
                        var rot = Quaternion.FromToRotation(entryDirLocal3, state.fwd);
                        Vector3 exitLocalM = MirrorLocal(profile.ExitLocalPos, mirrored);
                        Vector3 exitWorld = state.pos + rot * exitLocalM;
                        Vector2 exitDirM = MirrorLocal(profile.ExitDirXZ, mirrored);
                        Vector3 exitDirWorld = (rot * new Vector3(exitDirM.x, 0f, exitDirM.y)).normalized;
                        if (exitDirWorld.z < 0.15f) continue; // hard rule: never end up facing backward/sideways

                        var (step, maxOver) = StepPenalty(nm, mirrored, state.pos, state.fwd, exitWorld, exitDirWorld, profile, rot, state.lastName);
                        if (nm == "NarrowLink" && narrowEager) step -= 80f; // strong nudge once overdue -- NarrowLink is the required gameplay accent (section 20) and, being one of the straightest pieces (~9deg own turn), rarely wins purely on geometry against the malus-free LongCurve otherwise
                        thisStepCandidates.Add((nm, mirrored, rot, exitWorld, exitDirWorld, step, maxOver));
                    }
                    float bestMaxOverHere = thisStepCandidates.Count > 0 ? thisStepCandidates.Min(c => c.maxOver) : 0f;
                    bool anySafe = bestMaxOverHere <= HardOverTolerance;

                    foreach (var c in thisStepCandidates)
                    {
                        if (anySafe && c.maxOver > HardOverTolerance) continue; // hard-rejected: a safe alternative exists at this step, so don't even consider this one

                        var hist = new List<Placement>(state.history) { new Placement { name = c.nm, pos = state.pos, rot = c.rot, mirrored = c.mirrored } };
                        next.Add(new BeamState
                        {
                            pos = c.exitWorld,
                            fwd = new Vector3(c.exitDirWorld.x, 0f, c.exitDirWorld.z).normalized,
                            history = hist,
                            cumPenalty = state.cumPenalty + c.penalty,
                            narrowCount = state.narrowCount + (c.nm == "NarrowLink" ? 1 : 0),
                            lastNarrowAt = c.nm == "NarrowLink" ? state.history.Count : state.lastNarrowAt,
                            lastName = c.nm,
                            done = c.exitWorld.z >= ZEnd - 3f,
                        });
                    }
                }
                beam = next.OrderBy(s => s.cumPenalty).Take(BeamWidth).ToList();
            }

            // Prefer a route that actually finished; among finished routes (or, failing that, all
            // routes) take the lowest cumulative penalty.
            var finished = beam.Where(s => s.done).ToList();
            var winner = (finished.Count > 0 ? finished : beam).OrderBy(s => s.cumPenalty).First();

            var placed = new List<(string name, GameObject go, MossyPathAnalysis.Profile profile, Quaternion rot, bool mirrored)>();
            foreach (var p in winner.history)
            {
                var asset = assets[p.name];
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset.prefab, courseRoot.transform);
                inst.name = "MossyRockPath_" + p.name + (p.mirrored ? "_Mirrored" : "") + "_" + placed.Count;
                inst.transform.position = p.pos;
                inst.transform.rotation = p.rot;
                if (p.mirrored) inst.transform.localScale = new Vector3(-1f, 1f, 1f);
                placed.Add((p.name, inst, asset.profile, p.rot, p.mirrored));
            }

            // ---- validation: joint gaps/kinks, water clearance, bank clearance ----
            int gapWarnings = 0, kinkWarnings = 0, waterWarnings = 0, bankWarnings = 0;
            float worstBank = 999f, worstWater = 999f;
            for (int i = 0; i < placed.Count; i++)
            {
                var (nm, go, profile, rot, mirrored) = placed[i];
                foreach (var bin in profile.Bins)
                {
                    Vector3 worldPt = go.transform.TransformPoint(bin.Center); // TransformPoint honors localScale, so mirrored pieces (scale.x=-1) come out correctly without re-deriving the mirror math here
                    if (worldPt.z > 118f) continue; // past RiverZ1 (120) the river formulas degrade to 0 by design (safe far-forest end) -- not a real water/bank condition to validate
                    float wy = WaterY(worldPt.z);
                    worstWater = Mathf.Min(worstWater, worldPt.y - wy);
                    if (worldPt.y < wy + 0.15f) { waterWarnings++; log.AppendLine($"WARNING: {go.name} bin near/under water (surfaceY={worldPt.y:F2} waterY={wy:F2}) at z={worldPt.z:F1}"); }
                    float hw = RiverHalfWidth(worldPt.z);
                    float dist = Mathf.Abs(worldPt.x - RiverX(worldPt.z));
                    float edgeClear = hw - dist - bin.Width * 0.5f; // from the piece's own EDGE, not its centerline -- what actually matters for "can the player standing at the course edge jump to the bank"
                    worstBank = Mathf.Min(worstBank, edgeClear);
                    if (edgeClear < BankSafetyMargin) { bankWarnings++; log.AppendLine($"WARNING: {go.name} within bank margin (edgeClearance={edgeClear:F2}m) at z={worldPt.z:F1}"); }
                }
                if (i + 1 < placed.Count)
                {
                    var (nm2, go2, profile2, rot2, mirrored2) = placed[i + 1];
                    Vector3 exitW = go.transform.TransformPoint(profile.ExitLocalPos);
                    float gap = Vector3.Distance(exitW, go2.transform.position);
                    if (gap > 0.05f) { gapWarnings++; log.AppendLine($"WARNING: gap {gap:F3}m between {go.name} and {go2.name}"); }
                    // NOTE: Transform.TransformDirection ignores scale entirely (Unity docs), so it
                    // would silently give the UN-mirrored direction for a mirrored piece -- mirror the
                    // local vector by hand first, then rotate (matching how the beam search itself
                    // computes exit/entry directions).
                    Vector2 exitDirM = MirrorLocal(profile.ExitDirXZ, mirrored);
                    Vector2 entryDirM2 = MirrorLocal(profile2.EntryDirXZ, mirrored2);
                    Vector3 exitDirW = go.transform.rotation * new Vector3(exitDirM.x, 0, exitDirM.y);
                    Vector3 nextEntryDirW = go2.transform.rotation * new Vector3(entryDirM2.x, 0, entryDirM2.y);
                    float kink = Vector3.Angle(exitDirW, nextEntryDirW);
                    if (kink > 3f) { kinkWarnings++; log.AppendLine($"WARNING: kink {kink:F1} deg between {go.name} and {go2.name}"); }
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            log.AppendLine($"Removed old Footholds ({removedCount} children).");
            log.AppendLine($"Beam search: {beam.Count} states survived to depth cutoff, winner finished={winner.done}, cumPenalty={winner.cumPenalty:F1}");
            log.AppendLine($"Placed {placed.Count} pieces. gapWarnings={gapWarnings} kinkWarnings={kinkWarnings} waterWarnings={waterWarnings} bankWarnings={bankWarnings}");
            log.AppendLine($"worstBankClearance={worstBank:F2}m worstWaterClearance={worstWater:F2}m");
            log.AppendLine("Sequence: " + string.Join(" -> ", placed.Select(p => p.name)));
            log.AppendLine($"final cursor z={winner.pos.z:F1} x={winner.pos.x:F1}");
            log.AppendLine("SUCCESS");
        }
        catch (System.Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        LastLog = log.ToString();
        Debug.Log(LastLog);
    }
}
