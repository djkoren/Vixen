# Marquee Effect

An old‑fashioned theater‑marquee chase for pixel props: a repeating pattern of
`Lights On` lit LEDs followed by `Lights Off` dark LEDs slides smoothly along the
element. It was built specifically to give **clean, smooth motion at very slow
speeds** — the thing the Bars effect does poorly — and to give each bulb a warm
incandescent fade in and out.

It is a pixel effect (`PixelEffectBase`) and works in both **String** and
**Location** (matrix / megatree) target modes.

## Defaults

A freshly dropped Marquee starts as the plainest readable pattern, meant to be widened from rather
than trimmed down:

| | |
| --- | --- |
| Orientation | Horizontal |
| Lights On / Lights Off | `1` / `3` |
| Advance By | `1` |
| Fade | flat 100 (hard bulbs) |
| Speed | flat 50 |
| Fit To Element, Randomness, Ripples | off |

## Recipes

**Smooth shuffling crawl** (the one to reach for first)

| | |
| --- | --- |
| Fade | **a rounded V — up to 100 in the middle, 0 at both ends** |
| Speed | `0` (the ripples do the moving) |
| Ripples | `2` |
| Ripple Speed | `40`–`55` |
| Lights On / Off | `1` / `5` |

The Fade curve is the part that is easy to miss and the part that matters most — see the warning
under Config. Flat 100 will make this land as hard one‑LED jogs no matter what else is set.

**Classic marquee chase** — leave Fade flat 100, Ripples `0`, and drive it with Speed alone.

**Organic / alive** — the smooth crawl above plus Randomness `20`–`40` for irregular spacing on top
of the shuffle.

**Even spacing on an odd‑length prop** — turn on Fit To Element; Lights Off becomes the minimum gap.

**Animate on, hold, animate off** — Animation `Slide`, and an Animation Curve that ramps `0 → 50` over
the first stretch, holds flat at `50` through the middle, then ramps `50 → 100` at the end. The
pattern slides in, runs normally, and slides out the far side.

**A sign warming up** — Animation `Dissolve` with the curve ramping `0 → 50`, plus **Bad Bulbs** at 3–6.
Groups flicker on in a scattered order and a few never do.


## Controls

### Config
| Property | Meaning |
| --- | --- |
| **Direction** | Which way the pattern travels: Right, Left, Down, Up. |
| **Lights On** | Number of LEDs lit in each group (typed number, min 1). |
| **Lights Off** | Number of dark LEDs in the gap between groups (typed number, min 0). |
| **Fit To Element** | Pads the gap so a whole number of `[on][off]` cycles spans the element **exactly** — groups and gaps end up evenly spaced end to end and the pattern wraps seamlessly around the prop. Lights Off is the **minimum** gap; the pattern is only ever spread out, never tightened. Off by default. |
| **Advance By** | How many LEDs the pattern moves at a time, and so how many light and go out together. The element is divided into fixed steps of this many LEDs; every LED in a step always shows the same brightness and the whole step switches as one — `2` moves and lights two at a time, `5` does five at a time. `1` = the pattern slides one LED at a time (classic marquee). Above `1`, **Lights On** and **Lights Off** are rounded to a whole number of steps so every group stays in step with the others. Auto‑capped at **Lights On** and snaps down if you lower Lights On. *(Stored as `FadeGroup` for backwards compatibility.)* |
| **Speed** | A curve mapping to movement rate. Mapped **exponentially** (≈0.02 … 120 LEDs/sec) so most of the range is slow, fine control; a flat 0 stops it. |
| **Randomness** | Slider 0–100. Shifts each lit **group as a whole** early or late by a fixed random amount; the LEDs inside a group never move relative to each other. The shift is *static*, so the pattern ends up unevenly spaced but still slides as one rigid piece. Colour is **not** jittered, only position. |
| **Ripples** | Slider 0–20. How many ripples run along the element at once (`0` = off). A ripple carries every group it reaches forward by one **Advance By** step; the group then holds until the next ripple arrives. `1` is a single surge sweeping end to end, `4` is four chasing each other. Because it is a count *across the element*, the look is the same on a 50‑LED prop and a 500‑LED one. |
| **Ripple Speed** | Slider 0–100, ≈0.05 … 6 ripples/sec, exponentially mapped. **Independent of Speed.** Slower is genuinely smoother, not just slower: the group eases across its step over a fixed *fraction* of the cycle, so a slow ripple glides and a fast one snaps. Hidden until Ripples is turned up. |
| **Bad Bulbs** | How many LEDs on the element are blown and never light, like an old sign with a few dead bulbs. `0` = none. They are picked once per render, so a dud stays dud for the whole effect and shows through every animation. |
| **Bad Bulb Seed** | Which bulbs are blown. The same seed always picks the same ones, so editing anything else never reshuffles them; change it to shuffle to an arrangement you prefer. Hidden until Bad Bulbs is above 0. |

