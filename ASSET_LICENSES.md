# 外部アセット ライセンス一覧

このプロジェクトで使用している外部アセット(3Dモデル・テクスチャ)の一覧です。
新しい外部アセットを追加した場合は、必ずこの表に追記してください。
出所・ライセンスが確認できないアセットはプロジェクトに追加しないでください。

## 1. KayKit - Forest Nature Pack (Free tier)

- **作者**: Kay Lousberg (Kay Lousberg)
- **配布元**: itch.io
- **配布URL**: https://kaylousberg.itch.io/kaykit-forest
- **ライセンス**: CC0 1.0 Universal(パブリックドメイン)
- **商用利用**: 可
- **クレジット表記**: 不要(任意でKay Lousberg / www.kaylousberg.comのクレジットを歓迎、との記載あり)
- **改変**: 可(未改変のまま再配布して自作発言することは不可)
- **格納場所**: `Assets/ExternalAssets/KayKitForest/`(`Assets/fbx(unity)` のFBXとTexturesのみ保持。obj/gltf/無印fbxは容量削減のため削除済み)
- **ゲーム内での使用箇所**: 通常の森ステージ ― 木・茂み・岩・草の主要素材

## 2. KayKit - Dungeon Pack (旧称 Dungeon Remastered, Free tier)

- **作者**: Kay Lousberg
- **配布元**: itch.io
- **配布URL**: https://kaylousberg.itch.io/kaykit-dungeon-pack
- **ライセンス**: CC0 1.0 Universal(パブリックドメイン)
- **商用利用**: 可
- **クレジット表記**: 不要(任意)
- **改変**: 可(未改変のまま再配布して自作発言することは不可)
- **格納場所**: `Assets/ExternalAssets/KayKitDungeon/`(`Assets/fbx(unity)` のFBXとtexturesのみ保持)
- **ゲーム内での使用箇所**: 通常の森ステージ ― 石床・階段モジュールを石橋・古い石床として転用

## 3. Stylized Nature MegaKit (Standard/Free tier, 68/116モデル)

- **作者**: Quaternius
- **配布元**: itch.io
- **配布URL**: https://quaternius.itch.io/stylized-nature-megakit
- **ライセンス**: CC0 1.0 Universal(パブリックドメイン)
- **商用利用**: 可
- **クレジット表記**: 不要(任意でPatreon支援を歓迎、との記載あり)
- **改変**: 可
- **格納場所**: `Assets/ExternalAssets/QuaterniusNatureMegaKit/`(`FBX (Unity)` フォルダとTexturesのみ保持。無印FBX/glTF/OBJは削除済み)
- **ゲーム内での使用箇所**: 通常の森ステージ ― 木・植物・岩のバリエーション補強

## 4. Nature Kit (v2.1)

- **作者**: Kenney (www.kenney.nl)
- **配布元**: Kenney.nl
- **配布URL**: https://kenney.nl/assets/nature-kit
- **ライセンス**: CC0 1.0 Universal(パブリックドメイン)
- **商用利用**: 可
- **クレジット表記**: 不要(任意でKenney / www.kenney.nlのクレジットを歓迎、との記載あり)
- **改変**: 可
- **格納場所**: `Assets/ExternalAssets/KenneyNatureKit/`(`Models/FBX format` のみ保持。Isometric/Sideのプレビュー画像、DAE/GLTF/OBJ/STL形式は容量削減のため削除済み)
- **ゲーム内での使用箇所**: 通常の森ステージ ― 湖の復帰階段(`cliff_blockQuarter_stone`を階段の踏み段に使用)、`path_stone`を足場の崩れた遺跡風スラブ(RuinSlab)に使用。スタート地点の石橋は現在プロシージャル生成(コード生成メッシュ)に置き換え済みで、`bridge_center_stone`/`cliff_block_stone`は不使用(2026-08-11、湾曲アーチ橋への刷新に伴い変更)。

## 5. Poly Haven CC0 素材(個別ダウンロード)― 通常の森のメインビジュアル素材

写実寄りの渓流・森林という方向性への転換(2026-08-10)に伴い、KayKit/Kenney/Quaternius(スタイライズド・ローポリ)から、Poly Havenの写実PBRフォトスキャン素材をメインの見た目素材に切り替えた。特に "pine_forest" コレクションで統一的に揃えている。

