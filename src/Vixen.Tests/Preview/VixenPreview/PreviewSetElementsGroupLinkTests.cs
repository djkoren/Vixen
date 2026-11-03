using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using Vixen.Services;
using Vixen.Sys;
using VixenModules.Preview.VixenPreview;
using VixenModules.Preview.VixenPreview.Shapes;
using Xunit;

namespace Vixen.Tests.Preview.VixenPreview;

[Collection(VixenSystemConfigTestCollection.Name)]
public sealed class PreviewSetElementsGroupLinkTests : IDisposable
{
	private readonly string _dataRootPath;

	public PreviewSetElementsGroupLinkTests()
	{
		_dataRootPath = Path.Combine(Path.GetTempPath(), $"VixenPreviewGroupLinkTests-{Guid.NewGuid():N}");
		Paths.DataRootPath = _dataRootPath;
		typeof(VixenSystem)
			.GetProperty(nameof(VixenSystem.Instrumentation), BindingFlags.Public | BindingFlags.Static)!
			.SetValue(null, new global::Vixen.Sys.Instrumentation.Instrumentation());
		VixenSystem.LoadSystemConfig();
	}

	[Fact]
	public void DroppingParentGroup_LinksSubGroupElementsInTreeOrderOnce()
	{
		// Same shape as a prop's group: Ring holds Ring 1 and Ring 2, which the parent also lists on their own.
		var parent = Group(null, "Prop");
		var ring = Group(parent, "Ring");
		var ring1 = Group(ring, "Ring 1");
		var ring2 = Group(ring, "Ring 2");
		var ring1Lights = Lights(ring1, "R1", 3);
		var ring2Lights = Lights(ring2, "R2", 2);
		var ring1Again = Group(parent, "Ring 1 again");
		ring1Lights.ForEach(light => VixenSystem.Nodes.AddChildToParent(light, ring1Again));
		var fill = Group(parent, "Fill");
		var fillLights = Lights(fill, "F", 4);
		var expected = ring1Lights.Concat(ring2Lights).Concat(fillLights).ToList();
		var prop = CreateProp(expected.Count);
		using var dialog = OpenDialog(prop);

		var rowsLinked = LinkFromRow(dialog, 0, parent);

		Assert.Equal(expected.Count, rowsLinked);
		Assert.Equal(expected.Select(n => n.Id), DialogPixels(dialog).Select(p => p.NodeId));
		Invoke(dialog, "buttonOK_Click", null, EventArgs.Empty);
		Assert.Equal(expected.Select(n => n.Id), prop.PropPixels.Select(p => p.NodeId));
	}

	[Fact]
	public void DroppingGroupOnLaterRow_FillsFromThatRowAndStopsAtLastRow()
	{
		var parent = Group(null, "Prop");
		var lights = Lights(Group(parent, "Ring"), "R", 5);
		var prop = CreateProp(4);
		using var dialog = OpenDialog(prop);

		var rowsLinked = LinkFromRow(dialog, 1, parent);

		Assert.Equal(3, rowsLinked);
		Assert.Equal(
			new[] { Guid.Empty, lights[0].Id, lights[1].Id, lights[2].Id },
			DialogPixels(dialog).Select(p => p.NodeId));
	}

	[Fact]
	public void DroppingSingleElement_LinksOneRow()
	{
		var light = ElementNodeService.Instance.CreateSingle(null, "Light");
		var prop = CreateProp(3);
		using var dialog = OpenDialog(prop);

		var rowsLinked = LinkFromRow(dialog, 1, light);

		Assert.Equal(1, rowsLinked);
		Assert.Equal(new[] { Guid.Empty, light.Id, Guid.Empty }, DialogPixels(dialog).Select(p => p.NodeId));
	}

	public void Dispose()
	{
		if (Directory.Exists(_dataRootPath))
		{
			Directory.Delete(_dataRootPath, true);
		}
	}

	private static ElementNode Group(ElementNode? parent, string name)
	{
		return ElementNodeService.Instance.CreateSingle(parent, name, createElement: false);
	}

	private static List<ElementNode> Lights(ElementNode group, string prefix, int count)
	{
		return Enumerable.Range(1, count)
			.Select(i => ElementNodeService.Instance.CreateSingle(group, $"{prefix} {i}"))
			.ToList();
	}

	private static PreviewCustomProp CreateProp(int pixelCount)
	{
		var prop = new PreviewCustomProp(1);
		for (var i = 0; i < pixelCount; i++)
		{
			prop.PropPixels.Add(new PreviewPixel(new Point(200 + i * 10, 300), 2));
		}

		prop.Layout();
		return prop;
	}

	private static PreviewSetElements OpenDialog(PreviewCustomProp prop)
	{
		var dialog = new PreviewSetElements(prop.Strings);
		Invoke(dialog, "PopulateStringList");
		Invoke(dialog, "UpdateListLinkedElements");
		return dialog;
	}

	private static int LinkFromRow(PreviewSetElements dialog, int row, ElementNode node)
	{
		var list = (ListView)typeof(PreviewSetElements)
			.GetField("listLinkedElements", BindingFlags.NonPublic | BindingFlags.Instance)!
			.GetValue(dialog)!;
		return (int)typeof(PreviewSetElements)
			.GetMethod("SetLinkedElementItems", BindingFlags.NonPublic | BindingFlags.Instance)!
			.Invoke(dialog, [list.Items[row], node])!;
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
