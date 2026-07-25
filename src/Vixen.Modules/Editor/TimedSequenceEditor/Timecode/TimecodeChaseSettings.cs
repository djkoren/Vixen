using System;
using System.IO;
using System.Xml.Serialization;
using Common.Preferences;

namespace VixenModules.Editor.TimedSequenceEditor.Timecode
{
	public enum TimecodeFrameRateMode
	{
		Auto,
		Fps24,
		Fps25,
		Fps2997Drop,
		Fps30
	}

	public enum TimecodeStopPolicy
	{
		/// <summary>Freeze the last rendered look when timecode stops.</summary>
		Hold,

		/// <summary>Stop the sequence (blackout to no-effect state) when timecode stops.</summary>
		Stop
	}

	/// <summary>
	/// Per-workstation timecode-chase configuration. Persisted at app level (not on the sequence) so
	/// arming chase never mutates sequence content or its selected timing provider.
	/// </summary>
	[Serializable, XmlType(TypeName = "TimecodeChaseSettings")]
	public class TimecodeChaseSettings : PreferenceBase<TimecodeChaseSettings>
	{
		public static readonly string SettingsPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vixen", "TimecodeChase.settings");

		public override void SetStandardValues()
		{
			MidiInputDeviceName = string.Empty;
			FrameRateMode = TimecodeFrameRateMode.Auto;
			OffsetTicks = 0;
			LatencyTrimMs = 0;
			FreewheelFrames = 5;
			StopPolicy = TimecodeStopPolicy.Hold;
		}

		public string MidiInputDeviceName { get; set; } = string.Empty;

		public TimecodeFrameRateMode FrameRateMode { get; set; }

		/// <summary>Show timecode (in ticks) that maps to sequence time zero.</summary>
		public long OffsetTicks { get; set; }

		/// <summary>+/- latency calibration applied to the incoming clock, in milliseconds.</summary>
		public int LatencyTrimMs { get; set; }

		/// <summary>Number of frames to coast (interpolate) across a timecode dropout before holding.</summary>
		public int FreewheelFrames { get; set; } = 5;

		public TimecodeStopPolicy StopPolicy { get; set; } = TimecodeStopPolicy.Hold;

		[XmlIgnore]
		public TimeSpan Offset
		{
			get => TimeSpan.FromTicks(OffsetTicks);
			set => OffsetTicks = value.Ticks;
		}

		[XmlIgnore]
		public bool AutoFrameRate => FrameRateMode == TimecodeFrameRateMode.Auto;

		[XmlIgnore]
		public TimecodeFrameRate ForcedFrameRate
		{
			get
			{
				switch (FrameRateMode)
				{
					case TimecodeFrameRateMode.Fps24: return TimecodeFrameRate.Fps24;
					case TimecodeFrameRateMode.Fps25: return TimecodeFrameRate.Fps25;
					case TimecodeFrameRateMode.Fps2997Drop: return TimecodeFrameRate.Fps2997Drop;
					default: return TimecodeFrameRate.Fps30;
				}
			}
		}
	}
}
