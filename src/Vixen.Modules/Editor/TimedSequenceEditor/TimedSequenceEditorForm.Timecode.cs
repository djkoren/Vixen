using System;
using System.Drawing;
using System.Windows.Forms;
using Common.Controls;
using VixenModules.Editor.TimedSequenceEditor.Timecode;

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
				DisplayStyle = ToolStripItemDisplayStyle.Text,
				Text = "TC",
				ToolTipText = "Chase external MIDI timecode",
				Tag = "ChaseTimecode"
			};
			_chaseButton.CheckedChanged += toolStripButton_ChaseTimecode_CheckedChanged;

			_chaseSettingsButton = new ToolStripButton
			{
				Name = "playBackToolStripButton_ChaseTimecodeSettings",
				DisplayStyle = ToolStripItemDisplayStyle.Text,
				Text = "TC…",
				ToolTipText = "Timecode chase settings",
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

			_chaseStatusLabel = new ToolStripStatusLabel
			{
				Name = "toolStripStatusLabel_chase",
				Text = string.Empty,
				BorderSides = ToolStripStatusLabelBorderSides.Left,
				Margin = new Padding(8, 1, 0, 1)
			};
			statusStrip?.Items.Add(_chaseStatusLabel);
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
			_chaseController.StatusChanged += Chase_StatusChanged;

			_chaseArmed = true;

			// Rebuild the execution context so it runs against the chase clock.
			if (_context != null)
			{
				CloseSequenceContext();
				OpenSequenceContext(_sequence);
			}

			_chaseController.Start();
			return true;
		}

		private void DisarmChase()
		{
			_chaseArmed = false;

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
				_chaseController.StatusChanged -= Chase_StatusChanged;
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
				_chaseStatusLabel.Text = string.Empty;
			}
		}

		private void OpenChaseSettings()
		{
			var settings = _chaseSettings ?? TimecodeChaseSettings.Load(TimecodeChaseSettings.SettingsPath);
			TimeSpan? currentTc = (_chaseArmed && _chaseSource != null && _chaseSource.State == TimecodeState.Running)
				? _chaseSource.Position
				: (TimeSpan?)null;

			using (var dlg = new TimecodeChaseSettingsForm(settings, currentTc))
			{
				if (dlg.ShowDialog(this) == DialogResult.OK)
				{
					settings.Save(TimecodeChaseSettings.SettingsPath);
					_chaseSettings = settings;
					// Device/frame-rate changes take effect the next time chase is armed.
				}
			}
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

		private void Chase_StatusChanged(object sender, TimecodeStatus status)
		{
			MarshalToUi(() => UpdateChaseStatusLabel(status));
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

		private void UpdateChaseStatusLabel(TimecodeStatus s)
		{
			if (_chaseStatusLabel == null) return;

			string state;
			switch (s.State)
			{
				case TimecodeState.Running: state = "LOCKED"; break;
				case TimecodeState.Freewheeling: state = "FREEWHEEL"; break;
				case TimecodeState.Stopped: state = "HOLD"; break;
				default: state = "NO TC"; break;
			}

			_chaseStatusLabel.Text = string.Format("TC: {0}  {1}  {2}", state, FormatTc(s.Incoming), FrameRateText(s.FrameRate));
		}

		private static string FormatTc(TimeSpan ts)
		{
			if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
			return string.Format("{0}:{1:00}:{2:00}.{3:000}", (int)ts.TotalHours, ts.Minutes, ts.Seconds, ts.Milliseconds);
		}

		private static string FrameRateText(TimecodeFrameRate rate)
		{
			switch (rate)
			{
				case TimecodeFrameRate.Fps24: return "24";
				case TimecodeFrameRate.Fps25: return "25";
				case TimecodeFrameRate.Fps2997Drop: return "29.97DF";
				default: return "30";
			}
		}
	}
}
