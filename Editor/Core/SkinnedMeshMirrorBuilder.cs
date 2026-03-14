using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Sebanne.SkinnedMeshMirror.Editor
{
    public static class SkinnedMeshMirrorBuilder
    {
        private const string SourceSideVertexSelectionMode = "DominantBoneWeightSide";
        private const int VertexSampleLogLimit = 8;

        private struct VertexSelectionData
        {
            public bool[] keptVertices;
            public int[] oldToNewVertexIndices;
            public int sourceVertexCount;
            public int filteredOutVertexCount;
            public int keptTriangleCount;
            public int filteredOutTriangleCount;
            public string selectionMode;
            public List<string> samples;
        }

        public static bool Build(Config config, out Result result)
        {
            result = new Result
            {
                success = false,
                diagnostics = new List<DiagnosticEntry>(),
                mappingMode = config.mappingMode,
                outputMode = config.outputMode,
                missingBoneNames = new List<string>()
            };

            if (!ValidateConfig(config, out string validationError))
            {
                AddValidationDiagnostic(ref result, validationError);
                return false;
            }

            Log.Info($"mode: {config.mappingMode}");
            Log.Info($"outputMode: {config.outputMode}");

            if (config.optionalAnimator != null)
            {
                AddDiagnostic(
                    ref result,
                    DiagnosticSeverity.Warning,
                    "W002",
                    "Animator は補助扱いで無視されます",
                    "PrefabLocalMirror を本命として扱うため、指定された Animator は今回の MVP では使用しません。",
                    "Animator を指定していても問題ありませんが、結果には影響しません。");
            }

            ScanData scan = ScanSource(config.sourceRenderer);
            result.vertices = scan.vertices;
            result.bones = scan.bones;
            result.usedBones = scan.usedBones;
            result.blendShapeCount = scan.blendShapeCount;
            Log.Info($"scanned: vertices={scan.vertices} bones={scan.bones} usedBones={scan.usedBones}");
            AddDiagnostic(
                ref result,
                DiagnosticSeverity.Info,
                "I003",
                "対象メッシュを解析しました",
                $"vertices={scan.vertices} bones={scan.bones} usedBones={scan.usedBones}",
                "対象メッシュと部位設定が想定どおりか確認してください。");
            AddBlendShapeDiagnostics(ref result, scan.sharedMesh);

            if (!SkinnedMeshMirrorLocalMap.TryMap(config, scan, out MapData mapData, out string mappingError))
            {
                AddMappingFailureDiagnostic(ref result, mappingError);
                return false;
            }

            result.sourceSide = mapData.sourceSide;
            result.targetSide = mapData.targetSide;
            result.sourceSideDecisionMode = mapData.sourceSideAnalysis.overrideSide == Side.Unknown ? "Auto" : "Override";
            result.sourceSideOverride = mapData.sourceSideAnalysis.overrideSide == Side.Unknown ? "None" : mapData.sourceSideAnalysis.overrideSide.ToString();
            result.prefabCompatibilityPass = mapData.compatibilityAnalysis.isPass;
            result.compatibilityReason = mapData.compatibilityAnalysis.compatibilityReason;
            result.detectedArmatureRootPath = GetAbsolutePath(mapData.compatibilityAnalysis.detectedArmatureRoot);
            result.detectedHipsPath = GetAbsolutePath(mapData.compatibilityAnalysis.detectedHips);
            result.effectiveMirrorRootPath = GetAbsolutePath(mapData.compatibilityAnalysis.effectiveMirrorRoot);
            result.autoDetectedLeftCount = mapData.sourceSideAnalysis.leftCount;
            result.autoDetectedRightCount = mapData.sourceSideAnalysis.rightCount;
            result.sourceSideLeftCount = mapData.sourceSideAnalysis.leftCount;
            result.sourceSideRightCount = mapData.sourceSideAnalysis.rightCount;
            result.mirroredBones = mapData.mirroredBones;
            result.keptBones = mapData.keptBones;
            result.missingBones = mapData.missingBones;
            result.nullBoneSlots = mapData.nullBoneSlots;
            result.missingUsedBoneMappings = mapData.missingUsedBoneMappings;
            result.missingBoneNames = mapData.missingBoneNames ?? new List<string>();

            Log.Info($"sourceSide: {result.sourceSide}");
            Log.Info($"targetSide: {result.targetSide}");
            Log.Info($"sourceSideDecisionMode={result.sourceSideDecisionMode}");
            Log.Info($"sourceSideOverride={result.sourceSideOverride}");
            Log.Info($"autoDetectedLeftCount={result.autoDetectedLeftCount}");
            Log.Info($"autoDetectedRightCount={result.autoDetectedRightCount}");
            Log.Info($"finalSourceSide={result.sourceSide}");
            Log.Info($"finalTargetSide={result.targetSide}");
            Log.Info($"detectedArmatureRoot={result.detectedArmatureRootPath}");
            Log.Info($"detectedHips={result.detectedHipsPath}");
            Log.Info($"prefabCompatibility={(result.prefabCompatibilityPass ? "Pass" : "Fail")}");
            Log.Info($"compatibilityReason={result.compatibilityReason}");
            Log.Info($"mapped: mirrored={result.mirroredBones} kept={result.keptBones} missing={result.missingBones} nullBoneSlots={result.nullBoneSlots} missingUsedBoneMappings={result.missingUsedBoneMappings}");
            AddDiagnostic(
                ref result,
                DiagnosticSeverity.Info,
                "I004",
                "左右判定と bone 対応付けが完了しました",
                $"sourceSide={result.sourceSide} targetSide={result.targetSide}",
                "生成方向が意図どおりか確認してください。");
            if (result.missingBoneNames.Count > 0)
            {
                Log.Warn($"missingUsedBoneMappings: {string.Join(", ", result.missingBoneNames)}");
            }
            else
            {
                Log.Info("missingUsedBoneMappings: none");
            }

            if (result.nullBoneSlots > 0)
            {
                Log.Warn($"nullBoneSlots: {result.nullBoneSlots}");
            }

            if (result.nullBoneSlots > 0 || result.missingUsedBoneMappings > 0)
            {
                AddDiagnostic(
                    ref result,
                    DiagnosticSeverity.Warning,
                    "W003",
                    "一部 bone slot に注意が必要です",
                    $"nullBoneSlots={result.nullBoneSlots} missingUsedBoneMappings={result.missingUsedBoneMappings}",
                    "未使用 null slot は継続します。used bone の missing が増える場合は bone 名と部位設定を見直してください。");
            }

            if (config.verboseLog)
            {
                LogVerboseMapping(mapData);
            }

            Transform effectiveMirrorRoot = mapData.compatibilityAnalysis.effectiveMirrorRoot != null ? mapData.compatibilityAnalysis.effectiveMirrorRoot : config.mirrorRoot;
            if (!TryAnalyzeSourceSideVertexSelection(config.sourceRenderer, effectiveMirrorRoot, mapData, out VertexSelectionData vertexSelection, out string vertexSelectionError))
            {
                AddVertexSelectionFailureDiagnostic(ref result, vertexSelectionError);
                return false;
            }

            result.sourceVertexCount = vertexSelection.sourceVertexCount;
            result.filteredOutVertexCount = vertexSelection.filteredOutVertexCount;
            result.keptTriangleCount = vertexSelection.keptTriangleCount;
            result.filteredOutTriangleCount = vertexSelection.filteredOutTriangleCount;
            result.sourceSideVertexSelectionMode = vertexSelection.selectionMode;
            Log.Info($"sourceSideVertexSelectionMode={result.sourceSideVertexSelectionMode}");
            Log.Info($"sourceVertexCount={result.sourceVertexCount}");
            Log.Info($"filteredOutVertexCount={result.filteredOutVertexCount}");
            Log.Info($"keptTriangleCount={result.keptTriangleCount}");
            Log.Info($"filteredOutTriangleCount={result.filteredOutTriangleCount}");
            AddDiagnostic(
                ref result,
                DiagnosticSeverity.Info,
                "I005",
                "source side vertex 抽出が完了しました",
                $"sourceVertexCount={result.sourceVertexCount} filteredOutVertexCount={result.filteredOutVertexCount} keptTriangleCount={result.keptTriangleCount} filteredOutTriangleCount={result.filteredOutTriangleCount}",
                "残した頂点数と三角形数が想定どおりか確認してください。");
            if (config.verboseLog)
            {
                LogVertexSelectionSamples(vertexSelection);
            }

            if (string.IsNullOrWhiteSpace(config.outputFolder) ||
                !config.outputFolder.Replace("\\", "/").Trim().StartsWith("Assets", StringComparison.Ordinal))
            {
                AddOutputFolderErrorDiagnostic(ref result, config.outputFolder);
                return false;
            }

            string outputFolder = NormalizeOutputFolder(config.outputFolder);
            string assetBaseName = BuildMirroredName(config.sourceRenderer.sharedMesh.name, result.sourceSide, result.targetSide, config.fileNameSuffix);
            string objectName = BuildMirroredName(config.sourceRenderer.gameObject.name, result.sourceSide, result.targetSide, config.fileNameSuffix);

            result.plannedMeshAssetPath = CombineAssetPath(outputFolder, assetBaseName + ".asset");
            result.plannedObjectName = objectName;
            Log.Info($"planned: meshAsset={result.plannedMeshAssetPath} object={result.plannedObjectName}");
            AddDiagnostic(
                ref result,
                DiagnosticSeverity.Info,
                "I006",
                "出力内容を確定しました",
                $"meshAsset={result.plannedMeshAssetPath} object={result.plannedObjectName}",
                "出力先と名前の末尾が意図どおりか確認してください。");

            if (config.dryRun)
            {
                Log.Info("dryRun: no asset or object will be created");
                AddDiagnostic(
                    ref result,
                    DiagnosticSeverity.Info,
                    "I007",
                    "確認だけを完了しました",
                    "Dry Run のため asset と object はまだ作成していません。",
                    "診断結果と planned output を確認してから生成してください。");
                result.success = true;
                return true;
            }

            string ensuredFolder = EnsureAssetFolder(outputFolder);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(CombineAssetPath(ensuredFolder, assetBaseName + ".asset"));

            GameObject outputObject = CreateMirroredRendererObject(config, effectiveMirrorRoot, objectName);
            if (outputObject == null)
            {
                AddMeshCreationFailureDiagnostic(ref result, "Failed to create output GameObject.");
                return false;
            }

            SkinnedMeshRenderer outputRenderer = Undo.AddComponent<SkinnedMeshRenderer>(outputObject);
            if (outputRenderer == null)
            {
                AddMeshCreationFailureDiagnostic(ref result, "Failed to create mirrored SkinnedMeshRenderer.");
                return false;
            }

            Undo.RecordObject(outputRenderer, "Configure mirrored SkinnedMeshRenderer");
            CopyRendererSettings(config.sourceRenderer, outputRenderer);
            outputRenderer.bones = mapData.mappedBones;
            outputRenderer.rootBone = mapData.mappedRootBone;

            if (!TryCreateMirroredMesh(config.sourceRenderer, effectiveMirrorRoot, outputRenderer.transform, mapData, scan.usedBoneIndexSet, vertexSelection, config.fileNameSuffix, out Mesh mirroredMesh, out string meshError, out int flippedTriangles, out int bindposesRebuilt))
            {
                AddMeshCreationFailureDiagnostic(ref result, meshError);
                Undo.DestroyObjectImmediate(outputObject);
                return false;
            }

            AssetDatabase.CreateAsset(mirroredMesh, assetPath);
            AssetDatabase.SaveAssets();

            outputRenderer.sharedMesh = mirroredMesh;
            outputRenderer.localBounds = config.sourceRenderer.localBounds;
            CopySiblingComponents(config.sourceRenderer.gameObject, outputObject, config.verboseLog);
            EditorUtility.SetDirty(outputRenderer);
            EditorUtility.SetDirty(outputObject);

            result.flippedTriangles = flippedTriangles;
            result.bindposesRebuilt = bindposesRebuilt;
            result.meshAssetPath = assetPath;
            result.objectName = outputObject.name;
            result.success = true;
            AddDiagnostic(
                ref result,
                DiagnosticSeverity.Info,
                "I008",
                "生成が完了しました",
                $"meshAsset={result.meshAssetPath} object={result.objectName}",
                "Hierarchy と Project を確認して生成物を確認してください。");

            Log.Info($"created: meshAsset={assetPath} object={outputObject.name}");
            Log.Info("done");

            Selection.activeObject = outputObject;
            return true;
        }

        private static bool ValidateConfig(Config config, out string validationError)
        {
            validationError = string.Empty;

            if (config.sourceRenderer == null)
            {
                validationError = "Source SkinnedMeshRenderer is required.";
                return false;
            }

            if (config.sourceRenderer.sharedMesh == null)
            {
                validationError = "Source sharedMesh is required.";
                return false;
            }

            if (config.mirrorRoot == null)
            {
                validationError = "Mirror Root is required.";
                return false;
            }

            if (config.mappingMode != MappingMode.PrefabLocalMirror)
            {
                validationError = "This MVP supports PrefabLocalMirror only.";
                return false;
            }

            if (config.outputMode != OutputMode.MirroredRendererOnly)
            {
                validationError = "This MVP supports MirroredRendererOnly only.";
                return false;
            }

            Mesh mesh = config.sourceRenderer.sharedMesh;
            if (config.sourceRenderer.bones == null || config.sourceRenderer.bones.Length == 0)
            {
                validationError = "Source renderer bones are empty.";
                return false;
            }

            if (config.sourceRenderer.bones.Length != mesh.bindposes.Length)
            {
                validationError = "Source bones length and bindposes length do not match. Build is aborted for safety.";
                return false;
            }

            return true;
        }

        private static ScanData ScanSource(SkinnedMeshRenderer sourceRenderer)
        {
            Mesh mesh = sourceRenderer.sharedMesh;
            var usedBoneIndices = new HashSet<int>();
            BoneWeight[] weights = mesh.boneWeights;

            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight weight = weights[i];
                if (weight.weight0 > 0f)
                {
                    usedBoneIndices.Add(weight.boneIndex0);
                }

                if (weight.weight1 > 0f)
                {
                    usedBoneIndices.Add(weight.boneIndex1);
                }

                if (weight.weight2 > 0f)
                {
                    usedBoneIndices.Add(weight.boneIndex2);
                }

                if (weight.weight3 > 0f)
                {
                    usedBoneIndices.Add(weight.boneIndex3);
                }
            }

            return new ScanData
            {
                sharedMesh = mesh,
                sourceBones = sourceRenderer.bones,
                sourceRootBone = sourceRenderer.rootBone,
                usedBoneIndexSet = usedBoneIndices,
                vertices = mesh.vertexCount,
                bones = mesh.bindposes.Length,
                usedBones = usedBoneIndices.Count,
                blendShapeCount = mesh.blendShapeCount
            };
        }

        private static bool TryAnalyzeSourceSideVertexSelection(
            SkinnedMeshRenderer sourceRenderer,
            Transform effectiveMirrorRoot,
            MapData mapData,
            out VertexSelectionData selectionData,
            out string failureReason)
        {
            selectionData = new VertexSelectionData
            {
                keptVertices = Array.Empty<bool>(),
                oldToNewVertexIndices = Array.Empty<int>(),
                samples = new List<string>(),
                selectionMode = SourceSideVertexSelectionMode
            };
            failureReason = string.Empty;

            Mesh sourceMesh = sourceRenderer.sharedMesh;
            if (sourceMesh == null)
            {
                failureReason = "Source mesh is missing.";
                return false;
            }

            BoneWeight[] boneWeights = sourceMesh.boneWeights;
            if (boneWeights == null || boneWeights.Length != sourceMesh.vertexCount)
            {
                failureReason = "Source mesh boneWeights are missing or do not match vertex count.";
                return false;
            }

            bool[] keptVertices = new bool[sourceMesh.vertexCount];
            int[] oldToNewVertexIndices = new int[sourceMesh.vertexCount];
            for (int i = 0; i < oldToNewVertexIndices.Length; i++)
            {
                oldToNewVertexIndices[i] = -1;
            }

            Side[] boneSides = BuildBoneSideCache(sourceRenderer.bones, effectiveMirrorRoot);
            int keptVertexCursor = 0;

            for (int vertexIndex = 0; vertexIndex < sourceMesh.vertexCount; vertexIndex++)
            {
                BoneWeight boneWeight = boneWeights[vertexIndex];
                EvaluateVertexSelection(
                    sourceRenderer.bones,
                    boneSides,
                    boneWeight,
                    mapData.sourceSide,
                    mapData.targetSide,
                    out bool keepVertex,
                    out string dominantBoneName,
                    out Side dominantSide);

                keptVertices[vertexIndex] = keepVertex;
                if (keepVertex)
                {
                    oldToNewVertexIndices[vertexIndex] = keptVertexCursor++;
                    selectionData.sourceVertexCount++;
                }
                else
                {
                    selectionData.filteredOutVertexCount++;
                }

                if (selectionData.samples.Count < VertexSampleLogLimit)
                {
                    selectionData.samples.Add($"vertex sample: index={vertexIndex} dominantBone={dominantBoneName} side={dominantSide} kept={keepVertex}");
                }
            }

            int keptTriangleCount = 0;
            int filteredTriangleCount = 0;
            for (int subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
            {
                if (sourceMesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
                {
                    failureReason = $"Submesh {subMeshIndex} uses unsupported topology {sourceMesh.GetTopology(subMeshIndex)}. Only triangles are supported in this MVP.";
                    return false;
                }

                int[] triangles = sourceMesh.GetTriangles(subMeshIndex);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    bool keepTriangle = keptVertices[triangles[i]] &&
                                        keptVertices[triangles[i + 1]] &&
                                        keptVertices[triangles[i + 2]];
                    if (keepTriangle)
                    {
                        keptTriangleCount++;
                    }
                    else
                    {
                        filteredTriangleCount++;
                    }
                }
            }

            selectionData.keptVertices = keptVertices;
            selectionData.oldToNewVertexIndices = oldToNewVertexIndices;
            selectionData.keptTriangleCount = keptTriangleCount;
            selectionData.filteredOutTriangleCount = filteredTriangleCount;

            if (selectionData.sourceVertexCount == 0)
            {
                failureReason = $"No source-side vertices were selected for {mapData.sourceSide}.";
                return false;
            }

            if (selectionData.keptTriangleCount == 0)
            {
                failureReason = $"No triangles remained after filtering to source-side vertices for {mapData.sourceSide}.";
                return false;
            }

            return true;
        }

        private static Side[] BuildBoneSideCache(Transform[] bones, Transform effectiveMirrorRoot)
        {
            var boneSides = new Side[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                boneSides[i] = DetectBoneSide(bones[i], effectiveMirrorRoot);
            }

            return boneSides;
        }

        private static void EvaluateVertexSelection(
            Transform[] sourceBones,
            Side[] boneSides,
            BoneWeight boneWeight,
            Side sourceSide,
            Side targetSide,
            out bool keepVertex,
            out string dominantBoneName,
            out Side dominantSide)
        {
            keepVertex = false;
            dominantBoneName = "<none>";
            dominantSide = Side.Unknown;

            float sourceWeight = 0f;
            float targetWeight = 0f;
            float dominantWeight = -1f;
            int dominantBoneIndex = -1;

            AccumulateBoneContribution(sourceBones, boneSides, boneWeight.boneIndex0, boneWeight.weight0, sourceSide, targetSide, ref sourceWeight, ref targetWeight, ref dominantWeight, ref dominantBoneIndex);
            AccumulateBoneContribution(sourceBones, boneSides, boneWeight.boneIndex1, boneWeight.weight1, sourceSide, targetSide, ref sourceWeight, ref targetWeight, ref dominantWeight, ref dominantBoneIndex);
            AccumulateBoneContribution(sourceBones, boneSides, boneWeight.boneIndex2, boneWeight.weight2, sourceSide, targetSide, ref sourceWeight, ref targetWeight, ref dominantWeight, ref dominantBoneIndex);
            AccumulateBoneContribution(sourceBones, boneSides, boneWeight.boneIndex3, boneWeight.weight3, sourceSide, targetSide, ref sourceWeight, ref targetWeight, ref dominantWeight, ref dominantBoneIndex);

            if (dominantBoneIndex >= 0 && dominantBoneIndex < sourceBones.Length)
            {
                dominantBoneName = sourceBones[dominantBoneIndex] != null ? sourceBones[dominantBoneIndex].name : "<null>";
                dominantSide = boneSides[dominantBoneIndex];
            }

            if (dominantSide == sourceSide)
            {
                keepVertex = true;
                return;
            }

            if (dominantSide == targetSide)
            {
                keepVertex = false;
                return;
            }

            if (sourceWeight > targetWeight)
            {
                keepVertex = true;
                return;
            }

            if (targetWeight > sourceWeight)
            {
                keepVertex = false;
            }
        }

        private static void AccumulateBoneContribution(
            Transform[] sourceBones,
            Side[] boneSides,
            int boneIndex,
            float weight,
            Side sourceSide,
            Side targetSide,
            ref float sourceWeight,
            ref float targetWeight,
            ref float dominantWeight,
            ref int dominantBoneIndex)
        {
            if (weight <= 0f || boneIndex < 0 || boneIndex >= sourceBones.Length)
            {
                return;
            }

            if (weight > dominantWeight)
            {
                dominantWeight = weight;
                dominantBoneIndex = boneIndex;
            }

            Side boneSide = boneSides[boneIndex];
            if (boneSide == sourceSide)
            {
                sourceWeight += weight;
            }
            else if (boneSide == targetSide)
            {
                targetWeight += weight;
            }
        }

        private static Side DetectBoneSide(Transform bone, Transform effectiveMirrorRoot)
        {
            if (bone == null)
            {
                return Side.Unknown;
            }

            if (TryGetRelativePath(effectiveMirrorRoot, bone, out string relativePath))
            {
                Side relativeSide = SideTokenUtility.DetectSide(relativePath);
                if (relativeSide != Side.Unknown)
                {
                    return relativeSide;
                }
            }

            Side nameSide = SideTokenUtility.DetectSide(bone.name);
            if (nameSide != Side.Unknown)
            {
                return nameSide;
            }

            return SideTokenUtility.DetectSide(GetAbsolutePath(bone));
        }

        private static void LogVertexSelectionSamples(VertexSelectionData selectionData)
        {
            if (selectionData.samples == null)
            {
                return;
            }

            for (int i = 0; i < selectionData.samples.Count; i++)
            {
                Log.Info(selectionData.samples[i]);
            }
        }

        private static bool TryBuildSourceSideFilteredMesh(
            Mesh sourceMesh,
            VertexSelectionData selectionData,
            out Mesh filteredMesh,
            out string failureReason)
        {
            filteredMesh = null;
            failureReason = string.Empty;

            int keptVertexCount = selectionData.sourceVertexCount;
            if (keptVertexCount <= 0)
            {
                failureReason = "No source-side vertices were selected.";
                return false;
            }

            filteredMesh = new Mesh
            {
                indexFormat = keptVertexCount > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16,
                name = sourceMesh.name
            };

            filteredMesh.vertices = FilterVector3Array(sourceMesh.vertices, selectionData.oldToNewVertexIndices, keptVertexCount);

            Vector3[] normals = sourceMesh.normals;
            if (normals != null && normals.Length == sourceMesh.vertexCount)
            {
                filteredMesh.normals = FilterVector3Array(normals, selectionData.oldToNewVertexIndices, keptVertexCount);
            }

            Vector4[] tangents = sourceMesh.tangents;
            if (tangents != null && tangents.Length == sourceMesh.vertexCount)
            {
                filteredMesh.tangents = FilterVector4Array(tangents, selectionData.oldToNewVertexIndices, keptVertexCount);
            }

            Color[] colors = sourceMesh.colors;
            if (colors != null && colors.Length == sourceMesh.vertexCount)
            {
                filteredMesh.colors = FilterColorArray(colors, selectionData.oldToNewVertexIndices, keptVertexCount);
            }

            Vector2[] uv = sourceMesh.uv;
            if (uv != null && uv.Length == sourceMesh.vertexCount)
            {
                filteredMesh.uv = FilterVector2Array(uv, selectionData.oldToNewVertexIndices, keptVertexCount);
            }

            Vector2[] uv2 = sourceMesh.uv2;
            if (uv2 != null && uv2.Length == sourceMesh.vertexCount)
            {
                filteredMesh.uv2 = FilterVector2Array(uv2, selectionData.oldToNewVertexIndices, keptVertexCount);
            }

            Vector2[] uv3 = sourceMesh.uv3;
            if (uv3 != null && uv3.Length == sourceMesh.vertexCount)
            {
                filteredMesh.uv3 = FilterVector2Array(uv3, selectionData.oldToNewVertexIndices, keptVertexCount);
            }

            Vector2[] uv4 = sourceMesh.uv4;
            if (uv4 != null && uv4.Length == sourceMesh.vertexCount)
            {
                filteredMesh.uv4 = FilterVector2Array(uv4, selectionData.oldToNewVertexIndices, keptVertexCount);
            }

            BoneWeight[] boneWeights = sourceMesh.boneWeights;
            if (boneWeights != null && boneWeights.Length == sourceMesh.vertexCount)
            {
                filteredMesh.boneWeights = FilterBoneWeightArray(boneWeights, selectionData.oldToNewVertexIndices, keptVertexCount);
            }

            filteredMesh.bindposes = sourceMesh.bindposes;
            filteredMesh.subMeshCount = sourceMesh.subMeshCount;

            for (int subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
            {
                int[] sourceTriangles = sourceMesh.GetTriangles(subMeshIndex);
                var filteredTriangles = new List<int>(sourceTriangles.Length);
                for (int i = 0; i + 2 < sourceTriangles.Length; i += 3)
                {
                    int a = sourceTriangles[i];
                    int b = sourceTriangles[i + 1];
                    int c = sourceTriangles[i + 2];
                    int mappedA = selectionData.oldToNewVertexIndices[a];
                    int mappedB = selectionData.oldToNewVertexIndices[b];
                    int mappedC = selectionData.oldToNewVertexIndices[c];
                    if (mappedA < 0 || mappedB < 0 || mappedC < 0)
                    {
                        continue;
                    }

                    filteredTriangles.Add(mappedA);
                    filteredTriangles.Add(mappedB);
                    filteredTriangles.Add(mappedC);
                }

                filteredMesh.SetTriangles(filteredTriangles, subMeshIndex);
            }

            CopyFilteredBlendShapes(sourceMesh, filteredMesh, selectionData.oldToNewVertexIndices, keptVertexCount);
            return true;
        }

        private static void CopyFilteredBlendShapes(Mesh sourceMesh, Mesh filteredMesh, int[] oldToNewVertexIndices, int keptVertexCount)
        {
            if (sourceMesh.blendShapeCount <= 0)
            {
                return;
            }

            for (int shapeIndex = 0; shapeIndex < sourceMesh.blendShapeCount; shapeIndex++)
            {
                string shapeName = sourceMesh.GetBlendShapeName(shapeIndex);
                int frameCount = sourceMesh.GetBlendShapeFrameCount(shapeIndex);
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    float frameWeight = sourceMesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex);
                    Vector3[] deltaVertices = new Vector3[sourceMesh.vertexCount];
                    Vector3[] deltaNormals = new Vector3[sourceMesh.vertexCount];
                    Vector3[] deltaTangents = new Vector3[sourceMesh.vertexCount];
                    sourceMesh.GetBlendShapeFrameVertices(shapeIndex, frameIndex, deltaVertices, deltaNormals, deltaTangents);

                    Vector3[] filteredDeltaVertices = FilterVector3Array(deltaVertices, oldToNewVertexIndices, keptVertexCount);
                    Vector3[] filteredDeltaNormals = FilterVector3Array(deltaNormals, oldToNewVertexIndices, keptVertexCount);
                    Vector3[] filteredDeltaTangents = FilterVector3Array(deltaTangents, oldToNewVertexIndices, keptVertexCount);
                    filteredMesh.AddBlendShapeFrame(shapeName, frameWeight, filteredDeltaVertices, filteredDeltaNormals, filteredDeltaTangents);
                }
            }
        }

        private static Vector3[] FilterVector3Array(Vector3[] source, int[] oldToNewVertexIndices, int keptVertexCount)
        {
            var filtered = new Vector3[keptVertexCount];
            for (int i = 0; i < oldToNewVertexIndices.Length; i++)
            {
                int mappedIndex = oldToNewVertexIndices[i];
                if (mappedIndex >= 0)
                {
                    filtered[mappedIndex] = source[i];
                }
            }

            return filtered;
        }

        private static Vector4[] FilterVector4Array(Vector4[] source, int[] oldToNewVertexIndices, int keptVertexCount)
        {
            var filtered = new Vector4[keptVertexCount];
            for (int i = 0; i < oldToNewVertexIndices.Length; i++)
            {
                int mappedIndex = oldToNewVertexIndices[i];
                if (mappedIndex >= 0)
                {
                    filtered[mappedIndex] = source[i];
                }
            }

            return filtered;
        }

        private static Color[] FilterColorArray(Color[] source, int[] oldToNewVertexIndices, int keptVertexCount)
        {
            var filtered = new Color[keptVertexCount];
            for (int i = 0; i < oldToNewVertexIndices.Length; i++)
            {
                int mappedIndex = oldToNewVertexIndices[i];
                if (mappedIndex >= 0)
                {
                    filtered[mappedIndex] = source[i];
                }
            }

            return filtered;
        }

        private static Vector2[] FilterVector2Array(Vector2[] source, int[] oldToNewVertexIndices, int keptVertexCount)
        {
            var filtered = new Vector2[keptVertexCount];
            for (int i = 0; i < oldToNewVertexIndices.Length; i++)
            {
                int mappedIndex = oldToNewVertexIndices[i];
                if (mappedIndex >= 0)
                {
                    filtered[mappedIndex] = source[i];
                }
            }

            return filtered;
        }

        private static BoneWeight[] FilterBoneWeightArray(BoneWeight[] source, int[] oldToNewVertexIndices, int keptVertexCount)
        {
            var filtered = new BoneWeight[keptVertexCount];
            for (int i = 0; i < oldToNewVertexIndices.Length; i++)
            {
                int mappedIndex = oldToNewVertexIndices[i];
                if (mappedIndex >= 0)
                {
                    filtered[mappedIndex] = source[i];
                }
            }

            return filtered;
        }

        private static void AddValidationDiagnostic(ref Result result, string validationError)
        {
            switch (validationError)
            {
                case "Source SkinnedMeshRenderer is required.":
                    AddFailureDiagnostic(ref result, "E001", "対象メッシュが未設定です", validationError, "対象メッシュに SkinnedMeshRenderer を指定してください。");
                    break;
                case "Source sharedMesh is required.":
                    AddFailureDiagnostic(ref result, "E002", "sharedMesh が見つかりません", validationError, "対象メッシュの SkinnedMeshRenderer に sharedMesh が入っているか確認してください。");
                    break;
                case "Mirror Root is required.":
                    AddFailureDiagnostic(ref result, "E003", "ミラールートが未設定です", validationError, "ミラールートに衣装 prefab 内の共通親を指定してください。");
                    break;
                case "This MVP supports PrefabLocalMirror only.":
                    AddFailureDiagnostic(ref result, "E004", "未対応の方式です", validationError, "現在は PrefabLocalMirror のみ対応しています。");
                    break;
                case "This MVP supports MirroredRendererOnly only.":
                    AddFailureDiagnostic(ref result, "E005", "未対応の出力方式です", validationError, "現在は MirroredRendererOnly のみ対応しています。");
                    break;
                case "Source renderer bones are empty.":
                    AddFailureDiagnostic(ref result, "E006", "bone 情報が見つかりません", validationError, "対象メッシュの bones 設定を確認してください。");
                    break;
                case "Source bones length and bindposes length do not match. Build is aborted for safety.":
                    AddFailureDiagnostic(ref result, "E007", "bones と bindposes の数が一致しません", validationError, "元 mesh の bone 設定が壊れていないか確認してください。");
                    break;
                default:
                    AddFailureDiagnostic(ref result, "E012", "生成前チェックに失敗しました", validationError, "Console の詳細ログを確認してください。");
                    break;
            }
        }

        private static void AddBlendShapeDiagnostics(ref Result result, Mesh mesh)
        {
            if (mesh == null || mesh.blendShapeCount <= 0)
            {
                return;
            }

            AddDiagnostic(
                ref result,
                DiagnosticSeverity.Warning,
                "W001",
                "BlendShape 補正は未実装です",
                $"BlendShape correction is not implemented. BlendShapes are preserved as-is. count={mesh.blendShapeCount}",
                "BlendShape を動かしたときに変形が崩れる場合があります。必要なら生成後に見た目を確認してください。");
        }

        private static void AddMappingFailureDiagnostic(ref Result result, string mappingError)
        {
            if (mappingError.Contains("Auto sourceSide detection tied between left and right", StringComparison.Ordinal) ||
                mappingError.Contains("Could not determine sourceSide", StringComparison.Ordinal))
            {
                AddFailureDiagnostic(ref result, "E009", "元にする側を決定できません", mappingError, "元にする側を Left / Right で明示するか、bone 名と部位設定を確認してください。");
                return;
            }

            AddFailureDiagnostic(ref result, "E008", "bone 対応付けに失敗しました", mappingError, "MA 向け prefab 構造か、bone 名、対象部位、ミラールートを確認してください。");
        }

        private static void AddVertexSelectionFailureDiagnostic(ref Result result, string vertexSelectionError)
        {
            AddFailureDiagnostic(ref result, "E010", "source 側の頂点抽出に失敗しました", vertexSelectionError, "対象部位と元にする側が対象 mesh の boneWeight と合っているか確認してください。");
        }

        private static void AddOutputFolderErrorDiagnostic(ref Result result, string outputFolder)
        {
            string message = string.IsNullOrWhiteSpace(outputFolder)
                ? "出力先フォルダが未設定です。"
                : $"出力先フォルダが Assets 配下ではありません: {outputFolder}";

            AddFailureDiagnostic(ref result, "E011", "出力先フォルダが不正です", message, "Project 内の Assets 配下フォルダを指定してください。");
        }

        private static void AddMeshCreationFailureDiagnostic(ref Result result, string message)
        {
            AddFailureDiagnostic(ref result, "E012", "生成処理に失敗しました", message, "Console の詳細ログと診断コードを確認してください。");
        }

        private static void AddFailureDiagnostic(ref Result result, string code, string title, string message, string suggestion)
        {
            if (string.IsNullOrEmpty(result.failureReason))
            {
                result.failureReason = message;
            }

            AddDiagnostic(ref result, DiagnosticSeverity.Error, code, title, message, suggestion);
        }

        private static void AddDiagnostic(ref Result result, DiagnosticSeverity severity, string code, string title, string message, string suggestion)
        {
            if (result.diagnostics == null)
            {
                result.diagnostics = new List<DiagnosticEntry>();
            }

            var entry = new DiagnosticEntry
            {
                severity = severity,
                code = code,
                title = title,
                message = message,
                suggestion = suggestion
            };

            result.diagnostics.Add(entry);
            switch (severity)
            {
                case DiagnosticSeverity.Error:
                    result.errorCount++;
                    Log.Error(FormatDiagnosticLog(entry));
                    break;
                case DiagnosticSeverity.Warning:
                    result.warningCount++;
                    Log.Warn(FormatDiagnosticLog(entry));
                    break;
                default:
                    result.infoCount++;
                    Log.Info(FormatDiagnosticLog(entry));
                    break;
            }
        }

        private static string FormatDiagnosticLog(DiagnosticEntry entry)
        {
            string formatted = $"[{entry.code}] {entry.title}: {entry.message}";
            if (!string.IsNullOrWhiteSpace(entry.suggestion))
            {
                formatted += $" suggestion={entry.suggestion}";
            }

            return formatted;
        }

        private static void LogVerboseMapping(MapData mapData)
        {
            if (mapData.entries == null)
            {
                return;
            }

            for (int i = 0; i < mapData.entries.Length; i++)
            {
                BoneMappingEntry entry = mapData.entries[i];
                string sourceName = entry.sourceBone != null ? entry.sourceBone.name : "<null>";
                string mappedName = entry.mappedBone != null ? entry.mappedBone.name : "<null>";
                Log.Info($"map[{entry.index}]: source={sourceName} mapped={mappedName} used={entry.isUsed} part={entry.isPartBone} attempt={entry.attemptedMirror} missing={entry.missing} detail={entry.detail}");
            }
        }

        private static GameObject CreateMirroredRendererObject(Config config, Transform effectiveMirrorRoot, string objectName)
        {
            GameObject outputObject = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(outputObject, "Create mirrored renderer object");
            Undo.SetTransformParent(outputObject.transform, config.sourceRenderer.transform.parent, "Parent mirrored renderer object");
            Undo.RecordObject(outputObject.transform, "Mirror renderer transform");

            outputObject.layer = config.sourceRenderer.gameObject.layer;
            outputObject.tag = config.sourceRenderer.gameObject.tag;
            MirrorTransform(effectiveMirrorRoot, config.sourceRenderer.transform, outputObject.transform);

            int sourceSiblingIndex = config.sourceRenderer.transform.GetSiblingIndex();
            if (outputObject.transform.parent != null)
            {
                outputObject.transform.SetSiblingIndex(Mathf.Min(sourceSiblingIndex + 1, outputObject.transform.parent.childCount - 1));
            }

            return outputObject;
        }

        private static bool TryCreateMirroredMesh(
            SkinnedMeshRenderer sourceRenderer,
            Transform mirrorRoot,
            Transform outputTransform,
            MapData mapData,
            HashSet<int> usedBoneIndexSet,
            VertexSelectionData vertexSelection,
            string fileNameSuffix,
            out Mesh mirroredMesh,
            out string failureReason,
            out int flippedTriangles,
            out int bindposesRebuilt)
        {
            mirroredMesh = null;
            failureReason = string.Empty;
            flippedTriangles = 0;
            bindposesRebuilt = 0;

            Mesh sourceMesh = sourceRenderer.sharedMesh;
            if (sourceMesh == null)
            {
                failureReason = "Source mesh is missing.";
                return false;
            }

            if (!TryBuildSourceSideFilteredMesh(sourceMesh, vertexSelection, out mirroredMesh, out failureReason))
            {
                return false;
            }

            // Keep existing BlendShapes on the filtered mesh in this MVP without mirrored correction.
            // TODO: mirrored BlendShape correction is not implemented.
            mirroredMesh.name = sourceMesh.name + (string.IsNullOrWhiteSpace(fileNameSuffix) ? "_Mirrored" : fileNameSuffix.Trim());

            Vector3[] vertices = mirroredMesh.vertices;
            Vector3[] normals = mirroredMesh.normals;
            Vector4[] tangents = mirroredMesh.tangents;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 sourceWorld = sourceRenderer.transform.TransformPoint(vertices[i]);
                Vector3 mirroredWorld = MirrorPoint(mirrorRoot, sourceWorld);
                vertices[i] = outputTransform.InverseTransformPoint(mirroredWorld);
            }

            mirroredMesh.vertices = vertices;

            if (normals != null && normals.Length == vertices.Length)
            {
                for (int i = 0; i < normals.Length; i++)
                {
                    Vector3 sourceWorldDirection = sourceRenderer.transform.TransformDirection(normals[i]);
                    Vector3 mirroredWorldDirection = MirrorVector(mirrorRoot, sourceWorldDirection);
                    normals[i] = outputTransform.InverseTransformDirection(mirroredWorldDirection).normalized;
                }

                mirroredMesh.normals = normals;
            }

            if (tangents != null && tangents.Length == vertices.Length)
            {
                for (int i = 0; i < tangents.Length; i++)
                {
                    Vector3 tangentDirection = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                    Vector3 sourceWorldDirection = sourceRenderer.transform.TransformDirection(tangentDirection);
                    Vector3 mirroredWorldDirection = MirrorVector(mirrorRoot, sourceWorldDirection);
                    Vector3 localDirection = outputTransform.InverseTransformDirection(mirroredWorldDirection).normalized;
                    tangents[i] = new Vector4(localDirection.x, localDirection.y, localDirection.z, -tangents[i].w);
                }

                mirroredMesh.tangents = tangents;
            }

            flippedTriangles = FlipTriangles(mirroredMesh);

            Matrix4x4[] bindposes = new Matrix4x4[mapData.mappedBones.Length];
            for (int i = 0; i < mapData.mappedBones.Length; i++)
            {
                Transform bone = mapData.mappedBones[i];
                if (bone == null)
                {
                    if (usedBoneIndexSet != null && usedBoneIndexSet.Contains(i))
                    {
                        failureReason = $"Bindpose rebuild failed because mapped bone at index {i} is null and that bone index is used by mesh weights.";
                        return false;
                    }

                    bindposes[i] = sourceMesh.bindposes[i];
                    continue;
                }

                bindposes[i] = bone.worldToLocalMatrix * outputTransform.localToWorldMatrix;
            }

            mirroredMesh.bindposes = bindposes;
            bindposesRebuilt = bindposes.Length;
            return true;
        }

        private static int FlipTriangles(Mesh mesh)
        {
            int flipped = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                int[] triangles = mesh.GetTriangles(subMesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int temp = triangles[i];
                    triangles[i] = triangles[i + 1];
                    triangles[i + 1] = temp;
                    flipped += 3;
                }

                mesh.SetTriangles(triangles, subMesh);
            }

            return flipped;
        }

        private static void CopyRendererSettings(SkinnedMeshRenderer source, SkinnedMeshRenderer destination)
        {
            destination.sharedMaterials = source.sharedMaterials;
            destination.rootBone = source.rootBone;
            destination.localBounds = source.localBounds;
            destination.updateWhenOffscreen = source.updateWhenOffscreen;
            destination.skinnedMotionVectors = source.skinnedMotionVectors;
            destination.motionVectorGenerationMode = source.motionVectorGenerationMode;
            destination.quality = source.quality;
            destination.shadowCastingMode = source.shadowCastingMode;
            destination.receiveShadows = source.receiveShadows;
            destination.lightProbeUsage = source.lightProbeUsage;
            destination.reflectionProbeUsage = source.reflectionProbeUsage;
            destination.probeAnchor = source.probeAnchor;
            destination.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            destination.sortingLayerID = source.sortingLayerID;
            destination.sortingOrder = source.sortingOrder;
            destination.renderingLayerMask = source.renderingLayerMask;
        }

        private static void CopySiblingComponents(GameObject sourceObject, GameObject targetObject, bool verboseLog)
        {
            if (sourceObject == null || targetObject == null)
            {
                Log.Warn("componentCopy failed: source or target GameObject is null.");
                return;
            }

            Component[] components = sourceObject.GetComponents<Component>();
            int copiedCount = 0;

            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    if (verboseLog)
                    {
                        Log.Info("componentCopy skip: <null component>");
                    }

                    continue;
                }

                Type componentType = component.GetType();
                if (componentType == typeof(Transform))
                {
                    if (verboseLog)
                    {
                        Log.Info("componentCopy skip: Transform");
                    }

                    continue;
                }

                if (componentType == typeof(SkinnedMeshRenderer))
                {
                    if (verboseLog)
                    {
                        Log.Info("componentCopy skip: SkinnedMeshRenderer");
                    }

                    continue;
                }

                try
                {
                    bool copied = ComponentUtility.CopyComponent(component);
                    bool pasted = copied && ComponentUtility.PasteComponentAsNew(targetObject);
                    if (!copied || !pasted)
                    {
                        Log.Warn($"componentCopy failed: {componentType.Name} reason=Copy/Paste returned false");
                        continue;
                    }

                    copiedCount++;
                    if (verboseLog)
                    {
                        Log.Info($"componentCopy copied: {componentType.Name}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"componentCopy failed: {componentType.Name} reason={ex.Message}");
                }
            }

            Log.Info($"componentCopy: copied={copiedCount}");
        }

        private static void MirrorTransform(Transform mirrorRoot, Transform source, Transform target)
        {
            Vector3 mirroredPosition = MirrorPoint(mirrorRoot, source.position);
            Vector3 mirroredForward = MirrorVector(mirrorRoot, source.forward);
            Vector3 mirroredUp = MirrorVector(mirrorRoot, source.up);

            target.position = mirroredPosition;
            target.rotation = Quaternion.LookRotation(mirroredForward, mirroredUp);
            target.localScale = source.localScale;
        }

        private static Vector3 MirrorPoint(Transform mirrorRoot, Vector3 worldPoint)
        {
            Vector3 local = mirrorRoot.InverseTransformPoint(worldPoint);
            local.x = -local.x;
            return mirrorRoot.TransformPoint(local);
        }

        private static Vector3 MirrorVector(Transform mirrorRoot, Vector3 worldVector)
        {
            Vector3 local = mirrorRoot.InverseTransformDirection(worldVector);
            local.x = -local.x;
            return mirrorRoot.TransformDirection(local).normalized;
        }

        private static string BuildMirroredName(string sourceName, Side sourceSide, Side targetSide, string suffix)
        {
            string safeSuffix = string.IsNullOrWhiteSpace(suffix) ? "_Mirrored" : suffix.Trim();
            string swappedName = ReplaceSideInName(sourceName, sourceSide, targetSide);

            if (string.Equals(swappedName, sourceName, StringComparison.Ordinal))
            {
                swappedName = sourceName + safeSuffix;
            }
            else if (!swappedName.EndsWith(safeSuffix, StringComparison.Ordinal))
            {
                swappedName += safeSuffix;
            }

            return swappedName;
        }

        private static string ReplaceSideInName(string value, Side sourceSide, Side targetSide)
        {
            return SideTokenUtility.ReplaceSideInName(value, sourceSide, targetSide);
        }

        private static string NormalizeOutputFolder(string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                return "Assets";
            }

            string normalized = outputFolder.Replace("\\", "/").Trim();
            if (!normalized.StartsWith("Assets", StringComparison.Ordinal))
            {
                Log.Warn($"Output Folder '{outputFolder}' is outside Assets. Using Assets instead.");
                return "Assets";
            }

            return normalized.TrimEnd('/');
        }

        private static string EnsureAssetFolder(string outputFolder)
        {
            if (AssetDatabase.IsValidFolder(outputFolder))
            {
                return outputFolder;
            }

            string[] segments = outputFolder.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }

                current = next;
            }

            return outputFolder;
        }

        private static string CombineAssetPath(string folder, string fileName)
        {
            return Path.Combine(folder, fileName).Replace("\\", "/");
        }

        private static string GetAbsolutePath(Transform target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            if (target.parent == null)
            {
                return target.name;
            }

            return GetAbsolutePath(target.parent) + "/" + target.name;
        }

        private static bool TryGetRelativePath(Transform root, Transform target, out string relativePath)
        {
            if (root == null || target == null)
            {
                relativePath = string.Empty;
                return false;
            }

            if (root == target)
            {
                relativePath = root.name;
                return true;
            }

            var stack = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                relativePath = string.Empty;
                return false;
            }

            stack.Push(root.name);
            relativePath = string.Join("/", stack.ToArray());
            return true;
        }
    }
}
