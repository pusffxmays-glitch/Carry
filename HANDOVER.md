# 現状まとめ（2026-08-15 時点）

再起動後にここから再開できるようにした要約。
詳しい経緯と実測値は `WORKLOG.md`、未解決は `OPEN_ISSUES.md`、仕様は `FLUID_DESIGN.md`。

---

## 1. 動かし方

* シーン: `Assets/Scenes/CastleStage.unity`
* ステージを組み直したいとき: メニュー
  `Carry/Setup/Test Stage を作る（部屋拡大 + ギミック + デバッグワープ）`
  （何度実行してもよい。部屋の拡大倍率は累積しない）
* 流体一式を組み直したいとき: `CarrySetupFluidGame`

### 操作

| 入力 | 動作 |
|---|---|
| W / S | 前進 / 後退 |
| A / D | その場旋回 |
| Shift | 走り |
| Space | ジャンプ |
| ← / → | 壺の左右バランス（腕の高さ差） |
| ↑ / ↓ | 壺の前後バランス（↑ 前傾 / ↓ 後傾） |
| 1〜6 | 各ギミックの手前へワープ（ポーション満タンに戻る） |

### ギミック

| キー | 名前 | 内容 | 摩擦 μ |
|---|---|---|---|
| 1 | Slope_BankLeft | 左傾斜 15度 | 1.0 |
| 2 | Slope_Up | 上り勾配 15度 | 1.0 |
| 3 | Slope_BankRight | 右傾斜 15度 | 1.0 |
| 4 | Slope_Slippery | 氷の勾配（低摩擦）15度 | **0.08** |
| 5 | Jump_Platforms | ジャンプで渡る 2 つの台（隙間 1.6m） | 1.0 |
| 6 | Bridge_Swaying | 左右に揺れる橋（8度 / 2.6秒） | 1.0 |

部屋は 42m 角（元は 24m 角）。手前の列 (z=4..12) に 1〜4、奥の列 (z=11..20) に 5〜6。

---

## 2. 現在の設定値（CastleStage に保存済み）

```
FluidCore     particles=16384 fillFraction=0.95 initialSettleSeconds=0.7
              groundLifetime=10 escapeMarginSpacings=2 maxSpeed=5
              minSubSteps=6 maxSubSteps=20
              viscosity=2.8 boundaryViscosity=0.55 boundaryPressureScale=1.6
FluidBoundary simMaxSpeed=12 simMaxAngularSpeed=720 simMaxAccel=0
              teleportDistance=0.6 teleportAngle=100 rimFadePerKernel=1.0
FluidSurface  domainSize=(30,4.5,30) poolBrickCapacity=16384
              maxTriangles=2400000 clipToContainer=true
              voxelsPerSpacing=3 isoValue=0.45 smoothingPasses=2
GoblinCarryRig armInputSpeed=4 pitchRangeDeg=16 pitchHandReach=0.06
              staggerThresholdDeg=5.5 staggerRampDeg=10.5 staggerPitchWeight=0.35
GoblinTerrainTilt maxTiltDeg=30 responseSpeed=8 tiltStrength=1
GoblinGroundSlide maxSlideSpeed=8 groundedDistance=0.20 groundStickSpeed=2.5
GoblinLocomotion walk=1.0 run=5.0 turn=110 gravity=-20 jump=6
Physics.gravity = (0, -20, 0)   ← locomotion と一致させてある。ズレるとジャンプで噴き出す
```

---

## 3. このセッションで入れたもの

### 3-1. 液体の描画: Sparse Brick Pool (§14)

密な 3D テクスチャをやめ、Brick(8³ voxel) ごとにプールのスロットを毎フレーム割り当てる
方式にした。メモリが「実際に液体がある Brick の数」に比例するので、範囲を広げても軽い。

| | 変更前 | 変更後 |
|---|---|---|
| 描画される距離 | 約 1.77m | 12m 以上（ドメイン 30m 角） |
| 箱の縁の四角い境界線 | 出る | 出ない |
| VRAM（密度場） | 241MB | 107MB |

**要注意（再発しやすい）**

* プール参照はテクスチャ読みと違い 8 タップ + 索引計算になる。Marching Cubes の
  分岐へ展開されると **FXC が落ちて Editor ごと固まる**。対策は 2 つとも必要:
  ① トリリニア 8 タップと中央差分を `[loop]` で回す
  ② 法線を `BuildSurface` の中で計算せず、専用カーネルで頂点ごとに 1 回だけ付ける
  （落ちた `UnityShaderCompiler` プロセスを kill すれば Editor は復帰する）
* 確保する Brick 範囲は **voxel 半径から導く**。Brick 単位で 2 個ぶんにすると
  地面の液滴 1 粒ごとに 125 Brick 確保してプールを使い切る。
