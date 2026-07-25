using System.ComponentModel;
using Vixen.Attributes;
using Vixen.Marks;
using Vixen.Module;
using Vixen.Sys;
using Vixen.Sys.Attribute;
using Vixen.TypeConverters;
using VixenModules.App.ColorGradients;
using VixenModules.App.Curves;
using VixenModules.Effect.Effect;
using VixenModules.Effect.Pulse;
using VixenModules.EffectEditor.EffectDescriptorAttributes;
using ZedGraph;

namespace VixenModules.Effect.Lightning
{
	public class Lightning : BaseEffect
	{
		private EffectIntents _elementData;
		private LightningData _data;
		private Random _random;

		public Lightning()
		{
			_data = new LightningData();
			_random = new Random();
			InitAllAttributes();
		}

		protected override void TargetNodesChanged()
		{
			if (TargetNodes.Any())
			{
				CheckForInvalidColorData();
			}
		}

		protected override void _PreRender(CancellationTokenSource cancellationToken = null)
		{
			// Seed based on start time so preview is reproducible
			_random = new Random((int)(StartTime.Ticks & 0x7FFFFFFF));
			_elementData = new EffectIntents();

			var leaves = GetOrderedLeaves();
			if (leaves.Count == 0) return;

			if (LightningSource == LightningSource.MarkCollection)
			{
				RenderFromMarks(leaves);
			}
			else
			{
				RenderRandom(leaves);
			}
		}

		/// <summary>
		/// Returns all leaf elements sorted according to the current StringSetupMode/Orientation.
		/// </summary>
		private List<IElementNode> GetOrderedLeaves()
		{
			var leaves = TargetNodes
				.SelectMany(x => x.GetLeafEnumerator())
				.Distinct()
				.ToList();

			// String mode: sections form along the string in the chosen orientation
			// String + Horizontal = natural leaf order (default for most elements)
			// String + Vertical = reversed so sections climb vertically
			// Location mode: will be implemented with true X/Y coordinates later
			if (StringSetupMode == StringSetupMode.String && Orientation == OrientationMode.Vertical)
			{
				leaves.Reverse();
			}

			return leaves;
		}

		// ---- Random flashing with burst behavior ----
		// Integrates the FlashRate curve over time so ramps and multi-point curves work
		// correctly across the full effect duration.
		private void RenderRandom(List<IElementNode> leaves)
		{
			const double stepMs = 10.0; // 10ms resolution for curve sampling

			double currentMs = 0;
			double totalMs = TimeSpan.TotalMilliseconds;

			// Flash accumulator: each step adds flashRate * stepTime/1000 to the accumulator.
			// When it reaches 1.0, emit a flash.
			double flashAccumulator = 0;
			int flashesInCurrentBurst = 0;
			int currentBurstLength = -1;
			double pauseUntilMs = -1;

			while (currentMs < totalMs)
			{
				double pos = (currentMs / totalMs) * 100.0;

				// If we're in a pause, wait it out
				if (pauseUntilMs > 0 && currentMs < pauseUntilMs)
				{
					currentMs += stepMs;
					continue;
				}
				if (pauseUntilMs > 0 && currentMs >= pauseUntilMs)
				{
					// End of pause, start a new burst
					pauseUntilMs = -1;
					flashesInCurrentBurst = 0;
					currentBurstLength = -1;
					flashAccumulator = 0;
				}

				// Determine burst length at start of burst
				if (currentBurstLength < 0)
				{
					currentBurstLength = Math.Max(1, CurveValueInRange(BurstLengthCurve.GetValue(pos), 1, 50));
				}

				// Accumulate flash density based on FlashRate at current time
				double flashesPerSec = CurveValueInRangeD(FlashRateCurve.GetValue(pos), 0.5, 20.0);
				flashAccumulator += (stepMs / 1000.0) * flashesPerSec;

				// Emit flashes while accumulator >= 1.0 (can be multiple in one step for high rates)
				while (flashAccumulator >= 1.0 && currentMs < totalMs)
				{
					flashAccumulator -= 1.0;

					TimeSpan flashTime = TimeSpan.FromMilliseconds(currentMs);
					double flashPos = pos;
					RenderFlashOrBolt(leaves, flashTime, flashPos);
					flashesInCurrentBurst++;

					// Burst complete?
					if (flashesInCurrentBurst >= currentBurstLength)
					{
						int pauseMs = CurveValueInRange(BurstPauseCurve.GetValue(pos), 0, 8000);
						if (pauseMs > 0)
						{
							// Add randomness to pause (0.5x to 1.5x base)
							double jitter = 0.5 + _random.NextDouble();
							pauseUntilMs = currentMs + pauseMs * jitter;
						}
						else
						{
							// No pause, start new burst immediately
							flashesInCurrentBurst = 0;
							currentBurstLength = -1;
						}
						flashAccumulator = 0;
						break;
					}
				}

				currentMs += stepMs;
			}
		}

