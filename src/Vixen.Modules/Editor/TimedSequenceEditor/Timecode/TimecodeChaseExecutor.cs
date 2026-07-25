using System;
using Vixen.Module.Timing;
using Vixen.Sys;

namespace VixenModules.Editor.TimedSequenceEditor.Timecode
{
	/// <summary>
	/// A sequence executor for armed timecode chase. It runs the sequence against an externally
	/// supplied (MTC-driven) clock instead of the default Stopwatch and neutralizes the two
	/// base-class behaviors that are hostile to an external clock: the start-time spin-wait (which
	/// runs on the calling/UI thread) and the Position==0 / end-of-length auto-stop. The chase
	/// controller owns start/stop, so the sequence holds at its bounds instead of self-terminating.
	/// When chase is disarmed this class is never instantiated, so normal playback is unaffected.
	/// </summary>
	public sealed class TimecodeChaseExecutor : VixenModules.Sequence.Timed.Executor
	{
		private readonly ITiming _externalTiming;

		public TimecodeChaseExecutor(ITiming externalTiming)
		{
			_externalTiming = externalTiming;
		}

		protected override ITiming _ResolveTimingSource()
		{
			// The external clock wins unconditionally, even if the sequence has an audio (or other)
			// timing source selected — v1 chase is lights-only.
			return _externalTiming;
		}

		protected override void _WaitForTimingSourceToStart()
		{
			// No-op: an external clock only advances when its master advances, so spinning here would
			// block the calling (UI) thread until timecode happens to move.
		}

		protected override bool _IsEndOfSequence()
		{
			// The chase controller owns the lifecycle; never auto-end (hold at the bounds instead).
			return false;
		}

		// Lights-only chase (v1): do not start/resume the sequence's audio. Vixen's audio player
		// cannot be scrubbed frame-accurately under an external clock, and the audio reference lives
		// on the master side, so keep Vixen silent while chasing.
		protected override void _StartMedia() { }

		protected override void _ResumeMedia() { }

		/// <summary>
		/// Re-prime the effect data pump after a timecode jump/scrub in either direction, without
		/// resetting the external clock position. Firing the base SequenceReStarted path causes the
		/// NoCaching context to rebuild its forward-only effect queue from the start, so the correct
		/// effects re-pull at the new position (this is what makes backward jumps render correctly).
		/// </summary>
		public void RePrime()
		{
			OnSequenceReStarted(new SequenceStartedEventArgs(Sequence, TimingSource, StartTime, EndTime));
		}
	}
}
