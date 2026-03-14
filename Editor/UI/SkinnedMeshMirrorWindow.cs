using UnityEditor;
using UnityEngine;

namespace Sebanne.SkinnedMeshMirror.Editor
{
    public class SkinnedMeshMirrorWindow : EditorWindow
    {
        private const string DefaultOutputFolder = "Assets/Sebanne/SkinnedMeshMirror/Generated";
        private const string DiagnosticsHeightPrefsKey = "Sebanne.SkinnedMeshMirror.DiagnosticsHeight";
        private const float DefaultDiagnosticsHeight = 180f;
        private const float MinDiagnosticsHeight = 120f;
        private const float MaxDiagnosticsHeight = 420f;
        private const float SplitterHeight = 6f;

        private Config config = new Config
        {
            part = Part.Hand,
            mappingMode = MappingMode.PrefabLocalMirror,
            outputMode = OutputMode.MirroredRendererOnly,
            outputFolder = DefaultOutputFolder,
            fileNameSuffix = "_Mirrored",
            sourceSideMode = SourceSideMode.Auto,
            dryRun = true,
            verboseLog = true
        };

        private DefaultAsset outputFolderAsset;
        private bool showAdvancedSettings;
        private Vector2 windowScroll;
        private Vector2 diagnosticsScroll;
        private Result lastResult;
        private bool hasLastResult;
        private float diagnosticsPanelHeight = DefaultDiagnosticsHeight;
        private bool isResizingDiagnostics;
        private float diagnosticsResizeStartY;
        private float diagnosticsResizeStartHeight;

        [MenuItem("Tools/Sebanne/Skinned Mesh Mirror/Window")]
        public static void ShowWindow()
        {
            SkinnedMeshMirrorWindow window = GetWindow<SkinnedMeshMirrorWindow>("Skinned Mesh Mirror");
            window.minSize = new Vector2(460f, 520f);
        }

        private void OnEnable()
        {
            ApplyDefaultsIfNeeded();
            SyncOutputFolderAssetFromConfig();
            diagnosticsPanelHeight = Mathf.Clamp(
                EditorPrefs.GetFloat(DiagnosticsHeightPrefsKey, DefaultDiagnosticsHeight),
                MinDiagnosticsHeight,
                MaxDiagnosticsHeight);
        }

        private void OnGUI()
        {
            ApplyDefaultsIfNeeded();

            windowScroll = EditorGUILayout.BeginScrollView(windowScroll);
            try
            {
                EditorGUILayout.LabelField("Skinned Mesh Mirror", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "MA 向け衣装 prefab を前提にした MVP です。\n" +
                    "元の mesh / GameObject は直接変更せず、新しい Mesh.asset と GameObject を生成します。",
                    MessageType.Info);

                DrawPriorityErrorBox();
                DrawBasicSettings();
                DrawOutputSettings();
                DrawOptions();
                DrawSummary();
                DrawExecution();
                DrawDiagnostics();
                DrawAdvancedSettings();

                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "このツールは現在、本命モードのみ対応しています。\n" +
                    "BlendShape 補正は未実装です。\n" +
                    "prefab 構造によっては対応できない場合があります。",
                    MessageType.Warning);
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawBasicSettings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("基本設定", EditorStyles.boldLabel);
                DrawInlineDescription("何をどの向きで反転するかを決めます。");

                config.sourceRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("対象メッシュ", config.sourceRenderer, typeof(SkinnedMeshRenderer), true);
                DrawInlineDescription("反転元にする SkinnedMeshRenderer を選びます。");

                config.mirrorRoot = (Transform)EditorGUILayout.ObjectField("ミラールート", config.mirrorRoot, typeof(Transform), true);
                DrawInlineDescription("左右判定と反転の基準になる Transform です。");

                config.part = (Part)EditorGUILayout.EnumPopup("対象部位", config.part);
                DrawInlineDescription("左右対応を探す範囲を選びます。");

                config.sourceSideMode = (SourceSideMode)EditorGUILayout.EnumPopup("元にする側", config.sourceSideMode);

                EditorGUILayout.HelpBox(
                    "Left は左→右、Right は右→左、Auto は自動判定です。",
                    MessageType.Info);
            }
        }

        private void DrawOutputSettings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("出力設定", EditorStyles.boldLabel);
                DrawInlineDescription("保存先と生成名を決めます。");

