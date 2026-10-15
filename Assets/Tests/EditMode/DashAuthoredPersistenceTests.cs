using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.TestTools;

namespace PocketBump.Tests
{
    public sealed class DashAuthoredPersistenceTests
    {
        // Editor authoring utilities live in the editor assembly; invoke narrowly so
        // runtime test assemblies don't gain a dependency on editor implementation.
        static void Invoke(string method, params object[] args)
        {
            Type utility = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("PocketBump.Editor.DashAuthoredPersistenceVerification", false)).FirstOrDefault(t => t != null);
            Assert.That(utility, Is.Not.Null, "Authored persistence verification tool must compile.");
            try { utility.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, args); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }

        [UnityTest]
        public IEnumerator SavedArtistEditsSurviveReloadIncrementalRepairAndActualPlayMode()
        {
            Invoke("PrepareMenuFixture");
            yield return new EnterPlayMode();
            yield return null;
            Invoke("VerifyMenuFixture", true);
            yield return new ExitPlayMode();
            Invoke("VerifyMenuFixture", false);
            Invoke("PrepareArenaFixture");
            yield return new EnterPlayMode();
            yield return null;
            Invoke("VerifyArenaFixture", true);
            yield return new ExitPlayMode();
            Invoke("FinishVerification");
        }

        [UnityTearDown]
        public IEnumerator RestoreEditModeAfterPersistenceTest()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            Invoke("ReportIncompleteVerification");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
