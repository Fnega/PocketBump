using UnityEngine;
using UnityEngine.UI;

namespace PocketBump
{
    // Thin, empty-centred foot markers. Styles are authored serialized data; state
    // updates never replace them. No textures, timers, completion flashes or sounds.
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class DashReadinessGraphic : MaskableGraphic
    {
        [SerializeField] Color _identity = new Color(.28f, .78f, .83f);
        [SerializeField] Color _outline = new Color(.06f, .15f, .18f, .95f);
        [SerializeField, Range(1f, 4f)] float _stroke = 1.8f;
        [SerializeField] Vector2 _footOffset;
        [SerializeField] bool _detailedPreview = true;
        DashReadinessState _state;
        bool _hasState;
        public DashReadinessState State => _state;
        public Color IdentityColor { get => _identity; set { _identity = value; SetVerticesDirty(); } }
        public Color OutlineColor { get => _outline; set { _outline = value; SetVerticesDirty(); } }
        public Vector2 FootOffset { get => _footOffset; set => _footOffset = value; }
        public bool DetailedPreview { get => _detailedPreview; set { _detailedPreview = value; SetVerticesDirty(); } }
        public RectTransform RectTransform => rectTransform;

        public void SetState(DashReadinessState state)
        {
            bool changed = !_hasState || _state.Kind != state.Kind || _state.Detailed != state.Detailed || _state.Progress != state.Progress;
            _state = state; _hasState = true;
            if (changed) SetVerticesDirty();
        }
        protected override void Awake() { base.Awake(); raycastTarget = false; }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            DashReadinessState state = _hasState ? _state : new DashReadinessState(DashReadinessKind.Cooling, _detailedPreview, .65f);
            if (state.Kind == DashReadinessKind.Hidden) return;
            Vector2 size = rectTransform.rect.size;
            Color shade = state.Kind == DashReadinessKind.Ready ? _identity : Color.Lerp(_outline, _identity, .5f);
            if (!state.Detailed)
            {
                // Fixed geometry and colour for every unavailable opponent, regardless
                // of their cooldown, stun, active Dash or kickoff duration.
                Vector2 center = new Vector2(0f, -size.y * .22f);
                float r = Mathf.Min(size.x, size.y) * .24f;
                Stroke(mesh, center + Vector2.up * r, center + Vector2.right * r, shade);
                Stroke(mesh, center + Vector2.right * r, center + Vector2.down * r, shade);
                Stroke(mesh, center + Vector2.down * r, center + Vector2.left * r, shade);
                Stroke(mesh, center + Vector2.left * r, center + Vector2.up * r, shade);
                if (state.Kind == DashReadinessKind.Ready) Chevron(mesh, center, r * .52f, _identity);
                else Stroke(mesh, center + new Vector2(-r * .4f, 0f), center + new Vector2(r * .4f, 0f), shade);
                return;
            }
            Vector2 radius = new Vector2(size.x * .43f, size.y * .29f);
            Arc(mesh, radius, 1f, _outline, _stroke + 2f);
            if (state.Kind == DashReadinessKind.Cooling)
                Arc(mesh, radius, state.Progress, _identity, _stroke);
            else if (state.Kind == DashReadinessKind.Ready)
            {
                Arc(mesh, radius, 1f, _identity, _stroke);
                Chevron(mesh, new Vector2(0f, -size.y * .38f), 3.5f, _identity);
            }
            else if (state.Kind == DashReadinessKind.Dashing)
            {
                Chevron(mesh, new Vector2(-4f, -size.y * .38f), 3f, _identity);
                Chevron(mesh, new Vector2(4f, -size.y * .38f), 3f, _identity);
            }
            else Stroke(mesh, new Vector2(-4f, -size.y * .38f), new Vector2(4f, -size.y * .38f), shade);
        }
        void Arc(VertexHelper mesh, Vector2 radius, float progress, Color shade, float thickness)
        {
            const int segments = 48;
            for (int i = 0; i < segments && i / (float)segments < progress; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f, b = Mathf.Min((i + 1f) / segments, progress) * Mathf.PI * 2f;
                Line(mesh, new Vector2(Mathf.Sin(a) * radius.x, Mathf.Cos(a) * radius.y),
                    new Vector2(Mathf.Sin(b) * radius.x, Mathf.Cos(b) * radius.y), shade, thickness);
            }
        }
        void Chevron(VertexHelper mesh, Vector2 at, float r, Color shade)
        { Stroke(mesh, at + new Vector2(-r, -r * .5f), at + new Vector2(0, r * .5f), shade); Stroke(mesh, at + new Vector2(0, r * .5f), at + new Vector2(r, -r * .5f), shade); }
        void Stroke(VertexHelper mesh, Vector2 a, Vector2 b, Color shade)
        { Line(mesh, a, b, _outline, _stroke + 2f); Line(mesh, a, b, shade, _stroke); }
        void Line(VertexHelper mesh, Vector2 a, Vector2 b, Color shade, float thickness)
        {
            Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * (thickness * .5f);
            int start = mesh.currentVertCount;
            mesh.AddVert(a - normal, shade * color, Vector2.zero); mesh.AddVert(a + normal, shade * color, Vector2.zero);
            mesh.AddVert(b + normal, shade * color, Vector2.zero); mesh.AddVert(b - normal, shade * color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
