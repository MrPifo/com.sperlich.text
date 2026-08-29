// C# port of msdfgen/core/edge-selectors.cpp / .h — https://github.com/Chlumsky/msdfgen (MIT, Viktor Chlumsky).
using System;
using static Sperlich.Text.Rasterizer.MsdfMath;

namespace Sperlich.Text.Rasterizer {

	public struct MultiDistance { public double r, g, b; }
	public struct MultiAndTrueDistance { public double r, g, b, a; }

	/// <summary>Per-selector, per-edge scratch reused across close-together queries.</summary>
	public struct TrueDistanceEdgeCache {
		public Vector2 point;
		public double absDistance;
	}

	public struct PerpEdgeCache {
		public Vector2 point;
		public double absDistance;
		public double aDomainDistance, bDomainDistance;
		public double aPerpendicularDistance, bPerpendicularDistance;
	}

	/// <summary>CRTP-style selector contract (mirrors msdfgen's <c>EdgeSelector</c> typedef surface).</summary>
	public interface IEdgeSelector<TSelf, TDistance, TCache>
		where TSelf : class, IEdgeSelector<TSelf, TDistance, TCache>, new()
		where TCache : struct {
		void Reset(Vector2 p);
		void AddEdge(ref TCache cache, EdgeSegment prevEdge, EdgeSegment edge, EdgeSegment nextEdge);
		void Merge(TSelf other);
		TDistance Distance();
		/// <summary>msdfgen free fn <c>initDistance</c>: −∞ in every channel.</summary>
		TDistance InitialDistance();
		/// <summary>msdfgen free fn <c>resolveDistance</c>: a scalar for comparisons.</summary>
		double ResolveDistance(TDistance d);
	}

	internal static class SelectorConst {
		public const double DistanceDeltaFactor = 1.001;
	}

	// -------------------------------------------------------------------------------------------------

	public sealed class TrueDistanceSelector : IEdgeSelector<TrueDistanceSelector, double, TrueDistanceEdgeCache> {

		private Vector2 p;
		private SignedDistance minDistance = SignedDistance.Infinite;

		public void Reset(Vector2 newP) {
			double delta = SelectorConst.DistanceDeltaFactor * (newP - p).Length;
			minDistance.distance += NonZeroSign(minDistance.distance) * delta;
			p = newP;
		}

		public void AddEdge(ref TrueDistanceEdgeCache cache, EdgeSegment prevEdge, EdgeSegment edge, EdgeSegment nextEdge) {
			double delta = SelectorConst.DistanceDeltaFactor * (p - cache.point).Length;
			if (cache.absDistance - delta <= Math.Abs(minDistance.distance)) {
				SignedDistance distance = edge.SignedDistanceTo(p, out _);
				if (distance < minDistance)
					minDistance = distance;
				cache.point = p;
				cache.absDistance = Math.Abs(distance.distance);
			}
		}

		public void Merge(TrueDistanceSelector other) {
			if (other.minDistance < minDistance)
				minDistance = other.minDistance;
		}

		public double Distance() => minDistance.distance;
		public double InitialDistance() => double.MinValue;
		public double ResolveDistance(double d) => d;
	}

	// -------------------------------------------------------------------------------------------------

	/// <summary>Shared perpendicular-distance state; not a selector on its own.</summary>
	internal sealed class PerpendicularDistanceSelectorBase {

		public SignedDistance MinTrueDistance = SignedDistance.Infinite;
		private double minNegativePerpendicularDistance;
		private double minPositivePerpendicularDistance;
		private EdgeSegment nearEdge;
		private double nearEdgeParam;

		public PerpendicularDistanceSelectorBase() {
			minNegativePerpendicularDistance = -Math.Abs(MinTrueDistance.distance);
			minPositivePerpendicularDistance = Math.Abs(MinTrueDistance.distance);
		}

