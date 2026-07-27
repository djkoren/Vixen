using Common.Controls.ColorManagement.ColorModels;
using System.ComponentModel;
using Vixen.Attributes;
using Vixen.Module;
using Vixen.Sys.Attribute;
using VixenModules.App.ColorGradients;
using VixenModules.App.Curves;
using VixenModules.Effect.Effect;
using VixenModules.Effect.Effect.Location;
using VixenModules.EffectEditor.EffectDescriptorAttributes;
using Color = System.Drawing.Color;

namespace VixenModules.Effect.Marquee
{
	/// <summary>
	/// Old fashioned marquee chase effect.  A repeating pattern of "on" LEDs followed by a gap of "off" LEDs
	/// travels smoothly along the display element.  Unlike the Bars effect this uses a continuous (sub pixel)
	/// position and a per LED fade envelope so that even very slow movement stays clean and smooth.
	/// </summary>
	public class Marquee : PixelEffectBase
	{
		#region Constants

		/// <summary>
		/// Slowest movement in LEDs per second (a value near zero so the low end of the speed curve
		/// is a barely perceptible creep).
		/// </summary>
		private const double MinSpeedLedsPerSecond = 0.02;

		/// <summary>
		/// Fastest movement in LEDs per second.
		/// </summary>
		private const double MaxSpeedLedsPerSecond = 120.0;

		/// <summary>
		/// Safety margin on the gap-closing budget, so rounding can never quite eat the last dark bank.
		/// </summary>
		private const double GapClosureMargin = 0.95;

		/// <summary>
		/// Hard ceiling on any single group's displacement, as a fraction of the gap. Keeping every group
		/// inside its own dark space is what lets the group search only look one cell either side.
		/// </summary>
		private const double MaxOffsetGapFraction = 0.5;

		/// <summary>
		/// Fraction of a ripple cycle a group spends easing forward, spending the rest of it at rest.
		/// Below 1 there is a real pause between shoves; at 1 the motion never stops. Because it is a
		/// fraction of the cycle rather than a fixed time, a slow ripple glides and a fast one snaps.
		/// </summary>
		private const double RippleRiseFraction = 0.6;

		/// <summary>
		/// How much of the dissolve a group spends crossing between dark and lit. Above zero so groups fade
		/// across rather than popping on.
		/// </summary>
		private const double DissolveRampWidth = 0.25;

		/// <summary>
		/// Animation gate below which a pixel is treated as off. Rounding leaves the fully-closed cases
		/// sitting a hair above zero rather than on it, and without a floor that emits a pixel that is
		/// black anyway - so a fully animated-out pattern would never quite be empty.
		/// </summary>
		private const double MinVisibleGate = 1e-4;

		/// <summary>Slowest ripple rate in cycles per second.</summary>
		private const double MinRippleHz = 0.05;

		/// <summary>Fastest ripple rate in cycles per second.</summary>
		private const double MaxRippleHz = 6.0;

		/// <summary>
		/// How many sub-frame samples a fully open (360 degree) shutter is smeared over. The samples are
		/// what the blur is made of, so too few band the smear into separate copies; the count scales down
		/// with the shutter angle so a narrow shutter does not pay for samples it cannot show.
		/// </summary>
		private const int MaxMotionBlurSamples = 16;

		#endregion

		#region Private Fields

		private MarqueeData _data;

		/// <summary>
		/// Continuous scroll position of the pattern in LED units.  Accumulated across frames so movement is smooth.
		/// </summary>
		private double _phase;

		// Values cached once per render in SetupRender.
		private int _onCount;
		private int _offCount;

		/// <summary>
		/// Length of one on+off cycle in LED units. Fractional when <see cref="FitToElement"/> stretches
		/// the gap so a whole number of cycles spans the element exactly.
		/// </summary>
		private double _period;

		private int _colorCount;
		private double _fadeGroup;

		/// <summary>Number of banks in a lit group.</summary>
		private int _onBanks;

		/// <summary>
		/// Width of a lit group in LEDs, rounded to a whole number of banks. This is the effective
		/// Lights On; it differs from the requested value only when the step size does not divide it.
		/// </summary>
		private double _litWidth;

		/// <summary>
		/// The pattern period expressed in banks rather than LEDs. Group starts are laid out on this so
		/// they always land on a bank boundary.
		/// </summary>
		private double _periodBanks;

		/// <summary>Maximum per group timing offset in LED units (0 when Randomness is off).</summary>
		private double _jitterAmount;

		/// <summary>How far a ripple shoves a group, in LED units (0 when ripples are off).</summary>
		private double _rippleStep;

		/// <summary>Ripple rate in cycles per second.</summary>
		private double _rippleHz;

		/// <summary>How many groups sit between one ripple and the next.</summary>
		private double _rippleGroups;

		/// <summary>
		/// How many ripples have passed, at the frame being rendered. Driven by elapsed time rather than
		/// by travel, so the ripples run at their own rate and are the pattern's only motion when Speed
		/// is zero - which is what lets a group actually sit still between shoves.
		/// </summary>
		private double _ripplePhase;

		/// <summary>Scroll position contributed by the speed curve alone, before the ripples add theirs.</summary>
		private double _scrollPhase;

		/// <summary>
		/// Where the animation curve sits at the frame being rendered, remapped so 0 is fully assembled:
		/// -1 is completely gone the way it arrives from, +1 completely gone the opposite way.
		/// </summary>
		private double _animSigned;

		/// <summary>How far from assembled, 0 (fully on) to 1 (fully gone), whichever way.</summary>
		private double _animAmount;

		/// <summary>True when an animation is selected and the curve is actually away from 50.</summary>
		private bool _animActive;

		/// <summary>
		/// Which LEDs are blown, indexed <c>y * BufferWi + x</c>. Null when there are none, which is also
		/// the flag for keeping the single-line render fast path.
		/// </summary>
		private bool[] _badBulbs;

		private bool _moveAlongX;
		private double _dirSign;

		/// <summary>
		/// Length of the movement axis in LEDs. Cached because the animations lay themselves out on the
		/// element as a whole rather than on the pattern, and because it is what they wrap around.
		/// </summary>
		private int _axisLength;

		/// <summary>How many group slots fit on the element, which is how many landings fill a stack.</summary>
		private int _stackSlots;

		/// <summary>
		/// Fraction of a frame the shutter is open, from the Motion Blur angle. Zero closes it, which
		/// short-circuits the whole sub-frame path.
		/// </summary>
		private double _shutter;

		/// <summary>
		/// The sub-frame states the current frame is smeared over: one entry with a closed shutter, more as
		/// it opens. Rebuilt once per frame so the curves are evaluated per frame rather than per pixel.
		/// </summary>
		private FrameState[] _frameStates;
		private int _frameStateCount;

		/// <summary>
		/// Lookup table of the fade envelope (0..1) sampled across a lit group.  Avoids evaluating the curve per pixel.
		/// </summary>
		private double[] _fadeLut;
		private int _lutSize;

		#endregion

		#region Nested Types

		/// <summary>
		/// Everything about a single instant that the pixel maths reads: where the pattern has scrolled to,
		/// how far through the ripples and the animation it is, and how bright the effect is overall.
		/// </summary>
		/// <remarks>
		/// A frame is one of these with the shutter closed and several spread across it when it is open, so
		/// motion blur is just rendering the same pixel against each of them and averaging. Keeping them in
		/// a struct is what lets the curves be sampled once per frame instead of once per pixel per sample.
		/// </remarks>
		private struct FrameState
		{
			public double Phase;
			public double RipplePhase;
			public double AnimSigned;
			public double AnimAmount;
			public bool AnimActive;
			public double Level;
		}

		#endregion

		#region Constructor

		public Marquee()
		{
			_data = new MarqueeData();
			EnableTargetPositioning(true, true);
			InitAllAttributes();
		}

		#endregion

		#region Public (Override) Methods

		public override bool IsDirty
		{
			get
			{
				if (Colors.Any(x => !x.CheckLibraryReference()))
				{
					base.IsDirty = true;
				}

				return base.IsDirty;
			}
			protected set { base.IsDirty = value; }
		}

		#endregion

		#region Public Properties

		public override IModuleDataModel ModuleData
		{
			get { return _data; }
			set
			{
				_data = value as MarqueeData;
				InitAllAttributes();
				IsDirty = true;
			}
		}

