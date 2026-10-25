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
    public sealed class DashReadinessPersistenceTests
    {
        static void Invoke(string name, params object[] args)
        {
            Type utility = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("PocketBump.Editor.DashReadinessIntegration", false)).FirstOrDefault(t => t != null);
            Assert.That(utility, Is.Not.Null);
            try { utility.GetMethod(name, BindingFlags.Public | BindingFlags.Static).Invoke(null, args); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
        [UnityTest] public IEnumerator AuthoredMarkerReferencesAndStyleSurviveReloadPlayExitAndSecondInstall()
        {
            Invoke("PreparePersistenceFixture");
            yield return new EnterPlayMode(); yield return null;
            Invoke("VerifyPersistenceFixture", true);
            yield return new ExitPlayMode();
            Invoke("FinishPersistenceFixture");
        }
        [UnityTearDown] public IEnumerator RestoreEditMode()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
