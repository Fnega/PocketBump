using UnityEngine;

namespace PocketBump
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class MarbleMotor : MonoBehaviour
    {
        Rigidbody2D _body;
        public int SimIndex = -1;
        public float Speed { get; private set; }
        public bool IsDangerous => Speed >= ArenaLayout.MarbleDangerousSpeed;
        public int DangerousImpactCount { get; private set; }
        public PlayerMotor Carrier { get; private set; }
        public bool IsCarried => Carrier != null;
        public Rigidbody2D Body => _body;

        void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
        }

        public void ApplySim(MarbleSim sim, PlayerMotor carrier)
        {
            Speed = sim.Velocity.magnitude;
            DangerousImpactCount = sim.DangerousImpactCount;
            Carrier = sim.Carrier >= 0 ? carrier : null;
            if (_body == null) _body = GetComponent<Rigidbody2D>();
            if (_body != null)
            {
                _body.position = sim.Position;
                _body.velocity = sim.Velocity;
            }
        }

        public void AttachTo(PlayerMotor carrier, Transform well)
        {
            Carrier = carrier;
        }

        public void ReleaseToTable(Vector2 position, Vector2 velocity)
        {
            Carrier = null;
            transform.position = position;
            if (_body != null)
            {
                _body.position = position;
                _body.velocity = velocity;
            }
        }

        public void ReleaseToTable(Vector2 position)
        {
            ReleaseToTable(position, Vector2.zero);
        }

        public void DetachForPool()
        {
            Carrier = null;
        }

        public static float CalculateAdditionalImpulse(float impactSpeed)
        {
            if (impactSpeed < ArenaLayout.MarbleMildImpactSpeed)
                return 0f;

            float mildRange = Mathf.Min(
                impactSpeed,
                ArenaLayout.MarbleDangerousSpeed) - ArenaLayout.MarbleMildImpactSpeed;
            float impulse = mildRange * ArenaLayout.MarbleMildImpulseScale;
            if (impactSpeed > ArenaLayout.MarbleDangerousSpeed)
            {
                impulse += (impactSpeed - ArenaLayout.MarbleDangerousSpeed) *
                    ArenaLayout.MarbleDangerousImpulseScale;
            }

            return impulse;
        }
    }
}
