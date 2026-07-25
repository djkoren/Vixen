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

		/// <summary>Short display label for the rate, e.g. "25" or "29.97DF".</summary>
		public static string DisplayName(this TimecodeFrameRate rate)
		{
			switch (rate)
			{
				case TimecodeFrameRate.Fps24: return "24";
				case TimecodeFrameRate.Fps25: return "25";
				case TimecodeFrameRate.Fps2997Drop: return "29.97DF";
				default: return "30";
			}
		}

		/// <summary>
		/// Formats an elapsed position as an SMPTE address (HH:MM:SS:FF) at this frame rate. The frame
		/// field counts whole frames at the rate, so the display advances in the same steps the master
		/// does. 29.97 uses drop-frame numbering (two addresses skipped at the start of every minute
		/// except every tenth), which is what keeps a drop-frame display matching the master instead of
		/// drifting roughly 3.6 seconds per hour away from it.
		/// </summary>
		/// <param name="rate">Frame rate the position is being addressed at.</param>
		/// <param name="position">Elapsed position to format. Negative values are clamped to zero.</param>
		/// <returns>The position as an <c>HH:MM:SS:FF</c> string.</returns>
		public static string ToSmpteString(this TimecodeFrameRate rate, TimeSpan position)
		{
			if (position < TimeSpan.Zero) position = TimeSpan.Zero;

			int nominal = rate.NominalFrames();
			long frameNumber;

			if (rate == TimecodeFrameRate.Fps2997Drop)
			{
				// Elapsed real frames at exactly 30000/1001 fps, then renumbered to drop-frame addresses.
				frameNumber = position.Ticks * 30000L / (1001L * TimeSpan.TicksPerSecond);

				const long framesPerTenMinutes = 17982; // real frames in ten minutes of drop-frame
				const long framesPerMinute = 1798;      // real frames per minute after the first two are dropped
				long tenMinuteBlocks = frameNumber / framesPerTenMinutes;
				long remainder = frameNumber % framesPerTenMinutes;

				frameNumber += 18 * tenMinuteBlocks;
				if (remainder >= 2)
				{
					frameNumber += 2 * ((remainder - 2) / framesPerMinute);
				}
			}
			else
			{
				frameNumber = position.Ticks * nominal / TimeSpan.TicksPerSecond;
			}

			int frames = (int)(frameNumber % nominal);
			long totalSeconds = frameNumber / nominal;
			int seconds = (int)(totalSeconds % 60);
			long totalMinutes = totalSeconds / 60;
			int minutes = (int)(totalMinutes % 60);
			long hours = totalMinutes / 60;

			return string.Format("{0:00}:{1:00}:{2:00}:{3:00}", hours, minutes, seconds, frames);
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

		/// <summary>
		/// True once <see cref="FrameRate"/> reflects the real rate: always true when the rate is forced
		/// in settings, and true in auto-detect mode only after a complete timecode word has been read.
		/// Until then auto-detect is reporting a placeholder, and callers should say so rather than
		/// display a rate the master has not actually claimed.
		/// </summary>
		bool IsFrameRateKnown { get; }

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
