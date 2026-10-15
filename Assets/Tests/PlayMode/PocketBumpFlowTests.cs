using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Formal-scene integration for the approved Dash rules. Explicit position/velocity
// and terminal fixtures below test mechanisms/navigation, not natural full matches.
// Retired basket/charge/held-Boost algorithms retain separate legacy unit coverage.
namespace PocketBump.Tests
{
    public sealed class PocketBumpFlowTests
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
        public IEnumerator ThreeRegionHomePreservesNavigationAndRuntimeParity()
        {
            yield return LoadScene(GameSession.MainMenuScene);
            MainMenuController authored = Object.FindObjectOfType<MainMenuController>();
            AssertHomeRegions(authored);
            var runtimeRoot = new GameObject("RuntimeMenuParity");
            try
            {
                MainMenuController runtime = runtimeRoot.AddComponent<MainMenuController>();
                AssertHomeRegions(runtime);
                Transform authoredHome = authored.transform.Find("MainMenuCanvas/MainPanel");
                Transform runtimeHome = runtime.transform.Find("MainMenuCanvas/MainPanel");
                foreach (string name in new[] { "StartRegion", "ServerRegion", "ExitRegion", "Error" })
                {
                    var a = authoredHome.Find(name).GetComponent<RectTransform>();
                    var b = runtimeHome.Find(name).GetComponent<RectTransform>();
                    Assert.That(b.sizeDelta, Is.EqualTo(a.sizeDelta));
                    Assert.That(b.anchoredPosition, Is.EqualTo(a.anchoredPosition));
                }
            }
            finally { Object.DestroyImmediate(runtimeRoot); }

            foreach (string action in new[] { "Start", "HostLan", "JoinLan" })
            {
                Click(action);
                Assert.That(authored.transform.Find("MainMenuCanvas/MainPanel").gameObject.activeSelf, Is.False);
                string panel = action == "JoinLan" ? "JoinPanel" : "RuleSetupPanel";
                Assert.That(authored.transform.Find("MainMenuCanvas/" + panel).gameObject.activeSelf, Is.True);
                Click(action == "JoinLan" ? "JoinBack" : "Back");
                AssertHomeRegions(authored);
                Assert.That(Object.FindObjectOfType<DashMatchDirector>(), Is.Null);
            }
            Click("Settings");
            Assert.That(authored.transform.Find("MainMenuCanvas/SettingsPanel").gameObject.activeSelf, Is.True);
            Click("SettingsBack");
            AssertHomeRegions(authored);
            LogAssert.Expect(LogType.Log, "Exit requested (ignored while running in the Unity Editor).");
            Click("Exit");
            AssertHomeRegions(authored);
        }

