using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sebanne.SkinnedMeshMirror.Editor
{
    public static class SkinnedMeshMirrorLocalMap
    {
        private const float MinimumTargetCoverage = 0.6f;

        public static bool TryMap(Config config, ScanData scan, out MapData mapData, out string failureReason)
        {
            mapData = new MapData
            {
                mappedBones = Array.Empty<Transform>(),
                entries = Array.Empty<BoneMappingEntry>(),
                missingBoneNames = new List<string>()
            };
            failureReason = string.Empty;

            if (config.mappingMode != MappingMode.PrefabLocalMirror)
            {
                failureReason = "Only PrefabLocalMirror is implemented in this MVP.";
                return false;
            }

            if (scan.sourceBones == null || scan.sourceBones.Length == 0)
            {
                failureReason = BuildCompatibilityFailure("Source renderer has no bones.");
                Log.Error(failureReason);
                return false;
            }

            CompatibilityAnalysis compatibilityAnalysis = AnalyzePrefabCompatibility(config, scan);
            LogCompatibilityAnalysis(compatibilityAnalysis);
            if (!compatibilityAnalysis.isPass)
            {
                failureReason = compatibilityAnalysis.compatibilityReason;
                return false;
            }

            Transform effectiveMirrorRoot = compatibilityAnalysis.effectiveMirrorRoot != null ? compatibilityAnalysis.effectiveMirrorRoot : config.mirrorRoot;
            var allTransforms = effectiveMirrorRoot.GetComponentsInChildren<Transform>(true);
            var nameLookup = BuildNameLookup(allTransforms);
            var pathLookup = BuildPathLookup(effectiveMirrorRoot, allTransforms);

            SourceSideAnalysis sourceSideAnalysis = AnalyzeSourceSide(config, scan, effectiveMirrorRoot);
            if (config.verboseLog)
            {
                LogSourceSideDetails(sourceSideAnalysis);
            }

            Side sourceSide = sourceSideAnalysis.detectedSide;
            if (sourceSide == Side.Unknown)
            {
                LogSourceSideDiagnostics(sourceSideAnalysis);
                failureReason = sourceSideAnalysis.decisionMode == SourceSideMode.Auto &&
                                sourceSideAnalysis.leftCount == sourceSideAnalysis.rightCount &&
                                sourceSideAnalysis.leftCount > 0
                    ? "Auto sourceSide detection tied between left and right. Use Source Side Override."
                    : "Could not determine sourceSide from used bones. Check Part selection, Mirror Root, armature/hips chain, or bone naming.";
                return false;
            }

            Side targetSide = sourceSide == Side.Left ? Side.Right : Side.Left;
            compatibilityAnalysis = FinalizeCompatibilityWithSourceTargetCoverage(config, scan, effectiveMirrorRoot, compatibilityAnalysis, sourceSide, targetSide, nameLookup, pathLookup);
            LogCompatibilityCoverage(compatibilityAnalysis);
            if (!compatibilityAnalysis.isPass)
            {
                failureReason = compatibilityAnalysis.compatibilityReason;
                return false;
            }

            var mappedBones = new Transform[scan.sourceBones.Length];
            var entries = new BoneMappingEntry[scan.sourceBones.Length];
            int mirroredBones = 0;
            int keptBones = 0;
            int missingBones = 0;
            int nullBoneSlots = 0;
            int missingUsedBoneMappings = 0;
            int attemptedUsedPartBones = 0;
            int missingUsedPartBones = 0;

            for (int i = 0; i < scan.sourceBones.Length; i++)
            {
                Transform sourceBone = scan.sourceBones[i];
                bool isUsed = scan.usedBoneIndexSet != null && scan.usedBoneIndexSet.Contains(i);
                bool isPartBone = sourceBone != null && IsPartBone(sourceBone.name, config.part);
                Side boneSide = sourceBone == null ? Side.Unknown : GetEffectiveSide(effectiveMirrorRoot, sourceBone, out _, out _, out _);
                bool attemptedMirror = sourceBone != null && isPartBone && boneSide == sourceSide;

                var entry = new BoneMappingEntry
                {
                    index = i,
                    sourceBone = sourceBone,
                    isUsed = isUsed,
                    isPartBone = isPartBone,
                    attemptedMirror = attemptedMirror
                };

                if (sourceBone == null)
                {
                    entry.missing = true;
                    entry.detail = "source bone is null";
                    mappedBones[i] = null;
                    entries[i] = entry;
                    nullBoneSlots++;
                    missingBones++;
                    if (isUsed)
                    {
                        missingUsedBoneMappings++;
                    }
                    continue;
                }

                if (attemptedMirror)
                {
                    if (isUsed)
                    {
                        attemptedUsedPartBones++;
                    }

                    Transform mappedBone = FindOppositeBone(effectiveMirrorRoot, sourceBone, sourceSide, targetSide, nameLookup, pathLookup);
                    if (mappedBone != null && mappedBone != sourceBone)
                    {
                        mappedBones[i] = mappedBone;
                        entry.mappedBone = mappedBone;
                        entry.mirrored = true;
                        entry.detail = mappedBone.name;
                        mirroredBones++;
                    }
                    else
                    {
                        mappedBones[i] = sourceBone;
                        entry.mappedBone = sourceBone;
                        entry.kept = true;
                        entry.missing = isUsed;
                        entry.detail = "opposite bone not found";
                        keptBones++;

                        if (isUsed)
                        {
                            missingBones++;
                            missingUsedBoneMappings++;
                            missingUsedPartBones++;
                            mapData.missingBoneNames.Add(sourceBone.name);
                        }
                    }
                }
                else
                {
                    mappedBones[i] = sourceBone;
                    entry.mappedBone = sourceBone;
                    entry.kept = true;
                    entry.detail = isPartBone ? "kept because bone is not on sourceSide" : "kept because bone is outside selected Part";
                    keptBones++;
                }

                entries[i] = entry;
            }

            if (attemptedUsedPartBones == 0)
            {
                failureReason = BuildCompatibilityFailure("Selected Part did not match any used source-side bones.");
                return false;
            }

            float mappedRatio = (attemptedUsedPartBones - missingUsedPartBones) / (float)attemptedUsedPartBones;
            if (mappedRatio < 0.75f)
            {
                failureReason = $"Bone mapping coverage is too low for safe build. mappedRatio={mappedRatio:0.00}";
                return false;
            }

            Transform mappedRootBone = ResolveRootBone(effectiveMirrorRoot, scan.sourceRootBone, sourceSide, targetSide, nameLookup, pathLookup);
            if (scan.sourceRootBone != null && mappedRootBone == null)
            {
                failureReason = "rootBone mapping could not be resolved safely.";
                return false;
            }

            mapData.sourceSide = sourceSide;
            mapData.targetSide = targetSide;
            mapData.compatibilityAnalysis = compatibilityAnalysis;
            mapData.sourceSideAnalysis = sourceSideAnalysis;
            mapData.mappedBones = mappedBones;
            mapData.mappedRootBone = mappedRootBone;
            mapData.entries = entries;
            mapData.mirroredBones = mirroredBones;
            mapData.keptBones = keptBones;
            mapData.missingBones = missingBones;
            mapData.nullBoneSlots = nullBoneSlots;
            mapData.missingUsedBoneMappings = missingUsedBoneMappings;
            return true;
        }

        private static CompatibilityAnalysis AnalyzePrefabCompatibility(Config config, ScanData scan)
        {
            // TODO: Setup Outfit / Merge Armature compatibility is approximated here and may need tighter heuristics for unusual rigs.
            var analysis = new CompatibilityAnalysis
            {
                isPass = false,
                compatibilityReason = BuildCompatibilityFailure("Compatibility analysis did not run."),
                targetCoverage = 0f
            };

            List<Transform> nonNullBones = CollectNonNullBones(scan.sourceBones);
            if (nonNullBones.Count == 0)
            {
                analysis.compatibilityReason = BuildCompatibilityFailure("Source renderer bones are all null.");
                return analysis;
            }

            Transform commonAncestor = FindCommonAncestor(nonNullBones);
            Transform detectedHips = FindDetectedHips(scan, commonAncestor);
            Transform detectedArmatureRoot = DetermineArmatureRoot(commonAncestor, detectedHips);

            analysis.detectedArmatureRoot = detectedArmatureRoot;
            analysis.detectedHips = detectedHips;

            if (detectedArmatureRoot == null || detectedHips == null)
            {
                analysis.compatibilityReason = BuildCompatibilityFailure("Setup Outfit-style bone identification failed.");
                return analysis;
            }

            Transform effectiveMirrorRoot = DetermineEffectiveMirrorRoot(config.mirrorRoot, detectedArmatureRoot, detectedHips, nonNullBones, out bool supplemented);
            analysis.effectiveMirrorRoot = effectiveMirrorRoot;
            analysis.mirrorRootWasAutoSupplemented = supplemented;

            if (effectiveMirrorRoot == null)
            {
                analysis.compatibilityReason = BuildCompatibilityFailure("Could not determine an effective mirror root from the armature/hips chain.");
                return analysis;
            }

            var allTransforms = effectiveMirrorRoot.GetComponentsInChildren<Transform>(true);
            var nameLookup = BuildNameLookup(allTransforms);
            var pathLookup = BuildPathLookup(effectiveMirrorRoot, allTransforms);

            for (int i = 0; i < scan.sourceBones.Length; i++)
            {
                Transform bone = scan.sourceBones[i];
                if (bone == null || !IsPartBone(bone.name, config.part))
                {
                    continue;
                }

                if (scan.usedBoneIndexSet != null && scan.usedBoneIndexSet.Contains(i))
                {
                    analysis.usedPartBoneCount++;
                }

                Side side = GetEffectiveSide(effectiveMirrorRoot, bone, out _, out _, out _);
                if (side == Side.Left)
                {
                    analysis.leftPartBoneCount++;
                    if (FindOppositeBone(effectiveMirrorRoot, bone, Side.Left, Side.Right, nameLookup, pathLookup) != null)
                    {
                        analysis.pairablePartBoneCount++;
                    }
                }
                else if (side == Side.Right)
                {
                    analysis.rightPartBoneCount++;
                    if (FindOppositeBone(effectiveMirrorRoot, bone, Side.Right, Side.Left, nameLookup, pathLookup) != null)
                    {
                        analysis.pairablePartBoneCount++;
                    }
                }
            }

            if (analysis.usedPartBoneCount == 0)
            {
                analysis.compatibilityReason = BuildCompatibilityFailure("Used bones do not include the selected Part.");
                return analysis;
            }

            if (analysis.leftPartBoneCount < GetMinimumPartSideBoneCount(config.part) || analysis.rightPartBoneCount < GetMinimumPartSideBoneCount(config.part))
            {
                analysis.compatibilityReason = BuildCompatibilityFailure("Expected both source/target side bones for the selected Part.");
                return analysis;
            }

            if (analysis.pairablePartBoneCount == 0)
            {
                analysis.compatibilityReason = BuildCompatibilityFailure("Selected Part bones do not have exact opposite-side candidates.");
                return analysis;
            }

            analysis.isPass = true;
            analysis.compatibilityReason = "MA-friendly prefab compatibility checks passed.";
            return analysis;
        }

        private static CompatibilityAnalysis FinalizeCompatibilityWithSourceTargetCoverage(
            Config config,
            ScanData scan,
            Transform effectiveMirrorRoot,
            CompatibilityAnalysis analysis,
            Side sourceSide,
            Side targetSide,
            Dictionary<string, List<Transform>> nameLookup,
            Dictionary<string, Transform> pathLookup)
        {
            for (int i = 0; i < scan.sourceBones.Length; i++)
            {
                if (scan.usedBoneIndexSet == null || !scan.usedBoneIndexSet.Contains(i))
                {
                    continue;
                }

                Transform bone = scan.sourceBones[i];
                if (bone == null || !IsPartBone(bone.name, config.part))
                {
                    continue;
                }

                Side side = GetEffectiveSide(effectiveMirrorRoot, bone, out _, out _, out _);
                if (side != sourceSide)
                {
                    continue;
                }

                analysis.sourceCandidateCount++;
                if (FindOppositeBone(effectiveMirrorRoot, bone, sourceSide, targetSide, nameLookup, pathLookup) != null)
                {
                    analysis.targetCandidateCount++;
                }
            }

            analysis.targetCoverage = analysis.sourceCandidateCount > 0 ? analysis.targetCandidateCount / (float)analysis.sourceCandidateCount : 0f;
            if (analysis.sourceCandidateCount == 0 || analysis.targetCandidateCount == 0 || analysis.targetCoverage < MinimumTargetCoverage)
            {
                analysis.isPass = false;
                analysis.compatibilityReason = BuildCompatibilityFailure("Expected a common armature/hips chain and both source/target side bones for selected Part.");
            }

            return analysis;
        }

        private static SourceSideAnalysis AnalyzeSourceSide(Config config, ScanData scan, Transform referenceRoot)
        {
            var probes = new List<SourceSideProbeEntry>();
            bool matchedSelectedPart = false;
            bool hasOutsideMirrorRootBones = false;

            for (int i = 0; i < scan.sourceBones.Length; i++)
            {
                if (scan.usedBoneIndexSet == null || !scan.usedBoneIndexSet.Contains(i))
                {
                    continue;
                }

                Transform bone = scan.sourceBones[i];
                bool isPartBone = bone != null && IsPartBone(bone.name, config.part);
                matchedSelectedPart |= isPartBone;

                Side finalSideUsed = GetEffectiveSide(referenceRoot, bone, out string relativePath, out bool isUnderMirrorRoot, out string absolutePath);
                Side sideFromName = bone != null ? DetectSide(bone.name) : Side.Unknown;
                Side sideFromRelativePath = isUnderMirrorRoot ? DetectSide(relativePath) : Side.Unknown;
                Side sideFromAbsolutePath = DetectSide(absolutePath);

                hasOutsideMirrorRootBones |= bone != null && !isUnderMirrorRoot;
                probes.Add(new SourceSideProbeEntry
                {
                    usedBoneIndex = i,
                    sourceBoneName = bone != null ? bone.name : "<null>",
                    relativePath = isUnderMirrorRoot ? relativePath : "<outside Mirror Root>",
                    absolutePath = absolutePath,
                    isUnderMirrorRoot = isUnderMirrorRoot,
                    isPartBone = isPartBone,
                    sideFromName = sideFromName,
                    sideFromRelativePath = sideFromRelativePath,
                    sideFromAbsolutePath = sideFromAbsolutePath,
                    finalSideUsed = finalSideUsed
                });
            }

            int leftCount = 0;
            int rightCount = 0;
            bool usePartFilter = matchedSelectedPart;
            for (int i = 0; i < probes.Count; i++)
            {
                SourceSideProbeEntry probe = probes[i];
                if (usePartFilter && !probe.isPartBone)
                {
                    continue;
                }

                if (probe.finalSideUsed == Side.Left)
                {
                    leftCount++;
                }
                else if (probe.finalSideUsed == Side.Right)
                {
                    rightCount++;
                }
            }

            Side autoDetected = Side.Unknown;
            if (leftCount > rightCount)
            {
                autoDetected = Side.Left;
            }
            else if (rightCount > leftCount)
            {
                autoDetected = Side.Right;
            }

            Side overrideSide = ConvertSourceSideModeToSide(config.sourceSideMode);
            SourceSideMode decisionMode = config.sourceSideMode == SourceSideMode.Auto ? SourceSideMode.Auto : config.sourceSideMode;
            Side finalDetected = config.sourceSideMode == SourceSideMode.Auto ? autoDetected : overrideSide;

            return new SourceSideAnalysis
            {
                probes = probes.ToArray(),
                leftCount = leftCount,
                rightCount = rightCount,
                matchedSelectedPart = matchedSelectedPart,
                hasOutsideMirrorRootBones = hasOutsideMirrorRootBones,
                decisionMode = decisionMode,
                overrideSide = overrideSide,
                autoDetectedSide = autoDetected,
                detectedSide = finalDetected
            };
        }

        private static void LogCompatibilityAnalysis(CompatibilityAnalysis analysis)
        {
            Log.Info($"prefabCompatibility={(analysis.isPass ? "Pass" : "Fail")}");
            Log.Info($"compatibilityReason={analysis.compatibilityReason}");
            Log.Info($"detectedArmatureRoot={GetAbsolutePath(analysis.detectedArmatureRoot)}");
            Log.Info($"detectedHips={GetAbsolutePath(analysis.detectedHips)}");
            Log.Info($"effectiveMirrorRoot={GetAbsolutePath(analysis.effectiveMirrorRoot)}");

            if (analysis.mirrorRootWasAutoSupplemented)
            {
                Log.Warn("Mirror Root was auto-supplemented from the detected armature/hips chain because the supplied Mirror Root was not suitable.");
            }
        }

        private static void LogCompatibilityCoverage(CompatibilityAnalysis analysis)
        {
            Log.Info($"compatibilityCoverage: usedPartBones={analysis.usedPartBoneCount} leftPartBones={analysis.leftPartBoneCount} rightPartBones={analysis.rightPartBoneCount} pairablePartBones={analysis.pairablePartBoneCount} sourceCandidates={analysis.sourceCandidateCount} targetCandidates={analysis.targetCandidateCount} targetCoverage={analysis.targetCoverage:0.00}");
        }

        private static void LogSourceSideDiagnostics(SourceSideAnalysis analysis)
        {
            Log.Warn("sourceSide diagnostics: detection failed.");
            Log.Warn($"sourceSideDecisionMode={GetSourceSideDecisionModeLabel(analysis)}");
            Log.Warn($"sourceSideOverride={(analysis.overrideSide == Side.Unknown ? "None" : analysis.overrideSide.ToString())}");
            Log.Warn($"autoDetectedLeftCount={analysis.leftCount}");
            Log.Warn($"autoDetectedRightCount={analysis.rightCount}");
            Log.Warn($"finalSourceSide={analysis.detectedSide}");
            Log.Warn($"finalTargetSide={GetOppositeSide(analysis.detectedSide)}");
            if (analysis.probes == null || analysis.probes.Length == 0)
            {
                Log.Warn("sourceSide diagnostics: no used bones were available for analysis.");
                return;
            }

            for (int i = 0; i < analysis.probes.Length; i++)
            {
                SourceSideProbeEntry probe = analysis.probes[i];
                Log.Warn($"sourceSide probe: usedBoneIndex={probe.usedBoneIndex} sourceBone.name={probe.sourceBoneName} relativePath={probe.relativePath} absolutePath={probe.absolutePath} isPartBone={probe.isPartBone} sideFromName={probe.sideFromName} sideFromRelativePath={probe.sideFromRelativePath} sideFromAbsolutePath={probe.sideFromAbsolutePath} finalSideUsed={probe.finalSideUsed}");
                if (!probe.isUnderMirrorRoot)
                {
                    Log.Warn($"sourceSide probe: relative path is outside effective Mirror Root. usedBoneIndex={probe.usedBoneIndex} sourceBone.name={probe.sourceBoneName}");
                }
            }

            Log.Warn($"sourceSide tally: matchedSelectedPart={analysis.matchedSelectedPart} leftCount={analysis.leftCount} rightCount={analysis.rightCount}");
            if (analysis.decisionMode == SourceSideMode.Auto && analysis.leftCount == analysis.rightCount && analysis.leftCount > 0)
            {
                Log.Warn("Auto sourceSide detection tied between left and right. Use Source Side Override.");
            }
        }

        private static void LogSourceSideDetails(SourceSideAnalysis analysis)
        {
            Log.Info($"sourceSideDecisionMode={GetSourceSideDecisionModeLabel(analysis)}");
            Log.Info($"sourceSideOverride={(analysis.overrideSide == Side.Unknown ? "None" : analysis.overrideSide.ToString())}");
            Log.Info($"autoDetectedLeftCount={analysis.leftCount}");
            Log.Info($"autoDetectedRightCount={analysis.rightCount}");
            Log.Info($"finalSourceSide={analysis.detectedSide}");
            Log.Info($"finalTargetSide={GetOppositeSide(analysis.detectedSide)}");

            if (analysis.probes == null)
            {
                return;
            }

            for (int i = 0; i < analysis.probes.Length; i++)
            {
                SourceSideProbeEntry probe = analysis.probes[i];
                Log.Info($"sourceSide probe: usedBoneIndex={probe.usedBoneIndex} sourceBone.name={probe.sourceBoneName} relativePath={probe.relativePath} absolutePath={probe.absolutePath} isPartBone={probe.isPartBone} sideFromName={probe.sideFromName} sideFromRelativePath={probe.sideFromRelativePath} sideFromAbsolutePath={probe.sideFromAbsolutePath} finalSideUsed={probe.finalSideUsed}");
            }

            Log.Info($"sourceSide tally: leftCount={analysis.leftCount} rightCount={analysis.rightCount}");
        }

        private static Side ConvertSourceSideModeToSide(SourceSideMode sourceSideMode)
        {
            switch (sourceSideMode)
            {
                case SourceSideMode.Left:
                    return Side.Left;
                case SourceSideMode.Right:
                    return Side.Right;
                default:
                    return Side.Unknown;
            }
        }

        private static Side GetOppositeSide(Side side)
        {
            if (side == Side.Left)
            {
                return Side.Right;
            }

            if (side == Side.Right)
            {
                return Side.Left;
            }

            return Side.Unknown;
        }

        private static string GetSourceSideDecisionModeLabel(SourceSideAnalysis analysis)
        {
            return analysis.overrideSide == Side.Unknown ? "Auto" : "Override";
        }

        private static Dictionary<string, List<Transform>> BuildNameLookup(Transform[] transforms)
        {
            var lookup = new Dictionary<string, List<Transform>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform current = transforms[i];
                if (!lookup.TryGetValue(current.name, out List<Transform> list))
                {
                    list = new List<Transform>();
                    lookup[current.name] = list;
                }

                list.Add(current);
            }

            return lookup;
        }

        private static Dictionary<string, Transform> BuildPathLookup(Transform root, Transform[] transforms)
        {
            var lookup = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (!TryGetRelativePath(root, transforms[i], out string path))
                {
                    continue;
                }

                if (!lookup.ContainsKey(path))
                {
                    lookup.Add(path, transforms[i]);
                }
            }

            return lookup;
        }

        private static Transform ResolveRootBone(
            Transform effectiveMirrorRoot,
            Transform sourceRootBone,
            Side sourceSide,
            Side targetSide,
            Dictionary<string, List<Transform>> nameLookup,
            Dictionary<string, Transform> pathLookup)
        {
            if (sourceRootBone == null)
            {
                return null;
            }

            Side rootSide = GetEffectiveSide(effectiveMirrorRoot, sourceRootBone, out _, out _, out _);
            if (rootSide == sourceSide)
            {
                Transform mappedRootBone = FindOppositeBone(effectiveMirrorRoot, sourceRootBone, sourceSide, targetSide, nameLookup, pathLookup);
                if (mappedRootBone != null)
                {
                    return mappedRootBone;
                }
            }

            return sourceRootBone;
        }

        private static Transform FindOppositeBone(
            Transform effectiveMirrorRoot,
            Transform sourceBone,
            Side sourceSide,
            Side targetSide,
            Dictionary<string, List<Transform>> nameLookup,
            Dictionary<string, Transform> pathLookup)
        {
            if (sourceBone == null)
            {
                return null;
            }

            if (TryGetRelativePath(effectiveMirrorRoot, sourceBone, out string sourcePath))
            {
                var pathCandidates = GenerateOppositeCandidates(sourcePath, sourceSide, targetSide);
                for (int i = 0; i < pathCandidates.Count; i++)
                {
                    if (pathLookup.TryGetValue(pathCandidates[i], out Transform pathMatch) && pathMatch != sourceBone)
                    {
                        return pathMatch;
                    }
                }
            }

            var candidates = new List<Transform>();
            var nameCandidates = GenerateOppositeCandidates(sourceBone.name, sourceSide, targetSide);
            for (int i = 0; i < nameCandidates.Count; i++)
            {
                if (!nameLookup.TryGetValue(nameCandidates[i], out List<Transform> namedBones))
                {
                    continue;
                }

                for (int j = 0; j < namedBones.Count; j++)
                {
                    Transform current = namedBones[j];
                    if (current != null && current != sourceBone && !candidates.Contains(current))
                    {
                        candidates.Add(current);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            Vector3 mirroredPosition = MirrorPoint(effectiveMirrorRoot, sourceBone.position);
            Transform best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                float distance = Vector3.SqrMagnitude(candidates[i].position - mirroredPosition);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidates[i];
                }
            }

            return best;
        }

        private static Side GetEffectiveSide(Transform referenceRoot, Transform target, out string relativePath, out bool isUnderReferenceRoot, out string absolutePath)
        {
            absolutePath = GetAbsolutePath(target);
            isUnderReferenceRoot = TryGetRelativePath(referenceRoot, target, out relativePath);
            Side sideFromRelativePath = isUnderReferenceRoot ? DetectSide(relativePath) : Side.Unknown;
            if (sideFromRelativePath != Side.Unknown)
            {
                return sideFromRelativePath;
            }

            Side sideFromName = target != null ? DetectSide(target.name) : Side.Unknown;
            if (sideFromName != Side.Unknown)
            {
                return sideFromName;
            }

            return DetectSide(absolutePath);
        }

        private static List<Transform> CollectNonNullBones(Transform[] bones)
        {
            var result = new List<Transform>();
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                {
                    result.Add(bones[i]);
                }
            }

            return result;
        }

        private static Transform FindCommonAncestor(List<Transform> bones)
        {
            if (bones == null || bones.Count == 0)
            {
                return null;
            }

            Transform candidate = bones[0];
            while (candidate != null)
            {
                bool isCommon = true;
                for (int i = 1; i < bones.Count; i++)
                {
                    if (!IsAncestorOrSelf(candidate, bones[i]))
                    {
                        isCommon = false;
                        break;
                    }
                }

                if (isCommon)
                {
                    return candidate;
                }

                candidate = candidate.parent;
            }

            return null;
        }

        private static Transform FindDetectedHips(ScanData scan, Transform commonAncestor)
        {
            Transform best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < scan.sourceBones.Length; i++)
            {
                Transform bone = scan.sourceBones[i];
                if (bone == null)
                {
                    continue;
                }

                int score = GetHipsScore(bone.name);
                if (score <= 0)
                {
                    continue;
                }

                if (commonAncestor != null && !IsAncestorOrSelf(commonAncestor, bone))
                {
                    continue;
                }

                score -= GetDepthBelow(commonAncestor, bone);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = bone;
                }
            }

            return best;
        }

        private static int GetHipsScore(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return 0;
            }

            if (string.Equals(name, "Hips", StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            if (name.Contains("Hips", StringComparison.OrdinalIgnoreCase))
            {
                return 80;
            }

            if (string.Equals(name, "Hip", StringComparison.OrdinalIgnoreCase))
            {
                return 70;
            }

            if (name.Contains("Hip", StringComparison.OrdinalIgnoreCase))
            {
                return 50;
            }

            return 0;
        }

        private static Transform DetermineArmatureRoot(Transform commonAncestor, Transform detectedHips)
        {
            if (detectedHips != null && detectedHips.parent != null && detectedHips.parent.name.Contains("Armature", StringComparison.OrdinalIgnoreCase))
            {
                return detectedHips.parent;
            }

            if (commonAncestor != null)
            {
                return commonAncestor;
            }

            return detectedHips != null ? detectedHips.parent : null;
        }

        private static Transform DetermineEffectiveMirrorRoot(Transform suppliedMirrorRoot, Transform detectedArmatureRoot, Transform detectedHips, List<Transform> bones, out bool mirrorRootWasAutoSupplemented)
        {
            mirrorRootWasAutoSupplemented = false;

            if (IsReasonableMirrorRoot(suppliedMirrorRoot, detectedHips, bones))
            {
                return suppliedMirrorRoot;
            }

            mirrorRootWasAutoSupplemented = suppliedMirrorRoot != null;
            if (detectedArmatureRoot != null)
            {
                return detectedArmatureRoot;
            }

            if (detectedHips != null && detectedHips.parent != null)
            {
                return detectedHips.parent;
            }

            return suppliedMirrorRoot;
        }

        private static bool IsReasonableMirrorRoot(Transform candidate, Transform detectedHips, List<Transform> bones)
        {
            if (candidate == null || bones == null || bones.Count == 0)
            {
                return false;
            }

            int descendantCount = 0;
            for (int i = 0; i < bones.Count; i++)
            {
                if (IsAncestorOrSelf(candidate, bones[i]))
                {
                    descendantCount++;
                }
            }

            bool coversMostBones = descendantCount >= Mathf.CeilToInt(bones.Count * 0.6f);
            bool coversHips = detectedHips == null || IsAncestorOrSelf(candidate, detectedHips);
            return coversMostBones && coversHips;
        }

        private static bool IsAncestorOrSelf(Transform ancestor, Transform target)
        {
            if (ancestor == null || target == null)
            {
                return false;
            }

            Transform current = target;
            while (current != null)
            {
                if (current == ancestor)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static int GetDepthBelow(Transform ancestor, Transform target)
        {
            if (ancestor == null || target == null)
            {
                return int.MaxValue / 4;
            }

            int depth = 0;
            Transform current = target;
            while (current != null && current != ancestor)
            {
                depth++;
                current = current.parent;
            }

            return current == ancestor ? depth : int.MaxValue / 4;
        }

        private static string BuildCompatibilityFailure(string detail)
        {
            return "This prefab does not appear to be MA-friendly for Merge Armature. Setup Outfit-style bone identification failed. Expected a common armature/hips chain and both source/target side bones for selected Part. " + detail;
        }

        private static int GetMinimumPartSideBoneCount(Part part)
        {
            switch (part)
            {
                case Part.Arm:
                case Part.Leg:
                    return 2;
                case Part.Hand:
                case Part.Foot:
                default:
                    return 1;
            }
        }

        private static List<string> GenerateOppositeCandidates(string value, Side sourceSide, Side targetSide)
        {
            return SideTokenUtility.GenerateOppositeCandidates(value, sourceSide, targetSide);
        }

        private static bool IsPartBone(string boneName, Part part)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return false;
            }

            string name = boneName.ToLowerInvariant();
            bool isFinger = name.Contains("thumb") || name.Contains("index") || name.Contains("middle") || name.Contains("ring") || name.Contains("little") || name.Contains("pinky") || name.Contains("finger") || name.Contains("指");
            bool isHand = name.Contains("hand") || name.Contains("wrist") || name.Contains("手");
            bool isArm = name.Contains("shoulder") || name.Contains("upperarm") || name.Contains("lowerarm") || name.Contains("forearm") || name.Contains("arm") || name.Contains("肘") || name.Contains("腕");
            bool isFoot = name.Contains("foot") || name.Contains("ankle") || name.Contains("足首") || name.Contains("つま先");
            bool isToe = name.Contains("toe") || name.Contains("toes") || name.Contains("toe_") || name.Contains("指先");
            bool isLeg = name.Contains("upperleg") || name.Contains("lowerleg") || name.Contains("thigh") || name.Contains("calf") || name.Contains("leg") || name.Contains("膝") || name.Contains("脚");

            switch (part)
            {
                case Part.Hand:
                    return isHand || isFinger;
                case Part.Arm:
                    return isArm || isHand || isFinger;
                case Part.Foot:
                    return isFoot || isToe;
                case Part.Leg:
                    return isLeg || isFoot || isToe;
                default:
                    return false;
            }
        }

        private static Side DetectSide(string text)
        {
            return SideTokenUtility.DetectSide(text);
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

        private static string GetRelativePath(Transform root, Transform target)
        {
            return TryGetRelativePath(root, target, out string relativePath) ? relativePath : target != null ? target.name : string.Empty;
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

        private static Vector3 MirrorPoint(Transform mirrorRoot, Vector3 worldPoint)
        {
            Vector3 local = mirrorRoot.InverseTransformPoint(worldPoint);
            local.x = -local.x;
            return mirrorRoot.TransformPoint(local);
        }
    }
}
