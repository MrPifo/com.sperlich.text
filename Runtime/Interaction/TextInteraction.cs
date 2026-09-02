using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sperlich.Text {

	/// <summary>A resolved, hit-testable clickable region for one <c>&lt;link&gt;</c> span.</summary>
	public struct LinkHitbox {
		public string Id;
		public int Start;
		public int Length;
		public Rect Bounds;          // local text space, union of the link's glyph quads
		public List<Rect> LineRects; // per-line rects for multi-line links (optional, may be null)
	}

	/// <summary>
	/// Optional component (plan module 11). Bolt onto the same GameObject as a <see cref="SText"/>.
	/// Does bounds-based hit testing against link spans (no physics raycast) and raises hover / click events.
	/// </summary>
	[RequireComponent(typeof(SText))]
	public sealed class TextInteraction : MonoBehaviour, IPointerMoveHandler, IPointerClickHandler, IPointerExitHandler {

		[Serializable] public class LinkEvent : UnityEngine.Events.UnityEvent<string> { }

		public LinkEvent onLinkClick = new();
		public LinkEvent onLinkEnter = new();
		public LinkEvent onLinkExit = new();

		private SText text;
		private readonly List<LinkHitbox> hitboxes = new();
		private string hovered;

		private void Awake() {
			text = GetComponent<SText>();
		}

		private void OnEnable() {
			if (text != null) text.LayoutChanged += Rebuild;
			Rebuild();
		}

		private void OnDisable() {
			if (text != null) text.LayoutChanged -= Rebuild;
		}

		/// <summary>Recomputes link hitboxes from the current layout. Called automatically on layout change.</summary>
		public void Rebuild() {
			hitboxes.Clear();
			if (text == null) return;
			text.CollectLinkHitboxes(hitboxes);
		}

		public bool TryGetLinkAt(Vector2 localPoint, out string id) {
			for (int i = 0; i < hitboxes.Count; i++) {
				LinkHitbox h = hitboxes[i];
				if (h.LineRects != null) {
					for (int r = 0; r < h.LineRects.Count; r++) {
						if (h.LineRects[r].Contains(localPoint)) { id = h.Id; return true; }
					}
				} else if (h.Bounds.Contains(localPoint)) {
					id = h.Id;
					return true;
				}
			}
			id = null;
			return false;
		}

		public void OnPointerMove(PointerEventData eventData) {
			if (text == null) return;
			Vector2 local = text.ScreenToTextLocal(eventData.position, eventData.pressEventCamera);
			bool hit = TryGetLinkAt(local, out string id);

			if (hit && id != hovered) {
				if (hovered != null) onLinkExit.Invoke(hovered);
				hovered = id;
				onLinkEnter.Invoke(id);
			} else if (!hit && hovered != null) {
				onLinkExit.Invoke(hovered);
				hovered = null;
			}
		}

		public void OnPointerExit(PointerEventData eventData) {
			if (hovered != null) {
				onLinkExit.Invoke(hovered);
				hovered = null;
			}
		}

		public void OnPointerClick(PointerEventData eventData) {
			if (text == null) return;
			Vector2 local = text.ScreenToTextLocal(eventData.position, eventData.pressEventCamera);
			if (TryGetLinkAt(local, out string id)) onLinkClick.Invoke(id);
		}
	}
}