The ripples are real movement, not just a wobble: each one carries the pattern forward a step. Set
**Speed to 0** and the ripples are the only motion — which is the point, because a group can then sit
genuinely still between shoves instead of gliding through the pause. Leave Speed above 0 and you get a
smooth glide with the stepping laid over it.

> ⚠️ **The Fade curve decides whether any of this is visible.** A group flows across its step in
> fractions of an LED, but which LEDs are *lit* can only ever change a whole LED at a time. What turns
> sub‑LED movement into something the eye can see is the Fade curve ramping an LED up as the group
> arrives and down as it leaves. With the default **flat 100** curve (hard bulbs) an LED is only ever
> fully on or fully off, so every ripple lands as a hard one‑LED jog no matter how slowly it flows.
> Give the Fade curve any slope — a curve peaking in the middle is the classic choice — and the same
> ripple reads as a smooth crossfade. This applies to plain Speed movement too; it is not specific to
> ripples.

Both Randomness and the ripples need a gap to move in — with **Lights Off = 0** there is nowhere to go
and neither does anything. They share one budget (see below); the ripples are served first, so turning
them on leaves Randomness a little less room.

### Animation

Animates the pattern on and off from inside the effect. A layered overlay can only multiply
brightness, so it can never do anything *structural* — it has no idea where the groups are. Anything
that moves or reveals groups individually has to live here.

| Property | Meaning |
| --- | --- |
| **Animation** | `None` (default), `Slide`, `Dissolve`, `Stack`, `Scale`. |
| **Animation Curve** | Drives the animation over the effect's duration. **50 is fully assembled** — see below. |
| **Animation From** | Which end of the movement axis it arrives from: `Left`, `Right`, `Center Out`, `Ends In`. It leaves by the far end. Only shown for Slide and Stack; Dissolve and Scale have no axis. Named for a horizontal movement axis — on a vertical one they read as top and bottom. |

**The curve is the whole animation.** Not a duration, not a slider — so it can animate on over the
first beat, sit still, and leave on the last, any shape, any number of times.

```
curve   0 ---------- 50 ---------- 100
     fully gone    assembled    fully gone
     (arriving)                 (the other way)

0 -> 50        animate on
50 -> 100      animate off
0 -> 100       on, then straight off the far side
50 ->100-> 50  out and back
flat 50        no animation at all
```

**At exactly 50 every mode renders identically to `None`.** That is enforced by a test, not just
intended: the neutral point has to be genuinely neutral or picking an animation would nudge the
pattern before you had asked it to do anything.

| Mode | Below 50 | Above 50 |
| --- | --- | --- |
| **Slide** | The whole block travels in from the chosen end. All the groups move — this is not a wipe; nothing is revealed in place. | Keeps going and exits the opposite end. `Center Out` / `Ends In` split it into two halves that part and meet in the middle. |
| **Dissolve** | Groups appear in a scattered order, each fading up as its turn arrives. Whole **groups**, never individual LEDs. | Leaves in a *different* scattered order, so an out is not the in retraced. |
| **Stack** | Groups land one at a time from the chosen end and pile up until full. Whatever has landed **keeps running the main effect** — scroll, ripples, fade and all — so the pattern comes alive as it fills. | Unstacks from the other end. |
| **Scale** | Each group narrows toward its own centre, so the edges die first. | A hole opens at each group's centre and eats outward, so the middle dies first. Both extremes reach dark by opposite routes. |

