using UnityEngine;
using UnityEngine.Rendering;

namespace PocketBump
{
    // Authored world geometry, not an overlay or an independent gameplay timer.
    // The HUD owns whether this local-only view is active; shared assets stay read only.
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DashGroundReadinessRing : MonoBehaviour
    {
        [SerializeField, Min(.1f)] float _radius = 1.12f;
        [SerializeField, Min(.01f)] float _width = .24f;
        [SerializeField, Min(.001f)] float _groundHeight = .055f;
        [SerializeField] Color _identityColor = new Color(.035f, .72f, .9f, 1f);
        [SerializeField] Color _trackColor = new Color(.17f, .23f, .25f, .8f);
        [SerializeField] Color _outlineColor = new Color(.025f, .065f, .08f, 1f);
        [SerializeField, Min(0f)] float _outlineWidth = .025f;
        [SerializeField, Range(1f, 45f)] float _minimumCoolingGapDegrees = 12f;
        MeshRenderer _renderer;
        MaterialPropertyBlock _properties;
        DashReadinessState _state;
        bool _hasState, _dirty = true;
        Vector3 _groundPosition;

        static readonly int IdentityId = Shader.PropertyToID("_IdentityColor");
        static readonly int TrackId = Shader.PropertyToID("_TrackColor");
        static readonly int OutlineId = Shader.PropertyToID("_OutlineColor");
        static readonly int WidthId = Shader.PropertyToID("_RingWidth");
        static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        static readonly int ProgressId = Shader.PropertyToID("_Progress");

        public DashReadinessState State => _state;
        public float DisplayedProgress => ProgressFor(_state);
        public MeshRenderer RingRenderer => _renderer != null ? _renderer : (_renderer = GetComponent<MeshRenderer>());
        public bool Visible => RingRenderer != null && RingRenderer.enabled && gameObject.activeInHierarchy;
        public float Radius { get => _radius; set { _radius = Mathf.Max(.1f, value); RefreshStyle(); } }
        public float Width { get => _width; set { _width = Mathf.Clamp(value, .01f, _radius * .9f); RefreshStyle(); } }
        public float GroundHeight { get => _groundHeight; set { _groundHeight = Mathf.Max(.001f, value); RefreshStyle(); } }
        public Color IdentityColor { get => _identityColor; set { _identityColor = value; RefreshStyle(); } }
        public Color TrackColor { get => _trackColor; set { _trackColor = value; RefreshStyle(); } }
        public Color OutlineColor { get => _outlineColor; set { _outlineColor = value; RefreshStyle(); } }
        public float OutlineWidth { get => _outlineWidth; set { _outlineWidth = Mathf.Max(0f, value); RefreshStyle(); } }
        public float MinimumCoolingGapDegrees
        { get => _minimumCoolingGapDegrees; set { _minimumCoolingGapDegrees = Mathf.Clamp(value, 1f, 45f); RefreshStyle(); } }

        // Explicit authoring/test installation only. Awake never creates or replaces
        // a mesh, material, renderer, authored colour or other scene object.
        public void ConfigureAssets(Mesh mesh, Material material)
        {
            if (mesh == null || material == null) throw new System.ArgumentNullException(mesh == null ? nameof(mesh) : nameof(material));
            GetComponent<MeshFilter>().sharedMesh = mesh;
            RingRenderer.sharedMaterial = material;
            RingRenderer.shadowCastingMode = ShadowCastingMode.Off;
            RingRenderer.receiveShadows = false;
            RingRenderer.lightProbeUsage = LightProbeUsage.Off;
            RingRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _groundPosition = transform.position;
            RefreshStyle();
            if (Application.isPlaying && !_hasState) Hide();
        }

        public static Mesh CreateUnitGroundQuad()
        {
            var mesh = new Mesh { name = "Dash readiness unit XZ quad" };
            mesh.vertices = new[] { new Vector3(-1f, 0f, -1f), new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, -1f) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        public void ApplyState(DashReadinessState state, Vector3 groundPosition)
        {
            _state = state; _hasState = true; _groundPosition = groundPosition;
            RefreshStyle();
            RingRenderer.enabled = isActiveAndEnabled && state.Kind != DashReadinessKind.Hidden;
        }

        public void Hide()
        {
            if (RingRenderer != null) RingRenderer.enabled = false;
        }

        void OnEnable()
        {
            _groundPosition = transform.position;
            _dirty = true;
            // Never show an editor preview in a release/LAN frame before HUD binding.
            if (Application.isPlaying) Hide();
        }
        void OnDisable()
        {
            // Scene unload/domain reload in edit mode must not save a hidden renderer.
            if (Application.isPlaying) Hide();
        }
        void OnValidate()
        {
            _radius = Mathf.Max(.1f, _radius);
            _width = Mathf.Clamp(_width, .01f, _radius * .9f);
            _groundHeight = Mathf.Max(.001f, _groundHeight);
            _outlineWidth = Mathf.Max(0f, _outlineWidth);
            _minimumCoolingGapDegrees = Mathf.Clamp(_minimumCoolingGapDegrees, 1f, 45f);
            _dirty = true;
        }
        void Update()
        {
            if (!_dirty) return;
            if (!Application.isPlaying) _groundPosition = transform.position;
            RefreshStyle();
        }

        float ProgressFor(DashReadinessState state)
        {
            if (state.Kind == DashReadinessKind.Ready) return 1f;
            if (state.Kind != DashReadinessKind.Cooling) return 0f;
            // The final cooldown tick must not appear fully charged at small sizes.
            // This is only visual: the exact progress and eligibility remain in State.
            return Mathf.Min(Mathf.Clamp01(state.Progress), 1f - _minimumCoolingGapDegrees / 360f);
        }

        void RefreshStyle()
        {
            _dirty = false;
            if (RingRenderer == null) return;
            Vector3 position = new Vector3(_groundPosition.x, _groundHeight, _groundPosition.z);
            if (transform.position != position) transform.position = position;
            if (transform.rotation != Quaternion.identity) transform.rotation = Quaternion.identity;
            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            Vector3 scale = new Vector3(SafeScale(parentScale.x), SafeScale(parentScale.y), SafeScale(parentScale.z));
            if (transform.localScale != scale) transform.localScale = scale;
            if (_properties == null) _properties = new MaterialPropertyBlock();
            RingRenderer.GetPropertyBlock(_properties);
            _properties.SetColor(IdentityId, _identityColor);
            _properties.SetColor(TrackId, _trackColor);
            _properties.SetColor(OutlineId, _outlineColor);
            _properties.SetFloat(WidthId, Mathf.Clamp(_width / _radius, .001f, .9f));
            _properties.SetFloat(OutlineWidthId, Mathf.Clamp(_outlineWidth / _radius, 0f, _width / (_radius * 2f)));
            float shown = _hasState ? DisplayedProgress : .65f;
            _properties.SetFloat(ProgressId, shown);
            RingRenderer.SetPropertyBlock(_properties);
        }

        float SafeScale(float parentScale) => Mathf.Abs(parentScale) > .0001f ? _radius / parentScale : _radius;
    }
}
