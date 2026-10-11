using UnityEngine;

namespace HandPoseRecognition
{
    /// <summary>Supplies world vertical and the headset's horizontal forward in raw tracking coordinates.</summary>
    public sealed class GestureReferenceProvider : MonoBehaviour
    {
        [SerializeField, Tooltip("The same Camera Offset used by QuestHandAdapter; raw hand positions are local to this space.")]
        Transform m_TrackingSpace;
        [SerializeField, Tooltip("Tracked XR camera. Its heading is sampled only when each candidate/recording freezes its reference.")]
        Transform m_Head;
        ulong m_Epoch = 1;
        Transform m_LastSpace;
        Matrix4x4 m_LastMatrix;
        bool m_HasMatrix;
        public Transform TrackingSpace { get => m_TrackingSpace; set { if (m_TrackingSpace != value) { m_TrackingSpace = value; ResetCoordinates(); } } }
        public Transform Head { get => m_Head; set { if (m_Head != value) { m_Head = value; ResetCoordinates(); } } }
        public ulong CoordinateEpoch { get { InspectRig(); return m_Epoch; } }
        public string Reason { get; private set; }

        [ContextMenu("Reset coordinates after recenter / teleport")]
        public void ResetCoordinates() { m_Epoch = checked(m_Epoch + 1); m_HasMatrix = false; }
        void OnEnable() => ResetCoordinates();
        void OnDisable() => ResetCoordinates();
        void InspectRig()
        {
            if (m_TrackingSpace == null) { if (m_HasMatrix) ResetCoordinates(); return; }
            var matrix = m_TrackingSpace.localToWorldMatrix;
            if (m_HasMatrix && (m_LastSpace != m_TrackingSpace || !SameMatrix(matrix, m_LastMatrix))) ResetCoordinates();
            m_LastMatrix = matrix; m_LastSpace = m_TrackingSpace; m_HasMatrix = true;
        }
        static bool SameMatrix(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++) if (!PoseMath.IsFinite(a[i]) || Mathf.Abs(a[i] - b[i]) > 1e-5f) return false;
            return true;
        }
        public bool TryGetCalibration(out GestureCalibration calibration, out string reason)
        {
            calibration = null; InspectRig();
            if (!isActiveAndEnabled || m_TrackingSpace == null || m_Head == null)
            { reason = Reason = "Assign an enabled reference provider, Camera Offset and XR camera."; return false; }
            var scale = m_TrackingSpace.lossyScale;
            var x = (Vector3)m_LastMatrix.GetColumn(0); var y = (Vector3)m_LastMatrix.GetColumn(1); var z = (Vector3)m_LastMatrix.GetColumn(2);
            if (!PoseMath.IsFinite(x) || !PoseMath.IsFinite(y) || !PoseMath.IsFinite(z) || Vector3.Dot(Vector3.Cross(x, y), z) <= 0
                || Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > 1e-4f
                || Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) > 1e-4f || Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) > 1e-4f)
            { reason = Reason = "Rig transform must have positive uniform scale without shear or reflection."; return false; }
            var inverse = Quaternion.Inverse(m_TrackingSpace.rotation);
            calibration = new GestureCalibration(inverse * Vector3.up, inverse * m_Head.forward, scale, m_Epoch);
            if (!calibration.TryGetBasis(out _, out reason)) { Reason = reason; calibration = null; return false; }
            Reason = null; return true;
        }
    }
}
