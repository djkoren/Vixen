using System.ComponentModel;

namespace VixenModules.Effect.Marquee
{
	/// <summary>
	/// Determines how the color list is laid out across the marquee pattern.
	/// </summary>
	public enum MarqueeColorMode
	{
		// Each lit group is a single solid color; consecutive groups step through the color list.
		[Description("Solid Per Group")]
		SolidPerGroup,

		// The gradient is blended across the lit LEDs of each group.
		[Description("Gradient Across Group")]
		GradientAcrossGroup,

		// The color list forms one gradient stretched across the whole display element; the groups reveal slices of it.
		[Description("Gradient Along Prop")]
		GradientAlongProp
	}
}