- **作者**: Poly Haven contributors(rock_moss_set_01/02, boulder_01: Kless Gyzen 他。個別ページ参照)
- **配布元**: Poly Haven
- **配布URL**: https://polyhaven.com/a/ に各アセット名を付加したページ(例: https://polyhaven.com/a/pine_roots )。使用モデル一覧:
  `rock_moss_set_01`, `rock_moss_set_02`, `boulder_01`, `pine_roots`, `dead_tree_trunk`, `dead_tree_trunk_02`, `dry_branches_medium_01`, `tree_stump_01`, `tree_stump_02`, `fern_02`, `grass_medium_01`, `grass_medium_02`, `fir_sapling`, `fir_sapling_medium`, `pine_sapling_small`, `moss_01`, `fir_tree_01`, `shrub_01`, `shrub_02`, `root_cluster_01`, `mountainside`(2026-08-12追加、後日不採用), `coast_rocks_01`(2026-08-12追加), `root_cluster_02`(2026-08-12追加), `bark_debris_01`(2026-08-12追加), `rock_face_01`(2026-08-13追加), `rock_face_02`(2026-08-13追加), `coastal_cliff_01`(2026-08-13追加), `island_tree_01`(2026-08-13追加), `tree_small_02`(2026-08-13追加、密な葉の樹冠を持つ木)、`island_tree_03`(2026-08-13追加、湾曲幹+露出根を持つ古木、island_tree_01の同シリーズ)。
  地面マテリアル: `forrest_ground_01`, `mud_forest`, `dry_riverbed_rock`, `forest_ground_04`(苔むした地面), `forest_leaves_04`(落ち葉)(Terrain地面レイヤー、2026-08-12に苔・落ち葉レイヤーを追加し3層→5層化)。`lichen_rock`(2026-08-12ダウンロード済みだが今回のラウンドでは未使用 ― 湖崖壁などへの適用は今後の課題)。
  HDRI(スカイボックス/ライティング): `mossy_forest`。
