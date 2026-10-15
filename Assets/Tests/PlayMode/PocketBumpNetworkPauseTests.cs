using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PocketBump.Tests
{
    public sealed class PocketBumpNetworkPauseTests
    {
        [UnityTearDown]
        public IEnumerator RestoreMenuAndClock()
        {
            Time.timeScale = 1f;
            GameSession.Instance.ErrorMessage = "";
            GameSession.Instance.ReturnToMainMenu();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TerminalNetworkReasonsFreezeInPlaceUntilManualMenuAction()
        {
            MethodInfo abort = typeof(DashLanSession).GetMethod("Fail", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(abort, Is.Not.Null);

            // Explicit v7 failure-presentation fixture, not a claim these faults arose
            // naturally. Classification and deadlines have separate real-socket tests.
            foreach (PlayKind kind in new[] { PlayKind.HostLan, PlayKind.JoinLan })
            foreach (string reason in new[]
            {
                "DESYNC", "INPUT HOLE", "INPUT PROGRESS TIMEOUT", "PEER TIMEOUT", "PEER LEFT",
                "LAN HANDSHAKE TIMEOUT", "SETTINGS MISMATCH", "COULD NOT BIND PORT",
                "COULD NOT OPEN SOCKET", "BAD ADDRESS", "RESULT CONFIRMATION TIMEOUT",
                "REMATCH TIMEOUT", "REMATCH EPOCH EXHAUSTED"
            })
            {
                if (kind == PlayKind.HostLan) GameSession.Instance.BeginHost(8, 1);
                else GameSession.Instance.BeginJoin("127.0.0.1", 8, 1);
                yield return null;
                DashMatchDirector director = Object.FindObjectOfType<DashMatchDirector>();
                Assert.That(director, Is.Not.Null, reason);
                Assert.That(director.Kind, Is.EqualTo(kind));
                Assert.That(director.LocalSeat, Is.EqualTo(kind == PlayKind.HostLan ? 0 : 1));
                int before = director.Sim.Checksum();
                Vector3 playerPosition = director.Presentation.Views.Players[0].Root.position;

                abort.Invoke(director.LanSession, new object[] { reason });
                director.PumpFrame();
                Assert.That(director.NetworkPaused, Is.True, reason);
                Assert.That(Time.timeScale, Is.EqualTo(1f), "Only the authoritative world freezes.");
                Assert.That(director.Hud.NetworkIssueVisible, Is.True, reason);
                Assert.That(director.Hud.NetworkIssueReason, Is.EqualTo(reason));
                Assert.That(director.Hud.NetworkIssueRect.anchorMin, Is.EqualTo(new Vector2(1f, 0f)));
                Assert.That(director.Hud.NetworkIssueRect.anchorMax, Is.EqualTo(new Vector2(1f, 0f)));
                Assert.That(GameSession.Instance.ErrorMessage, Is.EqualTo(reason));

                // Check the simulation guard as well as the paused clock.
                director.AdvanceOneTick();
                yield return new WaitForSecondsRealtime(0.075f);
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameSession.ArenaScene));
                Assert.That(director.Sim.Checksum(), Is.EqualTo(before), reason);
                Assert.That(director.Presentation.Views.Players[0].Root.position, Is.EqualTo(playerPosition), reason);

                abort.Invoke(director.LanSession, new object[] { "LATER FAILURE" });
                director.PumpFrame();
                Assert.That(director.Hud.NetworkIssueReason, Is.EqualTo(reason), "Keep the original failure evidence.");
                Button menu = director.Hud.transform.Find("NetworkIssuePanel/NetworkMainMenu").GetComponent<Button>();
                Assert.That(menu.gameObject.activeInHierarchy, Is.True);
                menu.onClick.Invoke();
                yield return null;
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameSession.MainMenuScene));
                Assert.That(Object.FindObjectOfType<DashMatchDirector>(), Is.Null);
            }
        }
    }
}