		// ---- Mark-triggered flashing ----
		private void RenderFromMarks(List<IElementNode> leaves)
		{
			IMarkCollection mc = MarkCollections.FirstOrDefault(x => x.Id == _data.MarkCollectionId);
			if (mc == null) return;

			// Expand the mark search range to include marks that fall before the effect
			// but whose build-up would extend into the effect
			var searchStart = StartTime - TimeSpan.FromMilliseconds(BuildUpDurationMs);
			var searchEnd = StartTime + TimeSpan + TimeSpan.FromMilliseconds(AftershockDurationMs);
			var marks = mc.MarksInclusiveOfTime(searchStart, searchEnd);

			foreach (var mark in marks)
			{
				var markLocalTime = mark.StartTime - StartTime;
				RenderMarkPattern(leaves, markLocalTime);
			}
		}

		private void RenderMarkPattern(List<IElementNode> leaves, TimeSpan markTime)
		{
			// Full hit on mark (before the pattern plays) - renders first so pattern flashes overlap it
			if (FullHitMode != FullHitMode.None && markTime >= TimeSpan.Zero && markTime < TimeSpan)
			{
				RenderFullHit(leaves, markTime);
			}

			switch (MarkPattern)
			{
				case MarkPattern.Single:
					if (markTime >= TimeSpan.Zero && markTime < TimeSpan)
					{
						double p = GetEffectTimeIntervalPosition(markTime + StartTime) * 100.0;
						RenderFlashOrBolt(leaves, markTime, p);
					}
					break;

				case MarkPattern.Building:
					RenderBuildingPattern(leaves, markTime);
					break;

				case MarkPattern.Double:
					RenderMultiFlash(leaves, markTime, 2, 60);
					break;

				case MarkPattern.Triple:
					RenderMultiFlash(leaves, markTime, 3, 50);
					break;

				case MarkPattern.Aftershock:
					RenderAftershockPattern(leaves, markTime);
					break;
			}
		}

		// Renders a full-coverage flash on the mark, in either current color, white, or white->color
		private void RenderFullHit(List<IElementNode> leaves, TimeSpan markTime)
		{
			double pos = GetEffectTimeIntervalPosition(markTime + StartTime) * 100.0;
			float overallScale = (float)(OverallIntensityCurve.GetValue(pos) / 100.0);
			if (overallScale < 0.01f) return;

			TimeSpan hitDuration = TimeSpan.FromMilliseconds(Math.Max(10, FullHitDurationMs));
			var scaledIntensity = BuildScaledCurve(PerFlashIntensityCurve, overallScale);

			if (FullHitMode == FullHitMode.WhiteThenColor)
			{
				// Split duration: first half white, second half color
				TimeSpan whiteDur = TimeSpan.FromMilliseconds(hitDuration.TotalMilliseconds / 2);
				TimeSpan colorDur = hitDuration - whiteDur;

				if (markTime + whiteDur > TimeSpan) whiteDur = TimeSpan - markTime;
				if (whiteDur > TimeSpan.Zero)
				{
					RenderFullHitSegment(leaves, markTime, whiteDur, scaledIntensity, new ColorGradient(Color.White));
				}

				TimeSpan colorStart = markTime + whiteDur;
				if (colorStart + colorDur > TimeSpan) colorDur = TimeSpan - colorStart;
				if (colorDur > TimeSpan.Zero)
				{
					RenderFullHitSegment(leaves, colorStart, colorDur, scaledIntensity, Colors);
				}
			}
			else
			{
				// Single color fill
				ColorGradient hitColor = (FullHitMode == FullHitMode.White) ? new ColorGradient(Color.White) : Colors;

				if (markTime + hitDuration > TimeSpan) hitDuration = TimeSpan - markTime;
				if (hitDuration > TimeSpan.Zero)
				{
					RenderFullHitSegment(leaves, markTime, hitDuration, scaledIntensity, hitColor);
				}
			}
		}

		private void RenderFullHitSegment(List<IElementNode> leaves, TimeSpan startTime, TimeSpan duration, Curve intensity, ColorGradient color)
		{
			foreach (var leaf in leaves)
			{
				var intents = PulseRenderer.RenderNode(leaf, intensity, color, duration, HasDiscreteColors);
				intents.OffsetAllCommandsByTime(startTime);
				_elementData.Add(intents);
			}
		}