- **ライセンス**: CC0(パブリックドメイン)
- **商用利用**: 可
- **クレジット表記**: 不要(任意)
- **改変**: 可
- **格納場所**: `Assets/ExternalAssets/PolyHaven/<アセット名>/`(いずれも2K解像度のFBX/テクスチャ、HDRIのみ2K HDR)
- **ゲーム内での使用箇所**: 通常の森ステージ全体 ― 岩・倒木・切り株・木の根・下草・小木・地面マテリアル・スカイボックス照明。「渓流+森+岩や土の足場」という新しい景観の主要素材。スタート地点の石橋(2026-08-11、参考写真に基づき積み石アーチ橋として全面作り直し。プロシージャル生成メッシュ、外部の橋アセットは不使用)にも `dry_riverbed_rock` を石材テクスチャとして使用し、`rock_moss_set_01/02` と `pine_roots` を橋の両端の地形なじませ装飾として使用。湖(2026-08-11、参考写真に基づき「滝・岩壁・苔・巨木に囲まれた神秘的な湖」へ全面改修)の岩壁テクスチャにも `dry_riverbed_rock` を使用し、`rock_moss_set_01/02`・`boulder_01`・`pine_roots`・`dead_tree_trunk_02` を湖岸の岩・苔・倒木・木の根の装飾および湖底の岩に使用。復帰用の石階段は既存の `cliff_blockQuarter_stone`(Kenney Nature Kit)を再利用し新しい湖岸位置へ再配置。2026-08-12のEnvironment Artブラッシュアップでは、`shrub_01`/`shrub_02`(低木レイヤー、`BuildForestFloorClutter`内で森床全体に散布)、`root_cluster_01`(木の根バリエーション追加、`BuildGroundDetail`)、`grass_medium_02`(下草バリエーション追加、Terrain Detail)を新規導入。
- **注記**: `pine_tree_01` / `pine_sapling_medium` は写実的だがFBXが189〜618MBと巨大でリアルタイム用途に非現実的なため、未導入。`fir_tree_01`(約249MB)は2026-08-12のEnvironment Artブラッシュアップで、Blenderでの大幅ポリゴン削減(3バリエーション: `fir_tree_01_a/b/c_decimated.fbx`、`Assets/ExternalAssets/PolyHaven/fir_tree_01/Decimated/`に格納、約3〜20MBへ削減)を経て導入し、通常の森ステージ外周の「巨大な古木」レイヤーの主力アセットとして使用。`dead_tree_trunk`・`tree_stump_01`・`tree_stump_02` は配布元FBXのスケール単位が他アセットと異なりUnity上で極小(数cm)にインポートされたため、配置スクリプト側で補正スケール(約28〜110倍)を適用している。`fern_02`・`grass_medium_01`・`moss_01` は個別GameObjectとしての配置(浮遊バグの原因になっていた)を廃止し、2026-08-11よりUnity Terrain Detail(GPU instancing)のプロトタイプとして再導入 ― Terrainの高さに自動追従するため浮遊しない。`fir_sapling`・`pine_sapling_small` は同様の浮遊問題によりフィードバックを受けて個別配置から完全に撤去済み(再導入する場合はTerrain Tree系統など浮遊しない方式を使うこと)。
- **注記2(2026-08-12、湖周辺Environment Artブラッシュアップ)**: `mountainside`(一体型の苔むした崖面フォーメーション、CC0)・`coast_rocks_01`(海藻・苔付きの海岸岩フォーメーション、CC0)・`root_cluster_02`(`root_cluster_01`とは別バリエーションの風化した木の根クラスター、CC0)・`bark_debris_01`(散乱樹皮片、CC0)を追加。配布元FBXには元々LOD0〜LOD3が同梱されており(Poly Haven側で用意されたLODメッシュ)、それぞれ最軽量のLOD3のみをBlenderで抽出・法線再計算した上で `<アセット名>_decimated.fbx` として同フォルダに書き出して実使用(元の全LOD入りFBXは参照用に保持)。`mountainside_decimated.fbx`(9,828頂点)・`coast_rocks_01_decimated.fbx`(42,685頂点、元は59.5m×42.5mの巨大フォーメーションのため配置時に縮小スケールを使用)。湖岸の「岩壁(RockWall)」ゾーンと「木の根が露出した岸(RootBank)」ゾーン、森床の小枝散乱に使用。**`mountainside`は2026-08-13、湖岸を大量の岩で埋め尽くす方向性を撤回した際に不採用(コードから完全削除)。理由は技術的な浮遊バグではなく、単体では「丸みを帯びた岩の塊」に見え、崖の一枚岩感が出なかったため。** `coast_rocks_01`はユーザーが手動で調整した`HeroCoastRocks`インスタンス1体のみ引き続き使用(座標はコードにハードコード、以後のリビルドで上書きされない)。
- **注記3(2026-08-13、湖岸を「岩の山」から「大きく美しい岩肌」へ方向転換)**: `mountainside`の代替として、より扁平で「露出した岩盤の断面」に近い `rock_face_01`(20,174ポリゴン、7.1×5.0×5.6m)・`rock_face_02`(29,566ポリゴン、4.9×4.7×3.5m、CC0、作者Dario Barresi)を導入。どちらもBlenderで軸変換の補正のみ実施(法線解析により、`rock_face_01`は+Z方向、`rock_face_02`は-Z方向が実際の岩肌の正面であることを確認済み — アセットごとに異なるため、配置コードでは個別に向きを設定している)。あわせて大規模な帯状の崖セクション `coastal_cliff_01`(元92m×11m×10m、CC0、作者Rob Tuytel / Rico Cilliers)も導入 — 元FBXにLOD0〜3が同梱されており、最軽量のLOD3(57,726ポリゴン)のみ抽出・軸補正して使用。対岸(180°ゾーン)の背景岩肌として縮小スケールで配置。**`rock_face_01`/`rock_face_02`は2026-08-14、Scene内配置(`HeroRockFace_210`/`HeroRockFace_305`)を撤去し不採用とした**(理由: 向きの逆転・Terrainからの浮遊など配置品質問題が複数回のリトライでも解消せず)。`mountainside`と同様、**プロジェクトのAssetsフォルダ内には引き続き保持**しているが、今後のEnvironment配置スクリプトでは使用しない・自動配置候補にも含めない方針。`coastal_cliff_01`は問題なく機能しているため引き続き使用する。
- **注記4(2026-08-13、木のシルエットのバリエーション追加)**: 現状の木がすべて直立したシルエットだったため、湾曲・傾斜した幹を持つ `island_tree_01`(CC0)を導入。元は121万頂点・81万ポリゴンと非常に重量級だったため、Blenderで約4%(約6万ポリゴン)まで軽量化して使用。湖岸に近い場所の「傾いた木・特徴的な古木」アクセントとして使用(直立した通常の木の置き換えではなく、バリエーションの一つとして少数配置)。手続き生成のTerrain Tree(Y軸回転のみ、傾き不可)では表現できないため、`BuildLakeHeroLeaningTrees()`(`CarryBuildTerrainForest.cs`)で個別GameObjectとしてハンドプレイス — 崖上4箇所(湖中心から見た角度130°/195°/245°/285°、既存のHeroRockFace/HeroCoastalCliffBandの隣接ゾーン)に、それぞれ実測した「本来の自然な傾き方向」(Blenderでキャノピー重心と幹元重心の差分ベクトルとして計測)を湖側へ向くようYaw回転させて配置 — 幹自体が崖から湖側へ伸びる古木として機能する。バッチモードでのスクリーンショット確認済み(`CarryTempHeroTreeCheck.cs`、130°/195°で特に明瞭な「湖側へ傾いた古木」のシルエットを確認)。マテリアルはFBXと同フォルダにテクスチャが同梱されているため自動リンクに成功(白マテリアル化なし、`CarryTempFindWhiteMats.cs`で確認済み)。
- **注記5(2026-08-13、Environment Art全面ブラッシュアップ — 「古代の森の聖域」方向転換)**: `tree_small_02`(CC0、密な葉の樹冠を持つ木、元115万ポリゴン→Blenderで約8.2万ポリゴンへ軽量化)と`island_tree_03`(CC0、湾曲幹+露出根を持つ古木、island_tree_01の同シリーズ、元106万ポリゴン→約9.2万ポリゴンへ軽量化)を追加導入。`tree_small_02`は`BuildTrees()`の一括Terrain Tree配置に「leafy giant」として組み込み、これまでCommonTree_1(幹に対して葉が薄い)が担っていた巨木枠の約70%を置き換え(ネイティブ高さ4.6mを3.0〜4.3倍に拡大、樹冠のボリューム重視)。`island_tree_03`は`BuildLakeHeroLeaningTrees()`にisland_tree_01と交互配置する第2樹種として追加(5本中2本、実測した傾きベクトルは弱いためisland_tree_01ほど劇的な「湖側への傾き」にはならないが、樹種の重複を避ける目的)。`island_tree_03`は自動テクスチャリンクに失敗したため(island_tree_01とは違いFBXの埋め込みマテリアルが同フォルダのテクスチャを自動検出できず白マテリアル化 — 原因不明だが同一パイプラインの兄弟アセットでも発生することを確認)、`FixIslandTree03Materials()`で`ModelImporter.materialLocation = External`+再インポート後のプレハブから実体を読み直す手法(このプロジェクトの既存の白マテリアル修正パターンと同じ)で個別に修正、`CarryTempFindWhiteMats.cs`で解消を確認済み。