### Color
| Property | Meaning |
| --- | --- |
| **Color Mode** | `Solid Per Group` (each group one solid colour, cycling the palette), `Gradient Across Group` (the gradient spans each lit group), `Gradient Along Prop` (the palette forms one gradient stretched across the whole prop; the groups reveal slices of it). |
| **Color Gradients** | The palette. One colour = a single‑colour marquee; add more to cycle group‑by‑group. |

### Brightness
| Property | Meaning |
| --- | --- |
| **Fade** | Brightness of an LED across its **journey through a lit group**, read left to right: the left of the curve is the moment it lights, the right is the moment before it goes dark. Nothing is layered on top, so the curve alone decides the shape — a **rising line ramps up then snaps off**, a **falling line snaps on then ramps down**, a curve **peaking in the middle fades up and back down**, a **flat 100 line = hard bulbs** (instant on/off), a flat 0 line = off. It applies to a whole step of LEDs at once, never across the LEDs within one. |
| **Brightness** | Overall level of the whole effect over its duration. |

## How it renders

Everything keys off a single continuous scroll position, `_phase`, accumulated per
frame from the Speed curve (in LED units). Because it is fractional, slow motion is
smooth — there is no snapping to whole pixels like Bars.

Everything is laid out on the **step grid**: the element is divided into fixed steps of `AdvanceBy`
LEDs, and both the lit width and the pattern pitch are whole numbers of steps —
`onSteps = round(OnCount / AdvanceBy)`, `litWidth = onSteps · AdvanceBy`, and
`periodSteps = onSteps + ceil(OffCount / AdvanceBy)`. Steps are fixed to the element, so a lit width
or pitch that was not a whole number of them would land every group on a different step alignment
and each group would then sit at its own point in the fade — which reads as a chase running
*through* the pattern instead of one pattern moving as a unit. It would also make the lit count
flicker between two values as the pattern moved. With `AdvanceBy = 1` none of this rounds anything.

With **Fit To Element** on, `periodSteps` becomes
`floor(axisLength/AdvanceBy) / floor(that / periodSteps)`, deliberately allowed to be
**fractional**: dividing the element by a whole cycle count is what makes the spacing as even as the
step grid allows with no rounding error accumulating along the prop.

Group `g`'s leading edge is
`floor(g·periodSteps + 0.5)·AdvanceBy + GroupJitter(g) + GroupRipple(g)` — snapped to a step boundary
so all groups stay in step, with the two displacements added afterwards *un*snapped, since putting
groups out of step with each other is the whole point of them.

### The ripples are a staircase, not a wave

A group is either still waiting for the next ripple or has already been shoved by it, and
`GroupRipple` is which of the two. That is deliberately a **step function**, because a smooth wave
cannot hold still — it is never not moving, so it can only ever look like drifting, never like
walking.

```
placeInQueue   = mod(g, rippleGroups) / rippleGroups      // where g sits in the queue, 0..1
ripplesPassed  = ripplePhase − placeInQueue
rise           = min(1, frac(ripplesPassed) / RiseFraction)
eased          = rise²·(3 − 2·rise)                       // smoothstep: starts and ends at rest
GroupRipple(g) = rippleStep · (floor(ripplesPassed) + eased − ripplePhase + 1)
```

The `eased` term is what makes it flow rather than teleport — without it the group covers its whole
step in a single frame. Because the rise is a *fraction* of the cycle (0.6) rather than a fixed time,
a slow ripple eases over a long stretch and a fast one snaps, which is why Ripple Speed reads as a
smoothness control as much as a speed one. `floor + eased` is still monotonic and still gains exactly
1 per cycle, so the bound below is unchanged.

