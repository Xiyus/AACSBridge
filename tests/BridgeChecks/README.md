# Bridge contract checks

Runs against the installed AutoAnthony / CombatSolver / game binaries. Requires the bridge's
`Directory.Build.props` and `LocalDependencies.props`. Does not execute a live combat or install the Mod.

```powershell
dotnet run --project tests/BridgeChecks/BridgeChecks.csproj -p:SkipModDeploy=true -- `
  '<game>/data_sts2_windows_x86_64' `
  '<AutoAnthony DLL directory>' `
  '<CombatSolver DLL directory>' `
  '<RitsuLib>/compat/0.111.0' `
  '<RitsuLib>'
```

The trailing directories resolve runtime dependencies, including RitsuLib when the complete solver
assembly is inspected. Only the original binaries are loaded at runtime; publicization remains a compile step.

Checks include supported and rejected trigger/payload shapes, installed component-catalog coverage,
the exact binary hash guard with a negative control, lifecycle seam signatures, deep fork isolation,
hidden-state fingerprints, exact continuation text, active-trigger fork refusal, registry installation
and actual Harmony patch targets. They do not certify strict predicted/live combat equivalence.

The independent-template regression checks reject incomplete upgrade / autoplay / optional-exhaust
effects, verify that a supported condition cannot bypass payoff validation, and check the installed
Tempest output-slot contract (five fixed orb types, random, and the legacy default).
The latest 2026-10-07 run passed 117 checks, including exact-one-skill draw-state transitions,
draw-event ordering/filtering, unwired-trigger and approximation rejection, standalone proxy-rule
admission, and first-status flag fork/fingerprint/continuation isolation. Batch AR adds resolved-cost
trigger admission, lifetime/slot/dynamic-value rejection, captured-threshold fork isolation and an
executable BeforeCardPlayed registration check. Batch AO/AP admission assertions now reflect the
current implementation; these assertions do not certify those effects' combat equivalence.
Immediate poison, retain, free-skill and proxy combat behavior
still need live strict-diff fixtures; support/slot checks are not combat equivalence tests.

Set `AA_BRIDGE_AUDIT_PATH` to an output JSON path to emit the per-atom catalog audit after all
checks pass. It records dependency hashes and singleton-operation acceptance/rejection, not
whole-card coverage. The current installed catalog exposes 467 atoms; this denominator is not
interchangeable with the historical 931-entry report.

For a build without installing the Mod: `dotnet build -p:SkipModDeploy=true`.
