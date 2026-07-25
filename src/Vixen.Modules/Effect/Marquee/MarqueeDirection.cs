using System.ComponentModel;

namespace VixenModules.Effect.Marquee
{
	/// <summary>
	/// Direction the marquee pattern travels along the display element.
	/// </summary>
	public enum MarqueeDirection
	{
		[Description("Moves Right")]
		Right,

		[Description("Moves Left")]
		Left,

		[Description("Moves Down")]
		Down,

		[Description("Moves Up")]
		Up
	}
}