		public static bool GetPerpendicularDistance(ref double distance, Vector2 ep, Vector2 edgeDir) {
			double ts = DotProduct(ep, edgeDir);
			if (ts > 0) {
				double perpendicularDistance = CrossProduct(ep, edgeDir);
				if (Math.Abs(perpendicularDistance) < Math.Abs(distance)) {
					distance = perpendicularDistance;
					return true;
				}
			}
			return false;
		}

		public void Reset(double delta) {
			MinTrueDistance.distance += NonZeroSign(MinTrueDistance.distance) * delta;
			minNegativePerpendicularDistance = -Math.Abs(MinTrueDistance.distance);
			minPositivePerpendicularDistance = Math.Abs(MinTrueDistance.distance);
			nearEdge = null;
			nearEdgeParam = 0;
		}

		public bool IsEdgeRelevant(in PerpEdgeCache cache, Vector2 p) {
			double delta = SelectorConst.DistanceDeltaFactor * (p - cache.point).Length;
			return
				cache.absDistance - delta <= Math.Abs(MinTrueDistance.distance) ||
				Math.Abs(cache.aDomainDistance) < delta ||
				Math.Abs(cache.bDomainDistance) < delta ||
				(cache.aDomainDistance > 0 && (cache.aPerpendicularDistance < 0
					? cache.aPerpendicularDistance + delta >= minNegativePerpendicularDistance
					: cache.aPerpendicularDistance - delta <= minPositivePerpendicularDistance)) ||
				(cache.bDomainDistance > 0 && (cache.bPerpendicularDistance < 0
					? cache.bPerpendicularDistance + delta >= minNegativePerpendicularDistance
					: cache.bPerpendicularDistance - delta <= minPositivePerpendicularDistance));
		}

		public void AddEdgeTrueDistance(EdgeSegment edge, SignedDistance distance, double param) {
			if (distance < MinTrueDistance) {
				MinTrueDistance = distance;
				nearEdge = edge;
				nearEdgeParam = param;
			}
		}

		public void AddEdgePerpendicularDistance(double distance) {
			if (distance <= 0 && distance > minNegativePerpendicularDistance)
				minNegativePerpendicularDistance = distance;
			if (distance >= 0 && distance < minPositivePerpendicularDistance)
				minPositivePerpendicularDistance = distance;
		}

		public void Merge(PerpendicularDistanceSelectorBase other) {
			if (other.MinTrueDistance < MinTrueDistance) {
				MinTrueDistance = other.MinTrueDistance;
				nearEdge = other.nearEdge;
				nearEdgeParam = other.nearEdgeParam;
			}
			if (other.minNegativePerpendicularDistance > minNegativePerpendicularDistance)
				minNegativePerpendicularDistance = other.minNegativePerpendicularDistance;
			if (other.minPositivePerpendicularDistance < minPositivePerpendicularDistance)
				minPositivePerpendicularDistance = other.minPositivePerpendicularDistance;
		}

		public double ComputeDistance(Vector2 p) {
			double minDistance = MinTrueDistance.distance < 0 ? minNegativePerpendicularDistance : minPositivePerpendicularDistance;
			if (nearEdge != null) {
				SignedDistance distance = MinTrueDistance;
				nearEdge.DistanceToPerpendicularDistance(ref distance, p, nearEdgeParam);
				if (Math.Abs(distance.distance) < Math.Abs(minDistance))
					minDistance = distance.distance;
			}
			return minDistance;
		}

		public SignedDistance TrueDistance() => MinTrueDistance;
	}

	// -------------------------------------------------------------------------------------------------

	public sealed class PerpendicularDistanceSelector : IEdgeSelector<PerpendicularDistanceSelector, double, PerpEdgeCache> {

		private readonly PerpendicularDistanceSelectorBase b = new PerpendicularDistanceSelectorBase();
		private Vector2 p;

		public void Reset(Vector2 newP) {
			double delta = SelectorConst.DistanceDeltaFactor * (newP - p).Length;
			b.Reset(delta);
			p = newP;
		}

