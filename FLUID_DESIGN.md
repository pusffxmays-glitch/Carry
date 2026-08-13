# ポーション液体システム 設計書

対象仕様: 「ポーション液体システム 完全自作・最終仕様」(§1〜§47)
初版: 2026-08-13 / Phase 0 調査後、実装前
改訂: 2026-08-13 設計レビュー反映（修正1〜15）— **コード未着手**

維持する設計思想:
`World Space Fluid → PBF → Boundary Particles → 3D Density Field → GPU Marching Cubes → 同一SurfaceによるOverflow`

---

## Phase 0 調査結果

| 項目 | 実測値 | 設計への影響 |
|---|---|---|
| Unity / URP | 6000.5.4f1 / URP 17.5.0 | RenderGraph 必須。`AddUnsafePass` を使用 |
| GPU | AMD Radeon RX 9060 XT / VRAM 16189MB | Marching Cubes を高解像度で回せる。§12 第一候補を採用可能 |
| Compute / AsyncCompute / 3D RT / RandomWrite3D | すべて対応 | Density Field を 3D テクスチャで持てる |
| `Physics.gravity` | (0, −9.81, 0) | §19 の World Gravity 基準はこれ |
| `Time.fixedDeltaTime` | 0.02 | Fluid は独自固定ステップで駆動 |
| Carry_Pot | `Goblin` の子 / localScale 2.366 / Collider 無し | 壺は「移動する境界」。Collider は使わず実測プロファイルで境界を作る (§21) |
| 壺内部 (実測) | 床 y=0.0456, リム y=0.3601, 内径 0.153→0.232(腹)→0.195(リム), 容積 0.0431 local³ = **0.571 m³** | 腹が広く底が細い樽形。**単純円柱判定では内外を判定できない**（修正4の根拠） |
| GoblinLocomotion | CharacterController。`CurrentSpeed`/`IsMoving`/`IsRunning`/`TurnInputThisFrame`/`isGrounded` 公開。**locomotion gravity = −20** | §18 の入力源。Fluid は §19 に従い Physics.gravity を使う |
| GoblinCarryRig | LateUpdate で Head ボーンから壺姿勢を確定 | Fluid の更新順序は CarryRig の後 |
| Rigidbody | シーンに 0 個 | 壺の速度・角速度は Transform 差分から実測する |

### 既存資産の扱い

| 資産 | 判断 |
|---|---|
| `PotInteriorProfile.cs` | **流用・拡張**（修正4: Interior Test / SDF を追加） |
| `FluidSim.compute` | 土台として流用、**境界処理を全面再設計** |
| `PotionFluid.cs` | 流用（Moving Boundary / Mass 会計 / 分類を追加）。**疑似重力項は削除**（修正1） |
| Screen-Space Surface 4パス + `FluidSurfaceFeature` | **破棄**。§11 が 3D Density Field を必須としており非準拠 |
| `FluidTestRig.cs` | 拡張（§39 TEST A〜O） |

---

## 1. System Architecture

```
GoblinLocomotion / GoblinCarryRig            (Phase 11)
        │  pot transform (前フレーム / 現フレーム)
        ▼
PotionFluid (MonoBehaviour, CarryRig の後)
        │  壺の linear/angular velocity を実測
        │  CFL によるサブステップ数決定
        ▼
PotBoundary (Boundary Particle 生成・更新)     ★修正2
        │  BoundaryPosition / Velocity / Normal / Volume
        ▼
FluidSim.compute            ── Phase 1,4,5,6,7,8,9
        │  Particle Position / Velocity / Mass / Region
        ▼
PotInteriorTest (領域判定・Overflow/貫通判定)   ★修正4,5
        │  Region 遷移 → Overflow / Penetration
        ▼
DensityAccumulate.compute (uint atomic)        ★修正3
        │  RWStructuredBuffer<uint>  固定小数
        ▼
DensityDecodeSmooth.compute
        │  RWTexture3D<float>  Visual Density Field
        ▼
MarchingCubes.compute                          ★修正9
        │  GraphicsBuffer<Vertex> + IndirectArgs + OverflowCounter
        ▼
PotionLiquid.shader (URP)  ── Phase 3
        │  DrawProceduralIndirect
        ▼
Game View
```

**一方向。** 下流が上流へ影響しない。見た目の都合で Particle 位置を動かす経路は存在しない。

---

## 2. World Space Fluid と Moving Boundary の整合性 ★修正1

### 二重計上の問題

初版の設計は
`EffectiveGravity = Physics.gravity − potAcceleration × sensitivity`
としていた。これは**誤り**である。

| 定式化 | 座標系 | 壁 | 慣性の表現 |
|---|---|---|---|
| 疑似重力 `−a_pot` | 壺の**非慣性系** | 静止 | 疑似力として与える |
| Moving Boundary | World の**慣性系** | 動く | 実際の運動量として伝わる |

この2つは**同じ物理現象の2つの異なる定式化**であり、どちらか一方だけが正しい。
World 空間で計算しながら疑似重力も足すと、壺の運動が Fluid に 2 回入る。

### 採用する定式化

**慣性系（World 空間）で計算する。** したがって:

- 外力は `Physics.gravity` **のみ**。`accelerationSensitivity` / `maxMeasuredAcceleration` は削除
- 壺の並進・回転による影響は **Moving Boundary が実際に運動量を伝える**ことで生じる

これにより §20 が要求する挙動は次のように**自然に**成立する:

| 現象 | 機構 |
|---|---|
| 急加速 → Fluid が遅れて動く | 後方の壁が Fluid へ進入 → 密度上昇 → 圧力で押される。Fluid 自身は慣性でその場に留まろうとする |
| 急停止 → Fluid が前方へ大きく動く | Fluid は World 空間の運動量を保持したまま、減速した前壁へ突っ込む |
| 方向転換 → 横へ寄る | 同上（横壁が進入する） |
| 着地 → 大きく揺れる | 底の壁が上方向へ急進入する |

**これは疑似力による近似ではなく、実際の慣性である。** §20 に対してより強い準拠になる。

### 補正項: Boundary Viscosity（No-Slip）★二重計上しない理由

