using UnityEngine;

namespace PocketBump
{
    [RequireComponent(typeof(MarbleMotor))]
    public sealed class MarblePresentation : MonoBehaviour
    {
        [SerializeField] MarbleMotor _motor;
        [SerializeField] TrailRenderer _trail;
        [SerializeField] MeshRenderer _rim;

        void Awake()
        {
            _motor = _motor != null ? _motor : GetComponent<MarbleMotor>();
            _trail = _trail != null ? _trail : GetComponent<TrailRenderer>();
            Transform rim = transform.Find("Rim");
            if (_rim == null && rim != null) _rim = rim.GetComponent<MeshRenderer>();
        }

        public void Initialize()
        {
            _motor = GetComponent<MarbleMotor>();
            MeshFactory.AddSphere(gameObject, VisualPalette.Marble);

            _rim = MeshFactory.ZCylinder(
                transform,
                "Rim",
                Vector3.zero,
                new Vector3(1.2f, 0.06f, 1.2f),
                VisualPalette.WithAlpha(VisualPalette.Marble, 0.35f));
            MeshFactory.Sphere(
                transform,
                "Highlight",
                new Vector3(-0.18f, 0.2f, -0.28f),
                Vector3.one * 0.25f,
                Color.white);

            _trail = GetComponent<TrailRenderer>();
            if (_trail == null) _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.material = VisualPalette.SpriteMaterial();
            _trail.sortingOrder = 4;
            _trail.minVertexDistance = 0.06f;
            _trail.numCapVertices = 3;
            _trail.numCornerVertices = 2;
            _trail.emitting = false;
        }

        void Update()
        {
            if (_motor == null || _trail == null) return;

            bool dangerous = _motor.IsDangerous;
            _trail.emitting = _motor.Speed >= ArenaLayout.MarbleTrailSpeed;
            _trail.time = dangerous ? 0.48f : 0.18f;
            _trail.startWidth = dangerous ? 0.38f : 0.16f;
            _trail.endWidth = 0.01f;
            Color color = dangerous ? VisualPalette.Danger : VisualPalette.Marble;
            _trail.startColor = VisualPalette.WithAlpha(color, dangerous ? 0.95f : 0.55f);
            _trail.endColor = VisualPalette.WithAlpha(color, 0f);
            if (_rim != null)
                MeshFactory.SetColor(_rim, color);
        }
    }
}
