# Prebuilt native assemblies

These exist so the project can be built **without Visual Studio / the MSVC C++ toolset**, via
[`build.ps1`](../build.ps1). They are not used by a normal Visual Studio build, which compiles the
`.vcxproj` projects from source as usual — the switch is the `UsePrebuiltNative` MSBuild property,
which only `build.ps1` sets.

| File | Source |
|---|---|
| `QMLibrary.dll` | C++/CLI output of `src/Vixen.Modules/Analysis/QMLibrary/QMLibrary.vcxproj` |
| `Module.Effect.LiquidLiquidFunWrapper.dll` | C++/CLI output of `src/Vixen.Modules/Effect/Liquid/LiquidFunWrapper/LiquidLiquidFunWrapper.vcxproj` |
| `Ijwhost.dll` | .NET's C++/CLI (IJW) host shim, deployed alongside mixed-mode assemblies |

All three were taken from the official upstream **DevBuild-1393** binaries. That is safe for the
current base (`DevBuild-1469`): `git diff DevBuild-1393..DevBuild-1469` over the three native
project folders shows **no C++ source changes at all** — only line-ending normalisation in the
`.vcxproj` files. `Box2D.vcxproj` produces no managed assembly and is linked into the LiquidFun
wrapper, so it needs no copy here.

## When these go stale

Refresh them if upstream ever changes anything under `src/Vixen.Common/Box2D/`,
`src/Vixen.Modules/Analysis/QMLibrary/`, or `src/Vixen.Modules/Effect/Liquid/LiquidFunWrapper/`.
Check with:

```bash
git diff --stat -w <old-tag>..<new-tag> -- src/Vixen.Common/Box2D src/Vixen.Modules/Analysis/QMLibrary src/Vixen.Modules/Effect/Liquid/LiquidFunWrapper
```

If that reports real changes, replace these files with the `Debug\Output` copies from the matching
upstream DevBuild download (or from any machine with the C++ workload installed).

`Ijwhost.dll` must match the .NET major version the app targets.