Moving Boundary の密度結合だけでは伝達できない成分がある。

- 密度（圧力）結合は **法線方向**の運動量しか伝えない
- 実液体は**接線方向**にも壁に引きずられる（粘性によるせん断伝達）
- これが無いと、**壺が回転しても中身がほとんど回らない**（壁が滑るだけ）

したがって **Boundary Viscosity** を導入する:

```
v_i ← v_i + μ_b · Σ_b  ψ_b · W(|p_i − p_b|, h) · (V_boundary(b) − v_i)
```

**二重計上にならない理由**: 圧力結合が伝えるのは法線成分、境界粘性が伝えるのは接線成分であり、
両者は直交する独立な成分である。疑似重力のように「同じ成分を 2 回与える」関係にない。
`μ_b` は Inspector 調整（0 で完全スリップ、大で完全ノースリップ）。

---

## 3. Boundary Particle: 動的境界 ★修正2

Boundary Particle は壺形状に対して固定されるが、**World 空間では毎フレーム移動する**。

| フィールド | 内容 |
|---|---|
| `BoundaryPositionLocal` | 壺ローカル固定（生成時に確定、以後不変） |
| `BoundaryPosition` | World。毎フレーム `potLocalToWorld × local` |
| `BoundaryVelocity` | World。下式で毎フレーム算出 |
| `BoundaryNormal` | 壺ローカルの内向き法線を World へ回転 |
| `BoundaryVolume` (ψ) | Akinci の境界体積。生成時に近傍境界粒子密度から算出 |

```
V_boundary(b) = potLinearVelocity + cross(potAngularVelocity, BoundaryPosition(b) − potCenter)
```

- `potLinearVelocity` / `potAngularVelocity` は Transform 差分から実測（テレポート検出付き）
- PBF の境界処理は**静止境界を前提にしない**。密度計算に ψ で参加し、境界粘性で
  `V_boundary` を Fluid へ伝える

### サブステップ間の境界補間（実装時の破綻回避）

壺姿勢はフレーム境界でしか分からない。サブステップごとに壁が瞬間移動すると
エネルギーを注入して破綻するため、**前フレーム姿勢 → 現フレーム姿勢を
サブステップ数で補間**して各サブステップの境界姿勢とする。

### CFL によるサブステップ数

壁が 1 サブステップで粒子間隔の半分以上動くと Fluid を飲み込む。したがって

```
subSteps = clamp(ceil( max(|v_pot| + |ω_pot|·R_pot, v_fluid_max) · dt / (0.4 · particleSpacing) ), minSub, maxSub)
```

---

## 4. Fluid Data Structure

粒子 1 個あたり（SoA）:

| バッファ | 型 | 用途 |
|---|---|---|
| `Positions` | float3 | World 空間 |
| `PredictedPositions` | float3 | PBF 予測位置 |
| `Velocities` | float3 | 速度 |
| `Masses` | float | §30 Fluid Mass |
| `Lambdas` / `Densities` | float | PBF |
| `DeltaP` | float3 | 位置補正（反復内スクラッチ） |
| `RegionFlags` | uint | 0=Inside / 1=RimOpening / 2=Airborne / 3=Ground / 4=Retired |
| `PrevRegionFlags` | uint | 領域遷移検出用（修正4） |
| `Ages` | float | Ground 粒子の寿命 |

境界粒子は別バッファ（§3 の 5 フィールド）。積分しない。

**World 空間で持つ理由**: 壺ローカルで持つとリム通過時に座標系の乗り換えが必要になり、
それが §40「壺内 Liquid と Overflow を別物にする」の入口になる。World 空間なら乗り換えが存在しない。

---

## 5. GPU Buffer 一覧

| 名前 | 要素数 | 型 |
|---|---|---|
| Positions / PredictedPositions / Velocities / DeltaP | N | float3 |
| Masses / Lambdas / Densities / Ages | N | float |
| RegionFlags / PrevRegionFlags | N | uint |
| BoundaryPositionLocal / BoundaryPosition / BoundaryVelocity / BoundaryNormal | B | float3 |
| BoundaryVolume | B | float |
| CellKeys(sorted) / SortedIndices | N | uint |
| CellStart / CellEnd | C | uint |
| **DensityAccum** | Vx·Vy·Vz | **uint (固定小数)** ★修正3 |
| **DensityField** | Vx·Vy·Vz | RWTexture3D\<float\> R16F ★修正3 |
| BrickOccupancy | Bx·By·Bz | uint ★修正8 |
| MCVertices | MaxVertices | Vertex(pos+normal) ★修正9 |
| MCIndirectArgs / MCOverflowCounter | 4 / 1 | uint ★修正9 |
| MassCounters | 8 | uint (固定小数) |

初期値: **N = 16384**, B ≈ 6000, Voxel = **6mm 固定**（修正7/8）

---

## 6. Compute Shader Pass 一覧

§5 の 10 処理をカーネルへ割り付ける。

| # | カーネル | §5 | 内容 |
|---|---|---|---|
| 1 | `UpdateBoundary` | 9 | 境界の World 位置・速度・法線を更新（サブステップ補間） |
| 2 | `ClearGrid` | 6 | ハッシュ初期化 |
| 3 | `Integrate` | 1 | **World Gravity のみ** → 予測位置 |
| 4 | `ComputeCellKeys` | 2 | セルキー |
| 5 | `SortByCellKey` | 2 | GPU Bitonic Sort |
| 6 | `BuildCellRanges` | 6 | CellStart/End |
| 7 | `ComputeDensity` | 4 | 流体 + **境界粒子(ψ)** |
| 8 | `ComputeLambda` | 5 | λ |
| 9 | `ComputeDeltaP` | 5,7 | 位置補正 + Artificial Pressure |
| 10 | `ApplyDeltaP` | 7 | 補正適用（7→10 を反復） |
| 11 | `ComputeVelocity` | 10 | v = (p′−p)/dt |
| 12 | `ApplyViscosityAndTension` | 7,8 | XSPH + **Boundary Viscosity** + Akinci 表面張力 |
| 13 | `ResolveWorldCollision` | 9 | 地面・Sim Bounds（**壺は境界粒子が担当**） |
| 14 | `ClassifyRegionAndMass` | — | 領域判定・遷移検出・Mass 集計 |
| 15 | `ClearDensityAccum` | — | uint バッファクリア |
| 16 | `SplatDensityAtomic` | — | 粒子 → uint 固定小数 atomic 加算 |
| 17 | `DecodeAndSmoothDensity` | — | uint → float、3D 分離ガウシアン |
| 18 | `MarkBricks` | — | Brick Occupancy |
| 19 | `MarchingCubes` | — | 等値面 → 頂点 |

