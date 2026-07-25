using System;
using Vixen.Module.Timing;

namespace VixenModules.Editor.TimedSequenceEditor.Timecode
{
	/// <summary>
	/// Bridges an <see cref="ITimecodeSource"/> to Vixen's <see cref="ITiming"/> clock. The engine
	/// polls <see cref="Position"/> every output frame; this maps the raw incoming timecode onto
	/// sequence time via the user offset and latency trim, clamped to the sequence bounds. It is
	/// instantiated only by the armed chase controller and is never registered as a discoverable
	/// timing module, so it can never become a sequence's saved timing source.
	/// </summary>
	public sealed class TimecodeChaseTiming : ITiming
	{
		private readonly ITimecodeSource _source;
		private readonly TimecodeChaseSettings _settings;
		private readonly long _lengthTicks;

		public TimecodeChaseTiming(ITimecodeSource source, TimecodeChaseSettings settings, TimeSpan sequenceLength)
		{
			_source = source;
			_settings = settings;
			_lengthTicks = sequenceLength.Ticks;
		}

		public TimeSpan Position
		{
			get
			{
				long ticks = _source.Position.Ticks
				             - _settings.OffsetTicks
				             + (long)_settings.LatencyTrimMs * TimeSpan.TicksPerMillisecond;

				// Floor at one tick: SequenceExecutor's base end-check treats Position==0 as
				// end-of-sequence, so a held/idle look must never map to exactly zero.
				if (ticks < 1) ticks = 1;
				if (_lengthTicks > 0 && ticks > _lengthTicks) ticks = _lengthTicks;
				return TimeSpan.FromTicks(ticks);
			}
			set { /* external clock is authoritative; it cannot be commanded to seek */ }
		}

		public bool SupportsVariableSpeeds => false;

		public float Speed
		{
			get => 1f;
			set { /* variable speed is not supported for an external clock */ }
		}

		// The underlying source is opened/closed by the chase controller, so these are no-ops.
		public void Start() { }

		public void Stop() { }

		public void Pause() { }

		public void Resume() { }
	}
}
