# GPU readiness

`Utils/GpuReadiness.cs` — `FractalAnimator.KernelReadiness`

This is **not** a GPU port and contains no device code. It is the CPU-side prerequisite gate that the
design puts in front of all GPU work, plus a permanent regression gate for the precision ladder itself.

## 0. Why a gate exists at all

Every error-free transformation in `FloatFloat`, `DoubleDouble` and `QuadDouble` rests on one identity:

```
TwoProd(a, b):  p = a * b
                e = fma(a, b, -p)      // e is the exact rounding residual of the product
```

If a backend lowers `Math.FusedMultiplyAdd` / `MathF.FusedMultiplyAdd` to a separate multiply and add,
then `e = (a*b) - (a*b) = 0` for **every** input. No exception, no NaN, no crash. `FloatFloat` degrades
to `float`, `DoubleDouble` degrades to `double`, `QuadDouble` degrades to `double` — while
`CertifiedIterator` keeps charging `T.RelativeUlp` at 3e-15, 1e-31 and 1e-62 and keeps stamping pixels
as certified. The image still looks like a fractal. That is the entire reason the microbenchmark gates
the port: the defect is invisible in the output.

The same class of silent failure applies to the `RelativeUlp` constants themselves. They are not
documentation — `CertifiedIterator.cs:87-99` charges `CoupledArithmeticUlps * rungUlp` into the running
error every iteration, `:105-108` folds it into `displayError`, `:114-117` derives `escapeUncertainty`
from it, and `PowerJuliaAtom.cs:25-27` derives the coupled/decoupled threshold from
`pow(RelativeUlp, 2/3)`. A claimed ulp **tighter** than the arithmetic actually delivers makes every
certificate on that rung formally unsound, and biases the scheduler to keep pixels on a rung that has
not earned them.

## 1. What must hold for an ILGPU port to be sound

| # | Precondition | How it fails silently |
|---|---|---|
| P1 | `fma(a,b,c)` compiles to a single fused instruction with one rounding | Residual is identically 0; all rungs collapse to their high limb |
| P2 | The residual is not merely non-zero but **bit-exact**: `fl(a*b) + fma(a,b,-fl(a*b))` reproduces the exact product | A "nearly fused" implementation yields a plausible but wrong low limb |
| P3 | The `TwoSum` expression `(a - (s - bb)) + (b - bb)` is evaluated **as written** | Contraction or reassociation kills the compensation with `fma` untouched |
| P4 | No FTZ / denormal flushing on the low limbs | The lowest limb of a deep-zoom value silently becomes 0; the certificate does not notice |
| P5 | Round-to-nearest-even, no alternate rounding mode | Every ulp bound in the ladder is derived for RNE |
| P6 | `T.RelativeUlp` is an **upper** bound on the worst-case relative error of every operation the step uses | Certificate claims precision the rung does not deliver |
| P7 | The device transcendental library is at least as accurate as the host one for `Exp`, `Log`, `SinCos`, `Atan2` (`Fp32`/`Fp64` call them directly; `FF32`/`DD`/`QD` seed their Newton loops from them) | Rung ulp changes without the constant changing |
| P8 | The frontend can compile static abstract interface members, so `IPrecision<T>` monomorphises per rung | Hard compile failure, not silent — but it blocks the whole architecture |
| P9 | The frontend can compile `ValueTuple` returns and `stackalloc` / `Span<double>` | `SinCos`, every `TwoSum`/`TwoProd` helper, and the limb decomposition depend on them |

P1, P2, P3, P6 are the silent ones. P8 and P9 are loud but architectural.

## 2. What this harness checks automatically

All three checks run on the CPU, against `BigDecimalCompat` references built from the **exact binary
value** of each operand (`FromDoubleExact`), never from decimal text.

### Check 1 — the multiply-add really is fused (P1, P2)

For random normals with full 53-bit / 24-bit mantissas, it first decides **independently** whether the
exact product is representable (by comparing the exact product against the exact value of `fl(a*b)` in
`BigDecimalCompat`), and only then requires:

- the residual is non-zero for every non-representable product — reported as a fraction, so a broken
  backend reads as `nonzero=0.0000%` rather than as a bare pass/fail;
- the residual is **bit-exact**, i.e. `FromDoubleExact(residual)` compares equal to
  `exact(a*b) - exact(fl(a*b))`.

Both `double` and `float` are covered.

### Check 2 — the error-free transformations reconstruct (P1, P2, P3)

For `FloatFloat`, `DoubleDouble` and `QuadDouble`, single-limb operands are fed through the public
`Mul` and `Add`, which routes them through the type's private `TwoProd` / `TwoSum`. For single-limb
inputs the exact product (48 / 106 / 106 bits) and the exact sum are both representable in the target
type, so `hi + lo` must equal the exact value **bit for bit**, not approximately.

