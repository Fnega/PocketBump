using UnityEngine;

// 根据弹珠速度更新网格、颜色和拖尾等视觉效果。
// 视觉反馈读取 MarbleMotor 的模拟镜像，但不反向修改比赛规则。
namespace PocketBump
{
    [RequireComponent(typeof(MarbleMotor))]
    public sealed class MarblePresentation : MonoBehaviour
    {
        [SerializeField] MarbleMotor _motor;
        [SerializeField] TrailRenderer _trail;
        [SerializeField] MeshRenderer _rim;
        Renderer[] _kenneyRenderers;

        void Awake()
        {
            _motor = _motor != null ? _motor : GetComponent<MarbleMotor>();
            _trail = _trail != null ? _trail : GetComponent<TrailRenderer>();
            Transform rim = transform.Find("Rim");
            if (_rim == null && rim != null) _rim = rim.GetComponent<MeshRenderer>();
            ApplyArtTheme();
        }

        // 网格和 TrailRenderer 只创建一次，后续配合弹珠对象池反复启用。
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
            ApplyArtTheme();

            _trail = GetComponent<TrailRenderer>();
            if (_trail == null) _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.material = VisualPalette.SpriteMaterial();
            _trail.sortingOrder = 4;
            _trail.minVertexDistance = 0.06f;
            _trail.numCapVertices = 3;
            _trail.numCornerVertices = 2;
            _trail.emitting = false;
        }

        // 危险色由模拟速度阈值决定，帮助玩家读取高速弹珠的额外击退风险。
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
            if (_kenneyRenderers != null)
            {
                for (int i = 0; i < _kenneyRenderers.Length; i++)
                    if (_kenneyRenderers[i] != null)
                        MeshFactory.ApplyColor(_kenneyRenderers[i].sharedMaterial, color, dangerous ? 0.9f : 0.42f);
            }
        }

        void ApplyArtTheme()
        {
            GameObject visual = ArtCatalog.AttachModel(
                gameObject,
                "Models/Marble/marble-low",
                VisualPalette.Marble,
                Quaternion.Euler(90f, 0f, 0f));
            _kenneyRenderers = visual != null ? visual.GetComponentsInChildren<Renderer>(true) : null;
        }
    }
}