		public void AddEdge(ref PerpEdgeCache cache, EdgeSegment prevEdge, EdgeSegment edge, EdgeSegment nextEdge) {
			if (!b.IsEdgeRelevant(cache, p)) return;
			SignedDistance distance = edge.SignedDistanceTo(p, out double param);
			b.AddEdgeTrueDistance(edge, distance, param);
			cache.point = p;
			cache.absDistance = Math.Abs(distance.distance);

			Vector2 ap = p - edge.Point(0);
			Vector2 bp = p - edge.Point(1);
			Vector2 aDir = edge.Direction(0).Normalize(true);
			Vector2 bDir = edge.Direction(1).Normalize(true);
			Vector2 prevDir = prevEdge.Direction(1).Normalize(true);
			Vector2 nextDir = nextEdge.Direction(0).Normalize(true);
			double add = DotProduct(ap, (prevDir + aDir).Normalize(true));
			double bdd = -DotProduct(bp, (bDir + nextDir).Normalize(true));
			if (add > 0) {
				double pd = distance.distance;
				if (PerpendicularDistanceSelectorBase.GetPerpendicularDistance(ref pd, ap, -aDir)) {
					pd = -pd;
					b.AddEdgePerpendicularDistance(pd);
				}
				cache.aPerpendicularDistance = pd;
			}
			if (bdd > 0) {
				double pd = distance.distance;
				if (PerpendicularDistanceSelectorBase.GetPerpendicularDistance(ref pd, bp, bDir))
					b.AddEdgePerpendicularDistance(pd);
				cache.bPerpendicularDistance = pd;
			}
			cache.aDomainDistance = add;
			cache.bDomainDistance = bdd;
		}

		public void Merge(PerpendicularDistanceSelector other) => b.Merge(other.b);
		public double Distance() => b.ComputeDistance(p);
		public double InitialDistance() => double.MinValue;
		public double ResolveDistance(double d) => d;
	}

	// -------------------------------------------------------------------------------------------------

	/// <summary>Shared 3-channel perpendicular selector logic for MSDF and MTSDF.</summary>
	internal sealed class MultiDistanceSelectorImpl {

		public Vector2 P;
		public readonly PerpendicularDistanceSelectorBase R = new PerpendicularDistanceSelectorBase();
		public readonly PerpendicularDistanceSelectorBase G = new PerpendicularDistanceSelectorBase();
		public readonly PerpendicularDistanceSelectorBase B = new PerpendicularDistanceSelectorBase();

		public void Reset(Vector2 newP) {
			double delta = SelectorConst.DistanceDeltaFactor * (newP - P).Length;
			R.Reset(delta);
			G.Reset(delta);
			B.Reset(delta);
			P = newP;
		}

		public void AddEdge(ref PerpEdgeCache cache, EdgeSegment prevEdge, EdgeSegment edge, EdgeSegment nextEdge) {
			bool rBit = ((int) edge.Color & (int) EdgeColor.Red) != 0;
			bool gBit = ((int) edge.Color & (int) EdgeColor.Green) != 0;
			bool bBit = ((int) edge.Color & (int) EdgeColor.Blue) != 0;
			if (!((rBit && R.IsEdgeRelevant(cache, P)) ||
				  (gBit && G.IsEdgeRelevant(cache, P)) ||
				  (bBit && B.IsEdgeRelevant(cache, P))))
				return;

			SignedDistance distance = edge.SignedDistanceTo(P, out double param);
			if (rBit) R.AddEdgeTrueDistance(edge, distance, param);
			if (gBit) G.AddEdgeTrueDistance(edge, distance, param);
			if (bBit) B.AddEdgeTrueDistance(edge, distance, param);
			cache.point = P;
			cache.absDistance = Math.Abs(distance.distance);

			Vector2 ap = P - edge.Point(0);
			Vector2 bp = P - edge.Point(1);
			Vector2 aDir = edge.Direction(0).Normalize(true);
			Vector2 bDir = edge.Direction(1).Normalize(true);
			Vector2 prevDir = prevEdge.Direction(1).Normalize(true);
			Vector2 nextDir = nextEdge.Direction(0).Normalize(true);
			double add = DotProduct(ap, (prevDir + aDir).Normalize(true));
			double bdd = -DotProduct(bp, (bDir + nextDir).Normalize(true));
			if (add > 0) {
				double pd = distance.distance;
				if (PerpendicularDistanceSelectorBase.GetPerpendicularDistance(ref pd, ap, -aDir)) {
					pd = -pd;
					if (rBit) R.AddEdgePerpendicularDistance(pd);
					if (gBit) G.AddEdgePerpendicularDistance(pd);
					if (bBit) B.AddEdgePerpendicularDistance(pd);
				}
				cache.aPerpendicularDistance = pd;
			}
			if (bdd > 0) {
				double pd = distance.distance;
				if (PerpendicularDistanceSelectorBase.GetPerpendicularDistance(ref pd, bp, bDir)) {
					if (rBit) R.AddEdgePerpendicularDistance(pd);
					if (gBit) G.AddEdgePerpendicularDistance(pd);
					if (bBit) B.AddEdgePerpendicularDistance(pd);
				}
				cache.bPerpendicularDistance = pd;
			}
			cache.aDomainDistance = add;
			cache.bDomainDistance = bdd;
		}

