using System;
using System.Drawing;
using System.Windows.Forms;
using Common.Controls;
using Common.Resources.Properties;
using VixenModules.Editor.TimedSequenceEditor.Timecode;
using Timer = System.Windows.Forms.Timer;

namespace VixenModules.Editor.TimedSequenceEditor
{
	/// <summary>
	/// Timecode-chase wiring for the timed sequence editor. This is an optional, armed mode: while it
	/// is not armed, none of this code path runs and playback/editing behave exactly as before. When
	/// armed, the sequence is executed against an external MIDI Timecode clock (see the Timecode/
	/// folder), the transport is driven by the incoming timecode, and manual play/pause is suppressed.
	/// </summary>
	public partial class TimedSequenceEditorForm
	{
		private ToolStripButton _chaseButton;
		private ToolStripButton _chaseSettingsButton;
		private ToolStripStatusLabel _chaseStatusLabel;

		/// <summary>
		/// Refreshes the status readout. The controller's own status callback only fires a few times a
		/// second, which is far too coarse for a frame counter, so the label is repainted from a UI
		/// timer instead and reads the source's interpolated position directly.
		/// </summary>
		private Timer _chaseDisplayTimer;

		private string _chaseStatusText = string.Empty;

		private bool _chaseArmed;
		private bool _suppressChaseToggle;

		private TimecodeChaseSettings _chaseSettings;
		private MtcTimecodeSource _chaseSource;
		private TimecodeChaseTiming _chaseTiming;
		private TimecodeChaseController _chaseController;
		private TimecodeChaseExecutor _chaseExecutor;

		/// <summary>Called from the constructor after InitializeComponent to add the chase toolbar UI.</summary>
		private void InitializeTimecodeChaseUi()
		{
			_chaseButton = new ToolStripButton
			{
				Name = "playBackToolStripButton_ChaseTimecode",
				CheckOnClick = true,
				DisplayStyle = ToolStripItemDisplayStyle.Image,
				Image = Resources.timecode_chase,
				ImageTransparentColor = Color.Magenta,
				Text = "Timecode",
				ToolTipText = "Follow external timecode with timeline playback",
				Tag = "ChaseTimecode"
			};
			_chaseButton.CheckedChanged += toolStripButton_ChaseTimecode_CheckedChanged;

			_chaseSettingsButton = new ToolStripButton
			{
				Name = "playBackToolStripButton_ChaseTimecodeSettings",
				DisplayStyle = ToolStripItemDisplayStyle.Image,
				Image = Resources.timecode_chase_settings,
				ImageTransparentColor = Color.Magenta,
				Text = "TC Settings",
				ToolTipText = "Timecode settings",
				Tag = "ChaseTimecodeSettings"
			};
			_chaseSettingsButton.Click += (s, e) => OpenChaseSettings();

			var owner = playBackToolStripButton_Loop?.Owner;
			if (owner != null)
			{
				int idx = owner.Items.IndexOf(playBackToolStripButton_Loop);
				owner.Items.Insert(idx + 1, _chaseButton);
				owner.Items.Insert(idx + 2, _chaseSettingsButton);
			}

			// A fixed width keeps the status strip from re-flowing every time a digit changes, which is
			// what made the readout look like it was stuttering even when the clock was steady.
			_chaseStatusLabel = new ToolStripStatusLabel
			{
				Name = "toolStripStatusLabel_chase",
				Text = string.Empty,
				AutoSize = false,
				Width = 230,
				TextAlign = ContentAlignment.MiddleLeft,
				BorderSides = ToolStripStatusLabelBorderSides.Left,
				Margin = new Padding(8, 1, 0, 1)
			};
			statusStrip?.Items.Add(_chaseStatusLabel);

			_chaseDisplayTimer = new Timer(components) { Interval = 25 };
			_chaseDisplayTimer.Tick += ChaseDisplayTimer_Tick;
		}

