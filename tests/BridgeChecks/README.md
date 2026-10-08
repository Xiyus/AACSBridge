# Bridge contract checks

Run against the installed AutoAnthony, CombatSolver and game binaries. Configure
`Directory.Build.props` and `LocalDependencies.props` first; both stay local and are ignored by Git.
The checks do not execute a live combat or install the Mod.

From the repository root:

```powershell
dotnet run --project tests/BridgeChecks -p:SkipModDeploy=true -- `
  '<game>/data_sts2_windows_x86_64' `
  '<AutoAnthony DLL directory>' `
  '<CombatSolver DLL directory>' `
  '<RitsuLib>/compat/0.111.0' `
  '<RitsuLib>/shared' `
  '<RitsuLib>'
```

The trailing directories resolve runtime dependencies. Original binaries are loaded at runtime;
publicization only changes compile-time visibility.

The 2026-10-08 suite passes 159 checks covering supported/rejected RuntimeSpec shapes, installed
catalog admission, compatibility drift, actual mirror/Harmony registrations, fork isolation,
continuation fingerprints, post-play movement, temporary-power routing and random transformation routing.
These checks do not certify every whole-card combination or strict predicted/live combat equivalence.

Set `AA_BRIDGE_AUDIT_PATH` to a JSON output path to write the per-atom inventory after the checks pass.
The installed catalog exposes 467 atoms: 440 singleton operations are admitted. `--audit-only`
skips the tests and checks structural atoms with their necessary companions separately.
Do not interpret admission counts as combat-equivalence coverage.

To update the behavior baseline after reviewing a dependency change, set `AA_BRIDGE_BASELINE_PATH`
to `docs/compatibility-behavior-baseline.json` and add `--write-compat-baseline` before the dependency
arguments. Rebuild, then rerun the normal checks. Runtime and normal tests never refresh the baseline.

Build without deploying: `dotnet build -c Release -p:SkipModDeploy=true`.