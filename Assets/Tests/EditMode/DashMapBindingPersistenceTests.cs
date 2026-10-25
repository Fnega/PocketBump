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
    public sealed class DashMapBindingPersistenceTests
    {
        static void Invoke(string method, params object[] args)
        {
            Type tool = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("PocketBump.Editor.DashMapBindingIntegration", false)).FirstOrDefault(t => t != null);
            Assert.That(tool, Is.Not.Null);
            try { tool.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, args); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
        [UnityTest] public IEnumerator AuthoredMapSettingsSurviveSaveReloadPlayExitAndSecondInstall()
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
