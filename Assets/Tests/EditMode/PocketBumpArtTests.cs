using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class PocketBumpArtTests
    {
        const string Root = "PocketBump/Art/Kenney/";

        [TestCase("Models/Space/template-wall")]
        [TestCase("Models/Space/gate-lasers")]
        [TestCase("Models/Marble/marble-low")]
        public void CuratedModelsAreAvailableAtRuntime(string relativePath)
        {
            Assert.That(Resources.Load<GameObject>(Root + relativePath), Is.Not.Null);
        }

        [TestCase("UI/button_rectangle")]
        [TestCase("VFX/star_05")]
        [TestCase("Patterns/arena_octagons")]
        [TestCase("LightMasks/circle_rings")]
        [TestCase("Input/keyboard_w")]
        public void CuratedTexturesAreAvailableAtRuntime(string relativePath)
        {
            Assert.That(Resources.Load<Texture2D>(Root + relativePath), Is.Not.Null);
        }

        [Test]
        public void KeyPromptsUseIconsOnlyForCuratedBindings()
        {
            Assert.That(ArtCatalog.KeySprite(KeyCode.W), Is.Not.Null);
            Assert.That(ArtCatalog.KeySprite(KeyCode.RightArrow), Is.Not.Null);
            Assert.That(ArtCatalog.KeySprite(KeyCode.J), Is.Not.Null);
            Assert.That(ArtCatalog.KeySprite(KeyCode.Keypad2), Is.Not.Null);
            Assert.That(ArtCatalog.KeySprite(KeyCode.Space), Is.Null);
        }

        [Test]
        public void AttachedArtDoesNotReplaceGameplayColliderOrDuplicateVisuals()
        {
            var host = new GameObject("ArtHost");
            try
            {
                BoxCollider2D gameplayCollider = host.AddComponent<BoxCollider2D>();
                Assert.That(gameplayCollider, Is.Not.Null, "test setup must create the gameplay collider");
                GameObject first = ArtCatalog.AttachModel(
                    host,
                    "Models/Space/template-wall",
                    Color.cyan,
                    Quaternion.identity);
                GameObject second = ArtCatalog.AttachModel(
                    host,
                    "Models/Space/template-wall",
                    Color.cyan,
                    Quaternion.identity);

                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.Not.Null);
                Assert.That(second.GetInstanceID(), Is.EqualTo(first.GetInstanceID()));
                Assert.That(host.GetComponent<BoxCollider2D>(), Is.Not.Null);
                Assert.That(host.GetComponent<BoxCollider2D>().GetInstanceID(), Is.EqualTo(gameplayCollider.GetInstanceID()));
                Assert.That(first.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(first.GetComponentsInChildren<Collider2D>(true), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
