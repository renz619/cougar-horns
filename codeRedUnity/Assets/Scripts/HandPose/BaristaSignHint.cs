using UnityEngine;

namespace HandPoseRecognition
{
    public enum BaristaHintSign { One, Two, Three, Yes, Coffee, Tea, Please, ThankYou }

    /// <summary>Looping, original line-art hand diagrams for the current instruction.</summary>
    public sealed class BaristaSignHint : MonoBehaviour
    {
        [SerializeField] BaristaHintSign m_Sign;
        [SerializeField] Material m_Material;
        [SerializeField] LineRenderer[] m_Right;
        [SerializeField] LineRenderer[] m_Left;
        [SerializeField] LineRenderer m_Arrow;
        [SerializeField] LineRenderer m_CameraIcon;
        public BaristaHintSign Sign { get => m_Sign; set => m_Sign = value; }
        public Material Material { get => m_Material; set => m_Material = value; }

        public void Initialize()
        {
            if (m_Right == null || m_Right.Length != 6) m_Right = MakeHand("Right", Color.cyan);
            if (m_Left == null || m_Left.Length != 6) m_Left = MakeHand("Left", new Color(1, .72f, .35f));
            if (m_Arrow == null) m_Arrow = MakeLine("Motion path", new Color(.5f, .85f, .8f, .6f));
            if (m_CameraIcon == null) m_CameraIcon = MakeLine("Camera reference", new Color(.9f, .95f, .9f));
            // LineRenderer widths use world meters even under a scaled canvas.
            foreach (var line in m_Right) line.startWidth = line.endWidth = .012f;
            foreach (var line in m_Left) line.startWidth = line.endWidth = .012f;
            m_Arrow.startWidth = m_Arrow.endWidth = .008f;
            m_CameraIcon.startWidth = m_CameraIcon.endWidth = .008f;
            Render(0);
        }
        void Start() => Initialize();
        void Update()
        {
            if (m_Sign == BaristaHintSign.One || m_Sign == BaristaHintSign.Two || m_Sign == BaristaHintSign.Three) return;
            Render(Time.unscaledTime);
        }

        LineRenderer[] MakeHand(string side, Color color)
        {
            var lines = new LineRenderer[6];
            for (int i = 0; i < lines.Length; i++) lines[i] = MakeLine(side + " digit " + i, color);
            return lines;
        }
        LineRenderer MakeLine(string name, Color color)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false;
            line.sharedMaterial = m_Material; line.startColor = line.endColor = color;
            line.startWidth = line.endWidth = .006f; line.numCapVertices = line.numCornerVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        public void Render(float time)
        {
            if (m_Right == null || m_Right.Length != 6 || m_Arrow == null) return;
            float phase = time * 2.2f;
            bool twoHands = m_Sign == BaristaHintSign.Coffee || m_Sign == BaristaHintSign.Tea;
            foreach (var line in m_Left) line.enabled = twoHands;
            m_Arrow.positionCount = 0;
            m_CameraIcon.enabled = m_Sign == BaristaHintSign.Please || m_Sign == BaristaHintSign.ThankYou;
            if (m_CameraIcon.enabled)
            {
                Vector3 center = m_Sign == BaristaHintSign.Please ? Vector3.zero : new Vector3(-90, 35, 0);
                m_CameraIcon.positionCount = 8;
                var points = new[] { new Vector2(-18, -12), new Vector2(-18, 12), new Vector2(18, 12), new Vector2(18, -12),
                    new Vector2(-18, -12), new Vector2(18, -12), new Vector2(29, -18), new Vector2(29, 18) };
                for (int i = 0; i < points.Length; i++) m_CameraIcon.SetPosition(i, center + (Vector3)points[i] + Vector3.forward * -.02f);
            }
            Vector2 offset = Vector2.zero;
            float tilt = 0;
            bool thumb = false, index = false, middle = false, ring = false, little = false;
            bool pinch = false;
            if (m_Sign == BaristaHintSign.One) index = true;
            if (m_Sign == BaristaHintSign.Two) index = middle = true;
            if (m_Sign == BaristaHintSign.Three) thumb = index = middle = true;
            if (m_Sign == BaristaHintSign.Yes) tilt = Mathf.Sin(phase) * 28;
            if (m_Sign == BaristaHintSign.Coffee)
            {
                offset = new Vector2(Mathf.Cos(phase) * 18, 43 + Mathf.Sin(phase) * 8);
                tilt = Mathf.Sin(phase) * 12;
                Hand(m_Left, new Vector2(0, -32), 0, 210, false, false, false, false, false, false, true);
                PathEllipse(new Vector2(0, 47), new Vector2(24, 10));
            }
            if (m_Sign == BaristaHintSign.Tea)
            {
                pinch = true; middle = ring = little = true;
                tilt = 90;
                offset = new Vector2(25, 45 + Mathf.Cos(phase) * 25);
                Hand(m_Left, new Vector2(0, -28), 0, 210, true, false, false, false, false, false, true, true);
                m_Arrow.positionCount = 2; m_Arrow.SetPosition(0, new Vector3(36, -15, -.02f)); m_Arrow.SetPosition(1, new Vector3(36, 52, -.02f));
            }
            if (m_Sign == BaristaHintSign.Please)
            {
                thumb = index = middle = ring = little = true;
                offset = new Vector2(Mathf.Cos(phase) * 55, Mathf.Sin(phase) * 38);
                PathEllipse(Vector2.zero, new Vector2(55, 38));
            }
            if (m_Sign == BaristaHintSign.ThankYou)
            {
                thumb = index = middle = ring = little = true;
                float progress = Mathf.Repeat(time * .55f, 1);
                offset = new Vector2(-65 + progress * 135, -25 + progress * 40);
                m_Arrow.positionCount = 3;
                m_Arrow.SetPositions(new[] { new Vector3(-65, -60, -.02f), new Vector3(95, -10, -.02f), new Vector3(78, -25, -.02f) });
                Hand(m_Right, offset, 0, 210 - progress * 35, thumb, index, middle, ring, little, false, false);
                return;
            }
            Hand(m_Right, offset, tilt, twoHands ? 210 : m_Sign == BaristaHintSign.Please ? 180 : 270,
                thumb, index, middle, ring, little, pinch, false);
        }

