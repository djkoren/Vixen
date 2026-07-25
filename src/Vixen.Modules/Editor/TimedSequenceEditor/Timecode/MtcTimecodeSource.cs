using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace VixenModules.Editor.TimedSequenceEditor.Timecode
{
	/// <summary>
	/// Decodes MIDI Timecode (MTC) from a Windows MIDI input device into a smooth,
	/// monotonic-between-locates clock. MIDI input is read directly from <c>winmm.dll</c> (no external
	/// package dependency); quarter-frame (0xF1) messages are the lock source. Between decoded frames
	/// the position is interpolated with a <see cref="Stopwatch"/>, short dropouts are freewheeled
	/// before the clock is declared stopped, and forward/backward discontinuities raise
	/// <see cref="Located"/>. All public reads are lock-free and thread-safe (polled from engine + UI
	/// threads while decode updates arrive on the MIDI callback thread).
	///
	/// Full-frame SysEx locates are intentionally not consumed in v1: jumps are recovered from the
	/// quarter-frame stream via delta detection, which covers scrub/jump-then-play in either
	/// direction. SysEx pre-positioning is a possible future enhancement.
	/// </summary>
	public sealed class MtcTimecodeSource : ITimecodeSource
	{
		// Immutable clock anchor, swapped atomically via a volatile reference so the hot Position
		// getter never locks or tears.
		private sealed class Anchor
		{
			public readonly long TcTicks;   // decoded timecode ticks at the anchor instant
			public readonly long Stamp;      // Stopwatch.GetTimestamp() at the anchor instant
			public readonly bool Advancing;  // extrapolate forward from the anchor while true

			public Anchor(long tcTicks, long stamp, bool advancing)
			{
				TcTicks = tcTicks;
				Stamp = stamp;
				Advancing = advancing;
			}
		}

		private static readonly long StopwatchFrequency = Stopwatch.Frequency;

		private readonly string _deviceName;
		private readonly bool _autoFrameRate;
		private readonly TimecodeFrameRate _forcedFrameRate;
		private readonly int _freewheelFrames;

		private readonly object _decodeLock = new object();
		private readonly byte[] _pieces = new byte[8];
		private int _seenMask;

		private IntPtr _handle = IntPtr.Zero;
		private MidiInProc? _callback;      // kept referenced so it is not collected while open
		private GCHandle _callbackHandle;
		private System.Threading.Timer? _watchdog;
		private volatile bool _isOpen;

		private volatile Anchor _anchor;
		private long _lastMessageStamp;
		private volatile TimecodeState _state = TimecodeState.NoSignal;
		private volatile TimecodeFrameRate _frameRate;

		public event EventHandler<TimecodeStateChangedEventArgs>? StateChanged;
		public event EventHandler<TimecodeLocateEventArgs>? Located;

		public MtcTimecodeSource(string deviceName, bool autoFrameRate, TimecodeFrameRate forcedFrameRate, int freewheelFrames)
		{
			_deviceName = deviceName ?? string.Empty;
			_autoFrameRate = autoFrameRate;
			_forcedFrameRate = forcedFrameRate;
			_frameRate = forcedFrameRate;
			_freewheelFrames = Math.Max(1, freewheelFrames);
			_anchor = new Anchor(0, Stopwatch.GetTimestamp(), false);
		}

		public bool IsOpen => _isOpen;

		public TimecodeState State => _state;

		public TimecodeFrameRate FrameRate => _frameRate;

		public TimeSpan Position
		{
			get
			{
				var a = _anchor;
				if (!a.Advancing)
				{
					return TimeSpan.FromTicks(a.TcTicks);
				}

				long elapsed = Stopwatch.GetTimestamp() - a.Stamp;
				if (elapsed < 0) elapsed = 0;

				// Clamp extrapolation so a stall can never run the clock away.
				long maxElapsed = (long)(StopwatchFrequency * (_freewheelFrames + 2) / Math.Max(1.0, _frameRate.ToFps()));
				if (elapsed > maxElapsed) elapsed = maxElapsed;

				long ticks = a.TcTicks + elapsed * TimeSpan.TicksPerSecond / StopwatchFrequency;
				return TimeSpan.FromTicks(ticks);
			}
		}

		public void Open()
		{
			if (_isOpen) return;

			int deviceIndex = FindDevice(_deviceName);
			if (deviceIndex < 0)
			{
				throw new InvalidOperationException(
					$"MIDI input device '{_deviceName}' was not found. Check that it is connected and selected in Timecode Chase settings.");
			}

			_callback = MidiCallback;
			_callbackHandle = GCHandle.Alloc(_callback);

			int result = midiInOpen(out _handle, deviceIndex, _callback, IntPtr.Zero, CALLBACK_FUNCTION);
			if (result != MMSYSERR_NOERROR)
			{
				_callbackHandle.Free();
				_callback = null;
				_handle = IntPtr.Zero;
				throw new InvalidOperationException($"Failed to open MIDI input device '{_deviceName}' (winmm error {result}).");
			}

			midiInStart(_handle);

			_isOpen = true;
			Volatile.Write(ref _lastMessageStamp, Stopwatch.GetTimestamp());
			lock (_decodeLock) { _seenMask = 0; }
			SetState(TimecodeState.NoSignal);

			_watchdog = new System.Threading.Timer(WatchdogTick, null, 15, 15);
		}

		public void Close()
		{
			_isOpen = false;

			var watchdog = _watchdog;
			_watchdog = null;
			watchdog?.Dispose();

			if (_handle != IntPtr.Zero)
			{
				try
				{
					midiInStop(_handle);
					midiInReset(_handle);
					midiInClose(_handle);
				}
				catch { /* ignore device teardown races */ }
				_handle = IntPtr.Zero;
			}

			if (_callbackHandle.IsAllocated) _callbackHandle.Free();
			_callback = null;

			SetState(TimecodeState.NoSignal);
		}

		public void Dispose()
		{
			Close();
		}

		/// <summary>Enumerate the names of the available MIDI input devices (for the settings UI).</summary>
		public static string[] GetInputDeviceNames()
		{
			int n = midiInGetNumDevs();
			var list = new List<string>(Math.Max(0, n));
			for (int i = 0; i < n; i++)
			{
				var caps = new MIDIINCAPS();
				if (midiInGetDevCaps((IntPtr)i, ref caps, Marshal.SizeOf<MIDIINCAPS>()) == MMSYSERR_NOERROR)
				{
					list.Add(caps.szPname);
				}
			}
			return list.ToArray();
		}

		private static int FindDevice(string name)
		{
			int count = midiInGetNumDevs();
			if (count <= 0) return -1;

			if (string.IsNullOrWhiteSpace(name))
			{
				return 0; // nothing configured -> first available device
			}

			var names = new string[count];
			for (int i = 0; i < count; i++)
			{
				var caps = new MIDIINCAPS();
				names[i] = midiInGetDevCaps((IntPtr)i, ref caps, Marshal.SizeOf<MIDIINCAPS>()) == MMSYSERR_NOERROR
					? caps.szPname : string.Empty;
			}

			// Exact match first (device names may be truncated to 31 chars by the driver).
			for (int i = 0; i < count; i++)
			{
				if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
			}
			// Then a prefix match either way, to tolerate truncation.
			for (int i = 0; i < count; i++)
			{
				if (names[i].Length > 0 &&
				    (names[i].StartsWith(name, StringComparison.OrdinalIgnoreCase) ||
				     name.StartsWith(names[i], StringComparison.OrdinalIgnoreCase)))
					return i;
			}
			return -1;
		}

		private void MidiCallback(IntPtr handle, int wMsg, IntPtr instance, IntPtr param1, IntPtr param2)
		{
			if (wMsg != MIM_DATA) return;
			try
			{
				long msg = param1.ToInt64();
				int status = (int)(msg & 0xFF);
				if (status == 0xF1) // MTC quarter-frame
				{
					byte data = (byte)((msg >> 8) & 0x7F);
					OnQuarterFrame(data);
				}
			}
			catch { /* never let a decode error escape into the driver callback */ }
		}

		private void OnQuarterFrame(byte data)
		{
			int index = (data >> 4) & 0x07;
			int nibble = data & 0x0F;

			bool complete = false;
			int hour = 0, min = 0, sec = 0, frame = 0, rateBits = 0;

			lock (_decodeLock)
			{
				_pieces[index] = (byte)nibble;
				_seenMask |= 1 << index;

				if (index == 7)
				{
					if (_seenMask == 0xFF)
					{
						frame = _pieces[0] | ((_pieces[1] & 0x1) << 4);
						sec = _pieces[2] | ((_pieces[3] & 0x3) << 4);
						min = _pieces[4] | ((_pieces[5] & 0x3) << 4);
						hour = _pieces[6] | ((_pieces[7] & 0x1) << 4);
						rateBits = (_pieces[7] >> 1) & 0x03;
						complete = true;
					}
					_seenMask = 0; // begin the next collection
				}
			}

			Volatile.Write(ref _lastMessageStamp, Stopwatch.GetTimestamp());

			if (complete)
			{
				var rate = _autoFrameRate ? RateFromBits(rateBits) : _forcedFrameRate;
				if (rate != _frameRate)
				{
					_frameRate = rate;
					RaiseStateChanged();
				}

				// The assembled value is 2 frames old when read forward; advance it to "now".
				long ticks = SmpteToTicks(hour, min, sec, frame, rate) + 2 * rate.FrameDuration().Ticks;
				PublishAnchor(ticks, advancing: true);

				// Only announce Running once we have a real position to seed from.
				MarkRunning();
			}
		}

		private void PublishAnchor(long ticks, bool advancing)
		{
			long stamp = Stopwatch.GetTimestamp();

			long currentTicks = Position.Ticks;
			long delta = ticks - currentTicks;
			long jumpTicks = (long)(1.5 * _frameRate.FrameDuration().Ticks);
			bool isJump = delta < -jumpTicks || delta > jumpTicks;

			_anchor = new Anchor(ticks, stamp, advancing);

			if (isJump)
			{
				Located?.Invoke(this, new TimecodeLocateEventArgs(TimeSpan.FromTicks(ticks), isBackward: delta < 0));
			}
		}

		private void WatchdogTick(object? state)
		{
			if (!_isOpen) return;

			long now = Stopwatch.GetTimestamp();
			long gap = now - Volatile.Read(ref _lastMessageStamp);
			double gapSeconds = (double)gap / StopwatchFrequency;
			double frameSeconds = 1.0 / _frameRate.ToFps();

			var current = _state;
			if (current == TimecodeState.Running || current == TimecodeState.Freewheeling)
			{
				if (gapSeconds > _freewheelFrames * frameSeconds)
				{
					// Lost the master -> freeze at the last interpolated position.
					long frozen = Position.Ticks;
					_anchor = new Anchor(frozen, now, false);
					SetState(TimecodeState.Stopped);
				}
				else if (gapSeconds > 1.5 * frameSeconds)
				{
					SetState(TimecodeState.Freewheeling);
				}
			}
		}

		private void MarkRunning()
		{
			if (_state != TimecodeState.Running)
			{
				SetState(TimecodeState.Running);
			}
		}

		private void SetState(TimecodeState newState)
		{
			if (_state == newState) return;
			_state = newState;
			RaiseStateChanged();
		}

		private void RaiseStateChanged()
		{
			StateChanged?.Invoke(this, new TimecodeStateChangedEventArgs(_state, _frameRate));
		}

		private static TimecodeFrameRate RateFromBits(int rateBits)
		{
			switch (rateBits & 0x03)
			{
				case 0: return TimecodeFrameRate.Fps24;
				case 1: return TimecodeFrameRate.Fps25;
				case 2: return TimecodeFrameRate.Fps2997Drop;
				default: return TimecodeFrameRate.Fps30;
			}
		}

		private static long SmpteToTicks(int hours, int minutes, int seconds, int frames, TimecodeFrameRate rate)
		{
			if (rate == TimecodeFrameRate.Fps2997Drop)
			{
				int totalMinutes = hours * 60 + minutes;
				long droppedFrames = 2L * (totalMinutes - totalMinutes / 10);
				long frameNumber = ((long)(hours * 3600 + minutes * 60 + seconds) * 30 + frames) - droppedFrames;
				// realSeconds = frameNumber * 1001 / 30000
				return frameNumber * 1001L * TimeSpan.TicksPerSecond / 30000L;
			}

			int fps = rate.NominalFrames();
			long totalFrames = (long)(hours * 3600 + minutes * 60 + seconds) * fps + frames;
			return totalFrames * TimeSpan.TicksPerSecond / fps;
		}

		#region winmm interop

		private const int MMSYSERR_NOERROR = 0;
		private const int CALLBACK_FUNCTION = 0x00030000;
		private const int MIM_DATA = 0x3C3;

		private delegate void MidiInProc(IntPtr hMidiIn, int wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		private struct MIDIINCAPS
		{
			public short wMid;
			public short wPid;
			public int vDriverVersion;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
			public string szPname;
			public int dwSupport;
		}

		[DllImport("winmm.dll")]
		private static extern int midiInGetNumDevs();

		[DllImport("winmm.dll", CharSet = CharSet.Auto)]
		private static extern int midiInGetDevCaps(IntPtr uDeviceID, ref MIDIINCAPS caps, int cbMidiInCaps);

		[DllImport("winmm.dll")]
		private static extern int midiInOpen(out IntPtr lphMidiIn, int uDeviceID, MidiInProc dwCallback, IntPtr dwInstance, int dwFlags);

		[DllImport("winmm.dll")]
		private static extern int midiInStart(IntPtr hMidiIn);

		[DllImport("winmm.dll")]
		private static extern int midiInStop(IntPtr hMidiIn);

		[DllImport("winmm.dll")]
		private static extern int midiInReset(IntPtr hMidiIn);

		[DllImport("winmm.dll")]
		private static extern int midiInClose(IntPtr hMidiIn);

		#endregion
	}
}