		public void Merge(MultiDistanceSelectorImpl other) {
			R.Merge(other.R);
			G.Merge(other.G);
			B.Merge(other.B);
		}

		public MultiDistance MultiDist() => new MultiDistance {
			r = R.ComputeDistance(P),
			g = G.ComputeDistance(P),
			b = B.ComputeDistance(P)
		};

		public SignedDistance TrueDist() {
			SignedDistance d = R.TrueDistance();
			if (G.TrueDistance() < d) d = G.TrueDistance();
			if (B.TrueDistance() < d) d = B.TrueDistance();
			return d;
		}
	}

	public sealed class MultiDistanceSelector : IEdgeSelector<MultiDistanceSelector, MultiDistance, PerpEdgeCache> {
		internal readonly MultiDistanceSelectorImpl impl = new MultiDistanceSelectorImpl();
		public void Reset(Vector2 p) => impl.Reset(p);
		public void AddEdge(ref PerpEdgeCache cache, EdgeSegment prevEdge, EdgeSegment edge, EdgeSegment nextEdge) =>
			impl.AddEdge(ref cache, prevEdge, edge, nextEdge);
		public void Merge(MultiDistanceSelector other) => impl.Merge(other.impl);
		public MultiDistance Distance() => impl.MultiDist();
		public SignedDistance TrueDistance() => impl.TrueDist();
		public MultiDistance InitialDistance() => new MultiDistance { r = double.MinValue, g = double.MinValue, b = double.MinValue };
		public double ResolveDistance(MultiDistance d) => Median(d.r, d.g, d.b);
	}

	public sealed class MultiAndTrueDistanceSelector : IEdgeSelector<MultiAndTrueDistanceSelector, MultiAndTrueDistance, PerpEdgeCache> {
		internal readonly MultiDistanceSelectorImpl impl = new MultiDistanceSelectorImpl();
		public void Reset(Vector2 p) => impl.Reset(p);
		public void AddEdge(ref PerpEdgeCache cache, EdgeSegment prevEdge, EdgeSegment edge, EdgeSegment nextEdge) =>
			impl.AddEdge(ref cache, prevEdge, edge, nextEdge);
		public void Merge(MultiAndTrueDistanceSelector other) => impl.Merge(other.impl);
		public MultiAndTrueDistance Distance() {
			MultiDistance md = impl.MultiDist();
			return new MultiAndTrueDistance { r = md.r, g = md.g, b = md.b, a = impl.TrueDist().distance };
		}
		public MultiAndTrueDistance InitialDistance() =>
			new MultiAndTrueDistance { r = double.MinValue, g = double.MinValue, b = double.MinValue, a = double.MinValue };
		public double ResolveDistance(MultiAndTrueDistance d) => Median(d.r, d.g, d.b);
	}
}
