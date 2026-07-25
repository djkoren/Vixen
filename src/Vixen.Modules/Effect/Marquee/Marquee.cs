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
		/// Fraction of a full pattern period an LED can be shifted by at maximum randomness.
		/// </summary>
		private const double JitterFraction = 0.5;

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
		private int _period;
		private int _colorCount;
		private double _fadeGroup;
		private double _fadeWidth;
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
		[ProviderDescription(@"Direction")]
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
				// The fade group can never be wider than the lit group; snap it down if needed.
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
		[ProviderDescription(@"Number of dark LEDs in the gap between each lit group.")]
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

		[Value]
		[ProviderCategory(@"Config", 1)]
		[ProviderDisplayName(@"Fade Group")]
		[ProviderDescription(@"How many LEDs fade in and out together at each edge of the lit group.  1 = each LED fades one at a time (classic marquee); higher fades more LEDs together as a unit.  The center of the group always reaches full brightness.  Cannot exceed Lights On and snaps down automatically.")]
		[PropertyOrder(3)]
		public int FadeGroup
		{
			get { return _data.FadeGroup; }
			set
			{
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
		[ProviderDescription(@"Movement speed over the duration of the effect.  The low end of the curve is a very slow creep and the high end is fast; most of the range is devoted to slow motion.")]
		[PropertyOrder(4)]
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
		[ProviderDescription(@"Adds a random per LED timing offset to when each LED fades and lights.  Off is a perfectly synced marquee; higher values give an organic crawling shimmer.")]
		[PropertyEditor("SliderEditor")]
		[NumberRange(0, 100, 1)]
		[PropertyOrder(5)]
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
		[ProviderDescription(@"How the color list is laid out across the marquee: a solid color per group, a gradient across each group, or one gradient stretched along the whole prop.")]
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
		[ProviderDescription(@"Color")]
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
		[ProviderDescription(@"Shape of the fade ramp (off to full) applied over the Fade Group width at each edge of the lit group.  A straight line ramps evenly; an eased curve gives a warmer incandescent glow.  A flat curve gives hard bulbs with no fade.")]
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
		[ProviderDescription(@"Overall brightness of the effect over its duration.")]
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
			_period = Math.Max(1, _onCount + _offCount);
			_colorCount = Math.Max(1, Colors.Count);
			// Fade group in LEDs (clamped to the lit group width).
			_fadeGroup = Math.Min(Math.Max(1, FadeGroup), _onCount);
			// Effective ramp half-width at each edge.  Capped at half the lit group so the middle always reaches
			// full brightness -- this is what lets even a 1-LED-on marquee hit 100%.
			_fadeWidth = Math.Min(_fadeGroup, _onCount / 2.0);
			if (_fadeWidth < 0.5) _fadeWidth = 0.5;

			// Determine which axis the pattern travels along and in which direction.
			_moveAlongX = Direction == MarqueeDirection.Left || Direction == MarqueeDirection.Right;
			_dirSign = (Direction == MarqueeDirection.Right || Direction == MarqueeDirection.Down) ? 1.0 : -1.0;

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

			// When there is no randomness the pattern is identical across the axis perpendicular to the movement,
			// so we can evaluate each position along the movement axis just once and reuse it across the rows/columns.
			if (Randomness <= 0)
			{
				Color[] line = new Color[axisLength];
				for (int s = 0; s < axisLength; s++)
				{
					line[s] = RenderPixel(s, axisLength, 0.0, level);
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
			else
			{
				for (int x = 0; x < width; x++)
				{
					for (int y = 0; y < height; y++)
					{
						double s = _moveAlongX ? x : y;
						Color c = RenderPixel(s, axisLength, GetJitter(x, y), level);
						if (c.A != 0)
						{
							frameBuffer.SetPixel(x, y, c);
						}
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
					Color c = RenderPixel(s, axisLength, GetJitter(zx, zy), level);
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
		/// Calculates the per LED random timing offset (in LED units) for the pixel at the specified coordinate.
		/// The offset is a stable function of the coordinate so a given LED consistently leads or lags.
		/// </summary>
		/// <param name="x">Pixel X coordinate</param>
		/// <param name="y">Pixel Y coordinate</param>
		/// <returns>Phase offset in LED units</returns>
		private double GetJitter(int x, int y)
		{
			if (Randomness <= 0)
			{
				return 0.0;
			}

			double signed = Hash01(x, y) * 2.0 - 1.0;
			return signed * (Randomness / 100.0) * _period * JitterFraction;
		}

		/// <summary>
		/// Computes the rendered color for a single pixel.  Returns <see cref="Color.Transparent"/> when the pixel is
		/// in a gap (off).
		/// </summary>
		/// <param name="s">Coordinate of the pixel along the movement axis (zero based)</param>
		/// <param name="axisLength">Length of the movement axis</param>
		/// <param name="jitter">Per LED random phase offset in LED units</param>
		/// <param name="level">Overall brightness level for the frame (0..1)</param>
		/// <returns>The color for the pixel, or transparent if it is off</returns>
		private Color RenderPixel(double s, int axisLength, double jitter, double level)
		{
			// Colour uses the un-jittered position so colour groups stay stable (no bleed between gradients);
			// the fade timing uses the jittered position so the randomness makes each LED crawl independently.
			double sColour = s - _dirSign * _phase;
			double sFade = sColour + jitter;

			// Position within the scrolling pattern.
			double c = Mod(sFade, _period);
			if (c >= _onCount)
			{
				// In the dark gap between groups.
				return Color.Transparent;
			}

			// Fade ramp: off at each edge of the lit group, rising to full once _fadeWidth LEDs inside, and full
			// across the middle.  Motion stays smooth because c is continuous; a flat fade curve gives instant on/off.
			double dEdge = Math.Min(c, _onCount - c);
			double u = dEdge / _fadeWidth;
			if (u > 1.0) u = 1.0;
			int idx = (int)(u * (_lutSize - 1) + 0.5);
			if (idx < 0) idx = 0;
			else if (idx > _lutSize - 1) idx = _lutSize - 1;

			double brightness = _fadeLut[idx] * level;
			if (brightness <= 0.0)
			{
				return Color.Transparent;
			}

			Color baseColor = GetBaseColor(sColour, c, s, axisLength);

			HSV hsv = HSV.FromRGB(baseColor);
			hsv.V *= (float)brightness;
			if (hsv.V > 1f) hsv.V = 1f;
			return hsv.ToRGB();
		}

		/// <summary>
		/// Determines the (un-dimmed) color for a pixel based on the selected color mode.
		/// </summary>
		/// <param name="sColour">Un-jittered scrolling position, used to pick a stable colour group</param>
		/// <param name="cFade">Position within the lit group (0..OnCount), used for the gradient across the group</param>
		/// <param name="s">Fixed coordinate of the pixel along the movement axis (zero based)</param>
		/// <param name="axisLength">Length of the movement axis</param>
		/// <returns>The base color for the pixel</returns>
		private Color GetBaseColor(double sColour, double cFade, double s, int axisLength)
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
					int colorIndex = GetGroupColorIndex(sColour);
					double u = cFade / _onCount;
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
					int colorIndex = GetGroupColorIndex(sColour);
					return Colors[colorIndex].GetColorAt(0.0);
				}
			}
		}

		/// <summary>
		/// Returns the color list index for the group that contains the specified pattern position.  Consecutive
		/// groups step through the color list and each colored group travels with the pattern.
		/// </summary>
		/// <param name="sBase">Continuous scrolling position within the pattern</param>
		/// <returns>Index into the color list</returns>
		private int GetGroupColorIndex(double sBase)
		{
			long group = (long)Math.Floor(sBase / _period);
			int index = (int)(((group % _colorCount) + _colorCount) % _colorCount);
			return index;
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
		/// Deterministic hash of a pixel coordinate to a value in the range [0, 1).
		/// </summary>
		private static double Hash01(int x, int y)
		{
			unchecked
			{
				uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
				h ^= h >> 13;
				h *= 0x85ebca6bu;
				h ^= h >> 16;
				return (h & 0xFFFFFFu) / (double)0x1000000u;
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
