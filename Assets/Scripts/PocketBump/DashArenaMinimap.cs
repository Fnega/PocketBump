using UnityEngine;
using UnityEngine.UI;

namespace PocketBump
{
    // Passive, north-up geometry over the current corrected state. No camera, clock,
    // gameplay object, input, or independent simulated/interpolated entity is created.
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class DashArenaMinimap : MaskableGraphic
    {
        const float WorldMargin = 3f;
        static readonly ArenaRulesProfile PreviewRules = ArenaRulesProfile.CreateProductionV1();
        [SerializeField] Color _background = new Color(.10f, .21f, .21f, .94f);
        [SerializeField] Color _field = new Color(.71f, .87f, .76f, 1f);
        [SerializeField] Color _outline = new Color(.22f, .40f, .36f, 1f);
        [SerializeField] Color _playerOne = new Color(.20f, .83f, .95f, 1f);
        [SerializeField] Color _playerTwo = new Color(1f, .57f, .20f, 1f);
        [SerializeField] Color _ball = Color.white;
        [SerializeField] Color _wall = new Color(.25f, .41f, .39f, 1f);
        [SerializeField] Color _repair = new Color(.85f, .41f, .18f, 1f);
        DashArenaState _state;
        int _localSeat;

        public DashArenaState State => _state;
        public int LocalSeat => _localSeat;
        public RectTransform RectTransform => rectTransform;
        public Color PlayerOneColor { get => _playerOne; set { _playerOne = value; SetVerticesDirty(); } }
        public Color PlayerTwoColor { get => _playerTwo; set { _playerTwo = value; SetVerticesDirty(); } }
        public Color FieldColor { get => _field; set { _field = value; SetVerticesDirty(); } }
        public Color BallColor { get => _ball; set { _ball = value; SetVerticesDirty(); } }
        ArenaRulesProfile Rules => _state?.Rules ?? PreviewRules;
        public int ActivePlayerCount
        {
            get { int count = 0; if (_state != null) foreach (var player in _state.Players) if (player.Alive) count++; return count; }
        }
        public int ActiveBallCount
        {
            get { int count = 0; if (_state != null) foreach (var ball in _state.Balls) if (ball.Active) count++; return count; }
        }
        public int SolidWallCount => WallCount(0);
        public int PendingWallCount => WallCount(1);
        public int BrokenWallCount => WallCount(2);
        public Rect WorldRect => new Rect(-Rules.HalfWidth - WorldMargin, -Rules.HalfHeight - WorldMargin,
            Rules.Width + WorldMargin * 2f, Rules.Height + WorldMargin * 2f);
        public Rect MapRect
        {
            get
            {
                Rect rect = rectTransform.rect, world = WorldRect;
                // Header is reserved for P1 / N / P2, not drawn over the north goal.
                float width = Mathf.Max(0f, rect.width - 20f), height = Mathf.Max(0f, rect.height - 38f);
                float scale = Mathf.Min(width / world.width, height / world.height);
                Vector2 size = world.size * scale;
                Vector2 center = rect.center + Vector2.down * 9f;
                return new Rect(center - size * .5f, size);
            }
        }
        public Vector2 WorldToMap(Vector2 groundPosition)
        {
            Rect world = WorldRect, map = MapRect;
            return map.center + (groundPosition - world.center) * (map.width / world.width);
        }
        public Vector2 FacingToMap(Vector2 facing) => facing.sqrMagnitude > .000001f ? facing.normalized : Vector2.up;

        public void SetState(DashArenaState state, int localSeat)
        {
            _state = state; _localSeat = Mathf.Clamp(localSeat, 0, 1);
            // A rollback may change the same tick and object. Never cache by tick alone.
            SetVerticesDirty();
        }
        protected override void Awake() { base.Awake(); raycastTarget = false; }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Quad(mesh, rectTransform.rect, _background);
            Rect field = GroundRect(Vector2.zero, new Vector2(Rules.HalfWidth, Rules.HalfHeight));
            Quad(mesh, field, _field);
            Outline(mesh, field, _outline, 1.5f);
            Line(mesh, WorldToMap(new Vector2(-Rules.HalfWidth, 0)), WorldToMap(new Vector2(Rules.HalfWidth, 0)), _outline, .8f);
            for (int seat = 0; seat < 2; seat++)
            {
                float sign = seat == 0 ? 1f : -1f;
                Rect goal = GroundRect(new Vector2(0f, sign * (Rules.HalfHeight + 1.1f)), new Vector2(Rules.GoalWidth * .5f, 1.1f));
                Quad(mesh, goal, seat == 0 ? _playerOne : _playerTwo);
                Outline(mesh, goal, Color.white, .8f);
            }
            float scale = MapRect.width / WorldRect.width;
            foreach (ArenaPillarDefinition pillar in Rules.Pillars)
                Ring(mesh, WorldToMap(pillar.Position), pillar.Radius * scale, _outline, 1.5f);
            for (int i = 0; i < Rules.Walls.Count; i++)
            {
                Rect wall = GroundRect(Rules.Walls[i].Position, Rules.Walls[i].HalfSize);
                DashWallState state = _state == null ? default : _state.Walls[i];
                if (!state.Broken) { Quad(mesh, wall, _wall); Outline(mesh, wall, _outline, .8f); }
                else if (state.RepairPending) Outline(mesh, wall, _repair, 1.5f);
                // A destroyed, not-yet-repairing wall is genuinely absent, not solid.
            }
            if (_state == null) return;
            foreach (DashBallState ball in _state.Balls)
            {
                if (!ball.Active) continue;
                Vector2 at = WorldToMap(ball.Position);
                Disc(mesh, at, 3.5f, _background); Disc(mesh, at, 2.2f, _ball);
            }
            for (int seat = 0; seat < _state.Players.Length; seat++)
            {
                DashPlayerState player = _state.Players[seat];
                if (!player.Alive) continue;
                Vector2 at = WorldToMap(player.Position), facing = FacingToMap(player.Facing);
                if (seat == _localSeat) Ring(mesh, at, 7.6f, Color.white, 1.2f);
                Arrow(mesh, at, facing, 7f, _background);
                Arrow(mesh, at, facing, 5.1f, seat == 0 ? _playerOne : _playerTwo);
            }
        }
        int WallCount(int kind)
        {
            if (_state == null) return kind == 0 ? Rules.Walls.Count : 0;
            int count = 0;
            foreach (DashWallState wall in _state.Walls)
                if (kind == 0 ? !wall.Broken : kind == 1 ? wall.Broken && wall.RepairPending : wall.Broken && !wall.RepairPending) count++;
            return count;
        }
        Rect GroundRect(Vector2 position, Vector2 halfSize)
        {
            Vector2 min = WorldToMap(position - halfSize), max = WorldToMap(position + halfSize);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        void Quad(VertexHelper mesh, Rect rect, Color shade)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector3(rect.xMin, rect.yMin), shade * color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMin, rect.yMax), shade * color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMax), shade * color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMin), shade * color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
        void Line(VertexHelper mesh, Vector2 a, Vector2 b, Color shade, float thickness)
        {
            Vector2 delta = b - a;
            if (delta.sqrMagnitude < .00001f) return;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (thickness * .5f);
            int start = mesh.currentVertCount;
            mesh.AddVert(a - normal, shade * color, Vector2.zero); mesh.AddVert(a + normal, shade * color, Vector2.zero);
            mesh.AddVert(b + normal, shade * color, Vector2.zero); mesh.AddVert(b - normal, shade * color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
        void Outline(VertexHelper mesh, Rect rect, Color shade, float thickness)
        {
            Vector2 a = new Vector2(rect.xMin, rect.yMin), b = new Vector2(rect.xMin, rect.yMax),
                c = new Vector2(rect.xMax, rect.yMax), d = new Vector2(rect.xMax, rect.yMin);
            Line(mesh, a, b, shade, thickness); Line(mesh, b, c, shade, thickness);
            Line(mesh, c, d, shade, thickness); Line(mesh, d, a, shade, thickness);
        }
        void Ring(VertexHelper mesh, Vector2 at, float radius, Color shade, float thickness)
        {
            for (int i = 0; i < 16; i++) Line(mesh, at + Direction(i) * radius, at + Direction(i + 1) * radius, shade, thickness);
        }
        void Disc(VertexHelper mesh, Vector2 at, float radius, Color shade)
        {
            for (int i = 0; i < 16; i++) Triangle(mesh, at, at + Direction(i + 1) * radius, at + Direction(i) * radius, shade);
        }
        static Vector2 Direction(int segment)
        { float angle = segment * Mathf.PI / 8f; return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)); }
        void Arrow(VertexHelper mesh, Vector2 at, Vector2 direction, float radius, Color shade)
        {
            Vector2 side = new Vector2(-direction.y, direction.x);
            Triangle(mesh, at + direction * radius, at - direction * radius * .6f + side * radius * .65f,
                at - direction * radius * .6f - side * radius * .65f, shade);
        }
        void Triangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Color shade)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(a, shade * color, Vector2.zero); mesh.AddVert(b, shade * color, Vector2.zero); mesh.AddVert(c, shade * color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
        }
    }
}