* マテリアルの厚み積分も同じ間接参照にすること。忘れると表面だけ広がって
  厚み（色と発光）が壺の周りにしか出ない。

### 3-2. 「線上のノイズ」= 厚みの量子化

`MeasureThickness` が「サンプル点が内側なら stepLen を足す」数え方で、厚みが 37.5mm
刻みに量子化されていた。厚みは色と発光を決めるので、そのまま等高線状の縞になる。
裏側の等値面を横切る位置を線形補間で求めて連続にした。

### 3-3. 「ポーションが壺に遅れてついてくる」

§21 の平滑化が **速度と加速度を制限した一次遅れ追従**で、等速移動中も定常的な位置ずれが
残っていた。流体が見る壁が見えている壺より後ろに置かれていた。

実測（旧設定）: 走り 3.0m/s で **41.7mm**、ジャンプで **222mm** 遅れる（壺の内径は約 460mm）。

`simMaxSpeed` 5→12 / `simMaxAngularSpeed` 240→720 / `simMaxAccel` 70→**0（無効）** にして
全ケース遅れ **0.0000m**。15.5m/s の跳ね（リグの計算飛び）は今も削るので発散対策は生きている。
副次的にジャンプのこぼれも 34% → 11.5% に減った。

### 3-4. ジャンプで壺の底から液体が噴き出す

`SafetyCorrection` の 2 つの枝が `else if` で、床側に半径条件が付いていたため
**床より下かつ PotMaxRadius より外**（床と壁が出会う「角」）がどちらの枝にも入らず
一切補正されていなかった。ジャンプの上向き加速で液体がその角から搾り出されていた。

リムより下では「実体の中にいるなら、まず床の上へ上げ、そのうえで内側半径に収める」
という 1 本の処理に統一。噴き出しは 0 になり、注ぎ出し(100度)は 96.1% 出るので
堰にもなっていない。

### 3-5. 開始時のこぼれ

種の格子が PBF の密度拘束を満たしておらず、開始直後に緩んで液面がリムを越えていた。
`initialSettleSeconds`(0.7秒) を追加し、**容器を静止させたまま本番と同じ SubStep を
243 回**回してから始める。整定中は Escape 判定を止める。
結果、開始 0.0 秒から **fill=1.000 / 壺の外 0 粒子** で平坦。

### 3-6. こぼれた量と残量がリンクしない

`PotMass = InsideCount`（壺の内側の幾何判定）だったため、揺れで液面がリムより上へ
持ち上がるたびにゲージが落ち込み、戻ると回復していた。
実測: 実損失 3.3% の瞬間にゲージは 0.998 → **0.598**。

失われたかどうかを決めているのは幾何ではなく **Escaped 判定**なので、残量もそれに揃えた。

```
RecoverableCount = Inside + Rim + (Airborne - Escaped)
PotMass          = RecoverableCount * ParticleMass
AirborneMass     = Escaped * ParticleMass
```

ゲージと実損失のずれは全場面で **0.002 以内**。収支は閉じている（TotalMass/Initial = 1.0000）。

### 3-7. 地形による体の傾き（新規）

`GoblinCarryRig` は毎 LateUpdate で全ボーンの **world 位置**を基準姿勢から直接書き込む
（`bone.position = Posture.position + Posture.rotation * ...`）。つまり
**ボーンは親の回転を無視する**ので、「見た目用の子を作って傾ける」定石は効かない。
リグが姿勢を組み立てる **基準そのもの** を差し替える必要があった（`postureRoot`）。

* root（CharacterController 付き）は絶対に傾けない。`transform.forward * speed` で
  移動するので、傾けると歩くたび地面へめり込む。
* 地面法線は足元・前後・左右の **5 点平均**（1 本だと石畳の目地で跳ねる）。
* 壺の姿勢も Posture を土台にし、腕の高さ差は体に対する相対角として足す。

実測: 上り/左右傾斜 15度で壺も 14.4〜15.6 度傾く。

### 3-8. よろけ判定を世界基準に

`|armBalance|`（ゴブリンに対する相対角）で判定していたため、斜面では
「何もしなければよろけない／正しくバランスを取るとよろける」と逆になっていた。

**世界基準での壺の傾き（度）** に変更。さらに前後方向は重みを下げた
（人は足が左右に並ぶので支持面が左右に狭く前後に長い。上り坂ではよろけない）。

```
lateralDeg = asin(dot(pot.up, root.right))
foreDeg    = asin(dot(pot.up, root.forward))
tiltDeg    = sqrt(lateralDeg^2 + (foreDeg * staggerPitchWeight)^2)
```

平地の効き方は据え置き（armBalance 0.6 = 5.5度、0.9 = 16度をそのまま度に翻訳）。