---

## 7. Spatial Hash

- セルサイズ = カーネル半径 h
- **カウンティングソート**（Phase 1 実装時に Bitonic から変更。理由は下記）
  - 初版の「一様グリッド＋アトミック挿入」はセルあたり粒子数に上限があり、圧縮時に近傍を
    取りこぼす。取りこぼしは密度過小→圧力不足→漏れに直結するため不採用（この判断は不変）
  - 設計上の要件は「セル毎の上限を作らない**完全ソート**であること」。Bitonic もカウンティング
    ソートもこれを満たし、結果は同一
  - Bitonic は N=16384 で log2(N)(log2(N)+1)/2 = **105 ディスパッチ**必要。サブステップ 8 で
    840 ディスパッチ/フレームとなり、ディスパッチ発行だけで数 ms を消費する
  - カウンティングソート（クリア→計数→3段スキャン→散布）は **7 ディスパッチ**
  - **品質は同一**（どちらも完全ソート・上限なし）。純粋に発行コストの問題
- `hash(cell) = (cx·73856093 ^ cy·19349663 ^ cz·83492791) % tableSize`
- 近傍探索は 3×3×3
- **境界粒子も同じハッシュに登録する**

---

## 8. PBF 計算フロー

```
UpdateBoundary (サブステップ補間)
  ↓
Integrate  (World Gravity のみ)
  ↓
Build spatial hash (fluid + boundary)
  ↓
repeat solverIterations:            ← Inspector 調整 (§7)
      ComputeDensity   (境界 ψ を含む)
      ComputeLambda    (C = ρ/ρ0 − 1、圧縮のみ)
      ComputeDeltaP    (+ Artificial Pressure)
      ApplyDeltaP      (1 反復の移動量に上限)
  ↓
ComputeVelocity
  ↓
XSPH粘性 + Boundary Viscosity + Surface Tension
  ↓
ResolveWorldCollision (地面 / Sim Bounds)
  ↓
ClassifyRegionAndMass
```

### Solver Under-Relaxation (SOR) ★Phase 1 実装で追加

位置補正に緩和係数を掛ける。

```
dp *= SolverRelaxation / RestDensity;      // SolverRelaxation = 0.12
```

**なぜ必要か（実測）**: これが無い（= 完全な Newton ステップ）と、補正が毎反復で行き過ぎ、
その位置差が `v = (p'-p)/dt` で速度に変換され、サブステップごとにエネルギーが注入される。
Phase 1 で反復数だけを変えて計測した結果:

| 反復数 | 液面 topY (目標 0.690) | 最大速度 |
|---|---|---|
| 0（圧力投影なし） | 0.425（完全静止） | 0.00 |
| 1 | 1.197 | 2.47 |
| 4 | 1.350（天井） | 7.17 |

**反復するほど吹き上がる**、つまり圧力投影自体がエネルギー源だった。SOR 導入後:

| SOR | topY | 重心 Y (目標 0.420) | 最大速度 | 平均 rho/rho0 |
|---|---|---|---|---|
| 1.00 | 1.350 | 0.572 | 8.00 | 0.803 |
| 0.30 | 1.174 | 0.429 | 1.86 | 0.975 |
| **0.15** | **0.724** | **0.422** | **0.57** | **0.993** |
| 0.05 | 0.709 | 0.415 | 0.39 | 1.009 |

既定値 0.12。lambda の分母下限 (`MinDenom`) と併用する。

なお表面張力・境界粘性・人工圧力・境界圧力を個別にゼロにしても膨張は止まらず、
発生源が圧力投影であることは切り分け済み。

---

## 9. Viscosity / Surface Tension

**Viscosity (§8)**: XSPH。水 < ポーション < シロップ。
上げすぎると団子化する (§8 末尾) ため上限を設け、表面の連続性は Artificial Pressure と
Surface Tension で担保する。

**Surface Tension (§9)**: Akinci (2013) の cohesion + curvature。
目的は粒子の球体化ではなく**表面を滑らかにして液体同士を連続させること**。
したがって cohesion 係数は控えめ、curvature 側を主とする。

---

## 10. Pot Interior Boundary（物理） ★修正5

### 前実装の失敗と原因

前回は「密度投影の各反復の**後**に位置をハードクランプする」方式だった。結果:
静止した壺から毎秒約 15% の粒子が底方向へ漏れ、速度が上限に張り付いた。

**原因**: 圧力ソルバーが壁へ押し込む → クランプが押し戻す → その位置差が
`v = (p′−p)/dt` で速度に化ける、という綱引き。壁がソルバーの「外」にいるのが構造的誤り。

### 新方式: Boundary Particles

壺の**内壁・底・外壁**に動かない粒子層を敷く。

- 生成: `PotInteriorProfile` の内径プロファイルから、高さ × 円周方向に
  間隔 ≒ 流体粒子間隔で配置。厚さ 2 層（カーネル半径を埋めるため）
- **密度計算に参加する**（Akinci の境界体積 ψ で重み付け。質量ではない）
- 積分しない。力を受けない。位置補正もされない
- 速度は §3 の `V_boundary`

壁が圧力ソルバーの**内側**の存在になるため、綱引きが原理的に発生しない。

### 役割分担（修正5）

| 機構 | 役割 |
|---|---|
| **Boundary Particle** | Fluid の**物理的な壁**。これだけが衝突を担当する |
| **Pot Interior Test / SDF** | **領域判定・Overflow判定・異常検出・Debug**。物理には介入しない |

