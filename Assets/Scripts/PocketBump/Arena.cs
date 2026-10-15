using System.Collections.Generic;
using UnityEngine;

// 构建场地墙体、球门、中央挡板和弹珠显示对象。
// 几何尺寸全部取自 ArenaLayout，确保场景模型与 MatchSim 的碰撞边界一致。
namespace PocketBump
{
    public sealed class Arena : MonoBehaviour
    {
        public Collider2D GateCollider;
        public Renderer GateRenderer;
        public Pocket NorthPocket;
        public Pocket SouthPocket;
        public Pocket EastPocket;
        public Pocket WestPocket;
        public Transform MarbleRoot;
        public PhysicsMaterial2D BounceMat;
        bool _gateOpen;
        GameObject _gateArt;

        // 动态构建路径供测试和缺少烘焙场景时使用；生成物仍读取统一布局参数。
        public static Arena Build(Transform parent)
        {
            var root = new GameObject("Arena");
            root.transform.SetParent(parent, false);
            var arena = root.AddComponent<Arena>();
            arena.BounceMat = CreateBounceMat();
            arena.BuildWalls(root.transform);
            arena.BuildGate(root.transform);
            arena.NorthPocket = arena.BuildMarblePocket(root.transform, "NorthPocket", true, 0);
            arena.SouthPocket = arena.BuildMarblePocket(root.transform, "SouthPocket", false, 1);
            arena.EastPocket = arena.BuildDunkPocket(root.transform, "EastPocket", true);
            arena.WestPocket = arena.BuildDunkPocket(root.transform, "WestPocket", false);
            arena.MarbleRoot = new GameObject("Marbles").transform;
            arena.MarbleRoot.SetParent(root.transform, false);
            arena.ApplyArtTheme();
            arena.SetGateOpen(false);
            return arena;
        }

        static PhysicsMaterial2D CreateBounceMat()
        {
            PhysicsMaterial2D saved = Resources.Load<PhysicsMaterial2D>("PocketBump/BumpBounce");
            if (saved != null) return saved;
            var mat = new PhysicsMaterial2D("BumpBounce")
            {
                bounciness = ArenaLayout.Bounciness,
                friction = 0.12f
            };
            return mat;
        }

        // 南北墙为弹珠球门留缺口，东西墙为玩家 dunk 口留缺口；切分须与 MatchSim AABB 一致。
        void BuildWalls(Transform parent)
        {
            float w = ArenaLayout.HalfWidth;
            float h = ArenaLayout.HalfHeight;
            float t = ArenaLayout.Wall;
            float gapN = ArenaLayout.MarblePocketWidth * 0.5f;
            float gapE = ArenaLayout.DunkPocketHeight * 0.5f;

            // North wall split around marble pocket.
            MakeWall(parent, "WallN-L", new Vector2((-w - gapN) * 0.5f, h + t * 0.5f), new Vector2(w - gapN, t));
            MakeWall(parent, "WallN-R", new Vector2((w + gapN) * 0.5f, h + t * 0.5f), new Vector2(w - gapN, t));
            MakeWall(parent, "WallS-L", new Vector2((-w - gapN) * 0.5f, -h - t * 0.5f), new Vector2(w - gapN, t));
            MakeWall(parent, "WallS-R", new Vector2((w + gapN) * 0.5f, -h - t * 0.5f), new Vector2(w - gapN, t));
            MakeWall(parent, "WallE-T", new Vector2(w + t * 0.5f, (h + gapE) * 0.5f), new Vector2(t, h - gapE));
            MakeWall(parent, "WallE-B", new Vector2(w + t * 0.5f, (-h - gapE) * 0.5f), new Vector2(t, h - gapE));
            MakeWall(parent, "WallW-T", new Vector2(-w - t * 0.5f, (h + gapE) * 0.5f), new Vector2(t, h - gapE));
            MakeWall(parent, "WallW-B", new Vector2(-w - t * 0.5f, (-h - gapE) * 0.5f), new Vector2(t, h - gapE));

            float tableT = ArenaLayout.TableThickness;
            MeshFactory.Cube(
                parent,
                "Floor",
                new Vector3(0f, 0f, tableT * 0.5f + 0.03f),
                new Vector3(w * 2f, h * 2f, tableT),
                VisualPalette.Tabletop);

            const int gridStep = 3;
            int yMax = (Mathf.FloorToInt(h) / gridStep) * gridStep;
            if (yMax >= h) yMax -= gridStep;
            int xMax = (Mathf.FloorToInt(w) / gridStep) * gridStep;
            if (xMax >= w) xMax -= gridStep;

            for (int y = -yMax; y <= yMax; y += gridStep)
            {
                MeshFactory.Cube(
                    parent,
                    $"GridH_{y}",
                    new Vector3(0f, y, -0.02f),
                    new Vector3(w * 2f, 0.04f, 0.04f),
                    VisualPalette.TabletopAccent);
            }

            for (int x = -xMax; x <= xMax; x += gridStep)
            {
                MeshFactory.Cube(
                    parent,
                    $"GridV_{x}",
                    new Vector3(x, 0f, -0.02f),
                    new Vector3(0.04f, h * 2f, 0.04f),
                    VisualPalette.TabletopAccent);
            }
        }