		private void toolStripButton_ChaseTimecode_CheckedChanged(object sender, EventArgs e)
		{
			if (_chaseButton == null) return;

			if (_chaseButton.Checked)
			{
				if (!ArmChase())
				{
					// Arming failed: revert the toggle without re-entering disarm.
					_suppressChaseToggle = true;
					_chaseButton.Checked = false;
					_suppressChaseToggle = false;
				}
			}
			else if (!_suppressChaseToggle)
			{
				DisarmChase();
			}
		}

		private bool ArmChase()
		{
			if (_sequence == null)
			{
				return false;
			}

			try
			{
				_chaseSettings = TimecodeChaseSettings.Load(TimecodeChaseSettings.SettingsPath);
				_chaseSource = new MtcTimecodeSource(_chaseSettings.MidiInputDeviceName, _chaseSettings.AutoFrameRate,
					_chaseSettings.ForcedFrameRate, _chaseSettings.FreewheelFrames);
				_chaseSource.Open();
			}
			catch (Exception ex)
			{
				Logging.Error(ex, "Timecode chase: failed to open MIDI input.");
				_chaseSource?.Dispose();
				_chaseSource = null;
				MessageBoxForm.msgIcon = SystemIcons.Error;
				var mb = new MessageBoxForm("Could not start timecode chase:\n\n" + ex.Message, "Timecode Chase", false, false);
				mb.ShowDialog(this);
				return false;
			}

			_chaseTiming = new TimecodeChaseTiming(_chaseSource, _chaseSettings, _sequence.Length);
			_chaseController = new TimecodeChaseController(_chaseSource, _chaseTiming, _chaseSettings);
			_chaseController.ChaseStart += Chase_Start;
			_chaseController.ChaseHoldRequested += Chase_Hold;
			_chaseController.ChaseStopRequested += Chase_Stop;
			_chaseController.ChaseRePrimeRequested += Chase_RePrime;

			_chaseArmed = true;

			// Rebuild the execution context so it runs against the chase clock.
			if (_context != null)
			{
				CloseSequenceContext();
				OpenSequenceContext(_sequence);
			}

			_chaseController.Start();
			_chaseDisplayTimer?.Start();
			UpdateChaseStatusLabel();
			return true;
		}

		private void DisarmChase()
		{
			_chaseArmed = false;
			_chaseDisplayTimer?.Stop();

			try
			{
				if (_context != null && _context.IsRunning)
				{
					_context.Stop();
				}
			}
			catch (Exception ex)
			{
				Logging.Error(ex, "Timecode chase: error stopping context on disarm.");
			}

			if (_chaseController != null)
			{
				_chaseController.ChaseStart -= Chase_Start;
				_chaseController.ChaseHoldRequested -= Chase_Hold;
				_chaseController.ChaseStopRequested -= Chase_Stop;
				_chaseController.ChaseRePrimeRequested -= Chase_RePrime;
				_chaseController.Dispose();
				_chaseController = null;
			}

			_chaseSource?.Dispose();
			_chaseSource = null;
			_chaseTiming = null;
			_chaseExecutor = null;

			// Rebuild a normal (non-chase) context.
			if (_context != null)
			{
				CloseSequenceContext();
				OpenSequenceContext(_sequence);
			}

			if (_chaseStatusLabel != null)
			{
				_chaseStatusText = string.Empty;
				_chaseStatusLabel.Text = string.Empty;
			}
		}