		#endregion

		#region Setup

		[Value]
		public override StringOrientation StringOrientation
		{
			get { return _data.Orientation; }
			set
			{
				_data.Orientation = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		#endregion

		#region Config properties

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Direction")]
		[ProviderDescription(@"Which way the pattern travels.")]
		[PropertyOrder(0)]
		public MarqueeDirection Direction
		{
			get { return _data.Direction; }
			set
			{
				_data.Direction = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Lights On")]
		[ProviderDescription(@"Number of LEDs lit in each group.")]
		[PropertyOrder(1)]
		public int OnCount
		{
			get { return _data.OnCount; }
			set
			{
				if (value < 1) value = 1;
				_data.OnCount = value;
				// The step can never be wider than the lit group; snap it down if needed.
				if (_data.FadeGroup > value)
				{
					FadeGroup = value;
				}
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Lights Off")]
		[ProviderDescription(@"Number of dark LEDs between lit groups.")]
		[PropertyOrder(2)]
		public int OffCount
		{
			get { return _data.OffCount; }
			set
			{
				if (value < 0) value = 0;
				_data.OffCount = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Gets or sets whether the gap is padded so a whole number of on/off cycles spans the element
		/// exactly. Lights Off is the minimum gap; the pattern is only ever spread out, never tightened.
		/// </summary>
		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Fit To Element")]
		[ProviderDescription(@"Space the groups evenly across the element.  Lights Off is the minimum gap.")]
		[PropertyOrder(3)]
		public bool FitToElement
		{
			get { return _data.FitToElement; }
			set
			{
				_data.FitToElement = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Advance By")]
		[ProviderDescription(@"How many LEDs the pattern moves by.  They light and fade together.")]
		[PropertyOrder(4)]
		public int FadeGroup
		{
			get { return _data.FadeGroup; }
			set
			{
				// Kept as FadeGroup on the data model so existing sequences still deserialize; the UI calls
				// it Advance By, which is what it actually does.
				if (value < 1) value = 1;
				else if (value > _data.OnCount) value = _data.OnCount;
				_data.FadeGroup = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Speed")]
		[ProviderDescription(@"Movement speed over the effect.  Most of the range is slow, fine control.")]
		[PropertyOrder(5)]
		public Curve SpeedCurve
		{
			get { return _data.SpeedCurve; }
			set
			{
				_data.SpeedCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Randomness")]
		[ProviderDescription(@"Shifts each lit group early or late.  Needs a gap to move in.")]
		[PropertyEditor("SliderEditor")]
		[NumberRange(0, 100, 1)]
		[PropertyOrder(6)]
		public int Randomness
		{
			get { return _data.Randomness; }
			set
			{
				_data.Randomness = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Gets or sets how many ripples travel along the element at once. Each ripple shoves every group
		/// it reaches forward by one step, so groups advance in sequence and then hold until the next
		/// ripple arrives. 0 turns ripples off.
		/// </summary>
		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Ripples")]
		[ProviderDescription(@"Ripples running along the element.  Each one carries a group forward a step.")]
		[PropertyEditor("SliderEditor")]
		[NumberRange(0, 20, 1)]
		[PropertyOrder(7)]
		public int Ripples
		{
			get { return _data.Ripples; }
			set
			{
				if (value < 0) value = 0;
				_data.Ripples = value;
				UpdateRippleAttributes();
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Gets or sets how fast a ripple travels, 0 to 100. Independent of <see cref="SpeedCurve"/>: with
		/// Speed at zero the ripples are the only motion, which is what lets a group sit still between
		/// shoves instead of gliding through the pause.
		/// </summary>
		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Ripple Speed")]
		[ProviderDescription(@"How fast the ripples run.")]
		[PropertyEditor("SliderEditor")]
		[NumberRange(0, 100, 1)]
		[PropertyOrder(8)]
		public int RippleSpeed
		{
			get { return _data.RippleSpeed; }
			set
			{
				_data.RippleSpeed = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Gets or sets how many LEDs on the element are blown and never light, like an old marquee sign
		/// with a few dead bulbs. 0 = none.
		/// </summary>
		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Bad Bulbs")]
		[ProviderDescription(@"How many LEDs are blown and never light.")]
		[PropertyOrder(9)]
		public int BadBulbs
		{
			get { return _data.BadBulbs; }
			set
			{
				if (value < 0) value = 0;
				_data.BadBulbs = value;
				UpdateBadBulbAttributes();
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Gets or sets which LEDs are blown. The same seed always picks the same ones, so editing other
		/// properties never reshuffles them; change it to shuffle to a different arrangement.
		/// </summary>
		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Bad Bulb Seed")]
		[ProviderDescription(@"Change to shuffle which bulbs are blown.")]
		[PropertyOrder(10)]
		public int BadBulbSeed
		{
			get { return _data.BadBulbSeed; }
			set
			{
				_data.BadBulbSeed = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		#endregion

		#region Animation properties

		/// <summary>
		/// Gets or sets which animation brings the pattern on and off.
		/// </summary>
		[Value]
		[ProviderCategory(@"Animate In/Out", 2)]
		[ProviderDisplayName(@"Animation")]
		[ProviderDescription(@"How the pattern arrives and leaves.")]
		[PropertyOrder(0)]
		public MarqueeAnimation Animation
		{
			get { return _data.Animation; }
			set
			{
				_data.Animation = value;
				UpdateAnimationAttributes();
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Gets or sets the curve driving the animation. 50 is fully assembled; below 50 the pattern is
		/// arriving and above 50 it is leaving by the opposite route, so one curve does both.
		/// </summary>
		[Value]
		[ProviderCategory(@"Animate In/Out", 2)]
		[ProviderDisplayName(@"Animation Curve")]
		[ProviderDescription(@"50 is fully on.  Below is arriving, above is leaving the other way.")]
		[PropertyOrder(1)]
		public Curve AnimationCurve
		{
			get { return _data.AnimationCurve; }
			set
			{
				_data.AnimationCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Gets or sets which end of the movement axis the animation arrives from. It leaves by the far
		/// end.
		/// </summary>
		[Value]
		[ProviderCategory(@"Animate In/Out", 2)]
		[ProviderDisplayName(@"Animate From")]
		[ProviderDescription(@"Which end it arrives from.  It leaves by the other.")]
		[PropertyOrder(2)]
		public MarqueeAnimationFrom AnimationFrom
		{
			get { return _data.AnimationFrom; }
			set
			{
				_data.AnimationFrom = value;
				UpdateAnimationAttributes();
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Gets or sets the motion a group travels through as it slides into its slot in a stack. 0 is the
		/// moment it sets off and 100 the moment it lands, so an eased curve gives the groups weight.
		/// </summary>
		[Value]
		[ProviderCategory(@"Animate In/Out", 2)]
		[ProviderDisplayName(@"Stack Curve")]
		[ProviderDescription(@"How a group moves as it slides into place.")]
		[PropertyOrder(3)]
		public Curve StackCurve
		{
			get { return _data.StackCurve; }
			set
			{
				_data.StackCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Gets or sets in what order groups land when a stack starts from the centre: both sides together,
		/// or one at a time alternating sides.
		/// </summary>
		[Value]
		[ProviderCategory(@"Animate In/Out", 2)]
		[ProviderDisplayName(@"Center Order")]
		[ProviderDescription(@"Whether both sides land together or alternate.")]
		[PropertyOrder(4)]
		public MarqueeCenterOrder CenterOrder
		{
			get { return _data.CenterOrder; }
			set
			{
				_data.CenterOrder = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Gets or sets the shutter angle in degrees, read exactly as on a film camera: 0 is a closed
		/// shutter and a hard, strobed edge, 180 the usual cinema look, 360 a shutter open for the whole
		/// frame so the smear from one frame meets the next with no gap.
		/// </summary>
		[Value]
		[ProviderCategory(@"Animate In/Out", 2)]
		[ProviderDisplayName(@"Motion Blur")]
		[ProviderDescription(@"Shutter angle.  0 is no blur, 180 the usual film look, 360 a fully open shutter.")]
		[PropertyEditor("SliderEditor")]
		[NumberRange(0, 360, 1)]
		[PropertyOrder(5)]
		public int MotionBlur
		{
			get { return _data.MotionBlur; }
			set
			{
				if (value < 0) value = 0;
				else if (value > 360) value = 360;
				_data.MotionBlur = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		#endregion

		#region Color properties

		[Value]
		[ProviderCategory(@"Color", 3)]
		[ProviderDisplayName(@"Color Mode")]
		[ProviderDescription(@"How the colors are laid out across the marquee.")]
		[PropertyOrder(0)]
		public MarqueeColorMode ColorMode
		{
			get { return _data.ColorMode; }
			set
			{
				_data.ColorMode = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Color", 3)]
		[ProviderDisplayName(@"ColorGradients")]
		[ProviderDescription(@"The color palette.  Groups cycle through it.")]
		[PropertyOrder(1)]
		public List<ColorGradient> Colors
		{
			get { return _data.Colors; }
			set
			{
				_data.Colors = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		#endregion

		#region Brightness properties

		[Value]
		[ProviderCategory(@"Brightness", 4)]
		[ProviderDisplayName(@"Fade")]
		[ProviderDescription(@"Brightness of an LED from the moment it lights to the moment it goes dark.  Flat = no fade.")]
		[PropertyOrder(0)]
		public Curve FadeCurve
		{
			get { return _data.FadeCurve; }
			set
			{
				_data.FadeCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		[Value]
		[ProviderCategory(@"Brightness", 4)]
		[ProviderDisplayName(@"Brightness")]
		[ProviderDescription(@"Overall brightness over the effect.")]
		[PropertyOrder(1)]
		public Curve LevelCurve
		{
			get { return _data.LevelCurve; }
			set
			{
				_data.LevelCurve = value;
				IsDirty = true;
				OnPropertyChanged();
			}
		}

		#endregion

		#region Information

		public override string Information
		{
			get { return "Visit the Vixen Lights website for more information on this effect."; }
		}

		public override string InformationLink
		{
			get { return "http://www.vixenlights.com/vixen-3-documentation/sequencer/effects/"; }
		}

		#endregion

		#region Protected Properties

		protected override EffectTypeModuleData EffectModuleData => _data;

		#endregion

		#region Protected Methods

		/// <summary>
		/// Performs the calculations that only need to be done once per render.
		/// </summary>
		protected override void SetupRender()
		{
			_onCount = Math.Max(1, OnCount);
			_offCount = Math.Max(0, OffCount);
			_colorCount = Math.Max(1, Colors.Count);

			// Determine which axis the pattern travels along and in which direction.
			_moveAlongX = Direction == MarqueeDirection.Left || Direction == MarqueeDirection.Right;
			_dirSign = (Direction == MarqueeDirection.Right || Direction == MarqueeDirection.Down) ? 1.0 : -1.0;
			_axisLength = Math.Max(1, _moveAlongX ? BufferWi : BufferHt);

			// Width of one bank of LEDs that lights and steps together (clamped to the lit group width).
			_fadeGroup = Math.Min(Math.Max(1, FadeGroup), _onCount);

			// Everything is laid out in whole banks.  Banks are fixed to the element, so a lit width or a
			// pitch that was not a whole number of them would land every group on a different bank alignment
			// and each group would then sit at its own point in the fade -- which reads as a chase running
			// through the pattern instead of one pattern moving as a unit.  A whole number of banks also
			// keeps the lit count constant instead of flickering between two values as the pattern moves.
			// With a step of 1 (the default) none of this rounds anything.
			_onBanks = Math.Max(1, (int)Math.Floor(_onCount / _fadeGroup + 0.5));
			_litWidth = _onBanks * _fadeGroup;

			// Rounding the gap up keeps Lights Off a minimum: it can be padded by up to one bank.
			_periodBanks = _onBanks + Math.Ceiling(_offCount / _fadeGroup);

			if (FitToElement)
			{
				// Spread the pattern so a whole number of cycles spans the element.  Dividing the element by
				// the cycle count (rather than searching for an integer gap) keeps the spacing as even as the
				// bank grid allows: the pitch may be a fractional number of banks, which is what stops a
				// rounding error accumulating along the element.  The result is never shorter than the
				// requested Lights On + Lights Off, so Lights Off still acts as a minimum gap.
				double axisBanks = Math.Floor(_axisLength / _fadeGroup);
				double repeats = Math.Floor(axisBanks / _periodBanks);
				if (repeats >= 1)
				{
					_periodBanks = axisBanks / repeats;
				}
			}

			_period = _periodBanks * _fadeGroup;

			// How many groups the element holds, which is how many landings it takes to fill a stack.  It has
			// to be a whole number: a fractional count would leave the last landing adding a sliver of a group
			// and, worse, would stop a fully assembled stack landing exactly on "everything lit".
			_stackSlots = _period > 0.0 ? Math.Max(1, (int)Math.Floor(_axisLength / _period + 0.5)) : 1;

			// Ripples are spread over the element, so the count is what the eye sees travelling along it
			// however long the prop is: one ripple sweeps end to end, four chase each other.
			double totalGroups = _period > 0.0 ? _axisLength / _period : 1.0;
			_rippleGroups = Math.Max(1.0, totalGroups / Math.Max(1, Ripples));
			_rippleHz = RippleSpeed <= 0
				? 0.0
				: MinRippleHz * Math.Pow(MaxRippleHz / MinRippleHz, Math.Min(100, RippleSpeed) / 100.0);

			// Randomness and the ripples both displace groups, and between them they must not close the
			// gap, or two groups run together and the lit count stops matching Lights On.
			//
			// The budget is what is left of the gap once one step is set aside.  Not overlapping is not
			// enough: LEDs are discrete, so if the dark space between two groups shrinks below one step no
			// LED lands in it and they read as a single run even though the maths never overlapped them.
			// Keeping a step in reserve guarantees a dark LED always survives between groups.
			//
			// Ripples are served first because their shove is a fixed quantum rather than a slider - one
			// step, the same unit the whole pattern is built on.  Whatever the ripples do not need is left
			// to randomness, which costs two units of gap per unit of amplitude because two neighbours can
			// sit at opposite extremes of their range at the same time.
			double gap = Math.Max(0.0, _period - _litWidth);
			double closureBudget = Math.Max(0.0, gap - _fadeGroup) * GapClosureMargin;

			_rippleStep = Ripples <= 0 ? 0.0 : Math.Min(_fadeGroup, closureBudget);

			const double randomGapCost = 2.0;
			double randomWeight = Math.Min(100, Math.Max(0, Randomness)) / 100.0;
			double randomBudget = Math.Max(0.0, closureBudget - _rippleStep);
			_jitterAmount = Math.Min(randomWeight * randomBudget / randomGapCost,
				randomWeight * gap * MaxOffsetGapFraction);

			BuildBadBulbs();

			// The shutter is only offered on the two animations that carry something across the element;
			// nothing else has motion of its own for it to smear, and leaving it out of those keeps them
			// rendering exactly as they always have.
			bool canBlur = Animation == MarqueeAnimation.Slide || Animation == MarqueeAnimation.Stack;
			_shutter = canBlur ? Math.Min(360, Math.Max(0, MotionBlur)) / 360.0 : 0.0;
			int blurSamples = _shutter <= 0.0
				? 1
				: 1 + (int)Math.Ceiling(_shutter * (MaxMotionBlurSamples - 1));
			_frameStates = new FrameState[blurSamples];
			_frameStateCount = blurSamples;

			// Pre-sample the fade curve into a lookup table.
			_lutSize = 257;
			_fadeLut = new double[_lutSize];
			for (int i = 0; i < _lutSize; i++)
			{
				double x = (double)i / (_lutSize - 1) * 100.0;
				double val = FadeCurve.GetValue(x) / 100.0;
				if (val < 0.0) val = 0.0;
				else if (val > 1.0) val = 1.0;
				_fadeLut[i] = val;
			}

			_phase = 0.0;
		}

		protected override void CleanUpRender()
		{
			_fadeLut = null;
			_badBulbs = null;
			_frameStates = null;
		}

		/// <summary>
		/// Picks the blown bulbs for this render, or leaves the mask null when there are none.
		/// </summary>
		/// <remarks>
		/// Selection is driven by a <see cref="Random"/> seeded from <see cref="BadBulbSeed"/> rather than
		/// by hashing each index, because that gives exactly the requested count instead of approximately
		/// it. Seeding is the point: an unseeded generator would reshuffle the bulbs on every re-render,
		/// so simply nudging an unrelated property would silently move them.
		/// </remarks>
		private void BuildBadBulbs()
		{
			_badBulbs = null;

			int total = BufferWi * BufferHt;
			if (BadBulbs <= 0 || total <= 0) return;

			// Leave at least one bulb alive; an entirely dead element is never what was wanted.
			int dead = Math.Min(BadBulbs, total - 1);
			if (dead <= 0) return;

			var mask = new bool[total];
			var random = new Random(BadBulbSeed);
			int placed = 0;
			while (placed < dead)
			{
				int index = random.Next(total);
				if (mask[index]) continue;
				mask[index] = true;
				placed++;
			}

			_badBulbs = mask;
		}

		/// <summary>
		/// True if the pixel at the specified buffer coordinate is a blown bulb.
		/// </summary>
		/// <param name="x">Zero based buffer X coordinate</param>
		/// <param name="y">Zero based buffer Y coordinate</param>
		/// <returns>True when the bulb never lights</returns>
		private bool IsBadBulb(int x, int y)
		{
			if (_badBulbs == null) return false;
			if (x < 0 || y < 0 || x >= BufferWi || y >= BufferHt) return false;
			return _badBulbs[y * BufferWi + x];
		}

		/// <summary>
		/// Renders a single frame in string (grid) mode.
		/// </summary>
		/// <param name="frame">Current frame number</param>
		/// <param name="frameBuffer">Frame buffer to render in</param>
		protected override void RenderEffect(int frame, IPixelFrameBuffer frameBuffer)
		{
			BuildFrameStates(frame);

			int width = BufferWi;
			int height = BufferHt;
			int axisLength = _moveAlongX ? width : height;

			// The pattern only varies along the movement axis -- the randomizer shifts whole groups rather
			// than individual LEDs, so a group looks the same right across the perpendicular axis.  One line
			// is therefore evaluated and reused across the rows/columns.
			Color[] line = new Color[axisLength];
			for (int s = 0; s < axisLength; s++)
			{
				line[s] = RenderPixelExposed(s, axisLength);
			}

			// Blown bulbs are the one thing that does vary across the perpendicular axis, so they cannot
			// come out of the shared line -- reusing it would kill whole rows of a matrix instead of single
			// bulbs.  Only pay for the per-pixel walk when there are actually some.
			bool anyBadBulbs = _badBulbs != null;

			if (_moveAlongX)
			{
				for (int x = 0; x < width; x++)
				{
					Color c = line[x];
					if (c.A == 0) continue;
					for (int y = 0; y < height; y++)
					{
						if (anyBadBulbs && IsBadBulb(x, y)) continue;
						frameBuffer.SetPixel(x, y, c);
					}
				}
			}
			else
			{
				for (int y = 0; y < height; y++)
				{
					Color c = line[y];
					if (c.A == 0) continue;
					for (int x = 0; x < width; x++)
					{
						if (anyBadBulbs && IsBadBulb(x, y)) continue;
						frameBuffer.SetPixel(x, y, c);
					}
				}
			}
		}

		/// <summary>
		/// Renders the effect in location (pixel position) mode.
		/// </summary>
		/// <param name="numFrames">Number of frames to render</param>
		/// <param name="frameBuffer">Frame buffer to render in</param>
		protected override void RenderEffectByLocation(int numFrames, PixelLocationFrameBuffer frameBuffer)
		{
			for (int frame = 0; frame < numFrames; frame++)
			{
				frameBuffer.CurrentFrame = frame;
				BuildFrameStates(frame);

				int axisLength = _moveAlongX ? BufferWi : BufferHt;

				foreach (ElementLocation location in frameBuffer.ElementLocations)
				{
					// Offset to zero based coordinates for the pattern math.
					int zx = location.X - BufferWiOffset;
					int zy = location.Y - BufferHtOffset;

					if (IsBadBulb(zx, zy)) continue;

					double s = _moveAlongX ? zx : zy;
					Color c = RenderPixelExposed(s, axisLength);
					if (c.A != 0)
					{
						frameBuffer.SetPixel(location.X, location.Y, c);
					}
				}
			}
		}

		#endregion

		#region Private Methods

		/// <summary>
		/// Advances the scroll position and works out the sub-frame instants the specified frame is made of.
		/// </summary>
		/// <remarks>
		/// With the shutter closed there is exactly one instant, the frame itself, and the render is the
		/// same one it always was. Opening the shutter spreads the samples evenly across it and centres them
		/// on the frame, so the blur grows out either side rather than dragging the whole effect late.
		///
		/// The curves are read here, once per frame, rather than inside the pixel loop: a blurred render
		/// then costs extra pixel maths but no extra curve evaluation, which is the expensive half.
		/// </remarks>
		/// <param name="frame">Current frame number</param>
		private void BuildFrameStates(int frame)
		{
			double frameSeconds = FrameTime / 1000.0;
			double ledsPerSecond = SpeedToLedsPerSecond(SpeedCurve.GetValue(SubFramePosition(frame) * 100.0));

			if (frame == 0)
			{
				// Restart at the beginning of the pattern.  RenderEffect is called for frame 0 first for every
				// target node, so resetting here keeps the movement deterministic.
				_scrollPhase = 0.0;
			}
			else
			{
				_scrollPhase += ledsPerSecond * frameSeconds;
			}

			for (int i = 0; i < _frameStateCount; i++)
			{
				// Where in the frame this sample sits, in frames.  A closed shutter lands dead on the frame.
				double offset = _frameStateCount == 1
					? 0.0
					: _shutter * ((double)i / (_frameStateCount - 1) - 0.5);
				double position = SubFramePosition(frame + offset);

				FrameState state;

				// The ripples run at a constant rate, so their count is computed straight from the frame
				// number rather than accumulated -- no drift, and it needs no reset between target nodes.
				state.RipplePhase = (frame + offset) * frameSeconds * _rippleHz;

				// Every ripple shoves every group it passes forward by a step, so on average the ripples carry
				// the whole pattern along at one step per ripple.  That average is real movement and belongs in
				// the scroll; what is left over per group -- whether it has been shoved yet or is still waiting
				// -- is the stagger, and lives in GroupRipple.  Splitting it this way is what lets a group sit
				// genuinely still between shoves rather than drifting through the pause.
				//
				// Speed is taken as constant within the frame, which it is to well inside a frame's worth of
				// travel.
				state.Phase = _scrollPhase + ledsPerSecond * offset * frameSeconds + _rippleStep * state.RipplePhase;
				state.Level = LevelCurve.GetValue(position * 100.0) / 100.0;

				// Remap the animation curve so 0 is assembled.  Exactly 50 has to land on exactly 0 or the
				// neutral position would not be neutral and simply picking an animation would nudge the
				// pattern.
				if (Animation == MarqueeAnimation.None)
				{
					state.AnimSigned = 0.0;
					state.AnimAmount = 0.0;
					state.AnimActive = false;
				}
				else
				{
					double signed = (AnimationCurve.GetValue(position * 100.0) - 50.0) / 50.0;
					if (signed < -1.0) signed = -1.0;
					else if (signed > 1.0) signed = 1.0;
					state.AnimSigned = signed;
					state.AnimAmount = Math.Abs(signed);
					state.AnimActive = state.AnimAmount > 0.0;
				}

				_frameStates[i] = state;
			}

			// Leave the fields on the first sample so the un-blurred path can render straight off them.
			UseState(0);
		}

		/// <summary>
		/// Points the pixel maths at one of the frame's sub-frame instants.
		/// </summary>
		/// <param name="index">Index into the frame's sample list</param>
		private void UseState(int index)
		{
			FrameState state = _frameStates[index];
			_phase = state.Phase;
			_ripplePhase = state.RipplePhase;
			_animSigned = state.AnimSigned;
			_animAmount = state.AnimAmount;
			_animActive = state.AnimActive;
		}

		/// <summary>
		/// Where a fractional frame sits in the effect, 0 to 1.
		/// </summary>
		/// <remarks>
		/// The base class only offers whole frames, and the shutter's samples fall between them. Clamping
		/// holds the end values for samples that spill off either end of the effect; on a whole frame this
		/// returns exactly what the base class does.
		/// </remarks>
		/// <param name="frame">Frame number, which may be fractional</param>
		/// <returns>Position through the effect, 0 to 1</returns>
		private double SubFramePosition(double frame)
		{
			if (TimeSpan == TimeSpan.Zero)
			{
				return 0.0;
			}

			double position = frame * FrameTime / TimeSpan.TotalMilliseconds;
			if (position < 0.0) position = 0.0;
			else if (position > 1.0) position = 1.0;
			return position;
		}

		/// <summary>
		/// Maps a 0..100 speed curve value to a movement rate in LEDs per second.  The mapping is exponential so
		/// that the majority of the slider range is dedicated to slow, fine grained movement.
		/// </summary>
		/// <param name="value">Speed curve value (0..100)</param>
		/// <returns>Movement rate in LEDs per second</returns>
		private static double SpeedToLedsPerSecond(double value)
		{
			if (value <= 0.0)
			{
				return 0.0;
			}

			if (value > 100.0)
			{
				value = 100.0;
			}

			return MinSpeedLedsPerSecond * Math.Pow(MaxSpeedLedsPerSecond / MinSpeedLedsPerSecond, value / 100.0);
		}

		/// <summary>
		/// Random timing offset for a whole lit group, in LED units.  The offset is a stable function of the
		/// group index so a given group consistently leads or lags, and every LED inside it moves with it.
		/// </summary>
		/// <param name="group">Index of the group within the scrolling pattern</param>
		/// <returns>Phase offset in LED units</returns>
		private double GroupJitter(long group)
		{
			if (_jitterAmount <= 0.0)
			{
				return 0.0;
			}

			return (Hash01(group) * 2.0 - 1.0) * _jitterAmount;
		}

		/// <summary>
		/// Ripple displacement for a whole lit group, in LED units.
		/// </summary>
		/// <remarks>
		/// A group is either waiting for the next ripple, being carried by it, or already done with it -
		/// a staircase with eased risers rather than a wave. That is the whole point: a group holds
		/// position, flows forward a step, then holds again until the next ripple reaches it. A smooth
		/// wave cannot do that, because it is never not moving; a bare <c>floor</c> cannot either,
		/// because it teleports.
		///
		/// The staircase counts ripples, so on its own it would climb forever. The part that climbs is
		/// identical for every group and is real forward movement, so <see cref="UpdatePhase"/> puts it in
		/// the scroll; subtracting it here leaves only the bounded stagger of who has been shoved and who
		/// has not. The group's place in the queue is taken modulo the ripple spacing, which keeps the
		/// stagger periodic instead of stretching the pattern out along its length.
		/// </remarks>
		/// <param name="group">Index of the group within the scrolling pattern</param>
		/// <returns>Displacement in LED units, within one step either side of nominal</returns>
		private double GroupRipple(long group)
		{
			if (_rippleStep <= 0.0)
			{
				return 0.0;
			}

			double placeInQueue = Mod(group, _rippleGroups) / _rippleGroups;
			double ripplesPassed = _ripplePhase - placeInQueue;
			double whole = Math.Floor(ripplesPassed);

			// Ease across the shove rather than teleporting: smoothstep over the opening stretch of the
			// cycle, then hold for the rest.  The group leaves and arrives at a standstill, so there is no
			// jolt at either end of the move.
			double rise = Math.Min(1.0, (ripplesPassed - whole) / RippleRiseFraction);
			double eased = rise * rise * (3.0 - 2.0 * rise);

			return _rippleStep * (whole + eased - _ripplePhase + 1.0);
		}

		/// <summary>
		/// Leading edge of a lit group in pattern space.
		/// </summary>
		/// <remarks>
		/// Group starts are snapped to a bank boundary. Banks are fixed to the element, so a group starting
		/// part way through one would sit at a different point in the fade from its neighbours and the
		/// pattern would read as a chase running through it rather than as one pattern moving as a unit.
		/// Snapping puts every group on the same alignment, so they all fade in step. The rounding is what
		/// makes the spacing alternate between the floor and the ceiling of the pitch when
		/// <see cref="FitToElement"/> makes that fractional, which is the closest an evenly spaced pattern
		/// can get on a discrete strip, and it still lands the last group on the end of the element.
		/// Randomness and the ripples are added afterwards and are deliberately not snapped - putting
		/// groups out of step with each other is the whole point of them.
		/// </remarks>
		/// <param name="group">Index of the group within the scrolling pattern</param>
		/// <returns>Position of the group's leading edge in pattern space</returns>
		private double GroupStart(long group)
		{
			// Floor(x + 0.5) rather than Math.Round: banker's rounding would pull every other exact half
			// the wrong way and make the spacing lumpy.
			return Math.Floor(group * _periodBanks + 0.5) * _fadeGroup + GroupJitter(group) + GroupRipple(group);
		}

		/// <summary>
		/// Finds the lit group a bank falls inside, if any.
		/// </summary>
		/// <remarks>
		/// A bank is in or out as a unit, decided on its centre. Because group starts sit on bank boundaries
		/// and a group is a whole number of banks wide, exactly <see cref="_onBanks"/> banks are lit at every
		/// instant: the lit count never flickers, and a group is never drawn wider than it was asked to be.
		/// The start snapping, the randomizer and the crawl can each move a group off the cell the evenly
		/// spaced maths would put it in, so the neighbours either side are tested too; the displacements
		/// together are capped at half a gap, well under half a period, so one group either way is always
		/// enough. Where two groups could claim the bank the one holding it further from its own edge wins.
		/// </remarks>
		/// <param name="centre">Centre of the bank in pattern space (movement already applied)</param>
		/// <param name="group">Index of the group owning the bank; undefined when the method returns false</param>
		/// <param name="offset">Bank centre's position within the owning group, 0 to <see cref="_litWidth"/></param>
		/// <returns>True if the bank falls inside a lit group; false if it is in a gap</returns>
		private bool TryFindGroup(double centre, out long group, out double offset)
		{
			long nearest = (long)Math.Floor(centre / _period);

			double bestEdge = -1.0;
			bool found = false;
			group = nearest;
			offset = 0.0;

			for (long g = nearest - 1; g <= nearest + 1; g++)
			{
				double local = centre - GroupStart(g);
				if (local < 0.0 || local >= _litWidth) continue;

				double edge = Math.Min(local, _litWidth - local);
				if (edge <= bestEdge) continue;

				bestEdge = edge;
				group = g;
				offset = local;
				found = true;
			}

			return found;
		}

		/// <summary>
		/// Computes the color for a pixel across the whole of the frame's exposure.
		/// </summary>
		/// <remarks>
		/// With the shutter closed this is one look at one instant and costs nothing extra. Open it and the
		/// pixel is rendered against each sub-frame instant and the results averaged, which is what motion
		/// blur physically is: light collected while things moved.
		///
		/// The average is over *every* sample, not only the lit ones. A bulb that was dark for part of the
		/// exposure genuinely put less light on the film, and it is that dimming - a group's leading edge
		/// arriving part way through the frame and so arriving dim - that reads as the smear rather than as
		/// a group that has simply grown wider.
		/// </remarks>
		/// <param name="s">Coordinate of the pixel along the movement axis (zero based)</param>
		/// <param name="axisLength">Length of the movement axis</param>
		/// <returns>The color for the pixel, or transparent if it was dark for the whole exposure</returns>
		private Color RenderPixelExposed(double s, int axisLength)
		{
			if (_frameStateCount == 1)
			{
				return RenderPixel(s, axisLength, _frameStates[0].Level);
			}

			double red = 0.0, green = 0.0, blue = 0.0;
			bool anyLit = false;

			for (int i = 0; i < _frameStateCount; i++)
			{
				UseState(i);
				Color sample = RenderPixel(s, axisLength, _frameStates[i].Level);
				if (sample.A == 0) continue;

				red += sample.R;
				green += sample.G;
				blue += sample.B;
				anyLit = true;
			}

			if (!anyLit)
			{
				return Color.Transparent;
			}

			double scale = 1.0 / _frameStateCount;
			return Color.FromArgb(255, ToByte(red * scale), ToByte(green * scale), ToByte(blue * scale));
		}

		/// <summary>
		/// Rounds a 0..255 channel value to a byte, clamped.
		/// </summary>
		private static int ToByte(double value)
		{
			int result = (int)(value + 0.5);
			if (result < 0) return 0;
			return result > 255 ? 255 : result;
		}

		/// <summary>
		/// Computes the rendered color for a single pixel.  Returns <see cref="Color.Transparent"/> when the pixel is
		/// in a gap (off).
		/// </summary>
		/// <param name="s">Coordinate of the pixel along the movement axis (zero based)</param>
		/// <param name="axisLength">Length of the movement axis</param>
		/// <param name="level">Overall brightness level for the frame (0..1)</param>
		/// <returns>The color for the pixel, or transparent if it is off</returns>
		private Color RenderPixel(double s, int axisLength, double level)
		{
			// The element is divided into fixed banks of the step size and the whole bank is treated as one
			// lamp, so every LED in it shares a brightness and switches with it.  An LED covers [s, s+1), so
			// the bank containing it spans [b, b+step) and its centre is half a step in.  The bank rather
			// than the raw coordinate is what the slide is measured against too, so its edge never cuts a
			// bank in half.
			double bankCentre = (Math.Floor(s / _fadeGroup) + 0.5) * _fadeGroup;

			// Slide carries the pattern onto the element and shows only the stretch it has reached so far.
			double slide = 0.0;
			if (_animActive && Animation == MarqueeAnimation.Slide && !TrySlide(bankCentre, axisLength, out slide))
			{
				// The pattern has not reached this LED yet, or has already left it.
				return Color.Transparent;
			}

			double centre = bankCentre - slide - _dirSign * _phase;

			long group;
			double c;
			bool onGroup = TryFindGroup(centre, out group, out c);

			// Stack and Dissolve gate whole groups: a group is either in play, on its way in, or not there
			// yet.  Scale trims each group from the edges or hollows it from the middle.
			double animGate = 1.0;
			if (_animActive && Animation == MarqueeAnimation.Stack)
			{
				// Asked even where the LED falls in a gap.  The group sliding in is displaced from its own
				// slot, so it covers LEDs that its slot does not - returning early on the gap would mean it
				// was only ever visible once it had already landed.
				animGate = StackGate(onGroup, centre, ref group, ref c);
			}
			else if (!onGroup)
			{
				// In the dark gap between groups.
				return Color.Transparent;
			}
			else if (_animActive)
			{
				switch (Animation)
				{
					case MarqueeAnimation.Dissolve:
						animGate = DissolveGate(group);
						break;

					case MarqueeAnimation.Scale:
						animGate = ScaleGate(c);
						break;
				}
			}

			if (animGate <= MinVisibleGate)
			{
				return Color.Transparent;
			}

			// The Fade curve is read across the bank's journey through the lit group: 0 the moment it lights
			// and 1 just before it goes dark, whichever way the pattern is travelling.  Nothing is layered on
			// top of it, so the curve alone decides the shape -- a rising line ramps up and snaps off, a
			// falling line snaps on and ramps down, and a curve that peaks in the middle fades both ways.
			double u = _dirSign > 0.0 ? (_litWidth - c) / _litWidth : c / _litWidth;
			if (u < 0.0) u = 0.0;
			else if (u > 1.0) u = 1.0;
			int idx = (int)(u * (_lutSize - 1) + 0.5);
			if (idx < 0) idx = 0;
			else if (idx > _lutSize - 1) idx = _lutSize - 1;

			double brightness = _fadeLut[idx] * level * animGate;
			if (brightness <= 0.0)
			{
				return Color.Transparent;
			}

			Color baseColor = GetBaseColor(group, c, s, axisLength);

			HSV hsv = HSV.FromRGB(baseColor);
			hsv.V *= (float)brightness;
			if (hsv.V > 1f) hsv.V = 1f;
			return hsv.ToRGB();
		}

		#region Animation

		/// <summary>
		/// Whether a bank is inside the sliding pattern, and how far the pattern has been carried from its
		/// assembled position to get there.
		/// </summary>
		/// <remarks>
		/// The pattern really does slide: it is a sheet an element long that is displaced right off the
		/// element at the extremes of the curve and sits exactly over it at 50, and only the part of the
		/// sheet actually on the element is drawn. Opening a window onto a pattern that never moved is what
		/// made this read as a wipe rather than a slide - the boundary moved but nothing behind it did.
		///
		/// All of it is measured in a frame that travels with the pattern (<c>u</c> below), which is what
		/// makes the two lock together. The window's far edge then moves at exactly the speed of the
		/// pattern's leading edge, so nothing is ever revealed there; new pattern arrives only at the entry
		/// edge, which is the whole difference between a slide and a wipe. It also means a curve held part
		/// way leaves the visible stretch circling the element with the pattern instead of parked in a dead
		/// half, and that the entry point follows along rather than staying pinned to one end of the prop.
		///
		/// <see cref="MarqueeAnimationFrom.CenterOut"/> is the same thing done twice: the sheet is torn at
		/// the middle and the two halves slide out to their own ends, so the pattern still arrives by
		/// moving. Above 50 they carry straight on and leave by those ends, emptying the middle first.
		/// </remarks>
		/// <param name="bankCentre">Centre of the bank along the movement axis (zero based)</param>
		/// <param name="axisLength">Length of the movement axis</param>
		/// <param name="shift">How far the pattern is displaced from assembled, in LEDs; 0 when it has landed</param>
		/// <returns>True when the sliding pattern covers this bank</returns>
		private bool TrySlide(double bankCentre, int axisLength, out double shift)
		{
			double length = axisLength;

			// Position in the frame that travels with the pattern.  The seam at 0 is where the pattern
			// enters, and it drifts along the element with the scroll.
			double u = Mod(bankCentre - _dirSign * _phase, length);

			if (AnimationFrom == MarqueeAnimationFrom.CenterOut)
			{
				double half = length / 2.0;

				// Each half is displaced away from the middle and only shows where it has reached.  Splitting
				// on the middle rather than on the two halves' own extents matters while they are bunched
				// together at the start, when both would otherwise claim the same LEDs.
				if (u < half)
				{
					shift = -_animSigned * half;
					return u >= shift && u < half + shift;
				}

				shift = _animSigned * half;
				return u >= half + shift && u < length + shift;
			}

			// Arriving from the right is the mirror of arriving from the left, and above 50 the sheet simply
			// carries on the same way and leaves by the far end - so one signed displacement covers all four
			// journeys.
			shift = (AnimationFrom == MarqueeAnimationFrom.Right ? -1.0 : 1.0) * _animSigned * length;
			return u >= shift && u < length + shift;
		}

		/// <summary>
		/// How lit a group is under the dissolve, 0 (not yet) to 1 (fully in).
		/// </summary>
		/// <remarks>
		/// Each group gets a threshold from <see cref="Hash01"/>, so the order is scattered but is a stable
		/// function of the group - identical on every frame and on every re-render. That last part is why
		/// this does not borrow the Dissolve effect's approach: that one reshuffles from a time-seeded
		/// generator every time it pre-renders, so editing any unrelated property silently reorders it.
		/// Groups cross over a short ramp rather than popping, so a slow dissolve reads as a fade.
		/// </remarks>
		/// <param name="group">Index of the group within the scrolling pattern</param>
		/// <returns>Brightness multiplier for the group</returns>
		private double DissolveGate(long group)
		{
			// Both halves of the curve dissolve away; the two differ in which groups go first, so an out
			// does not simply retrace the in.
			double threshold = _animSigned > 0.0 ? 1.0 - Hash01(group) : Hash01(group);

			// Sweep the front from one ramp-width below zero up to one, so that at fully out even the group
			// with the lowest threshold is still behind the ramp and dark.  Sweeping plain 0..1 would leave
			// the earliest groups part lit at the bottom of the curve.
			double assembled = 1.0 - _animAmount;
			double front = assembled * (1.0 + DissolveRampWidth) - DissolveRampWidth;

			double edge = threshold - front;
			if (edge >= DissolveRampWidth) return 0.0;
			if (edge <= 0.0) return 1.0;
			return 1.0 - edge / DissolveRampWidth;
		}

		/// <summary>
		/// Which slot on the element a group belongs to, counted from the low end.
		/// </summary>
		/// <remarks>
		/// Read from where the group sits in the *pattern* rather than from where it currently is on the
		/// element, so it is fixed for the life of the render. That is what makes a part-filled stack hold
		/// together once Speed is above zero: whatever has landed stays landed and the whole stack simply
		/// travels along with the pattern instead of groups crossing a fixed threshold and popping in and
		/// out of it.
		///
		/// The nominal position is used rather than <see cref="GroupStart"/> for the same reason - the
		/// randomizer and the ripples move a group about, and a group whose slot flickered as a ripple went
		/// past would drop out of the stack and back in.
		///
		/// Wrapping at the element length rather than at a whole number of groups is what keeps that travel
		/// seamless: the landed stretch is then exactly periodic in the element, so the part of it leaving
		/// one end is picked up by the part arriving at the other with no step in between.
		/// </remarks>
		/// <param name="group">Index of the group within the scrolling pattern</param>
		/// <returns>Slot index, 0 to <see cref="_stackSlots"/> - 1</returns>
		private int StackSlot(long group)
		{
			double nominal = Math.Floor(group * _periodBanks + 0.5) * _fadeGroup;
			int slot = (int)(Mod(nominal, _axisLength) / _period);
			if (slot < 0) return 0;
			return slot > _stackSlots - 1 ? _stackSlots - 1 : slot;
		}

		/// <summary>
		/// Where a group sits in the landing queue, counting from wherever the stack starts.
		/// </summary>
		/// <remarks>
		/// Groups pile up against the far end, so the slot furthest from where they come in is the one that
		/// fills first. That is what stacking actually does, and it also means an arriving group never has
		/// to cross the ones already landed - which would otherwise have it passing straight through them.
		/// </remarks>
		/// <param name="group">Index of the group within the scrolling pattern</param>
		/// <param name="orderCount">How many landings it takes to fill the element</param>
		/// <param name="reversed">True when the stack is unstacking, which empties from the other end</param>
		/// <param name="side">-1 or +1 for which side of the middle a centre stack lands on; 0 otherwise</param>
		/// <returns>Place in the queue, 0 being the first to land</returns>
		private double StackOrder(long group, int orderCount, bool reversed, out double side)
		{
			side = 0.0;

			int slot = StackSlot(group);
			double order;

			if (AnimationFrom == MarqueeAnimationFrom.CenterOut)
			{
				double middleSlot = (_stackSlots - 1.0) / 2.0;
				double outward = slot - middleSlot;
				side = outward >= 0.0 ? 1.0 : -1.0;

				double fromMiddle;
				if (CenterOrder == MarqueeCenterOrder.BothSides)
				{
					// Both sides land together, so a ring counts once however many slots are in it.
					fromMiddle = Math.Floor(Math.Abs(outward));
				}
				else if ((_stackSlots & 1) == 0)
				{
					// One at a time, alternating.  An even slot count pairs off exactly either side of the
					// middle, so the two sides simply interleave.
					fromMiddle = Math.Floor(Math.Abs(outward)) * 2.0 + (side > 0.0 ? 1.0 : 0.0);
				}
				else
				{
					// An odd count has one slot sitting exactly on the middle with no partner.  Interleaving
					// as if it had one would run the queue a place past the last slot and leave two groups
					// sharing a landing, so the pairing starts one ring out from it instead.
					double ring = Math.Abs(outward);
					fromMiddle = ring == 0.0 ? 0.0 : ring * 2.0 - (side > 0.0 ? 0.0 : 1.0);
				}

				// Coming out of the middle, the outermost ring is the one that fills first.
				order = orderCount - 1.0 - fromMiddle;
			}
			else
			{
				bool comesInFromLowEnd = AnimationFrom != MarqueeAnimationFrom.Right;
				order = comesInFromLowEnd ? _stackSlots - 1.0 - slot : slot;
			}

			if (order < 0.0) order = 0.0;
			else if (order > orderCount - 1.0) order = orderCount - 1.0;

			return reversed ? orderCount - 1.0 - order : order;
		}

		/// <summary>
		/// How lit a group is under the stack: landed, still sliding in, or not started yet.
		/// </summary>
		/// <remarks>
		/// Groups land one at a time from the chosen end and everything that has landed keeps running the
		/// main effect - scroll, ripples, fade and all.
		///
		/// The group still on its way in is drawn by looking the pattern up again at a shifted coordinate,
		/// which is the same trick <see cref="IsWithinSlide"/> uses and for the same reason: it moves the
		/// group a whole element's width if need be while leaving the group maths and the one-cell search
		/// in <see cref="TryFindGroup"/> completely intact. Shifting the position *within* the group
		/// instead would only ever nudge it a group's width and would slice it in half on the way.
		///
		/// How far it has left to go is only ever an estimate, but the shift reaches exactly zero when it
		/// lands, so a group always settles precisely into its slot however roughly it set off.
		///
		/// Slots belong to the pattern rather than to the element (see <see cref="StackSlot"/>), so with
		/// Speed above zero the part-built stack travels round the element as one piece and keeps running
		/// the effect while it does, rather than groups drifting across a fixed fill line and popping in and
		/// out of it. The landing count is a whole number of slots, which is also what lets a completed
		/// stack land exactly on "everything lit" instead of a hair short of it.
		/// </remarks>
		/// <param name="onGroup">True when the un-shifted lookup landed inside a group's own slot</param>
		/// <param name="centre">Centre of the bank in pattern space</param>
		/// <param name="owner">The group found naturally; replaced by the one sliding in when that wins</param>
		/// <param name="c">Position within the group, replaced for the group sliding in</param>
		/// <returns>Brightness multiplier for the group</returns>
		private double StackGate(bool onGroup, double centre, ref long owner, ref double c)
		{
			bool reversed = _animSigned > 0.0;   // unstacking starts from the other end

			bool centreStack = AnimationFrom == MarqueeAnimationFrom.CenterOut;
			int orderCount = centreStack && CenterOrder == MarqueeCenterOrder.BothSides
				? (_stackSlots + 1) / 2
				: _stackSlots;

			double filled = (1.0 - _animAmount) * orderCount;
			double landed = Math.Floor(filled);

			if (onGroup)
			{
				double order = StackOrder(owner, orderCount, reversed, out _);
				if (order < landed) return 1.0;
			}

			double arriving = filled - landed;
			if (arriving <= 0.0) return 0.0;

			// Shape the travel so the groups can be given weight rather than sliding in at a flat rate.
			double eased = StackCurve.GetValue(arriving * 100.0) / 100.0;
			if (eased < 0.0) eased = 0.0;
			else if (eased > 1.0) eased = 1.0;

			// How far it has to come.  Since the stack fills from the far end, each successive group stops
			// a slot shorter than the last.  A centre stack sets off from the middle instead, so its
			// distance follows the ring it is bound for - with alternating sides that is half the landing
			// count, and using the count directly would send a group in from the wrong side of the middle.
			double stepsOut = Math.Max(0.0, orderCount - 1.0 - landed);
			double ringsOut = centreStack && CenterOrder == MarqueeCenterOrder.Alternate
				? Math.Floor(stepsOut / 2.0)
				: stepsOut;

			// Pulling a centre stack's start in by a group keeps the pair that sets off together on their
			// own sides of the middle.  Starting both exactly at the middle has them occupy the same LED
			// as they cross, which reads as one of the two blinking out for a frame.
			double distance = centreStack
				? Math.Max(_litWidth, (ringsOut + 0.5) * _period - _litWidth)
				: stepsOut * _period + _litWidth;

			double travel = (1.0 - eased) * distance;
			if (travel <= 0.0) travel = 0.0;

			// A centre stack sends one group out each way, so both directions have to be tried.
			bool towardHigher = AnimationFrom != MarqueeAnimationFrom.Right;
			if (reversed && !centreStack) towardHigher = !towardHigher;

			// Where the bank sits in the frame that travels with the pattern, so the lookup below can be
			// held inside a single lap of the element.
			double u = Mod(centre, _axisLength);

			for (int attempt = 0; attempt < (centreStack ? 2 : 1); attempt++)
			{
				double shift = travel * (attempt == 0 ? 1.0 : -1.0) * (towardHigher ? 1.0 : -1.0);

				// A lookup that ran off the end of the element would find the next copy of the pattern round
				// and draw a second, ghost, arriving group parked at the far end before the real one had set
				// off.  Slots repeat every element, so only the copy inside this lap is the right one.
				double target = u + shift;
				if (target < 0.0 || target >= _axisLength) continue;

				long candidate;
				double candidateOffset;
				if (!TryFindGroup(centre + shift, out candidate, out candidateOffset)) continue;

				double candidateSide;
				if (StackOrder(candidate, orderCount, reversed, out candidateSide) != landed) continue;

				// On a centre stack each side travels its own way, so ignore the one going the wrong way.
				if (centreStack && candidateSide != 0.0)
				{
					bool wantHigher = candidateSide > 0.0;
					if (reversed) wantHigher = !wantHigher;
					if ((shift > 0.0) != wantHigher) continue;
				}

				owner = candidate;
				c = candidateOffset;
				return 1.0;
			}

			return 0.0;
		}

		/// <summary>
		/// How lit a position within a group is under the scale.
		/// </summary>
		/// <remarks>
		/// The two halves of the curve reach darkness by opposite routes: below 50 the group narrows toward
		/// its centre so the edges die first, above 50 a hole opens at the centre and eats outward so the
		/// middle dies first. Brightness is scaled by how much of the bank is still inside the surviving
		/// band as well as gated on it, so a one LED group fades out instead of snapping.
		/// </remarks>
		/// <param name="c">Position within the lit group, 0 to <see cref="_litWidth"/></param>
		/// <returns>Brightness multiplier for the position</returns>
		private double ScaleGate(double c)
		{
			double half = _litWidth / 2.0;

			// The band measured out from the middle of the group: below 50 it is what stays lit and closes
			// in from the edges, above 50 it is the hole and opens out from the middle.
			double bound = _animSigned > 0.0 ? _animAmount * half : (1.0 - _animAmount) * half;

			// Measure against the part of the bank that is inside the group, not the whole bank.  A
			// scrolling pattern puts banks at fractional offsets, so one can straddle the group's edge;
			// dividing by the full bank width would then leave the protruding part lit even when the band
			// covers the entire group, and a fully animated-out pattern would never go quite dark.
			double bankLow = Math.Max(0.0, c - _fadeGroup / 2.0);
			double bankHigh = Math.Min(_litWidth, c + _fadeGroup / 2.0);
			double bankWidth = bankHigh - bankLow;
			if (bankWidth <= 0.0) return 0.0;

			// Using coverage rather than a fixed soft edge is also what makes the extremes exact for a
			// one-bank group, where an edge half a step wide would be as wide as the whole group.
			double overlap = Math.Min(bankHigh, half + bound) - Math.Max(bankLow, half - bound);
			if (overlap < 0.0) overlap = 0.0;

			double covered = overlap / bankWidth;
			if (covered > 1.0) covered = 1.0;

			return _animSigned > 0.0 ? 1.0 - covered : covered;
		}

		#endregion

		/// <summary>
		/// Determines the (un-dimmed) color for a pixel based on the selected color mode.
		/// </summary>
		/// <param name="group">Index of the lit group the pixel belongs to, used to pick a stable colour</param>
		/// <param name="cFade">Position within the lit group (0..<see cref="_litWidth"/>), used for the gradient across the group</param>
		/// <param name="s">Fixed coordinate of the pixel along the movement axis (zero based)</param>
		/// <param name="axisLength">Length of the movement axis</param>
		/// <returns>The base color for the pixel</returns>
		private Color GetBaseColor(long group, double cFade, double s, int axisLength)
		{
			// Defensive: if the palette was emptied in the UI, fall back to white rather than throwing.
			if (Colors.Count == 0)
			{
				return Color.White;
			}

			switch (ColorMode)
			{
				case MarqueeColorMode.GradientAcrossGroup:
				{
					int colorIndex = GetGroupColorIndex(group);
					double u = cFade / _litWidth;
					if (u > 1.0) u = 1.0;
					else if (u < 0.0) u = 0.0;
					return Colors[colorIndex].GetColorAt(u);
				}

				case MarqueeColorMode.GradientAlongProp:
				{
					// One gradient (the concatenated color list) stretched across the whole prop.  The lit/unlit
					// groups reveal slices of this fixed spatial gradient as they move.
					double t = axisLength > 1 ? s / (double)(axisLength - 1) : 0.0;
					if (t < 0.0) t = 0.0;
					else if (t > 1.0) t = 1.0;

					double segment = t * _colorCount;
					int idx = (int)Math.Floor(segment);
					if (idx < 0) idx = 0;
					else if (idx > _colorCount - 1) idx = _colorCount - 1;
					double local = segment - idx;
					if (local < 0.0) local = 0.0;
					else if (local > 1.0) local = 1.0;
					return Colors[idx].GetColorAt(local);
				}

				default: // MarqueeColorMode.SolidPerGroup
				{
					int colorIndex = GetGroupColorIndex(group);
					return Colors[colorIndex].GetColorAt(0.0);
				}
			}
		}

		/// <summary>
		/// Returns the color list index for a group.  Consecutive groups step through the color list and each
		/// colored group travels with the pattern.
		/// </summary>
		/// <param name="group">Index of the group within the scrolling pattern</param>
		/// <returns>Index into the color list</returns>
		private int GetGroupColorIndex(long group)
		{
			return (int)(((group % _colorCount) + _colorCount) % _colorCount);
		}

		/// <summary>
		/// Floating point modulo that always returns a non negative result.
		/// </summary>
		private static double Mod(double value, double modulus)
		{
			double result = value % modulus;
			if (result < 0.0)
			{
				result += modulus;
			}
			return result;
		}

		/// <summary>
		/// Deterministic hash of a group index to a value in the range [0, 1).
		/// </summary>
		private static double Hash01(long value)
		{
			unchecked
			{
				ulong h = (ulong)value * 0x9E3779B97F4A7C15UL;
				h ^= h >> 30;
				h *= 0xBF58476D1CE4E5B9UL;
				h ^= h >> 27;
				h *= 0x94D049BB133111EBUL;
				h ^= h >> 31;
				return (h >> 11) * (1.0 / 9007199254740992.0);
			}
		}

		/// <summary>
		/// Initializes the visibility of the attributes.
		/// </summary>
		private void InitAllAttributes()
		{
			UpdateStringOrientationAttributes(true);
			UpdateRippleAttributes(false);
			UpdateAnimationAttributes(false);
			UpdateBadBulbAttributes(false);
			TypeDescriptor.Refresh(this);
		}

		/// <summary>
		/// Updates the visibility of the animation attributes. Nothing about the animation matters until one
		/// is chosen, and the axis only matters for the two that travel along it. Motion blur is offered on
		/// those same two, since they are the ones that carry something across the element for it to smear.
		/// </summary>
		/// <param name="refresh">True to refresh the type descriptor after updating visibility</param>
		private void UpdateAnimationAttributes(bool refresh = true)
		{
			bool animating = Animation != MarqueeAnimation.None;
			bool travels = Animation == MarqueeAnimation.Slide || Animation == MarqueeAnimation.Stack;
			bool stacking = Animation == MarqueeAnimation.Stack;

			Dictionary<string, bool> propertyStates = new Dictionary<string, bool>(5)
			{
				{ nameof(AnimationCurve), animating },
				{ nameof(AnimationFrom), animating && travels },
				{ nameof(StackCurve), stacking },
				{ nameof(CenterOrder), stacking && AnimationFrom == MarqueeAnimationFrom.CenterOut },
				{ nameof(MotionBlur), travels },
			};
			SetBrowsable(propertyStates);
			if (refresh)
			{
				TypeDescriptor.Refresh(this);
			}
		}

		/// <summary>
		/// Updates the visibility of the bad bulb attributes. The seed only means anything once there are
		/// some bulbs to pick.
		/// </summary>
		/// <param name="refresh">True to refresh the type descriptor after updating visibility</param>
		private void UpdateBadBulbAttributes(bool refresh = true)
		{
			Dictionary<string, bool> propertyStates = new Dictionary<string, bool>(1)
			{
				{ nameof(BadBulbSeed), BadBulbs > 0 },
			};
			SetBrowsable(propertyStates);
			if (refresh)
			{
				TypeDescriptor.Refresh(this);
			}
		}

		/// <summary>
		/// Updates the visibility of the ripple attributes. The speed only means anything once at least
		/// one ripple is asked for, so it stays out of the way until then.
		/// </summary>
		/// <param name="refresh">True to refresh the type descriptor after updating visibility</param>
		private void UpdateRippleAttributes(bool refresh = true)
		{
			Dictionary<string, bool> propertyStates = new Dictionary<string, bool>(1)
			{
				{ nameof(RippleSpeed), Ripples > 0 },
			};
			SetBrowsable(propertyStates);
			if (refresh)
			{
				TypeDescriptor.Refresh(this);
			}
		}

		#endregion
	}
}