        // 开球倒计时用中央挡板隔开双方；MatchSim 决定时机，本对象只同步显示和兼容碰撞体。
        void BuildGate(Transform parent)
        {
            var go = new GameObject("CenterGate");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, -ArenaLayout.WallHeight * 0.5f);
            go.transform.localScale = new Vector3(ArenaLayout.HalfWidth * 2f - 0.2f, 0.22f, ArenaLayout.WallHeight);
            go.layer = GameLayers.Gate;
            GateRenderer = MeshFactory.AddCube(go, VisualPalette.Gate);
            var col = go.AddComponent<BoxCollider2D>();
            GateCollider = col;
        }

        Pocket BuildMarblePocket(Transform parent, string name, bool north, int owner)
        {
            float y = north ? ArenaLayout.HalfHeight : -ArenaLayout.HalfHeight;
            float sign = north ? 1f : -1f;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector2(0f, y);

            Color ownerColor = VisualPalette.PlayerColor(owner);
            var blocker = MeshFactory.Cube(
                root.transform,
                "PlayerBlocker",
                new Vector3(0f, 0f, -ArenaLayout.WallHeight * 0.5f),
                new Vector3(ArenaLayout.MarblePocketWidth, ArenaLayout.Wall, ArenaLayout.WallHeight),
                VisualPalette.WithAlpha(ownerColor, 0.55f));
            blocker.gameObject.layer = GameLayers.BlockPlayers;
            blocker.gameObject.AddComponent<BoxCollider2D>();

            var triggerGo = new GameObject("Trigger");
            triggerGo.transform.SetParent(root.transform, false);
            triggerGo.transform.localPosition = new Vector3(0f, sign * (ArenaLayout.Wall + 0.55f), 0f);
            triggerGo.layer = GameLayers.GoalNS;
            var box = triggerGo.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(ArenaLayout.MarblePocketWidth, 1.1f);
            var pocket = triggerGo.AddComponent<Pocket>();
            pocket.Kind = PocketKind.MarbleGoal;
            pocket.OwnerIndex = owner;

            MeshFactory.Cube(
                root.transform,
                "Lip",
                new Vector3(0f, sign * 0.55f, -ArenaLayout.WallHeight * 0.45f),
                new Vector3(ArenaLayout.MarblePocketWidth + 0.4f, 0.2f, ArenaLayout.WallHeight * 0.9f),
                ownerColor);
            return pocket;
        }

        Pocket BuildDunkPocket(Transform parent, string name, bool east)
        {
            float x = east ? ArenaLayout.HalfWidth : -ArenaLayout.HalfWidth;
            float sign = east ? 1f : -1f;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector2(x, 0f);

            var blocker = MeshFactory.Cube(
                root.transform,
                "MarbleBlocker",
                new Vector3(0f, 0f, -ArenaLayout.WallHeight * 0.5f),
                new Vector3(ArenaLayout.Wall, ArenaLayout.DunkPocketHeight, ArenaLayout.WallHeight),
                VisualPalette.WithAlpha(VisualPalette.Danger, 0.55f));
            blocker.gameObject.layer = GameLayers.BlockMarbles;
            blocker.gameObject.AddComponent<BoxCollider2D>();

            var triggerGo = new GameObject("Trigger");
            triggerGo.transform.SetParent(root.transform, false);
            triggerGo.transform.localPosition = new Vector3(sign * (ArenaLayout.Wall + 0.55f), 0f, 0f);
            triggerGo.layer = GameLayers.DunkEW;
            var box = triggerGo.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(1.1f, ArenaLayout.DunkPocketHeight);
            var pocket = triggerGo.AddComponent<Pocket>();
            pocket.Kind = PocketKind.Dunk;
            pocket.OwnerIndex = -1;

            MeshFactory.Cube(
                root.transform,
                "DangerLip",
                new Vector3(sign * 0.55f, 0f, -ArenaLayout.WallHeight * 0.45f),
                new Vector3(0.2f, ArenaLayout.DunkPocketHeight + 0.35f, ArenaLayout.WallHeight * 0.9f),
                VisualPalette.Danger);
            return pocket;
        }

