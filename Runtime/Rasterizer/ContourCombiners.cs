// C# port of msdfgen/core/contour-combiners.cpp / .h — https://github.com/Chlumsky/msdfgen (MIT).
using System;
using System.Collections.Generic;

namespace Sperlich.Text.Rasterizer {

	public interface IContourCombiner<TSel, TDist, TCache>
		where TSel : class, IEdgeSelector<TSel, TDist, TCache>, new()
		where TCache : struct {
		void Reset(Vector2 p);
		TSel EdgeSelector(int i);
		TDist Distance();
	}

	/// <summary>Simply selects the nearest contour.</summary>
	public sealed class SimpleContourCombiner<TSel, TDist, TCache> : IContourCombiner<TSel, TDist, TCache>
		where TSel : class, IEdgeSelector<TSel, TDist, TCache>, new()
		where TCache : struct {

		private readonly TSel shapeEdgeSelector = new TSel();

		public SimpleContourCombiner(Shape shape) { }
		public void Reset(Vector2 p) => shapeEdgeSelector.Reset(p);
		public TSel EdgeSelector(int i) => shapeEdgeSelector;
		public TDist Distance() => shapeEdgeSelector.Distance();
	}

	/// <summary>Selects the nearest contour that actually forms a fill border (handles overlap).</summary>
	public sealed class OverlappingContourCombiner<TSel, TDist, TCache> : IContourCombiner<TSel, TDist, TCache>
		where TSel : class, IEdgeSelector<TSel, TDist, TCache>, new()
		where TCache : struct {

		private Vector2 p;
		private readonly int[] windings;
		private readonly TSel[] edgeSelectors;
		private readonly TSel shapeEdgeSelector = new TSel();
		private readonly TSel innerEdgeSelector = new TSel();
		private readonly TSel outerEdgeSelector = new TSel();

		public OverlappingContourCombiner(Shape shape) {
			int n = shape.Contours.Count;
			windings = new int[n];
			edgeSelectors = new TSel[n];
			for (int i = 0; i < n; i++) {
				windings[i] = shape.Contours[i].Winding();
				edgeSelectors[i] = new TSel();
			}
		}

		public void Reset(Vector2 newP) {
			p = newP;
			for (int i = 0; i < edgeSelectors.Length; i++)
				edgeSelectors[i].Reset(newP);
		}

		public TSel EdgeSelector(int i) => edgeSelectors[i];

		public TDist Distance() {
			int contourCount = edgeSelectors.Length;
			shapeEdgeSelector.Clear();
			innerEdgeSelector.Clear();
			outerEdgeSelector.Clear();
			shapeEdgeSelector.Reset(p);
			innerEdgeSelector.Reset(p);
			outerEdgeSelector.Reset(p);

			double Resolve(TDist d) => shapeEdgeSelector.ResolveDistance(d);

			for (int i = 0; i < contourCount; ++i) {
				TDist edgeDistance = edgeSelectors[i].Distance();
				shapeEdgeSelector.Merge(edgeSelectors[i]);
				if (windings[i] > 0 && Resolve(edgeDistance) >= 0)
					innerEdgeSelector.Merge(edgeSelectors[i]);
				if (windings[i] < 0 && Resolve(edgeDistance) <= 0)
					outerEdgeSelector.Merge(edgeSelectors[i]);
			}

			TDist shapeDistance = shapeEdgeSelector.Distance();
			TDist innerDistance = innerEdgeSelector.Distance();
			TDist outerDistance = outerEdgeSelector.Distance();
			double innerScalarDistance = Resolve(innerDistance);
			double outerScalarDistance = Resolve(outerDistance);
			TDist distance = shapeEdgeSelector.InitialDistance();

			int winding = 0;
			if (innerScalarDistance >= 0 && Math.Abs(innerScalarDistance) <= Math.Abs(outerScalarDistance)) {
				distance = innerDistance;
				winding = 1;
				for (int i = 0; i < contourCount; ++i)
					if (windings[i] > 0) {
						TDist contourDistance = edgeSelectors[i].Distance();
						if (Math.Abs(Resolve(contourDistance)) < Math.Abs(outerScalarDistance) &&
							Resolve(contourDistance) > Resolve(distance))
							distance = contourDistance;
					}
			} else if (outerScalarDistance <= 0 && Math.Abs(outerScalarDistance) < Math.Abs(innerScalarDistance)) {
				distance = outerDistance;
				winding = -1;
				for (int i = 0; i < contourCount; ++i)
					if (windings[i] < 0) {
						TDist contourDistance = edgeSelectors[i].Distance();
						if (Math.Abs(Resolve(contourDistance)) < Math.Abs(innerScalarDistance) &&
							Resolve(contourDistance) < Resolve(distance))
							distance = contourDistance;
					}
			} else {
				return shapeDistance;
			}

			for (int i = 0; i < contourCount; ++i)
				if (windings[i] != winding) {
					TDist contourDistance = edgeSelectors[i].Distance();
					if (Resolve(contourDistance) * Resolve(distance) >= 0 &&
						Math.Abs(Resolve(contourDistance)) < Math.Abs(Resolve(distance)))
						distance = contourDistance;
				}
			if (Resolve(distance) == Resolve(shapeDistance))
				distance = shapeDistance;
			return distance;
		}
	}
}
