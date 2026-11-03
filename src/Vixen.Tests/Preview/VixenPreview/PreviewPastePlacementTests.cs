using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using Vixen.Services;
using Vixen.Sys;
using VixenModules.Preview.VixenPreview;
using VixenModules.Preview.VixenPreview.Shapes;
using Xunit;
using CanvasPoint = System.Drawing.Point;
using LayoutPoint = System.Windows.Point;

namespace Vixen.Tests.Preview.VixenPreview;

[Collection(VixenSystemConfigTestCollection.Name)]
public sealed class PreviewPastePlacementTests : IDisposable
{
	private readonly string _dataRootPath;

	public PreviewPastePlacementTests()
	{
		_dataRootPath = Path.Combine(Path.GetTempPath(), $"VixenPreviewPasteTests-{Guid.NewGuid():N}");
		Paths.DataRootPath = _dataRootPath;
		typeof(VixenSystem)
			.GetProperty(nameof(VixenSystem.Instrumentation), BindingFlags.Public | BindingFlags.Static)!
			.SetValue(null, new global::Vixen.Sys.Instrumentation.Instrumentation());
		VixenSystem.LoadSystemConfig();
	}

	[Theory]
	[InlineData(1, 0)]
	[InlineData(0.5, 0)]
	[InlineData(0.6, 270)]
	[InlineData(2, 92)]
	public void PastedCustomProp_LandsUnderTheMouse(double zoomLevel, int rotationAngle)
	{
		var prop = CreateProp(zoomLevel, rotationAngle);

		MoveShapesOnCanvas([prop], new CanvasPoint(400, 300), zoomLevel);

		Assert.InRange(prop.CanvasBounds.Left, 399, 401);
		Assert.InRange(prop.CanvasBounds.Top, 299, 301);
	}

	[Fact]
	public void PastedLineAndCustomProp_KeepTheirSpacingWhenZoomedOut()
	{
		const double zoomLevel = 0.5;
		var line = new PreviewLine(new PreviewPoint(100, 100), new PreviewPoint(200, 100), 5,
			ElementNodeService.Instance.CreateSingle(null, "Line"), zoomLevel);
		var prop = CreateProp(zoomLevel, 0);
		var propOffsetX = prop.CanvasBounds.Left - 100;
		var propOffsetY = prop.CanvasBounds.Top - 100;

		MoveShapesOnCanvas([line, prop], new CanvasPoint(500, 450), zoomLevel);

		Assert.Equal(500, line.Left * zoomLevel);
		Assert.Equal(450, line.Top * zoomLevel);
		Assert.InRange(prop.CanvasBounds.Left, 500 + propOffsetX - 1, 500 + propOffsetX + 1);
		Assert.InRange(prop.CanvasBounds.Top, 450 + propOffsetY - 1, 450 + propOffsetY + 1);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(180)]
	[InlineData(90)]
	public void PastedRotatedLine_IsDrawnWhereItWasPasted(int rotationAngle)
	{
		var line = PastedLine(rotationAngle);
		line.ZoomLevel = 1;

		MoveShapesOnCanvas([line], new CanvasPoint(600, 300), 1);

		// The lights are drawn around the line's own center, not swung around the original's.
		var center = line.Center;
		Assert.InRange(line.Pixels.Average(p => p.X), center.X - 2, center.X + 2);
		Assert.InRange(line.Pixels.Average(p => p.Y), center.Y - 2, center.Y + 2);
	}

	[Fact]
	public void PastedLinesAndCustomProp_FollowTheMouseUntilPlaced()
	{
		RunOnStaThread(() =>
		{
			var straight = PastedLine(0);
			var flipped = PastedLine(180);
			var prop = (PreviewCustomProp)CopyAndPaste(new DisplayItem { Shape = CreateProp(1, 270) }).Shape;
			var shapes = new List<PreviewBaseShape> { straight, flipped, prop };
			var lineStarts = new[] { straight, flipped }.Select(l => (l.Left, l.Top)).ToList();
			var propStart = prop.CanvasBounds.TopLeft;

			using var preview = new VixenPreviewControl { EditMode = true };
			preview.SelectedDisplayItems = shapes.Select(s => new DisplayItem { Shape = s }).ToList();
			InvokePreview(preview, "StartMove", 300, 200);
			InvokePreview(preview, "VixenPreviewControl_MouseMove", preview,
				new MouseEventArgs(MouseButtons.None, 0, 350, 230, 0));

			Assert.Equal(lineStarts.Select(s => (s.Left + 50, s.Top + 30)),
				new[] { straight, flipped }.Select(l => (l.Left, l.Top)));
			Assert.InRange(prop.CanvasBounds.Left, propStart.X + 49, propStart.X + 51);
			Assert.InRange(prop.CanvasBounds.Top, propStart.Y + 29, propStart.Y + 31);
		});
	}

