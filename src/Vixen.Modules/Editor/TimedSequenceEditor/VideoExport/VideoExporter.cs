using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.IO;
using NLog;
using Vixen.Cache.Sequence;
using Vixen.Common.ffmpeg;
using Vixen.Sys;
using VixenModules.Preview.VixenPreview;
using VixenModules.Preview.VixenPreview.GDIPreview;

namespace VixenModules.Editor.TimedSequenceEditor.VideoExport
{
	/// <summary>
	/// Renders a Vixen sequence to an MP4 video by stepping through the sequence
	/// frame-by-frame, capturing the preview bitmap at each interval, and encoding
	/// the result with FFmpeg (optionally with the sequence's audio track).
	/// </summary>
	public class VideoExporter : IDisposable
	{
		private static readonly Logger Logging = LogManager.GetCurrentClassLogger();

		public event EventHandler<VideoExportProgressEventArgs> Progress;
		public event EventHandler<string> StatusChanged;

		private readonly ISequence _sequence;
		private readonly string _outputFile;
		private readonly int _fps;
		private readonly bool _includeAudio;
		private readonly Ffmpeg.VideoEncoder _encoder;

		private string _tempFolder;
		private GDIPreviewForm _previewForm;
		private bool _createdForm;
		private bool _keepTempFolder; // set to true on error for debugging

		public VideoExporter(ISequence sequence, string outputFile, int fps, bool includeAudio,
			Ffmpeg.VideoEncoder encoder = Ffmpeg.VideoEncoder.CpuBalanced)
		{
			_sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
			_outputFile = outputFile ?? throw new ArgumentNullException(nameof(outputFile));
			_fps = fps;
			_includeAudio = includeAudio;
			_encoder = encoder;
		}

