/// <summary>ポーション残量 (0..1) の供給元。
///
/// FLUID_DESIGN.md §17 のとおり、この値は必ず Fluid の状態から導出されたものでなければならず、
/// 独立に書ける変数であってはならない。実装は Phase 10 で FluidCore の上に作る。
/// このインターフェースは、Phase 1〜9 の間 PotionGaugeUI をコンパイル可能に保つために置いてある。</summary>
public interface IPotionVolumeSource
{
    float FillFraction01 { get; }
}