Pot Interior Test だけで壁衝突を処理する方式には**戻さない**。

### 安全網 SafetyCorrection ★追加修正2

**SafetyCorrection は通常の壁衝突処理ではない。**
数値的不安定、または極端な一時的貫通を検出した場合の**最終安全装置**である。

| 処理 | 担当 |
|---|---|
| **通常時**の壺内壁との衝突 | Boundary Particles + PBF Density Constraint + CFL Substep |
| 数値的不安定・極端な一時貫通 | SafetyCorrection（例外処理） |

**クランプ由来の位置差は速度に変換しない。**
実装: `ComputeVelocity` は `PredictedPositions` と `Positions` の差から速度を出すが、
安全クランプは専用バッファ `SafetyCorrection` に記録し、速度計算時にその分を差し引く。

```
v = (p′ − p − SafetyCorrection) / dt
```

これが前回のバグ（クランプの位置差が速度に化ける綱引き）の再発を構造的に防ぐ肝である。

#### 常態化は「正常」ではない

**SafetyCorrection を大量発生させることで Fluid を壺内に無理やり閉じ込める実装は禁止。**
発生が常態化している状態は「動いているから良い」ではなく、
**壁衝突処理が破綻している**と判断する。

Debug 指標として以下を計測・表示する:

| 指標 | 破綻の判断基準 |
|---|---|
| SafetyCorrection 発生回数（累計） | — |
| SafetyCorrection 発生 Particle 数（フレーム毎） | 全粒子の数 % を超えたら異常 |
| 1 フレームあたりの発生率 | 恒常的に 0 でなければ異常 |
| **連続発生フレーム数** | **連続して発生し続けている = 破綻** |
| 最大 Correction 量 | 粒子間隔を大きく超えていたら CFL 設定が不足 |

発生時の対処は「SafetyCorrection を強める」ではなく、原因側を直す:

1. CFL サブステップ数が足りているか（§3）
2. 境界粒子の密度・層数が足りているか（§10）
3. 境界体積 ψ の算出が正しいか
4. Boundary Viscosity 係数が極端でないか

**理想状態は「TEST A〜O のいずれでも SafetyCorrection が発動しない」ことである。**

---

## 11. Pot Interior Test と Overflow 判定 ★修正4

### 単純円柱判定を使わない理由

実測形状は 底 0.153 → 腹 0.232 → リム 0.195 の**樽形**。
`y > rimY && radius > rimR` では「壺の外へ出た」を正確に判定できない。

### Profile-based Interior Signed Distance

`PotInteriorProfile` から壺ローカルの符号付き距離を構築する:

```
R(y)      = プロファイル補間による内径
d_side(p) = length(p.xz) − R(clamp(p.y, floorY, rimY))
d_floor(p)= floorY − p.y
d_interior(p) = max(d_side, d_floor)          // 負 = 内部
```

判定:

| 領域 | 条件 |
|---|---|
| **Inside** | `d_interior < −ε` かつ `p.y ≤ rimY` |
| **Boundary** | `|d_interior| ≤ ε` |
| **Outside** | `d_interior > ε` または `p.y > rimY + rimOpeningHeight` |

### Rim Opening Region（明示定義） ★修正6

```
RimOpening = { p : rimY ≤ p.y ≤ rimY + rimOpeningHeight,  length(p.xz) ≤ rimR }
```

`rimOpeningHeight` は粒子間隔の 2〜3 倍程度。リムより上に境界粒子は置かないが、
「どこからでも外へ出られる」状態にはせず、**この領域を通ったかどうかを判定する**。

### Overflow と Penetration の区別

粒子ごとに `PrevRegionFlags` を保持し、遷移を判定する:

| 遷移 | 判定 |
|---|---|
| `Inside → RimOpening → Outside` | ✅ **正常な Overflow**。`AirborneMass` へ計上 |
| `Inside → Outside`（RimOpening を経ずに） | ❌ **Boundary Failure**。壁抜け/底抜け |

壁抜けはさらに脱出位置で細分する:

- 脱出点の `p.y < floorY` → **底抜け**
- `floorY ≤ p.y ≤ rimY` かつ `length(p.xz) > R(p.y)` → **壁抜け**

Boundary Failure は:
1. カウンタへ記録し Debug 表示（§39 の検証で使う）
2. 該当粒子は安全網で壺内へ戻す（**この移動は速度に変換しない**、§10）
3. **Overflow としては計上しない**（Mass 会計を汚さない）

これにより「正常な Overflow」「壁抜け」「底抜け」「不正な脱出」を区別できる。

### Overflow は生成しない ★修正6

Overflow は既存粒子の**移動**によってのみ発生する。粒子の生成・別バッファへの移動・
別 Fluid の生成は一切行わない。`RegionFlags` を書き換えるだけである。
Density Field も Marching Cubes も壺内外を区別しない。

→ §23〜§26 の連続性は「別々のものを繋げる」のではなく **「最初から一つしか無い」** ことで満たす。

### RimOpening は「出口」であって「押し出す装置」ではない ★追加修正1

RimOpening は **Overflow を発生させるトリガーではない**。
すでに壺内から壺外へ移動した Fluid を Overflow として**分類するための領域**である。

したがって RimOpening の判定結果によって、以下を行ってはならない:

- ❌ Particle Position を外側へ移動する
- ❌ Particle Velocity を外向きに変更する
- ❌ 外向きの Force を加える
- ❌ Particle を生成する
- ❌ Overflow 用 Particle を生成する
- ❌ Overflow 用 Mesh を生成する
- ❌ Overflow 用 VFX を生成する

Fluid が壺の外へ出る原因は、**必ず**以下の物理の結果でなければならない:

`World Gravity` / `Fluid Inertia` / `PBF Pressure` / `Viscosity` /
`Surface Tension` / `Moving Boundary`

正しい流れ:

```
Fluid が壺内に存在
  ↓
物理シミュレーション（上記6要素のみ）
  ↓
液体が実際にリム方向へ移動
  ↓
Rim Opening を通過                 ← ここは「通過を観測する」だけ
  ↓
壺外へ出る
  ↓
RegionFlags = Airborne             ← 分類のみ。位置・速度に触れない
  ↓
Overflow として Mass 集計
```

