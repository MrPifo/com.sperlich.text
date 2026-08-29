using UnityEditor;
using UnityEngine;

namespace Sperlich.Text.EditorTools {

	/// <summary>
	/// Default inspector for <see cref="FontDefinition"/> with one change: <c>fieldKind</c> is drawn
	/// disabled. Only single-channel SDF is generated in v1 (see <see cref="FontAccess"/>); MTSDF and
	/// Bitmap arrive with the native msdfgen path and would change the shader too, so the field must
	/// not read as a working knob yet.
	/// </summary>
	[CustomEditor(typeof(FontDefinition))]
	public sealed class FontDefinitionEditor : Editor {

		public override void OnInspectorGUI() {
			serializedObject.Update();

			DrawPropertiesExcluding(serializedObject, "m_Script", "fieldKind");

			using (new EditorGUI.DisabledScope(true)) {
				EditorGUILayout.PropertyField(serializedObject.FindProperty("fieldKind"));
			}
			EditorGUILayout.HelpBox(
				"Only single-channel SDF is generated right now. MTSDF / Bitmap come with the native " +
				"msdfgen path (Weg B) and will also swap the shader.",
				MessageType.Info);

			serializedObject.ApplyModifiedProperties();
		}
	}
}