One methodological note that matters: this comparison is carried at 512 digits, not 60. The exact
product of two doubles carries only ~32 significant digits of *value*, but its decimal expansion runs
to ~105 digits (`2^-104 = 5^104 * 10^-104`, 73 digits, times a 32-digit integer). Judging at 60 digits
would round the reference and the "exact" comparison would silently become approximate. The 60-digit
context is retained only for the reported error ratio.

### Check 3 — the declared `RelativeUlp` is an upper bound (P6, P7)

For each rung on the ladder, the worst-case relative error is measured over `Add`, `Sub`, `Mul`, `Div`,
`MulByDouble`, `Sqrt` and the six transcendentals the decoupled step calls (`Exp`, `ExpM1`, `Log`,
`LogP1`, `SinCos`, `Atan2` — see `PowerJuliaAtom.Step` lines 70-86 and `Cx<T>.Pow`).

Three details make the measurement trustworthy:

1. **Operands are full width.** `T.FromDouble(x)` produces a *single-limb* value for `DoubleDouble` and
   `QuadDouble`, and multiplying two single-limb double-doubles is exactly the `TwoProd` special case —
   error zero. Measuring that way understates DD and QD by ~30 orders of magnitude. Operands are
   therefore composed as `x + x*2^-53*r1 + x*2^-106*r2 + x*2^-159*r3` so every limb is populated.
2. **The reference is built from the rung's own value**, never from the source double. Referencing
   `exp` of the *double* instead of the exact value of the `FloatFloat` the routine actually received
   contributes `|a|*2^-49` of pure conversion error and fabricates defects — the trap already
   documented in `SelfTest.DoubleDoubleCheck`.
3. **Reference precision adapts to the claim**: `ceil(-log10(RelativeUlp)) + 30` digits, plus 40 more
   for the cancellation-carrying references (`ExpM1` as `exp(x)-1`, `LogP1` as `ln(1+x)`), so a QD
   claim of 1e-62 is judged at 92/132 digits.

Two normalisation choices are documented rather than hidden. `SinCos` error is divided by 1 (the true
`(sin, cos)` pair has magnitude 1, and a near-zero sine is ill-conditioned in the relative sense while
the kernel consumes the pair as a unit vector). `Atan2` error is divided by `max(|true|, 1)`, because
`theta` is consumed additively. Everything else, `Log` included, uses strict relative error — no
exemption was carved out to make a claim pass.

## 3. Measured results

Host: 13th Gen Intel Core i7-13700KF, 24 logical cores, Windows 11, .NET SDK 10.0.301, x64 Release.
Command: seed 20260730, 16384 fusion samples, 16384 reconstruction samples, 8192 ulp samples per
operation. Runtime ~35 s.

### Checks 1 and 2

| check | result |
|---|---|
| `double` fma | 16384/16384 products inexact, **100.0000%** non-zero residual, **100.0000%** bit-exact — PASS |
| `float` fma | 16384/16384 products inexact, **100.0000%** non-zero residual, **100.0000%** bit-exact — PASS |
| FF32 TwoProd / TwoSum | 16384/16384 bit-exact reconstruction — PASS |
| DD TwoProd / TwoSum | 16384/16384 bit-exact reconstruction — PASS |
| QD TwoProd / TwoSum | 16384/16384 bit-exact reconstruction — PASS |

The x64 JIT emits a true `vfmadd` for both widths. This is the baseline every GPU backend must match.

### Check 3 — claimed vs measured relative ulp

| rung | claimed | measured worst | overrun | worst operation | verdict |
|---|---|---|---|---|---|
| FP32 | 6.000e-08 | 5.954e-08 | 0.99x | `ExpM1` / `LogP1` | sound, **0.8% margin** |
| FF32 | 3.000e-15 | 1.010e-11 | **3368x** | `LogNearOne` | **UNSOUND** |
| FP64 | 1.100e-16 | 3.052e-16 | **2.77x** | `LogP1` | **UNSOUND** |
| DD | 1.000e-31 | 2.694e-28 | **2694x** | `LogNearOne` | **UNSOUND** |
| QD | 1.000e-62 | 1.820e-64 | 0.02x | `Exp` | sound, 55x headroom |

`LogNearOne` is a deterministic adversarial probe at `a = 1 +/- 10^-k`, `k = 1..4`. Excluding it, so the
ordinary operating range can be read separately:

| rung | worst excluding `LogNearOne` | overrun | operation |
|---|---|---|---|
| FP32 | 5.954e-08 | 0.99x | `ExpM1` at a = -1.1925021814942798e-07 |
| FF32 | 4.749e-14 | 15.83x | `Log` at a = 0.86049612824066379 |
| FP64 | 3.052e-16 | 2.77x | `LogP1` at a = 0.0039271492444197037 |
| DD | 1.518e-31 | 1.52x | `Log` at a = 0.87607159190757566 |
| QD | 1.820e-64 | 0.02x | `Exp` at a = 8.6945950699479297 |

