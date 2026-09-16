using UnityEngine;

// 根据玩家索引和运动状态更新网格颜色、朝向和拖尾。
// 将视觉表现与碰撞/得分分离，避免美术变化破坏模拟一致性。
namespace PocketBump
{
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class PlayerPresentation : MonoBehaviour
    {
        [SerializeField] PlayerMotor _motor;
        [SerializeField] TrailRenderer _trail;
        [SerializeField] MeshRenderer _rim;
        [SerializeField] Color _identity;

        void Awake()
        {
            _motor = _motor != null ? _motor : GetComponent<PlayerMotor>();
            _trail = _trail != null ? _trail : GetComponent<TrailRenderer>();
            Transform rim = transform.Find("Rim");
            if (_rim == null && rim != null) _rim = rim.GetComponent<MeshRenderer>();
            if (_motor != null) _identity = VisualPalette.PlayerColor(_motor.PlayerIndex);
        }

        // 玩家索引决定身份色；视觉缩放与 MatchSim 的玩家半径保持一致。
        public void Initialize(int playerIndex)
        {
            _motor = GetComponent<PlayerMotor>();
            _identity = VisualPalette.PlayerColor(playerIndex);

            MeshFactory.AddSphere(gameObject, VisualPalette.WithAlpha(_identity, 0.88f));

            _rim = MeshFactory.ZCylinder(
                transform,
                "Rim",
                Vector3.zero,
                new Vector3(1.18f, 0.07f, 1.18f),
                _identity);
            MeshFactory.Sphere(
                transform,
                "Highlight",
                new Vector3(-0.16f, 0.18f, -0.28f),
                Vector3.one * 0.28f,
                VisualPalette.WithAlpha(Color.white, 0.9f));

            _trail = GetComponent<TrailRenderer>();
            if (_trail == null) _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.material = VisualPalette.SpriteMaterial();
            _trail.sortingOrder = 5;
            _trail.minVertexDistance = 0.08f;
            _trail.numCapVertices = 4;
            _trail.numCornerVertices = 3;
            _trail.emitting = false;
            ApplyTrail(false);
        }

        // 拖尾与发光使用渲染帧更新，因为这些信息不进入模拟和校验和。
        void Update()
        {
            if (_motor == null || _trail == null) return;

            bool moving = _motor.Speed >= ArenaLayout.PlayerTrailSpeed;
            _trail.emitting = moving;
            ApplyTrail(_motor.IsBoosting);

            float pulse = _motor.IsBoosting
                ? 0.75f + Mathf.Sin(Time.time * 18f) * 0.2f
                : 0.45f;
            if (_rim != null)
                MeshFactory.SetColor(_rim, Color.Lerp(_identity, Color.white, pulse * 0.35f));
        }

        void ApplyTrail(bool boosted)
        {
            _trail.time = boosted ? 0.42f : 0.2f;
            _trail.startWidth = boosted ? 0.52f : 0.28f;
            _trail.endWidth = boosted ? 0.04f : 0.015f;
            _trail.startColor = VisualPalette.WithAlpha(_identity, boosted ? 0.95f : 0.5f);
            _trail.endColor = VisualPalette.WithAlpha(_identity, 0f);
        }
    }
}