### 旧アセット(KayKit Forest/Dungeon, Kenney Nature Kit, Quaternius)の扱い

上記4パックはローポリ・スタイライズド調のため、通常の森のメインビジュアルとしては使用しない方針に変更した。プロジェクトからは削除していない(石橋モジュールなど機能的に再利用できる部分があるため)が、新しい写実ベースのシーンでは非表示・不使用としている。**2026-08-13追記**: それまで森のTerrain Treeの主力(非巨木枠の約78%)として残っていたQuaternius `CommonTree_1`/`DeadTree_1`/`DeadTree_2`も、「テイストの違う木が混在している」というフィードバックを受けて完全に撤去。通常の森の木は現在100% Poly Havenの写実フォトスキャン種(`fir_tree_01` A/B/C/D + `tree_small_02`)に統一されている。標準的な「枯れ木・立ち枯れ」シルエットに相当する無料の写実アセットは見つかっておらず、その多様性は現状失われている(必要であればMeshyでの新規制作候補)。

## 6. Meshy AI 生成モデル ― スタート地点の石橋(2026-08-11差し替え)

- **作成方法**: ユーザー自身がMeshy(AI 3D生成ツール)で作成した石橋モデル。第三者配布物ではなくユーザー自身の生成物のため、外部配布ライセンスの確認対象ではないが、記録として残す。
- **格納場所**: `Assets/Stage/Forest/Bridge/Models/StoneBridge/`。元FBX(`Meshy_AI_Mossy_Stone_Bridge_0811073607_texture.fbx`、約51万頂点/75万ポリゴン)は参照用に保持しつつ、実際にシーンで使用するのはBlenderで軽量化した `Meshy_AI_Mossy_Stone_Bridge_decimated.fbx`(2026-08-11、約2.5万ポリゴンに削減、法線再計算済み)。テクスチャ: BaseColor/Normal/Roughness/Metallic の4枚。
- **ゲーム内での使用箇所**: 通常の森ステージ ― スタート地点の石橋本体(`Assets/Stage/Forest/Bridge/Prefabs/StoneBridge.prefab`)。マテリアルは `Assets/Stage/Forest/Bridge/Mat_MeshyStoneBridge.mat`(BaseMap/Normal/合成Metallic-Smoothnessテクスチャを設定済み)。歩行用Colliderはメッシュそのものではなく簡易Box Collider(デッキ用・両端の橋台用)を別途使用。
- **注記**: ポリゴン削減はBlender 5.2.0 LTS(`C:\Program Files (x86)\blender.exe`、headless `--background --python`)で実施。デシメート後もワールド寸法はほぼ変わらず(Extents (1.00, 0.31, 0.71) → (1.00, 0.31, 0.71))、インポート時の変換もクリーンな単位トランスフォームになった。

