using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sebanne.SkinnedMeshMirror.Editor
{
    public enum Part
    {
        Hand,
        Arm,
        Foot,
        Leg
    }

    public enum MappingMode
    {
        Auto,
        PrefabLocalMirror,
        AnimatorAssist
    }

    public enum OutputMode
    {
        MirroredRendererOnly,
        DuplicateOutfitAndReplaceTarget
    }

    public enum Side
    {
        Unknown,
        Left,
        Right
    }

    public enum SourceSideMode
    {
        Auto,
        Left,
        Right
    }

    public enum DiagnosticSeverity
    {
        Error,
        Warning,
        Info
    }

    [Serializable]
    public struct Config
    {
        public SkinnedMeshRenderer sourceRenderer;
        public Transform mirrorRoot;
        public Transform outfitRoot;
        public Part part;
        public MappingMode mappingMode;
        public OutputMode outputMode;
        public Animator optionalAnimator;
        public string outputFolder;
        public string fileNameSuffix;
        public SourceSideMode sourceSideMode;
        public bool dryRun;
        public bool verboseLog;
    }

    public struct ScanData
    {
        public Mesh sharedMesh;
        public Transform[] sourceBones;
        public Transform sourceRootBone;
        public HashSet<int> usedBoneIndexSet;
        public int vertices;
        public int bones;
        public int usedBones;
        public int blendShapeCount;
    }

    [Serializable]
    public struct DiagnosticEntry
    {
        public DiagnosticSeverity severity;
        public string code;
        public string title;
        public string message;
        public string suggestion;
    }

    public struct BoneMappingEntry
    {
        public int index;
        public Transform sourceBone;
        public Transform mappedBone;
        public bool isUsed;
        public bool isPartBone;
        public bool attemptedMirror;
        public bool mirrored;
        public bool kept;
        public bool missing;
        public string detail;
    }

    public struct SourceSideProbeEntry
    {
        public int usedBoneIndex;
        public string sourceBoneName;
        public string relativePath;
        public string absolutePath;
        public bool isUnderMirrorRoot;
        public bool isPartBone;
        public Side sideFromName;
        public Side sideFromRelativePath;
        public Side sideFromAbsolutePath;
        public Side finalSideUsed;
    }

    public struct SourceSideAnalysis
    {
        public SourceSideProbeEntry[] probes;
        public int leftCount;
        public int rightCount;
        public bool matchedSelectedPart;
        public bool hasOutsideMirrorRootBones;
        public SourceSideMode decisionMode;
        public Side overrideSide;
        public Side autoDetectedSide;
        public Side detectedSide;
    }

    public struct CompatibilityAnalysis
    {
        public bool isPass;
        public string compatibilityReason;
        public Transform detectedArmatureRoot;
        public Transform detectedHips;
        public Transform effectiveMirrorRoot;
        public bool mirrorRootWasAutoSupplemented;
        public int usedPartBoneCount;
        public int leftPartBoneCount;
        public int rightPartBoneCount;
        public int pairablePartBoneCount;
        public int sourceCandidateCount;
        public int targetCandidateCount;
        public float targetCoverage;
    }

    public struct MapData
    {
        public Side sourceSide;
        public Side targetSide;
        public CompatibilityAnalysis compatibilityAnalysis;
        public SourceSideAnalysis sourceSideAnalysis;
        public Transform[] mappedBones;
        public Transform mappedRootBone;
        public BoneMappingEntry[] entries;
        public List<string> missingBoneNames;
        public int mirroredBones;
        public int keptBones;
        public int missingBones;
        public int nullBoneSlots;
        public int missingUsedBoneMappings;
    }

    public struct Result
    {
        public bool success;
        public string failureReason;
        public List<DiagnosticEntry> diagnostics;
        public int errorCount;
        public int warningCount;
        public int infoCount;
        public MappingMode mappingMode;
        public OutputMode outputMode;
        public Side sourceSide;
        public Side targetSide;
        public string sourceSideDecisionMode;
        public string sourceSideOverride;
        public bool prefabCompatibilityPass;
        public string compatibilityReason;
        public string detectedArmatureRootPath;
        public string detectedHipsPath;
        public string effectiveMirrorRootPath;
        public int autoDetectedLeftCount;
        public int autoDetectedRightCount;
        public int sourceSideLeftCount;
        public int sourceSideRightCount;
        public int vertices;
        public int bones;
        public int usedBones;
        public int blendShapeCount;
        public int mirroredBones;
        public int keptBones;
        public int missingBones;
        public int nullBoneSlots;
        public int missingUsedBoneMappings;
        public int sourceVertexCount;
        public int filteredOutVertexCount;
        public int keptTriangleCount;
        public int filteredOutTriangleCount;
        public string sourceSideVertexSelectionMode;
        public int flippedTriangles;
        public int bindposesRebuilt;
        public string meshAssetPath;
        public string objectName;
        public string plannedMeshAssetPath;
        public string plannedObjectName;
        public List<string> missingBoneNames;
    }

    public static class Log
    {
        private const string Prefix = "[SkinnedMeshMirror] ";

        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            Debug.LogError(Prefix + message);
        }

        public static void Verbose(bool enabled, string message)
        {
            if (enabled)
            {
                Info(message);
            }
        }
    }

    internal static class SideTokenUtility
    {
        private static readonly string[] LeftSuffixTokens = { ".L", ".l", "_L", "_l" };
        private static readonly string[] RightSuffixTokens = { ".R", ".r", "_R", "_r" };
        private static readonly string[] LeftPrefixTokens = { "L_", "l_" };
        private static readonly string[] RightPrefixTokens = { "R_", "r_" };
        private static readonly string[] LeftWordTokens = { "Left", "left", "LEFT", "左" };
        private static readonly string[] RightWordTokens = { "Right", "right", "RIGHT", "右" };

        public static Side DetectSide(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return Side.Unknown;
            }

            string[] segments = SplitSegments(value);
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                Side segmentSide = DetectSideFromSegment(segments[i]);
                if (segmentSide != Side.Unknown)
                {
                    return segmentSide;
                }
            }

            return Side.Unknown;
        }

        public static List<string> GenerateOppositeCandidates(string value, Side fromSide, Side toSide)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(value) || fromSide == Side.Unknown || toSide == Side.Unknown)
            {
                return result;
            }

            string normalized = value.Replace("\\", "/");
            string[] segments = SplitSegments(normalized);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = segments.Length - 1; i >= 0; i--)
            {
                if (!TryReplaceSegmentSideToken(segments[i], fromSide, toSide, out string replacedSegment))
                {
                    continue;
                }

                string[] candidateSegments = (string[])segments.Clone();
                candidateSegments[i] = replacedSegment;
                string candidate = string.Join("/", candidateSegments);
                if (seen.Add(candidate))
                {
                    result.Add(candidate);
                }
            }

            if (segments.Length == 1 && result.Count == 0 && TryReplaceSegmentSideToken(normalized, fromSide, toSide, out string replacedValue))
            {
                if (seen.Add(replacedValue))
                {
                    result.Add(replacedValue);
                }
            }

            return result;
        }

        public static string ReplaceSideInName(string value, Side fromSide, Side toSide)
        {
            List<string> candidates = GenerateOppositeCandidates(value, fromSide, toSide);
            return candidates.Count > 0 ? candidates[0] : value;
        }

        private static Side DetectSideFromSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment))
            {
                return Side.Unknown;
            }

            bool hasLeft = HasSuffixToken(segment, LeftSuffixTokens) ||
                           HasPrefixToken(segment, LeftPrefixTokens) ||
                           ContainsDelimitedToken(segment, LeftWordTokens);

            bool hasRight = HasSuffixToken(segment, RightSuffixTokens) ||
                            HasPrefixToken(segment, RightPrefixTokens) ||
                            ContainsDelimitedToken(segment, RightWordTokens);

            if (hasLeft == hasRight)
            {
                return Side.Unknown;
            }

            return hasLeft ? Side.Left : Side.Right;
        }

        private static bool TryReplaceSegmentSideToken(string segment, Side fromSide, Side toSide, out string replacedSegment)
        {
            replacedSegment = segment;
            if (string.IsNullOrEmpty(segment) || fromSide == Side.Unknown || toSide == Side.Unknown)
            {
                return false;
            }

            string[] fromSuffixes = fromSide == Side.Left ? LeftSuffixTokens : RightSuffixTokens;
            string[] toSuffixes = toSide == Side.Left ? LeftSuffixTokens : RightSuffixTokens;
            string[] fromPrefixes = fromSide == Side.Left ? LeftPrefixTokens : RightPrefixTokens;
            string[] toPrefixes = toSide == Side.Left ? LeftPrefixTokens : RightPrefixTokens;
            string[] fromWords = fromSide == Side.Left ? LeftWordTokens : RightWordTokens;
            string[] toWords = toSide == Side.Left ? LeftWordTokens : RightWordTokens;

            for (int i = 0; i < fromSuffixes.Length; i++)
            {
                if (!segment.EndsWith(fromSuffixes[i], StringComparison.Ordinal))
                {
                    continue;
                }

                replacedSegment = segment.Substring(0, segment.Length - fromSuffixes[i].Length) + toSuffixes[i];
                return true;
            }

            for (int i = 0; i < fromPrefixes.Length; i++)
            {
                if (!segment.StartsWith(fromPrefixes[i], StringComparison.Ordinal))
                {
                    continue;
                }

                replacedSegment = toPrefixes[i] + segment.Substring(fromPrefixes[i].Length);
                return true;
            }

            for (int i = 0; i < fromWords.Length; i++)
            {
                if (!TryReplaceDelimitedToken(segment, fromWords[i], toWords[i], fromWords[i].Length == 1, out replacedSegment))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static string[] SplitSegments(string value)
        {
            return value.Replace("\\", "/").Split(new[] { '/' }, StringSplitOptions.None);
        }

        private static bool HasSuffixToken(string segment, string[] suffixTokens)
        {
            for (int i = 0; i < suffixTokens.Length; i++)
            {
                if (segment.EndsWith(suffixTokens[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasPrefixToken(string segment, string[] prefixTokens)
        {
            for (int i = 0; i < prefixTokens.Length; i++)
            {
                if (segment.StartsWith(prefixTokens[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsDelimitedToken(string segment, string[] tokens)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                if (FindDelimitedTokenIndex(segment, tokens[i], tokens[i].Length == 1) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReplaceDelimitedToken(string segment, string fromToken, string toToken, bool ordinal, out string replacedSegment)
        {
            replacedSegment = segment;
            int index = FindDelimitedTokenIndex(segment, fromToken, ordinal);
            if (index < 0)
            {
                return false;
            }

            replacedSegment = segment.Substring(0, index) + toToken + segment.Substring(index + fromToken.Length);
            return true;
        }

        private static int FindDelimitedTokenIndex(string segment, string token, bool ordinal)
        {
            if (string.IsNullOrEmpty(segment) || string.IsNullOrEmpty(token))
            {
                return -1;
            }

            StringComparison comparison = ordinal ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int startIndex = 0;
            int foundIndex = -1;

            while (startIndex < segment.Length)
            {
                int index = segment.IndexOf(token, startIndex, comparison);
                if (index < 0)
                {
                    break;
                }

                bool isStartBoundary = index == 0 || IsTokenBoundary(segment[index - 1]);
                int afterIndex = index + token.Length;
                bool isEndBoundary = afterIndex == segment.Length || IsTokenBoundary(segment[afterIndex]);
                if (isStartBoundary && isEndBoundary)
                {
                    foundIndex = index;
                }

                startIndex = index + 1;
            }

            return foundIndex;
        }

        private static bool IsTokenBoundary(char value)
        {
            return value == '.' || value == '_' || value == '-' || value == ' ';
        }
    }
}
