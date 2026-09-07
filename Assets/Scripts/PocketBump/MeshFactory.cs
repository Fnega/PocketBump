using UnityEngine;

namespace PocketBump
{
    /// Built-in primitives with 3D colliders stripped so 2D physics stays in charge.
    public static class MeshFactory
    {
        static Mesh _sphere;
        static Mesh _cube;
        static Mesh _cylinder;
        static Shader _litShader;
        static Material _litTemplate;


        public static void ReloadAssets()
        {
            _litShader = null;
            _litTemplate = null;
        }

        public static MeshRenderer AddSphere(GameObject host, Color color)
        {
            return Apply(host, SphereMesh, color);
        }

        public static MeshRenderer AddCube(GameObject host, Color color)
        {
            return Apply(host, CubeMesh, color);
        }

        public static MeshRenderer AddZCylinder(GameObject host, Color color)
        {
            host.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return Apply(host, CylinderMesh, color);
        }

        public static MeshRenderer Cube(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            GameObject go = EnsureChild(parent, name);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            return AddCube(go, color);
        }

        public static MeshRenderer Sphere(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            GameObject go = EnsureChild(parent, name);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            return AddSphere(go, color);
        }

        public static MeshRenderer ZCylinder(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            GameObject go = EnsureChild(parent, name);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            return AddZCylinder(go, color);
        }

        public static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            ApplyColor(renderer.sharedMaterial, color);
        }

        public static MeshRenderer Apply(GameObject host, Mesh mesh, Color color)
        {
            StripSprite(host);
            Strip3DColliders(host);

            var filter = host.GetComponent<MeshFilter>();
            if (filter == null) filter = host.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = host.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateLit(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        public static Material CreateLit(Color color, float emissionScale = 0.32f)
        {
            Material material = new Material(LitTemplate());
            ApplyColor(material, color, emissionScale);
            return material;
        }

        public static void ApplyColor(Material material, Color color, float emissionScale = 0.32f)
        {
            if (material == null) return;
            color.a = 1f;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", color * emissionScale);
        }

        static Material LitTemplate()
        {
            if (_litTemplate != null) return _litTemplate;
            _litTemplate = Resources.Load<Material>("PocketBump/NeonLit");
            if (_litTemplate != null) return _litTemplate;

            Shader shader = LitShader();
            _litTemplate = new Material(shader) { name = "PocketBump Neon Lit" };
            if (_litTemplate.HasProperty("_Smoothness")) _litTemplate.SetFloat("_Smoothness", 0.42f);
            if (_litTemplate.HasProperty("_Metallic")) _litTemplate.SetFloat("_Metallic", 0.08f);
            return _litTemplate;
        }

        static Shader LitShader()
        {
            if (_litShader != null) return _litShader;
            _litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (_litShader == null) _litShader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (_litShader == null) _litShader = Shader.Find("Sprites/Default");
            return _litShader;
        }

        static Mesh SphereMesh => _sphere != null ? _sphere : (_sphere = Grab(PrimitiveType.Sphere));
        static Mesh CubeMesh => _cube != null ? _cube : (_cube = Grab(PrimitiveType.Cube));
        static Mesh CylinderMesh => _cylinder != null ? _cylinder : (_cylinder = Grab(PrimitiveType.Cylinder));

        static Mesh Grab(PrimitiveType type)
        {
            var temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            return mesh;
        }

        static GameObject EnsureChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing.gameObject;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static void StripSprite(GameObject host)
        {
            var sprite = host.GetComponent<SpriteRenderer>();
            if (sprite != null) DestroySafe(sprite);
        }

        static void Strip3DColliders(GameObject host)
        {
            Collider[] colliders = host.GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
                DestroySafe(colliders[i]);
        }

        static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
