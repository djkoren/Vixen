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
`Line`/`PointPairList` code, plus curve evaluation and serialization changes. Right-clicking a point
adds or removes its handles, which is now spelled out in the dialog's instruction lines. Removing the
last handle on a curve used to leave the handles on screen until some later event forced a redraw —
the "nothing happened" was a missing invalidate on an early-return path, now repainted
unconditionally.

**Curve and gradient editor UX** (`src/Vixen.Modules/App/ColorGradients/`,
`src/Vixen.Modules/Editor/EffectEditor/`) — audio waveform backdrop and mark overlays in the curve
popup, and rework of the gradient type editor, color gradient editor and gradient edit control.
Marks draw in light yellow and both editors carry a **Hide Marks** toggle (curve editor: under
Draw Interval; gradient editor: beside Delete). The two share one persisted setting via
`Common.Controls.MarkOverlayPreferences`, so the choice follows you between editors and across
restarts. Marks are shown by default.

**Lightning effect** (`src/Vixen.Modules/Effect/Lightning/`) — Basic-group effect: random lightning
flashes with an optional sequential bolt mode. *This module existed on disk in the old tree but was
never registered in `Vixen.sln`, so it never actually built or shipped. It is registered here.*
Its toolbox icon is a lightning bolt on a night sky, replacing the placeholder grey sphere.

**Video export** (`src/Vixen.Modules/Editor/TimedSequenceEditor/VideoExport/`,
`src/Vixen.Common/ffmpeg/`) — renders a sequence to MP4 by capturing GDI preview frames and
encoding via ffmpeg. Rational framerate keeps A/V sync exact; encoder auto-detection covers
libx264, NVENC, QuickSync and AMF.

**MIDI Timecode chase** (`src/Vixen.Modules/Editor/TimedSequenceEditor/Timecode/`) — an armable
"Chase Timecode" mode that slaves playback to an external MTC master, supporting free jumping and
scrubbing. The MTC clock is a plain `ITiming` created only while armed — deliberately not a
discoverable timing module, so it can never be saved onto a sequence. Disarmed behavior is
unchanged. MIDI input is raw `winmm` P/Invoke.

The status readout is `HH:MM:SS:FF` at the incoming frame rate (29.97 uses proper drop-frame
numbering) and is repainted from a 40 Hz UI timer on a fixed-width label, so the frame field ticks
smoothly instead of stuttering at the controller's status interval. Changing the device, frame-rate
mode or freewheel window in **TC Settings** now rebuilds the decoder immediately rather than waiting
for the next arm — that stale-decoder bug is why a forced frame rate appeared to be ignored. In
auto-detect mode the rate reads `--` until a whole timecode word has actually been decoded. The two
toolbar buttons are icon-only (clapper board; clapper board with a gear), matching the rest of the
playback strip.

**Marquee effect** (`src/Vixen.Modules/Effect/Marquee/`) — theater-marquee chase: a repeating
`[N on][M off]` pattern sliding along the prop, built for clean motion at very slow speeds. See
that folder's `README.md` for the full parameter reference.

**Advance By** (persisted as `FadeGroup`) is a discrete step of N LEDs that moves and switches as one
unit, and the **Fade** curve is read straight across an LED's journey through a lit group — 0 when it
lights, 1 just before it goes dark — with nothing layered on top, so a rising line ramps up and snaps
off and a curve peaking in the middle fades both ways. Everything is laid out on the step grid, which
is what keeps every group at the same point in the fade (otherwise a chase appears to run *through*
the pattern) and keeps exactly `Lights On` LEDs lit at every instant. **Randomness** displaces whole
groups rather than individual LEDs, bounded by the gap so groups can never merge. **Fit To Element**
(new, off by default) spreads the pattern so a whole number of groups spans the element, giving even
spacing and a seamless wrap.

**Ripples** (new, off by default, just a count and a speed) send pulses along the groups: each one
carries a group forward one step, easing across it, and the group then holds until the next ripple
reaches it. That stepping is real movement, so with Speed at 0 the ripples are the only motion and a
group genuinely sits still between shoves rather than gliding through the pause. Randomness and the
ripples share one gap budget that guarantees a dark LED always survives between groups.

⚠️ **The Fade curve decides whether smooth movement is visible at all.** Which LEDs are lit can only
change a whole LED at a time, so sub-LED motion only reaches the prop as one LED ramping up while
another ramps down — and that ramp is the Fade curve. Under the default flat 100 (hard bulbs) every
ripple lands as a hard one-LED jog however smoothly it flows. A rounded V is what makes the ripples
read as designed. This is true of plain Speed movement too and is not specific to ripples.

An **Animate In/Out** group (new, `None` by default) animates the pattern on and off from inside the
effect, which a layered overlay cannot do — an overlay only multiplies brightness and has no idea where
the groups are. Four modes: `Slide` (the pattern translates onto the prop from the chosen end, a sheet
an element long displaced right off it at the extremes of the curve), `Dissolve` (whole groups in a
scattered order), `Stack` (groups slide in one at a time and pile against the far end, with a Stack
Curve shaping the travel, and whatever has landed keeps running the effect) and `Scale` (each group
narrows from its edges below 50, hollows from its centre above 50). One curve drives it, with **50
meaning fully assembled**: below 50 is arriving, above 50 is leaving by the opposite route, so a single
animator covers both and can do it any shape, anywhere in the effect. At exactly 50 every mode is
byte-identical to `None`.

Slide and Stack are laid out in a frame that travels with the scroll, so a curve held part way does not
park them: the stretch that has arrived keeps circling the prop, carrying its entry point round with it,
and a part-built stack travels as one rigid piece with the rest dropping in behind it whenever the curve
moves on. For Slide that is also what separates it from a wipe — window and pattern move at identical
speeds, so pattern is only ever added at the entry edge. For Stack it is what stopped groups popping in
and out once Speed was above zero. At Speed 0 both are stationary and fill from the end you picked.

**Motion Blur** (new, `0` by default, Slide and Stack only) is a shutter angle in degrees read exactly
as on a film camera — 0 closed, 180 the usual cinema look, 360 open for the whole frame. It is a real
exposure, not a smudge: the frame is rendered at up to 16 sub-frame instants and averaged, so a group
flying into a stack streaks rather than stepping. Because it is an exposure it blurs the ordinary scroll
and the ripples too, which is why a blurred render is never byte-identical to an unblurred one.

**Bad Bulbs** (new, 0 by default) blows a given number of LEDs on the element so they never light, like
an old sign. A seed picks which — no other Vixen effect exposes one, and it is deliberate: it stops the
arrangement silently reshuffling every time an unrelated property is edited, which is the trap both
Dissolve and Lightning have.

A new Marquee defaults to Horizontal, Lights On 1, Lights Off 3, Advance By 1 and a flat 100 Fade, with
Animation and Bad Bulbs off. See the module README for those and for tested recipes.

These are behavior changes: a saved sequence with `Advance By > 1` or `Randomness > 0` will look
different from how it looked before, any sequence whose Fade curve is not symmetrical will render
that curve differently, and above `Advance By = 1` Lights On and Lights Off are rounded to whole
steps.

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