`floor(...)` counts how many ripples have reached this group, so on its own it climbs forever. The
climbing part is **identical for every group** and is genuine forward movement, so `UpdatePhase` puts
it in the scroll (`phase = scrollPhase + rippleStep · ripplePhase`); subtracting it here leaves only
the bounded stagger of who has been shoved and who has not. Splitting it this way is exactly what
lets a group sit still: its own position stops changing between shoves, instead of creeping.

Taking the queue position **modulo** the ripple spacing is what keeps the stagger periodic. Using `g`
directly would add a term growing with `g`, quietly stretching the pattern out along its length.

`ripplePhase` runs off elapsed **time**, not travel, so the ripples keep going with Speed at 0.

### Why the displacements are bounded

Randomness and the ripples share one **gap‑closing budget**. Two things matter, and the obvious bound
gets both wrong:

- What costs gap is how far a group moves **relative to its neighbour**, not its absolute
  displacement — two groups leaning towards each other both eat the same gap. A shove costs one step
  (any two groups differ by at most one ripple); a unit of Randomness costs `2`, since two neighbours
  can sit at opposite extremes of their range at the same time.
- Not overlapping is **not enough**. LEDs are discrete: if the dark space shrinks below one step, no
  LED lands in it and two groups read as a single run even though the maths never overlapped them.
  The budget is therefore `gap − AdvanceBy`, keeping one step in reserve so a dark LED always
  survives between groups.

The ripples are served first, because their shove is a fixed quantum rather than a slider — one step,
the same unit the whole pattern is built on. Randomness gets what is left. Together this keeps
"exactly Lights On LEDs lit" true at every setting, which is checked across every combination of the
two.

For each pixel, at movement‑axis coordinate `s`:

1. **Step centre** `centre = (floor(s/AdvanceBy) + 0.5)·AdvanceBy − dir·phase`. The whole step is
   treated as one lamp, so every LED in it shares a brightness.
2. Find the owning group: `local = centre − GroupStart(g)` must lie in `[0, litWidth)`, else the
   step is in a gap → off. `g−1 … g+1` are tested, since both the step snapping and the randomizer
   can move a group off its nominal cell; where two could claim the step, the one holding it
   further from its own edge wins. Because starts sit on step boundaries and a group is a whole
   number of steps wide, **exactly `onSteps` steps are lit at every instant**.
3. Fade: `u` is the step's progress through the group — `0` the moment it lights, `1` just before
   it goes dark (`u = (litWidth − local)/litWidth`, or `local/litWidth` when travelling the other
   way, so reversing direction does not invert the shape). `brightness = FadeCurve(u) · Level`.
   Nothing is layered on top of the curve, so the curve alone decides the shape.
4. Colour comes from `Color Mode`, keyed on the group index `g` and on `local` (for gradients).
   `Gradient Along Prop` uses the true `s` instead, since that gradient is fixed to the prop rather
   than to the moving pattern.

There is intentionally **no supersampling** — a flat Fade curve therefore snaps instantly on/off.
The pattern varies only along the movement axis (randomness moves whole groups, not individual
LEDs), so the string renderer always computes one line and reuses it across the perpendicular axis.

### Where each animation hooks in

Deliberately four different layers, chosen so the group maths and the bounds it relies on stay intact
wherever possible:

- **Slide** moves the *sample coordinate*, not the groups: LED `s` reads the pattern at `s − travel`,
  and reads outside `[0, axisLength)` are dark. That windowing is the whole trick — the pattern is
  infinite and periodic, so translating the groups themselves would displace it by whole periods and
  look like nothing had happened. `GroupStart` and `TryFindGroup` are untouched.
- **Dissolve** is a per-group brightness gate on `Hash01(group)`. Deliberately *not* the Dissolve
  effect's approach: that one reshuffles from a time-seeded generator on every pre-render, so editing
  any unrelated property silently reorders it. `Hash01` is stable across frames *and* re-renders.
- **Stack** is a per-group arrival gate with exactly one group in transit, which keeps it to a single
  interval test rather than a widened group search.
