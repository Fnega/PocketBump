using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PocketBump.Tests
{
    public sealed class PocketBumpLanResultTests
    {
        [UnityTearDown]
        public IEnumerator RestoreMenu()
        {
            Time.timeScale = 1f;
            GameSession.Instance.ReturnToMainMenu();
            GameSession.Instance.ErrorMessage = "";
            yield return null;
        }

        [UnityTest]
        public IEnumerator LanRematchCannotReloadSceneBeforeBilateralConfirmation()
        {
            GameSession.Instance.BeginHost(1, 1);
            yield return null;
            var first = Object.FindObjectOfType<DashMatchDirector>();
            Assert.That(first, Is.Not.Null);
            // Public v7 navigation guard, not a completed network match.
            GameSession.Instance.Rematch();
            yield return null;
            Assert.That(Object.FindObjectOfType<DashMatchDirector>(), Is.SameAs(first));
            Assert.That(first.MatchEpoch, Is.Zero);
            Assert.That(first.LanSession.SocketBound, Is.True);
        }

        [UnityTest]
        public IEnumerator AuthoredAndFallbackHudShowProvisionalConfirmedAndWithdrawnResultsForBothSeats()
        {
            // Real v7 handshake followed by a deliberately injected lifecycle/UI terminal
            // fixture and clock. This verifies presentation/navigation only, not a natural
            // winning game; socket Finish/rematch and natural-driver evidence are separate.
            foreach (bool isHost in new[] { true, false })
            {
                if (isHost) GameSession.Instance.BeginHost(8, 1);
                else GameSession.Instance.BeginJoin("127.0.0.1", 8, 1);
                yield return null;
                var director = Object.FindObjectOfType<DashMatchDirector>();
                using (var pair = new DashSceneSocketPair(director))
                {
                    pair.DriveTo(20);
                    float now = 0f;
                    var lifecycle = new DashLanLifecycle(isHost, director.MatchId, director.Rules,
                        director.LanSession.Seed, () => now);
                    typeof(DashLanSession).GetProperty("Lifecycle").SetValue(director.LanSession, lifecycle);
                    director.Sim.Scores[0] = 8; director.Sim.Scores[1] = 0;
                    director.Sim.Phase = MatchState.MatchOver;
                    var finish = new NetFinishTuple
                    {
                        FinishTick = director.Sim.Tick - 1, Checksum = director.Sim.Checksum(),
                        Score0 = 8, Score1 = 0, Winner = 0,
                        SettingsHash = DashNetcode.ContentTag(director.Rules.ContentHash)
                    };
                    var fallbackRoot = new GameObject("DashLifecycleFallbackHud");
                    var fallback = DashMatchHud.Build(fallbackRoot.transform,
                        director.Hud.GetComponentInChildren<TMP_Text>(true).font);
                    fallback.Bind(director);
                    try
                    {
                        foreach (var hud in new[] { director.Hud, fallback })
                        {
                            director.Sim.Phase = MatchState.MatchOver;
                            lifecycle.UpdateTerminal(true, false, finish);
                            hud.RefreshFromDirector();
                            Assert.That(ResultText(hud, "Winner"), Is.EqualTo("CONFIRMING RESULT"));
                            Assert.That(hud.ResultVisible, Is.True);
                            Assert.That(ResultButton(hud, "Rematch").interactable, Is.False);
                            Assert.That(ResultButton(hud, "ModifyRules").gameObject.activeSelf, Is.False);
                            Assert.That(hud.transform.Find("ResultPanel/LanRulesNote").gameObject.activeSelf, Is.True);
                            lifecycle.UpdateTerminal(false, false, default);
                            director.Sim.Phase = MatchState.Live;
                            hud.RefreshFromDirector();
                            Assert.That(hud.ResultVisible, Is.False, "A withdrawn prediction removes its result panel.");
                        }
                        director.Sim.Phase = MatchState.MatchOver;
                        lifecycle.UpdateTerminal(true, true, finish);
                        var message = new DashLifecycleMessage
                        {
                            MatchId = director.MatchId, Epoch = 0, NextEpoch = 0,
                            Seat = (byte)(isHost ? 1 : 0), Finish = finish, Kind = NetLifecycleKind.Finish,
                            ContentHash = director.Rules.ContentHash, Seed = director.LanSession.Seed
                        };
                        Assert.That(lifecycle.Receive(message), Is.True);
                        message.Kind = NetLifecycleKind.FinishAck;
                        Assert.That(lifecycle.Receive(message), Is.True);
                        for (int second = 0; second <= 9; second++)
                        {
                            now = second;
                            message.Kind = NetLifecycleKind.Heartbeat;
                            Assert.That(lifecycle.Receive(message), Is.True);
                            lifecycle.Tick();
                            foreach (var hud in new[] { director.Hud, fallback })
                            {
                                hud.RefreshFromDirector();
                                Assert.That(ResultText(hud, "Winner"), Does.StartWith("P1 WINS"));
                                Assert.That(ResultButton(hud, "Rematch").interactable, Is.True);
                                Assert.That(hud.NetworkIssueVisible, Is.False);
                            }
                            Assert.That(lifecycle.FailureReason, Is.Empty);
                        }
                        GameSession.Instance.ModifyRules();
                        Assert.That(Object.FindObjectOfType<DashMatchDirector>(), Is.SameAs(director));
                        Assert.That(lifecycle.RequestRematch(), Is.True);
                        foreach (var hud in new[] { director.Hud, fallback })
                        {
                            hud.RefreshFromDirector();
                            Assert.That(ResultButton(hud, "Rematch").interactable, Is.False);
                            Assert.That(ResultText(hud, "Rematch"), Is.EqualTo("WAITING FOR PEER"));
                        }
                        message.Kind = NetLifecycleKind.RematchIntent;
                        Assert.That(lifecycle.Receive(message), Is.True);
                        if (!isHost)
                        {
                            message.Kind = NetLifecycleKind.Offer; message.NextEpoch = 1; message.NextSeed = 9u;
                            Assert.That(lifecycle.Receive(message), Is.True);
                        }
                        foreach (var hud in new[] { director.Hud, fallback })
                        {
                            hud.RefreshFromDirector();
                            Assert.That(hud.ResultVisible, Is.True);
                            Assert.That(ResultText(hud, "Winner"), Is.EqualTo("STARTING NEXT MATCH"));
                            Assert.That(ResultButton(hud, "Rematch").interactable, Is.False);
                        }
                    }
                    finally { Object.DestroyImmediate(fallbackRoot); }
                }
                GameSession.Instance.ReturnToMainMenu();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator RebindingAndRepeatedLanButtonsDoNotReloadOrChangeRules()
        {
            GameSession.Instance.BeginHost(8, 2);
            yield return null;
            var director = Object.FindObjectOfType<DashMatchDirector>();
            var hud = director.Hud;
            hud.Bind(director); hud.Bind(director);
            ResultButton(hud, "Rematch").onClick.Invoke();
            ResultButton(hud, "Rematch").onClick.Invoke();
            ResultButton(hud, "ModifyRules").onClick.Invoke();
            yield return null;
            Assert.That(Object.FindObjectOfType<DashMatchDirector>(), Is.SameAs(director));
            Assert.That(GameSession.Instance.Kind, Is.EqualTo(PlayKind.HostLan));
            Assert.That(director.Settings.marbleCount, Is.EqualTo(2));
            Assert.That(director.MatchEpoch, Is.Zero);
        }

        static Button ResultButton(DashMatchHud hud, string name) =>
            hud.transform.Find("ResultPanel/" + name).GetComponent<Button>();
        static string ResultText(DashMatchHud hud, string name) =>
            hud.transform.Find("ResultPanel/" + name).GetComponentInChildren<TMP_Text>(true).text;
    }
}