		private void RenderBuildingPattern(List<IElementNode> leaves, TimeSpan markTime)
		{
			// Build up: start BuildUpDurationMs before the mark with small, sparse flashes
			// that grow in intensity and frequency until a big flash at the mark.
			TimeSpan buildStart = markTime - TimeSpan.FromMilliseconds(BuildUpDurationMs);
			double buildupMs = BuildUpDurationMs;

			// Emit ~5-8 ramping flashes before the mark
			int flashCount = 6;
			for (int i = 0; i < flashCount; i++)
			{
				// t goes from 0 (earliest) to 1 (just before mark)
				double t = (double)i / flashCount;
				// Accelerate: use t^2 so flashes get closer together near the mark
				double timeFraction = t * t;
				TimeSpan flashTime = buildStart + TimeSpan.FromMilliseconds(timeFraction * buildupMs);

				if (flashTime < TimeSpan.Zero || flashTime >= TimeSpan) continue;

				double p = GetEffectTimeIntervalPosition(flashTime + StartTime) * 100.0;

				// Smaller coverage and intensity for buildup flashes
				int coverage = Math.Max(5, (int)(CurveValueInRange(SectionCoverageCurve.GetValue(p), 5, 100) * (0.2 + t * 0.5)));
				RenderOneFlashWithCoverage(leaves, flashTime, p, coverage, (float)(0.3 + t * 0.7));
			}

			// Big flash at the mark
			if (markTime >= TimeSpan.Zero && markTime < TimeSpan)
			{
				double p = GetEffectTimeIntervalPosition(markTime + StartTime) * 100.0;
				RenderFlashOrBolt(leaves, markTime, p);
			}
		}

		private void RenderMultiFlash(List<IElementNode> leaves, TimeSpan markTime, int count, int spacingMs)
		{
			for (int i = 0; i < count; i++)
			{
				TimeSpan flashTime = markTime + TimeSpan.FromMilliseconds(i * spacingMs);
				if (flashTime < TimeSpan.Zero || flashTime >= TimeSpan) continue;

				double p = GetEffectTimeIntervalPosition(flashTime + StartTime) * 100.0;
				RenderFlashOrBolt(leaves, flashTime, p);
			}
		}

		private void RenderAftershockPattern(List<IElementNode> leaves, TimeSpan markTime)
		{
			// Main flash at mark
			if (markTime >= TimeSpan.Zero && markTime < TimeSpan)
			{
				double p = GetEffectTimeIntervalPosition(markTime + StartTime) * 100.0;
				RenderFlashOrBolt(leaves, markTime, p);
			}

			// 3-5 random aftershocks within AftershockDurationMs
			int shockCount = 3 + _random.Next(3);
			for (int i = 0; i < shockCount; i++)
			{
				double tFraction = (i + 1.0) / (shockCount + 1);
				// Add randomness
				double jitter = tFraction + (_random.NextDouble() * 0.1 - 0.05);
				TimeSpan shockTime = markTime + TimeSpan.FromMilliseconds(jitter * AftershockDurationMs);

				if (shockTime < TimeSpan.Zero || shockTime >= TimeSpan) continue;

				double p = GetEffectTimeIntervalPosition(shockTime + StartTime) * 100.0;

				// Aftershocks are smaller: ~30% of normal coverage and intensity
				int coverage = Math.Max(5, CurveValueInRange(SectionCoverageCurve.GetValue(p), 5, 100) / 3);
				RenderOneFlashWithCoverage(leaves, shockTime, p, coverage, 0.5f);
			}
		}

		// Dispatch: Flash or Bolt mode
		private void RenderFlashOrBolt(List<IElementNode> leaves, TimeSpan flashTime, double curvePos)
		{
			if (LightningMode == LightningMode.Bolt)
			{
				RenderBolt(leaves, flashTime, curvePos);
			}
			else
			{
				int coverage = CurveValueInRange(SectionCoverageCurve.GetValue(curvePos), 5, 100);
				RenderOneFlashWithCoverage(leaves, flashTime, curvePos, coverage, 1.0f);
			}
		}