	[Fact]
	public void MoveTo_LetsRotatedPropHangItsUnrotatedBoxOffTheEdge()
	{
		// Wide prop on its side: its unrotated box reaches further left than what is drawn.
		var prop = CreateProp(1, 270);
		var overhang = prop.CanvasBounds.Left - prop.Bounds.Left;
		var drop = prop.CanvasBounds.Top - prop.Bounds.Top;

		prop.MoveTo((int)Math.Round(5 - overhang), (int)Math.Round(200 - drop));

		Assert.True(prop.Bounds.Left < 0);
		Assert.InRange(prop.CanvasBounds.Left, 4, 6);
		Assert.InRange(prop.CanvasBounds.Top, 199, 201);
	}

	[Fact]
	public void MoveTo_PutsRotatedPropAtTenTenWhenItWouldLeaveTheCanvas()
	{
		var prop = CreateProp(1, 270);

		prop.MoveTo(-500, 200);

		Assert.InRange(prop.CanvasBounds.Left, 9, 11);
		Assert.InRange(prop.CanvasBounds.Top, 9, 11);
	}

	public void Dispose()
	{
		if (Directory.Exists(_dataRootPath))
		{
			Directory.Delete(_dataRootPath, true);
		}
	}

	private static PreviewCustomProp CreateProp(double zoomLevel, int rotationAngle)
	{
		var prop = new PreviewCustomProp(zoomLevel);
		for (var x = 0; x <= 200; x += 10)
		{
			for (var y = 0; y <= 60; y += 10)
			{
				prop.PropPixels.Add(new PreviewPixel(new LayoutPoint(600 + x, 400 + y), 2));
			}
		}

		prop.RotationAngle = rotationAngle;
		prop.Layout();
		return prop;
	}

	private static PreviewLine PastedLine(int rotationAngle)
	{
		var original = new PreviewLine(new PreviewPoint(278, 798), new PreviewPoint(364, 798), 5,
			ElementNodeService.Instance.CreateSingle(null, "Pane"), 1);
		// Selecting a shape in the editor gives it its rotation axis before it can be rotated.
		original.RotationAxis = new PreviewPoint(original.Center.X, original.Center.Y);
		original.RotationAngle = rotationAngle;
		return (PreviewLine)CopyAndPaste(new DisplayItem { Shape = original }).Shape;
	}

	private static void InvokePreview(VixenPreviewControl preview, string methodName, params object[] args)
	{
		typeof(VixenPreviewControl)
			.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
			.Invoke(preview, args);
	}

	/// <summary>The preview control takes mouse capture, which WinForms only allows on an STA thread.</summary>
	private static void RunOnStaThread(Action test)
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				test();
			}
			catch (Exception e)
			{
				failure = e;
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		if (failure != null)
		{
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
		}
	}

	/// <summary>Round-trips an item through XML the way Copy and Paste do.</summary>
	private static DisplayItem CopyAndPaste(DisplayItem item)
	{
		var serializer = new DataContractSerializer(typeof(List<DisplayItem>));
		using var stream = new MemoryStream();
		serializer.WriteObject(stream, new List<DisplayItem> { item });
		stream.Position = 0;
		return ((List<DisplayItem>)serializer.ReadObject(stream)!).Single();
	}

	private static void MoveShapesOnCanvas(List<PreviewBaseShape> shapes, CanvasPoint target, double zoomLevel)
	{
		typeof(VixenPreviewControl)
			.GetMethod("MoveShapesOnCanvas", BindingFlags.NonPublic | BindingFlags.Static)!
			.Invoke(null, [shapes, target, zoomLevel]);
	}
}
