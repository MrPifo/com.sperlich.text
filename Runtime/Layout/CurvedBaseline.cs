using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// A polyline path the text baseline can follow. Characters are distributed by arc length and
	/// rotated to the local tangent (plan module 5.2). A spline-backed variant can be added later;
	/// the polyline form keeps v1 free of the Splines API surface.
	/// </summary>
	public sealed class CurvedBaseline {

		private readonly List<float2> points = new();
		private readonly List<float> cumulative = new(); // arc length at each point
		private float totalLength;

		public float Length => totalLength;
		public bool IsValid => points.Count >= 2 && totalLength > 1e-4f;

		public void SetWaypoints(IReadOnlyList<Vector2> waypoints) {
			points.Clear();
			cumulative.Clear();
			totalLength = 0f;
			if (waypoints == null || waypoints.Count < 2) return;

			points.Add(waypoints[0]);
			cumulative.Add(0f);
			for (int i = 1; i < waypoints.Count; i++) {
				float2 p = waypoints[i];
				float seg = math.distance(points[^1], p);
				if (seg < 1e-5f) continue;
				totalLength += seg;
				points.Add(p);
				cumulative.Add(totalLength);
			}
		}

		/// <summary>Samples the path at <paramref name="distance"/> along its arc length (clamped).</summary>
		public void Evaluate(float distance, out float2 position, out float tangentAngle) {
			if (IsValid == false) {
				position = float2.zero;
				tangentAngle = 0f;
				return;
			}

			distance = math.clamp(distance, 0f, totalLength);
			int seg = 1;
			while (seg < cumulative.Count && cumulative[seg] < distance) seg++;
			if (seg >= points.Count) seg = points.Count - 1;

			float2 a = points[seg - 1];
			float2 b = points[seg];
			float segLen = cumulative[seg] - cumulative[seg - 1];
			float t = segLen > 1e-5f ? (distance - cumulative[seg - 1]) / segLen : 0f;

			position = math.lerp(a, b, t);
			float2 dir = math.normalizesafe(b - a, new float2(1, 0));
			tangentAngle = math.atan2(dir.y, dir.x);
		}
	}
}
