using System.IO;
using Common.Controls;
using Common.Controls.Theme;
using Common.Resources.Properties;
using NLog;
using Vixen.Common.ffmpeg;
using Vixen.Sys;

namespace VixenModules.Editor.TimedSequenceEditor.VideoExport
{
	/// <summary>
	/// Dialog for exporting a Vixen sequence preview to an MP4 video file.
	/// </summary>
	public partial class VideoExportDialog : BaseForm
	{
		private static readonly Logger Logging = LogManager.GetCurrentClassLogger();

		private readonly ISequence _sequence;
		private CancellationTokenSource _cts;
		private Task _exportTask;

		public VideoExportDialog(ISequence sequence)
		{
			_sequence = sequence;
			InitializeComponent();
			Icon = Resources.Icon_Vixen3;
			ThemeUpdateControls.UpdateControls(this);

			// Default output file: same folder as sequence, same name, .mp4 extension
			if (!string.IsNullOrEmpty(sequence.FilePath))
			{
				var dir = System.IO.Path.GetDirectoryName(sequence.FilePath);
				var name = System.IO.Path.GetFileNameWithoutExtension(sequence.FilePath);
				txtOutputPath.Text = System.IO.Path.Combine(dir ?? "", name + ".mp4");
			}

			// Default to 60fps
			cbFrameRate.SelectedItem = "60";
			chkIncludeAudio.Checked = true;

			// Populate encoder dropdown with available encoders
			PopulateEncoders();

			progressBar.Visible = false;
			lblStatus.Text = "";
		}

		private void PopulateEncoders()
		{
			cbEncoder.Items.Clear();
			var available = Ffmpeg.DetectAvailableEncoders();

			// Order: GPU encoders first (fastest), then CPU options
			var ordered = new List<(Ffmpeg.VideoEncoder encoder, string label)>();

			if (available.Contains(Ffmpeg.VideoEncoder.GpuNvidia))
				ordered.Add((Ffmpeg.VideoEncoder.GpuNvidia, "GPU: NVIDIA NVENC (fastest)"));
			if (available.Contains(Ffmpeg.VideoEncoder.GpuIntel))
				ordered.Add((Ffmpeg.VideoEncoder.GpuIntel, "GPU: Intel QuickSync (fast)"));
			if (available.Contains(Ffmpeg.VideoEncoder.GpuAmd))
				ordered.Add((Ffmpeg.VideoEncoder.GpuAmd, "GPU: AMD AMF (fast)"));

			ordered.Add((Ffmpeg.VideoEncoder.CpuFast, "CPU: Fast (lower quality)"));
			ordered.Add((Ffmpeg.VideoEncoder.CpuBalanced, "CPU: Balanced (default)"));
			ordered.Add((Ffmpeg.VideoEncoder.CpuHighQuality, "CPU: High Quality (slowest)"));

			foreach (var item in ordered)
			{
				cbEncoder.Items.Add(new EncoderItem(item.encoder, item.label));
			}

			// Default to first GPU encoder if available, otherwise CpuBalanced
			cbEncoder.SelectedIndex = 0;
			foreach (EncoderItem item in cbEncoder.Items)
			{
				if (item.Encoder == Ffmpeg.VideoEncoder.CpuBalanced && ordered[0].encoder == Ffmpeg.VideoEncoder.CpuFast)
				{
					cbEncoder.SelectedItem = item;
					break;
				}
			}
		}

		private class EncoderItem
		{
			public Ffmpeg.VideoEncoder Encoder { get; }
			public string Label { get; }
			public EncoderItem(Ffmpeg.VideoEncoder encoder, string label) { Encoder = encoder; Label = label; }
			public override string ToString() => Label;
		}

		private void btnBrowse_Click(object sender, EventArgs e)
		{
			using (var dlg = new SaveFileDialog())
			{
				dlg.Filter = "MP4 Video (*.mp4)|*.mp4";
				dlg.DefaultExt = "mp4";
				if (!string.IsNullOrEmpty(txtOutputPath.Text))
				{
					var dir = System.IO.Path.GetDirectoryName(txtOutputPath.Text);
					if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
						dlg.InitialDirectory = dir;
					dlg.FileName = System.IO.Path.GetFileName(txtOutputPath.Text);
				}

				if (dlg.ShowDialog(this) == DialogResult.OK)
				{
					txtOutputPath.Text = dlg.FileName;
				}
			}
		}