        static void AssertHomeRegions(MainMenuController menu)
        {
            Transform home = menu.transform.Find("MainMenuCanvas/MainPanel");
            Assert.That(home.gameObject.activeInHierarchy, Is.True);
            string[] names = { "StartRegion/Start", "ServerRegion/HostLan", "ServerRegion/JoinLan", "ExitRegion/Settings", "ExitRegion/Exit" };
            foreach (string path in names)
            {
                Transform button = home.Find(path);
                Assert.That(button, Is.Not.Null, path);
                Assert.That(button.gameObject.activeInHierarchy, Is.True);
                Assert.That(button.parent.Find("Heading"), Is.Not.Null);
            }
            Assert.That(home.GetComponentsInChildren<Button>(true).Length, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator MenuRulesArenaAndModifyRulesFlowTransfersSettings()
        {
            GameSession.Instance.SelectedSettings.pointsToWin = 8;
            GameSession.Instance.SelectedSettings.marbleCount = 1;
            yield return LoadScene(GameSession.MainMenuScene);
            Assert.That(Object.FindObjectOfType<DashMatchDirector>(), Is.Null);
            Click("Start");
            Assert.That(GameObject.Find("RuleSetupPanel").activeSelf, Is.True);
            Click("Back");
            Assert.That(GameObject.Find("MainPanel").activeSelf, Is.True);
            Click("Start");
            for (int i = 0; i < 3; i++) Click("POINTS TO WINMinus");
            for (int i = 0; i < 2; i++) Click("MARBLESPlus");
            Click("Play");
            yield return null;

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameSession.ArenaScene));
            DashMatchDirector director = ProductionDirector();
            Assert.That(director.Settings.pointsToWin, Is.EqualTo(5));
            Assert.That(director.Settings.marbleCount, Is.EqualTo(3));
            Assert.That(director.Rules.IsProvisional, Is.False);
            Assert.That(director.Rules.ContentHash, Is.EqualTo(ArenaRulesProfile.CreateProductionV1(5, 3).ContentHash));
            AdvanceToLive(director);
            Assert.That(director.Sim.Balls.Count(ball => ball.Active), Is.EqualTo(3));
            Assert.That(director.LanSession, Is.Null, "Local two-player play needs no socket.");
            Assert.That(director.Presentation, Is.Not.Null, "The formal scene must contain authored Dash presentation.");

            CompleteNavigationFixture(director, 0);
            Click("ModifyRules");
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameSession.MainMenuScene));
            Assert.That(GameObject.Find("RuleSetupPanel").activeSelf, Is.True);
            Assert.That(GameSession.Instance.SelectedSettings.pointsToWin, Is.EqualTo(5));
            Assert.That(GameSession.Instance.SelectedSettings.marbleCount, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator ResultRematchAndMainMenuActionsPreserveAndLeaveMatch()
        {
            GameSession.Instance.BeginMatch(4, 2);
            yield return null;
            DashMatchDirector director = ProductionDirector();
            Assert.That(director.Settings.pointsToWin, Is.EqualTo(4));
            Assert.That(director.Settings.marbleCount, Is.EqualTo(2));
            CompleteNavigationFixture(director, 0);
            DashArenaState previous = director.Sim;
            ArenaRulesProfile rules = director.Rules;
            Click("Rematch");
            yield return null;
            Assert.That(Object.FindObjectOfType<DashMatchDirector>(), Is.SameAs(director));
            Assert.That(director.Sim, Is.Not.SameAs(previous), "Local rematch creates a fresh epoch without duplicating the director.");
            Assert.That(director.MatchEpoch, Is.EqualTo(previous.Epoch + 1));
            Assert.That(director.Rules, Is.SameAs(rules));
            Assert.That(director.Sim.Scores, Is.EqualTo(new[] { 0, 0 }));
            Assert.That(director.State, Is.EqualTo(MatchState.Kickoff));
            Assert.That(director.Settings.marbleCount, Is.EqualTo(2));

            CompleteNavigationFixture(director, 1);
            Click("MainMenu");
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameSession.MainMenuScene));
            Assert.That(Object.FindObjectOfType<DashMatchDirector>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator DangerousBallImpactsCanCausePersonalFallWithoutAwardingPoints()
        {
            GameSession.Instance.BeginMatch(8, 1);
            yield return null;
            DashMatchDirector director = ProductionDirector();
            AdvanceToLive(director);
            var observed = new List<DashArenaEvent>();
            director.ConfirmedEvents += batch => observed.AddRange(batch);
            DashArenaState state = director.Sim;

            // Explicit low/high-speed contact fixtures replace legacy dunk-for-points.
            PlacePlayerFixture(state, 0, Vector2.zero, Vector2.right);
            SetBallFixture(state, new Vector2(-1.2f, 0f), Vector2.right * 5f);
            Advance(director, 8);
            Assert.That(state.Balls[0].DangerousImpactCount, Is.Zero);

            PlacePlayerFixture(state, 0, Vector2.zero, Vector2.right);
            SetBallFixture(state, new Vector2(-1.2f, 0f), Vector2.right * 12f);
            Advance(director, 4);
            Assert.That(state.Balls[0].DangerousImpactCount, Is.EqualTo(1));
            Assert.That(state.Balls[0].Velocity.magnitude, Is.LessThan(12f));

            PlacePlayerFixture(state, 0, new Vector2(director.Rules.HalfWidth - .25f, 0f), Vector2.right);
            SetBallFixture(state, new Vector2(director.Rules.HalfWidth - 1.85f, 0f), Vector2.right * director.Rules.BallMaxSpeed);
            for (int i = 0; i < 25 && state.Players[0].Alive; i++) director.AdvanceOneTick();
            Assert.That(state.Players[0].Alive, Is.False);
            Assert.That(state.Scores, Is.EqualTo(new[] { 0, 0 }), "A fall cannot score a goal.");
            Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
            Assert.That(state.Players[0].RespawnTicks, Is.EqualTo(director.Rules.RespawnTicks));
            Assert.That(observed.Count(e => e.Kind == DashArenaEventKind.Fell && e.Seat == 0), Is.EqualTo(1));
            int fellAt = state.Tick;
            Vector2 otherBefore = state.Players[1].Position;
            director.InputOverride = (seat, tick, sim) => new DashArenaInput { Move = seat == 1 ? Vector2.right : Vector2.zero };
            Advance(director, director.Rules.RespawnTicks - 1);
            Assert.That(state.Players[0].Alive, Is.False, "No early respawn.");
            Assert.That(state.Players[1].Position, Is.Not.EqualTo(otherBefore), "Opponent keeps playing.");
            director.AdvanceOneTick();
            Assert.That(state.Players[0].Alive, Is.True);
            Assert.That(state.Tick - fellAt, Is.EqualTo(director.Rules.RespawnTicks));
            Assert.That(state.Scores, Is.EqualTo(new[] { 0, 0 }));
            Assert.That(observed.Count(e => e.Kind == DashArenaEventKind.Respawned && e.Seat == 0), Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator IsolatedFreeBallCoastsToRestWithoutSpeedingUp()
        {
            GameSession.Instance.BeginMatch(8, 1);
            yield return null;
            DashMatchDirector director = ProductionDirector();
            AdvanceToLive(director);
            SetBallFixture(director.Sim, Vector2.zero, Vector2.right * 6f);
            float previous = director.Sim.Balls[0].Velocity.magnitude;
            for (int i = 0; i < 190; i++)
            {
                director.AdvanceOneTick();
                float speed = director.Sim.Balls[0].Velocity.magnitude;
                Assert.That(speed, Is.LessThanOrEqualTo(previous + .0001f));
                previous = speed;
            }
            Assert.That(previous, Is.LessThan(.001f));
            Assert.That(director.Sim.Scores, Is.EqualTo(new[] { 0, 0 }));
            yield return null;
        }

        [UnityTest]
        public IEnumerator EitherPlayerBodyStrikesAFreeBallWithoutCollectingOrBasketGeometry()
        {
            GameSession.Instance.BeginMatch(8, 1);
            yield return null;
            DashMatchDirector director = ProductionDirector();
            AdvanceToLive(director);
            Assert.That(Object.FindObjectsOfType<PlayerBasket>(), Is.Empty, "Retired basket components are absent from the formal playable world.");
            Assert.That(Object.FindObjectsOfType<PlayerChargePresentation>(), Is.Empty);
            var impacts = new List<DashArenaEvent>();
            director.ConfirmedEvents += batch => impacts.AddRange(batch);
            for (int seat = 0; seat < 2; seat++)
            {
                // Explicit contact fixture; the ensuing movement/collision is the real stepper.
                PlacePlayerFixture(director.Sim, 1 - seat, director.Rules.SpawnForSeat(1 - seat), Vector2.up);
                PlacePlayerFixture(director.Sim, seat, new Vector2(-1.4f, 0f), Vector2.right);
                SetBallFixture(director.Sim, Vector2.zero, Vector2.zero);
                int impactBefore = impacts.Count(e => e.Kind == DashArenaEventKind.Impact && e.Seat == seat);
                director.InputOverride = (which, tick, state) => new DashArenaInput { Move = which == seat ? Vector2.right : Vector2.zero };
                Advance(director, 24);
                Assert.That(impacts.Count(e => e.Kind == DashArenaEventKind.Impact && e.Seat == seat), Is.GreaterThan(impactBefore));
                Assert.That(director.Sim.Balls[0].Active, Is.True);
                Assert.That(director.Sim.Balls[0].Velocity.x, Is.GreaterThan(0f));
                Assert.That(Vector2.Distance(director.Sim.Balls[0].Position, director.Sim.Players[seat].Position),
                    Is.GreaterThan(director.Rules.PlayerRadius + director.Rules.BallRadius), "The scoring ball stays free, never welded to a carrier.");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SingleDashCollisionScoresWithoutChargeOrIndependentShootAction()
        {
            GameSession.Instance.BeginMatch(8, 1);
            yield return null;
            DashMatchDirector director = ProductionDirector();
            AdvanceToLive(director);
            // Near-goal collision fixture, not a natural zero-setup match.
            PlacePlayerFixture(director.Sim, 0, new Vector2(0f, -director.Rules.HalfHeight + 3f), Vector2.down);
            SetBallFixture(director.Sim, new Vector2(0f, -director.Rules.HalfHeight + 1.7f), Vector2.zero);
            var events = new List<DashArenaEvent>();
            director.ConfirmedEvents += batch => events.AddRange(batch);
            int firstTick = director.Sim.Tick;
            director.InputOverride = (seat, tick, state) => seat == 0
                ? new DashArenaInput { Move = Vector2.down, DashPressed = tick == firstTick } : default;
            for (int i = 0; i < 30 && director.State == MatchState.Live; i++) director.AdvanceOneTick();
            Assert.That(events.Count(e => e.Kind == DashArenaEventKind.DashStarted && e.Seat == 0), Is.EqualTo(1));
            Assert.That(events.Any(e => e.Kind == DashArenaEventKind.Impact && e.Seat == 0), Is.True);
            Assert.That(events.Count(e => e.Kind == DashArenaEventKind.Goal && e.Seat == 0), Is.EqualTo(1));
            Assert.That(director.Sim.Scores, Is.EqualTo(new[] { 1, 0 }));
            Assert.That(director.State, Is.EqualTo(MatchState.Scored));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FreeBallCrossingNorthGoalScoresForPlayerTwo()
        {
            yield return VerifyGoalFixture(1, 1);
        }

        [UnityTest]
        public IEnumerator FreeBallCrossingSouthGoalScoresForPlayerOne()
        {
            yield return VerifyGoalFixture(-1, 0);
        }

        static IEnumerator VerifyGoalFixture(int direction, int scorer)
        {
            GameSession.Instance.BeginMatch(8, 1);
            yield return null;
            DashMatchDirector director = ProductionDirector();
            AdvanceToLive(director);
            // Start inside the arena and actually sweep across the goal plane. Merely
            // placing an already-outside, stationary ball cannot prove swept scoring.
            SetBallFixture(director.Sim,
                new Vector2(0f, direction * (director.Rules.HalfHeight - .5f)), Vector2.up * (direction * 12f));
            for (int i = 0; i < 20 && director.State == MatchState.Live; i++) director.AdvanceOneTick();
            Assert.That(director.Sim.Scores[scorer], Is.EqualTo(1));
            Assert.That(director.Sim.Scores[1 - scorer], Is.Zero);
            Assert.That(director.State, Is.EqualTo(MatchState.Scored));
            Assert.That(director.Sim.LastScorer, Is.EqualTo(scorer));
            yield return null;
        }

        static DashMatchDirector ProductionDirector()
        {
            var director = Object.FindObjectOfType<DashMatchDirector>();
            Assert.That(director, Is.Not.Null, "Formal Arena must use the Dash director.");
            Assert.That(Object.FindObjectOfType<MatchDirector>(), Is.Null, "Do not run both old and new clocks.");
            Assert.That(director.Initialized, Is.True);
            director.AutomaticTicking = false;
            director.InputOverride = (seat, tick, state) => default;
            return director;
        }

        static void AdvanceToLive(DashMatchDirector director)
        {
            for (int i = 0; i <= director.Rules.KickoffTicks && director.State == MatchState.Kickoff; i++)
                director.AdvanceOneTick();
            Assert.That(director.State, Is.EqualTo(MatchState.Live));
        }

        static void Advance(DashMatchDirector director, int ticks)
        {
            for (int i = 0; i < ticks; i++) director.AdvanceOneTick();
        }

        // Deliberate local navigation fixture: not proof of natural match completion.
        // One actual scored-phase step publishes the final state the HUD is allowed to show.
        static void CompleteNavigationFixture(DashMatchDirector director, int winner)
        {
            director.Sim.Scores[winner] = director.Rules.PointsToWin;
            director.Sim.Scores[1 - winner] = 0;
            director.Sim.Phase = MatchState.Scored;
            director.Sim.PhaseTicks = 1;
            director.AdvanceOneTick();
            Assert.That(director.ResultsReady, Is.True);
            Assert.That(director.Hud, Is.Not.Null);
            director.Hud.RefreshFromDirector();
        }

        static void PlacePlayerFixture(DashArenaState state, int seat, Vector2 position, Vector2 facing)
        {
            state.Players[seat] = new DashPlayerState { Alive = true, Position = position, Facing = facing };
            Array.Clear(state.PlayerContacts, 0, state.PlayerContacts.Length);
            Array.Clear(state.PlayerBallContacts, 0, state.PlayerBallContacts.Length);
            Array.Clear(state.CylinderContacts, 0, state.CylinderContacts.Length);
            Array.Clear(state.WallContacts, 0, state.WallContacts.Length);
        }

        static void SetBallFixture(DashArenaState state, Vector2 position, Vector2 velocity)
        {
            state.Balls[0] = new DashBallState { Active = true, Position = position, Velocity = velocity };
            Array.Clear(state.PlayerBallContacts, 0, state.PlayerBallContacts.Length);
            Array.Clear(state.BallWallContacts, 0, state.BallWallContacts.Length);
        }

        static IEnumerator LoadScene(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
            yield return null;
        }

        static void Click(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            Assert.That(go, Is.Not.Null, objectName + " was not found.");
            Button button = go.GetComponent<Button>();
            Assert.That(button, Is.Not.Null, objectName + " has no Button component.");
            button.onClick.Invoke();
        }
    }
}