		// Flash mode: light up contiguous sections of the element
		private void RenderOneFlashWithCoverage(List<IElementNode> leaves, TimeSpan flashTime, double curvePos, int coveragePercent, float intensityScale)
		{
			int totalToLight = Math.Max(1, leaves.Count * coveragePercent / 100);
			int sectionCount = Math.Max(1, CurveValueInRange(SectionCountCurve.GetValue(curvePos), 1, 8));
			if (sectionCount > totalToLight) sectionCount = totalToLight;

			int baseSectionSize = Math.Max(1, totalToLight / sectionCount);

			int baseDurationMs = CurveValueInRange(FlashDurationCurve.GetValue(curvePos), 10, 500);

			// Compute overall intensity scale
			float overallScale = (float)(OverallIntensityCurve.GetValue(curvePos) / 100.0);
			float finalScale = intensityScale * overallScale;
			if (finalScale < 0.01f) return;

			var scaledIntensity = BuildScaledCurve(PerFlashIntensityCurve, finalScale);

			// Render each section with its own randomized size and duration
			var usedStarts = new HashSet<int>();
			for (int s = 0; s < sectionCount; s++)
			{
				// Randomize section size per-section
				int sectionSize = VaryValue(baseSectionSize, SizeVariation, 1);

				// Randomize duration per-section
				int durationMs = VaryValue(baseDurationMs, DurationVariation, 10);
				TimeSpan duration = TimeSpan.FromMilliseconds(durationMs);

				if (flashTime + duration > TimeSpan)
				{
					duration = TimeSpan - flashTime;
					if (duration <= TimeSpan.Zero) continue;
				}

				// Try to find a non-overlapping section start
				int start = -1;
				for (int tries = 0; tries < 10; tries++)
				{
					int candidate = _random.Next(Math.Max(1, leaves.Count - sectionSize + 1));
					bool overlaps = false;
					foreach (var u in usedStarts)
					{
						if (Math.Abs(u - candidate) < sectionSize)
						{
							overlaps = true;
							break;
						}
					}
					if (!overlaps)
					{
						start = candidate;
						break;
					}
				}
				if (start < 0) start = _random.Next(Math.Max(1, leaves.Count - sectionSize + 1));
				usedStarts.Add(start);

				int end = Math.Min(start + sectionSize, leaves.Count);
				for (int i = start; i < end; i++)
				{
					var intents = PulseRenderer.RenderNode(leaves[i], scaledIntensity, Colors, duration, HasDiscreteColors);
					intents.OffsetAllCommandsByTime(flashTime);
					_elementData.Add(intents);
				}
			}
		}

		/// <summary>
		/// Applies random variation to a base value. variation 0 = exact,
		/// 100 = full random from ~5% to 500% of base (very dramatic).
		/// </summary>
		private int VaryValue(int baseValue, int variationPercent, int minValue)
		{
			if (variationPercent <= 0) return Math.Max(minValue, baseValue);

			// Scale variation more aggressively
			// At 100% variation: minFactor = 0.05, maxFactor = 5.0
			double variation = variationPercent / 100.0;
			double minFactor = Math.Max(0.05, 1.0 - variation * 0.95);
			double maxFactor = 1.0 + variation * 4.0;
			double factor = minFactor + _random.NextDouble() * (maxFactor - minFactor);

			int result = (int)Math.Round(baseValue * factor);
			return Math.Max(minValue, result);
		}

