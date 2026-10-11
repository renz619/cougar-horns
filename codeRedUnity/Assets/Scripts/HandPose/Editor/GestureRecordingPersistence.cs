using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HandPoseRecognition.Editor
{
    /// <summary>Explicit complete-clip authoring with prospective validation before any asset mutation.</summary>
    public static class GestureRecordingPersistence
    {
        public static bool TrySave(GestureRecordingReview review, string newPath, GestureData destination,
            RecordingSaveMode mode, GestureCatalog catalog, out GestureData saved, out string error)
        {
            saved = null; error = null;
            if (review == null) { error = "Review: explicitly select a complete reviewed clip."; return false; }
            if (!review.TryValidate(out _, out error)) return false;
            if (!Enum.IsDefined(typeof(RecordingSaveMode), mode)) { error = "Save/Mode: unsupported action."; return false; }
            string catalogPath = catalog == null ? null : AssetDatabase.GetAssetPath(catalog);
            if (catalog != null && (string.IsNullOrEmpty(catalogPath) || Protected(catalogPath)))
            { error = "Catalog/Path: choose a persistent non-synthetic gesture catalog asset."; return false; }
            var clip = review.CopyClip(); var definition = clip.Definition;
            var clips = new List<GestureClip>(); string path;
            if (mode == RecordingSaveMode.Create)
            {
                if (destination != null || !ValidNewPath(newPath)) { error = "Save/Path: choose an existing non-synthetic Assets/HandGestures folder and .asset filename."; return false; }
                path = AssetDatabase.GenerateUniqueAssetPath(newPath);
            }
            else
            {
                path = destination == null ? null : AssetDatabase.GetAssetPath(destination);
                if (string.IsNullOrEmpty(path) || Protected(path) || !path.StartsWith("Assets/HandGestures/", StringComparison.Ordinal))
                { error = "Save/Destination: choose a persistent recorded gesture asset."; return false; }
                if (!destination.TryValidate(out var oldReport)) { error = oldReport.ToString(); return false; }
                if (!GestureData.DefinitionsAgree(destination.Definition, definition))
                { error = "Save/Destination: identity, versions, side/roles, features, provenance and settings must match the frozen definition."; return false; }
                if (mode == RecordingSaveMode.Append) clips.AddRange(destination.CopyClips());
            }
            clips.Add(clip);
            var proposal = ScriptableObject.CreateInstance<GestureData>();
            var prospective = ScriptableObject.CreateInstance<GestureCatalog>();
            int group = -1; bool created = false;
            try
            {
                proposal.name = path; proposal.SetDefinition(definition, clips.ToArray());
                if (!proposal.TryValidate(out var report)) { error = report.ToString(); return false; }
                GestureData[] references = null;
                if (catalog != null)
                {
                    if (catalog.SchemaVersion != 1) { error = catalogPath + "/SchemaVersion: unsupported."; return false; }
                    var current = catalog.CopyEntries();
                    if (current == null) { error = catalogPath + "/Entries: missing reference array."; return false; }
                    var list = new List<GestureData>(current); bool replaced = false;
                    for (int i = 0; i < list.Count; i++) if (destination != null && list[i] == destination) { list[i] = proposal; replaced = true; }
                    if (!replaced) list.Add(proposal);
                    references = list.ToArray(); prospective.SetEntries(references, catalog.MaxEntries, catalog.MaxTotalClips);
                    if (!prospective.TryBuildSnapshot(out _, out report)) { error = catalogPath + ": " + report; return false; }
                }
                Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Save reviewed complete gesture");
                if (mode == RecordingSaveMode.Create)
                {
                    destination = ScriptableObject.CreateInstance<GestureData>(); destination.SetDefinition(definition, clips.ToArray());
                    AssetDatabase.CreateAsset(destination, path); created = true;
                    Undo.RegisterCreatedObjectUndo(destination, "Create complete gesture asset");
                }
                else { Undo.RecordObject(destination, "Update reviewed gesture clips"); destination.SetDefinition(definition, clips.ToArray()); EditorUtility.SetDirty(destination); }
                if (catalog != null)
                {
                    for (int i = 0; i < references.Length; i++) if (references[i] == proposal) references[i] = destination;
                    Undo.RecordObject(catalog, "Update gesture catalog"); catalog.SetEntries(references, catalog.MaxEntries, catalog.MaxTotalClips); EditorUtility.SetDirty(catalog);
                }
                AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                saved = AssetDatabase.LoadAssetAtPath<GestureData>(path);
                if (saved == null) throw new InvalidOperationException(path + ": saved asset could not be reloaded.");
                if (!saved.TryValidate(out report)) throw new InvalidOperationException(path + ": " + report);
                if (catalog != null)
                {
                    AssetDatabase.ImportAsset(catalogPath, ImportAssetOptions.ForceUpdate);
                    var reloaded = AssetDatabase.LoadAssetAtPath<GestureCatalog>(catalogPath);
                    if (reloaded == null) throw new InvalidOperationException(catalogPath + ": catalog could not be reloaded.");
                    if (!reloaded.TryBuildSnapshot(out _, out report)) throw new InvalidOperationException(catalogPath + ": " + report);
                }
                Undo.CollapseUndoOperations(group); return true;
            }
            catch (Exception exception)
            {
                saved = null; error = path + ": " + exception.Message;
                if (group >= 0) Undo.RevertAllDownToGroup(group);
                if (created && AssetDatabase.LoadAssetAtPath<GestureData>(path) != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.SaveAssets(); return false;
            }
            finally { UnityEngine.Object.DestroyImmediate(proposal); UnityEngine.Object.DestroyImmediate(prospective); }
        }
        static bool Protected(string path) => path.IndexOf("/Synthetic/", StringComparison.OrdinalIgnoreCase) >= 0;
        static bool ValidNewPath(string path) => !string.IsNullOrWhiteSpace(path) && path.StartsWith("Assets/HandGestures/", StringComparison.Ordinal)
            && path.EndsWith(".asset", StringComparison.Ordinal) && !path.Contains("..") && !path.Contains("\\") && !Protected(path)
            && AssetDatabase.IsValidFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
    }

    public static class GestureReviewRetention
    {
        public static void Store(string key, GestureRecordingReview review) => SessionState.SetString(key, JsonUtility.ToJson(review));
        public static GestureRecordingReview Restore(string key)
        {
            string json = SessionState.GetString(key, "");
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<GestureRecordingReview>(json);
        }
        public static void Clear(string key) => SessionState.EraseString(key);
    }
}
