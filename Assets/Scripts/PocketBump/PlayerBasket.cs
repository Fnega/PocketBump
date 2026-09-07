using UnityEngine;

namespace PocketBump
{
    public sealed class PlayerBasket : MonoBehaviour
    {
        [SerializeField] PlayerMotor _motor;
        Transform _well;
        MeshRenderer _visual;

        public PlayerMotor Motor => _motor;
        public Transform Well => _well;
        public bool IsEmpty => _motor == null || !_motor.IsCarrying;

        public static PlayerBasket Ensure(PlayerMotor motor)
        {
            if (motor == null) return null;
            Transform existing = motor.transform.Find("Basket");
            PlayerBasket basket = existing != null
                ? existing.GetComponent<PlayerBasket>()
                : null;
            if (basket == null)
            {
                var go = existing != null ? existing.gameObject : new GameObject("Basket");
                go.transform.SetParent(motor.transform, false);
                go.layer = GameLayers.Player;
                basket = go.GetComponent<PlayerBasket>();
                if (basket == null) basket = go.AddComponent<PlayerBasket>();
            }

            basket.Initialize(motor);
            return basket;
        }

        public void Initialize(PlayerMotor motor)
        {
            _motor = motor;
            CompensateParentScale();
            BuildVisual();
            BuildRims();
            BuildWell();
            ApplyFacing(_motor != null ? _motor.Facing : Vector2.down);
        }

        public void ApplyFacing(Vector2 facing)
        {
            if (facing.sqrMagnitude < 0.0001f) facing = Vector2.down;
            facing.Normalize();
            CompensateParentScale();
            float parentScale = Mathf.Max(0.0001f, transform.parent.localScale.x);
            transform.localPosition = (Vector3)(facing * (ArenaLayout.BasketOffset / parentScale));
            float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg - 90f;
            transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        void CompensateParentScale()
        {
            if (transform.parent == null) return;
            float parentScale = Mathf.Max(0.0001f, transform.parent.localScale.x);
            transform.localScale = Vector3.one / parentScale;
        }

        void BuildVisual()
        {
            Transform visual = transform.Find("Visual");
            var go = visual != null ? visual.gameObject : new GameObject("Visual");
            if (visual == null) go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -ArenaLayout.BasketThickness * 0.5f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(
                ArenaLayout.BasketWidth,
                ArenaLayout.BasketLength,
                ArenaLayout.BasketThickness);
            Color identity = _motor != null
                ? VisualPalette.PlayerColor(_motor.PlayerIndex)
                : Color.white;
            _visual = MeshFactory.AddCube(go, VisualPalette.WithAlpha(identity, 0.55f));
        }

        void BuildRims()
        {
            float w = ArenaLayout.BasketWidth;
            float h = ArenaLayout.BasketLength;
            float rim = ArenaLayout.BasketRim;
            MakeRim("RimBack", new Vector2(0f, -h * 0.5f + rim * 0.5f), new Vector2(w, rim));
            MakeRim("RimLeft", new Vector2(-w * 0.5f + rim * 0.5f, rim * 0.15f), new Vector2(rim, h - rim));
            MakeRim("RimRight", new Vector2(w * 0.5f - rim * 0.5f, rim * 0.15f), new Vector2(rim, h - rim));
        }

        void MakeRim(string name, Vector2 localPos, Vector2 size)
        {
            Transform existing = transform.Find(name);
            var go = existing != null ? existing.gameObject : new GameObject(name);
            if (existing == null) go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = GameLayers.Player;
            var col = go.GetComponent<BoxCollider2D>();
            if (col == null) col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = false;
            col.size = size;
            col.offset = Vector2.zero;
            if (_motor != null && _motor.Body != null)
                col.sharedMaterial = _motor.Body.sharedMaterial;
            Color identity = _motor != null
                ? VisualPalette.PlayerColor(_motor.PlayerIndex)
                : VisualPalette.Wall;
            MeshFactory.Cube(
                go.transform,
                "Mesh",
                new Vector3(0f, 0f, -ArenaLayout.BasketThickness * 0.5f),
                new Vector3(size.x, size.y, ArenaLayout.BasketThickness),
                identity);
        }

        void BuildWell()
        {
            Transform existing = transform.Find("Well");
            var go = existing != null ? existing.gameObject : new GameObject("Well");
            if (existing == null) go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, ArenaLayout.BasketRim * 0.2f, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = GameLayers.Player;
            var col = go.GetComponent<BoxCollider2D>();
            if (col == null) col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(
                ArenaLayout.BasketWidth - ArenaLayout.BasketRim * 2.2f,
                ArenaLayout.BasketLength - ArenaLayout.BasketRim * 1.6f);
            _well = go.transform;
            var well = go.GetComponent<BasketWell>();
            if (well == null) well = go.AddComponent<BasketWell>();
            well.Basket = this;
        }

        public void HandleWellOverlap(Collider2D other)
        {
            if (_motor == null || other == null) return;
            var marble = other.GetComponent<MarbleMotor>();
            if (marble == null) marble = other.GetComponentInParent<MarbleMotor>();
            if (marble == null) return;
            _motor.TryCollectOrSteal(marble);
        }
    }

    public sealed class BasketWell : MonoBehaviour
    {
        public PlayerBasket Basket;

        void OnTriggerEnter2D(Collider2D other)
        {
            if (Basket != null) Basket.HandleWellOverlap(other);
        }

        void OnTriggerStay2D(Collider2D other)
        {
            if (Basket != null) Basket.HandleWellOverlap(other);
        }
    }
}
