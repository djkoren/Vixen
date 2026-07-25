using System;
using System.Threading;

namespace VixenModules.Editor.TimedSequenceEditor.Timecode
{
	/// <summary>Immutable snapshot of chase status for the editor's status indicator.</summary>
	public readonly struct TimecodeStatus
	{
		public TimecodeStatus(TimecodeState state, TimecodeFrameRate frameRate, TimeSpan incoming, TimeSpan sequenceTime, bool playing)
		{
			State = state;
			FrameRate = frameRate;
			Incoming = incoming;
			SequenceTime = sequenceTime;
			Playing = playing;
		}

		public TimecodeState State { get; }
		public TimecodeFrameRate FrameRate { get; }
		public TimeSpan Incoming { get; }       // raw incoming timecode
		public TimeSpan SequenceTime { get; }   // incoming mapped onto sequence time
		public bool Playing { get; }
	}

	/// <summary>
	/// Orchestrates timecode chase: watches an <see cref="ITimecodeSource"/> and raises editor-facing
	/// events to start playback, hold/stop on loss of timecode, and re-prime on backward relocations.
	/// Holds are handled by letting the bridge freeze (the executor keeps running and re-renders the
	/// held look), so only the first roll starts playback. Backward jumps request a data-pump
	/// re-prime; forward motion drains through the pump on its own and needs none. Events are raised
	/// on arbitrary threads; the editor marshals them onto the UI thread.
	/// </summary>
	public sealed class TimecodeChaseController : IDisposable
	{
		private readonly ITimecodeSource _source;
		private readonly TimecodeChaseTiming _timing;
		private readonly TimecodeChaseSettings _settings;

		private System.Threading.Timer? _statusTimer;
		private bool _playbackStarted;
		private long _holdPositionTicks;
		private bool _disposed;

		/// <summary>Begin playback seeded at the supplied sequence time.</summary>
		public event EventHandler<TimeSpan>? ChaseStart;

		/// <summary>Timecode stopped and policy is Hold: freeze the current look (no transport change).</summary>
		public event EventHandler? ChaseHoldRequested;

		/// <summary>Timecode stopped and policy is Stop: stop the sequence.</summary>
		public event EventHandler? ChaseStopRequested;

		/// <summary>A backward relocation occurred: rebuild the effect pump at the new position.</summary>
		public event EventHandler? ChaseRePrimeRequested;

		public event EventHandler<TimecodeStatus>? StatusChanged;

		public TimecodeChaseController(ITimecodeSource source, TimecodeChaseTiming timing, TimecodeChaseSettings settings)
		{
			_source = source;
			_timing = timing;
			_settings = settings;
		}

		public void Start()
		{
			_source.StateChanged += OnSourceStateChanged;
			_source.Located += OnSourceLocated;
			_statusTimer = new System.Threading.Timer(OnStatusTick, null, 200, 200);

			// Timecode may already be rolling at arm time (transition fired before we subscribed).
			if (_source.State == TimecodeState.Running && !_playbackStarted)
			{
				_playbackStarted = true;
				ChaseStart?.Invoke(this, _timing.Position);
			}
		}

		public TimecodeStatus CurrentStatus =>
			new TimecodeStatus(_source.State, _source.FrameRate, _source.Position, _timing.Position, _playbackStarted);

		private long JumpThresholdTicks => (long)(2.0 * _source.FrameRate.FrameDuration().Ticks);

		private void OnSourceStateChanged(object? sender, TimecodeStateChangedEventArgs e)
		{
			switch (e.State)
			{
				case TimecodeState.Running:
					if (!_playbackStarted)
					{
						_playbackStarted = true;
						ChaseStart?.Invoke(this, _timing.Position);
					}
					else
					{
						// Resuming after a hold. If the master relocated earlier while parked,
						// re-prime for backward motion (forward drains through the pump on its own).
						long now = _timing.Position.Ticks;
						if (now < _holdPositionTicks - JumpThresholdTicks)
						{
							ChaseRePrimeRequested?.Invoke(this, EventArgs.Empty);
						}
					}
					break;

				case TimecodeState.Stopped:
				case TimecodeState.NoSignal:
					if (_playbackStarted)
					{
						_holdPositionTicks = _timing.Position.Ticks;
						if (_settings.StopPolicy == TimecodeStopPolicy.Stop)
						{
							_playbackStarted = false;
							ChaseStopRequested?.Invoke(this, EventArgs.Empty);
						}
						else
						{
							ChaseHoldRequested?.Invoke(this, EventArgs.Empty);
						}
					}
					break;

				// Freewheeling: keep chasing on the interpolator; no action.
			}

			RaiseStatus();
		}

		private void OnSourceLocated(object? sender, TimecodeLocateEventArgs e)
		{
			// Only backward relocations break the forward-only effect pump and need a re-prime.
			if (_playbackStarted && e.IsBackward)
			{
				ChaseRePrimeRequested?.Invoke(this, EventArgs.Empty);
			}
			RaiseStatus();
		}

		private void OnStatusTick(object? state)
		{
			if (_disposed) return;
			RaiseStatus();
		}

		private void RaiseStatus()
		{
			StatusChanged?.Invoke(this, CurrentStatus);
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;

			_source.StateChanged -= OnSourceStateChanged;
			_source.Located -= OnSourceLocated;

			var timer = _statusTimer;
			_statusTimer = null;
			timer?.Dispose();
		}
	}
}
