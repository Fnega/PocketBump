using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketBump.Tests
{
    public sealed class DashGroundReadinessPersistenceTests
    {
        static void Invoke(string name, params object[] args)
        {
            Type utility = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("PocketBump.Editor.DashGroundReadinessIntegration", false)).FirstOrDefault(t => t != null);
            Assert.That(utility, Is.Not.Null);
            MethodInfo method = utility.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, name);
            try { method.Invoke(null, args); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }

        [UnityTest]
        public IEnumerator AuthoredGroundRingStyleAndReferencesSurviveReloadPlayExitAndSecondInstall()
        {
            Invoke("PreparePersistenceFixture");
            yield return new EnterPlayMode();
            yield return null;
            Invoke("VerifyPersistenceFixture", true);
            yield return new ExitPlayMode();
            Invoke("FinishPersistenceFixture");
        }

        [Test]
        public void MismatchedFootprintBindingIsRejectedWithoutChangingReferencesOrRenderers()
        {
            Invoke("PreparePersistenceFixture");
            Scene scene = SceneManager.GetActiveScene();
            try
            {
                var binding = FindBinding(scene);
                var serialized = new SerializedObject(binding);
                var footprints = serialized.FindProperty("_identityFootprints");
                footprints.GetArrayElementAtIndex(0).objectReferenceValue = footprints.GetArrayElementAtIndex(1).objectReferenceValue;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                binding.GroundRingFor(0).RingRenderer.enabled = true;
                binding.GroundRingFor(1).RingRenderer.enabled = false;
                string references = EditorJsonUtility.ToJson(binding);
                var renderers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                    .ToDictionary(r => r, r => EditorJsonUtility.ToJson(r));

                var error = Assert.Throws<InvalidOperationException>(() => Invoke("UpgradeLoadedArena", scene));
                StringAssert.Contains("footprint binding differs", error.Message);
                Assert.That(EditorJsonUtility.ToJson(binding), Is.EqualTo(references), "Rejected integration must preserve even the mistaken author reference.");
                foreach (var renderer in renderers)
                    Assert.That(EditorJsonUtility.ToJson(renderer.Key), Is.EqualTo(renderer.Value), "Rejected integration changed renderer " + renderer.Key.name);
            }
            finally { ReloadAndFinishFixture(scene.path); }
        }

        [Test]
        public void EditModeHudDisablePreservesAuthoredGroundAndFootprintRendererStates()
        {
            Invoke("PreparePersistenceFixture");
            Scene scene = SceneManager.GetActiveScene();
            try
            {
                var binding = FindBinding(scene);
                var serialized = new SerializedObject(binding);
                var footprints = serialized.FindProperty("_identityFootprints");
                var renderers = new Renderer[]
                {
                    binding.GroundRingFor(0).RingRenderer, binding.GroundRingFor(1).RingRenderer,
                    (Renderer)footprints.GetArrayElementAtIndex(0).objectReferenceValue,
                    (Renderer)footprints.GetArrayElementAtIndex(1).objectReferenceValue
                };
                for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = i % 2 == 0;
                var preserved = renderers.ToDictionary(r => r, r => EditorJsonUtility.ToJson(r));
                Assert.That(binding.LocalGroundRingsActive, Is.False);
                binding.enabled = false;
                // Exercise the unload/domain-reload callback explicitly in Edit Mode;
                // regular MonoBehaviours need not get an editor toggle callback.
                typeof(DashReadinessHud).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(binding, null);
                foreach (var renderer in preserved)
                    Assert.That(EditorJsonUtility.ToJson(renderer.Key), Is.EqualTo(renderer.Value), "Editor disable changed authored renderer " + renderer.Key.name);
            }
            finally { ReloadAndFinishFixture(scene.path); }
        }

        static DashReadinessHud FindBinding(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<DashReadinessHud>(true)).Single();
        static void ReloadAndFinishFixture(string path)
        {
            // Discard only mutations in the disposable copy; the saved fixture and
            // all production files must still match their pre-test hashes.
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Invoke("FinishPersistenceFixture");
        }

        [UnityTearDown]
        public IEnumerator RestoreEditMode()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
