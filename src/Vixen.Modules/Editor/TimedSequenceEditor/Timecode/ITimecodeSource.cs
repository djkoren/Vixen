using System;

namespace VixenModules.Editor.TimedSequenceEditor.Timecode
{
	/// <summary>
	/// Lock/run state of an external timecode source.
	/// </summary>
	public enum TimecodeState
	{
		/// <summary>No timecode is being received (device closed or silent past the freewheel window).</summary>
		NoSignal,

		/// <summary>Timecode is actively advancing.</summary>
		Running,

		/// <summary>Timecode dropped out momentarily; the source is coasting on its interpolator.</summary>
		Freewheeling,

		/// <summary>Timecode was running and has stopped (master paused/held).</summary>
		Stopped
	}

	/// <summary>
	/// SMPTE frame rate reported by (or assumed for) the timecode stream.
	/// </summary>
	public enum TimecodeFrameRate
	{
		Fps24,
		Fps25,
		Fps2997Drop,
		Fps30
	}

	public static class TimecodeFrameRateExtensions
	{
		/// <summary>Real frames per second (29.97 for drop-frame, i.e. 30000/1001).</summary>
		public static double ToFps(this TimecodeFrameRate rate)
		{
			switch (rate)
			{
				case TimecodeFrameRate.Fps24: return 24d;
				case TimecodeFrameRate.Fps25: return 25d;
				case TimecodeFrameRate.Fps2997Drop: return 30000d / 1001d;
				case TimecodeFrameRate.Fps30: return 30d;
				default: return 30d;
			}
		}

		/// <summary>Integer frames-per-second used for SMPTE addressing (30 for drop-frame).</summary>
		public static int NominalFrames(this TimecodeFrameRate rate)
		{
			switch (rate)
			{
				case TimecodeFrameRate.Fps24: return 24;
				case TimecodeFrameRate.Fps25: return 25;
				default: return 30; // 29.97DF and 30 both address 30 frames
			}
		}

		/// <summary>Duration of a single frame at this rate.</summary>
		public static TimeSpan FrameDuration(this TimecodeFrameRate rate)
		{
			return TimeSpan.FromTicks((long)(TimeSpan.TicksPerSecond / rate.ToFps()));
		}
	}

	public class TimecodeStateChangedEventArgs : EventArgs
	{
		public TimecodeStateChangedEventArgs(TimecodeState state, TimecodeFrameRate frameRate)
		{
			State = state;
			FrameRate = frameRate;
		}

		public TimecodeState State { get; }
		public TimecodeFrameRate FrameRate { get; }
	}

	public class TimecodeLocateEventArgs : EventArgs
	{
		public TimecodeLocateEventArgs(TimeSpan position, bool isBackward)
		{
			Position = position;
			IsBackward = isBackward;
		}

		/// <summary>The raw incoming timecode position after the locate/jump (offset-free).</summary>
		public TimeSpan Position { get; }

		/// <summary>True when the new position is earlier than the previous position.</summary>
		public bool IsBackward { get; }
	}

	/// <summary>
	/// Abstraction over an external timecode master (MIDI Timecode today; LTC / Art-Net timecode
	/// can implement this later without changing the chase layer). Implementations decode the wire
	/// protocol and expose a smooth, monotonic-between-locates <see cref="Position"/> that the chase
	/// bridge maps onto sequence time.
	/// </summary>
	public interface ITimecodeSource : IDisposable
	{
		/// <summary>
		/// Current decoded timecode position (offset-free). Must be cheap, allocation-free and
		/// thread-safe: it is polled from the engine output threads (~20 Hz) and the editor cursor
		/// timer (~25 Hz) concurrently with decode updates arriving on a device callback thread.
		/// </summary>
		TimeSpan Position { get; }

		TimecodeState State { get; }

		TimecodeFrameRate FrameRate { get; }

		bool IsOpen { get; }

		/// <summary>Open the underlying device and begin decoding. Throws on device failure.</summary>
		void Open();

		/// <summary>Stop decoding and release the device. Safe to call when already closed.</summary>
		void Close();

		/// <summary>Raised (on an arbitrary thread) when the run/stop/signal state changes.</summary>
		event EventHandler<TimecodeStateChangedEventArgs> StateChanged;

		/// <summary>
		/// Raised when the master relocates (e.g. an MTC full-frame message) or a position
		/// discontinuity is detected in the stream. Consumers re-lock — and, for the effect pump,
		/// re-prime — on this.
		/// </summary>
		event EventHandler<TimecodeLocateEventArgs> Located;
	}
}
