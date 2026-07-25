using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Vixen.Marks;
using Vixen.Module.Effect;
using VixenModules.App.ColorGradients;

namespace VixenModules.Editor.EffectEditor.Editors
{
	public class GradientTypeEditor : BaseColorTypeEditor
	{
		public GradientTypeEditor():base(typeof(ColorGradient), EditorKeys.GradientEditorKey)
		{
		}

		public override Object ShowDialog(PropertyItem propertyItem, Object propertyValue, IInputElement commandSource)
		{
			ColorGradient gradient;
			var value = propertyValue as ColorGradient;
			HashSet<Color> discreteColors = GetDiscreteColors(propertyItem.Component);
			if (value != null)
			{
				gradient = value;
			}
			else
			{
				gradient = discreteColors.Any() ? new ColorGradient(discreteColors.First()) : new ColorGradient(Color.White);
			}

			ColorGradientEditor editor = new ColorGradientEditor(gradient, discreteColors.Any(), discreteColors);
			editor.Text = propertyItem.DisplayName;

			// Extract mark positions from effect context
			try
			{
				if (propertyItem.Component is IEffectModuleInstance effect)
				{
					var effectStart = effect.StartTime;
					var effectDuration = effect.TimeSpan;

					if (effectDuration > TimeSpan.Zero)
					{
						editor.MarkPositions = ExtractMarkPositions(effect, effectStart, effectDuration);
					}
				}
			}
			catch
			{
				// Gracefully degrade
			}

			if (editor.ShowDialog() == DialogResult.OK)
			{
				propertyValue = editor.Gradient;
			}

			return propertyValue;
		}

		private List<(double Position, Color Color)> ExtractMarkPositions(
			IEffectModuleInstance effect, TimeSpan effectStart, TimeSpan effectDuration)
		{
			var result = new List<(double, Color)>();

			try
			{
				var effectEnd = effectStart + effectDuration;

				var markCollections = effect.MarkCollections;
				if (markCollections == null || markCollections.Count == 0)
				{
					markCollections = CurveEditor.ActiveMarkCollections;
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