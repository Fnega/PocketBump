using System.Collections.Generic;
using UnityEngine;

// 集中加载精选 Kenney 资源，并把外部模型约束为纯表现子对象。
// 所有碰撞、计分和回滚状态仍由现有 MatchSim 与二维碰撞体负责。
namespace PocketBump
{
    public static class ArtCatalog
    {
        const string Root = "PocketBump/Art/Kenney/";

        static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

        public static Sprite Sprite(string relativePath, float pixelsPerUnit = 100f, Vector4 border = default)
        {
            string key = relativePath + "|" + pixelsPerUnit + "|" + border;
            if (Sprites.TryGetValue(key, out Sprite cached) && cached != null) return cached;

            Texture2D texture = Texture(relativePath);
            if (texture == null) return null;
            Sprite sprite = UnityEngine.Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                border);
            sprite.name = "Kenney_" + texture.name;
            Sprites[key] = sprite;
            return sprite;
        }

        public static Texture2D Texture(string relativePath)
        {
            if (Textures.TryGetValue(relativePath, out Texture2D cached) && cached != null) return cached;
            Texture2D texture = Resources.Load<Texture2D>(Root + relativePath);
            if (texture != null) Textures[relativePath] = texture;
            return texture;
        }

        public static Sprite KeySprite(KeyCode key)
        {
            string name;
            switch (key)
            {
                case KeyCode.W: name = "w"; break;
                case KeyCode.A: name = "a"; break;
                case KeyCode.S: name = "s"; break;
                case KeyCode.D: name = "d"; break;
                case KeyCode.UpArrow: name = "arrow_up"; break;
                case KeyCode.DownArrow: name = "arrow_down"; break;
                case KeyCode.LeftArrow: name = "arrow_left"; break;
                case KeyCode.RightArrow: name = "arrow_right"; break;
                case KeyCode.J: name = "j"; break;
                case KeyCode.K: name = "k"; break;
                case KeyCode.Alpha1: name = "1"; break;
                case KeyCode.Alpha2: name = "2"; break;
                case KeyCode.Keypad2: name = "2"; break;
                default: return null;
            }
            return Sprite("Input/keyboard_" + name, 64f);
        }

        public static Sprite FxSprite(GameFxKind kind)
        {
            string path;
            switch (kind)
            {
                case GameFxKind.Collect: path = "VFX/star_05"; break;
                case GameFxKind.Fire: path = "VFX/muzzle_03"; break;
                case GameFxKind.Impact: path = "VFX/spark_04"; break;
                case GameFxKind.Score: path = "VFX/magic_02"; break;
                case GameFxKind.Gate: path = "VFX/flare_01"; break;
                default: path = "VFX/circle_04"; break;
            }
            return Sprite(path, 512f);
        }

        public static SpriteRenderer AddWorldSprite(
            Transform parent,
            string name,
            string relativePath,
            Color color,
            Vector3 localPosition,
            Vector3 localScale,
            int sortingOrder)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name);
            if (existing == null) go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = Sprite(relativePath, 512f);
            renderer.material = VisualPalette.SpriteMaterial();
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        // 将外部 FBX 归一化到宿主局部单位盒，并移除其中任何碰撞组件。
        public static GameObject AttachModel(
            GameObject host,
            string relativePath,
            Color color,
            Quaternion localRotation,
            bool replaceHostRenderer = true)
        {
            if (host == null) return null;
            Transform existing = host.transform.Find("KenneyVisual");
            if (existing != null)
            {
                if (replaceHostRenderer)
                {
                    MeshRenderer existingHostRenderer = host.GetComponent<MeshRenderer>();
                    if (existingHostRenderer != null) existingHostRenderer.enabled = false;
                }
                return existing.gameObject;
            }

            // 宿主已有的碰撞体属于玩法层；即使模型导入层级出现异常，也绝不允许清理到它们。
            var protected3DColliders = new HashSet<Collider>(host.GetComponentsInChildren<Collider>(true));
            var protected2DColliders = new HashSet<Collider2D>(host.GetComponentsInChildren<Collider2D>(true));
            GameObject prefab = Resources.Load<GameObject>(Root + relativePath);
            if (prefab == null) return null;
            GameObject visual = Object.Instantiate(prefab, host.transform);
            visual.name = "KenneyVisual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = localRotation;
            visual.transform.localScale = Vector3.one;

            Collider[] colliders3D = visual.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders3D.Length; i++)
                if (!protected3DColliders.Contains(colliders3D[i])) DestroySafe(colliders3D[i]);
            Collider2D[] colliders2D = visual.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders2D.Length; i++)
                if (!protected2DColliders.Contains(colliders2D[i])) DestroySafe(colliders2D[i]);

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                if (materials.Length == 0) materials = new Material[1];
                for (int m = 0; m < materials.Length; m++) materials[m] = MeshFactory.CreateLit(color, 0.5f);
                renderers[i].sharedMaterials = materials;
                if (renderers[i] is MeshRenderer meshRenderer)
                {
                    meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    meshRenderer.receiveShadows = false;
                }
            }

            Bounds bounds = LocalBounds(host.transform, visual.transform);
            Vector3 size = bounds.size;
            Vector3 scale = new Vector3(
                size.x > 0.0001f ? 1f / size.x : 1f,
                size.y > 0.0001f ? 1f / size.y : 1f,
                size.z > 0.0001f ? 1f / size.z : 1f);
            visual.transform.localScale = scale;
            visual.transform.localPosition = -Vector3.Scale(bounds.center, scale);

            if (replaceHostRenderer)
            {
                MeshRenderer hostRenderer = host.GetComponent<MeshRenderer>();
                if (hostRenderer != null) hostRenderer.enabled = false;
            }
            return visual;
        }

        static Bounds LocalBounds(Transform host, Transform visual)
        {
            MeshFilter[] filters = visual.GetComponentsInChildren<MeshFilter>(true);
            bool found = false;
            Bounds result = default;
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null) continue;
                Bounds bounds = mesh.bounds;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    point = host.InverseTransformPoint(filters[i].transform.TransformPoint(point));
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                    else result.Encapsulate(point);
                }
            }
            return found ? result : new Bounds(Vector3.zero, Vector3.one);
        }

        static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
