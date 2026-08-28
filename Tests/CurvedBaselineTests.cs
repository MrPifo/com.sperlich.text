using NUnit.Framework;
using UnityEngine;
using Sperlich.Text;

namespace Sperlich.Text.Tests {

	public class CurvedBaselineTests {

		[Test]
		public void InvalidWithFewerThanTwoPoints() {
			CurvedBaseline c = new CurvedBaseline();
			c.SetWaypoints(new[] { new Vector2(0, 0) });
			Assert.IsFalse(c.IsValid);
		}

		[Test]
		public void LengthIsSumOfSegments() {
			CurvedBaseline c = new CurvedBaseline();
			c.SetWaypoints(new[] { new Vector2(0, 0), new Vector2(3, 0), new Vector2(3, 4) });
			Assert.AreEqual(7f, c.Length, 1e-3f);
		}

		[Test]
		public void EvaluateMidpointOfStraightLine() {
			CurvedBaseline c = new CurvedBaseline();
			c.SetWaypoints(new[] { new Vector2(0, 0), new Vector2(10, 0) });
			c.Evaluate(5f, out Unity.Mathematics.float2 pos, out float angle);
			Assert.AreEqual(5f, pos.x, 1e-3f);
			Assert.AreEqual(0f, pos.y, 1e-3f);
			Assert.AreEqual(0f, angle, 1e-3f);
		}

		[Test]
		public void EvaluateClampsBeyondEnds() {
			CurvedBaseline c = new CurvedBaseline();
			c.SetWaypoints(new[] { new Vector2(0, 0), new Vector2(10, 0) });
			c.Evaluate(999f, out Unity.Mathematics.float2 pos, out _);
			Assert.AreEqual(10f, pos.x, 1e-3f);
		}

		[Test]
		public void TangentTracksDirectionChange() {
			CurvedBaseline c = new CurvedBaseline();
			c.SetWaypoints(new[] { new Vector2(0, 0), new Vector2(0, 10) });
			c.Evaluate(5f, out _, out float angle);
			Assert.AreEqual(Mathf.PI / 2f, angle, 1e-3f);
		}
	}
}
