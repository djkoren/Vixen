# Marquee Effect

An old‑fashioned theater‑marquee chase for pixel props: a repeating pattern of
`Lights On` lit LEDs followed by `Lights Off` dark LEDs slides smoothly along the
element. It was built specifically to give **clean, smooth motion at very slow
speeds** — the thing the Bars effect does poorly — and to give each bulb a warm
incandescent fade in and out.

It is a pixel effect (`PixelEffectBase`) and works in both **String** and
**Location** (matrix / megatree) target modes.

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
| **Crawl** | Slider 0–100. Groups surge forward and back **in sequence**, each lagging the one before it, so the surge travels along the pattern — a metachronal wave, which is what makes a centipede look like it is walking rather than sliding. Unlike Randomness this varies over *time*, which is what gives it life. Stacks with Randomness (a crawl with a little randomness on top is the most organic result). |
| **Crawl Speed** | Slider 0–100, ≈0.05 … 4 wave cycles/sec, exponentially mapped. **Independent of Speed**, so the pattern can crawl on the spot with Speed at 0. Hidden until Crawl is turned up. |
| **Wave Length** | How many groups one surge spans (min 2). `2` puts neighbours in opposition for a fast scuttle, `4` is the classic centipede ripple, `10`+ is a long lazy body wave. Hidden until Crawl is turned up. |

Both Randomness and Crawl need a gap to move in — with **Lights Off = 0** there is nowhere to go and
neither does anything. They share one budget (see below), so turning both up splits the range between
them rather than doubling the movement.

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
`floor(g·periodSteps + 0.5)·AdvanceBy + GroupJitter(g) + GroupCrawl(g)` — snapped to a step boundary
so all groups stay in step, with the two displacements added afterwards *un*snapped, since putting
groups out of step with each other is the whole point of them.

`GroupCrawl(g) = sin(2π·(crawlPhase − g/WaveLength)) · crawlAmount`. The `g/WaveLength` term is the
per‑group lag that turns a synchronised bobbing into a wave travelling along the chain.
`crawlPhase` is driven by elapsed **time**, not by `phase`, which is why the crawl keeps running with
Speed at 0.

### Why the displacements are bounded

Randomness and Crawl share one **gap‑closing budget**, split in proportion to the two sliders. Two
things matter, and the obvious bound gets both wrong:

- What costs gap is how far a group moves **relative to its neighbour**, not its absolute
  displacement — two groups leaning towards each other both eat the same gap. A unit of Randomness
  costs `2` (neighbours can sit at opposite extremes); a unit of Crawl costs `2·sin(π/WaveLength)`,
  so a short wave is expensive and a long wave is nearly free. That is why long waves are allowed a
  much bigger swing before the absolute cap (half the gap) takes over.
- Not overlapping is **not enough**. LEDs are discrete: if the dark space shrinks below one step, no
  LED lands in it and two groups read as a single run even though the maths never overlapped them.
  The budget is therefore `gap − AdvanceBy`, keeping one step in reserve so a dark LED always
  survives between groups.

This is what keeps "exactly Lights On LEDs lit" true at every slider setting. Randomness alone almost
never hits its worst case (it needs two adjacent hashes at opposite extremes), which is why the
looser bound survived until Crawl — `Wave Length 2` puts neighbours in perfect opposition on *every*
cycle, deterministically.

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

## Files
- `Marquee.cs` — effect logic (properties + rendering).
- `MarqueeData.cs` — persisted settings (`[DataContract]`).
- `MarqueeDescriptor.cs` — module descriptor (`TypeId 9DE5A327‑AF69‑4472‑B8C9‑704B03A6AA43`, group Pixel).
- `MarqueeColorMode.cs`, `MarqueeDirection.cs` — enums.
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
- The default **Fade** curve is still a rising line, which under the new reading means *ramp up,
  snap off*. A curve peaking in the middle is the classic incandescent look if that is wanted as
  the default instead.
- **Randomness** used to be a per‑pixel offset seeded from both axes, which read as noise
  rather than as a marquee. It is now per group and bounded by the gap. Existing sequences
  with Randomness above 0 will look different (calmer and more marquee‑like).
- **Crawl**'s wave travels backwards along the chain relative to travel, matching the retrograde gait
  real centipedes use. If it reads the wrong way round on a prop it is a sign flip on the
  `g / WaveLength` term in `GroupCrawl()`.
- Location‑mode direction polarity (Up/Down/Left/Right on a matrix) is worth an
  eyeball; it is a one‑line sign flip if any axis reads backwards.
- **Fit To Element** measures the movement axis of the render buffer: pixels‑per‑string for
  Left/Right, string count for Up/Down. On a mixed‑length prop that is the *longest* string,
  so shorter strings in the same group will not tile perfectly.