## 4. Findings

### F1 — FP64 is unsound on plain addition, by theorem not by sampling

`Fp64.RelativeUlp = 1.1e-16` is below `2^-53 = 1.1102230246251565e-16`. Measured: `Add` 1.107e-16
(1.01x), `Sub` 1.107e-16, `MulByDouble` 1.101e-16, `Div` 1.098e-16, `Sqrt` 1.095e-16 — a whole cluster
sitting at 0.99-1.01x the claim, exactly where correctly-rounded IEEE arithmetic must sit. No sample
size changes this: the supremum of the relative error of a correctly-rounded double operation is
`u/(1+u) = 1.11022e-16 > 1.1e-16`. On top of that, `LogP1` measures 3.052e-16 (2.77x) and `ExpM1`
2.811e-16 (2.56x), because `NativeTranscendental` applies a Kahan correction whose two roundings
compound. A sound constant is **4e-16** — 1.31x over the measured 3.052e-16 worst, in line with the
headroom policy `DoubleDouble.cs` states in prose. 1.2e-16 is the bare minimum that makes it an upper
bound for pure arithmetic alone and would still be violated by `LogP1`.

### F2 — FF32 is unsound on ordinary multiplication

`FloatFloat.RelativeUlp = 3e-15` is below one representable ulp of the format
(`2^-48 = 3.5527e-15`), so it cannot be an upper bound for anything. Measured across the ordinary
range: `Log` 15.83x, `LogP1` 6.52x, `ExpM1` 4.47x, `MulByDouble` 3.91x, `Mul` 3.30x, `SinCos` 3.04x,
`Atan2` 2.47x, `Add` 2.33x, `Sqrt` 2.29x, `Div` 2.18x, `Exp` 1.70x, `Sub` 1.67x. **Every single
operation overruns.** The constant should not be re-derived until F3 is fixed, because `Log` dominates
the measurement and F3 changes `Log`. With F3 fixed and the rung re-measured, the floor is the current
ordinary-range worst of 4.749e-14, so **1e-13** (2.1x) is the shape of the answer. What is certain
without any further work is that 3e-15 is indefensible under every reading: it is below one
representable ulp of the format, so no implementation of any quality could satisfy it.

### F3 — `Log` near unity is unbounded on DD and FF32, and it is reachable

This is the sharpest finding and it is new. `QuadDouble.Log` routes near-unity arguments into `LogP1`:

```
var offset = Sub(a, One);
if (Math.Abs(offset.X0) < 0.25) return LogP1(offset);
```

`DoubleDouble.Log` and `FloatFloat.Log` do not. They Newton-iterate on a correction term of size O(1),
so their **absolute** error stays at ~2 ulp of 1 while the result goes to zero — the relative error
diverges as `a -> 1`. Measured against 80-digit references built from the exact binary value:

| a | `DD.Log` | `DD.LogP1(a-1)` | `QD.Log` | `FF32.Log` | `FF32.LogP1(a-1)` |
|---|---|---|---|---|---|
| 0.99 | 3.135e-31 | 6.939e-33 | 6.446e-66 | 1.443e-13 | 2.516e-14 |
| 0.999 | 7.396e-30 | 5.940e-33 | 9.945e-66 | 2.525e-13 | 3.322e-13 |
| 1.001 | 4.459e-31 | 3.289e-34 | 2.113e-67 | 2.846e-12 | 1.329e-12 |
| 1.5 | 1.925e-32 | 1.925e-32 | 3.085e-65 | 5.547e-16 | 5.547e-16 |
| 10 | 6.370e-33 | 6.370e-33 | 8.377e-66 | 4.800e-16 | 4.800e-16 |

At `a = 0.999`, `DoubleDouble.Log` delivers 7.4e-30 against a claim of 1e-31 — 74x — while routing the
same argument through the type's own `LogP1` gives 5.9e-33, three orders better. `QuadDouble` is
unaffected precisely because it routes. The fix is to copy `QuadDouble.Log`'s first two lines into
`DoubleDouble.Log` and `FloatFloat.Log`.

Reachability: `Cx<T>.Pow` (`IPrecision.cs:71`) computes `T.Log(Re*Re + Im*Im)`, i.e. `log(|z|^2)`, and
`PowerJuliaAtom.Step` takes that branch whenever the accumulated angle wraps past `+/-pi`. `|z| ~ 1` is
an entirely ordinary orbit position — the bailout is `|z|^2 <= 1024`.

