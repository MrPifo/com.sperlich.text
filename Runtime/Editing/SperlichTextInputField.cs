using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sperlich.Text {

	/// <summary>
	/// Minimal single/multi-line editable field on top of <see cref="SperlichText"/> (plan module 12).
	/// Caret + selection rendering via <see cref="SperlichText.SetEditingRects"/>, keyboard through the
	/// EventSystem, clipboard via <see cref="GUIUtility.systemCopyBuffer"/>. No IME / composition.
	/// </summary>
	[RequireComponent(typeof(SperlichText))]
	public sealed class SperlichTextInputField : MonoBehaviour,
		ISelectHandler, IDeselectHandler, IUpdateSelectedHandler, IPointerClickHandler {

		[Serializable] public class StringEvent : UnityEngine.Events.UnityEvent<string> { }

		[TextArea(1, 4)] public string value = string.Empty;
		public bool multiline = false;
		public int characterLimit = 0;
		public Color caretColor = new Color(1f, 1f, 1f, 0.9f);
		public Color selectionColor = new Color(0.25f, 0.5f, 1f, 0.35f);
		public float caretBlinkRate = 1.4f;
		public float caretWidth = 2f;

		public StringEvent onValueChanged = new();
		public StringEvent onSubmit = new();

		private SperlichText text;
		private int caret;
		private int anchor;
		private bool focused;
		private readonly List<Rect> rects = new();

		public bool IsFocused => focused;
		public int CaretIndex => caret;

		private void Awake() {
			text = GetComponent<SperlichText>();
		}

		private void OnEnable() {
			PushText();
			if (text != null) text.LayoutChanged += RefreshDecorations;
		}

		private void OnDisable() {
			if (text != null) text.LayoutChanged -= RefreshDecorations;
			text?.SetEditingRects(null);
		}

		private void Update() {
			if (focused) RefreshDecorations();
		}

		// -- EventSystem ------------------------------------------------------------------------

		public void OnSelect(BaseEventData eventData) {
			focused = true;
			caret = value.Length;
			anchor = caret;
			RefreshDecorations();
		}

		public void OnDeselect(BaseEventData eventData) {
			focused = false;
			text?.SetEditingRects(null);
		}

		public void OnPointerClick(PointerEventData eventData) {
			if (!focused) {
				EventSystem.current?.SetSelectedGameObject(gameObject);
				focused = true;
			}
			Vector2 local = text.ScreenToTextLocal(eventData.position, eventData.pressEventCamera);
			caret = CaretIndexFromLocal(local);
			if (!eventData.dragging) anchor = caret;
			RefreshDecorations();
		}

		public void OnUpdateSelected(BaseEventData eventData) {
			if (!focused) return;
			Event e = new Event();
			bool changed = false;
			while (Event.PopEvent(e)) {
				if (e.rawType != EventType.KeyDown) continue;
				changed |= HandleKey(e);
			}
			if (changed) {
				ClampCaret();
				PushText();
			}
			RefreshDecorations();
			eventData.Use();
		}

		// -- editing model -------------------------------------------------------------------

		private bool HandleKey(Event e) {
			bool ctrl = (e.modifiers & (EventModifiers.Control | EventModifiers.Command)) != 0;
			bool shift = (e.modifiers & EventModifiers.Shift) != 0;

			switch (e.keyCode) {
				case KeyCode.LeftArrow: Move(-1, shift); return false;
				case KeyCode.RightArrow: Move(1, shift); return false;
				case KeyCode.Home: SetCaret(LineStart(caret), shift); return false;
				case KeyCode.End: SetCaret(LineEnd(caret), shift); return false;
				case KeyCode.UpArrow: SetCaret(VerticalCaret(-1), shift); return false;
				case KeyCode.DownArrow: SetCaret(VerticalCaret(1), shift); return false;
				case KeyCode.Backspace:
					if (HasSelection()) { DeleteSelection(); return true; }
					if (caret > 0) { value = value.Remove(caret - 1, 1); caret--; anchor = caret; return true; }
					return false;
				case KeyCode.Delete:
					if (HasSelection()) { DeleteSelection(); return true; }
					if (caret < value.Length) { value = value.Remove(caret, 1); return true; }
					return false;
				case KeyCode.Return:
				case KeyCode.KeypadEnter:
					if (multiline && !ctrl) { InsertText("\n"); return true; }
					onSubmit.Invoke(value);
					return false;
			}

			if (ctrl) {
				switch (e.keyCode) {
					case KeyCode.A: anchor = 0; caret = value.Length; return false;
					case KeyCode.C: if (HasSelection()) GUIUtility.systemCopyBuffer = SelectedText(); return false;
					case KeyCode.X:
						if (HasSelection()) { GUIUtility.systemCopyBuffer = SelectedText(); DeleteSelection(); return true; }
						return false;
					case KeyCode.V:
						InsertText(SanitizePaste(GUIUtility.systemCopyBuffer));
						return true;
				}
				return false;
			}

			if (e.character != '\0' && !char.IsControl(e.character)) {
				InsertText(e.character.ToString());
				return true;
			}
			return false;
		}

		private void InsertText(string s) {
			if (string.IsNullOrEmpty(s)) return;
			if (HasSelection()) DeleteSelection();
			if (characterLimit > 0 && value.Length + s.Length > characterLimit) {
				int room = characterLimit - value.Length;
				if (room <= 0) return;
				s = s.Substring(0, room);
			}
			value = value.Insert(caret, s);
			caret += s.Length;
			anchor = caret;
		}

		private string SanitizePaste(string s) {
			if (string.IsNullOrEmpty(s)) return string.Empty;
			s = s.Replace("\r\n", "\n").Replace('\r', '\n');
			if (!multiline) s = s.Replace("\n", " ");
			return s;
		}

		private bool HasSelection() => caret != anchor;
		private int SelMin => Mathf.Min(caret, anchor);
		private int SelMax => Mathf.Max(caret, anchor);
		private string SelectedText() => value.Substring(SelMin, SelMax - SelMin);

		private void DeleteSelection() {
			int a = SelMin, b = SelMax;
			value = value.Remove(a, b - a);
			caret = a;
			anchor = a;
		}

		private void Move(int dir, bool shift) {
			if (HasSelection() && !shift) { caret = dir < 0 ? SelMin : SelMax; anchor = caret; return; }
			caret = Mathf.Clamp(caret + dir, 0, value.Length);
			if (!shift) anchor = caret;
		}

		private void SetCaret(int index, bool shift) {
			caret = Mathf.Clamp(index, 0, value.Length);
			if (!shift) anchor = caret;
		}

		private void ClampCaret() {
			caret = Mathf.Clamp(caret, 0, value.Length);
			anchor = Mathf.Clamp(anchor, 0, value.Length);
		}

		private int LineStart(int index) {
			int i = Mathf.Clamp(index, 0, value.Length) - 1;
			while (i >= 0 && value[i] != '\n') i--;
			return i + 1;
		}

		private int LineEnd(int index) {
			int i = Mathf.Clamp(index, 0, value.Length);
			while (i < value.Length && value[i] != '\n') i++;
			return i;
		}

		private int VerticalCaret(int dir) {
			LayoutResult layout = text != null ? text.CurrentLayout : null;
			if (layout == null || layout.Glyphs.Count == 0) return caret;

			int gi = GlyphIndexForCaret(caret, out bool after);
			if (gi < 0) return caret;
			PositionedGlyph g = layout.Glyphs[Mathf.Clamp(gi, 0, layout.Glyphs.Count - 1)];
			int targetLine = Mathf.Clamp(g.LineIndex + dir, 0, layout.Lines.Count - 1);
			if (targetLine == g.LineIndex) return caret;

			float targetX = g.Pen.x + (after ? g.Glyph.Advance * g.UnitScale : 0f);
			LineInfo line = layout.Lines[targetLine];
			int best = caret;
			float bestDist = float.MaxValue;
			for (int i = line.FirstGlyph; i < line.FirstGlyph + line.GlyphCount; i++) {
				PositionedGlyph p = layout.Glyphs[i];
				if (p.SourceIndex < 0) continue;
				float d = Mathf.Abs(p.Pen.x - targetX);
				if (d < bestDist) { bestDist = d; best = p.SourceIndex; }
			}
			return best;
		}

		// -- caret / selection geometry -------------------------------------------------------

		private void RefreshDecorations() {
			if (text == null || !focused) return;
			LayoutResult layout = text.CurrentLayout;
			rects.Clear();
			if (layout == null) { text.SetEditingRects(rects); return; }

			BuildSelectionRects(layout, rects);

			bool blinkOn = caretBlinkRate <= 0f || Mathf.Repeat(SperlichTextClock.Time * caretBlinkRate, 1f) < 0.5f;
			if (blinkOn) {
				Rect c = CaretRect(layout);
				if (c.width > 0f) rects.Add(c);
			}
			text.SetEditingRects(rects);
		}

		private void BuildSelectionRects(LayoutResult layout, List<Rect> output) {
			if (!HasSelection()) return;
			int a = SelMin, b = SelMax;
			Dictionary<int, Rect> perLine = new();

			for (int i = 0; i < layout.Glyphs.Count; i++) {
				PositionedGlyph g = layout.Glyphs[i];
				if (g.SourceIndex < a || g.SourceIndex >= b) continue;
				LineInfo line = layout.Lines[g.LineIndex];
				float x0 = g.Pen.x;
				float x1 = g.Pen.x + g.Glyph.Advance * g.UnitScale;
				float top = g.Pen.y + line.Ascent;
				float bot = g.Pen.y - line.Descent;
				Rect r = Rect.MinMaxRect(x0, bot, x1, top);
				perLine[g.LineIndex] = perLine.TryGetValue(g.LineIndex, out Rect ex)
					? Rect.MinMaxRect(Mathf.Min(ex.xMin, r.xMin), Mathf.Min(ex.yMin, r.yMin), Mathf.Max(ex.xMax, r.xMax), Mathf.Max(ex.yMax, r.yMax))
					: r;
			}
			foreach (Rect r in perLine.Values) output.Add(r);
		}

		private Rect CaretRect(LayoutResult layout) {
			if (layout.Lines.Count == 0) return new Rect(0f, 0f, caretWidth, text.FontSize);

			int gi = GlyphIndexForCaret(caret, out bool after);
			if (gi < 0) {
				LineInfo l0 = layout.Lines[0];
				return new Rect(0f, -l0.Descent, caretWidth, l0.Ascent + l0.Descent);
			}

			PositionedGlyph g = layout.Glyphs[Mathf.Clamp(gi, 0, layout.Glyphs.Count - 1)];
			LineInfo line = layout.Lines[g.LineIndex];
			float x = g.Pen.x + (after ? g.Glyph.Advance * g.UnitScale : 0f);
			float bot = g.Pen.y - line.Descent;
			return new Rect(x, bot, caretWidth, line.Ascent + line.Descent);
		}

		private int GlyphIndexForCaret(int caretIndex, out bool after) {
			after = false;
			LayoutResult layout = text.CurrentLayout;
			if (layout == null || layout.Glyphs.Count == 0) return -1;

			if (caretIndex <= 0) return 0;
			for (int i = 0; i < layout.Glyphs.Count; i++) {
				if (layout.Glyphs[i].SourceIndex == caretIndex) return i;
			}
			// caret past the last char: anchor to the last real glyph, on its trailing edge
			for (int i = layout.Glyphs.Count - 1; i >= 0; i--) {
				if (layout.Glyphs[i].SourceIndex >= 0 && layout.Glyphs[i].SourceIndex < caretIndex) {
					after = true;
					return i;
				}
			}
			return 0;
		}

		private int CaretIndexFromLocal(Vector2 local) {
			LayoutResult layout = text.CurrentLayout;
			if (layout == null || layout.Glyphs.Count == 0) return 0;

			int best = 0;
			float bestDist = float.MaxValue;
			for (int i = 0; i < layout.Glyphs.Count; i++) {
				PositionedGlyph g = layout.Glyphs[i];
				if (g.SourceIndex < 0) continue;
				float cx = g.Pen.x + g.Glyph.Advance * g.UnitScale * 0.5f;
				float cy = g.Pen.y;
				float d = (cx - local.x) * (cx - local.x) + (cy - local.y) * (cy - local.y);
				if (d < bestDist) {
					bestDist = d;
					best = local.x > g.Pen.x + g.Glyph.Advance * g.UnitScale * 0.5f ? g.SourceIndex + 1 : g.SourceIndex;
				}
			}
			return Mathf.Clamp(best, 0, value.Length);
		}

		private void PushText() {
			if (text != null) text.SetText(value);
			onValueChanged.Invoke(value);
		}
	}
}