		private void btnStart_Click(object sender, EventArgs e)
		{
			if (string.IsNullOrWhiteSpace(txtOutputPath.Text))
			{
				MessageBox.Show(this, "Please choose an output file.", "Video Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			int fps = int.TryParse(cbFrameRate.SelectedItem?.ToString(), out var f) ? f : 60;
			bool includeAudio = chkIncludeAudio.Checked;
			string outputFile = txtOutputPath.Text;
			Ffmpeg.VideoEncoder encoder = (cbEncoder.SelectedItem is EncoderItem encItem)
				? encItem.Encoder
				: Ffmpeg.VideoEncoder.CpuBalanced;

			// Disable controls, enable progress
			SetExporting(true);

			_cts = new CancellationTokenSource();
			var exporter = new VideoExporter(_sequence, outputFile, fps, includeAudio, encoder);
			exporter.Progress += OnProgress;
			exporter.StatusChanged += OnStatus;

			_exportTask = Task.Run(() =>
			{
				try
				{
					exporter.Run(_cts.Token);
					BeginInvoke(new Action(() => OnExportFinished(null)));
				}
				catch (OperationCanceledException)
				{
					BeginInvoke(new Action(() => OnExportFinished("Cancelled")));
				}
				catch (Exception ex)
				{
					Logging.Error(ex, "Video export failed");
					BeginInvoke(new Action(() => OnExportFinished("Error: " + ex.Message)));
				}
			});
		}

		private void btnCancel_Click(object sender, EventArgs e)
		{
			if (_exportTask != null && !_exportTask.IsCompleted)
			{
				_cts?.Cancel();
				btnCancel.Enabled = false;
				lblStatus.Text = "Cancelling...";
			}
			else
			{
				Close();
			}
		}

		private void OnProgress(object sender, VideoExportProgressEventArgs e)
		{
			BeginInvoke(new Action(() =>
			{
				progressBar.Value = Math.Max(0, Math.Min(100, (int)e.PercentComplete));
				lblStatus.Text = $"Rendering frame {e.CurrentFrame} / {e.TotalFrames} ({e.PercentComplete:0.0}%)";
			}));
		}

		private void OnStatus(object sender, string message)
		{
			BeginInvoke(new Action(() =>
			{
				lblStatus.Text = message;
			}));
		}

		private void OnExportFinished(string error)
		{
			SetExporting(false);
			if (error == null)
			{
				lblStatus.Text = "Export complete!";
				PlayChime();
				MessageBox.Show(this,
					"Video export complete!\n\nSaved to:\n" + txtOutputPath.Text,
					"Video Export",
					MessageBoxButtons.OK, MessageBoxIcon.None);
			}
			else
			{
				// Show short message in status bar, full details in dialog
				lblStatus.Text = error.Length > 80 ? error.Substring(0, 80) + "..." : error;
				if (error != "Cancelled" && !error.StartsWith("Error: Cancelled"))
				{
					MessageBox.Show(this, error, "Video Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		/// <summary>
		/// Plays a nice completion chime. Tries Windows built-in chimes.wav first,
		/// falls back to notify.wav or a simple beep.
		/// </summary>
		private void PlayChime()
		{
			try
			{
				string windowsMediaFolder = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

				// Try these in order of preference (existence varies by Windows version)
				string[] candidates =
				{
					"chimes.wav",
					"Windows Notify System Generic.wav",
					"notify.wav",
					"tada.wav",
					"Windows Ding.wav"
				};

				foreach (var name in candidates)
				{
					string path = Path.Combine(windowsMediaFolder, name);
					if (File.Exists(path))
					{
						using var player = new System.Media.SoundPlayer(path);
						player.Play();
						return;
					}
				}

				// Fallback
				System.Media.SystemSounds.Asterisk.Play();
			}
			catch
			{
				try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
			}
		}

		private void SetExporting(bool exporting)
		{
			txtOutputPath.Enabled = !exporting;
			btnBrowse.Enabled = !exporting;
			cbFrameRate.Enabled = !exporting;
			chkIncludeAudio.Enabled = !exporting;
			cbEncoder.Enabled = !exporting;
			btnStart.Enabled = !exporting;
			btnCancel.Text = exporting ? "Cancel" : "Close";
			btnCancel.Enabled = true;
			progressBar.Visible = exporting;
			if (!exporting) progressBar.Value = 0;
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if (_exportTask != null && !_exportTask.IsCompleted)
			{
				_cts?.Cancel();
				try { _exportTask.Wait(TimeSpan.FromSeconds(5)); } catch { }
			}
			base.OnFormClosing(e);
		}
	}
}
