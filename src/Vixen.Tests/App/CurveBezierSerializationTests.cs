using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using Utilities;
using VixenModules.App.Curves;
using VixenModules.Effect.Wipe;
using Xunit;
using ZedGraph;

namespace Vixen.Tests.App;

/// <summary>
/// Covers Bezier handle data surviving the two serializers a curve passes through: the
/// <see cref="DataContractSerializer"/> used to save sequences, and the System.Text.Json
/// round-trip performed by <see cref="DragDropUtils"/> when a curve is dragged onto an effect.
/// </summary>
/// <remarks>
/// Regression coverage for the crash where dragging a Bezier curve onto an effect property left a
/// <c>JsonElement</c> in <see cref="PointPair.Tag"/>, which then aborted the whole sequence save
/// with a <see cref="SerializationException"/>.
/// </remarks>
public class CurveBezierSerializationTests
{
	private static Curve CreateBezierCurve()
	{
		var curve = new Curve(new PointPairList(new[] { 0.0, 50.0, 100.0 }, new[] { 0.0, 100.0, 0.0 }));

		curve.Points[0].Tag = new BezierHandleData
		{
			OutHandleX = 12.5,
			OutHandleY = 34.0,
			HasOutHandle = true
		};
		curve.Points[1].Tag = new BezierHandleData
		{
			InHandleX = -8.0,
			InHandleY = -3.5,
			OutHandleX = 8.0,
			OutHandleY = 3.5,
			HasInHandle = true,
			HasOutHandle = true
		};

		return curve;
	}

	private static void AssertHandlesIntact(Curve curve)
	{
		var first = Assert.IsType<BezierHandleData>(curve.Points[0].Tag);
		Assert.Equal(12.5, first.OutHandleX);
		Assert.Equal(34.0, first.OutHandleY);
		Assert.True(first.HasOutHandle);
		Assert.False(first.HasInHandle);

		var middle = Assert.IsType<BezierHandleData>(curve.Points[1].Tag);
		Assert.Equal(-8.0, middle.InHandleX);
		Assert.Equal(-3.5, middle.InHandleY);
		Assert.Equal(8.0, middle.OutHandleX);
		Assert.Equal(3.5, middle.OutHandleY);
		Assert.True(middle.HasInHandle);
		Assert.True(middle.HasOutHandle);

		Assert.Null(curve.Points[2].Tag);
	}

	private static string SaveWithDataContract<T>(T graph)
	{
		var builder = new StringBuilder();
		using (var writer = XmlWriter.Create(builder))
		{
			new DataContractSerializer(typeof(T)).WriteObject(writer, graph);
		}

		return builder.ToString();
	}

	private static T RoundTripDragDrop<T>(T value)
	{
		var dataObject = DragDropUtils.CreateDataObject(value!);
		Assert.True(DragDropUtils.TryGetDragDropData(dataObject, out T? result));
		return result!;
	}

	[Fact]
	public void DragDropRoundTrip_PreservesBezierHandles()
	{
		var result = RoundTripDragDrop(CreateBezierCurve());

		AssertHandlesIntact(result);
	}

	[Fact]
	public void DragDropRoundTrip_DoesNotLeaveJsonElementInPointTag()
	{
		var result = RoundTripDragDrop(CreateBezierCurve());

		Assert.All(result.Points, point => Assert.False(point.Tag is System.Text.Json.JsonElement));
	}

	[Fact]
	public void DataContractSave_AfterDragDropRoundTrip_DoesNotThrow()
	{
		var result = RoundTripDragDrop(CreateBezierCurve());

		var xml = SaveWithDataContract(result);

		Assert.Contains("BezierHandleData", xml);
	}

	[Fact]
	public void DataContractRoundTrip_PreservesBezierHandles()
	{
		var xml = SaveWithDataContract(CreateBezierCurve());

		Curve reloaded;
		using (var reader = XmlReader.Create(new StringReader(xml)))
		{
			reloaded = (Curve)new DataContractSerializer(typeof(Curve)).ReadObject(reader)!;
		}

		AssertHandlesIntact(reloaded);
	}

	[Fact]
	public void WipeDataSave_AfterCurveDraggedOntoIntensity_DoesNotThrow()
	{
		// Mirrors the reported crash: a Bezier curve dragged onto the Wipe effect's intensity
		// curve, then the sequence saved.
		var wipeData = new WipeData
		{
			PassCount = 30,
			Curve = RoundTripDragDrop(CreateBezierCurve())
		};

		var xml = SaveWithDataContract(wipeData);

		Assert.Contains("BezierHandleData", xml);
	}

	[Fact]
	public void DataContractSave_WithUnserializableTag_DropsTagInsteadOfThrowing()
	{
		var curve = CreateBezierCurve();
		curve.Points[2].Tag = new object();

		var xml = SaveWithDataContract(curve);

		Assert.Contains("BezierHandleData", xml);
		Assert.Null(curve.Points[2].Tag);
	}

	[Fact]
	public void DataContractSave_PointWithForeignTag_DropsTagInsteadOfThrowing()
	{
		// Curve clears foreign tags before the writer sees them, but a point that reaches the writer
		// by some other route must not be able to abort the save either.
		var points = new PointPairList(new[] { 0.0, 100.0 }, new[] { 0.0, 100.0 });
		points[0].Tag = System.Text.Json.JsonDocument.Parse("{\"InHandleX\":0}").RootElement;

		var xml = SaveWithDataContract(points);

		Assert.DoesNotContain("JsonElement", xml);
	}

	[Fact]
	public void DataContractSave_PointWithStringTag_KeepsTag()
	{
		var points = new PointPairList(new[] { 0.0, 100.0 }, new[] { 0.0, 100.0 });
		points[0].Tag = "a zedgraph label";

		var xml = SaveWithDataContract(points);

		Assert.Contains("a zedgraph label", xml);
	}
}
