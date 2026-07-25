# Fork customizations

This fork tracks upstream [VixenLights/Vixen](https://github.com/VixenLights/Vixen) and adds six
features. The base is the upstream tag **`DevBuild-1469`** (commit `b917b255`, 2026-07-23), which
this tree matches byte-for-byte before the feature commits.

## Branch layout

| Branch | Contents |
|---|---|
| `main` | `DevBuild-1469` + all six features merged + solution wiring. **Build this one.** |
| `feature/bezier-curves` | one feature, on top of `DevBuild-1469` |
| `feature/curve-gradient-editor` | " |
| `feature/lightning-effect` | " |
| `feature/video-export` | " |
| `feature/timecode-chase` | " |
| `feature/marquee-effect` | " |
| `vendor/3.12u6` | the four features as they existed on their **original** base (upstream tag `3.12u6`) |
| `vendor/1393` | timecode + marquee as they existed on their original base (tag `DevBuild-1393`) |

Each `feature/*` branch holds exactly one self-contained commit, so a feature can be cherry-picked
onto a future dev build on its own. The `vendor/*` branches preserve each feature against the
baseline it was actually written on, which is what makes those cherry-picks merge accurately.

To move onto a newer upstream build later:

```bash
git fetch upstream --tags && git checkout -b main-NNNN DevBuild-NNNN && git cherry-pick <each feature commit>
```

## The features

**Bezier curve handles** (`src/Vixen.Modules/App/Curves/`) — cubic-bezier control handles in the
curve editor: a new `BezierHandleData` model, handle hit-testing and dragging in the ZedGraph
`Line`/`PointPairList` code, plus curve evaluation and serialization changes.

**Curve and gradient editor UX** (`src/Vixen.Modules/App/ColorGradients/`,
`src/Vixen.Modules/Editor/EffectEditor/`) — audio waveform backdrop and mark overlays in the curve
popup, and rework of the gradient type editor, color gradient editor and gradient edit control.

**Lightning effect** (`src/Vixen.Modules/Effect/Lightning/`) — Basic-group effect: random lightning
flashes with an optional sequential bolt mode. *This module existed on disk in the old tree but was
never registered in `Vixen.sln`, so it never actually built or shipped. It is registered here.*

**Video export** (`src/Vixen.Modules/Editor/TimedSequenceEditor/VideoExport/`,
`src/Vixen.Common/ffmpeg/`) — renders a sequence to MP4 by capturing GDI preview frames and
encoding via ffmpeg. Rational framerate keeps A/V sync exact; encoder auto-detection covers
libx264, NVENC, QuickSync and AMF.

**MIDI Timecode chase** (`src/Vixen.Modules/Editor/TimedSequenceEditor/Timecode/`) — an armable
"Chase Timecode" mode that slaves playback to an external MTC master, supporting free jumping and
scrubbing. The MTC clock is a plain `ITiming` created only while armed — deliberately not a
discoverable timing module, so it can never be saved onto a sequence. Disarmed behavior is
unchanged. MIDI input is raw `winmm` P/Invoke.

**Marquee effect** (`src/Vixen.Modules/Effect/Marquee/`) — theater-marquee chase: a repeating
`[N on][M off]` pattern sliding along the prop, built for clean motion at very slow speeds. See
that folder's `README.md` for the full parameter reference.

## Deliberately not carried over

The old `Vixen-3.12u6` / `Vixen-Bezier-TC-Build` trees contained edits that worked around a machine
with no Visual Studio C++ toolchain. Those are **not** in this fork, because they break real
functionality:

- `Liquid.csproj`, `BeatsAndBars.csproj` — native `.vcxproj` references commented out
- `Timed.csproj`, `EffectEditor.csproj` — Liquid/Emitter project references removed
- `TimedSequenceMigrator.cs` — the `MigrateLiquidFrom7To8` sequence migration deleted
- `EditorCollection.cs`, `KnownTypes.cs` — Liquid emitter editor registrations commented out
- `Vixen-NoCpp.sln` — a stripped solution

If you were running a build from that tree, **the Liquid effect was disabled in it and the Liquid
7→8 sequence migration would not run**. Both work normally in this fork.

## Building

**Without Visual Studio** (dotnet SDK only):

```powershell
.\build.ps1
```

Then run `Debug\Output\Vixen.Application.exe`. This references prebuilt copies of the two C++/CLI
assemblies from `build-native\` rather than compiling them — see
[`build-native/README.md`](build-native/README.md) for why that's safe and when to refresh them.
Everything else is compiled from source.

**With Visual Studio**: open `Vixen.sln` (needs the C++ workload) and build `Debug|x64` as normal —
the `build-native` path is gated behind the `UsePrebuiltNative` property that only `build.ps1` sets,
so the Visual Studio build is completely unaffected.
