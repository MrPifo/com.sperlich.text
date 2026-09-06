using System.Collections.Generic;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Shares one runtime <see cref="Material"/> between every <see cref="SText"/> that has the exact same
	/// shader, MTSDF state, sprite atlas, and component-level style config (dilate/sharpness/outline/shadow/
	/// glow/bloom). This is the same model TextMeshPro uses: labels with identical settings batch into one
	/// uGUI draw call (same material reference + same texture + same clip context); labels with differing
	/// settings simply don't share a material — and therefore don't batch with each other, exactly like TMP.
	/// Ref-counted, same lifecycle shape as <see cref="GlyphStoreRegistry"/>.
	/// </summary>
	public static class SharedMaterialRegistry {

		public struct Key : System.IEquatable<Key> {
			public Shader Shader;
			public bool Mtsdf;
			public Texture2D SpriteAtlas;
			public float FaceDilate, Sharpness;
			public Color OutlineColor;
			public float OutlineWidth;
			public int OutlineMode;
			public Color ShadowColor;
			public Vector2 ShadowOffset;
			public float ShadowSoftness, ShadowDilate;
			public int ShadowTaps;
			public Color GlowColor;
			public float GlowPower, GlowOuter;
			public int GlowTaps;
			public float BloomFalloff;
			public int BloomTaps;

			public bool Equals(Key o) =>
				Shader == o.Shader && Mtsdf == o.Mtsdf && SpriteAtlas == o.SpriteAtlas &&
				FaceDilate.Equals(o.FaceDilate) && Sharpness.Equals(o.Sharpness) &&
				OutlineColor.Equals(o.OutlineColor) && OutlineWidth.Equals(o.OutlineWidth) && OutlineMode == o.OutlineMode &&
				ShadowColor.Equals(o.ShadowColor) && ShadowOffset.Equals(o.ShadowOffset) &&
				ShadowSoftness.Equals(o.ShadowSoftness) && ShadowDilate.Equals(o.ShadowDilate) && ShadowTaps == o.ShadowTaps &&
				GlowColor.Equals(o.GlowColor) && GlowPower.Equals(o.GlowPower) && GlowOuter.Equals(o.GlowOuter) && GlowTaps == o.GlowTaps &&
				BloomFalloff.Equals(o.BloomFalloff) && BloomTaps == o.BloomTaps;

			public override bool Equals(object obj) => obj is Key k && Equals(k);

			public override int GetHashCode() {
				unchecked {
					int h = Shader != null ? Shader.GetHashCode() : 0;
					h = h * 397 ^ Mtsdf.GetHashCode();
					h = h * 397 ^ (SpriteAtlas != null ? SpriteAtlas.GetHashCode() : 0);
					h = h * 397 ^ FaceDilate.GetHashCode();
					h = h * 397 ^ Sharpness.GetHashCode();
					h = h * 397 ^ OutlineColor.GetHashCode();
					h = h * 397 ^ OutlineWidth.GetHashCode();
					h = h * 397 ^ OutlineMode;
					h = h * 397 ^ ShadowColor.GetHashCode();
					h = h * 397 ^ ShadowOffset.GetHashCode();
					h = h * 397 ^ ShadowSoftness.GetHashCode();
					h = h * 397 ^ ShadowDilate.GetHashCode();
					h = h * 397 ^ ShadowTaps;
					h = h * 397 ^ GlowColor.GetHashCode();
					h = h * 397 ^ GlowPower.GetHashCode();
					h = h * 397 ^ GlowOuter.GetHashCode();
					h = h * 397 ^ GlowTaps;
					h = h * 397 ^ BloomFalloff.GetHashCode();
					h = h * 397 ^ BloomTaps;
					return h;
				}
			}
		}

		private struct Entry {
			public Material Material;
			public int RefCount;
		}

		private static readonly Dictionary<Key, Entry> materials = new();

		public static Material Acquire(in Key key) {
			if (key.Shader == null) return null;
			if (materials.TryGetValue(key, out Entry e) && e.Material != null) {
				e.RefCount++;
				materials[key] = e;
				return e.Material;
			}

			var mat = new Material(key.Shader) { name = "SperlichText (shared)", hideFlags = HideFlags.DontSave };
			ApplyKey(mat, key);
			materials[key] = new Entry { Material = mat, RefCount = 1 };
			return mat;
		}

		public static void Release(in Key key) {
			if (!materials.TryGetValue(key, out Entry e)) return;
			e.RefCount--;
			if (e.RefCount <= 0) {
				DestroySafe(e.Material);
				materials.Remove(key);
			} else {
				materials[key] = e;
			}
		}

		private static void ApplyKey(Material mat, in Key key) {
			mat.SetFloat("_FaceDilate", key.FaceDilate);
			mat.SetFloat("_Sharpness", key.Sharpness);
			mat.SetColor("_OutlineColor", key.OutlineColor);
			mat.SetFloat("_OutlineWidth", key.OutlineWidth);
			mat.SetFloat("_OutlineMode", key.OutlineMode);
			mat.SetColor("_UnderlayColor", key.ShadowColor);
			mat.SetVector("_UnderlayOffset", new Vector4(key.ShadowOffset.x, key.ShadowOffset.y, Mathf.Max(0.0001f, key.ShadowSoftness), 0f));
			mat.SetFloat("_UnderlayDilate", key.ShadowDilate);
			mat.SetFloat("_ShadowTaps", key.ShadowTaps);
			mat.SetColor("_GlowColor", key.GlowColor);
			mat.SetFloat("_GlowPower", key.GlowPower);
			mat.SetFloat("_GlowOuter", key.GlowOuter);
			mat.SetFloat("_GlowTaps", key.GlowTaps);
			mat.SetFloat("_BloomFalloff", key.BloomFalloff);
			mat.SetFloat("_BloomTaps", key.BloomTaps);
			if (key.SpriteAtlas != null) mat.SetTexture("_SpriteTex", key.SpriteAtlas);
			if (key.Mtsdf) mat.EnableKeyword("SPERLICH_MTSDF");
			else mat.DisableKeyword("SPERLICH_MTSDF");
		}

		private static void DestroySafe(Object o) {
			if (o == null) return;
			if (Application.isPlaying) Object.Destroy(o);
			else Object.DestroyImmediate(o);
		}
	}
}
