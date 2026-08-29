// C# port of msdfgen/core/ShapeDistanceFinder.hpp / .h — https://github.com/Chlumsky/msdfgen (MIT).

namespace Sperlich.Text.Rasterizer {

	/// <summary>
	/// Distance between a point and a <see cref="Shape"/>. The contour combiner dictates the metric
	/// and its data type. Not thread-safe; fastest when consecutive queries are close together.
	/// </summary>
	public sealed class ShapeDistanceFinder<TCombiner, TSel, TDist, TCache>
		where TCombiner : IContourCombiner<TSel, TDist, TCache>
		where TSel : class, IEdgeSelector<TSel, TDist, TCache>, new()
		where TCache : struct {

		private readonly Shape shape;
		private readonly TCombiner contourCombiner;
		private readonly TCache[] shapeEdgeCache;

		public ShapeDistanceFinder(Shape shape, TCombiner contourCombiner) {
			this.shape = shape;
			this.contourCombiner = contourCombiner;
			shapeEdgeCache = new TCache[shape.EdgeCount()];
		}

		public TDist Distance(Vector2 origin) {
			contourCombiner.Reset(origin);
			int cacheIndex = 0;

			for (int ci = 0; ci < shape.Contours.Count; ci++) {
				Contour contour = shape.Contours[ci];
				int n = contour.Edges.Count;
				if (n == 0) continue;

				TSel edgeSelector = contourCombiner.EdgeSelector(ci);
				EdgeSegment prevEdge = n >= 2 ? contour.Edges[n - 2] : contour.Edges[0];
				EdgeSegment curEdge = contour.Edges[n - 1];
				for (int ei = 0; ei < n; ei++) {
					EdgeSegment nextEdge = contour.Edges[ei];
					edgeSelector.AddEdge(ref shapeEdgeCache[cacheIndex++], prevEdge, curEdge, nextEdge);
					prevEdge = curEdge;
					curEdge = nextEdge;
				}
			}

			return contourCombiner.Distance();
		}
	}
}