        void PathEllipse(Vector2 center, Vector2 radii)
        {
            m_Arrow.positionCount = 41;
            for (int i = 0; i <= 40; i++)
            { float angle = i * Mathf.PI * 2 / 40; m_Arrow.SetPosition(i, new Vector3(center.x + Mathf.Cos(angle) * radii.x, center.y + Mathf.Sin(angle) * radii.y, -.02f)); }
        }

        static void Hand(LineRenderer[] lines, Vector2 offset, float angle, float scale,
            bool thumb, bool index, bool middle, bool ring, bool little, bool pinch, bool mirror, bool cup = false)
        {
            var rotation = Quaternion.Euler(0, 0, angle);
            Vector3 Map(Vector2 point) => new Vector3(offset.x, offset.y, -.03f)
                + rotation * new Vector3(point.x * (mirror ? -scale : scale), point.y * scale, 0);
            void Draw(int id, params Vector2[] points)
            {
                lines[id].positionCount = points.Length;
                for (int i = 0; i < points.Length; i++) lines[id].SetPosition(i, Map(points[i]));
            }
            Draw(5, new Vector2(-.14f, 0), new Vector2(-.15f, -.18f), new Vector2(-.09f, -.29f), new Vector2(.08f, -.29f), new Vector2(.15f, -.18f), new Vector2(.14f, 0));
            if (pinch) Draw(0, new Vector2(-.14f, -.13f), new Vector2(-.22f, -.03f), new Vector2(-.20f, .10f), new Vector2(-.14f, .12f));
            else if (thumb) Draw(0, new Vector2(-.14f, -.14f), new Vector2(-.22f, -.08f), new Vector2(-.30f, -.01f), new Vector2(-.34f, .05f));
            else Draw(0, new Vector2(-.14f, -.14f), new Vector2(-.12f, -.08f), new Vector2(-.04f, -.04f), new Vector2(.06f, -.04f));
            var extended = new[] { index, middle, ring, little };
            var heights = new[] { .27f, .34f, .29f, .20f };
            for (int finger = 0; finger < 4; finger++)
            {
                float x = -.105f + finger * .075f;
                if (pinch && finger == 0) Draw(1, new Vector2(x, 0), new Vector2(x - .015f, .16f), new Vector2(-.20f, .20f), new Vector2(-.14f, .12f));
                else if (extended[finger]) Draw(finger + 1, new Vector2(x, 0), new Vector2(x, .10f), new Vector2(x, heights[finger] * .75f), new Vector2(x, heights[finger]));
                else if (cup) Draw(finger + 1, new Vector2(x, 0), new Vector2(x + .06f, .09f), new Vector2(x + .14f, .065f), new Vector2(x + .16f, -.025f));
                else Draw(finger + 1, new Vector2(x, 0), new Vector2(x + .012f, .075f), new Vector2(x + .035f, .06f), new Vector2(x + .023f, -.04f));
            }
        }
    }
}