		// Bolt mode: a bolt of lightning travels fast down the element, then strikes bright at the end
		// followed by a couple of flickering after-flashes like real lightning.
		private void RenderBolt(List<IElementNode> leaves, TimeSpan flashTime, double curvePos)
		{
			int coverage = CurveValueInRange(SectionCoverageCurve.GetValue(curvePos), 5, 100);
			int segmentSize = Math.Max(1, CurveValueInRange(BoltSegmentSizeCurve.GetValue(curvePos), 1, 20));
			int segmentDelayMs = Math.Max(1, CurveValueInRange(BoltSegmentDelayCurve.GetValue(curvePos), 5, 200));
			int baseFlashDurationMs = CurveValueInRange(FlashDurationCurve.GetValue(curvePos), 10, 500);

			float overallScale = (float)(OverallIntensityCurve.GetValue(curvePos) / 100.0);
			if (overallScale < 0.01f) return;
			var scaledIntensity = BuildScaledCurve(PerFlashIntensityCurve, overallScale);

			// Pick how far the bolt travels (total length based on coverage)
			int travelDistance = Math.Max(segmentSize * 2, leaves.Count * coverage / 100);

			// Random start & direction -- bolt zig-zags but moves mostly in one direction
			int startIdx = _random.Next(leaves.Count);
			int direction = _random.Next(2) == 0 ? 1 : -1;

			// Travel phase: fast-moving short flashes that overlap briefly creating a traveling streak
			int travelFlashDurMs = Math.Min(baseFlashDurationMs, Math.Max(20, segmentDelayMs * 3));
			TimeSpan segmentTime = flashTime;
			int currentIdx = startIdx;
			int traveled = 0;

			while (traveled < travelDistance && segmentTime < TimeSpan)
			{
				// Small zig-zag jitter -- mostly moves in 'direction' but with small offset
				int segSize = VaryValue(segmentSize, SizeVariation, 1);
				int from = Math.Min(currentIdx, currentIdx + segSize * direction);
				int to = Math.Max(currentIdx, currentIdx + segSize * direction);
				from = Math.Max(0, from);
				to = Math.Min(leaves.Count, to);

				TimeSpan duration = TimeSpan.FromMilliseconds(travelFlashDurMs);
				if (segmentTime + duration > TimeSpan)
				{
					duration = TimeSpan - segmentTime;
					if (duration <= TimeSpan.Zero) break;
				}

				for (int i = from; i < to; i++)
				{
					var intents = PulseRenderer.RenderNode(leaves[i], scaledIntensity, Colors, duration, HasDiscreteColors);
					intents.OffsetAllCommandsByTime(segmentTime);
					_elementData.Add(intents);
				}

				// Advance position with slight zig-zag
				int jitter = _random.Next(-segSize / 2, segSize / 2 + 1);
				currentIdx += segSize * direction + jitter;
				traveled += segSize;

				if (currentIdx < 0) { currentIdx = 0; direction = 1; }
				if (currentIdx >= leaves.Count) { currentIdx = leaves.Count - 1; direction = -1; }

				segmentTime = segmentTime.Add(TimeSpan.FromMilliseconds(Math.Max(1, segmentDelayMs)));
			}

			// Strike phase: bright flash at the end point, covering wider area with longer duration
			int strikeIdx = currentIdx;
			int strikeSize = segmentSize * 3;
			int strikeFrom = Math.Max(0, strikeIdx - strikeSize / 2);
			int strikeTo = Math.Min(leaves.Count, strikeFrom + strikeSize);

			int strikeDurationMs = baseFlashDurationMs;
			TimeSpan strikeDuration = TimeSpan.FromMilliseconds(strikeDurationMs);
			if (segmentTime + strikeDuration > TimeSpan)
			{
				strikeDuration = TimeSpan - segmentTime;
			}

			if (strikeDuration > TimeSpan.Zero)
			{
				for (int i = strikeFrom; i < strikeTo; i++)
				{
					var intents = PulseRenderer.RenderNode(leaves[i], scaledIntensity, Colors, strikeDuration, HasDiscreteColors);
					intents.OffsetAllCommandsByTime(segmentTime);
					_elementData.Add(intents);
				}
			}

			if (BoltFadeMode == BoltFadeMode.CleanFade)
			{
				// Clean fade: render one extra fading pulse at the strike point
				TimeSpan fadeStart = segmentTime + strikeDuration;
				TimeSpan fadeDuration = TimeSpan.FromMilliseconds(Math.Max(50, BoltFadeExtraMs));
				if (fadeStart + fadeDuration > TimeSpan)
				{
					fadeDuration = TimeSpan - fadeStart;
				}

				if (fadeDuration > TimeSpan.Zero)
				{
					// Intensity ramps from current level down to 0 over the fade duration
					var fadePoints = new PointPairList(new[] { 0.0, 100.0 }, new[] { 100.0 * overallScale, 0.0 });
					var fadeCurve = new Curve(fadePoints);
					for (int i = strikeFrom; i < strikeTo; i++)
					{
						var intents = PulseRenderer.RenderNode(leaves[i], fadeCurve, Colors, fadeDuration, HasDiscreteColors);
						intents.OffsetAllCommandsByTime(fadeStart);
						_elementData.Add(intents);
					}
				}
			}
			else
			{
				// Flicker phase: 2-3 brief flickers at the strike point like real lightning
				int flickerCount = 2 + _random.Next(2);
				TimeSpan flickerTime = segmentTime + strikeDuration + TimeSpan.FromMilliseconds(30);
				for (int f = 0; f < flickerCount; f++)
				{
					if (flickerTime >= TimeSpan) break;

					int flickerDurMs = VaryValue(travelFlashDurMs, 50, 10);
					TimeSpan flickerDuration = TimeSpan.FromMilliseconds(flickerDurMs);
					if (flickerTime + flickerDuration > TimeSpan)
					{
						flickerDuration = TimeSpan - flickerTime;
						if (flickerDuration <= TimeSpan.Zero) break;
					}

					// Dimmer flicker intensity
					var dimmerIntensity = BuildScaledCurve(PerFlashIntensityCurve, overallScale * 0.6f);
					for (int i = strikeFrom; i < strikeTo; i++)
					{
						var intents = PulseRenderer.RenderNode(leaves[i], dimmerIntensity, Colors, flickerDuration, HasDiscreteColors);
						intents.OffsetAllCommandsByTime(flickerTime);
						_elementData.Add(intents);
					}

					// Random gap between flickers
					flickerTime = flickerTime.Add(TimeSpan.FromMilliseconds(flickerDurMs + 40 + _random.Next(80)));
				}
			}
		}

		// ---- Helpers ----
		private int CurveValueInRange(double curveValue, int min, int max)
		{
			double t = curveValue / 100.0;
			return (int)Math.Round(min + t * (max - min));
		}

		private double CurveValueInRangeD(double curveValue, double min, double max)
		{
			double t = curveValue / 100.0;
			return min + t * (max - min);
		}

		private Curve BuildScaledCurve(Curve source, float scale)
		{
			if (scale >= 0.99f && scale <= 1.01f) return source;

			var points = new PointPairList();
			foreach (var p in source.Points)
			{
				double y = p.Y * scale;
				if (y < 0) y = 0;
				if (y > 100) y = 100;
				points.Add(p.X, y);
			}
			return new Curve(points);
		}

