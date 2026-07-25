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
			// Crawl is off by default so it never changes an existing look; the speed and wave length
			// only come into play once it is turned up.
			Crawl = 0;
			CrawlSpeed = 30;
			CrawlWaveLength = 4;
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
		/// Strength of the crawl, 0 to 100. Groups surge forward and back in sequence, each lagging the
		/// one before it, so the surge travels along the pattern.
		/// </summary>
		[DataMember]
		public int Crawl { get; set; }

		/// <summary>How fast the surge travels, 0 to 100. Independent of <see cref="SpeedCurve"/>.</summary>
		[DataMember]
		public int CrawlSpeed { get; set; }

		/// <summary>How many groups one surge spans. Minimum 2.</summary>
		[DataMember]
		public int CrawlWaveLength { get; set; }

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

			// Sequences saved before Crawl existed deserialize with a zero wave length, which would
			// divide by zero in the wave. Crawl itself defaults to 0 on those, so this only matters if
			// the slider is later turned up.
			if (CrawlWaveLength < 2)
			{
				CrawlWaveLength = 2;
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
				Crawl = Crawl,
				CrawlSpeed = CrawlSpeed,
				CrawlWaveLength = CrawlWaveLength,
				Orientation = Orientation,
			};
			return result;
		}
	}
}
