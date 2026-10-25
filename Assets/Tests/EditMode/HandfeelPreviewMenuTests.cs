using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace PocketBump.Tests
{
    public sealed class HandfeelPreviewMenuTests
    {
        [Test]
        public void SelectorDoesNotRewriteAssetsAndLeavingPlayDisarmsCandidate()
        {
            const string key = "PocketBump.HandfeelCandidate.NextPlay";
            bool selected = DashHandfeelPreview.Selected, stored = SessionState.GetBool(key, false);
            string[] files = { "Assets/Scenes/Arena.unity", "Assets/Scenes/MainMenu.unity", "ProjectSettings/EditorBuildSettings.asset" };
            byte[][] before = files.Select(File.ReadAllBytes).ToArray();
            Type menu = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("PocketBump.Editor.DashHandfeelPreviewMenu", false)).Single(t => t != null);
            try
            {
                menu.GetMethod("SelectCandidate").Invoke(null, null);
                Assert.That(DashHandfeelPreview.Selected, Is.True);
                menu.GetMethod("OnPlayState", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { PlayModeStateChange.EnteredEditMode });
                Assert.That(DashHandfeelPreview.Selected, Is.False);
                Assert.That(SessionState.GetBool(key, true), Is.False);
                for (int i = 0; i < files.Length; i++) CollectionAssert.AreEqual(before[i], File.ReadAllBytes(files[i]), files[i]);
                Assert.That(ArenaRulesProfile.CreateProductionV1().ContentHash,
                    Is.EqualTo("7012a8bc484f3d522ace17ccc550c9572659c5d0d2f96fa122a83fa27831abb8"));
            }
            finally { SessionState.SetBool(key, stored); DashHandfeelPreview.Selected = selected; }
        }
    }
}
