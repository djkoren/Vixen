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
			OnCount = 3;
			OffCount = 3;
			// Fade one LED at a time by default; can be raised up to OnCount so a whole group fades together.
			FadeGroup = 1;
			// Speed is mapped exponentially so most of the slider travel lives in the slow range.
			// A flat mid value gives a gentle, smooth crawl by default.
			SpeedCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 50.0, 50.0 }));
			// The fade curve shapes each edge ramp (off -> full) applied over FadeGroup LEDs. Default is a linear ramp.
			FadeCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 0.0, 100.0 }));
			LevelCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 100.0, 100.0 }));
			Randomness = 0;
			Orientation = StringOrientation.Vertical;
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
				FadeGroup = FadeGroup,
				SpeedCurve = new Curve(SpeedCurve),
				FadeCurve = new Curve(FadeCurve),
				LevelCurve = new Curve(LevelCurve),
				Randomness = Randomness,
				Orientation = Orientation,
			};
			return result;
		}
	}
}
