# Bridge 0.6 contract checks

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

For a build without installing the Mod: `dotnet build -p:SkipModDeploy=true`.
