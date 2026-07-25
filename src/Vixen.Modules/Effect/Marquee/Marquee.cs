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
		/// Fraction of the gap a group can be shifted by at maximum randomness. Half a gap is the point
		/// at which two groups leaning towards each other exactly meet, so staying at or below it means
		/// the randomizer can never merge two groups or hide one behind another - every group keeps its
		/// full width and only the spacing between them varies.
		/// </summary>
		private const double MaxJitterGapFraction = 0.5;

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

		private bool _moveAlongX;
		private double _dirSign;

		/// <summary>
		/// Lookup table of the fade envelope (0..1) sampled across a lit group.  Avoids evaluating the curve per pixel.
		/// </summary>
		private double[] _fadeLut;
		private int _lutSize;

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

		#endregion

		#region Color properties

		[Value]
		[ProviderCategory(@"Color", 2)]
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
		[ProviderCategory(@"Color", 2)]
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
		[ProviderCategory(@"Brightness", 3)]
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
		[ProviderCategory(@"Brightness", 3)]
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
				int axisLength = _moveAlongX ? BufferWi : BufferHt;
				double axisBanks = Math.Floor(axisLength / _fadeGroup);
				double repeats = Math.Floor(axisBanks / _periodBanks);
				if (repeats >= 1)
				{
					_periodBanks = axisBanks / repeats;
				}
			}

			_period = _periodBanks * _fadeGroup;

			// Largest amount a single group can be shifted early or late by the randomizer.  It is scaled
			// by the gap rather than the whole period so groups shuffle within the dark space between them
			// instead of running into each other.  With no gap at all there is nowhere to move.
			double gap = Math.Max(0.0, _period - _litWidth);
			_jitterAmount = Randomness <= 0 ? 0.0 : (Randomness / 100.0) * gap * MaxJitterGapFraction;

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
		}

		/// <summary>
		/// Renders a single frame in string (grid) mode.
		/// </summary>
		/// <param name="frame">Current frame number</param>
		/// <param name="frameBuffer">Frame buffer to render in</param>
		protected override void RenderEffect(int frame, IPixelFrameBuffer frameBuffer)
		{
			UpdatePhase(frame);

			double level = LevelCurve.GetValue(GetEffectTimeIntervalPosition(frame) * 100.0) / 100.0;

			int width = BufferWi;
			int height = BufferHt;
			int axisLength = _moveAlongX ? width : height;

			// The pattern only varies along the movement axis -- the randomizer shifts whole groups rather
			// than individual LEDs, so a group looks the same right across the perpendicular axis.  One line
			// is therefore evaluated and reused across the rows/columns.
			Color[] line = new Color[axisLength];
			for (int s = 0; s < axisLength; s++)
			{
				line[s] = RenderPixel(s, axisLength, level);
			}

			if (_moveAlongX)
			{
				for (int x = 0; x < width; x++)
				{
					Color c = line[x];
					if (c.A == 0) continue;
					for (int y = 0; y < height; y++)
					{
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
				UpdatePhase(frame);

				double level = LevelCurve.GetValue(GetEffectTimeIntervalPosition(frame) * 100.0) / 100.0;
				int axisLength = _moveAlongX ? BufferWi : BufferHt;

				foreach (ElementLocation location in frameBuffer.ElementLocations)
				{
					// Offset to zero based coordinates for the pattern math.
					int zx = location.X - BufferWiOffset;
					int zy = location.Y - BufferHtOffset;

					double s = _moveAlongX ? zx : zy;
					Color c = RenderPixel(s, axisLength, level);
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
		/// Advances the continuous scroll position for the specified frame based on the speed curve.
		/// </summary>
		/// <param name="frame">Current frame number</param>
		private void UpdatePhase(int frame)
		{
			if (frame == 0)
			{
				// Restart at the beginning of the pattern.  RenderEffect is called for frame 0 first for every
				// target node, so resetting here keeps the movement deterministic.
				_phase = 0.0;
				return;
			}

			double intervalPos = GetEffectTimeIntervalPosition(frame) * 100.0;
			double ledsPerSecond = SpeedToLedsPerSecond(SpeedCurve.GetValue(intervalPos));
			_phase += ledsPerSecond * (FrameTime / 1000.0);
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
		/// Randomness is added afterwards and is deliberately not snapped - that per group offset is the
		/// whole point of it.
		/// </remarks>
		/// <param name="group">Index of the group within the scrolling pattern</param>
		/// <returns>Position of the group's leading edge in pattern space</returns>
		private double GroupStart(long group)
		{
			// Floor(x + 0.5) rather than Math.Round: banker's rounding would pull every other exact half
			// the wrong way and make the spacing lumpy.
			return Math.Floor(group * _periodBanks + 0.5) * _fadeGroup + GroupJitter(group);
		}

		/// <summary>
		/// Finds the lit group a bank falls inside, if any.
		/// </summary>
		/// <remarks>
		/// A bank is in or out as a unit, decided on its centre. Because group starts sit on bank boundaries
		/// and a group is a whole number of banks wide, exactly <see cref="_onBanks"/> banks are lit at every
		/// instant: the lit count never flickers, and a group is never drawn wider than it was asked to be.
		/// Both the start snapping and the randomizer can move a group off the cell the evenly spaced maths
		/// would put it in, so the neighbours either side are tested too; both displacements are well under
		/// half a period, so one group either way is always enough. Where two groups could claim the bank the
		/// one holding it further from its own edge wins.
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
			// the bank containing it spans [b, b+step) and its centre is half a step in.
			double centre = (Math.Floor(s / _fadeGroup) + 0.5) * _fadeGroup - _dirSign * _phase;

			long group;
			double c;
			if (!TryFindGroup(centre, out group, out c))
			{
				// In the dark gap between groups.
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

			double brightness = _fadeLut[idx] * level;
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
			TypeDescriptor.Refresh(this);
		}

		#endregion
	}
}
