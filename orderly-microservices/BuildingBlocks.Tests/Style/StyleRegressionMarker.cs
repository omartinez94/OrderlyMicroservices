// --------------------------------------------------------------
// StyleRegressionMarker.cs — Phase 5 regression check
// --------------------------------------------------------------
//
// This file INTENTIONALLY violates IDE0008 (var → explicit) and
// IDE0011 / SA1503 (braces required). It is guarded by
// `#if STYLE_GATE_TEST` so it does not pollute daily IDE warnings or
// the build's warning count.
//
// To prove the analyzer gate works, run:
//
//   dotnet build orderly-microservices.slnx \
//       -warnaserror:IDE0008,IDE0011,SA1503 \
//       -p:DefineConstants="STYLE_GATE_TEST" \
//       --no-restore
//
// If the analyzer gate is wired correctly, the build FAILS with
// errors on the violations below. If the gate is broken (e.g. the
// -warnaserror flags aren't honoured, or the IDE0xxx rules are
// disabled in .editorconfig), the build succeeds and this regression
// check has lost its bite — fix the gate, not this file.
//
// Do NOT delete this file — it is the tripwire for the Phase 5
// verification per
// .agents/plan/platform/CODE_STYLE_ITERATION_PLAN.md §Phase 5.
// --------------------------------------------------------------

namespace BuildingBlocks.Tests.Style;

internal static class StyleRegressionMarker
{
    // IDE0008 violation: var where the type is non-anonymous.
    // (Phase 3's `var` carve-outs explicitly exclude this shape.)
    public static int UsesVarWithoutCarveout()
    {
        var x = 42;
        return x;
    }

    // IDE0011 / SA1503 violation: braceless if (single-line).
    // Phase 1 added braces everywhere; this regression guard
    // verifies the analyzer still flags new occurrences.
    public static int HasBracelessIf(int n)
    {
        if (n > 0) return n;
        return 0;
    }
}
