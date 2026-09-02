using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sperlich.Text {

	/// <summary>
	/// Blittable per-vertex record. Chosen as a native struct from phase 1 (not a later refactor)
	/// so the Burst effect jobs and a possible GPU tier can write it directly.
	/// Layout must stay byte-for-byte compatible with <see cref="TextVertex.Layout"/>.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct TextVertex {

		/// <summary>Local space position (z is usually 0, used by depth-based effects).</summary>
		public float3 position;

		/// <summary>Local space normal. Kept for lit UI shaders / bevel effects.</summary>
		public float3 normal;

		/// <summary>Tangent. xyz tangent, w sign. Used by the SDF shader to carry per-vertex scale in w when needed.</summary>
		public float4 tangent;

		/// <summary>Linear RGBA vertex colour (0..1).</summary>
		public float4 color;

		/// <summary>xy: atlas UV. z: signed distance texel scale. w: synthetic weight bias (faux bold/light).</summary>
		public float4 uv0;

		/// <summary>Free channel for effects / gradient params / mask coordinates.</summary>
		public float4 uv1;

		/// <summary>Atlas glyph bounds rect (u0, v0, u1, v1) for shader clamping.</summary>
		public float4 uv2;

		/// <summary>Vertex buffer descriptor matching this struct exactly (26 floats).</summary>
		public static readonly VertexAttributeDescriptor[] Layout = {
			new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
			new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
			new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
			new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
			new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
			new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
			new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4)
		};
	}
}