		/// <summary>
		/// Runs the export. Can be called on a background thread.
		/// </summary>
		public void Run(CancellationToken cancellationToken)
		{
			try
			{
				RaiseStatus("Preparing preview...");

				// Get a preview form to render frames with
				_previewForm = GetOrCreatePreviewForm();
				if (_previewForm == null)
				{
					throw new InvalidOperationException(
						"No preview is configured. Please set up a preview in Vixen Administration → Setup Previews.");
				}

				// Prepare temp folder for frames
				_tempFolder = Path.Combine(Path.GetTempPath(), "VixenVideoExport_" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(_tempFolder);
				RaiseStatus($"Working folder: {_tempFolder}");

				// Interval in ms for the chosen framerate (must be integer ms - limitation of the timing source)
				int intervalMs = (int)Math.Round(1000.0 / _fps);
				if (intervalMs < 1) intervalMs = 1;

				// Because intervalMs is rounded, the ACTUAL framerate may differ slightly from requested.
				// Tell ffmpeg the real rate so audio/video stay in sync.
				// e.g. 60fps requested → 17ms interval → actual rate = 1000/17 = 58.82fps
				double actualFps = 1000.0 / intervalMs;

				// Total frames to render
				long totalFrames = (long)(_sequence.Length.TotalMilliseconds / intervalMs);
				if (totalFrames < 1) totalFrames = 1;

				RaiseStatus($"Rendering {totalFrames} frames at {actualFps:F3}fps (requested {_fps}fps)...");
				RenderFrames(intervalMs, totalFrames, cancellationToken);

				if (cancellationToken.IsCancellationRequested) return;

				// Encode frames to MP4
				RaiseStatus("Encoding video...");
				string audioFile = _includeAudio ? GetSequenceAudioPath() : null;
				string framesPattern = Path.Combine(_tempFolder, "frame_%06d.png");

				// Use rational framerate "1000/intervalMs" for exact A/V sync
				int exitCode = Ffmpeg.EncodeFramesToMp4Rational(framesPattern, audioFile, _outputFile, intervalMs,
					out string ffmpegOutput, cancellationToken, _encoder);

				if (cancellationToken.IsCancellationRequested) return;

				if (exitCode != 0)
				{
					// Keep temp folder on error for debugging
					_keepTempFolder = true;

					// Log full output
					Logging.Error("FFmpeg failed (exit code {0}):\n{1}", exitCode, ffmpegOutput);

					// If the user chose a GPU encoder, suggest trying another encoder
					string hint = "";
					if (_encoder == Ffmpeg.VideoEncoder.GpuNvidia ||
					    _encoder == Ffmpeg.VideoEncoder.GpuIntel ||
					    _encoder == Ffmpeg.VideoEncoder.GpuAmd)
					{
						hint = "The selected GPU encoder failed. This can happen if the GPU driver " +
						       "doesn't fully support ffmpeg. Try switching the Encoder dropdown " +
						       "to a different option (e.g. 'CPU: Balanced') and try again.\n\n";
					}

					// Include the last portion of the ffmpeg output so the user sees the real error
					string tail = ffmpegOutput ?? "(no output captured)";
					if (tail.Length > 600) tail = "..." + tail.Substring(tail.Length - 600);
					throw new Exception(
						$"{hint}FFmpeg failed (exit code {exitCode}).\n\nFrames kept for inspection at:\n{_tempFolder}\n\nFFmpeg output:\n{tail}");
				}

				RaiseStatus("Export complete!");
			}
			finally
			{
				Cleanup();
			}
		}

		private void RenderFrames(int intervalMs, long totalFrames, CancellationToken cancellationToken)
		{
			var generator = new SequenceIntervalGenerator(intervalMs, _sequence);

			// Parallel PNG save: capture thread produces bitmaps, worker threads save them.
			// BoundedCapacity prevents memory runaway if saves can't keep up with captures.
			var saveQueue = new BlockingCollection<(Bitmap bmp, string path)>(boundedCapacity: 16);

			// Start save workers (2 is usually enough - PNG compression is CPU-bound)
			int workerCount = Math.Max(2, Environment.ProcessorCount / 2);
			var saveWorkers = new List<Task>();
			for (int i = 0; i < workerCount; i++)
			{
				saveWorkers.Add(Task.Run(() => SaveWorker(saveQueue, cancellationToken)));
			}

			try
			{
				generator.BeginGeneration();

				long frameIndex = 0;
				while (frameIndex < totalFrames)
				{
					cancellationToken.ThrowIfCancellationRequested();

					// Update the preview with current element states, then capture
					var bmp = CaptureBitmap();
					if (bmp != null)
					{
						string path = Path.Combine(_tempFolder, $"frame_{frameIndex:D6}.png");
						saveQueue.Add((bmp, path), cancellationToken);
					}

					// Advance to next interval
					if (!generator.HasNextInterval()) break;
					generator.NextInterval();

					frameIndex++;

					// Progress update every ~30 frames to avoid UI thrashing
					if (frameIndex % 30 == 0 || frameIndex == totalFrames)
					{
						RaiseProgress(frameIndex, totalFrames);
					}
				}
				RaiseProgress(frameIndex, totalFrames);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			finally
			{
				try { generator.EndGeneration(); } catch { }

				// Signal workers no more items and wait for them to finish writes
				saveQueue.CompleteAdding();
				try { Task.WaitAll(saveWorkers.ToArray(), TimeSpan.FromSeconds(30)); } catch { }
				saveQueue.Dispose();
			}
		}

		private Bitmap CaptureBitmap()
		{
			// Must marshal to UI thread -- the preview form is a WinForms form
			if (_previewForm.InvokeRequired)
			{
				_previewForm.Invoke(new Action(() => _previewForm.UpdatePreview()));
			}
			else
			{
				_previewForm.UpdatePreview();
			}

			Bitmap bmp = null;
			if (_previewForm.InvokeRequired)
			{
				_previewForm.Invoke(new Action(() => { bmp = _previewForm.CaptureBitmap(); }));
			}
			else
			{
				bmp = _previewForm.CaptureBitmap();
			}

			return bmp;
		}

		private static void SaveWorker(BlockingCollection<(Bitmap bmp, string path)> queue,
			CancellationToken cancellationToken)
		{
			try
			{
				foreach (var item in queue.GetConsumingEnumerable(cancellationToken))
				{
					try
					{
						item.bmp.Save(item.path, ImageFormat.Png);
					}
					catch (Exception ex)
					{
						Logging.Warn(ex, "Failed to save frame: {0}", item.path);
					}
					finally
					{
						item.bmp.Dispose();
					}
				}
			}
			catch (OperationCanceledException)
			{
				// Drain and dispose remaining bitmaps
				while (queue.TryTake(out var item))
				{
					try { item.bmp.Dispose(); } catch { }
				}
			}
		}

		/// <summary>
		/// Looks for an existing GDIPreviewForm among open forms, or creates a hidden one
		/// using the first configured preview's data.
		/// </summary>
		private GDIPreviewForm GetOrCreatePreviewForm()
		{
			// First, try to find an already-open GDIPreviewForm
			foreach (System.Windows.Forms.Form form in System.Windows.Forms.Application.OpenForms)
			{
				if (form is GDIPreviewForm existing)
				{
					_createdForm = false;
					return existing;
				}
			}

			// Otherwise create a hidden preview form from the first configured preview's data
			foreach (var outputPreview in VixenSystem.Previews)
			{
				if (outputPreview?.PreviewModule == null) continue;

				// PreviewModule is IPreview; cast to IModuleInstance to get ModuleData
				var instance = outputPreview.PreviewModule as Vixen.Module.IModuleInstance;
				if (instance == null) continue;

				var data = instance.ModuleData as VixenPreviewData;
				if (data == null) continue;

				var form = new GDIPreviewForm(data, outputPreview.Id);
				form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
				form.WindowState = System.Windows.Forms.FormWindowState.Normal;
				form.Opacity = 0; // hide it
				form.ShowInTaskbar = false;
				form.Show(); // must be shown for rendering to work
				form.Setup();

				_createdForm = true;
				return form;
			}

			return null;
		}

		private string GetSequenceAudioPath()
		{
			try
			{
				var media = _sequence.SequenceData.Media;
				if (media == null) return null;

				foreach (var m in media)
				{
					if (m == null) continue;
					string typeName = m.GetType().ToString();
					if (typeName.Contains("Audio") && !string.IsNullOrEmpty(m.MediaFilePath) && File.Exists(m.MediaFilePath))
					{
						return m.MediaFilePath;
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Warn(ex, "Could not extract audio from sequence");
			}
			return null;
		}

		private void RaiseProgress(long currentFrame, long totalFrames)
		{
			Progress?.Invoke(this, new VideoExportProgressEventArgs(currentFrame, totalFrames));
		}

		private void RaiseStatus(string message)
		{
			Logging.Info(message);
			StatusChanged?.Invoke(this, message);
		}

		private void Cleanup()
		{
			// Close the hidden preview form if we created it
			if (_createdForm && _previewForm != null)
			{
				try
				{
					if (_previewForm.InvokeRequired)
						_previewForm.Invoke(new Action(() => _previewForm.Close()));
					else
						_previewForm.Close();
				}
				catch { }
				_previewForm = null;
			}

			// Remove temp frames folder (unless we're keeping it for debugging)
			if (!_keepTempFolder && !string.IsNullOrEmpty(_tempFolder) && Directory.Exists(_tempFolder))
			{
				try
				{
					Directory.Delete(_tempFolder, true);
				}
				catch (Exception ex)
				{
					Logging.Warn(ex, "Could not clean up temp folder: {0}", _tempFolder);
				}
			}
		}

		public void Dispose()
		{
			Cleanup();
		}
	}

	public class VideoExportProgressEventArgs : EventArgs
	{
		public long CurrentFrame { get; }
		public long TotalFrames { get; }
		public double PercentComplete => TotalFrames > 0 ? (100.0 * CurrentFrame / TotalFrames) : 0;

		public VideoExportProgressEventArgs(long currentFrame, long totalFrames)
		{
			CurrentFrame = currentFrame;
			TotalFrames = totalFrames;
		}
	}
}
