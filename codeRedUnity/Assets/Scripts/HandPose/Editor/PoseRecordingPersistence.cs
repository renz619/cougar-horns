using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HandPoseRecognition.Editor
{
    public enum RecordingSaveMode { Create, Replace, Append }

    /// <summary>Validate a complete proposal, including prospective catalog references, before any destination mutation.</summary>
    public static class PoseRecordingPersistence
    {
        public static bool TrySave(PoseRecordingReview review, int[] selectedIndices, string newPath,
            HandPoseData destination, RecordingSaveMode mode, PoseDatabase catalog,
            out HandPoseData saved, out string error)
        {
            saved = null; error = null;
            if (catalog != null && AssetDatabase.GetAssetPath(catalog).StartsWith("Assets/HandPoses/Synthetic/", StringComparison.Ordinal))
            { error = "Catalog/Path: supplied synthetic fixtures cannot be updated by recorded-sample saves."; return false; }
            if (review == null || selectedIndices == null || selectedIndices.Length == 0)
            { error = "Review/Selection: explicitly select at least one sample."; return false; }
            var definition = review.CopyDefinition();
            var candidates = review.CopySamples();
            if (definition == null || !definition.TryValidate(out error)) return false;
            var selected = new List<PoseSample>();
            var seen = new HashSet<int>();
            for (int i = 0; i < selectedIndices.Length; i++)
            {
                int index = selectedIndices[i];
                if (index < 0 || index >= candidates.Length || !seen.Add(index))
                { error = $"Review/Selection[{i}]: invalid or duplicate sample index {index}."; return false; }
                selected.Add(candidates[index]);
            }
            if (mode != RecordingSaveMode.Create && mode != RecordingSaveMode.Replace && mode != RecordingSaveMode.Append)
            { error = "Save/Mode: unsupported action."; return false; }
            if (mode == RecordingSaveMode.Create)
            {
                if (destination != null || string.IsNullOrEmpty(newPath) || !newPath.StartsWith("Assets/HandPoses/", StringComparison.Ordinal)
                    || newPath.Contains("..") || !newPath.EndsWith(".asset", StringComparison.Ordinal)
                    || !AssetDatabase.IsValidFolder(System.IO.Path.GetDirectoryName(newPath).Replace('\\', '/')))
                { error = "Save/Path: choose an existing Assets/HandPoses folder and an .asset filename."; return false; }
                newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);
            }
            else
            {
                string existingPath = destination == null ? null : AssetDatabase.GetAssetPath(destination);
                if (destination == null || string.IsNullOrEmpty(existingPath) || existingPath.StartsWith("Assets/HandPoses/Synthetic/", StringComparison.Ordinal))
                { error = "Save/Destination: choose an existing non-synthetic template asset."; return false; }
                if (!destination.TryValidate(out var destinationReport)) { error = destinationReport.ToString(); return false; }
                var weights = destination.CopyJointWeights();
                bool compatible = destination.PoseId == definition.PoseId && destination.HandSide == definition.Side
                    && destination.SchemaVersion == 1 && destination.JointLayoutVersion == 1 && destination.NormalizationVersion == 1
                    && destination.MaxSamplesPerPose == definition.MaxSamplesPerPose && destination.MaxError == definition.MaxError;
                for (int i = 0; i < weights.Length && compatible; i++) compatible &= weights[i] == definition.Weights[i];
                if (!compatible) { error = "Save/Destination: ID, side, versions, weights, threshold and budget must match the frozen definition."; return false; }
                if (mode == RecordingSaveMode.Append) selected.InsertRange(0, destination.CopySamples());
                definition.DisplayName = destination.DisplayName;
            }
            var proposal = ScriptableObject.CreateInstance<HandPoseData>();
            var prospective = ScriptableObject.CreateInstance<PoseDatabase>();
            try
            {
                proposal.name = mode == RecordingSaveMode.Create ? newPath : AssetDatabase.GetAssetPath(destination);
                proposal.SetDefinition(definition.PoseId, definition.DisplayName, definition.Side, selected.ToArray(),
                    definition.Weights, definition.MaxError, definition.MaxSamplesPerPose);
                if (!proposal.TryValidate(out var report)) { error = report.ToString(); return false; }
                HandPoseData[] references = null;
                if (catalog != null)
                {
                    if (catalog.SchemaVersion != 1) { error = "Catalog/SchemaVersion: only schema 1 supported."; return false; }
                    var previous = catalog.CopyTemplates();
                    if (previous == null) { error = "Catalog/Templates: null reference set."; return false; }
                    var list = new List<HandPoseData>(previous);
                    bool replaced = false;
                    for (int i = 0; i < list.Count; i++)
                        if (destination != null && list[i] == destination) { list[i] = proposal; replaced = true; }
                    if (!replaced) list.Add(proposal);
                    prospective.SetTemplates(list.ToArray());
                    if (!prospective.TryBuildSnapshot(out _, out report)) { error = report.ToString(); return false; }
                    references = list.ToArray();
                }
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Save reviewed hand pose");
                if (mode == RecordingSaveMode.Create)
                {
                    destination = ScriptableObject.CreateInstance<HandPoseData>();
                    destination.SetDefinition(definition.PoseId, definition.DisplayName, definition.Side, selected.ToArray(),
                        definition.Weights, definition.MaxError, definition.MaxSamplesPerPose);
                    AssetDatabase.CreateAsset(destination, newPath);
                    Undo.RegisterCreatedObjectUndo(destination, "Create reviewed hand pose");
                }
                else
                {
                    Undo.RecordObject(destination, "Update reviewed hand pose");
                    destination.SetDefinition(definition.PoseId, definition.DisplayName, definition.Side, selected.ToArray(),
                        definition.Weights, definition.MaxError, definition.MaxSamplesPerPose);
                    EditorUtility.SetDirty(destination);
                }
                if (catalog != null)
                {
                    for (int i = 0; i < references.Length; i++) if (references[i] == proposal) references[i] = destination;
                    Undo.RecordObject(catalog, "Assign reviewed hand pose catalog");
                    catalog.SetTemplates(references); EditorUtility.SetDirty(catalog);
                }
                Undo.CollapseUndoOperations(group);
                AssetDatabase.SaveAssets();
                string path = AssetDatabase.GetAssetPath(destination);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                saved = AssetDatabase.LoadAssetAtPath<HandPoseData>(path);
                if (!saved.TryValidate(out report)) { error = report.ToString(); return false; }
                if (catalog != null)
                {
                    string catalogPath = AssetDatabase.GetAssetPath(catalog);
                    if (!string.IsNullOrEmpty(catalogPath)) AssetDatabase.ImportAsset(catalogPath, ImportAssetOptions.ForceUpdate);
                    if (!catalog.TryBuildSnapshot(out _, out report)) { error = report.ToString(); return false; }
                }
                return true;
            }
            finally { UnityEngine.Object.DestroyImmediate(proposal); UnityEngine.Object.DestroyImmediate(prospective); }
        }
    }

    /// <summary>SessionState survives domain reload and Play Mode exit, but not closing the Editor. No template writes.</summary>
    public static class PoseReviewRetention
    {
        public static void Store(string key, PoseRecordingReview review) => SessionState.SetString(key, JsonUtility.ToJson(review));
        public static PoseRecordingReview Restore(string key)
        {
            string json = SessionState.GetString(key, "");
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<PoseRecordingReview>(json);
        }
        public static void Clear(string key) => SessionState.EraseString(key);
    }
}