**実装上の制約**: `ClassifyRegionAndMass` カーネルは `RegionFlags` / `PrevRegionFlags` /
`MassCounters` にのみ書き込む。`Positions` / `PredictedPositions` / `Velocities` /
`DeltaP` への書き込み権限を持たせない（読み取り専用としてバインドする）。
これによりコード上、分類処理が Fluid を押し出すことが**不可能**になる。

---

## 12. Density Field ★修正3, 修正7, 修正8

### Atomic Accumulation（修正3）

`RWTexture3D<float>` への `InterlockedAdd` は使わない（float への atomic は使用できない）。

```
Particle
  ↓  Poly6 カーネル
  ↓  値を ρ0 で正規化 → densityFixedPointScale 倍 → uint 化
RWStructuredBuffer<uint> DensityAccum        ← InterlockedAdd
  ↓  Decode（uint → float、スケール除算）
RWTexture3D<float> DensityField (R16F)       ← Visual 用
  ↓  3D 分離ガウシアン (§14 Density Smoothing)
Marching Cubes
```

- **Atomic 蓄積用**と **Visual 用**を分離する（修正3 の要求）
- `densityFixedPointScale` は Inspector 調整。小さすぎると量子化で Surface が粒状になるため、
  Debug で「蓄積値の最大値 / uint 上限」を表示して飽和と精度不足を監視する

### Dynamic Grid の安定化（修正7）

Particle AABB 追従は維持するが、そのままではグリッドが毎フレーム振動して Surface が
ちらつく。したがって:

| 対策 | 内容 |
|---|---|
| **Voxel Size 固定** | 6mm 固定。ドメインの広さでスケールしない（修正8 の要求） |
| **Quantized Grid Origin** | Grid Origin を Voxel Size 単位に量子化（`floor(x / voxel) * voxel`） |
| **Padding** | AABB に粒子間隔 × 4 のマージン |
| **Minimum Extent** | 壺を必ず包含する最小サイズ |
| **Maximum Extent** | 上限（超えたら Brick 疎割り当てで対応） |
| **ヒステリシス** | 縮小は AABB が閾値以上小さくなった時のみ。微小移動では Origin を変えない |

### 細い液だれ・液滴の解像度（修正8）

**Voxel Size を粗くして解像度を落とすことはしない。** 液だれ・液滴が消えるため。

代わりに **Sparse Brick 割り当て**で「全体は疎、液体のある所だけ密」を実現する:

- ドメインを 8³ voxel の Brick に分割
- `MarkBricks` が粒子を含む Brick（+1 Brick マージン）を立てる
- Density 蓄積・Smoothing・Marching Cubes は**立っている Brick のみ**処理

**Surface が分割されない理由**: Voxel Size と Density 関数は全ドメインで共通・連続。
隣接 Brick は共有面で完全に同じ値を持つため、継ぎ目は発生しない。
**Brick は「同じ 1 つの場のどこを計算するか」を決めるだけで、別の場を作らない。**

したがって「複数の別 Fluid を生成しているように見える Surface 分割」(修正8 末尾) には該当しない。

**実装段階**: Phase 2 では全 Brick を立てた **Dense 経路**で実装する（Brick 疎割り当ての特殊ケース）。
壺内に液体が収まっている間はこれで足りる。Phase 9（Ground Fluid）で液体が壺〜地面に
広がった時点で Sparse を有効化する。**両者は同一の場から同一の結果を出す。**

### Brick Pool（実装済み・2026-08-14）

上の「疎ディスパッチ」だけでは足りなかった。計算量は液体の量に比例するが、
**メモリはドメインの体積に比例したまま**なので、密度場を壺の周り 1.77m の箱にしか
広げられず、こぼした液体が離れると描画されず、箱の縁が四角い境界線として見えた。

そこで Brick に **プールのスロットを毎フレーム割り当てる**方式にした。
メモリが「実際に液体がある Brick の数」に比例するので、ドメインを 24m 角へ広げても
VRAM は 241MB → 107MB に**下がる**。

| 構造 | 役割 |
|---|---|
| `BrickSlot[brick]` | Brick → プールのスロット。未割当は `0xFFFFFFFF` |
| `ActiveBricks[slot]` | スロット → Brick（Blur / Marching Cubes が位置を戻すのに使う） |
| `PoolAccum / PoolA / PoolB` | 実体。`slot * 512 + localIndex` で引く |

読み取りは必ず `ReadField(voxel)` を通す。未割当の Brick は密度 0 を返す。
Blur も Marching Cubes も法線も**マテリアルの厚み積分も**この 1 本を通るので、
場は全ドメインで 1 つ・連続のまま。Brick 境界に継ぎ目は発生しない。

守るべき点:

- **割り当ては粒子数に比例させる**（ドメイン全体を毎フレーム走査しない）。
  そうしないと範囲を広げた分だけ重くなり、この方式の利点が消える。
- **確保する範囲は voxel 半径から導く**。Brick 単位で 2 個ぶんだと 125 Brick になり、
  地面に散った液滴 1 粒ごとにそれを確保してプールを使い切る。
  Splat 半径 + Marching Cubes/法線 だけを足す。**Blur の到達ぶんは足さない**
  （Splat 半径の外は実際に密度 0 なので、未割当を 0 と読むのが正しい値）。
- **法線を `BuildSurface` の中で計算しない**。プール参照はテクスチャ読みと違って
  8 タップ x 索引計算になるため、Marching Cubes の分岐へ展開されると FXC が落ちる。
  位置だけ書き出し、頂点ごとに別カーネルで 1 回だけ付ける。

### 単一 Density Field（修正7 末尾）

「壺内液体」「Overflow」「Ground Fluid」は**すべて同一の Density Field へ入れる**。
別々の Surface Mesh を生成して後から接続する方式は禁止。

---

## 13. Surface Reconstruction: GPU Marching Cubes ★修正9

