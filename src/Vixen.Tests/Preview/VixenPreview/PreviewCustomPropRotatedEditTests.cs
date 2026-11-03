using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Vixen.Services;
using Vixen.Sys;
using VixenModules.Preview.VixenPreview;
using VixenModules.Preview.VixenPreview.Shapes;
using Xunit;

namespace Vixen.Tests.Preview.VixenPreview;

[Collection(VixenSystemConfigTestCollection.Name)]
public sealed class PreviewCustomPropRotatedEditTests : IDisposable
{
	private readonly string _dataRootPath;

	public PreviewCustomPropRotatedEditTests()
	{
		_dataRootPath = Path.Combine(Path.GetTempPath(), $"VixenPreviewRotatedEditTests-{Guid.NewGuid():N}");
		Paths.DataRootPath = _dataRootPath;
		typeof(VixenSystem)
			.GetProperty(nameof(VixenSystem.Instrumentation), BindingFlags.Public | BindingFlags.Static)!
			.SetValue(null, new global::Vixen.Sys.Instrumentation.Instrumentation());
		VixenSystem.LoadSystemConfig();
	}

	[Theory]
	[InlineData(0)]
	[InlineData(92)]
	[InlineData(270)]
	public void DraggingBottomRightHandle_HoldsTopLeftCornerAndFollowsMouse(int rotationAngle)
	{
		var prop = CreateProp(rotationAngle);
		prop.Select(true);
		var topLeft = CornerOnScreen(prop, b => b.TopLeft);
		var bottomRight = CornerOnScreen(prop, b => b.BottomRight);
		prop.SetSelectPoint(new PreviewPoint((int)Math.Round(bottomRight.X), (int)Math.Round(bottomRight.Y)));

		// Pull the handle 30 further along the prop's width and 20 along its height.
		var target = bottomRight + (Vector)new RotateTransform(rotationAngle).Transform(new Point(30, 20));
		prop.MouseMove((int)Math.Round(target.X), (int)Math.Round(target.Y), 0, 0);

		AssertNear(topLeft, CornerOnScreen(prop, b => b.TopLeft), 0.01);
		AssertNear(target, CornerOnScreen(prop, b => b.BottomRight), 3);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(270)]
	public void Match_HoldsTopLeftCornerOfRotatedProp(int rotationAngle)
	{
		var prop = CreateProp(rotationAngle);
		var larger = CreateProp(rotationAngle, width: 150, height: 60);
		var topLeft = CornerOnScreen(prop, b => b.TopLeft);

		prop.Match(larger);

		AssertNear(topLeft, CornerOnScreen(prop, b => b.TopLeft), 0.01);
		// Bounds include the light size, which does not scale, so the widths match to within a light.
		Assert.InRange(prop.Bounds.Width, larger.Bounds.Width - 2, larger.Bounds.Width + 2);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(270)]
	public void LinkElementsOk_RelinksSavedPixelsOfCustomProp(int rotationAngle)
	{
		var original = ElementNodeService.Instance.CreateSingle(null, "Original");
		var copy = ElementNodeService.Instance.CreateSingle(null, "Copy");
		var prop = CreateProp(rotationAngle);
		prop.PropPixels.ForEach(p => p.Node = original);
		prop.Layout();

		using var dialog = new PreviewSetElements(prop.Strings);
		Invoke(dialog, "PopulateStringList");
		foreach (var pixel in DialogPixels(dialog))
		{
			pixel.Node = copy;
		}
		Invoke(dialog, "buttonOK_Click", null, EventArgs.Empty);

		Assert.All(prop.PropPixels, p => Assert.Equal(copy.Id, p.NodeId));
		Assert.All(prop.Pixels, p => Assert.Equal(copy.Id, p.NodeId));
	}

	public void Dispose()
	{
		if (Directory.Exists(_dataRootPath))
		{
			Directory.Delete(_dataRootPath, true);
		}
	}

	private static PreviewCustomProp CreateProp(int rotationAngle, int width = 100, int height = 40)
	{
		var prop = new PreviewCustomProp(1);
		for (var x = 0; x <= width; x += 10)
		{
			for (var y = 0; y <= height; y += 10)
			{
				prop.PropPixels.Add(new PreviewPixel(new Point(200 + x, 300 + y), 2));
			}
		}

		prop.RotationAngle = rotationAngle;
		prop.Layout();
		return prop;
	}

	private static Point CornerOnScreen(PreviewCustomProp prop, Func<Rect, Point> corner)
	{
		var bounds = prop.Bounds;
		var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
		return new RotateTransform(prop.RotationAngle, center.X, center.Y).Transform(corner(bounds));
	}

	private static void AssertNear(Point expected, Point actual, double tolerance)
	{
		Assert.True((expected - actual).Length <= tolerance, $"Expected {expected} but was {actual}");
	}

	private static IEnumerable<PreviewPixel> DialogPixels(PreviewSetElements dialog)
	{
		var strings = (IEnumerable)typeof(PreviewSetElements)
			.GetField("_strings", BindingFlags.NonPublic | BindingFlags.Instance)!
			.GetValue(dialog)!;
		return strings
			.Cast<object>()
			.SelectMany(s => (List<PreviewPixel>)s.GetType().GetProperty("Pixels")!.GetValue(s)!);
	}

	private static void Invoke(PreviewSetElements dialog, string methodName, params object?[] args)
	{
		typeof(PreviewSetElements)
			.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
			.Invoke(dialog, args);
	}
}
