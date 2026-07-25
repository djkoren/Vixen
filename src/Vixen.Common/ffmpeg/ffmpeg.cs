using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Vixen.Common.ffmpeg
{
	public partial class Ffmpeg
	{
		private static readonly string FfmpegPath;

		//ffmpeg output line: "  Duration: 00:02:29.46, start: 0.00...."
		[GeneratedRegex(@"Duration: (\d+):(\d{2}):(\d{2})\.(\d{2})")]
		private static partial Regex parseDuration();

		// look for ", ####x####" where numbers can be 2 to 5 digits long
		[GeneratedRegex(@", (?<width>\d{2,5})x(?<height>\d{2,5})")]
		private static partial Regex parseResolution();

		static Ffmpeg()
		{
			FfmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"ffmpeg.exe");
		}

		public static void MakeScaledThumbNails(string movieFile, string outputPath, double startPosition, double duration, int width, int height, bool maintainAspect, int rotateVideo, string cropVideo, double fps = 20, string cacheFileType = "bmp")
		{
			string args = $" -y -ss {startPosition} -i \"{movieFile}\" -an -t {duration.ToString(CultureInfo.InvariantCulture)} -vf \"scale={width}:{(maintainAspect ? -1 : height)}{cropVideo}, rotate={rotateVideo}*(PI/180)\" -r {fps} \"{outputPath}\\%5d.{cacheFileType}\"";

			ProcessStartInfo psi = new(FfmpegPath, args)
			{
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using (Process process = new())
			{
				process.StartInfo = psi;
				process.Start();
				process.WaitForExit();
			};
		}

		public static void GetVideoDurationAndResolution(string videoFile, out TimeSpan duration, out int width, out int height)
		{
			duration = TimeSpan.Zero;
			width = 0;
			height = 0;
			
			string args = $"-hide_banner -an -sn -dn -i \"{videoFile}\"";

			ProcessStartInfo psi = new(FfmpegPath, args)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true
			};
			using (Process process = new())
			{
				process.StartInfo = psi;
				process.Start();

				// Keep all the ffmpeg output parsing logic in this class
				string line;
				Match match;
				while ((line = process.StandardError.ReadLine()) != null)
				{
					match = parseDuration().Match(line);
					if (match.Success)
					{
						duration = new TimeSpan(0,
							Int32.Parse(match.Groups[1].Value),
							Int32.Parse(match.Groups[2].Value),
							Int32.Parse(match.Groups[3].Value),
							Int32.Parse(match.Groups[4].Value) * 10);
					}
					// Find the " Video: " line, then look for the resolution
					else if (line.Contains(" Video: "))
					{
						match = parseResolution().Match(line);
						if (match.Success)
						{
							width = Int32.Parse(match.Groups["width"].Value);
							height = Int32.Parse(match.Groups["height"].Value);
							return; // Since Duration is always before the Video line, once width and height are found, we're done
						}
					}
				}
				// If we get here, something has failed to be found or parse
				process.WaitForExit();
				if (duration == TimeSpan.Zero)
				{
					throw new Exception($"Unable to parse Duration from ffmpeg output. Error code {process.ExitCode}");
				}
				else
				{
					throw new Exception($"Unable to parse Resolution from ffmpeg output. Error code {process.ExitCode}");
				}
			};
		}

		[Obsolete("Instead use GetVideoDurationAndResolution to return duration")]
		//Get Video Info for native Video effect.
		public static string GetVideoInfo(string movieFile, string outputPath)
		{
			//Gets Video length and will continue if users start position is less then the video length.
			string args = " -i \"" + movieFile + "\"";

			ProcessStartInfo procStartInfo = new ProcessStartInfo(FfmpegPath, args);
			procStartInfo.RedirectStandardError = true;
			procStartInfo.UseShellExecute = false;
			procStartInfo.CreateNoWindow = true;
			Process proc = new Process();
			proc.StartInfo = procStartInfo;
			proc.Start();
			string result = proc.StandardError.ReadToEnd();
			return result;
		}

		[Obsolete("Instead use GetVideoDurationAndResolution to return resolution")]
		//Get Native Video Size Effect
		public static void GetVideoSize(string movieFile, string outputPath)
		{
			//make arguments string
			string args = $" -i \"{movieFile}\"  -vframes 1 \"{outputPath}\"";

			//Console.Out.WriteLine(args);
			ProcessStartInfo psi = new ProcessStartInfo(FfmpegPath, args);
			psi.UseShellExecute = false;
			psi.CreateNoWindow = true;
			Process process = new Process();
			process.StartInfo = psi;
			process.Start();
			process.WaitForExit();
		}

		/// <summary>
		/// Encodes a sequence of image frames into an MP4 video, optionally with an audio track.
		/// </summary>
		/// <param name="framesPattern">Full path pattern like "C:\tmp\frames\frame_%06d.png"</param>
		/// <param name="audioFile">Path to audio file to mux, or null/empty for silent video</param>
		/// <param name="outputFile">Output .mp4 file path</param>
		/// <param name="fps">Video framerate (frames per second)</param>
		/// <param name="cancellationToken">Optional cancellation token to abort encoding</param>
		/// <returns>The exit code from ffmpeg (0 = success)</returns>
		public static int EncodeFramesToMp4(string framesPattern, string audioFile, string outputFile, int fps,
			System.Threading.CancellationToken cancellationToken = default)
		{
			return EncodeFramesToMp4(framesPattern, audioFile, outputFile, fps, out _, cancellationToken);
		}

		public static int EncodeFramesToMp4(string framesPattern, string audioFile, string outputFile, int fps,
			out string errorOutput,
			System.Threading.CancellationToken cancellationToken = default)
		{
			return EncodeFramesToMp4Internal(framesPattern, audioFile, outputFile,
				framerateArg: fps.ToString(), out errorOutput, cancellationToken);
		}

		/// <summary>
		/// Encode frames using a rational framerate expressed as 1000ms per frame / intervalMs.
		/// This keeps A/V sync exact when intervalMs doesn't divide 1000 evenly.
		/// </summary>
		public static int EncodeFramesToMp4Rational(string framesPattern, string audioFile, string outputFile,
			int intervalMs, out string errorOutput,
			System.Threading.CancellationToken cancellationToken = default,
			VideoEncoder encoder = VideoEncoder.CpuBalanced)
		{
			// FFmpeg accepts rational "num/den" for -framerate
			string framerate = $"1000/{intervalMs}";
			return EncodeFramesToMp4Internal(framesPattern, audioFile, outputFile,
				framerate, out errorOutput, cancellationToken, encoder);
		}

		public enum VideoEncoder
		{
			CpuFast,        // libx264 with ultrafast preset (fastest CPU)
			CpuBalanced,    // libx264 with fast preset (default, good balance)
			CpuHighQuality, // libx264 with medium preset (best quality, slowest CPU)
			GpuNvidia,      // h264_nvenc (NVIDIA GPU)
			GpuIntel,       // h264_qsv (Intel QuickSync)
			GpuAmd          // h264_amf (AMD GPU)
		}

		/// <summary>
		/// Auto-detects which GPU encoders are available on this system.
		/// Returns a list of VideoEncoder values that can be used.
		/// </summary>
		public static List<VideoEncoder> DetectAvailableEncoders()
		{
			var available = new List<VideoEncoder>
			{
				VideoEncoder.CpuFast, VideoEncoder.CpuBalanced, VideoEncoder.CpuHighQuality
			};

			if (!File.Exists(FfmpegPath)) return available;

			try
			{
				var psi = new ProcessStartInfo(FfmpegPath, "-hide_banner -encoders")
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};
				using var process = Process.Start(psi);
				if (process == null) return available;

				string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
				process.WaitForExit(5000);

				if (output.Contains("h264_nvenc")) available.Add(VideoEncoder.GpuNvidia);
				if (output.Contains("h264_qsv")) available.Add(VideoEncoder.GpuIntel);
				if (output.Contains("h264_amf")) available.Add(VideoEncoder.GpuAmd);
			}
			catch { }

			return available;
		}

		private static int EncodeFramesToMp4Internal(string framesPattern, string audioFile, string outputFile,
			string framerateArg, out string errorOutput,
			System.Threading.CancellationToken cancellationToken,
			VideoEncoder encoder = VideoEncoder.CpuBalanced)
		{
			errorOutput = null;

			if (!File.Exists(FfmpegPath))
			{
				errorOutput = $"ffmpeg.exe not found at expected path: {FfmpegPath}";
				return -1;
			}

			bool hasAudio = !string.IsNullOrEmpty(audioFile) && File.Exists(audioFile);

			// libx264 with yuv420p requires even dimensions; the scale filter pads odd sizes
			const string evenScaleFilter = "-vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\"";

			// Map encoder to ffmpeg args
			string videoEncoderArgs = encoder switch
			{
				VideoEncoder.CpuFast => "-c:v libx264 -pix_fmt yuv420p -preset ultrafast -crf 23",
				VideoEncoder.CpuBalanced => "-c:v libx264 -pix_fmt yuv420p -preset fast -crf 20",
				VideoEncoder.CpuHighQuality => "-c:v libx264 -pix_fmt yuv420p -preset medium -crf 18",
				VideoEncoder.GpuNvidia => "-c:v h264_nvenc -pix_fmt yuv420p -preset p4 -rc vbr -cq 20",
				VideoEncoder.GpuIntel => "-c:v h264_qsv -pix_fmt nv12 -preset fast -global_quality 20",
				VideoEncoder.GpuAmd => "-c:v h264_amf -pix_fmt yuv420p -quality balanced -rc cqp -qp_i 20 -qp_p 22",
				_ => "-c:v libx264 -pix_fmt yuv420p -preset fast -crf 20"
			};

			string args;
			if (hasAudio)
			{
				// -shortest ends the video when the shorter of video/audio ends
				args = $"-y -framerate {framerateArg} -i \"{framesPattern}\" -i \"{audioFile}\" " +
				       $"{evenScaleFilter} {videoEncoderArgs} " +
				       $"-c:a aac -b:a 192k -shortest \"{outputFile}\"";
			}
			else
			{
				args = $"-y -framerate {framerateArg} -i \"{framesPattern}\" " +
				       $"{evenScaleFilter} {videoEncoderArgs} \"{outputFile}\"";
			}

			ProcessStartInfo psi = new(FfmpegPath, args)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
				RedirectStandardOutput = true
			};

			var errorBuffer = new System.Text.StringBuilder();
			errorBuffer.AppendLine("FFmpeg command:");
			errorBuffer.AppendLine($"  \"{FfmpegPath}\" {args}");
			errorBuffer.AppendLine();
			errorBuffer.AppendLine("FFmpeg output:");

			using (Process process = new())
			{
				process.StartInfo = psi;
				process.ErrorDataReceived += (s, e) =>
				{
					if (e.Data != null) errorBuffer.AppendLine(e.Data);
				};
				process.OutputDataReceived += (s, e) =>
				{
					if (e.Data != null) errorBuffer.AppendLine(e.Data);
				};

				try
				{
					process.Start();
				}
				catch (Exception ex)
				{
					errorOutput = "Failed to start ffmpeg: " + ex.Message;
					return -1;
				}

				process.BeginErrorReadLine();
				process.BeginOutputReadLine();

				// Poll for cancellation
				while (!process.HasExited)
				{
					if (cancellationToken.IsCancellationRequested)
					{
						try { process.Kill(); } catch { }
						break;
					}
					System.Threading.Thread.Sleep(100);
				}

				process.WaitForExit();

				errorOutput = errorBuffer.ToString();
				return process.ExitCode;
			}
		}
	}
}