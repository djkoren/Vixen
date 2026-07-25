using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Vixen.Marks;
using Vixen.Module.Effect;
using VixenModules.App.Curves;
using VixenModules.Media.Audio;

namespace VixenModules.Editor.EffectEditor.Editors
{
	public class CurveEditor : TypeEditor
	{
		/// <summary>
		/// Static reference to the sequence audio, set externally by the sequence editor.
		/// Used to display waveform in the curve editor dialog.
		/// </summary>
		public static Audio ActiveSequenceAudio { get; set; }

		/// <summary>
		/// Static reference to the sequence's mark collections, set externally by the sequence editor.
		/// Used to display marks in the curve editor dialog.
		/// </summary>
		public static ObservableCollection<IMarkCollection> ActiveMarkCollections { get; set; }

		public CurveEditor():base(typeof(Curve),EditorKeys.CurveEditorKey,null)
		{
		}

		public override Object ShowDialog(PropertyItem propertyItem, Object propertyValue, IInputElement commandSource)
		{
			Curve curveValue = propertyValue as Curve;
			App.Curves.CurveEditor editor = new App.Curves.CurveEditor(curveValue ?? new Curve());
			editor.Text = propertyItem.DisplayName;

			// Extract timing, waveform, and marks context from the effect
			try
			{
				if (propertyItem.Component is IEffectModuleInstance effect)
				{
					var effectStart = effect.StartTime;
					var effectDuration = effect.TimeSpan;

					if (effectDuration > TimeSpan.Zero)
					{
						editor.WaveformPeaks = ExtractWaveformPeaks(effectStart, effectDuration);
						editor.MarkPositions = ExtractMarkPositions(effect, effectStart, effectDuration);
					}
				}
			}
			catch
			{
				// Gracefully degrade -- curve editor works fine without waveform/marks
			}

			if (editor.ShowDialog() == DialogResult.OK)
			{
				propertyValue = editor.Curve;
			}

			return propertyValue;
		}

		/// <summary>
		/// Extracts waveform peak data for the effect's time range from the sequence audio.
		/// </summary>
		private float[] ExtractWaveformPeaks(TimeSpan effectStart, TimeSpan effectDuration)
		{
			try
			{
				// Get audio from the static reference
				var audio = ActiveSequenceAudio;
				if (audio == null || !audio.MediaLoaded) return null;

				var audioDuration = audio.MediaDuration;
				if (audioDuration <= TimeSpan.Zero) return null;

				// Request enough total samples so the effect's slice has ~800 samples
				const int targetEffectSamples = 800;
				double effectFraction = effectDuration.TotalMilliseconds / audioDuration.TotalMilliseconds;
				if (effectFraction <= 0) return null;

				int totalSamplesNeeded = (int)(targetEffectSamples / effectFraction);
				totalSamplesNeeded = Math.Max(100, Math.Min(totalSamplesNeeded, 20000));

				var allSamples = audio.GetSamples(totalSamplesNeeded, CancellationToken.None);
				if (allSamples == null || allSamples.Count == 0) return null;

				// Slice to the effect's time range
				int startIdx = (int)(effectStart.TotalMilliseconds / audioDuration.TotalMilliseconds * allSamples.Count);
				int endIdx = (int)((effectStart + effectDuration).TotalMilliseconds / audioDuration.TotalMilliseconds * allSamples.Count);

				startIdx = Math.Max(0, Math.Min(startIdx, allSamples.Count - 1));
				endIdx = Math.Max(startIdx + 1, Math.Min(endIdx, allSamples.Count));

				var peaks = new float[endIdx - startIdx];
				for (int i = 0; i < peaks.Length; i++)
				{
					peaks[i] = Math.Max(Math.Abs(allSamples[startIdx + i].High), Math.Abs(allSamples[startIdx + i].Low));
				}

				return peaks;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// Extracts mark positions within the effect's time range, converted to 0-100 scale.
		/// </summary>
		private List<(double Position, Color Color)> ExtractMarkPositions(
			IEffectModuleInstance effect, TimeSpan effectStart, TimeSpan effectDuration)
		{
			var result = new List<(double, Color)>();

			try
			{
				var effectEnd = effectStart + effectDuration;

				// Try effect's own mark collections first, fall back to sequence-level marks
				var markCollections = effect.MarkCollections;
				if (markCollections == null || markCollections.Count == 0)
				{
					markCollections = ActiveMarkCollections;
				}

				if (markCollections == null) return result;

				foreach (var collection in markCollections)
				{
					if (!collection.IsVisible) continue;

					var color = collection.Decorator?.Color ?? Color.Yellow;
					var marks = collection.MarksInclusiveOfTime(effectStart, effectEnd);

					foreach (var mark in marks)
					{
						double pos = (mark.StartTime - effectStart).TotalMilliseconds
						             / effectDuration.TotalMilliseconds * 100.0;
						if (pos >= 0 && pos <= 100)
						{
							result.Add((pos, color));
						}
					}
				}
			}
			catch
			{
				// Gracefully degrade
			}

			return result;
		}
	}
}