### 3-9. ギミックまわり

* **CharacterController は PhysicMaterial の摩擦を見ない**（Rigidbody のソルバを
  通らないため）。氷の坂は `GroundSurface`(μ) + `GoblinGroundSlide` で自前に滑らせる。
  斜面のクーロン摩擦そのもの: 滑り出す角度 = atan(μ)。
* **CharacterController は動く床にも自動で乗らない。** 揺れる橋は
  前後フレームの姿勢行列の差分で乗っている相手を運ぶ。
* 滑りの接地判定に `controller.isGrounded` を使ってはいけない。
  「最後に呼んだ Move の結果」なので、自分で Move を足すと反転して
  加速と減速を往復し **その場で振動する**。実測した `GroundDistance` で判定し、
  滑走中は少しだけ地面へ押し付ける。

---

## 4. 計測のしかた（重要）

**Editor はフォーカスが無いとプレイループを回さない。**
`Application.runInBackground` を実行時に立てても Play に入り直すと false に戻る。
この状態で計測すると `Time.frameCount` が 1 のまま固まり、
「値が変化しない＝安定している」という **逆の結論** が出る。
実際にこの取り違えを一度やっている（「遅れは 0」「ジャンプの損失 0」と誤報告した）。

### 流体だけを回す

```csharp
core.autoStep = false;
boundary.ResyncMotion(); core.SeedFluid();
for (...) { container.position = ...; core.Step(1f/60f); }
```

### リグ込みで実機の動きを再現する

```csharp
loco.enabled = false; cc.enabled = false;
anim.Update(dt);
rigUpdate.Invoke(rig, null); rigLateUpdate.Invoke(rig, null);
core.Step(dt);
srf.BuildNow();          // 撮影する場合
```

### 1 フレームずつ追う（ジッタの切り分け）

フォーカス外では `Time.deltaTime` が最後の値（0.02）で固定される。これを利用して
`locomotion.Update → slide.Update → tilt.LateUpdate → rig.LateUpdate` を手で順に呼べば
**dt 一定でフレーム単位の再現**ができる。滑りの振動はこれで特定した。

---

## 5. 残っている課題

* **OI-1 リムの残留堰**: 静止液面がリムまで届かない（70.5% vs 理論 41.7%）。
  箱モードでは起きないので壺形状固有。円筒に置き換える切り分けテスト
  (`debugForceCylinderRadius`) は実装済みだが未実行。
* **OI-4 非同期リードバック**: `synchronousReadback = false` にすると
  Play 中の再初期化で Editor が固まる。既定は同期のまま。
* **地面の水たまりが広がらない**: Escaped 粒子は相互作用しないので、
  1 粒ずつ独立した液滴として落ちる。仕様上の割り切り。
* **こぼれ量の妥当性**: 走り + 旋回 + ジャンプ連打を 12 秒続けると 7 割以上失う。
  操作としてこれが妥当かは未判断（`escapeMarginSpacings` で調整可能）。
* `GoblinGroundSlide.minSlopeDeg` はコード既定 2 に対しシーンには 3 が保存されている
  （旧版の値が残っているだけ。実害なし）。

---

## 6. このセッションで触ったファイル

```
Assets/Shaders/Fluid/FluidSurface.compute      Brick Pool 全面書き換え
Assets/Shaders/Fluid/FluidCore.compute         脱出判定/SafetyCorrection/Escaped カウンタ
Assets/Shaders/Fluid/PotionLiquidSurface.shader プール間接参照 + 厚みの線形補間
Assets/Scripts/Fluid/FluidSurface.cs           Brick Pool / 壺クリップ
Assets/Scripts/Fluid/FluidCore.cs              PreSettle / ResetFluid / 残量の定義
Assets/Scripts/Fluid/FluidBoundary.cs          平滑化の上限（位置ずれ解消）
Assets/Scripts/Fluid/PotInteriorProfile.cs     外形プロファイル追加
Assets/Scripts/GoblinLocomotion.cs             WASD へ変更
Assets/Scripts/GoblinCarryRig.cs               postureRoot / 前後バランス / よろけ判定
Assets/Scripts/GoblinTerrainTilt.cs            新規（地形傾斜）
Assets/Scripts/GoblinGroundSlide.cs            新規（低摩擦の滑り）
Assets/Scripts/GroundSurface.cs                新規（摩擦マーカー）
Assets/Scripts/SwayingBridge.cs                新規（揺れる橋）
Assets/Scripts/GimmickWarpPoint.cs             新規
Assets/Scripts/DebugGimmickWarp.cs             新規（数字キーワープ）
Assets/Editor/CarrySetupTestSlopes.cs          ステージ構築
Assets/Editor/CarrySetupFluidGame.cs           流体の既定値
```