- 256 パターンの edge/tri テーブルは C# 側で生成し `StructuredBuffer<int>` でアップロード
- 1 スレッド = 1 セル。立っている Brick のセルのみ処理
- 法線は **Density Field の勾配**から算出（三角形法線より滑らか / §14）
- Isovalue は Inspector 調整 (§14)
- 描画は `DrawProceduralIndirect`（CPU へ戻さない）

### Buffer Capacity（修正9）

| 項目 | 設計 |
|---|---|
| `MaxVertices` | 初期 1,572,864 頂点（= 524,288 三角形）。Vertex 24B → 約 37MB |
| `MaxTriangles` | `MaxVertices / 3` |
| `MCOverflowCounter` | `RWStructuredBuffer<uint>`。容量超過で書き込めなかった三角形数 |
| 容量超過時の挙動 | `InterlockedAdd` で確保したインデックスが容量を超えた場合は**書き込まずカウンタを加算するだけ**。バッファ外書き込みを行わない |

- Buffer 不足でも**メモリ破壊・不正 Vertex・GPU エラーは発生しない**（書き込み前に境界チェック）
- Debug 表示に `OverflowCounter` と使用率を出す
- **容量不足を理由に Sphere / Blob / Line 等の簡易 Surface へ切り替えることは禁止**。
  容量不足が起きた場合の対処は「容量を増やす」か「Brick 疎割り当てを有効化する」であり、
  Surface 方式の変更ではない

サイズ根拠: 液体表面積を約 3m²、voxel 6mm と仮定すると表面セル数 ≈ 3 / 0.006² ≈ 83,000。
MC の平均 3 三角形/セルで約 250,000 三角形。初期容量はその 2 倍の余裕を持たせている。

---

## 14. Liquid Material

§15/§16。**Alpha 透過を主役にしない。** 出力は不透明 (`alpha = 1`)。

**色の基準は完成イメージ `ポーション＿神聖.png`（2026-08-13 差し替え）。**
それ以前の基準は濃緑だったが、深い青＋内部発光の「神聖な」ポーションへ変更した。

| 要素 | 実装 |
|---|---|
| Deep Blue Base | 深く鮮やかな青 |
| 厚み | Density Field を視線方向に積分した Thickness → 色深度 |
| 薄い部分 | 明るい水色 |
| 厚い部分 | 濃紺へ沈む（厚みで内部が翳る） |
| **内部からの発光** | **光源に依存しない加算項。薄いほど早く光り、厚いほど吸収される。影の中でも光る** |
| Smoothness / Specular | 高 Smoothness + 明瞭なハイライト |
| Fresnel | 輪郭の強調 |
| Subtle Normal | 微細ノイズ法線（**形状には触れない**） |
| 弱い SSS 的表現 | 逆光時のみ薄く加算 |

発光は「表面が光る」のではなく「中で光っているものが透けて見える」ことを狙う。
そのため厚みを吸収の指数に使い、厚い中心にもわずかな残光を残す。

「背景が透ける膜」(§16) は不透明出力により構造的に発生しない。

---

## 15. 液面 (§17)

液面専用 Plane は存在しない。液面は Liquid Volume の上部そのもの。
Density Field の等値面が変形すれば液面も変形する。
Shader だけで液面を波打たせる経路は存在しない。

---

## 16. Fluid Mass Management ★修正10

| Mass | 定義 |
|---|---|
| `PotMass` | 壺内部に存在する Fluid |
| `AirborneMass` | 壺外・空中。落下中の液だれ・液滴 |
| `GroundMass` | 地面上に存在し、**将来的に回収可能**な Fluid |
| `RetiredMass` | ゲーム世界から**完全に消滅し、回収不可能**になった Fluid |

```
TotalMass = PotMass + AirborneMass + GroundMass + RetiredMass   （常に一定）
```

- 集計は GPU の `InterlockedAdd`（固定小数）→ 非同期リードバック
- **Retired へ移した Mass は PotionVolume へ戻らない**
- **GroundMass を回収した場合のみ** `GroundMass → PotMass` へ移動する (§34)
- Mass Conservation の Debug 検証では、**意図的に Retired へ移した Mass を
  「数値誤差」として扱わない**。Retired は収支の正規の項目である
- 誤差が閾値を超えた場合のみ警告ログ（Debug ビルドのみ）

---

## 17. PotionVolume Synchronization ★修正11

```
Fluid Simulation → PotMass → PotionVolume
```

```
PotionVolume (0..1) = PotMass / InitialTotalMass
```

- **PotMass が先、PotionVolume が後**。逆方向の経路は存在しない
- 液面高さだけを操作して PotionVolume を変更する経路は存在しない (§30)
- Overflow した Fluid Mass だけ PotMass が減る
- 補充は Retired 粒子を壺内へ再投入して Mass を戻すことで行う

---

## 18. World Gravity と Goblin 傾斜 ★修正12

**液体の静止方向は常に World Gravity。**

以下は**禁止**であり、設計上そのような経路を持たない:

- Goblin / Pot の Rotation から液面角度を直接設定する
- 「坂道で 20 度傾く → 液面も 20 度傾ける」という直接変換

正しい決定経路:

```
Goblin/Pot の姿勢
  → Boundary Particle の World 位置・速度（Moving Boundary）
World Gravity (Physics.gravity)
  → Fluid Particle への外力
        ↓
   PBF Simulation
        ↓
   液面（Density Field の等値面）
```

液面は**シミュレーション結果としてのみ**決まる。液面角度を計算して設定するコードは存在しない。

---

## 19. Ground Slope ★修正13

Ground Slope は**液面角度を直接決めるためには使用しない**。用途は以下に限定する:

- 地面 Collision（接触面の法線）
- Ground Fluid の流動方向
- Ground Fluid の広がり方

壺内 Fluid の液面は World Gravity と Fluid Simulation によってのみ決まる。

---

## 20. Ground Fluid (§28)

地面に達した粒子は**流体のまま**扱う。

- `RegionFlags = Ground`
- 粘性・摩擦を強め、薄く広がる
- **Density Field にも Marching Cubes にも参加し続ける**（平面 Decal にしない）
- 一定時間後に `Retired` へ。Mass は `RetiredMass` へ移る
- 回収システム実装時は `GroundMass → PotMass` (§34)

