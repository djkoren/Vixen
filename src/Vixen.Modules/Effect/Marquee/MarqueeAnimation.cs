using System.ComponentModel;

namespace VixenModules.Effect.Marquee
{
	/// <summary>
	/// How the marquee pattern animates on and off. The Animation curve drives whichever one is
	/// selected, with 50 meaning fully assembled: below 50 the pattern is arriving, above 50 it is
	/// leaving by the opposite route, so one curve covers both.
	/// </summary>
	public enum MarqueeAnimation
	{
		[Description("None")]
		None,

		[Description("Slide")]
		Slide,

		[Description("Dissolve")]
		Dissolve,

		[Description("Stack")]
		Stack,

		[Description("Scale")]
		Scale
	}

	/// <summary>
	/// Which end of the movement axis an animation arrives from. The far end is automatically where it
	/// leaves, so the two halves of the Animation curve are different animations rather than mirror
	/// images. Named for a horizontal movement axis; on a vertical one they read as top and bottom.
	/// There is no ends-inward member because reversing the curve gives exactly that.
	/// </summary>
	public enum MarqueeAnimationFrom
	{
		[Description("From Left")]
		Left,

		[Description("From Right")]
		Right,

		[Description("From Center Out")]
		CenterOut
	}

	/// <summary>
	/// In what order groups land when a stack starts from the centre.
	/// </summary>
	public enum MarqueeCenterOrder
	{
		[Description("Both Sides At Once")]
		BothSides,

		[Description("Alternate Sides")]
		Alternate
	}
}
