using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PocketBump.Tests
{
    public sealed class DashDemoBallMarkerTests
    {
        GameObject _root;
        Mesh _mesh;
        Material _material;
        static Type Utility => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("PocketBump.Editor.DashDemoReleaseUiIntegration", false)).FirstOrDefault(t => t != null);
        static object Invoke(string method, params object[] values)
        {
            Assert.That(Utility, Is.Not.Null);
            try { return Utility.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, values); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
        [SetUp] public void Setup()
        {
            _root = new GameObject("Ball marker geometry fixture");
            _mesh = (Mesh)Invoke("BuildMarkerMesh");
            _material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(_root); Object.DestroyImmediate(_mesh); Object.DestroyImmediate(_material);
        }

        [Test]
        public void EveryAnnulusTriangleFacesUpAndLeavesAVisibleGapOutsideBall()
        {
            Assert.That(_mesh.vertexCount, Is.EqualTo(96));
            Vector3[] positions = _mesh.vertices; int[] triangles = _mesh.triangles;
            Assert.That(triangles.Length, Is.EqualTo(288));
            foreach (Vector3 normal in _mesh.normals) Assert.That(normal, Is.EqualTo(Vector3.up));
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 cross = Vector3.Cross(positions[triangles[i + 1]] - positions[triangles[i]], positions[triangles[i + 2]] - positions[triangles[i]]);
                Assert.That(cross.y, Is.GreaterThan(.0001f), "A top-facing, non-degenerate triangle is required with the existing backface-culling material.");
            }
            foreach (Vector3 point in positions)
            {
                Assert.That(point.y, Is.Zero); Assert.That(point.magnitude, Is.InRange(.43999f, .52001f));
                Assert.That(point.magnitude - ArenaRulesProfile.CreateProductionV1().BallRadius, Is.GreaterThan(.13f));
            }
        }

        [Test]
        public void MigrationRepairsOnlyItsLineMarkerAndPreservesLaterAuthorEdits()
        {
            _root.transform.position = new Vector3(0f, .3f, 0f);
            var legacy = new GameObject("Demo scoring ball contrast ring"); legacy.transform.SetParent(_root.transform, false);
            legacy.transform.localPosition = new Vector3(0f, -.275f, 0f); legacy.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var line = legacy.AddComponent<LineRenderer>(); line.sharedMaterial = _material;
            var artistChild = new GameObject("Artist ball detail"); artistChild.transform.SetParent(_root.transform, false);
            artistChild.transform.localPosition = new Vector3(.1f, .2f, .3f);
            Assert.That((bool)Invoke("UpgradeBallMarker", _root.transform, _mesh, _material), Is.True);
            Assert.That(legacy.GetComponent<LineRenderer>(), Is.Null);
            Assert.That(legacy.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(_mesh));
            Assert.That(legacy.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(_material));
            Assert.That(legacy.transform.position.y, Is.EqualTo(.04f).Within(.00001f));
            Assert.That(legacy.transform.position.y, Is.GreaterThan(.0175f), "The actual authored floor tile top is y=.0175.");
            legacy.transform.localPosition += Vector3.up * .01f;
            Vector3 custom = legacy.transform.localPosition;
            Assert.That((bool)Invoke("UpgradeBallMarker", _root.transform, _mesh, _material), Is.False);
            Assert.That(legacy.transform.localPosition, Is.EqualTo(custom));
            Assert.That(artistChild.transform.localPosition, Is.EqualTo(new Vector3(.1f, .2f, .3f)));
            Assert.That(_root.GetComponentsInChildren<Collider>(), Is.Empty); Assert.That(_root.GetComponentsInChildren<Rigidbody>(), Is.Empty);
        }

        [Test]
        public void AuthoredCameraSeesDarkAnnulusPixelsAgainstPaleFloor()
        {
            _material.SetColor("_BaseColor", new Color(.055f, .095f, .12f));
            _material.SetFloat("_Cull", 2f); _material.SetFloat("_Smoothness", 0f);
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere); ball.transform.SetParent(_root.transform, false);
            ball.transform.localPosition = new Vector3(0f, .3f, 0f); ball.transform.localScale = Vector3.one * .6f;
            var surface = new Material(_material); surface.SetColor("_BaseColor", new Color(.96f, .95f, .8f));
            ball.GetComponent<Renderer>().sharedMaterial = surface;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(_root.transform, false);
            floor.transform.localPosition = new Vector3(0, -.005f, 0); floor.transform.localScale = new Vector3(4, .045f, 4);
            floor.GetComponent<Renderer>().sharedMaterial = surface;
            var marker = new GameObject("Rendered marker", typeof(MeshFilter), typeof(MeshRenderer)); marker.transform.SetParent(_root.transform, false);
            marker.transform.localPosition = Vector3.up * .04f; marker.GetComponent<MeshFilter>().sharedMesh = _mesh;
            marker.GetComponent<MeshRenderer>().sharedMaterial = _material;
            var cameraObject = new GameObject("Marker fixture camera", typeof(Camera)); cameraObject.transform.SetParent(_root.transform, false);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1.2f;
            camera.transform.position = new Vector3(0, 20, -12); camera.transform.rotation = Quaternion.LookRotation(-camera.transform.position, Vector3.up);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.white;
            var lampObject = new GameObject("Marker fixture light", typeof(Light)); lampObject.transform.SetParent(_root.transform, false);
            Light lamp = lampObject.GetComponent<Light>(); lamp.type = LightType.Directional; lamp.intensity = 1.5f; lamp.shadows = LightShadows.None;
            lamp.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            // Keep loaded scene geometry out of this render without modifying its state.
            foreach (Transform node in _root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
            camera.cullingMask = lamp.cullingMask = 1 << 30;
            RenderTexture target = RenderTexture.GetTemporary(256, 256, 24); RenderTexture previous = RenderTexture.active;
            var pixels = new Texture2D(256, 256, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); pixels.Apply(); Color32[] visible = pixels.GetPixels32();
                marker.SetActive(false); camera.Render(); pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); pixels.Apply(); Color32[] hidden = pixels.GetPixels32();
                int darker = 0;
                for (int i = 0; i < visible.Length; i++)
                    if ((hidden[i].r + hidden[i].g + hidden[i].b) - (visible[i].r + visible[i].g + visible[i].b) > 70) darker++;
                Assert.That(darker, Is.GreaterThan(250), "Actual rendered pixels must show the dark ring; serialized transforms alone do not prove visibility.");
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(pixels); Object.DestroyImmediate(surface);
            }
        }
    }
}