		protected override EffectIntents _Render()
		{
			return _elementData;
		}

		public override IModuleDataModel ModuleData
		{
			get { return _data; }
			set
			{
				_data = value as LightningData;
				CheckForInvalidColorData();
				InitAllAttributes();
			}
		}

		protected override EffectTypeModuleData EffectModuleData
		{
			get { return _data; }
		}

		private void CheckForInvalidColorData()
		{
			var validColors = GetValidColors();
			if (validColors.Any())
			{
				bool changed = false;
				if (!Colors.GetColorsInGradient().IsSubsetOf(validColors))
				{
					Colors = new ColorGradient(validColors.First());
					changed = true;
				}
				if (changed)
				{
					OnPropertyChanged("Colors");
				}
			}
		}

		#region String Setup Properties

		[Value]
		[ProviderCategory(@"String Setup", 0)]
		[ProviderDisplayName(@"Mode")]
		[ProviderDescription(@"String = sections follow the natural string order with chosen orientation. Location = reserved for future X/Y-based layout.")]
		[PropertyOrder(0)]
		public StringSetupMode StringSetupMode
		{
			get { return _data.StringSetupMode; }
			set
			{
				_data.StringSetupMode = value;
				UpdateBrowsableAttributes();
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"String Setup", 0)]
		[ProviderDisplayName(@"Orientation")]
		[ProviderDescription(@"Direction sections form along the string. Horizontal = natural string order; Vertical = reversed order.")]
		[PropertyOrder(1)]
		public OrientationMode Orientation
		{
			get { return _data.Orientation; }
			set
			{
				_data.Orientation = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		#endregion

		#region Config Properties

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Mode")]
		[ProviderDescription(@"Flash = random sections; Bolt = sequential segments forming a bolt-like path.")]
		[PropertyOrder(0)]
		public LightningMode LightningMode
		{
			get { return _data.LightningMode; }
			set
			{
				_data.LightningMode = value;
				UpdateBrowsableAttributes();
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Timing Source")]
		[ProviderDescription(@"Random = automatic; Mark Collection = triggered by marks.")]
		[PropertyOrder(1)]
		public LightningSource LightningSource
		{
			get { return _data.LightningSource; }
			set
			{
				_data.LightningSource = value;
				UpdateBrowsableAttributes();
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Mark Collection")]
		[ProviderDescription(@"Mark collection that triggers lightning.")]
		[TypeConverter(typeof(IMarkCollectionNameConverter))]
		[PropertyEditor("SelectionEditor")]
		[PropertyOrder(2)]
		public string MarkCollectionId
		{
			get
			{
				return MarkCollections.FirstOrDefault(x => x.Id == _data.MarkCollectionId)?.Name;
			}
			set
			{
				var newMarkCollection = MarkCollections.FirstOrDefault(x => x.Name.Equals(value));
				var id = newMarkCollection?.Id ?? Guid.Empty;
				if (!id.Equals(_data.MarkCollectionId))
				{
					var oldMarkCollection = MarkCollections.FirstOrDefault(x => x.Id.Equals(_data.MarkCollectionId));
					RemoveMarkCollectionListeners(oldMarkCollection);
					_data.MarkCollectionId = id;
					AddMarkCollectionListeners(newMarkCollection);
					IsDirty = true;
					OnPropertyChanged();
				}
			}
		}

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Mark Pattern")]
		[ProviderDescription(@"How lightning behaves around each mark.")]
		[PropertyOrder(3)]
		public MarkPattern MarkPattern
		{
			get { return _data.MarkPattern; }
			set
			{
				_data.MarkPattern = value;
				UpdateBrowsableAttributes();
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Build-up Duration (ms)")]
		[ProviderDescription(@"How long before a mark the building flashes start (Building pattern).")]
		[NumberRange(50, 5000, 50, 0)]
		[PropertyOrder(4)]
		public int BuildUpDurationMs
		{
			get { return _data.BuildUpDurationMs; }
			set
			{
				_data.BuildUpDurationMs = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Aftershock Duration (ms)")]
		[ProviderDescription(@"How long after a mark aftershocks continue (Aftershock pattern).")]
		[NumberRange(100, 5000, 50, 0)]
		[PropertyOrder(5)]
		public int AftershockDurationMs
		{
			get { return _data.AftershockDurationMs; }
			set
			{
				_data.AftershockDurationMs = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Full Hit on Mark")]
		[ProviderDescription(@"Adds a full-element flash right on each mark (works with any mark pattern).")]
		[PropertyOrder(6)]
		public FullHitMode FullHitMode
		{
			get { return _data.FullHitMode; }
			set
			{
				_data.FullHitMode = value;
				UpdateBrowsableAttributes();
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Full Hit Duration (ms)")]
		[ProviderDescription(@"How long the full-element hit lasts on each mark.")]
		[NumberRange(10, 2000, 10, 0)]
		[PropertyOrder(7)]
		public int FullHitDurationMs
		{
			get { return _data.FullHitDurationMs; }
			set
			{
				_data.FullHitDurationMs = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		#endregion

		#region Random Timing Curves

		[Value]
		[ProviderCategory(@"Random Timing", 2)]
		[ProviderDisplayName(@"Flash Rate")]
		[ProviderDescription(@"Flashes per second within a burst. 0 = 0.5/sec, 100 = 20/sec.")]
		[PropertyOrder(0)]
		public Curve FlashRateCurve
		{
			get { return _data.FlashRateCurve; }
			set
			{
				_data.FlashRateCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Random Timing", 2)]
		[ProviderDisplayName(@"Burst Length")]
		[ProviderDescription(@"Flashes per burst. 0 = 1 flash, 100 = 50 flashes (effectively continuous).")]
		[PropertyOrder(1)]
		public Curve BurstLengthCurve
		{
			get { return _data.BurstLengthCurve; }
			set
			{
				_data.BurstLengthCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Random Timing", 2)]
		[ProviderDisplayName(@"Burst Pause")]
		[ProviderDescription(@"Pause between bursts. 0 = no pause (continuous), 100 = 8 sec long quiet gap.")]
		[PropertyOrder(2)]
		public Curve BurstPauseCurve
		{
			get { return _data.BurstPauseCurve; }
			set
			{
				_data.BurstPauseCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		#endregion

		#region Flash Appearance

		[Value]
		[ProviderCategory(@"Flash Appearance", 3)]
		[ProviderDisplayName(@"Flash Duration")]
		[ProviderDescription(@"How long each flash lasts. 0 = 10ms, 100 = 500ms.")]
		[PropertyOrder(0)]
		public Curve FlashDurationCurve
		{
			get { return _data.FlashDurationCurve; }
			set
			{
				_data.FlashDurationCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Flash Appearance", 3)]
		[ProviderDisplayName(@"Section Coverage")]
		[ProviderDescription(@"Total % of element lit per flash (0-100%).")]
		[PropertyOrder(1)]
		public Curve SectionCoverageCurve
		{
			get { return _data.SectionCoverageCurve; }
			set
			{
				_data.SectionCoverageCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Flash Appearance", 3)]
		[ProviderDisplayName(@"Section Count")]
		[ProviderDescription(@"Number of separate sections per flash. 0 = 1, 100 = 8.")]
		[PropertyOrder(2)]
		public Curve SectionCountCurve
		{
			get { return _data.SectionCountCurve; }
			set
			{
				_data.SectionCountCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Flash Appearance", 3)]
		[ProviderDisplayName(@"Size Variation")]
		[ProviderDescription(@"Random variation in section sizes per flash (0 = uniform, 100 = wide range).")]
		[PropertyEditor("SliderEditor")]
		[NumberRange(0, 100, 1)]
		[PropertyOrder(3)]
		public int SizeVariation
		{
			get { return _data.SizeVariation; }
			set
			{
				_data.SizeVariation = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Flash Appearance", 3)]
		[ProviderDisplayName(@"Duration Variation")]
		[ProviderDescription(@"Random variation in flash durations per flash (0 = uniform, 100 = wide range).")]
		[PropertyEditor("SliderEditor")]
		[NumberRange(0, 100, 1)]
		[PropertyOrder(4)]
		public int DurationVariation
		{
			get { return _data.DurationVariation; }
			set
			{
				_data.DurationVariation = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		#endregion

		#region Bolt

		[Value]
		[ProviderCategory(@"Bolt", 4)]
		[ProviderDisplayName(@"Segment Delay")]
		[ProviderDescription(@"Delay between bolt segments. 0 = 5ms, 100 = 200ms.")]
		[PropertyOrder(0)]
		public Curve BoltSegmentDelayCurve
		{
			get { return _data.BoltSegmentDelayCurve; }
			set
			{
				_data.BoltSegmentDelayCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Bolt", 4)]
		[ProviderDisplayName(@"Segment Size")]
		[ProviderDescription(@"Elements per bolt segment. 0 = 1, 100 = 20.")]
		[PropertyOrder(1)]
		public Curve BoltSegmentSizeCurve
		{
			get { return _data.BoltSegmentSizeCurve; }
			set
			{
				_data.BoltSegmentSizeCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Bolt", 4)]
		[ProviderDisplayName(@"Fade Mode")]
		[ProviderDescription(@"With Flickers = real lightning (small flickers after strike); Clean Fade = single smooth fade out.")]
		[PropertyOrder(2)]
		public BoltFadeMode BoltFadeMode
		{
			get { return _data.BoltFadeMode; }
			set
			{
				_data.BoltFadeMode = value;
				UpdateBrowsableAttributes();
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Bolt", 4)]
		[ProviderDisplayName(@"Fade Duration (ms)")]
		[ProviderDescription(@"Extra fade time after the strike (Clean Fade mode).")]
		[NumberRange(50, 3000, 10, 0)]
		[PropertyOrder(3)]
		public int BoltFadeExtraMs
		{
			get { return _data.BoltFadeExtraMs; }
			set
			{
				_data.BoltFadeExtraMs = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		#endregion

		#region Color & Intensity

		[Value]
		[ProviderCategory(@"Color", 5)]
		[ProviderDisplayName(@"Colors")]
		[ProviderDescription(@"Lightning flash color.")]
		[PropertyOrder(0)]
		public ColorGradient Colors
		{
			get { return _data.Colors; }
			set
			{
				_data.Colors = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Color", 5)]
		[ProviderDisplayName(@"Overall Intensity")]
		[ProviderDescription(@"Intensity envelope over the entire effect duration (scales every flash).")]
		[PropertyOrder(1)]
		public Curve OverallIntensityCurve
		{
			get { return _data.OverallIntensityCurve; }
			set
			{
				_data.OverallIntensityCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Color", 5)]
		[ProviderDisplayName(@"Per-Flash Intensity")]
		[ProviderDescription(@"Intensity curve applied to each individual flash (shape of one flash).")]
		[PropertyOrder(2)]
		public Curve PerFlashIntensityCurve
		{
			get { return _data.PerFlashIntensityCurve; }
			set
			{
				_data.PerFlashIntensityCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		#endregion

		#region Information

		public override string Information
		{
			get { return "Realistic lightning flashes in Flash or Bolt mode. Random mode uses curve-driven bursts and pauses. Mark mode supports Single/Double/Triple/Building/Aftershock patterns with an optional full-element hit on each mark in color, white, or white-then-color."; }
		}

		public override string InformationLink
		{
			get { return "http://www.vixenlights.com/"; }
		}

		#endregion

		#region Attributes

		private void InitAllAttributes()
		{
			UpdateBrowsableAttributes(false);
			TypeDescriptor.Refresh(this);
		}

		private void UpdateBrowsableAttributes(bool refresh = true)
		{
			bool isBolt = LightningMode == LightningMode.Bolt;
			bool isRandom = LightningSource == LightningSource.Random;
			bool isString = StringSetupMode == StringSetupMode.String;
			bool showBuildUp = !isRandom && MarkPattern == MarkPattern.Building;
			bool showAftershock = !isRandom && MarkPattern == MarkPattern.Aftershock;

			bool showFadeExtra = isBolt && BoltFadeMode == BoltFadeMode.CleanFade;
			bool showFullHitDuration = !isRandom && FullHitMode != FullHitMode.None;

			Dictionary<string, bool> propertyStates = new Dictionary<string, bool>()
			{
				{"Orientation", isString},
				{"MarkCollectionId", !isRandom},
				{"MarkPattern", !isRandom},
				{"BuildUpDurationMs", showBuildUp},
				{"AftershockDurationMs", showAftershock},
				{"FullHitMode", !isRandom},
				{"FullHitDurationMs", showFullHitDuration},
				{"FlashRateCurve", isRandom},
				{"BurstLengthCurve", isRandom},
				{"BurstPauseCurve", isRandom},
				{"BoltSegmentDelayCurve", isBolt},
				{"BoltSegmentSizeCurve", isBolt},
				{"BoltFadeMode", isBolt},
				{"BoltFadeExtraMs", showFadeExtra}
			};
			SetBrowsable(propertyStates);

			if (refresh)
			{
				TypeDescriptor.Refresh(this);
			}
		}

		#endregion

		public override bool IsDirty
		{
			get
			{
				if (!Colors.CheckLibraryReference()
				    || !OverallIntensityCurve.CheckLibraryReference()
				    || !PerFlashIntensityCurve.CheckLibraryReference()
				    || !FlashRateCurve.CheckLibraryReference()
				    || !BurstLengthCurve.CheckLibraryReference()
				    || !BurstPauseCurve.CheckLibraryReference()
				    || !FlashDurationCurve.CheckLibraryReference()
				    || !SectionCoverageCurve.CheckLibraryReference()
				    || !SectionCountCurve.CheckLibraryReference()
				    || !BoltSegmentDelayCurve.CheckLibraryReference()
				    || !BoltSegmentSizeCurve.CheckLibraryReference())
				{
					base.IsDirty = true;
				}
				return base.IsDirty;
			}
			protected set { base.IsDirty = value; }
		}

		protected override void MarkCollectionsChanged()
		{
			if (LightningSource == LightningSource.MarkCollection)
			{
				var markCollection = MarkCollections.FirstOrDefault(x => x.Name.Equals(MarkCollectionId));
				InitializeMarkCollectionListeners(markCollection);
			}
		}
	}
}
