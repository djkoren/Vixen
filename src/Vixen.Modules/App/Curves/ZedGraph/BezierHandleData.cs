using System;
using System.Runtime.Serialization;

namespace ZedGraph
{
	/// <summary>
	/// Stores Bezier control handle data for a <see cref="PointPair"/>.
	/// Handle offsets are relative to the anchor point in curve-space (0-100 range).
	/// Store an instance in <see cref="PointPair.Tag"/> to enable Bezier interpolation
	/// for the segments adjacent to that point.
	/// </summary>
	[Serializable]
	[DataContract]
	public class BezierHandleData : ICloneable
	{
		/// <summary>
		/// X offset of the incoming (left) handle relative to the anchor point.
		/// Must be &lt;= 0 to maintain X monotonicity.
		/// </summary>
		[DataMember] public double InHandleX;

		/// <summary>
		/// Y offset of the incoming (left) handle relative to the anchor point.
		/// </summary>
		[DataMember] public double InHandleY;

		/// <summary>
		/// X offset of the outgoing (right) handle relative to the anchor point.
		/// Must be &gt;= 0 to maintain X monotonicity.
		/// </summary>
		[DataMember] public double OutHandleX;

		/// <summary>
		/// Y offset of the outgoing (right) handle relative to the anchor point.
		/// </summary>
		[DataMember] public double OutHandleY;

		/// <summary>
		/// Whether this point has an incoming handle. False for the first point in a curve.
		/// </summary>
		[DataMember] public bool HasInHandle;

		/// <summary>
		/// Whether this point has an outgoing handle. False for the last point in a curve.
		/// </summary>
		[DataMember] public bool HasOutHandle;

		/// <summary>
		/// Creates a deep copy of this handle data.
		/// </summary>
		public object Clone()
		{
			return MemberwiseClone();
		}

		/// <summary>
		/// Clamps handle X offsets to enforce monotonicity.
		/// InHandleX must be &lt;= 0, OutHandleX must be &gt;= 0.
		/// </summary>
		public void EnforceMonotonicity()
		{
			if (InHandleX > 0) InHandleX = 0;
			if (OutHandleX < 0) OutHandleX = 0;
		}

		/// <summary>
		/// Evaluates the cubic Bezier curve at parameter t for one axis.
		/// B(t) = (1-t)^3 * p0 + 3*(1-t)^2*t * c1 + 3*(1-t)*t^2 * c2 + t^3 * p1
		/// </summary>
		public static double CubicBezier(double t, double p0, double c1, double c2, double p1)
		{
			double u = 1.0 - t;
			return u * u * u * p0
			     + 3.0 * u * u * t * c1
			     + 3.0 * u * t * t * c2
			     + t * t * t * p1;
		}

		/// <summary>
		/// Finds the parameter t in [0,1] where the X component of the Bezier equals xTarget,
		/// using the bisection method. Requires Bx(t) to be monotonically increasing.
		/// </summary>
		public static double SolveBezierT(double xTarget, double p0x, double c1x, double c2x, double p1x,
			int maxIterations = 50, double tolerance = 1e-10)
		{
			double lo = 0.0, hi = 1.0;

			for (int i = 0; i < maxIterations; i++)
			{
				double mid = (lo + hi) * 0.5;
				double xMid = CubicBezier(mid, p0x, c1x, c2x, p1x);

				if (Math.Abs(xMid - xTarget) < tolerance)
					return mid;

				if (xMid < xTarget)
					lo = mid;
				else
					hi = mid;
			}

			return (lo + hi) * 0.5;
		}
	}
}
