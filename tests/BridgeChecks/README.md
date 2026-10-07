# Bridge contract checks

Runs against the installed AutoAnthony / CombatSolver / game binaries. Requires the bridge's
`Directory.Build.props` and `LocalDependencies.props`. Does not execute a live combat or install the Mod.

```powershell
dotnet run --project tests/BridgeChecks/BridgeChecks.csproj -p:SkipModDeploy=true -- `
  '<game>/data_sts2_windows_x86_64' `
  '<AutoAnthony DLL directory>' `
  '<CombatSolver DLL directory>' `
  '<RitsuLib>/compat/0.111.0' `
  '<RitsuLib>/shared' `
  '<RitsuLib>'
```

The trailing directories resolve runtime dependencies, including RitsuLib when the complete solver
assembly is inspected. Only the original binaries are loaded at runtime; publicization remains a compile step.

After reviewing a dependency behavior change, explicitly generate a new baseline with
`AA_BRIDGE_BASELINE_PATH` pointing to `docs/compatibility-behavior-baseline.json` and
`--write-compat-baseline` before the dependency-directory arguments. Rebuild, then run the checks.
This maintenance command is never invoked by runtime or the normal tests.

Checks include supported and rejected trigger/payload shapes, installed component-catalog coverage,
the behavior-contract guard with drift/missing-member/addition controls, lifecycle seam signatures, deep fork isolation,
hidden-state fingerprints, exact continuation text, active-trigger fork refusal, registry installation
and actual Harmony patch targets. They do not certify strict predicted/live combat equivalence.

The independent-template regression checks reject incomplete upgrade / autoplay / optional-exhaust
effects, verify that a supported condition cannot bypass payoff validation, and check the installed
Tempest output-slot contract (five fixed orb types, random, and the legacy default).
The latest 2026-10-07 run passed 134 checks, including exact-one-skill draw-state transitions,
draw-event ordering/filtering, unwired-trigger and approximation rejection, standalone proxy-rule
admission, and first-status flag fork/fingerprint/continuation isolation. Batch AR adds resolved-cost
trigger admission, lifetime/slot/dynamic-value rejection, captured-threshold fork isolation and an
executable BeforeCardPlayed registration check. Batch AO/AP admission assertions now reflect the
current implementation; these assertions do not certify those effects' combat equivalence.
Batch AS adds self-exhaust identity, linked-payoff admission and malformed/recursive-payoff rejection,
concrete-card AfterDeath registration, prevented-removal filtering, and the solver exhaust Harmony seam.
The live evidence records matching scalar replays but a full continuation mismatch in death-triggered
card cost; these checks do not certify the new cost fix or exhaust effects against live combat.
Immediate poison, retain, free-skill and proxy combat behavior
still need live strict-diff fixtures; support/slot checks are not combat equivalence tests.

Set `AA_BRIDGE_AUDIT_PATH` to an output JSON path to emit the per-atom catalog audit after all
checks pass. It records dependency hashes and singleton-operation acceptance/rejection, not
whole-card coverage. The current installed catalog exposes 467 atoms; this denominator is not
interchangeable with the historical 931-entry report.

The implementation sweep retains 134 checks after replacing obsolete negative admission assertions
with checks for the new executable paths and adding slot/provenance fork-remapping controls.
`--audit-only` bypasses the tests and emits an implementation inventory. Its companion audit checks
structural atoms with a legal adjacent or linked payoff and records that separately from singleton
acceptance: currently 440/467 singleton atoms and 467/467 with their required companions.
It does not certify arbitrary whole-card combinations or live combat equivalence.

For a build without installing the Mod: `dotnet build -p:SkipModDeploy=true`.