- **Scale** gates on `local`, the position within the group, measuring coverage against the part of the
  bank *inside* the group. That last part matters: a scrolling pattern puts banks at fractional offsets
  so one can straddle the group's edge, and dividing by the full bank width leaves the protruding part
  lit even when the hole covers the whole group.

Mid-animation the no-merge guarantee is legitimately suspended — `Center Out` collapsing to the middle
is groups overlapping on purpose. It holds at the neutral point, which is what the tests assert.

**Bad bulbs** are a `bool[]` mask built once per render by a seeded `Random`, giving exactly the count
asked for rather than approximately it. Note this is the one thing that varies across the axis
*perpendicular* to the movement, so it cannot come out of the shared line — the single-line fast path
is kept when there are no duds and skipped when there are, otherwise a matrix would lose whole rows
instead of single bulbs.

## Files
- `Marquee.cs` — effect logic (properties + rendering).
- `MarqueeData.cs` — persisted settings (`[DataContract]`).
- `MarqueeDescriptor.cs` — module descriptor (`TypeId 9DE5A327‑AF69‑4472‑B8C9‑704B03A6AA43`, group Pixel).
- `MarqueeColorMode.cs`, `MarqueeDirection.cs`, `MarqueeAnimation.cs` — enums.
- `Images/EffectImage.png` — 64×64 toolbox icon.
- Registered in `Vixen.sln` (project GUID `477703A1‑64AC‑4741‑AB44‑481EC4EFF0CF`).

## Notes / possible future work
- **Advance By** (was "Fade Group") used to be a *fade‑front width* (a short gradient across the
  LEDs at each edge). It is now a discrete step of N LEDs that moves and switches together. It is
  still persisted as `FadeGroup`, so old sequences load unchanged.
- The **Fade** curve used to be read as a spatial ramp measured from the edges of the lit group,
  which layered a built‑in fade‑in *and* fade‑out on top of whatever curve you drew — a rising line
  came out symmetrical instead of ramping up and snapping off. It is now read straight across an
  LED's journey through the group, so the curve is the whole story.
- The default **Fade** curve is flat 100 — deliberately hard bulbs, so a new effect reads as a plain
  marquee. The cost is that sub‑LED movement is invisible until the curve is given a slope, which
  catches people out (see the warning under Config). A rounded V is the setting that makes the
  ripples look the way they are meant to; making it the default is a one‑line change in
  `MarqueeData`'s constructor if the hard‑bulb start ever stops being worth it.
- **Randomness** used to be a per‑pixel offset seeded from both axes, which read as noise
  rather than as a marquee. It is now per group and bounded by the gap. Existing sequences
  with Randomness above 0 will look different (calmer and more marquee‑like).
- **Ripples** replaced an earlier sinusoidal "Crawl" (three controls: amount, speed, wave length).
  A sine reads as drifting rather than walking, because it is always in motion and never plants.
  The staircase does, and it needs two controls instead of three.
- The ripple travels up the group index. If it reads the wrong way round on a prop it is a sign flip
  on the `placeInQueue` term in `GroupRipple()`.
- **Stack** orders groups by where they sit on the element, so with Speed above 0 a group can drift
  across the fill threshold while the animation is mid-flight and pop in or out. It is exact at Speed 0,
  which is the natural way to use a stack. Ordering by group identity (`g mod N`) instead would be
  stable per group but would put one discontinuity somewhere in the chain.
- **Animation From**'s names suit a horizontal movement axis; on a vertical Direction they read as top
  and bottom. Renaming to Start/End would be accurate for both but less obvious for the common case.
- Location‑mode direction polarity (Up/Down/Left/Right on a matrix) is worth an
  eyeball; it is a one‑line sign flip if any axis reads backwards.
- **Fit To Element** measures the movement axis of the render buffer: pixels‑per‑string for
  Left/Right, string count for Up/Down. On a mixed‑length prop that is the *longest* string,
  so shorter strings in the same group will not tile perfectly.