                EditorGUI.BeginChangeCheck();
                DefaultAsset selectedFolder = (DefaultAsset)EditorGUILayout.ObjectField("出力先フォルダ", outputFolderAsset, typeof(DefaultAsset), false);
                if (EditorGUI.EndChangeCheck())
                {
                    outputFolderAsset = selectedFolder;
                    SyncConfigOutputFolderFromAsset();
                }
                DrawInlineDescription("生成する Mesh.asset の保存先です。");

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("既定値に戻す"))
                    {
                        ResetOutputFolderToDefault();
                    }

                    using (new EditorGUI.DisabledScope(outputFolderAsset == null))
                    {
                        if (GUILayout.Button("選択する"))
                        {
                            EditorGUIUtility.PingObject(outputFolderAsset);
                            Selection.activeObject = outputFolderAsset;
                        }
                    }
                }

                if (!IsValidOutputFolderAsset(outputFolderAsset))
                {
                    EditorGUILayout.HelpBox("Project 内の Assets 配下フォルダを指定してください。", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField("現在の出力先", config.outputFolder);
                    DrawInlineDescription("実際に保存される場所です。");
                }

                config.fileNameSuffix = EditorGUILayout.TextField("名前の末尾", config.fileNameSuffix);
                DrawInlineDescription("生成名の末尾に付ける文字です。");
            }
        }

        private void DrawOptions()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("オプション", EditorStyles.boldLabel);
                DrawInlineDescription("確認や切り分け用の設定です。");
                config.verboseLog = EditorGUILayout.Toggle("詳細ログ", config.verboseLog);
                DrawInlineDescription("Console に詳しいログを出します。");
                config.dryRun = EditorGUILayout.Toggle("確認だけ", config.dryRun);
                DrawInlineDescription("生成せず、診断だけ行います。");
            }
        }

        private void DrawSummary()
        {
            string generationDirection = GetGenerationDirectionText(config.sourceSideMode);
            string outputFolderText = string.IsNullOrWhiteSpace(config.outputFolder) ? "(未設定)" : config.outputFolder;

            EditorGUILayout.HelpBox(
                "実行サマリー\n" +
                $"生成方向: {generationDirection}\n" +
                $"出力先: {outputFolderText}\n" +
                $"名前の末尾: {config.fileNameSuffix}\n" +
                $"現在の方式: {MappingMode.PrefabLocalMirror}\n" +
                $"出力方式: {OutputMode.MirroredRendererOnly}",
                MessageType.Info);
            DrawInlineDescription("現在の設定内容を確認できます。");
        }

        private void DrawAdvancedSettings()
        {
            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "詳細設定", true);
            if (!showAdvancedSettings)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.EnumPopup("Mapping Mode", MappingMode.PrefabLocalMirror);
                    EditorGUILayout.EnumPopup("Output Mode", OutputMode.MirroredRendererOnly);
                }

                DrawInlineDescription("この MVP では固定項目です。");
            }
        }

        private void DrawExecution()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("実行", EditorStyles.boldLabel);
                DrawInlineDescription("診断または生成を実行します。");
                string buttonLabel = config.dryRun ? "確認だけ実行する" : "生成する";
                if (GUILayout.Button(buttonLabel, GUILayout.Height(34f)))
                {
                    Execute();
                }
            }
        }

        private void DrawPriorityErrorBox()
        {
            if (!hasLastResult || lastResult.errorCount <= 0 || lastResult.diagnostics == null)
            {
                return;
            }

            DiagnosticEntry topError = default;
            bool found = false;
            for (int i = 0; i < lastResult.diagnostics.Count; i++)
            {
                if (lastResult.diagnostics[i].severity != DiagnosticSeverity.Error)
                {
                    continue;
                }

                topError = lastResult.diagnostics[i];
                found = true;
                break;
            }

            if (!found)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(
                    $"エラーが {lastResult.errorCount} 件あります。\n" +
                    $"最重要: {topError.title}\n" +
                    $"原因: {topError.message}\n" +
                    $"対策: {(string.IsNullOrWhiteSpace(topError.suggestion) ? "診断一覧を確認してください。" : topError.suggestion)}",
                    MessageType.Error);
            }
        }

        private void DrawDiagnostics()
        {
            if (!hasLastResult)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("診断結果", EditorStyles.boldLabel);
                DrawInlineDescription("エラーや注意点を表示します。");
                EditorGUILayout.LabelField($"Error: {lastResult.errorCount}    Warning: {lastResult.warningCount}    Info: {lastResult.infoCount}");

                if (lastResult.diagnostics == null || lastResult.diagnostics.Count == 0)
                {
                    EditorGUILayout.LabelField("診断はまだありません。");
                    return;
                }

                diagnosticsScroll = EditorGUILayout.BeginScrollView(diagnosticsScroll, GUILayout.Height(diagnosticsPanelHeight));
                for (int i = 0; i < lastResult.diagnostics.Count; i++)
                {
                    DiagnosticEntry entry = lastResult.diagnostics[i];
                    DrawDiagnosticEntry(entry);
                }

                EditorGUILayout.EndScrollView();

                Rect splitterRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(SplitterHeight), GUILayout.ExpandWidth(true));
                DrawDiagnosticsSplitter(splitterRect);
            }
        }

        private void DrawDiagnosticsSplitter(Rect splitterRect)
        {
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);
            EditorGUI.DrawRect(
                new Rect(splitterRect.x, splitterRect.center.y - 1f, splitterRect.width, 2f),
                new Color(0.35f, 0.35f, 0.35f, 0.9f));

            Event currentEvent = Event.current;
            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                    if (currentEvent.button == 0 && splitterRect.Contains(currentEvent.mousePosition))
                    {
                        if (currentEvent.clickCount >= 2)
                        {
                            SetDiagnosticsPanelHeight(DefaultDiagnosticsHeight);
                        }
                        else
                        {
                            isResizingDiagnostics = true;
                            diagnosticsResizeStartY = currentEvent.mousePosition.y;
                            diagnosticsResizeStartHeight = diagnosticsPanelHeight;
                        }

                        currentEvent.Use();
                    }

                    break;

                case EventType.MouseDrag:
                    if (isResizingDiagnostics)
                    {
                        float delta = currentEvent.mousePosition.y - diagnosticsResizeStartY;
                        SetDiagnosticsPanelHeight(diagnosticsResizeStartHeight + delta);
                        Repaint();
                        currentEvent.Use();
                    }

                    break;

                case EventType.MouseUp:
                    if (isResizingDiagnostics)
                    {
                        isResizingDiagnostics = false;
                        currentEvent.Use();
                    }

                    break;
            }
        }

        private void SetDiagnosticsPanelHeight(float height)
        {
            diagnosticsPanelHeight = Mathf.Clamp(height, MinDiagnosticsHeight, MaxDiagnosticsHeight);
            EditorPrefs.SetFloat(DiagnosticsHeightPrefsKey, diagnosticsPanelHeight);
        }

        private void DrawDiagnosticEntry(DiagnosticEntry entry)
        {
            GUIContent iconContent = GetSeverityIcon(entry.severity);
            MessageType messageType = GetSeverityMessageType(entry.severity);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(iconContent, GUILayout.Width(24f), GUILayout.Height(20f));
                    EditorGUILayout.LabelField(GetSeverityLabel(entry.severity), EditorStyles.boldLabel, GUILayout.Width(70f));
                    EditorGUILayout.LabelField(entry.title, EditorStyles.boldLabel);
                }

                EditorGUILayout.HelpBox(entry.message, messageType);
                EditorGUILayout.LabelField("対処", string.IsNullOrWhiteSpace(entry.suggestion) ? "-" : entry.suggestion);

                Color previousColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                EditorGUILayout.LabelField($"診断コード: {entry.code}");
                GUI.color = previousColor;
            }
        }

        private void Execute()
        {
            config.mappingMode = MappingMode.PrefabLocalMirror;
            config.outputMode = OutputMode.MirroredRendererOnly;
            if (IsValidOutputFolderAsset(outputFolderAsset))
            {
                SyncConfigOutputFolderFromAsset();
            }
            else if (outputFolderAsset != null)
            {
                config.outputFolder = string.Empty;
            }

            Log.Info("ui: config validated");

            if (!SkinnedMeshMirrorBuilder.Build(config, out Result result))
            {
                lastResult = result;
                hasLastResult = true;
                if (!string.IsNullOrEmpty(result.failureReason))
                {
                    Log.Error("Build failed: " + result.failureReason);
                }

                return;
            }

            lastResult = result;
            hasLastResult = true;

            if (config.dryRun)
            {
                Log.Info($"dryRun result: plannedMeshAsset={result.plannedMeshAssetPath} plannedObject={result.plannedObjectName}");
                return;
            }

            Log.Info($"created: meshAsset={result.meshAssetPath} object={result.objectName}");
            if (config.verboseLog)
            {
                Log.Info($"sourceSideDecisionMode={result.sourceSideDecisionMode}");
                Log.Info($"sourceSideOverride={result.sourceSideOverride}");
                Log.Info($"autoDetectedLeftCount={result.autoDetectedLeftCount}");
                Log.Info($"autoDetectedRightCount={result.autoDetectedRightCount}");
                Log.Info($"finalSourceSide={result.sourceSide}");
                Log.Info($"finalTargetSide={result.targetSide}");
                Log.Info($"prefabCompatibility={(result.prefabCompatibilityPass ? "Pass" : "Fail")}");
                Log.Info($"compatibilityReason={result.compatibilityReason}");
                Log.Info($"detectedArmatureRoot={result.detectedArmatureRootPath}");
                Log.Info($"detectedHips={result.detectedHipsPath}");
                Log.Info($"effectiveMirrorRoot={result.effectiveMirrorRootPath}");
                Log.Info($"sourceSide tally: leftCount={result.sourceSideLeftCount} rightCount={result.sourceSideRightCount}");
                Log.Info($"mesh: flippedTriangles={result.flippedTriangles} bindposesRebuilt={result.bindposesRebuilt}");
                Log.Info($"blendShapes: {result.blendShapeCount}");
            }
        }

        private void ApplyDefaultsIfNeeded()
        {
            if (string.IsNullOrWhiteSpace(config.outputFolder))
            {
                config.outputFolder = DefaultOutputFolder;
            }

            if (string.IsNullOrWhiteSpace(config.fileNameSuffix))
            {
                config.fileNameSuffix = "_Mirrored";
            }

            if (outputFolderAsset == null)
            {
                SyncOutputFolderAssetFromConfig();
            }
        }

        private void ResetOutputFolderToDefault()
        {
            config.outputFolder = DefaultOutputFolder;
            SyncOutputFolderAssetFromConfig();
        }

        private void SyncOutputFolderAssetFromConfig()
        {
            if (string.IsNullOrWhiteSpace(config.outputFolder))
            {
                outputFolderAsset = null;
                return;
            }

            outputFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(config.outputFolder);
        }

        private void SyncConfigOutputFolderFromAsset()
        {
            if (!IsValidOutputFolderAsset(outputFolderAsset))
            {
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(outputFolderAsset);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                config.outputFolder = assetPath;
            }
        }

        private static bool IsValidOutputFolderAsset(DefaultAsset folderAsset)
        {
            if (folderAsset == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(folderAsset);
            return !string.IsNullOrWhiteSpace(assetPath) &&
                   assetPath.StartsWith("Assets", System.StringComparison.Ordinal) &&
                   AssetDatabase.IsValidFolder(assetPath);
        }

        private static string GetGenerationDirectionText(SourceSideMode sourceSideMode)
        {
            switch (sourceSideMode)
            {
                case SourceSideMode.Left:
                    return "左 → 右";
                case SourceSideMode.Right:
                    return "右 → 左";
                default:
                    return "自動判定（解析結果に応じて 左→右 / 右→左）";
            }
        }

        private static void DrawInlineDescription(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            GUIStyle descriptionStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            Color previousColor = GUI.contentColor;
            GUI.contentColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            EditorGUILayout.LabelField(text, descriptionStyle);
            GUI.contentColor = previousColor;
            GUILayout.Space(4f);
        }

        private static GUIContent GetSeverityIcon(DiagnosticSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticSeverity.Error:
                    return EditorGUIUtility.IconContent("console.erroricon");
                case DiagnosticSeverity.Warning:
                    return EditorGUIUtility.IconContent("console.warnicon");
                default:
                    return EditorGUIUtility.IconContent("console.infoicon");
            }
        }

        private static MessageType GetSeverityMessageType(DiagnosticSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticSeverity.Error:
                    return MessageType.Error;
                case DiagnosticSeverity.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }

        private static string GetSeverityLabel(DiagnosticSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticSeverity.Error:
                    return "エラー";
                case DiagnosticSeverity.Warning:
                    return "警告";
                default:
                    return "情報";
            }
        }
    }
}
