using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PocketBump.Editor
{
    public static class DashArenaArtSampleBaker
    {
        public const string AssetRoot = "Assets/PocketBumpDashArt";
        public const string ScenePath = "Assets/Scenes/DashArenaArtSample.unity";
        public const string ResultsRoot = "Tools/DashArenaArtSampleResults";
        public const string FontPath = AssetRoot + "/Fonts/NotoSansCJKsc-Regular.otf";

        sealed class Palette
        {
            public Material Cream, CreamTile, Mint, MintDark, Porcelain, Cyan, Orange, Graphite, Eye;
            public Material Coral, Yellow, Green, Blue, Purple, Bell, PurpleAura, GreenAura, CyanTrail, OrangeTrail, BallTrail, BluePulse, MintPulse;
            public readonly List<Material> All = new List<Material>();
            public Mesh Ring, Star, BellMesh;
        }

        public static void BuildAndCapture()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("BuildAndCapture is a batch-only entry.");
            BuildSample();
            CapturePreview();
        }

        [MenuItem("Tools/Pocket Bump/Art Sample/Create Independent Sample (Never Overwrite)")]
        public static void CreateFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildSample();
        }

        [MenuItem("Tools/Pocket Bump/Art Sample/Open Independent Sample")]
        public static void OpenFromMenu()
        {
            if (!File.Exists(ScenePath)) throw new FileNotFoundException("Create the independent art sample first.", ScenePath);
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        public static void BuildSample()
        {
            RequireNewTargets();
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring.");
            Font approvedFont = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (approvedFont == null) throw new FileNotFoundException("Import the separately approved Chinese font before building the sample.", FontPath);
            ValidateFontGlyphs(approvedFont);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnsureFolder("Assets", "PocketBumpDashArt");
            EnsureFolder(AssetRoot, "Materials");
            EnsureFolder(AssetRoot, "Meshes");
            EnsureFolder(AssetRoot, "Prefabs");
            Palette palette = CreatePalette();
            ArenaRulesProfile rules = ArenaRulesProfile.CreateProductionV1();
            GameObject robotCyan = SavePrefab("Robot_Cyan", () => MakeRobot("Robot_Cyan", palette.Cyan, palette.CyanTrail, palette));
            GameObject robotOrange = SavePrefab("Robot_Orange", () => MakeRobot("Robot_Orange", palette.Orange, palette.OrangeTrail, palette));
            GameObject ballPrefab = SavePrefab("FreeScoringBall", () => MakeBall(palette, rules.BallRadius));
            GameObject pillarPrefab = SavePrefab("MintPillar", () => MakePillar(palette, rules.Pillars[0].Radius));
            GameObject wallPrefab = SavePrefab("RepairableWall", () => MakeWall(palette, rules.Walls[0].HalfSize));
            var buffPrefabs = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                int kind = i;
                buffPrefabs[i] = SavePrefab(((DashPickupKind)i) + "Buff", () => MakeBuff(kind, palette));
            }

            var root = new GameObject("INDEPENDENT SILENT ART SAMPLE - AWAITING G3 REVIEW");
            var sample = root.AddComponent<DashArenaArtSample>();
            sample.PreviewFont = approvedFont;
            Transform world = Empty(root.transform, "Authored Arena - physics comes from profile");
            MakeArena(world, rules, palette);
            var refs = new DashArenaArtSample.SceneReferences
            {
                Players = new DashArenaArtSample.RobotView[2], Balls = new DashArenaArtSample.BallView[8],
                Pillars = new DashArenaArtSample.PillarView[rules.Pillars.Count],
                Walls = new DashArenaArtSample.WallView[rules.Walls.Count],
                Pickups = new DashArenaArtSample.PickupView[rules.PickupLimit],
                GameplayCameras = new Camera[2], ThemeMaterials = palette.All.ToArray(), Pulses = new DashArenaArtSample.PulseView[8]
            };
            for (int seat = 0; seat < 2; seat++)
            {
                Transform robot = Instance(seat == 0 ? robotCyan : robotOrange, world, "Player " + (seat + 1), Ground(rules.SpawnForSeat(seat), 0f));
                robot.rotation = Quaternion.Euler(0f, seat == 0 ? 180f : 0f, 0f);
                refs.Players[seat] = BindRobot(robot);
            }
            for (int i = 0; i < 8; i++)
            {
                Transform ball = Instance(ballPrefab, world, "Ball visual slot " + i, Ground(i == 0 ? rules.BallSpawn(0) : Vector2.zero, rules.BallRadius));
                ball.gameObject.SetActive(i == 0);
                refs.Balls[i] = new DashArenaArtSample.BallView { Root = ball, Trail = ball.Find("Speed trail").GetComponent<LineRenderer>() };
            }
            for (int i = 0; i < rules.Pillars.Count; i++)
            {
                ArenaPillarDefinition def = rules.Pillars[i];
                Transform pillar = Instance(pillarPrefab, world, "Pillar " + def.Id, Ground(def.Position, 0f));
                refs.Pillars[i] = new DashArenaArtSample.PillarView { Id = def.Id, Root = pillar, Bell = pillar.Find("Golden bell effect") };
            }
            for (int i = 0; i < rules.Walls.Count; i++)
            {
                ArenaWallDefinition def = rules.Walls[i];
                Transform wall = Instance(wallPrefab, world, "Wall " + def.Id, Ground(def.Position, 0f));
                refs.Walls[i] = new DashArenaArtSample.WallView
                { Id = def.Id, Root = wall, Solid = wall.Find("Solid"), Outline = wall.Find("Repair outline"), Debris = wall.Find("Visual debris") };
            }
            for (int i = 0; i < refs.Pickups.Length; i++)
            {
                Transform slot = Empty(world, "Pickup visual slot " + i);
                slot.position = Ground(rules.PickupCandidates[i % rules.PickupCandidates.Count], .65f);
                var icons = new Transform[3];
                for (int kind = 0; kind < 3; kind++)
                {
                    icons[kind] = Instance(buffPrefabs[kind], slot, ((DashPickupKind)kind).ToString(), Vector3.zero);
                    icons[kind].gameObject.SetActive(kind == i % 3);
                }
                refs.Pickups[i] = new DashArenaArtSample.PickupView { Root = slot, Icons = icons };
            }
            refs.Gate = Primitive(world, "Yellow kickoff gate", PrimitiveType.Cube, new Vector3(0f, .18f, 0f),
                new Vector3(rules.Width, .36f, rules.KickoffHalfDepth * 2f), palette.Yellow);
            for (int i = 0; i < refs.Pulses.Length; i++) refs.Pulses[i] = MakePulsePoolSlot(world, i, palette);
            for (int seat = 0; seat < 2; seat++)
            {
                Camera camera = CameraObject(root.transform, "P" + (seat + 1) + " follow viewport", new Rect(seat * .5f, 0f, .5f, 1f), 10.5f);
                Vector3 focus = Ground(rules.SpawnForSeat(seat), 0f);
                camera.transform.position = focus + new Vector3(0f, 20f, -12f);
                camera.transform.LookAt(focus);
                if (seat == 0) camera.gameObject.AddComponent<AudioListener>();
                refs.GameplayCameras[seat] = camera;
            }
            refs.ShowcaseCamera = BuildShowcase(root.transform, palette, robotCyan, robotOrange, ballPrefab, pillarPrefab, wallPrefab, buffPrefabs);
            MakeLighting(root.transform);
            sample.Configure(refs);
            EditorUtility.SetDirty(sample);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save independent art sample.");
            AssetDatabase.SaveAssets();
            WriteNew(AssetRoot + "/AUTHORING_AND_PROVENANCE.txt",
                "INDEPENDENT SILENT ART SAMPLE - NOT APPROVED FINAL ART\n" +
                "Original project-authored primitive composition and generated meshes; no external model, texture or audio downloads.\n" +
                "Materials are editable assets in Materials; robots, free ball, pillar, repairable wall and three Buff symbols are Prefabs.\n" +
                "Player visual details live under Motion; editing material assets or adding child details survives normal scene loading and Play Mode.\n" +
                "The explicit creation command refuses existing assets/scene. It never replaces MainMenu, Arena or DashArenaGreybox, and does not change Build Settings.\n" +
                "Gameplay uses ArenaRulesProfile.CreateProductionV1, not mesh colliders. All sample geometry is non-authoritative and has no physics colliders.\n" +
                "The separate display board is a staged model/effect comparison, not evidence of natural gameplay events.\n" +
                "Chinese HUD references the separately approved Noto Sans CJK SC Regular font. Its source and license record are retained under Fonts.\n" +
                "No sound or music is included. The golden bell is a visual effect only.\n" +
                "Controls: local P1 WASD + current Dash (default K), P2 arrows + current Dash (default top-row 2).\n");
            AssetDatabase.Refresh();
            Debug.Log("Created independent editable art sample: " + ScenePath + ". Formal scenes and Build Settings unchanged; G3 approval still required.");
        }

        static void RequireNewTargets()
        {
            var targets = new List<string> { ScenePath, AssetRoot + "/AUTHORING_AND_PROVENANCE.txt" };
            string[] materials = { "Cream foundation", "Warm ivory tiles", "Fresh mint", "Mint edge shade", "Porcelain trim and ball", "P1 cyan shell",
                "P2 tangerine shell", "Dark robot visor and joints", "Soft luminous eyes and symbols", "Coral death warning", "Yellow kickoff identity",
                "Green impact pickup", "Blue teleport pickup", "Purple armor pickup", "Golden bell overlay", "Purple armor overlay", "Green impact overlay",
                "Deep cyan Dash trail", "Deep orange Dash trail", "Warm scoring ball trail", "Blue teleport pulse", "Mint respawn pulse" };
            foreach (string name in materials) targets.Add(AssetRoot + "/Materials/" + name + ".mat");
            foreach (string name in new[] { "Robot_Cyan", "Robot_Orange", "FreeScoringBall", "MintPillar", "RepairableWall", "GreenBuff", "BlueBuff", "PurpleBuff" })
                targets.Add(AssetRoot + "/Prefabs/" + name + ".prefab");
            foreach (string name in new[] { "Unit rounded ring", "Original bell shaped shell", "Original five point symbol" })
                targets.Add(AssetRoot + "/Meshes/" + name + ".asset");
            foreach (string target in targets)
                if (File.Exists(target) || Directory.Exists(target) || AssetDatabase.LoadMainAssetAtPath(target) != null)
                    throw new IOException("Refusing to overwrite authored sample target: " + target);
        }

        static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
            {
                if (Directory.Exists(path)) AssetDatabase.Refresh();
                if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
            }
        }

        static Palette CreatePalette()
        {
            var p = new Palette();
            p.Cream = Mat(p, "Cream foundation", new Color(.79f, .76f, .64f));
            p.CreamTile = Mat(p, "Warm ivory tiles", new Color(.93f, .89f, .77f));
            p.Mint = Mat(p, "Fresh mint", new Color(.40f, .69f, .54f));
            p.MintDark = Mat(p, "Mint edge shade", new Color(.24f, .42f, .35f));
            p.Porcelain = Mat(p, "Porcelain trim and ball", new Color(.97f, .94f, .84f));
            p.Cyan = Mat(p, "P1 cyan shell", new Color(.015f, .66f, .78f), .48f);
            p.Orange = Mat(p, "P2 tangerine shell", new Color(1f, .37f, .055f), .48f);
            p.Graphite = Mat(p, "Dark robot visor and joints", new Color(.055f, .095f, .12f), .52f);
            p.Eye = Mat(p, "Soft luminous eyes and symbols", new Color(.73f, .98f, 1f), .25f, true);
            p.Coral = Mat(p, "Coral death warning", new Color(.95f, .23f, .17f));
            p.Yellow = Mat(p, "Yellow kickoff identity", new Color(1f, .79f, .15f));
            p.Green = Mat(p, "Green impact pickup", new Color(.24f, .87f, .12f), .55f);
            p.Blue = Mat(p, "Blue teleport pickup", new Color(.03f, .30f, .98f), .55f);
            p.Purple = Mat(p, "Purple armor pickup", new Color(.52f, .1f, .87f), .55f);
            p.Bell = Transparent(p, "Golden bell overlay", new Color(1f, .67f, .025f, .22f));
            p.PurpleAura = Transparent(p, "Purple armor overlay", new Color(.6f, .2f, 1f, .18f));
            p.GreenAura = Transparent(p, "Green impact overlay", new Color(.27f, 1f, .14f, .2f));
            p.CyanTrail = Transparent(p, "Deep cyan Dash trail", new Color(0f, .44f, .64f, .8f));
            p.OrangeTrail = Transparent(p, "Deep orange Dash trail", new Color(.89f, .19f, .015f, .8f));
            p.BallTrail = Transparent(p, "Warm scoring ball trail", new Color(1f, .7f, .25f, .62f));
            p.BluePulse = Transparent(p, "Blue teleport pulse", new Color(.08f, .42f, 1f, .82f));
            p.MintPulse = Transparent(p, "Mint respawn pulse", new Color(.14f, .76f, .51f, .82f));
            p.Ring = SaveMesh("Unit rounded ring", Lathe(new[] { new Vector2(1.035f, 0f), new Vector2(1.025f, .025f),
                new Vector2(1f, .035f), new Vector2(.975f, .025f), new Vector2(.965f, 0f), new Vector2(.975f, -.025f),
                new Vector2(1f, -.035f), new Vector2(1.025f, -.025f), new Vector2(1.035f, 0f) }, 40));
            p.BellMesh = SaveMesh("Original bell shaped shell", Lathe(new[] { new Vector2(1.46f, .07f),
                new Vector2(1.43f, .18f), new Vector2(1.27f, .36f), new Vector2(1.12f, 1.35f),
                new Vector2(.94f, 1.79f), new Vector2(.56f, 2.02f), new Vector2(.08f, 2.12f) }, 40));
            p.Star = SaveMesh("Original five point symbol", StarMesh());
            return p;
        }

        static Material Mat(Palette palette, string name, Color color, float smoothness = .22f, bool emissive = false)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("The sample needs the project's existing URP Lit shader.");
            var material = new Material(shader) { name = name, enableInstancing = true };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", .035f);
            if (emissive) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * .4f); }
            AssetDatabase.CreateAsset(material, AssetRoot + "/Materials/" + name + ".mat");
            palette.All.Add(material);
            return material;
        }

        static Material Transparent(Palette palette, string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("The sample needs the project's existing URP Unlit shader.");
            var material = new Material(shader) { name = name, renderQueue = (int)RenderQueue.Transparent };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            AssetDatabase.CreateAsset(material, AssetRoot + "/Materials/" + name + ".mat");
            palette.All.Add(material);
            return material;
        }

        static GameObject MakeRobot(string name, Material shell, Material trail, Palette p)
        {
            var root = new GameObject(name);
            Transform motion = Empty(root.transform, "Motion");
            Primitive(motion, "Round torso", PrimitiveType.Sphere, new Vector3(0f, .57f, 0f), new Vector3(.83f, .77f, .68f), shell);
            Primitive(motion, "Waist joint", PrimitiveType.Sphere, new Vector3(0f, .38f, 0f), new Vector3(.81f, .17f, .67f), p.Graphite);
            Primitive(motion, "Round helmet", PrimitiveType.Sphere, new Vector3(0f, 1.16f, 0f), new Vector3(1.21f, .9f, 1.04f), shell);
            Primitive(motion, "Dark face visor", PrimitiveType.Sphere, new Vector3(0f, 1.12f, .445f), new Vector3(.99f, .52f, .19f), p.Graphite);
            for (int side = -1; side <= 1; side += 2)
            {
                Primitive(motion, "Eye " + side, PrimitiveType.Sphere, new Vector3(side * .22f, 1.15f, .54f), new Vector3(.14f, .205f, .05f), p.Eye);
                Primitive(motion, "Ear " + side, PrimitiveType.Sphere, new Vector3(side * .57f, 1.12f, 0f), new Vector3(.13f, .31f, .30f), p.Porcelain);
                Transform arm = Empty(motion, side < 0 ? "Left arm" : "Right arm");
                arm.localPosition = new Vector3(side * .51f, .64f, 0f);
                Primitive(arm, "Joint", PrimitiveType.Sphere, Vector3.zero, Vector3.one * .23f, p.Graphite);
                Primitive(arm, "Rounded glove", PrimitiveType.Sphere, new Vector3(side * .075f, -.16f, .025f), Vector3.one * .31f, shell);
                Transform foot = Empty(motion, side < 0 ? "Left foot" : "Right foot");
                foot.localPosition = new Vector3(side * .22f, .13f, .025f);
                Primitive(foot, "Soft boot", PrimitiveType.Sphere, Vector3.zero, new Vector3(.32f, .25f, .47f), p.Graphite);
                Primitive(foot, "Boot cap", PrimitiveType.Sphere, new Vector3(0f, .01f, .10f), new Vector3(.28f, .17f, .24f), shell);
            }
            Primitive(motion, "Chest lamp", PrimitiveType.Sphere, new Vector3(0f, .63f, .337f), new Vector3(.20f, .17f, .06f), p.Porcelain);
            Primitive(motion, "Antenna stem", PrimitiveType.Cylinder, new Vector3(0f, 1.66f, -.04f), new Vector3(.07f, .10f, .07f), p.Graphite);
            Primitive(motion, "Antenna tip", PrimitiveType.Sphere, new Vector3(0f, 1.78f, -.04f), Vector3.one * .15f, shell);
            Ring(root.transform, "Identity footprint", .75f, .035f, shell, p);
            Transform green = Primitive(root.transform, "Green aura", PrimitiveType.Sphere, new Vector3(0f, .75f, 0f), new Vector3(1.65f, 1.5f, 1.65f), p.GreenAura);
            Transform purple = Primitive(root.transform, "Purple aura", PrimitiveType.Sphere, new Vector3(0f, .83f, 0f), new Vector3(1.85f, 1.85f, 1.85f), p.PurpleAura);
            green.gameObject.SetActive(false); purple.gameObject.SetActive(false);
            Transform stars = Empty(root.transform, "Orbiting stun stars");
            stars.localPosition = Vector3.up * 1.95f;
            Ring(stars, "Orbit ring", .52f, 0f, p.Yellow, p);
            for (int i = 0; i < 3; i++)
            {
                float angle = i * Mathf.PI * 2f / 3f;
                Transform star = MeshObject(stars, "Stun star " + i, p.Star, p.Yellow,
                    new Vector3(Mathf.Cos(angle) * .52f, .055f, Mathf.Sin(angle) * .52f), Vector3.one * .16f);
                star.localRotation = Quaternion.Euler(-90f, 0f, i * 25f);
            }
            stars.gameObject.SetActive(false);
            Line(root.transform, "Motion trail", trail, .1f, true);
            return root;
        }

        static GameObject MakeBall(Palette p, float radius)
        {
            var root = new GameObject("Free scoring ball");
            Primitive(root.transform, "Porcelain ball", PrimitiveType.Sphere, Vector3.zero, Vector3.one * radius * 2f, p.Porcelain);
            Transform stripe = Ring(root.transform, "Scoring ball mint seam", radius * .98f, 0f, p.MintDark, p);
            stripe.localRotation = Quaternion.Euler(65f, 20f, 0f);
            Line(root.transform, "Speed trail", p.BallTrail, .07f, true);
            return root;
        }

        static GameObject MakePillar(Palette p, float radius)
        {
            var root = new GameObject("Mint pillar");
            Primitive(root.transform, "Stone base", PrimitiveType.Cylinder, new Vector3(0f, .15f, 0f), new Vector3(radius * 2f, .15f, radius * 2f), p.Porcelain);
            Primitive(root.transform, "Mint body", PrimitiveType.Cylinder, new Vector3(0f, .91f, 0f), new Vector3(radius * 1.92f, .61f, radius * 1.92f), p.Mint);
            Primitive(root.transform, "Stone crown", PrimitiveType.Cylinder, new Vector3(0f, 1.64f, 0f), new Vector3(radius * 2f, .12f, radius * 2f), p.Porcelain);
            Primitive(root.transform, "Top inset", PrimitiveType.Cylinder, new Vector3(0f, 1.775f, 0f), new Vector3(radius * 1.72f, .015f, radius * 1.72f), p.CreamTile);
            Ring(root.transform, "Crown seam", radius * .93f, 1.77f, p.Cream, p);
            Transform bell = MeshObject(root.transform, "Golden bell effect", p.BellMesh, p.Bell, Vector3.zero, Vector3.one);
            Ring(bell, "Golden bell lip", 1.45f, .1f, p.Yellow, p);
            bell.gameObject.SetActive(false);
            return root;
        }

        static GameObject MakeWall(Palette p, Vector2 half)
        {
            var root = new GameObject("Repairable wall");
            Transform solid = Empty(root.transform, "Solid");
            for (int row = 0; row < 2; row++)
                for (int block = 0; block < 4; block++)
                    Primitive(solid, "Editable stone " + row + " " + block, PrimitiveType.Cube,
                        new Vector3(-half.x + (block + .5f) * half.x * .5f, .31f + row * .60f, 0f),
                        new Vector3(half.x * .5f - .035f, .58f, half.y * 2f), row == 1 && block % 2 == 0 ? p.Mint : p.Porcelain);
            Transform outline = Empty(root.transform, "Repair outline");
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Primitive(outline, "Long edge " + sign, PrimitiveType.Cube, new Vector3(0f, .035f, sign * half.y), new Vector3(half.x * 2f, .055f, .06f), p.Yellow);
                Primitive(outline, "Short edge " + sign, PrimitiveType.Cube, new Vector3(sign * half.x, .035f, 0f), new Vector3(.06f, .055f, half.y * 2f), p.Yellow);
            }
            outline.gameObject.SetActive(false);
            Transform debris = Empty(root.transform, "Visual debris");
            for (int i = 0; i < 9; i++)
            {
                Transform piece = Primitive(debris, "Noncolliding fragment " + i, PrimitiveType.Cube,
                    new Vector3((i - 4f) * .55f, .13f + i % 2 * .08f, Mathf.Sin(i * 2.7f) * .85f),
                    new Vector3(.42f + i % 3 * .08f, .27f, .36f), i % 3 == 0 ? p.Mint : p.Porcelain);
                piece.localRotation = Quaternion.Euler(i * 13f, i * 37f, i * 11f);
            }
            debris.gameObject.SetActive(false);
            return root;
        }

        static GameObject MakeBuff(int kind, Palette p)
        {
            var root = new GameObject(((DashPickupKind)kind) + " pickup");
            Material color = kind == 0 ? p.Green : kind == 1 ? p.Blue : p.Purple;
            Primitive(root.transform, "Colored orb", PrimitiveType.Sphere, Vector3.zero, Vector3.one * 1.05f, color);
            Transform symbol = Empty(root.transform, "Distinct effect symbol");
            symbol.localRotation = Quaternion.LookRotation(new Vector3(0f, .65f, -1f), Vector3.up);
            if (kind == 0) MeshObject(symbol, "Impact star", p.Star, p.Eye, Vector3.forward * .525f, Vector3.one * .32f);
            else if (kind == 1)
            {
                LineRenderer spiral = Line(symbol, "Teleport spiral", p.Eye, .055f, false);
                spiral.positionCount = 42;
                for (int i = 0; i < 42; i++)
                {
                    float t = i / 41f, angle = t * Mathf.PI * 4.3f;
                    spiral.SetPosition(i, new Vector3(Mathf.Cos(angle) * (.04f + .29f * t), Mathf.Sin(angle) * (.04f + .29f * t), .535f));
                }
            }
            else
            {
                LineRenderer shield = Line(symbol, "Armor shield", p.Eye, .055f, false);
                shield.loop = true;
                shield.positionCount = 7;
                shield.SetPositions(new[] { new Vector3(-.25f, .25f, .54f), new Vector3(0f, .19f, .54f), new Vector3(.25f, .25f, .54f),
                    new Vector3(.23f, -.02f, .54f), new Vector3(.15f, -.21f, .54f), new Vector3(0f, -.31f, .54f), new Vector3(-.23f, -.03f, .54f) });
            }
            return root;
        }

        static DashArenaArtSample.PulseView MakePulsePoolSlot(Transform parent, int index, Palette p)
        {
            Transform root = Empty(parent, "Bounded visual pulse slot " + index);
            var variants = new Transform[5];
            Material[] colors = { p.GreenAura, p.BluePulse, p.PurpleAura, p.BluePulse, p.MintPulse };
            for (int kind = 0; kind < variants.Length; kind++)
            {
                Transform variant = Empty(root, kind < 3 ? ((DashPickupKind)kind) + " pickup pulse" : kind == 3 ? "Teleport rings" : "Respawn rising rings");
                variants[kind] = variant;
                Ring(variant, "Ground pulse", .7f, .02f, colors[kind], p);
                if (kind < 3)
                    for (int spark = 0; spark < 3; spark++)
                    {
                        float angle = spark * Mathf.PI * 2f / 3f;
                        Transform star = MeshObject(variant, "Pickup glint " + spark, p.Star, colors[kind],
                            new Vector3(Mathf.Cos(angle) * .55f, .15f, Mathf.Sin(angle) * .55f), Vector3.one * .12f);
                        star.localRotation = Quaternion.Euler(-90f, 0f, spark * 25f);
                    }
                else if (kind == 3)
                {
                    Ring(variant, "Teleport outer ring", 1.05f, .07f, colors[kind], p);
                    Ring(variant, "Teleport elevated ring", .5f, .45f, colors[kind], p);
                }
                else
                {
                    Ring(variant, "Respawn middle ring", .58f, .48f, colors[kind], p);
                    Ring(variant, "Respawn upper ring", .4f, .98f, colors[kind], p);
                }
                variant.gameObject.SetActive(false);
            }
            root.gameObject.SetActive(false);
            return new DashArenaArtSample.PulseView { Root = root, Variants = variants };
        }

        static void MakeArena(Transform parent, ArenaRulesProfile rules, Palette p)
        {
            Primitive(parent, "Cream arena foundation", PrimitiveType.Cube, new Vector3(0f, -.27f, 0f), new Vector3(rules.Width, .5f, rules.Height), p.Cream);
            MakeTiles(parent, "Editable floor tiles", Vector3.zero, rules.Width, rules.Height, 4f, p);
            Ring(parent, "Center court mint ring", 4.5f, .026f, p.Mint, p);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float gh = rules.GoalWidth * .5f, dh = rules.DeathOpening * .5f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Primitive(parent, "Goal-side border " + sign + " " + side, PrimitiveType.Cube,
                        new Vector3(side * (rules.HalfWidth + gh) * .5f, .34f, sign * (rules.HalfHeight + rules.WallThickness * .5f)),
                        new Vector3(rules.HalfWidth - gh, .68f, rules.WallThickness), p.Mint);
                    Primitive(parent, "Long border " + sign + " " + side, PrimitiveType.Cube,
                        new Vector3(sign * (rules.HalfWidth + rules.WallThickness * .5f), .34f, side * (rules.HalfHeight + dh) * .5f),
                        new Vector3(rules.WallThickness, .68f, rules.HalfHeight - dh), p.Mint);
                }
                Primitive(parent, "Dark fall opening " + sign, PrimitiveType.Cube, new Vector3(sign * (rules.HalfWidth + 1.5f), -1.2f, 0f),
                    new Vector3(3f, 1.2f, rules.DeathOpening), p.Graphite);
                for (int stripe = 0; stripe < 12; stripe++)
                    Primitive(parent, "Coral hazard dash " + sign + " " + stripe, PrimitiveType.Cube,
                        new Vector3(sign * (rules.HalfWidth - .18f), .035f, -dh + (stripe + .5f) * rules.DeathOpening / 12f),
                        new Vector3(.30f, .07f, rules.DeathOpening / 12f * .62f), p.Coral);
                Material team = sign > 0 ? p.Cyan : p.Orange;
                Primitive(parent, "Goal mouth " + sign, PrimitiveType.Cube, new Vector3(0f, -.05f, sign * (rules.HalfHeight + .7f)), new Vector3(rules.GoalWidth, .15f, 1.4f), team);
                for (int side = -1; side <= 1; side += 2)
                    Primitive(parent, "Goal post " + sign + " " + side, PrimitiveType.Cube,
                        new Vector3(side * (gh + .18f), 1.25f, sign * (rules.HalfHeight + .2f)), new Vector3(.36f, 2.5f, .6f), p.Porcelain);
                Primitive(parent, "Goal lintel " + sign, PrimitiveType.Cube, new Vector3(0f, 2.42f, sign * (rules.HalfHeight + .2f)), new Vector3(rules.GoalWidth + .72f, .36f, .6f), team);
                Ring(parent, "Birth region " + sign, 2f, .04f, team, p).localPosition += Vector3.forward * sign * 18f;
            }
        }

        static void MakeTiles(Transform parent, string name, Vector3 origin, float width, float height, float size, Palette p)
        {
            Transform tiles = Empty(parent, name);
            tiles.localPosition = origin;
            int columns = Mathf.RoundToInt(width / size), rows = Mathf.RoundToInt(height / size);
            for (int x = 0; x < columns; x++)
                for (int z = 0; z < rows; z++)
                    Primitive(tiles, "Tile " + x + " " + z, PrimitiveType.Cube,
                        new Vector3(-width * .5f + (x + .5f) * size, -.005f, -height * .5f + (z + .5f) * size),
                        new Vector3(size - .025f, .045f, size - .025f), (x + z) % 5 == 0 ? p.Cream : p.CreamTile);
        }

        static Camera BuildShowcase(Transform parent, Palette p, GameObject cyan, GameObject orange, GameObject ball,
            GameObject pillar, GameObject wall, GameObject[] buffs)
        {
            Transform board = Empty(parent, "STAGED ART COMPARISON BOARD - NOT GAMEPLAY EVIDENCE");
            board.localPosition = new Vector3(68f, 0f, 0f);
            Primitive(board, "Display foundation", PrimitiveType.Cube, new Vector3(0f, -.3f, 0f), new Vector3(18f, .55f, 14f), p.MintDark);
            MakeTiles(board, "Display tiles", Vector3.zero, 18f, 14f, 2f, p);
            Ring(board, "Display court mark", 2.1f, .03f, p.Mint, p);
            Transform robot = Instance(cyan, board, "Cyan original robot", new Vector3(-3.1f, 0f, -.7f));
            robot.localRotation = Quaternion.Euler(0f, 145f, 0f);
            Transform rival = Instance(orange, board, "Orange original robot - staged stun", new Vector3(2.5f, 0f, .7f));
            rival.localRotation = Quaternion.Euler(0f, 165f, 0f);
            rival.Find("Orbiting stun stars").gameObject.SetActive(true);
            Instance(ball, board, "Free scoring ball", new Vector3(-1.25f, .3f, -1.45f));
            Transform bellPillar = Instance(pillar, board, "Pillar - staged golden bell", new Vector3(2.1f, 0f, 3f));
            bellPillar.Find("Golden bell effect").gameObject.SetActive(true);
            Transform broken = Instance(wall, board, "Wall - staged breakage", new Vector3(-3.1f, 0f, 4.15f));
            broken.Find("Solid").gameObject.SetActive(false);
            broken.Find("Visual debris").gameObject.SetActive(true);
            broken.Find("Repair outline").gameObject.SetActive(true);
            Instance(wall, board, "Intact wall silhouette", new Vector3(4.5f, 0f, 5.9f));
            Instance(buffs[0], board, "Green impact symbol", new Vector3(-6.3f, .75f, 2f));
            Instance(buffs[1], board, "Blue teleport symbol", new Vector3(.8f, .75f, -4.1f));
            Instance(buffs[2], board, "Purple armor symbol", new Vector3(6.1f, .75f, .15f));
            LineRenderer trail = Line(board, "Staged deep cyan Dash illustration", p.CyanTrail, .6f, false);
            trail.positionCount = 4;
            trail.SetPositions(new[] { new Vector3(-6.7f, .14f, 1.5f), new Vector3(-5.5f, .14f, .7f), new Vector3(-4.25f, .14f, 0f), new Vector3(-3.1f, .14f, -.7f) });
            for (int i = 0; i < 8; i++)
                Primitive(board, "Staged coral danger mark " + i, PrimitiveType.Cube,
                    new Vector3(-8.83f, .03f, -4.8f + i * .65f), new Vector3(.25f, .07f, .4f), p.Coral);
            Camera camera = CameraObject(parent, "Art board inspection camera (disabled in play)", new Rect(0f, 0f, 1f, 1f), 9.5f);
            camera.transform.position = new Vector3(80f, 17f, -20f);
            camera.transform.LookAt(new Vector3(68f, .1f, .6f));
            camera.enabled = false;
            return camera;
        }

        static void MakeLighting(Transform root)
        {
            Transform key = Empty(root, "Sample warm daylight");
            Light sun = key.gameObject.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.15f; sun.color = new Color(1f, .96f, .87f);
            sun.shadows = LightShadows.Soft; sun.shadowStrength = .55f; key.rotation = Quaternion.Euler(50f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.73f, .82f, .91f);
            RenderSettings.ambientEquatorColor = new Color(.64f, .71f, .66f);
            RenderSettings.ambientGroundColor = new Color(.4f, .43f, .36f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
        }

        static Camera CameraObject(Transform parent, string name, Rect rect, float halfHeight)
        {
            Camera camera = Empty(parent, name).gameObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = halfHeight; camera.rect = rect;
            camera.nearClipPlane = .1f; camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.78f, .84f, .79f);
            camera.allowHDR = false;
            return camera;
        }

        static DashArenaArtSample.RobotView BindRobot(Transform root)
        {
            Transform motion = root.Find("Motion");
            return new DashArenaArtSample.RobotView { Root = root, Motion = motion,
                LeftArm = motion.Find("Left arm"), RightArm = motion.Find("Right arm"), LeftFoot = motion.Find("Left foot"), RightFoot = motion.Find("Right foot"),
                GreenAura = root.Find("Green aura"), PurpleAura = root.Find("Purple aura"), Stars = root.Find("Orbiting stun stars"), Trail = root.Find("Motion trail").GetComponent<LineRenderer>() };
        }

        static GameObject SavePrefab(string name, Func<GameObject> create)
        {
            GameObject source = create();
            try
            {
                GameObject asset = PrefabUtility.SaveAsPrefabAsset(source, AssetRoot + "/Prefabs/" + name + ".prefab");
                if (asset == null) throw new IOException("Could not save sample prefab " + name);
                return asset;
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        static Transform Instance(GameObject prefab, Transform parent, string name, Vector3 localPosition)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name; instance.transform.localPosition = localPosition;
            return instance.transform;
        }

        static Transform Empty(Transform parent, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }

        static Transform Primitive(Transform parent, string name, PrimitiveType primitive, Vector3 at, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(primitive); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = at; go.transform.localScale = scale;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        static Transform MeshObject(Transform parent, string name, Mesh mesh, Material material, Vector3 at, Vector3 scale)
        {
            Transform root = Empty(parent, name); root.localPosition = at; root.localScale = scale;
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            return root;
        }

        static Transform Ring(Transform parent, string name, float radius, float height, Material material, Palette p)
            => MeshObject(parent, name, p.Ring, material, Vector3.up * height, Vector3.one * radius);

        static LineRenderer Line(Transform parent, string name, Material material, float width, bool worldSpace)
        {
            LineRenderer line = Empty(parent, name).gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material; line.useWorldSpace = worldSpace; line.widthMultiplier = width;
            line.numCapVertices = 5; line.numCornerVertices = 4; line.positionCount = 0;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            line.startColor = new Color(1f, 1f, 1f, .15f); line.endColor = Color.white;
            return line;
        }

        static Mesh SaveMesh(string name, Mesh mesh)
        {
            mesh.name = name; AssetDatabase.CreateAsset(mesh, AssetRoot + "/Meshes/" + name + ".asset"); return mesh;
        }

        static Mesh Lathe(Vector2[] profile, int segments)
        {
            var vertices = new Vector3[profile.Length * (segments + 1)];
            var triangles = new int[(profile.Length - 1) * segments * 6];
            int cursor = 0;
            for (int row = 0; row < profile.Length; row++)
                for (int col = 0; col <= segments; col++)
                {
                    float angle = col * Mathf.PI * 2f / segments;
                    vertices[row * (segments + 1) + col] = new Vector3(Mathf.Cos(angle) * profile[row].x, profile[row].y, Mathf.Sin(angle) * profile[row].x);
                    if (row == profile.Length - 1 || col == segments) continue;
                    int a = row * (segments + 1) + col, b = a + segments + 1;
                    triangles[cursor++] = a; triangles[cursor++] = b; triangles[cursor++] = a + 1;
                    triangles[cursor++] = a + 1; triangles[cursor++] = b; triangles[cursor++] = b + 1;
                }
            var mesh = new Mesh { vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        static Mesh StarMesh()
        {
            var vertices = new Vector3[11]; var triangles = new int[30];
            for (int i = 0; i < 10; i++)
            {
                float angle = Mathf.PI * .5f + i * Mathf.PI / 5f, radius = i % 2 == 0 ? 1f : .45f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = (i + 1) % 10 + 1;
            }
            var mesh = new Mesh { vertices = vertices, triangles = triangles }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        static Vector3 Ground(Vector2 point, float height) => DashArenaArtSample.Ground(point, height);

        public static void CapturePreview()
        {
            if (!Application.isBatchMode || EditorApplication.isPlaying) throw new InvalidOperationException("CapturePreview is an edit-mode batch entry.");
            if (!File.Exists(ScenePath)) throw new FileNotFoundException("Build the independent art sample first.", ScenePath);
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Actual Unity art capture requires graphics; omit -nographics.");
            string output = NewOutputDirectory();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            try
            {
                DashArenaArtSample sample = UnityEngine.Object.FindObjectOfType<DashArenaArtSample>();
                if (sample == null) throw new InvalidOperationException("The sample scene has no authored presentation controller.");
                Directory.CreateDirectory(output);
                CaptureCameras(new[] { sample.Views.ShowcaseCamera }, 1600, 1000, Path.Combine(output, "art-board-staged.png"));
                Camera inspection = sample.Views.ShowcaseCamera;
                Vector3 savedPosition = inspection.transform.position; Quaternion savedRotation = inspection.transform.rotation;
                float savedSize = inspection.orthographicSize;
                try
                {
                    // Same saved prefab instances, only closer framing; not a concept illustration.
                    inspection.transform.position = new Vector3(77f, 9f, -12f);
                    inspection.transform.LookAt(new Vector3(68f, .95f, .05f));
                    inspection.orthographicSize = 4.4f;
                    CaptureCameras(new[] { inspection }, 1600, 1000, Path.Combine(output, "robots-closeup-staged.png"));
                }
                finally
                {
                    inspection.transform.position = savedPosition; inspection.transform.rotation = savedRotation; inspection.orthographicSize = savedSize;
                }
                sample.AutomaticTicking = false; sample.ShowHud = false; sample.RestartPreview();
                for (int tick = 0; tick < 240; tick++)
                {
                    sample.StepPreview(new DashArenaInput { Move = Vector2.down }, new DashArenaInput { Move = Vector2.up });
                    sample.RenderPreview(1f);
                }
                CaptureCameras(sample.Views.GameplayCameras, 1600, 900, Path.Combine(output, "live-split-world-only.png"));
                WriteNew(Path.Combine(output, "capture-info.txt"),
                    "source=actual_Unity_Camera.Render\nscene=" + ScenePath + "\n" +
                    "art-board-staged.png=authored_static_model_and_effect_comparison; NOT natural gameplay evidence\n" +
                    "robots-closeup-staged.png=same_saved_robot_prefab_instances_with_close_camera; staged_model_review_only\n" +
                    "live-split-world-only.png=single_production_profile_simulation_scripted_midfield_approach_tick_240; NOT full_match_evidence\n" +
                    "HUD_not_included=Camera.Render excludes IMGUI; approved Noto font is referenced, but this image is not release glyph validation\n" +
                    "No final G3 approval, network presentation validation or release performance claim implied. Silent sample.\n" +
                    "Rules=" + sample.State.Rules.ContentHash + "\nGraphics=" + SystemInfo.graphicsDeviceName + "\n");
                Debug.Log("Saved actual Unity art sample captures to " + output + ". Await user G3 review before formal scene replacement.");
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }

        static void CaptureCameras(Camera[] cameras, int width, int height, string path)
        {
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                int partWidth = width / cameras.Length;
                for (int i = 0; i < cameras.Length; i++)
                {
                    Camera camera = cameras[i]; RenderTexture prior = camera.targetTexture;
                    Rect rect = camera.rect; float aspect = camera.aspect; bool enabled = camera.enabled;
                    RenderTexture target = RenderTexture.GetTemporary(partWidth, height, 24, RenderTextureFormat.ARGB32);
                    try
                    {
                        camera.enabled = false; camera.targetTexture = target; camera.rect = new Rect(0f, 0f, 1f, 1f);
                        camera.aspect = (float)partWidth / height; camera.Render(); RenderTexture.active = target;
                        image.ReadPixels(new Rect(0f, 0f, partWidth, height), i * partWidth, 0, false);
                    }
                    finally
                    {
                        camera.targetTexture = prior; camera.rect = rect; camera.aspect = aspect; camera.enabled = enabled;
                        RenderTexture.active = previousActive; RenderTexture.ReleaseTemporary(target);
                    }
                }
                image.Apply(false, false); byte[] bytes = image.EncodeToPNG();
                if (bytes == null || bytes.Length == 0) throw new IOException("No image data produced by Unity.");
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes, 0, bytes.Length);
            }
            finally { RenderTexture.active = previousActive; UnityEngine.Object.DestroyImmediate(image); }
        }

        static string NewOutputDirectory()
        {
            string root = Path.GetFullPath(ResultsRoot), requested = null;
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-artSampleOutput");
            if (index >= 0)
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal)) throw new ArgumentException("Missing -artSampleOutput value.");
                requested = args[index + 1];
            }
            string path = Path.GetFullPath(string.IsNullOrEmpty(requested) ? Path.Combine(root, "capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"))
                : Path.IsPathRooted(requested) ? requested : Path.Combine(root, requested));
            if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Art sample capture must stay inside " + root);
            if (Directory.Exists(path) || File.Exists(path)) throw new IOException("Refusing to overwrite existing art capture " + path);
            return path;
        }

        static void WriteNew(string path, string content)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream)) writer.Write(content);
        }

        static void ValidateFontGlyphs(Font font)
        {
            var characters = new HashSet<char>(DashArenaArtSample.RequiredChineseGlyphs);
            for (int code = 33; code <= 126; code++) characters.Add((char)code);
            var sorted = new List<char>(characters); sorted.Sort();
            string corpus = new string(sorted.ToArray());
            var report = new System.Text.StringBuilder();
            report.AppendLine("Actual Unity Font glyph preflight (not a rendered HUD screenshot)");
            report.AppendLine("Font: " + FontPath + " / " + font.name + " / dynamic=" + font.dynamic);
            report.AppendLine("Corpus: " + corpus);
            bool passed = true;
            foreach (int size in new[] { 12, 13, 17, 20 })
            {
                font.RequestCharactersInTexture(corpus, size, FontStyle.Normal);
                int missing = 0;
                foreach (char character in sorted)
                    if (!font.HasCharacter(character) || !font.GetCharacterInfo(character, out CharacterInfo info, size, FontStyle.Normal))
                    {
                        report.AppendLine("MISSING size=" + size + " U+" + ((int)character).ToString("X4") + " " + character);
                        missing++;
                    }
                report.AppendLine("Size " + size + ": " + (missing == 0 ? "PASS" : "FAIL") + ", checked=" + sorted.Count + ", missing=" + missing);
                passed &= missing == 0;
            }
            Directory.CreateDirectory(ResultsRoot);
            string path = Path.Combine(ResultsRoot, "font-validation-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
            WriteNew(path, report.ToString());
            if (!passed) throw new InvalidOperationException("Approved font failed actual glyph preflight. No sample assets created; inspect " + path);
            Debug.Log("Approved Chinese font glyph preflight passed: " + path);
        }

        [MenuItem("Tools/Pocket Bump/Art Sample/Capture Actual Running Sample With HUD")]
        public static void CaptureRunningSampleWithHud()
        {
            if (!EditorApplication.isPlaying || UnityEngine.Object.FindObjectOfType<DashArenaArtSample>() == null)
                throw new InvalidOperationException("Run the independent art sample before capturing its actual Game view.");
            Directory.CreateDirectory(ResultsRoot);
            string path = Path.GetFullPath(Path.Combine(ResultsRoot, "running-hud-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png"));
            if (File.Exists(path)) throw new IOException("Refusing to overwrite running capture " + path);
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("Requested actual running sample screenshot including HUD (saved at frame end): " + path);
        }
    }
}
