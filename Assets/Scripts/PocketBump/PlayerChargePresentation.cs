using UnityEngine;

// 将 ChargePhase 转换为玩家身旁的方向/力度指示条。
// 指示条只解释模拟状态，不参与锁定方向或决定发射力度。
namespace PocketBump
{
    public sealed class PlayerChargePresentation : MonoBehaviour
    {
        [SerializeField] PlayerMotor _motor;
        Transform _direction;
        Transform _power;

        public static PlayerChargePresentation Ensure(PlayerMotor motor)
        {
            if (motor == null) return null;
            var presentation = motor.GetComponent<PlayerChargePresentation>();
            if (presentation == null) presentation = motor.gameObject.AddComponent<PlayerChargePresentation>();
            presentation._motor = motor;
            presentation.Build();
            return presentation;
        }

        void Awake()
        {
            _motor = _motor != null ? _motor : GetComponent<PlayerMotor>();
            Build();
        }

        void Build()
        {
            Color identity = _motor != null
                ? VisualPalette.PlayerColor(_motor.PlayerIndex)
                : VisualPalette.PlayerOne;
            _direction = MakeBar("ChargeDirection", identity, 1f);
            _power = MakeBar("ChargePower", VisualPalette.Text, 1.4f);
            Hide();
        }

        Transform NeedleParent()
        {
            if (_motor != null && _motor.Basket != null)
                return _motor.Basket.transform;
            return transform;
        }

        Transform MakeBar(string name, Color color, float emission)
        {
            Transform parent = NeedleParent();
            Transform existing = parent.Find(name);
            if (existing == null) existing = transform.Find(name);
            var go = existing != null ? existing.gameObject : new GameObject(name);
            go.transform.SetParent(parent, false);
            MeshRenderer renderer = MeshFactory.AddCube(go, color);
            MeshFactory.ApplyColor(renderer.sharedMaterial, color, emission);
            return go.transform;
        }

        // 在模拟和玩家位置应用后刷新指示器，避免针条落后一帧。
        void LateUpdate()
        {
            if (_motor == null) return;
            Transform parent = NeedleParent();
            if (_direction != null && _direction.parent != parent)
                _direction.SetParent(parent, false);
            if (_power != null && _power.parent != parent)
                _power.SetParent(parent, false);

            ChargePhase phase = _motor.CurrentChargePhase;
            if (phase == ChargePhase.Direction)
            {
                _direction.gameObject.SetActive(true);
                _power.gameObject.SetActive(false);
                Vector2 dir = PlayerChargeMath.LockedShotDirection(
                    _motor.Facing,
                    _motor.ChargeDirectionOffset);
                ApplyNeedle(
                    _direction,
                    dir,
                    ArenaLayout.ChargeDirectionLength,
                    ArenaLayout.ChargeDirectionWidth,
                    ArenaLayout.NeedleLift);
            }
            else if (phase == ChargePhase.Power)
            {
                _direction.gameObject.SetActive(true);
                _power.gameObject.SetActive(true);
                Vector2 dir = _motor.LockedShotDirection;
                ApplyNeedle(
                    _direction,
                    dir,
                    ArenaLayout.ChargeDirectionLength,
                    ArenaLayout.ChargeDirectionWidth,
                    ArenaLayout.NeedleLift);
                float length = ArenaLayout.ChargePowerLengthMin
                    + _motor.ChargePower01 * ArenaLayout.ChargePowerLengthRange;
                ApplyNeedle(
                    _power,
                    dir,
                    length,
                    ArenaLayout.ChargePowerWidth,
                    ArenaLayout.NeedlePowerLift);
            }
            else
            {
                Hide();
            }
        }

        void ApplyNeedle(Transform bar, Vector2 dir, float length, float width, float lift)
        {
            if (bar == null) return;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
            dir.Normalize();

            Transform parent = bar.parent != null ? bar.parent : transform;
            Vector3 localDir = parent.InverseTransformDirection(new Vector3(dir.x, dir.y, 0f));
            localDir.z = 0f;
            if (localDir.sqrMagnitude < 0.0001f) localDir = Vector3.up;
            localDir.Normalize();

            Vector3 origin = new Vector3(0f, ArenaLayout.BasketRim * 0.2f, -lift);
            bar.localPosition = origin + localDir * (length * 0.5f);
            bar.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(localDir.y, localDir.x) * Mathf.Rad2Deg);
            bar.localScale = new Vector3(length, width, ArenaLayout.NeedleThickness);
        }

        // 无蓄力时隐藏而非销毁，避免频繁创建网格和材质实例。
        void Hide()
        {
            if (_direction != null) _direction.gameObject.SetActive(false);
            if (_power != null) _power.gameObject.SetActive(false);
        }
    }
}
