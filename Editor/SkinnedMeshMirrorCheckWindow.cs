using UnityEditor;
using UnityEngine;

namespace Sebanne.SkinnedMeshMirror.Editor
{
    public sealed class SkinnedMeshMirrorCheckWindow : EditorWindow
    {
        private const string WindowTitle = "Skinned Mesh Mirror";
        private const string PackageName = "com.sebanne.skinned-mesh-mirror";
        private const string DisplayName = "Skinned Mesh Mirror";

        [MenuItem("Tools/Sebanne/Skinned Mesh Mirror/Check Window")]
        private static void Open()
        {
            var window = GetWindow<SkinnedMeshMirrorCheckWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(420f, 220f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Sebanne Skinned Mesh Mirror の package 読み込み確認用ウィンドウです。MVP 本体は Window メニューから開き、ここでは package 情報と導線確認を行います。", MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("package名", PackageName);
            EditorGUILayout.LabelField("displayName", DisplayName);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("MVP 本体は `Tools/Sebanne/Skinned Mesh Mirror/Window` から開けます。", EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();
            if (GUILayout.Button("本体 Window を開く"))
            {
                SkinnedMeshMirrorWindow.ShowWindow();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("確認ログを出す"))
            {
                Debug.Log("[Sebanne Skinned Mesh Mirror] Check window is working.");
            }
        }
    }
}
