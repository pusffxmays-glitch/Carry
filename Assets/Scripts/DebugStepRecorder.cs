using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// 調査用 (2026-08-16): 小さい段差・隙間を踏んだときの壺の動きと流出量の対応を
// 毎フレーム記録する。MCP の execute_code ポーリングはヒッチで dt を荒らし
// 計測自体がこぼれを増やす (WORKLOG 追補 10) ため、記録はゲーム内で行い
// 終了後にまとめて読み出す。
public class DebugStepRecorder : MonoBehaviour
{
    public static DebugStepRecorder Active;

    struct Row { public float t, gy, gz, py, vy, fill, tilt; }
    readonly List<Row> rows = new List<Row>(8192);

    Transform goblin, pot;
    FluidBoundary boundary;
    FluidCore core;
    public bool recording;

    void Awake()
    {
        Active = this;
        var loco = FindObjectOfType<GoblinLocomotion>();
        goblin = loco != null ? loco.transform : null;
        core = FindObjectOfType<FluidCore>();
        boundary = FindObjectOfType<FluidBoundary>();
        pot = goblin != null ? goblin.Find("Carry_Pot") : null;
    }

    // MCP の execute_code は呼び出しごとに 0.1-0.3 秒のヒッチを起こし、ジャンプ入力を
    // その瞬間に注入すると dt スパイクで 1 フレーム 2m 上昇する「超ジャンプ」になって
    // 計測を壊す。Space 入力はゲーム内でスケジュールしてヒッチなしに注入する。
    // 複数登録可 (例: 離陸ジャンプ + 着地クッションの 2 タップ)。
    readonly List<float> tapAt = new List<float>();
    readonly List<bool> tapShift = new List<bool>();
    bool tapPressed;
    public void ScheduleJump(float delay, bool withShift = false)
    {
        tapAt.Add(Time.time + delay);
        tapShift.Add(withShift);
    }

    void LateUpdate()
    {
        if (tapAt.Count > 0 && Time.time >= tapAt[0] && Keyboard.current != null)
        {
            bool shift = tapShift[0];
            if (!tapPressed)
            {
                InputSystem.QueueStateEvent(Keyboard.current, shift
                    ? new KeyboardState(Key.W, Key.LeftShift, Key.Space)
                    : new KeyboardState(Key.W, Key.Space));
                tapPressed = true;
            }
            else
            {
                InputSystem.QueueStateEvent(Keyboard.current, shift
                    ? new KeyboardState(Key.W, Key.LeftShift)
                    : new KeyboardState(Key.W));
                tapPressed = false;
                tapAt.RemoveAt(0);
                tapShift.RemoveAt(0);
            }
        }

        if (!recording || goblin == null) return;
        rows.Add(new Row
        {
            t = Time.time,
            gy = goblin.position.y,
            gz = goblin.position.z,
            py = pot != null ? pot.position.y : 0f,
            vy = boundary != null ? boundary.LinearVelocity.y : 0f,
            fill = core != null ? core.FillFraction01 * 100f : 0f,
            tilt = pot != null ? Vector3.Angle(pot.up, Vector3.up) : 0f,
        });
    }

    public int Count => rows.Count;
    public void Clear() => rows.Clear();

    // stride 行おきに間引いて読み出す。1 回の返答に収まるよう範囲指定つき。
    public string DumpRange(int start, int count, int stride = 1)
    {
        var sb = new StringBuilder();
        int end = Mathf.Min(rows.Count, start + count);
        for (int i = start; i < end; i += stride)
        {
            var r = rows[i];
            sb.Append(r.t.ToString("F3")).Append(" z=").Append(r.gz.ToString("F2"))
              .Append(" gy=").Append(r.gy.ToString("F3")).Append(" py=").Append(r.py.ToString("F3"))
              .Append(" vy=").Append(r.vy.ToString("F2")).Append(" tilt=").Append(r.tilt.ToString("F1"))
              .Append(" fill=").Append(r.fill.ToString("F1"))
              .AppendLine();
        }
        return sb.ToString();
    }

    // 要約: 縦速度スパイク (|vy| がしきい値超え) と、その前後の fill 変化だけを抜き出す。
    public string DumpSpikes(float vyThreshold = 1.5f)
    {
        var sb = new StringBuilder();
        for (int i = 1; i < rows.Count; i++)
        {
            if (Mathf.Abs(rows[i].vy) < vyThreshold) continue;
            int after = Mathf.Min(rows.Count - 1, i + 30);   // 約 0.5 秒後
            var r = rows[i];
            sb.Append(r.t.ToString("F3")).Append(" z=").Append(r.gz.ToString("F2"))
              .Append(" vy=").Append(r.vy.ToString("F2"))
              .Append(" fill ").Append(rows[i - 1].fill.ToString("F1"))
              .Append(" -> ").Append(rows[after].fill.ToString("F1")).AppendLine();
        }
        return sb.ToString();
    }
}
