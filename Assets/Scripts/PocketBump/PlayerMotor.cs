using UnityEngine;

// 玩家场景对象与 PlayerSim 之间的适配层。
// 历史方法保留给场景/测试兼容，正式比赛状态均由 MatchSim 决定。
namespace PocketBump
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        public int PlayerIndex;
        public bool InputEnabled = true;
        public MatchDirector Director;

        Rigidbody2D _body;
        BoostPhase _boostPhase;
        float _boostPhaseTime;
        Vector2 _facing;
        ChargePhase _chargePhase;
        float _chargeTime;
        Vector2 _lockedShotDirection;
        MarbleMotor _carried;
        PlayerBasket _basket;

        public Rigidbody2D Body => _body;
        public float Speed => _body == null ? 0f : _body.velocity.magnitude;
        public BoostPhase CurrentBoostPhase => _boostPhase;
        public bool IsBoosting => _boostPhase == BoostPhase.Boosting;
        public bool IsWindingUp => _boostPhase == BoostPhase.Windup;
        public Vector2 Facing => _facing;
        public ChargePhase CurrentChargePhase => _chargePhase;
        public float ChargeDirectionOffset => PlayerChargeMath.DirectionOffsetDegrees(_chargeTime);
        public float ChargePower01 => _chargePhase == ChargePhase.Power
            ? PlayerChargeMath.Power01(_chargeTime)
            : 0f;
        public Vector2 LockedShotDirection => _lockedShotDirection;
        public bool IsCarrying => Director != null && Director.Sim != null
            ? Director.Sim.Players[PlayerIndex].Carried >= 0
            : _carried != null;
        public MarbleMotor Carried => _carried;
        public PlayerBasket Basket => _basket;

        void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _facing = PlayerChargeMath.SpawnFacing(PlayerIndex);
            _basket = PlayerBasket.Ensure(this);
            PlayerChargePresentation.Ensure(this);
        }

        // presentedPosition 可是 Tick 间插值位置；其余字段取当前权威状态供表现读取。
        public void ApplySim(PlayerSim sim, Vector2 presentedPosition)
        {
            _facing = sim.Facing;
            _boostPhase = sim.Boost;
            _boostPhaseTime = sim.BoostTime;
            _chargePhase = sim.Charge;
            _chargeTime = sim.ChargeTime;
            _lockedShotDirection = sim.LockedShotDir;
            transform.position = presentedPosition;
            if (_body != null)
            {
                _body.position = presentedPosition;
                _body.velocity = sim.Velocity;
            }

            if (_basket != null) _basket.ApplyFacing(_facing);
        }

        public Vector2 ReadInput()
        {
            return TickInputSampler.ReadMove(PlayerIndex);
        }

        public bool ReadBoost()
        {
            return Input.GetKey(BoostKeyForPlayer(PlayerIndex));
        }

        public static KeyCode BoostKeyForPlayer(int playerIndex)
        {
            return ControlBindings.Boost(playerIndex);
        }

        public static KeyCode ShootKeyForPlayer(int playerIndex)
        {
            return ControlBindings.Shoot(playerIndex);
        }

        public void ApplyPlayerIndex(int index)
        {
            PlayerIndex = index;
            _facing = PlayerChargeMath.SpawnFacing(index);
            _basket = PlayerBasket.Ensure(this);
            if (_basket != null) _basket.Initialize(this);
            PlayerChargePresentation.Ensure(this);
        }

        public void SetFacing(Vector2 facing)
        {
            if (Director != null && Director.Sim != null)
            {
                Director.DebugPlacePlayer(PlayerIndex, Director.Sim.Players[PlayerIndex].Position, facing);
                return;
            }

            if (facing.sqrMagnitude < 0.0001f) facing = Vector2.down;
            _facing = facing.normalized;
            if (_basket != null) _basket.ApplyFacing(_facing);
        }

        // 兼容旧调用；正式输入由 TickInputSampler 锁存并在 MatchSim 内推进。
        public bool HandleShootPress()
        {
            if (Director != null && Director.Sim != null)
                return Director.DebugShoot(PlayerIndex);
            return false;
        }

        public void CancelCharge()
        {
            if (Director == null || Director.Sim == null) return;
            PlayerSim p = Director.Sim.Players[PlayerIndex];
            p.Charge = ChargePhase.None;
            p.ChargeTime = 0f;
            Director.Sim.Players[PlayerIndex] = p;
            _chargePhase = ChargePhase.None;
        }

        public void TryCollectOrSteal(MarbleMotor marble)
        {
            if (Director == null || marble == null) return;
            int index = marble.SimIndex;
            if (index < 0) index = 0;
            Director.DebugCollect(PlayerIndex, index);
        }

        public void NotifyLostMarble(MarbleMotor marble)
        {
            if (_carried == marble) _carried = null;
        }

        public void ClearCarry()
        {
            if (Director == null || Director.Sim == null) return;
            PlayerSim p = Director.Sim.Players[PlayerIndex];
            int carried = p.Carried;
            if (carried < 0) return;
            p.Carried = -1;
            p.Charge = ChargePhase.None;
            Director.Sim.Players[PlayerIndex] = p;
            MarbleSim m = Director.Sim.Marbles[carried];
            m.Carrier = -1;
            Director.Sim.Marbles[carried] = m;
        }

        public void StopAndPlace(Vector2 position)
        {
            if (Director != null && Director.Sim != null)
            {
                Director.DebugPlacePlayer(PlayerIndex, position, PlayerChargeMath.SpawnFacing(PlayerIndex));
                return;
            }

            transform.position = position;
            if (_body == null) _body = GetComponent<Rigidbody2D>();
            if (_body != null)
            {
                _body.position = position;
                _body.velocity = Vector2.zero;
            }
        }

        public void ResetBoost()
        {
            _boostPhase = BoostPhase.Normal;
            _boostPhaseTime = 0f;
        }

        // 调试/旧组件入口最终仍修改 Sim，避免只推动画面而遗漏权威状态。
        public void ApplyKnockback(Vector2 impulse)
        {
            if (Director == null || Director.Sim == null) return;
            PlayerSim p = Director.Sim.Players[PlayerIndex];
            p.Velocity += impulse / ArenaLayout.PlayerMass;
            p.ImpactCoast = ArenaLayout.ImpactCoastSeconds;
            Director.Sim.Players[PlayerIndex] = p;
        }
    }
}
