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
| **Fade Group** | How many LEDs fade in/out **together** at each edge of the lit group. `1` = each LED fades one at a time (classic marquee); larger fades more LEDs as a unit. Auto‑capped at **Lights On** and snaps down if you lower Lights On. The middle of the group always reaches full brightness. |
| **Speed** | A curve mapping to movement rate. Mapped **exponentially** (≈0.02 … 120 LEDs/sec) so most of the range is slow, fine control; a flat 0 stops it. |
| **Randomness** | Slider 0–100. Adds a stable per‑LED timing offset to when each LED fades/lights. `0` = perfectly synced marquee; higher = organic crawling shimmer. Colour is **not** jittered, only fade timing. |

### Color
| Property | Meaning |
| --- | --- |
| **Color Mode** | `Solid Per Group` (each group one solid colour, cycling the palette), `Gradient Across Group` (the gradient spans each lit group), `Gradient Along Prop` (the palette forms one gradient stretched across the whole prop; the groups reveal slices of it). |
| **Color Gradients** | The palette. One colour = a single‑colour marquee; add more to cycle group‑by‑group. |

### Brightness
| Property | Meaning |
| --- | --- |
| **Fade** | The shape of the fade ramp (off → full) applied over the Fade Group width at each edge. A straight line ramps evenly; an eased curve gives a warmer glow; a **flat 100 line = instant on/off** (hard bulbs, no fade); a flat 0 line = off. |
| **Brightness** | Overall level of the whole effect over its duration. |

## How it renders

Everything keys off a single continuous scroll position, `_phase`, accumulated per
frame from the Speed curve (in LED units). Because it is fractional, slow motion is
smooth — there is no snapping to whole pixels like Bars.

For each pixel, at movement‑axis coordinate `s`:

1. **Colour position** `sColour = s − dir·phase` (un‑jittered) and **fade position**
   `sFade = sColour + jitter`. Colour uses the un‑jittered value so colour groups never
   bleed; only the fade timing is jittered (that is the randomizer / crawl).
2. `c = sFade mod (OnCount+OffCount)`. If `c ≥ OnCount` the pixel is in the gap → off.
3. Fade ramp: `dEdge = min(c, OnCount − c)`, `u = min(dEdge / fadeWidth, 1)`,
   `brightness = FadeCurve(u) · Level`. `fadeWidth = min(FadeGroup, OnCount/2)` so the
   centre of every group reaches 100% even when `Lights On = 1`.
4. Colour comes from `Color Mode` evaluated at `sColour` (group index) and `c`
   (position within the group for gradients).

There is intentionally **no supersampling** — a flat Fade curve therefore snaps
instantly on/off, and a sloped curve gives smooth fades. When Randomness is 0 the
string renderer computes one line along the movement axis and reuses it across the
perpendicular axis for speed.

## Files
- `Marquee.cs` — effect logic (properties + rendering).
- `MarqueeData.cs` — persisted settings (`[DataContract]`).
- `MarqueeDescriptor.cs` — module descriptor (`TypeId 9DE5A327‑AF69‑4472‑B8C9‑704B03A6AA43`, group Pixel).
- `MarqueeColorMode.cs`, `MarqueeDirection.cs` — enums.
- `Images/EffectImage.png` — 64×64 toolbox icon.
- Registered in `Vixen.sln` (project GUID `477703A1‑64AC‑4741‑AB44‑481EC4EFF0CF`).

## Notes / possible future work
- **Fade Group** currently controls the *fade‑front width* (how many LEDs fade
  together at the edges). If discrete, hard‑switched banks of N are wanted instead,
  that is a different model and can be added.
- Location‑mode direction polarity (Up/Down/Left/Right on a matrix) is worth an
  eyeball; it is a one‑line sign flip if any axis reads backwards.