		private void OpenChaseSettings()
		{
			var settings = _chaseSettings ?? TimecodeChaseSettings.Load(TimecodeChaseSettings.SettingsPath);
			TimeSpan? currentTc = (_chaseArmed && _chaseSource != null && _chaseSource.State == TimecodeState.Running)
				? _chaseSource.Position
				: (TimeSpan?)null;

			// The decoder captures the device, frame-rate mode and freewheel window when it is opened, so
			// a change to any of them needs the source rebuilt. Offset and latency trim are read live off
			// the settings object and need no restart.
			string previousDevice = settings.MidiInputDeviceName;
			TimecodeFrameRateMode previousRateMode = settings.FrameRateMode;
			int previousFreewheel = settings.FreewheelFrames;

			using (var dlg = new TimecodeChaseSettingsForm(settings, currentTc))
			{
				if (dlg.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}
			}

			settings.Save(TimecodeChaseSettings.SettingsPath);
			_chaseSettings = settings;

			bool decoderChanged = !string.Equals(previousDevice, settings.MidiInputDeviceName, StringComparison.Ordinal)
			                      || previousRateMode != settings.FrameRateMode
			                      || previousFreewheel != settings.FreewheelFrames;

			if (_chaseArmed && decoderChanged)
			{
				ReArmChase();
			}
		}

		/// <summary>
		/// Rebuilds the timecode source in place so a settings change applies without the user having to
		/// toggle chase off and on. Leaves chase disarmed (and the toolbar button unchecked) if the
		/// device cannot be reopened.
		/// </summary>
		private void ReArmChase()
		{
			DisarmChase();

			if (ArmChase()) return;

			_suppressChaseToggle = true;
			if (_chaseButton != null) _chaseButton.Checked = false;
			_suppressChaseToggle = false;
		}

		#region Controller callbacks (raised off the UI thread; marshal before touching the UI/transport)

		private void Chase_Start(object sender, TimeSpan seed)
		{
			MarshalToUi(() => { if (_chaseArmed) PlaySequenceFrom(seed); });
		}

		private void Chase_Hold(object sender, EventArgs e)
		{
			// Hold policy: the bridge freezes its position, so the executor keeps re-rendering the
			// last look and output holds. Nothing to do on the transport.
		}

		private void Chase_Stop(object sender, EventArgs e)
		{
			MarshalToUi(() => { if (_chaseArmed) StopSequence(); });
		}

		private void Chase_RePrime(object sender, EventArgs e)
		{
			MarshalToUi(() => _chaseExecutor?.RePrime());
		}

		#endregion

		private void MarshalToUi(Action action)
		{
			if (IsDisposed || Disposing || !IsHandleCreated) return;
			try
			{
				BeginInvoke(action);
			}
			catch
			{
				// Handle destroyed between the checks and the call; safe to drop.
			}
		}

		private void ChaseDisplayTimer_Tick(object sender, EventArgs e)
		{
			UpdateChaseStatusLabel();
		}

		/// <summary>
		/// Repaints the timecode readout from the live decoder state. Called from a UI timer rather than
		/// the controller's status callback so the frame field ticks at the incoming rate instead of the
		/// controller's much slower status interval. The text is only assigned when it actually changes,
		/// so a held clock costs nothing.
		/// </summary>
		private void UpdateChaseStatusLabel()
		{
			if (_chaseStatusLabel == null) return;

			var source = _chaseSource;
			if (!_chaseArmed || source == null)
			{
				if (_chaseStatusText.Length == 0) return;
				_chaseStatusText = string.Empty;
				_chaseStatusLabel.Text = string.Empty;
				return;
			}

			TimecodeFrameRate rate = source.FrameRate;

			string state;
			switch (source.State)
			{
				case TimecodeState.Running: state = "LOCKED"; break;
				case TimecodeState.Freewheeling: state = "FREEWHEEL"; break;
				case TimecodeState.Stopped: state = "HOLD"; break;
				default: state = "NO TC"; break;
			}

			// In auto-detect mode the rate is a placeholder until a whole timecode word has been read, so
			// show that rather than a number the master has not actually claimed.
			string rateText = source.IsFrameRateKnown ? rate.DisplayName() : "--";

			string text = string.Format("TC: {0}  {1}  {2}", state, rate.ToSmpteString(source.Position), rateText);
			if (string.Equals(text, _chaseStatusText, StringComparison.Ordinal)) return;

			_chaseStatusText = text;
			_chaseStatusLabel.Text = text;
		}
	}
}