## 7. Meshy AI 生成モデル ― AzureCrystal(魔力を帯びた湖のクリスタル、2026-08-14導入)

- **作成方法**: ユーザー自身がMeshyで作成した5種一体のクリスタルモデル(Azure Crystal Outcrop)。第三者配布物ではなくユーザー自身の生成物のため、外部配布ライセンスの確認対象ではないが、記録として残す。
- **格納場所**: `Assets/Stage/Forest/Crystal/` 配下。`Source/` にMeshyオリジナル(FBX 約25.6万ポリゴン + BaseColor/Normal/Metallic/Roughness 4枚、元ファイル名のまま無改変で保持)。`Models/Separated/` にBlenderで5分割・軽量化(合計約8.9万ポリゴン、各35%)・ピボット調整(各底面中央)・軸補正済みの `AzureCrystal_LakeFloor/CliffWall/RockGap/CliffCrack/Rock.fbx`。`Textures/` に整理名のテクスチャコピー(`AzureCrystal_BaseColor/Normal/Metallic/Roughness.png`)+生成した `AzureCrystal_Emission.png`(BaseColorの青色度マスクから自動生成 — 結晶部分のみ発光させ岩部分は光らせないため)と `AzureCrystal_MetallicSmoothness.png`(URP Lit用にMetallic+Roughness→1枚へ合成)。`Materials/MAT_AzureCrystal.mat`(URP Lit、控えめな青Emission)。`Prefabs/PF_AzureCrystal_*.prefab` 5種。
- **ゲーム内での使用箇所**: 通常の森ステージの湖 ― 「湖の水に魔力が宿っている理由」を景観で伝えるEnvironment Asset。滝の岩盤亀裂(CliffCrack/RockGap、195°・225°・255°周辺の滝の水源部)、湖底(LakeFloor、水面下に完全に沈む高さ制限付き+微弱な青Point Light 1灯)、崖壁(CliffWall 210°/115°)、湖岸の岩場(Rock 108°/132°)へ、`CarryBuildTerrainForest.BuildAzureCrystals()` がRaycastベースの接地ルールで自動配置(計10個、鉱脈のように滝周辺へ集中配置、均等散布はしない)。
- **注記**: 分割時、モデル内に含まれていた微小な浮遊破片(259頂点)はRock型クリスタルへ結合して保持。Rock型のConvex MeshColliderはUnityの256ポリゴン制限により部分ハルで近似(装飾物のため実用上問題なし)。

## 8. Meshy AI 生成モデル ― Ancient Forest Guardian(太古の森の守護者、2026-08-14導入)

- **作成方法**: ユーザー自身がMeshyで作成した古木モデル。第三者配布物ではなくユーザー自身の生成物のため、外部配布ライセンスの確認対象ではないが、記録として残す。
- **格納場所**: `Assets/Stage/Forest/Trees/AncientForestGuardian/` 配下(元は `Assets/Stage/Forest/Trees/tree/` に生ファイル名のまま置かれていたものを`Carry/Setup Ancient Forest Guardian Tree`エディタスクリプトで整理・移動)。`Source/` にMeshyオリジナル(FBX + BaseColor/Normal/Metallic/Roughness 4枚、元ファイル名のまま無改変で保持)。`Textures/` に整理名のテクスチャコピー(`AncientForestGuardian_BaseColor/Normal/Metallic/Roughness.png`)+ 生成した `AncientForestGuardian_MetallicSmoothness.png`(URP Lit用にMetallic+Roughness→1枚へ合成)。`Materials/MAT_AncientForestGuardian.mat`(URP Lit)。`Prefabs/PF_AncientForestGuardian.prefab`。
- **ゲーム内での使用箇所**: 未配置(格納・マテリアル整備のみ完了、ステージへの配置は別途対応)。
- **注記**: 他のMeshy生成FBXと同様、インポート時に単位スケール差があり(ルートTransformが100倍スケール、メッシュ自体はネイティブでごく小さい)、配置時は他アセット同様スケール補正が必要。

## 運用ルール

1. 新しい外部アセットを追加したら、上記と同じ形式でこのファイルに追記する。
2. ライセンス条件(商用利用可否・クレジット表記要否・改変可否)を配布元ページで必ず確認してから追加する。
3. 出所不明・ライセンス不明のアセットはプロジェクトに含めない。
4. CC0でクレジット表記が「任意」のアセットも、敬意として上記のとおり作者・配布元を記録している。
