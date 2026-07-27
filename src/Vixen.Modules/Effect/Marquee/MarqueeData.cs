using System.Runtime.Serialization;
using VixenModules.App.ColorGradients;
using VixenModules.App.Curves;
using VixenModules.Effect.Effect;
using ZedGraph;

namespace VixenModules.Effect.Marquee
{
	[DataContract]
	public class MarqueeData : EffectTypeModuleData
	{
		public MarqueeData()
		{
			Colors = new List<ColorGradient> { new ColorGradient(Color.White) };
			Direction = MarqueeDirection.Right;
			ColorMode = MarqueeColorMode.SolidPerGroup;
			// A single lit LED with a three LED gap: the plainest marquee to read, and the easiest
			// starting point to widen from.
			OnCount = 1;
			OffCount = 3;
			// Off by default so the gap is exactly Lights Off unless the user asks for even spacing.
			FitToElement = false;
			// Move one LED at a time by default (classic marquee).
			FadeGroup = 1;
			// Speed is mapped exponentially so most of the slider travel lives in the slow range.
			// A flat mid value gives a gentle, smooth crawl by default.
			SpeedCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 50.0, 50.0 }));
			// The fade curve is read across an LED's journey through the lit group. A flat 100 line is
			// hard bulbs -- instant on, instant off -- which is the cleanest default to hear the pattern
			// before shaping the fade.
			FadeCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 100.0, 100.0 }));
			LevelCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 100.0, 100.0 }));
			Randomness = 0;
			// Ripples are off by default so they never change an existing look; the speed only comes
			// into play once at least one ripple is asked for.
			Ripples = 0;
			RippleSpeed = 40;
			// No animation, and a flat 50 curve -- the assembled position -- so turning one on does not
			// immediately move anything until the curve is shaped.
			Animation = MarqueeAnimation.None;
			AnimationCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 50.0, 50.0 }));
			AnimationFrom = MarqueeAnimationFrom.Left;
			// A straight ramp, so a group slides in at a steady rate until the user shapes it.
			StackCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 0.0, 100.0 }));
			CenterOrder = MarqueeCenterOrder.BothSides;
			// Shutter closed, so a new effect renders exactly as it did before motion blur existed.
			MotionBlur = 0;
			// No blown bulbs. The seed only matters once there are some.
			BadBulbs = 0;
			BadBulbSeed = 1;
			Orientation = StringOrientation.Horizontal;
		}

		[DataMember]
		public List<ColorGradient> Colors { get; set; }

		[DataMember]
		public MarqueeDirection Direction { get; set; }

		[DataMember]
		public MarqueeColorMode ColorMode { get; set; }

		[DataMember]
		public int OnCount { get; set; }

		[DataMember]
		public int OffCount { get; set; }

		/// <summary>
		/// When true the gap is padded so a whole number of on/off cycles spans the element exactly,
		/// spacing the groups evenly end to end. <see cref="OffCount"/> is the minimum gap.
		/// </summary>
		[DataMember]
		public bool FitToElement { get; set; }

		[DataMember]
		public int FadeGroup { get; set; }

		[DataMember]
		public Curve SpeedCurve { get; set; }

		[DataMember]
		public Curve FadeCurve { get; set; }

		[DataMember]
		public Curve LevelCurve { get; set; }

		[DataMember]
		public int Randomness { get; set; }

		/// <summary>
		/// How many ripples travel along the element at once. Each ripple shoves every group it reaches
		/// forward by one step, so groups advance in sequence and hold until the next one arrives.
		/// 0 turns ripples off.
		/// </summary>
		[DataMember]
		public int Ripples { get; set; }

		/// <summary>How fast a ripple travels, 0 to 100. Independent of <see cref="SpeedCurve"/>.</summary>
		[DataMember]
		public int RippleSpeed { get; set; }

		/// <summary>Which animation, if any, brings the pattern on and off.</summary>
		[DataMember]
		public MarqueeAnimation Animation { get; set; }

		/// <summary>
		/// Drives <see cref="Animation"/> over the effect's duration. 50 is fully assembled; below 50 the
		/// pattern is arriving and above 50 it is leaving by the opposite route.
		/// </summary>
		[DataMember]
		public Curve AnimationCurve { get; set; }

		/// <summary>Which end of the movement axis the animation arrives from.</summary>
		[DataMember]
		public MarqueeAnimationFrom AnimationFrom { get; set; }

		/// <summary>
		/// Shapes how a group travels as it slides into its slot in a stack: 0 is the moment it sets off
		/// and 100 the moment it lands.
		/// </summary>
		[DataMember]
		public Curve StackCurve { get; set; }

		/// <summary>In what order groups land when a stack starts from the centre.</summary>
		[DataMember]
		public MarqueeCenterOrder CenterOrder { get; set; }

		/// <summary>
		/// Shutter angle in degrees, 0 to 360, exactly as on a film camera: 0 is a closed shutter and no
		/// blur at all, 180 the usual cinema look, 360 a shutter open for the whole frame. Only used by
		/// the <see cref="MarqueeAnimation.Slide"/> and <see cref="MarqueeAnimation.Stack"/> animations.
		/// </summary>
		[DataMember]
		public int MotionBlur { get; set; }

		/// <summary>How many LEDs on the element are blown and never light. 0 = none.</summary>
		[DataMember]
		public int BadBulbs { get; set; }

		/// <summary>
		/// Picks which LEDs are blown. The same seed always gives the same set, so the arrangement does
		/// not change when other properties are edited; change it to shuffle to a different one.
		/// </summary>
		[DataMember]
		public int BadBulbSeed { get; set; }

		[DataMember]
		public StringOrientation Orientation { get; set; }

		[OnDeserialized]
		public void OnDeserialized(StreamingContext c)
		{
			// Guard against older serialized instances that are missing any of the curves.
			if (SpeedCurve == null)
			{
				SpeedCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 50.0, 50.0 }));
			}

			if (FadeCurve == null)
			{
				FadeCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 0.0, 100.0 }));
			}

			if (FadeGroup < 1)
			{
				FadeGroup = 1;
			}

			if (Ripples < 0)
			{
				Ripples = 0;
			}

			// Sequences saved before the animation existed deserialize with no curve at all. A flat 50 is
			// the assembled position, so they load looking exactly as they did.
			if (AnimationCurve == null)
			{
				AnimationCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 50.0, 50.0 }));
			}

			if (StackCurve == null)
			{
				StackCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 0.0, 100.0 }));
			}

			if (BadBulbs < 0)
			{
				BadBulbs = 0;
			}

			// Sequences saved before motion blur existed deserialize a zero shutter, which is exactly the
			// no-blur behaviour they were authored against, so nothing needs defaulting here beyond a range
			// guard.
			if (MotionBlur < 0)
			{
				MotionBlur = 0;
			}
			else if (MotionBlur > 360)
			{
				MotionBlur = 360;
			}

			// Those sequences also deserialize a zero seed, which would make every one of them pick the
			// same arrangement the moment bad bulbs were turned on.
			if (BadBulbSeed == 0)
			{
				BadBulbSeed = 1;
			}

			if (LevelCurve == null)
			{
				LevelCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 100.0, 100.0 }));
			}

			if (Colors == null || Colors.Count == 0)
			{
				Colors = new List<ColorGradient> { new ColorGradient(Color.White) };
			}
		}

		protected override EffectTypeModuleData CreateInstanceForClone()
		{
			MarqueeData result = new MarqueeData
			{
				Colors = Colors.ToList(),
				Direction = Direction,
				ColorMode = ColorMode,
				OnCount = OnCount,
				OffCount = OffCount,
				FitToElement = FitToElement,
				FadeGroup = FadeGroup,
				SpeedCurve = new Curve(SpeedCurve),
				FadeCurve = new Curve(FadeCurve),
				LevelCurve = new Curve(LevelCurve),
				Randomness = Randomness,
				Ripples = Ripples,
				RippleSpeed = RippleSpeed,
				Animation = Animation,
				AnimationCurve = new Curve(AnimationCurve),
				AnimationFrom = AnimationFrom,
				StackCurve = new Curve(StackCurve),
				CenterOrder = CenterOrder,
				MotionBlur = MotionBlur,
				BadBulbs = BadBulbs,
				BadBulbSeed = BadBulbSeed,
				Orientation = Orientation,
			};
			return result;
		}
	}
}