**Caveat, stated so it is not overclaimed.** In `Cx<T>.Pow` the result `logR` is scaled and then
exponentiated, so what propagates downstream is `logR`'s *absolute* error, which stays at ~2 ulp. The
`LogNearOne` overrun is therefore a genuine defect of the *primitive* — `RelativeUlp` is a relative
claim and makes no exemption for arguments near 1 — but its kernel-level consequence is bounded by the
call site. F1 and F2 have no such escape: they are relative errors on ordinary arithmetic that the
per-step charge applies to directly.

### F4 — FP32 passes with 0.8% margin, and that margin is the GPU risk

`Fp32.RelativeUlp = 6e-8` versus `2^-24 = 5.96046e-8`. Measured worst 5.954e-08 (0.99x), with twelve of
thirteen operations landing between 0.96x and 0.99x (only `SinCos`, at 0.52x, is not pressed against
the ceiling). The claim is sound only because every host CRT
entry point (`MathF.Exp`, `MathF.Log`, `MathF.SinCos`, `MathF.Atan2`) happens to be within half an ulp.
A device math library at 1.01 ulp breaks FP32 immediately. Since the measured deep-frame ladder retires
**100% of pixels on FP32** at zoom 120/200/300, this is the single most consequential row in the table
for a GPU port, and it must be re-measured on-device before any deep frame is trusted.

### F5 — QD has 55x headroom and DD has 1.5x over its ordinary range

`QuadDouble` at 0.02x is the only rung with comfortable margin. `DoubleDouble` excluding `LogNearOne`
is at 1.52x (`Log`) and 0.97x (`LogP1`), so even setting F3 aside, `1e-31` must become at least
**2e-31** (1.32x over the measured 1.518e-31).

## 5. What remains unverifiable until real GPU hardware runs it

Nothing in section 3 transfers to a device. These are the items the harness explicitly cannot decide,
and they are also printed as section 4 of the harness's own output so they travel with the result:

1. **PTX / SPIR-V emission of `fma.rn`.** Only a device compile plus a disassembly of the generated
   kernel can prove the backend keeps the fused form. Host `vfmadd` says nothing about it. The
   recommended device-side check is to run check 1 verbatim in a kernel and read back the non-zero
   fraction — a lowered fma reports 0.0000%.
2. **FTZ / denormal flushing.** GPUs commonly flush subnormal inputs and results to zero. That zeroes
   `TwoProd` residuals near the bottom of the exponent range, which is exactly where a deep-zoom delta
   lives after `ScaleB`. Untestable on x64, where denormals are honoured.
3. **Contraction and reassociation of the `TwoSum` expressions.** `s - a`, `(a - (s - bb)) + (b - bb)`
   are error-free only when evaluated exactly as written. A backend that contracts or reassociates them
   destroys the compensation while leaving `FusedMultiplyAdd` alone, so check 1 would still pass and
   check 2 would fail — which is why check 2 exists separately.
4. **Alternate rounding modes.** The ladder's bounds assume round-to-nearest-even throughout.
5. **ILGPU frontend support for static abstract interface members.** The whole ladder is monomorphised
   through `IPrecision<T>`; if the frontend cannot resolve `T.Add` / `T.RelativeUlp`, the kernel does
   not compile at all and the architecture needs a different dispatch strategy.
6. **ILGPU frontend support for `ValueTuple` returns.** `SinCos` and every `TwoSum` / `TwoProd` helper
   returns a tuple.
7. **ILGPU frontend support for `stackalloc` / `Span<double>`.** Used by the limb decomposition
   (`BigDecimalCompat.ToLimbs`, `PowerJuliaBaker.Narrow<T>`).
8. **Device transcendental accuracy.** `Fp32` and `Fp64` call the host CRT directly and `FF32` / `DD` /
   `QD` seed their Newton loops from it. Every number in section 3's check-3 table is a property of the
   host math library as much as of this codebase, and must be re-measured on-device. See F4.
9. **Warp divergence cost of the ladder scheduler.** Not a correctness question, but the "FP32 retires
   100% of deep-frame pixels" measurement does not transfer to a warp-scheduled backend.

## 6. Running it

```csharp
var report = KernelReadiness.Run();                       // full audit, ~7 s at the 1024-sample default
Console.WriteLine(KernelReadiness.Describe(report));
if (!report.Passed) { /* unsound */ }

if (!KernelReadiness.TryGate(out var text)) { /* fusion + reconstruction only, sub-second */ }
```

`ReadinessScope.FusionGate` runs checks 1 and 2 only and is cheap enough to sit on a startup path as a
permanent gate. `ReadinessScope.Full` adds check 3. The seed is fixed, so a given sample count is
reproducible; `LogNearOne` is deterministic and independent of the seed, so F3 is caught at any sample
count.
