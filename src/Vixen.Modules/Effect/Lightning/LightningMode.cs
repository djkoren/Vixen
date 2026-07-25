using System.ComponentModel;

namespace VixenModules.Effect.Lightning
{
	public enum LightningMode
	{
		[Description("Flash")]
		Flash,
		[Description("Bolt")]
		Bolt
	}

	public enum LightningSource
	{
		[Description("Random")]
		Random,
		[Description("Mark Collection")]
		MarkCollection
	}

	public enum StringSetupMode
	{
		[Description("String")]
		String,
		[Description("Location")]
		Location
	}

	public enum OrientationMode
	{
		[Description("Vertical")]
		Vertical,
		[Description("Horizontal")]
		Horizontal
	}

	public enum MarkPattern
	{
		[Description("Single Flash")]
		Single,
		[Description("Building Flashes (before mark)")]
		Building,
		[Description("Double Flash (on mark)")]
		Double,
		[Description("Triple Flash (on mark)")]
		Triple,
		[Description("Aftershock (small flashes after)")]
		Aftershock
	}

	public enum BoltFadeMode
	{
		[Description("With Flickers")]
		WithFlickers,
		[Description("Clean Fade")]
		CleanFade
	}

	public enum FullHitMode
	{
		[Description("None")]
		None,
		[Description("Current Color")]
		CurrentColor,
		[Description("White")]
		White,
		[Description("White then Color")]
		WhiteThenColor
	}
}
