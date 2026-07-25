using System;
using System.Drawing;
using System.Windows.Forms;

namespace VixenModules.Editor.TimedSequenceEditor.Timecode
{
	/// <summary>
	/// Settings dialog for timecode chase: MIDI input device, frame-rate mode, offset (with a
	/// "capture current TC" helper), latency trim, freewheel frames, and the stop policy. Built in
	/// code (no designer/resx) so it stays self-contained. Reads/writes a <see cref="TimecodeChaseSettings"/>.
	/// </summary>
	public sealed class TimecodeChaseSettingsForm : Form
	{
		private readonly TimecodeChaseSettings _settings;
		private readonly TimeSpan? _currentIncomingTc;

		private ComboBox _deviceCombo = null!;
		private ComboBox _frameRateCombo = null!;
		private TextBox _offsetText = null!;
		private Button _captureButton = null!;
		private NumericUpDown _latencyTrim = null!;
		private NumericUpDown _freewheel = null!;
		private ComboBox _stopPolicyCombo = null!;

		private static readonly string[] FrameRateItems = { "Auto-detect", "24 fps", "25 fps", "29.97 fps (drop)", "30 fps" };

		public TimecodeChaseSettingsForm(TimecodeChaseSettings settings, TimeSpan? currentIncomingTc = null)
		{
			_settings = settings ?? throw new ArgumentNullException(nameof(settings));
			_currentIncomingTc = currentIncomingTc;
			BuildLayout();
			LoadFromSettings();
		}

		private void BuildLayout()
		{
			Text = "Timecode Chase Settings";
			FormBorderStyle = FormBorderStyle.FixedDialog;
			StartPosition = FormStartPosition.CenterParent;
			MinimizeBox = false;
			MaximizeBox = false;
			ClientSize = new Size(420, 260);

			int labelX = 12, ctrlX = 150, y = 15, rowH = 32, ctrlW = 240;

			AddLabel("MIDI input device:", labelX, y + 3);
			_deviceCombo = new ComboBox { Left = ctrlX, Top = y, Width = ctrlW, DropDownStyle = ComboBoxStyle.DropDownList };
			Controls.Add(_deviceCombo);
			y += rowH;

			AddLabel("Frame rate:", labelX, y + 3);
			_frameRateCombo = new ComboBox { Left = ctrlX, Top = y, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
			_frameRateCombo.Items.AddRange(FrameRateItems);
			Controls.Add(_frameRateCombo);
			y += rowH;

			AddLabel("Offset (h:mm:ss.fff):", labelX, y + 3);
			_offsetText = new TextBox { Left = ctrlX, Top = y, Width = 120 };
			Controls.Add(_offsetText);
			_captureButton = new Button { Left = ctrlX + 128, Top = y - 1, Width = 112, Text = "Capture current" };
			_captureButton.Click += CaptureButton_Click;
			_captureButton.Enabled = _currentIncomingTc.HasValue;
			Controls.Add(_captureButton);
			y += rowH;

			AddLabel("Latency trim (ms):", labelX, y + 3);
			_latencyTrim = new NumericUpDown { Left = ctrlX, Top = y, Width = 80, Minimum = -1000, Maximum = 1000 };
			Controls.Add(_latencyTrim);
			y += rowH;

			AddLabel("Freewheel (frames):", labelX, y + 3);
			_freewheel = new NumericUpDown { Left = ctrlX, Top = y, Width = 80, Minimum = 1, Maximum = 120 };
			Controls.Add(_freewheel);
			y += rowH;

			AddLabel("On timecode stop:", labelX, y + 3);
			_stopPolicyCombo = new ComboBox { Left = ctrlX, Top = y, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
			_stopPolicyCombo.Items.AddRange(new object[] { "Hold last look", "Stop sequence" });
			Controls.Add(_stopPolicyCombo);
			y += rowH + 6;

			var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = ClientSize.Width - 176, Top = y, Width = 76 };
			ok.Click += Ok_Click;
			var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = ClientSize.Width - 92, Top = y, Width = 76 };
			Controls.Add(ok);
			Controls.Add(cancel);
			AcceptButton = ok;
			CancelButton = cancel;
		}

		private void AddLabel(string text, int x, int y)
		{
			Controls.Add(new Label { Text = text, Left = x, Top = y, Width = 135, AutoSize = false });
		}

		private void LoadFromSettings()
		{
			_deviceCombo.Items.Clear();
			try
			{
				_deviceCombo.Items.AddRange(MtcTimecodeSource.GetInputDeviceNames());
			}
			catch
			{
				// Enumeration failure should not break the dialog.
			}

			if (!string.IsNullOrEmpty(_settings.MidiInputDeviceName))
			{
				int idx = _deviceCombo.Items.IndexOf(_settings.MidiInputDeviceName);
				if (idx < 0) { _deviceCombo.Items.Add(_settings.MidiInputDeviceName); idx = _deviceCombo.Items.Count - 1; }
				_deviceCombo.SelectedIndex = idx;
			}
			else if (_deviceCombo.Items.Count > 0)
			{
				_deviceCombo.SelectedIndex = 0;
			}

			_frameRateCombo.SelectedIndex = (int)_settings.FrameRateMode; // enum order matches item order
			_offsetText.Text = FormatOffset(_settings.Offset);
			_latencyTrim.Value = Clamp(_settings.LatencyTrimMs, (int)_latencyTrim.Minimum, (int)_latencyTrim.Maximum);
			_freewheel.Value = Clamp(_settings.FreewheelFrames, (int)_freewheel.Minimum, (int)_freewheel.Maximum);
			_stopPolicyCombo.SelectedIndex = (int)_settings.StopPolicy;
		}

		private void CaptureButton_Click(object? sender, EventArgs e)
		{
			if (_currentIncomingTc.HasValue)
			{
				_offsetText.Text = FormatOffset(_currentIncomingTc.Value);
			}
		}

		private void Ok_Click(object? sender, EventArgs e)
		{
			if (!TryParseOffset(_offsetText.Text, out TimeSpan offset))
			{
				MessageBox.Show(this, "Offset must be in the form h:mm:ss or h:mm:ss.fff.", "Invalid offset",
					MessageBoxButtons.OK, MessageBoxIcon.Warning);
				DialogResult = DialogResult.None;
				return;
			}

			_settings.MidiInputDeviceName = _deviceCombo.SelectedItem?.ToString() ?? string.Empty;
			_settings.FrameRateMode = (TimecodeFrameRateMode)Math.Max(0, _frameRateCombo.SelectedIndex);
			_settings.Offset = offset;
			_settings.LatencyTrimMs = (int)_latencyTrim.Value;
			_settings.FreewheelFrames = (int)_freewheel.Value;
			_settings.StopPolicy = (TimecodeStopPolicy)Math.Max(0, _stopPolicyCombo.SelectedIndex);
		}

		private static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);

		private static string FormatOffset(TimeSpan ts)
		{
			return string.Format("{0}:{1:00}:{2:00}.{3:000}", (int)ts.TotalHours, ts.Minutes, ts.Seconds, ts.Milliseconds);
		}

		private static bool TryParseOffset(string text, out TimeSpan value)
		{
			value = TimeSpan.Zero;
			if (string.IsNullOrWhiteSpace(text)) return true; // empty == zero offset
			return TimeSpan.TryParse(text.Trim(), out value);
		}
	}
}
