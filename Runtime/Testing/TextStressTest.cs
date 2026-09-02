using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Rendering backend to benchmark.
	/// </summary>
	public enum BenchmarkBackend {
		/// <summary>Custom Sperlich.Text pipeline (SText).</summary>
		SperlichText = 0,
		/// <summary>Unity TextMeshPro (TextMeshProUGUI).</summary>
		TextMeshPro = 1
	}

	/// <summary>
	/// Configurable stress-testing and benchmarking tool for <see cref="SText"/> and <see cref="TextMeshProUGUI"/>.
	/// Spawns animated floating damage numbers with customizable visual features, separate backend object pools, and zero runtime GC allocs.
	/// </summary>
	[AddComponentMenu("Sperlich UI/Testing/Text Stress Test")]
	public class TextStressTest : MonoBehaviour {

		[Header("Backend Selection")]
		[SerializeField] private BenchmarkBackend m_backend = BenchmarkBackend.SperlichText;

		/// <summary>Font definition to use for Sperlich.Text.</summary>
		[Header("Font & Spawn Settings")]
		[SerializeField] private FontDefinition m_font;
		/// <summary>Font asset to use for TextMeshPro.</summary>
		[SerializeField] private TMP_FontAsset m_tmpFont;
		[SerializeField] private float m_fontSize = 28f;
		[SerializeField, Range(10, 5000)] private int m_targetCount = 300;
		[SerializeField] private float m_lifetime = 1.2f;
		[SerializeField] private Vector2 m_spawnArea = new Vector2(700f, 450f);
		[SerializeField] private float m_floatSpeed = 90f;
		[SerializeField] private bool m_randomRichText = false;
		[SerializeField] private bool m_continuouslyUpdateText = false;

		[Header("Drop Shadow")]
		[SerializeField] private bool m_enableShadow = true;
		[SerializeField] private BlurQuality m_shadowQuality = BlurQuality.Low;
		[SerializeField] private Color m_shadowColor = new Color(0f, 0f, 0f, 0.75f);
		[SerializeField] private Vector2 m_shadowOffset = new Vector2(2f, -2f);
		[SerializeField, Range(0f, 10f)] private float m_shadowSoftness = 3f;
		[SerializeField, Range(0f, 1f)] private float m_shadowDilate = 0f;

		[Header("Outline")]
		[SerializeField] private bool m_enableOutline = true;
		[SerializeField] private Color m_outlineColor = Color.black;
		[SerializeField, Range(0f, 16f)] private float m_outlineWidth = 2f;
		[SerializeField] private TextOutlinePlacement m_outlineMode = TextOutlinePlacement.Outer;

		[Header("Glow & Bloom (Sperlich.Text Only)")]
		[SerializeField] private bool m_enableGlow = false;
		[SerializeField] private Color m_glowColor = new Color(1f, 0.4f, 0.1f, 1f);
		[SerializeField, Range(0f, 1f)] private float m_glowPower = 0.5f;
		[SerializeField] private bool m_enableBloom = false;
		[SerializeField] private Color m_bloomColor = new Color(1f, 0.8f, 0.2f, 1f);

		[Header("Built-in Burst Effects (Sperlich.Text Only)")]
		[SerializeField] private bool m_enableBuiltinEffects = false;
		[SerializeField] private BuiltinEffect m_builtinEffect = BuiltinEffect.Wave;

		[Header("HUD Overlay")]
		[SerializeField] private bool m_showHUD = true;

		private struct DamageItem {
			public BenchmarkBackend Backend;
			public GameObject GameObject;
			public RectTransform Rect;
			public SText STextComp;
			public TextMeshProUGUI TmpComp;
			public Vector2 StartPos;
			public float Elapsed;
			public float MaxLife;
			public Color BaseColor;
			public bool IsCritical;
			public string TextContent;
		}

		private static readonly string[] NormalDamageStrings = new string[1000];
		private static readonly string[] CritDamageStrings = new string[1000];
		private static readonly string[] RichNormalStrings = new string[1000];
		private static readonly string[] RichCritStrings = new string[1000];
		private static bool stringsInitialized = false;

		private static void InitStringTables() {
			if (stringsInitialized) return;
			for (int i = 0; i < 1000; i++) {
				int dmg = i * 10 + Random.Range(1, 9);
				NormalDamageStrings[i] = $"-{dmg}";
				CritDamageStrings[i] = $"CRIT {dmg * 3}!";
				RichNormalStrings[i] = $"<color=#FF5555>-{dmg}</color>";
				RichCritStrings[i] = $"<b><color=#FFDF40>CRIT!</color></b> {dmg * 3}";
			}
			stringsInitialized = true;
		}

		private readonly List<DamageItem> activeItems = new();
		private readonly Stack<DamageItem> poolSText = new();
		private readonly Stack<DamageItem> poolTMP = new();
		private Canvas targetCanvas;
		private RectTransform container;

		// FPS counter
		private float fpsAccumulator;
		private int fpsFrames;
		private float currentFps;
		private float currentFrameTimeMs;
		private float fpsTimer;
		private BenchmarkBackend lastBackend;

		private void Awake() {
			InitStringTables();
			EnsureCanvas();
			lastBackend = m_backend;
		}

		private void Start() {
			if (m_font == null) {
				m_font = SperlichTextSettings.GetOrDefault()?.defaultFont;
			}
			PrewarmPool(m_targetCount);
		}

		/// <summary>Pre-creates all pooled GameObjects in advance for both backends.</summary>
		public void PrewarmPool(int count) {
			int neededSText = count - (GetActiveCount(BenchmarkBackend.SperlichText) + poolSText.Count);
			for (int i = 0; i < neededSText; i++) {
				DamageItem item = CreateNewSTextItem();
				item.GameObject.SetActive(false);
				poolSText.Push(item);
			}

			int neededTMP = count - (GetActiveCount(BenchmarkBackend.TextMeshPro) + poolTMP.Count);
			for (int i = 0; i < neededTMP; i++) {
				DamageItem item = CreateNewTMPItem();
				item.GameObject.SetActive(false);
				poolTMP.Push(item);
			}
		}

		private int GetActiveCount(BenchmarkBackend backend) {
			int c = 0;
			for (int i = 0; i < activeItems.Count; i++) {
				if (activeItems[i].Backend == backend) c++;
			}
			return c;
		}

		private void Update() {
			UpdateFps();
			HandleHotkeys();

			// Detect backend changes from inspector
			if (m_backend != lastBackend) {
				SwitchBackend(m_backend);
			}

			UpdateSpawning();
			UpdateMovement();
		}

		/// <summary>Switches active backend by returning all items and repopulating from the target pool.</summary>
		public void SwitchBackend(BenchmarkBackend newBackend) {
			m_backend = newBackend;
			lastBackend = newBackend;

			for (int i = 0; i < activeItems.Count; i++) {
				ReturnToPool(activeItems[i]);
			}
			activeItems.Clear();
			UpdateSpawning();
		}

		/// <summary>Ensures a uGUI Canvas container exists for spawning.</summary>
		private void EnsureCanvas() {
			targetCanvas = GetComponentInParent<Canvas>();
			if (targetCanvas == null) {
				var canvasGo = new GameObject("Benchmark_Canvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
				targetCanvas = canvasGo.GetComponent<Canvas>();
				targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
			}

			if (container == null) {
				var containerGo = new GameObject("DamageNumbers_Container", typeof(RectTransform));
				container = containerGo.GetComponent<RectTransform>();
				container.SetParent(targetCanvas.transform, false);
				container.anchorMin = new Vector2(0.5f, 0.5f);
				container.anchorMax = new Vector2(0.5f, 0.5f);
				container.pivot = new Vector2(0.5f, 0.5f);
				container.sizeDelta = Vector2.zero;
			}
		}

		/// <summary>Updates the rolling FPS counter.</summary>
		private void UpdateFps() {
			fpsFrames++;
			fpsAccumulator += Time.unscaledDeltaTime;
			fpsTimer += Time.unscaledDeltaTime;

			if (fpsTimer >= 0.25f) {
				currentFps = fpsFrames / fpsAccumulator;
				currentFrameTimeMs = (fpsAccumulator / fpsFrames) * 1000f;
				fpsFrames = 0;
				fpsAccumulator = 0f;
				fpsTimer = 0f;
			}
		}

		/// <summary>Spawns damage numbers up to targetCount using object pooling.</summary>
		private void UpdateSpawning() {
			while (activeItems.Count < m_targetCount) {
				DamageItem item = GetFromPool(m_backend);
				InitDamageItem(ref item, randomProgress: true);
				activeItems.Add(item);
			}

			while (activeItems.Count > m_targetCount && activeItems.Count > 0) {
				int lastIdx = activeItems.Count - 1;
				ReturnToPool(activeItems[lastIdx]);
				activeItems.RemoveAt(lastIdx);
			}
		}

		/// <summary>Initializes a single damage text instance with random values and current visual settings.</summary>
		private void InitDamageItem(ref DamageItem item, bool randomProgress = false) {
			item.MaxLife = m_lifetime * Random.Range(0.9f, 1.1f);
			item.Elapsed = randomProgress ? Random.Range(0f, item.MaxLife) : 0f;
			item.StartPos = new Vector2(
				Random.Range(-m_spawnArea.x * 0.5f, m_spawnArea.x * 0.5f),
				Random.Range(-m_spawnArea.y * 0.5f, m_spawnArea.y * 0.5f)
			);

			item.IsCritical = Random.value < 0.2f;
			item.BaseColor = item.IsCritical ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(1f, 0.35f, 0.3f, 1f);

			int strIdx = Random.Range(0, 1000);
			if (m_randomRichText) {
				item.TextContent = item.IsCritical ? RichCritStrings[strIdx] : RichNormalStrings[strIdx];
			} else {
				item.TextContent = item.IsCritical ? CritDamageStrings[strIdx] : NormalDamageStrings[strIdx];
			}

			float fontSize = item.IsCritical ? m_fontSize * 1.35f : m_fontSize;

			// Position and alpha based on initial elapsed time
			float progress = item.Elapsed / item.MaxLife;
			float yOffset = Mathf.Sin(progress * Mathf.PI * 0.5f) * (m_floatSpeed * item.MaxLife);
			item.Rect.anchoredPosition = item.StartPos + new Vector2(0f, yOffset);

			float alpha = progress > 0.7f ? (1f - progress) / 0.3f : 1f;
			Color c = item.BaseColor;
			c.a *= alpha;

			if (item.Backend == BenchmarkBackend.SperlichText) {
				SText st = item.STextComp;
				st.Font = m_font;
				st.FontSize = fontSize;
				st.Text = item.TextContent;
				ApplySettingsToSText(st, c);
			} else {
				TextMeshProUGUI tmp = item.TmpComp;
				if (m_tmpFont != null) tmp.font = m_tmpFont;
				tmp.fontSize = fontSize;
				tmp.text = item.TextContent;
				tmp.color = c;
				ApplySettingsToTMP(tmp);
			}

			item.GameObject.SetActive(true);
		}

		/// <summary>Applies the current benchmark properties to an SText component.</summary>
		private void ApplySettingsToSText(SText st, Color col) {
			st.color = col;

			st.Shadow = m_enableShadow;
			st.ShadowQuality = m_shadowQuality;
			st.ShadowColor = m_shadowColor;
			st.ShadowOffset = m_shadowOffset;
			st.ShadowSoftness = m_shadowSoftness;
			st.ShadowDilate = m_shadowDilate;

			st.Outline = m_enableOutline;
			st.OutlineColor = m_outlineColor;
			st.OutlineWidth = m_outlineWidth;
			st.OutlineMode = m_outlineMode;

			st.Glow = m_enableGlow;
			st.GlowColor = m_glowColor;
			st.GlowPower = m_glowPower;

			st.Bloom = m_enableBloom;
			st.BloomColor = m_bloomColor;

			st.ClearBuiltinEffects();
			if (m_enableBuiltinEffects && m_builtinEffect != BuiltinEffect.None) {
				st.AddBuiltinEffect(new BuiltinEffectParams {
					Effect = m_builtinEffect,
					Enabled = true,
					Speed = 3f,
					Amplitude = 6f,
					Frequency = 2f
				});
			}
		}

		/// <summary>Applies supported properties (Outline, Underlay) to a TextMeshProUGUI component.</summary>
		private void ApplySettingsToTMP(TextMeshProUGUI tmp) {
			if (m_enableOutline && m_outlineWidth > 0f) {
				tmp.outlineWidth = m_outlineWidth * 0.1f;
				tmp.outlineColor = m_outlineColor;
			} else {
				tmp.outlineWidth = 0f;
			}

			// Underlay (Drop shadow) via material properties
			if (tmp.fontMaterial != null) {
				if (m_enableShadow) {
					tmp.fontMaterial.EnableKeyword("UNDERLAY_ON");
					tmp.fontMaterial.SetColor("_UnderlayColor", m_shadowColor);
					tmp.fontMaterial.SetFloat("_UnderlayOffsetX", m_shadowOffset.x * 0.1f);
					tmp.fontMaterial.SetFloat("_UnderlayOffsetY", m_shadowOffset.y * 0.1f);
					tmp.fontMaterial.SetFloat("_UnderlaySoftness", m_shadowSoftness * 0.1f);
					tmp.fontMaterial.SetFloat("_UnderlayDilate", m_shadowDilate);
				} else {
					tmp.fontMaterial.DisableKeyword("UNDERLAY_ON");
				}
			}
		}

		/// <summary>Moves floating damage numbers upward and fades them out.</summary>
		private void UpdateMovement() {
			float dt = Time.deltaTime;
			for (int i = activeItems.Count - 1; i >= 0; i--) {
				DamageItem item = activeItems[i];
				item.Elapsed += dt;

				if (item.Elapsed >= item.MaxLife) {
					InitDamageItem(ref item, randomProgress: false);
					activeItems[i] = item;
					continue;
				}

				float progress = item.Elapsed / item.MaxLife;
				float yOffset = Mathf.Sin(progress * Mathf.PI * 0.5f) * (m_floatSpeed * item.MaxLife);
				item.Rect.anchoredPosition = item.StartPos + new Vector2(0f, yOffset);

				// Alpha fade out in last 30% of life
				float alpha = progress > 0.7f ? (1f - progress) / 0.3f : 1f;
				Color c = item.BaseColor;
				c.a *= alpha;

				if (item.Backend == BenchmarkBackend.SperlichText) {
					item.STextComp.color = c;
					if (m_continuouslyUpdateText && Random.value < 0.05f) {
						item.STextComp.Text = NormalDamageStrings[Random.Range(0, 1000)];
					}
				} else {
					item.TmpComp.color = c;
					if (m_continuouslyUpdateText && Random.value < 0.05f) {
						item.TmpComp.text = NormalDamageStrings[Random.Range(0, 1000)];
					}
				}

				activeItems[i] = item;
			}
		}

		private DamageItem GetFromPool(BenchmarkBackend backend) {
			if (backend == BenchmarkBackend.SperlichText) {
				if (poolSText.Count > 0) return poolSText.Pop();
				return CreateNewSTextItem();
			} else {
				if (poolTMP.Count > 0) return poolTMP.Pop();
				return CreateNewTMPItem();
			}
		}

		private DamageItem CreateNewSTextItem() {
			var go = new GameObject("DmgText_SText", typeof(RectTransform), typeof(SText));
			var rt = go.GetComponent<RectTransform>();
			rt.SetParent(container, false);
			rt.sizeDelta = new Vector2(250f, 60f);

			var st = go.GetComponent<SText>();
			st.Align = TextAlign.Center;
			st.VerticalAlign = TextVerticalAlign.Middle;
			st.Wrap = TextWrap.NoWrap;

			return new DamageItem {
				Backend = BenchmarkBackend.SperlichText,
				GameObject = go,
				STextComp = st,
				Rect = rt
			};
		}

		private DamageItem CreateNewTMPItem() {
			var go = new GameObject("DmgText_TMP", typeof(RectTransform), typeof(TextMeshProUGUI));
			var rt = go.GetComponent<RectTransform>();
			rt.SetParent(container, false);
			rt.sizeDelta = new Vector2(250f, 60f);

			var tmp = go.GetComponent<TextMeshProUGUI>();
			tmp.alignment = TextAlignmentOptions.Center;
			tmp.textWrappingMode = TextWrappingModes.NoWrap;

			return new DamageItem {
				Backend = BenchmarkBackend.TextMeshPro,
				GameObject = go,
				TmpComp = tmp,
				Rect = rt
			};
		}

		private void ReturnToPool(DamageItem item) {
			item.GameObject.SetActive(false);
			if (item.Backend == BenchmarkBackend.SperlichText) {
				poolSText.Push(item);
			} else {
				poolTMP.Push(item);
			}
		}

		/// <summary>Keyboard shortcuts for quickly toggling features during playmode.</summary>
		private void HandleHotkeys() {
			// Tab or 0: Toggle Backend
			if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Alpha0)) {
				SwitchBackend(m_backend == BenchmarkBackend.SperlichText ? BenchmarkBackend.TextMeshPro : BenchmarkBackend.SperlichText);
			}

			if (Input.GetKeyDown(KeyCode.Alpha1)) {
				if (!m_enableShadow) {
					m_enableShadow = true;
					m_shadowQuality = BlurQuality.Low;
				} else if (m_shadowQuality == BlurQuality.Low) {
					m_shadowQuality = BlurQuality.Medium;
				} else if (m_shadowQuality == BlurQuality.Medium) {
					m_shadowQuality = BlurQuality.High;
				} else {
					m_enableShadow = false;
				}
				ReapplyAll();
			}

			if (Input.GetKeyDown(KeyCode.Alpha2)) {
				m_enableOutline = !m_enableOutline;
				ReapplyAll();
			}

			if (Input.GetKeyDown(KeyCode.Alpha3)) {
				m_enableGlow = !m_enableGlow;
				ReapplyAll();
			}

			if (Input.GetKeyDown(KeyCode.Alpha4)) {
				m_enableBloom = !m_enableBloom;
				ReapplyAll();
			}

			if (Input.GetKeyDown(KeyCode.Alpha5)) {
				m_enableBuiltinEffects = !m_enableBuiltinEffects;
				ReapplyAll();
			}

			if (Input.GetKeyDown(KeyCode.UpArrow)) {
				m_targetCount = Mathf.Min(5000, m_targetCount + 100);
				PrewarmPool(m_targetCount);
			}

			if (Input.GetKeyDown(KeyCode.DownArrow)) {
				m_targetCount = Mathf.Max(10, m_targetCount - 100);
			}
		}

		/// <summary>Reapplies the current settings to all active damage numbers.</summary>
		[ContextMenu("Reapply Settings To All")]
		public void ReapplyAll() {
			for (int i = 0; i < activeItems.Count; i++) {
				DamageItem item = activeItems[i];
				float fontSize = item.IsCritical ? m_fontSize * 1.35f : m_fontSize;

				if (item.Backend == BenchmarkBackend.SperlichText) {
					item.STextComp.Font = m_font;
					item.STextComp.FontSize = fontSize;
					item.STextComp.Text = item.TextContent;
					ApplySettingsToSText(item.STextComp, item.BaseColor);
				} else {
					if (m_tmpFont != null) item.TmpComp.font = m_tmpFont;
					item.TmpComp.fontSize = fontSize;
					item.TmpComp.text = item.TextContent;
					item.TmpComp.color = item.BaseColor;
					ApplySettingsToTMP(item.TmpComp);
				}
			}
		}

		private void OnGUI() {
			if (!m_showHUD) return;

			GUILayout.BeginArea(new Rect(15, 15, 360, 340), GUI.skin.box);
			GUILayout.Label("<b><size=14>Sperlich.Text Benchmark</size></b>");
			GUILayout.Space(4);

			string backendText = m_backend == BenchmarkBackend.SperlichText
				? "<color=#55FF55><b>Sperlich.Text (SText)</b></color>"
				: "<color=#55FFFF><b>TextMeshPro (TMP)</b></color>";
			GUILayout.Label($"[TAB] <b>Backend:</b> {backendText}");
			GUILayout.Space(4);

			GUILayout.Label($"<b>FPS:</b> {currentFps:0.0} ({currentFrameTimeMs:0.00} ms)");
			GUILayout.Label($"<b>Instanzen:</b> {activeItems.Count} (Pfeiltasten hoch/runter)");
			GUILayout.Space(6);

			string shadowText = !m_enableShadow ? "<color=grey>Aus</color>" :
				m_shadowQuality == BlurQuality.Low ? "<color=#55FF55>Low (SDF Single-Tap)</color>" :
				m_shadowQuality == BlurQuality.Medium ? "<color=#FFFF55>Medium (24 Gaussian)</color>" :
				"<color=#FF5555>High (48 Gaussian)</color>";

			GUILayout.Label($"[1] <b>Shadow:</b> {shadowText}");
			GUILayout.Label($"[2] <b>Outline:</b> {(m_enableOutline ? "<color=#55FF55>An</color>" : "<color=grey>Aus</color>")}");

			if (m_backend == BenchmarkBackend.SperlichText) {
				GUILayout.Label($"[3] <b>Glow:</b> {(m_enableGlow ? "<color=#55FF55>An</color>" : "<color=grey>Aus</color>")}");
				GUILayout.Label($"[4] <b>Bloom:</b> {(m_enableBloom ? "<color=#55FF55>An</color>" : "<color=grey>Aus</color>")}");
				GUILayout.Label($"[5] <b>Effects:</b> {(m_enableBuiltinEffects ? $"<color=#55FF55>{m_builtinEffect}</color>" : "<color=grey>Aus</color>")}");
			} else {
				GUILayout.Label("<color=#888888>[3-5] Glow/Bloom/Burst: (Nur SperlichText)</color>");
			}

			GUILayout.Space(8);
			GUILayout.Label("<size=10><color=#AAAAAA>[TAB] Backend | [1-5] Toggles | [↑/↓] Anzahl</color></size>");
			GUILayout.EndArea();
		}
	}
}