---

## 21. Goblin Integration (Phase 11)

| 入力 | 取得方法 | 用途 |
|---|---|---|
| Pot Position / Rotation | `Carry_Pot.transform`（CarryRig の LateUpdate 後） | Boundary の World 変換 |
| Pot Linear Velocity | Transform 差分（平滑化・テレポート検出） | `V_boundary` |
| Pot Angular Velocity | 回転差分 → 角速度 | `V_boundary` の `ω × r` |
| Goblin Velocity / Acceleration | `CharacterController.velocity` / 差分 | Debug 表示・CFL |
| Ground Slope | 足元レイキャスト法線 | Ground Fluid のみ (§19) |
| World Gravity | `Physics.gravity` | Fluid の唯一の外力 |

**注**: `GoblinLocomotion.gravity = −20` はゲーム挙動用の値。Fluid は §19 に従い
`Physics.gravity`(−9.81) を使う。両者は独立でよい（ジャンプ・着地は壺の実測運動を通じて
Moving Boundary から Fluid へ伝わる）。

---

## 22. Debug / Test (§39)

`FluidTestRig` を TEST A〜O へ拡張。数字キー / Inspector 切替。

A 静止 / B 左右傾斜 / C 前後傾斜 / D 急加速 / E 急停止 / F 方向転換 /
G 坂道 / H ジャンプ / I 着地 / J リムOverflow / K 液だれ / L 液滴 /
M Ground Flow / N PotionVolume減少 / O Low Volume

### Debug 表示項目

- 各 Mass（Pot / Airborne / Ground / Retired / Total）と収支誤差
- **正常 Overflow 数 / 壁抜け数 / 底抜け数**（修正4）
- **SafetyCorrection: 発生回数 / 発生粒子数 / 発生率 / 連続発生フレーム数 / 最大補正量**（追加修正2）
- MC OverflowCounter と Vertex 使用率（修正9）
- Density 蓄積の最大値 / uint 上限（修正3）
- Brick 割り当て数
- サブステップ数（CFL）

**エディタ非フォーカス時に Player Loop が進まない**問題があるため、
`SimulateSeconds()` による決定論的ステップ実行を併用する（前セッションで確認済み）。

各 Phase 終了時に §38 のチェックを Game View で行い、1 つでも該当したら次へ進まない。

---

## 22-A. Visual Validation: TEST J / K / L ★追加修正3

完成イメージにおいて最も重要なのは

`リムから溢れる液体 → 太い液柱 → 細くなる → Neck → 液滴形成 → 液滴分離 → 落下`

という**一連の形状変化**である。したがって **TEST K（液だれ）と TEST L（液滴）を
最重要 Visual Validation として扱う**。

### TEST J: Rim Overflow — 合格条件

- [ ] 液体がリムへ自然に寄る
- [ ] リム上で液体が盛り上がる
- [ ] リムを越える
- [ ] 壺内 Fluid と Overflow が**連続している**
- [ ] Overflow が突然生成されない
- [ ] Sphere / Blob / Line に見えない

### TEST K: Liquid Drip — 合格条件

- [ ] リムから液体が**連続して**伸びる
- [ ] **根元が太い**
- [ ] 重力方向へ垂れる
- [ ] 下方向へ進むほど細くなる
- [ ] Surface が滑らか
- [ ] 粘性が感じられる

### TEST L: Droplet — 合格条件

- [ ] 液柱先端に液体が集まる
- [ ] **Neck が形成される**
- [ ] Neck が細くなる
- [ ] 最終的に液滴が**分離する**
- [ ] 分離後も**同じ Fluid Simulation 由来**である
- [ ] 球体 Particle を追加したような見た目にならない

### Voxel Size 6mm は初期値であり決め打ちではない

Voxel Size 6mm は初期値として維持する。ただし「6mm で必ず十分」とは**設計上決め打ちしない**。

実装後の Visual Test で以下が発生した場合、**性能を理由にその状態を完成としない**:

- 細い液柱が維持できない
- Neck が形成されない
- 液滴が形成されない
- 液滴が途中で消える
- 液滴が不自然な球体に見える
- 液柱が太すぎる
- Surface が粒状になる

#### 品質確保のための調整順序

1. `Voxel Resolution`（必要なら 6mm より細かくする）
2. `Density Kernel` 半径
3. `Isovalue`
4. `Surface Smoothing` 強度
5. `Particle Spacing`（＝粒子数）
6. `Surface Tension`
7. `Viscosity`
8. `Marching Cubes Resolution`

**Sparse Brick は「液体の存在する領域だけを高解像度で処理するための最適化」として使う。**
解像度を落とすための手段ではない。

#### 代替表現の禁止

以下は**禁止**。液だれと液滴は、壺内部 Fluid と**同じ Particle Simulation・
同じ Density Field・同じ Marching Cubes Surface** から生成する。

- ❌ 液滴を Sphere Particle で補う
- ❌ 液だれを Line Renderer で補う
- ❌ Overflow 専用 Mesh を追加する

---

## 22-B. 完成判定 ★追加修正3

以下のいずれかに該当する場合、**「実装完了」と報告してはならない**:

- ❌ 液体が単純な平面に見える
- ❌ 液体が透明な色付きの膜に見える
- ❌ Overflow が線に見える
- ❌ Overflow が球体の連続に見える
- ❌ 液柱が硬い棒のように見える
- ❌ 液滴が単純な Sphere に見える
- ❌ 壺内 Fluid と Overflow の**境界が見える**
- ❌ 粘性が感じられない
- ❌ 液体の慣性が弱い
- ❌ **SafetyCorrection が常態化している**
- ❌ **BoundaryFailure が発生している**

**「物理的には動いている」だけでは完成としない。**

完成条件は、最終 Game View で
**「厚みのある粘性ポーションが実際に壺の中で揺れ、そのまま連続して溢れている」**
と視覚的に感じられることである。

---

## 23. Performance Strategy ★修正14

§36 に従い、**まず品質**。最適化は Phase 12。

以下は **目標値であり保証値ではない**。実装後に GPU Profiler で**実測**する。