        MeshRenderer MakeWall(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var renderer = MeshFactory.Cube(
                parent,
                name,
                new Vector3(pos.x, pos.y, -ArenaLayout.WallHeight * 0.5f),
                new Vector3(size.x, size.y, ArenaLayout.WallHeight),
                VisualPalette.Wall);
            renderer.gameObject.layer = GameLayers.Wall;
            var col = renderer.gameObject.AddComponent<BoxCollider2D>();
            col.sharedMaterial = BounceMat;
            return renderer;
        }

        public void SetGateOpen(bool open)
        {
            if (GateCollider != null) GateCollider.enabled = !open;
            if (GateRenderer != null) GateRenderer.enabled = !open;
            if (_gateArt != null) _gateArt.SetActive(!open);
            if (open && !_gateOpen)
                GameFx.Pulse(Vector2.zero, VisualPalette.Gate, 0.3f, 3f, 0.35f, -1, GameFxKind.Gate);
            _gateOpen = open;
        }

        public void ApplyArtTheme()
        {
            Quaternion modelRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.name.StartsWith("Wall"))
                    ArtCatalog.AttachModel(child.gameObject, "Models/Space/template-wall", VisualPalette.Wall,
                        modelRotation);
            }

            Transform floor = transform.Find("Floor");
            if (floor != null)
            {
                SpriteRenderer pattern = ArtCatalog.AddWorldSprite(
                    transform,
                    "KenneyFloorPattern",
                    "Patterns/arena_octagons",
                    VisualPalette.WithAlpha(VisualPalette.PlayerOne, 0.13f),
                    new Vector3(0f, 0f, -0.02f),
                    Vector3.one,
                    -5);
                pattern.drawMode = SpriteDrawMode.Tiled;
                pattern.size = new Vector2(ArenaLayout.HalfWidth * 2f, ArenaLayout.HalfHeight * 2f);
            }

            Transform gate = transform.Find("CenterGate");
            if (gate != null)
            {
                _gateArt = ArtCatalog.AttachModel(
                    gate.gameObject,
                    "Models/Space/gate-lasers",
                    VisualPalette.Gate,
                    modelRotation);
                if (_gateArt != null) _gateArt.SetActive(!_gateOpen);
            }

            ApplyPocketArt(NorthPocket, VisualPalette.PlayerOne, false);
            ApplyPocketArt(SouthPocket, VisualPalette.PlayerTwo, false);
            ApplyPocketArt(EastPocket, VisualPalette.Danger, true);
            ApplyPocketArt(WestPocket, VisualPalette.Danger, true);
        }

        static void ApplyPocketArt(Pocket pocket, Color color, bool sideways)
        {
            if (pocket == null || pocket.transform.parent == null) return;
            Transform root = pocket.transform.parent;
            Transform existing = root.Find("KenneyPocketArt");
            GameObject host = existing != null ? existing.gameObject : new GameObject("KenneyPocketArt");
            if (existing == null) host.transform.SetParent(root, false);
            host.transform.localPosition = pocket.transform.localPosition;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = sideways
                ? new Vector3(1.5f, ArenaLayout.DunkPocketHeight, ArenaLayout.WallHeight)
                : new Vector3(ArenaLayout.MarblePocketWidth, 1.5f, ArenaLayout.WallHeight);
            ArtCatalog.AttachModel(
                host,
                sideways ? "Models/Space/corridor-end" : "Models/Marble/funnel",
                color,
                Quaternion.Euler(90f, sideways ? 90f : 0f, 0f),
                false);
        }

        // 清理旧兼容对象；正式回合通常使用对象池与 SetActive，而不会重复创建/销毁。
        public void ClearMarbles()
        {
            MarbleMotor[] motors = FindObjectsOfType<MarbleMotor>();
            for (int i = 0; i < motors.Length; i++)
                motors[i].DetachForPool();

            if (MarbleRoot == null) return;
            for (int i = MarbleRoot.childCount - 1; i >= 0; i--)
            {
                GameObject marble = MarbleRoot.GetChild(i).gameObject;
                Rigidbody2D body = marble.GetComponent<Rigidbody2D>();
                if (body != null)
                {
                    body.velocity = Vector2.zero;
                    body.angularVelocity = 0f;
                }
                marble.SetActive(false);
            }
        }

        // 兼容旧测试/场景的立即生成入口，数量和初始排布仍遵守统一规则。
        public List<Rigidbody2D> SpawnMarbles(int count)
        {
            ClearMarbles();
            count = Mathf.Clamp(count, MatchSettings.MinMarbles, MatchSettings.MaxMarbles);
            EnsureMarblePool(count);
            var bodies = new List<Rigidbody2D>(count);
            float r = ArenaLayout.MarbleRadius;
            float spacing = r * 2.15f;
            for (int i = 0; i < count; i++)
            {
                Vector2 pos = ClusterOffset(i, count, spacing);
                GameObject marble = MarbleRoot.GetChild(i).gameObject;
                marble.SetActive(true);
                Rigidbody2D body = marble.GetComponent<Rigidbody2D>();
                body.position = pos;
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
                bodies.Add(body);
            }

            VerifyMarbles(bodies, r);
            return bodies;
        }

        // 只补足缺少的对象；多余对象由 ApplyViews 隐藏，避免回合切换产生频繁 GC。
        public void EnsureMarblePool(int count)
        {
            if (MarbleRoot == null) return;
            count = Mathf.Clamp(count, MatchSettings.MinMarbles, MatchSettings.MaxMarbles);
            while (MarbleRoot.childCount < count)
            {
                Rigidbody2D body = CreateMarble(ArenaLayout.MarbleCenter);
                body.gameObject.SetActive(false);
            }
        }

        // Rigidbody 不参与权威模拟，保留它只为旧组件 API 和场景引用兼容。
        public Rigidbody2D CreateMarble(Vector2 pos)
        {
            var go = new GameObject($"Marble_{MarbleRoot.childCount + 1:00}");
            go.transform.SetParent(MarbleRoot, false);
            go.transform.position = pos;
            go.layer = GameLayers.Marble;
            go.tag = "Marble";
            go.transform.localScale = Vector3.one * (ArenaLayout.MarbleRadius * 2f);
            MeshFactory.AddSphere(go, VisualPalette.Marble);
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            col.sharedMaterial = BounceMat;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.mass = ArenaLayout.MarbleMass;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.velocity = Vector2.zero;
            rb.simulated = false;
            var motor = go.AddComponent<MarbleMotor>();
            motor.SimIndex = MarbleRoot.childCount - 1;
            go.AddComponent<MarblePresentation>().Initialize();
            return rb;
        }

        static Vector2 ClusterOffset(int index, int count, float spacing)
        {
            if (count <= 1 || index == 0) return ArenaLayout.MarbleCenter;
            float angle = index * 2.399963f;
            float dist = spacing * Mathf.Sqrt(index);
            return ArenaLayout.MarbleCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
        }

        // 防御性检查生成点互不重叠，否则开局求解会平白注入碰撞能量。
        public static void VerifyMarbles(List<Rigidbody2D> marbles, float radius)
        {
            for (int i = 0; i < marbles.Count; i++)
            {
                if (marbles[i].velocity.sqrMagnitude > 0.0001f)
                    Debug.LogError($"Marble {i} spawned with velocity {marbles[i].velocity}");
                for (int j = i + 1; j < marbles.Count; j++)
                {
                    float d = Vector2.Distance(marbles[i].position, marbles[j].position);
                    if (d < radius * 2f - 0.02f)
                        Debug.LogError($"Marbles {i} and {j} overlap (d={d})");
                }
            }
        }
    }
}
