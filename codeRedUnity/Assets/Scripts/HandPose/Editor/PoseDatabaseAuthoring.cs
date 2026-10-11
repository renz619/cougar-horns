using System;
using UnityEditor;
using UnityEngine;

namespace HandPoseRecognition.Editor
{
    public static class PoseDatabaseAuthoring
    {
        public const string SyntheticFolder = "Assets/HandPoses/Synthetic";

        /// <summary>Clearly synthetic geometry exercises serialization; it is not a trained ASL template.</summary>
        public static bool TryCreateSyntheticSample(out PoseSample sample, out string error)
        {
            sample = null;
            var joints = HandFrame.CreateUnavailableJoints();
            for (int i = 0; i < joints.Length; i++)
                joints[i] = new JointPose((HandJointId)i, new Vector3(i * .003f, .07f + i * .001f, .002f), Quaternion.identity, true, true);
            joints[0].Position = Vector3.zero;
            joints[7].Position = new Vector3(.04f, .05f, 0);
            joints[12].Position = new Vector3(0, .06f, 0);
            joints[22].Position = new Vector3(-.04f, .05f, 0);
            var raw = new HandFrame(0, 0, HandSide.Right, true, HandFrameSpace.TrackingSpace, joints);
            if (!HandPoseNormalizer.TryNormalizeFrame(raw, out var canonical, out error)) return false;
            if (!PoseSample.TryCreate(canonical, HandPoseData.CreateDefaultWeights(), out sample, out var report))
            { error = report.ToString(); return false; }
            error = null;
            return true;
        }

        [MenuItem("Tools/Hand Pose/Create Synthetic Catalog")]
        public static void CreateSyntheticCatalog()
        {
            if (!TryCreateSyntheticSample(out var sample, out var reason)) throw new InvalidOperationException(reason);
            if (!AssetDatabase.IsValidFolder("Assets/HandPoses")) AssetDatabase.CreateFolder("Assets", "HandPoses");
            if (!AssetDatabase.IsValidFolder(SyntheticFolder)) AssetDatabase.CreateFolder("Assets/HandPoses", "Synthetic");
            var template = ScriptableObject.CreateInstance<HandPoseData>();
            var database = ScriptableObject.CreateInstance<PoseDatabase>();
            // Fixed ID for the initial fixture; repeated menu calls create independent catalogs with unique IDs.
            string templatePath = AssetDatabase.GenerateUniqueAssetPath(SyntheticFolder + "/SyntheticRight.asset");
            string databasePath = AssetDatabase.GenerateUniqueAssetPath(SyntheticFolder + "/SyntheticDatabase.asset");
            string id = AssetDatabase.FindAssets("t:HandPoseData", new[] { SyntheticFolder }).Length == 0
                ? "synthetic.fixture.right" : "synthetic.fixture.right." + Guid.NewGuid().ToString("N");
            template.SetDefinition(id, "SYNTHETIC right-hand fixture (not ASL training data)", HandSide.Right,
                new[] { sample }, HandPoseData.CreateDefaultWeights(), .1f);
            database.SetTemplates(new[] { template });
            if (!database.TryBuildSnapshot(out _, out var report)) throw new InvalidOperationException(report.ToString());
            AssetDatabase.CreateAsset(template, templatePath);
            AssetDatabase.CreateAsset(database, databasePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(templatePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<PoseDatabase>(databasePath);
            if (!reloaded.TryBuildSnapshot(out var snapshot, out report)) throw new InvalidOperationException(report.ToString());
            Debug.Log($"Synthetic catalog saved/reloaded: {databasePath}; {snapshot.PoseCount} pose, {snapshot.SampleCount} sample, Right; schema/layout/normalization 1. Not validated ASL data.");
        }

        [MenuItem("Tools/Hand Pose/Validate Selected Catalog")]
        public static void ValidateSelectedCatalog()
        {
            if (Selection.activeObject is PoseDatabase database) Validate(database);
            else Debug.LogWarning("Select a PoseDatabase asset first.");
        }

        internal static void Validate(PoseDatabase database)
        {
            if (!database.TryBuildSnapshot(out var snapshot, out var report))
            { Debug.LogError(report.ToString(), database); return; }
            Debug.Log($"Valid catalog: {snapshot.PoseCount} poses, {snapshot.SampleCount} samples; Left {snapshot.GetCandidates(HandSide.Left).Count}, Right {snapshot.GetCandidates(HandSide.Right).Count}; schema/layout/normalization 1.", database);
        }
    }

    [CustomEditor(typeof(PoseDatabase))]
    public sealed class PoseDatabaseInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("Build and validate runtime snapshot")) PoseDatabaseAuthoring.Validate((PoseDatabase)target);
        }
    }
}