| 処理 | 目標 |
|---|---|
| PBF (16384 粒子 × 4 反復 × サブステップ) | 0.5〜1.5 ms |
| Bitonic Sort | 0.2 ms |
| Density Accum + Decode + Smooth | 0.5 ms |
| Marching Cubes | 1.0〜2.0 ms |
| 描画 | 0.3 ms |
| **合計（目標）** | **2.5〜4 ms** |

**品質を落として目標 ms に合わせることは禁止。**
まず正しい Fluid Simulation と Surface を完成させ、その後 Profiler 実測を見て最適化する。

最適化の順序（必要になったら）:
1. Brick 疎割り当ての有効化（空セル棄却）
2. Surface 更新頻度の分離（物理 60Hz / Surface 30Hz + 補間）
3. Async Compute
4. Bitonic Sort の段数削減（部分ソート）

---

## 一貫性の確認: 一連の挙動が設計上成立するか ★修正15

| # | 挙動 | 担当する設計要素 |
|---|---|---|
| 1 | ゴブリンが歩く | GoblinLocomotion (§21) |
| 2 | 壺が移動・回転する | GoblinCarryRig → `Carry_Pot.transform` |
| 3 | 壺 Boundary も World Space で移動 | §3 Boundary Particle（Local 固定 / World 更新） |
| 4 | Fluid が Boundary と World Gravity の影響を受ける | §2 慣性系定式化。外力は重力のみ、壺の影響は Moving Boundary |
| 5 | 液体が慣性で遅れて動く | §2 の表。実際の慣性（疑似力ではない） |
| 6 | 液体がリムへ寄る | PBF + Moving Boundary の結果 |
| 7 | リム上で盛り上がる | 同上。リムより上に境界粒子が無い (§11) |
| 8 | Rim Opening を通過 | §11 RimOpening Region の遷移判定。**RimOpening は通過を観測するだけで、液体を押し出さない**（追加修正1） |
| 9 | **同じ** Fluid Particle が壺外へ出る | §11「Overflow は生成しない」。`RegionFlags` を書き換えるのみ |
| 10 | **同じ** Density Field へ入る | §12「単一 Density Field」 |
| 11 | **同じ** Surface Reconstruction から Overflow Surface が生成される | §13 MC は壺内外を区別しない |
| 12 | 液体が伸びる / Neck / 液滴 | §12 Voxel 6mm（初期値）+ Sparse Brick で細部を保持。合格条件は §22-A の TEST K / L（追加修正3） |
| 13 | Ground へ到達 | §20 Ground Fluid（流体のまま） |
| 14 | 流出した Mass だけ PotMass が減少 | §16 Mass 会計（GPU 集計） |
| 15 | PotionVolume が減少 | §17 `PotMass → PotionVolume` の一方向 |

**すべて同一の Fluid Simulation / 同一の Density Field / 同一の Surface から生じる。**
別 VFX・別 Mesh・別 Particle System は設計上どこにも存在しない。

---

## 構造的に発生しないことの確認

| 禁止事項 | 構造的に発生しない理由 |
|---|---|
| **静止した壺から自然に漏れる** | 壁が Boundary Particle として圧力ソルバーの**内側**にあり、綱引きが起きない。安全クランプは速度に変換されない (§10)。漏れが起きても壁抜け/底抜けとして検出・計数される (§11) |
| **壁を突き抜ける** | Boundary Particle の密度結合 + CFL サブステップ (§3) + 安全網 (§10)。貫通は Penetration として検出され Overflow に計上されない (§11) |
| **Overflow が球体になる** | 球体を生成するコードが存在しない。形状は Density Field の等値面から MC が生成する。液滴は Surface が分離した結果としてのみ生じる (§13, §27) |
| **壺内 Liquid と Overflow が別物になる** | Overflow は粒子の生成ではなくフラグの書き換え。粒子バッファ・Density Field・Surface すべて共通 (§11, §12) |
| **ゴブリンの傾きを液面角度へ変換する** | 液面角度を計算・設定するコードが存在しない。液面は Density Field の等値面であり、姿勢は Boundary の位置・速度としてのみ Fluid に入る (§18) |
| **液体量だけ減って Fluid Mass が減らない** | `PotionVolume` は `PotMass` からの導出値であり独立に書けない (§17)。Mass は GPU 集計の実測値 (§16) |
| **RimOpening が液体を人工的に押し出す** | 分類カーネルは `Positions`/`Velocities`/`DeltaP` を読み取り専用でバインドし、書き込み権限を持たない。押し出しがコード上不可能 (§11 追加修正1) |
| **SafetyCorrection の常態化で無理やり閉じ込める** | 発生率・連続発生フレーム数を計測し、常態化を「破綻」と判定する。理想状態は全 TEST で発動 0 (§10 追加修正2) |
| **液滴を Sphere、液だれを Line で補う** | 代替表現を明示禁止。液だれ・液滴は壺内 Fluid と同じ Simulation / Density Field / MC Surface から生成する (§22-A 追加修正3) |

---

## 未解決事項（実装開始前に確認したい点）

1. **完成イメージ画像がこのセッションに届いていない。** §47 が最終見た目の基準としているため、
   再添付が必要。それまでは §15/§16 のテキスト仕様を基準に進める。
2. **初期パラメータ**: 粒子数 16384 / Voxel 6mm / MC 頂点上限 1,572,864 で開始する想定。
   **Voxel 6mm は初期値であり決め打ちではない**。TEST K / L（§22-A）が合格しない場合は
   性能を理由に妥協せず、まず解像度・カーネル・Isovalue 等を調整する（追加修正3）。
3. **Boundary Viscosity 係数 `μ_b` の初期値**は実測で決める必要がある。
   回転追従（TEST F）で「壁だけ滑って中身が回らない」場合は上げる、
   「中身が壺と一緒に剛体的に回る」場合は下げる。Phase 6 で確定させる。
4. **Sparse Brick の有効化タイミング**を Phase 9 としたが、Phase 7（Overflow）の時点で
   液体が壺〜地面に広がるなら前倒しになる可能性がある。Phase 7 の実測で判断する。
