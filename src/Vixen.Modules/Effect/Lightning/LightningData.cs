using System.Runtime.Serialization;
using VixenModules.App.ColorGradients;
using VixenModules.App.Curves;
using VixenModules.Effect.Effect;
using ZedGraph;

namespace VixenModules.Effect.Lightning
{
	[DataContract]
	public class LightningData : EffectTypeModuleData
	{
		// ---- String Setup ----
		[DataMember]
		public StringSetupMode StringSetupMode { get; set; }

		[DataMember]
		public OrientationMode Orientation { get; set; }

		// ---- Config ----
		[DataMember]
		public LightningMode LightningMode { get; set; }

		[DataMember]
		public LightningSource LightningSource { get; set; }

		[DataMember]
		public Guid MarkCollectionId { get; set; }

		[DataMember]
		public MarkPattern MarkPattern { get; set; }

		// Full-coverage hit on mark (works with all MarkPatterns)
		[DataMember]
		public FullHitMode FullHitMode { get; set; }

		// Duration of the full hit in ms
		[DataMember]
		public int FullHitDurationMs { get; set; }

		// ---- Random Timing Curves ----
		// FlashRateCurve: 0-100 scale maps to 0.5..20 flashes/second
		[DataMember]
		public Curve FlashRateCurve { get; set; }

		// BurstLengthCurve: 0-100 maps to 1..10 flashes per burst
		[DataMember]
		public Curve BurstLengthCurve { get; set; }

		// BurstPauseCurve: 0-100 maps to 0..3000 ms pause between bursts
		[DataMember]
		public Curve BurstPauseCurve { get; set; }

		// ---- Flash Appearance Curves ----
		// FlashDurationCurve: 0-100 maps to 10..500 ms
		[DataMember]
		public Curve FlashDurationCurve { get; set; }

		// SectionCoverageCurve: 0-100% of element covered per flash
		[DataMember]
		public Curve SectionCoverageCurve { get; set; }

		// SectionCountCurve: 0-100 maps to 1..8 simultaneous sections per flash
		[DataMember]
		public Curve SectionCountCurve { get; set; }

		// ---- Bolt ----
		// BoltSegmentDelayCurve: 0-100 maps to 5..200 ms between bolt segments
		[DataMember]
		public Curve BoltSegmentDelayCurve { get; set; }

		// BoltSegmentSizeCurve: 0-100 maps to 1..20 elements per bolt segment
		[DataMember]
		public Curve BoltSegmentSizeCurve { get; set; }

		// How the bolt strike fades: WithFlickers (like real lightning) or CleanFade (single fade out)
		[DataMember]
		public BoltFadeMode BoltFadeMode { get; set; }

		// For CleanFade mode: extra fade time after the strike in ms (beyond the normal flash duration)
		[DataMember]
		public int BoltFadeExtraMs { get; set; }

		// ---- Randomization ----
		// How much section sizes vary per flash, 0-100% (0 = uniform, 100 = fully random)
		[DataMember]
		public int SizeVariation { get; set; }

		// How much flash durations vary per flash, 0-100%
		[DataMember]
		public int DurationVariation { get; set; }

		// ---- Mark Pattern Timing ----
		// For Building pattern: how many ms before the mark to start the buildup
		[DataMember]
		public int BuildUpDurationMs { get; set; }

		// For Aftershock pattern: how many ms after the mark for aftershocks
		[DataMember]
		public int AftershockDurationMs { get; set; }

		// ---- Color ----
		[DataMember]
		public ColorGradient Colors { get; set; }

		// Overall intensity envelope applied over the entire effect duration
		[DataMember]
		public Curve OverallIntensityCurve { get; set; }

		// Per-flash intensity curve applied to each individual flash
		[DataMember]
		public Curve PerFlashIntensityCurve { get; set; }

		public LightningData()
		{
			// String setup defaults
			StringSetupMode = StringSetupMode.String;
			Orientation = OrientationMode.Vertical;

			// Config defaults
			LightningMode = LightningMode.Flash;
			LightningSource = LightningSource.Random;
			MarkCollectionId = Guid.Empty;
			MarkPattern = MarkPattern.Single;
			FullHitMode = FullHitMode.None;
			FullHitDurationMs = 300;

			// Random timing curves - default to moderate flash rate with bursts
			FlashRateCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 40.0, 40.0 }));
			BurstLengthCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 30.0, 30.0 }));
			BurstPauseCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 50.0, 50.0 }));

			// Flash appearance defaults
			FlashDurationCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 20.0, 20.0 }));
			SectionCoverageCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 40.0, 40.0 }));
			SectionCountCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 25.0, 25.0 }));

			// Bolt defaults
			BoltSegmentDelayCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 15.0, 15.0 }));
			BoltSegmentSizeCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 15.0, 15.0 }));
			BoltFadeMode = BoltFadeMode.WithFlickers;
			BoltFadeExtraMs = 200;

			// Randomization defaults
			SizeVariation = 40;
			DurationVariation = 40;

			// Mark pattern timing
			BuildUpDurationMs = 500;
			AftershockDurationMs = 800;

			// Color defaults
			Colors = new ColorGradient(Color.White);
			OverallIntensityCurve = new Curve(new PointPairList(new[] { 0.0, 100.0 }, new[] { 100.0, 100.0 }));
			PerFlashIntensityCurve = new Curve(new PointPairList(new[] { 0.0, 50.0, 100.0 }, new[] { 0.0, 100.0, 0.0 }));
		}

		protected override EffectTypeModuleData CreateInstanceForClone()
		{
			var result = new LightningData
			{
				StringSetupMode = StringSetupMode,
				Orientation = Orientation,
				LightningMode = LightningMode,
				LightningSource = LightningSource,
				MarkCollectionId = MarkCollectionId,
				MarkPattern = MarkPattern,
				FullHitMode = FullHitMode,
				FullHitDurationMs = FullHitDurationMs,
				FlashRateCurve = FlashRateCurve,
				BurstLengthCurve = BurstLengthCurve,
				BurstPauseCurve = BurstPauseCurve,
				FlashDurationCurve = FlashDurationCurve,
				SectionCoverageCurve = SectionCoverageCurve,
				SectionCountCurve = SectionCountCurve,
				BoltSegmentDelayCurve = BoltSegmentDelayCurve,
				BoltSegmentSizeCurve = BoltSegmentSizeCurve,
				BoltFadeMode = BoltFadeMode,
				BoltFadeExtraMs = BoltFadeExtraMs,
				SizeVariation = SizeVariation,
				DurationVariation = DurationVariation,
				BuildUpDurationMs = BuildUpDurationMs,
				AftershockDurationMs = AftershockDurationMs,
				Colors = Colors,
				OverallIntensityCurve = OverallIntensityCurve,
				PerFlashIntensityCurve = PerFlashIntensityCurve
			};
			return result;
		}
	}
}
