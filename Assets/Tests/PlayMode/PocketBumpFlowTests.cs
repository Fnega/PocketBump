using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PocketBump.Tests
{
    public sealed class PocketBumpFlowTests
    {
        [UnityTest]
        public IEnumerator MenuRulesArenaAndModifyRulesFlowTransfersSettings()
        {
            yield return LoadScene(GameSession.MainMenuScene);
            Assert.That(Object.FindObjectOfType<MatchDirector>(), Is.Null);

            Click("Start");
            Assert.That(GameObject.Find("RuleSetupPanel").activeSelf, Is.True);
            Click("Back");
            Assert.That(GameObject.Find("MainPanel").activeSelf, Is.True);

            Click("Start");
            Click("POINTS TO WINMinus");
            Click("POINTS TO WINMinus");
            Click("POINTS TO WINMinus");
            Click("MARBLESPlus");
            Click("MARBLESPlus");
            Click("Play");
            yield return null;

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameSession.ArenaScene));
            MatchDirector director = Object.FindObjectOfType<MatchDirector>();
            Assert.That(director, Is.Not.Null);
            Assert.That(director.Settings.pointsToWin, Is.EqualTo(5));
            Assert.That(director.Settings.marbleCount, Is.EqualTo(3));

            yield return new WaitForSeconds(ArenaLayout.KickoffSeconds + 0.2f);
            Assert.That(director.State, Is.EqualTo(MatchState.Live));
            Assert.That(ActiveMarbleCount(director), Is.EqualTo(3));

            director.Hud.ShowResult(0);
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
            MatchDirector first = Object.FindObjectOfType<MatchDirector>();
            Assert.That(first.Settings.pointsToWin, Is.EqualTo(4));
            Assert.That(first.Settings.marbleCount, Is.EqualTo(2));

            first.Hud.ShowResult(0);
            Click("Rematch");
            yield return null;
            MatchDirector rematch = Object.FindObjectOfType<MatchDirector>();
            Assert.That(rematch, Is.Not.SameAs(first));
            Assert.That(rematch.Settings.pointsToWin, Is.EqualTo(4));
            Assert.That(rematch.Settings.marbleCount, Is.EqualTo(2));

            rematch.Hud.ShowResult(1);
            Click("MainMenu");
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameSession.MainMenuScene));
            Assert.That(Object.FindObjectOfType<MatchDirector>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator MarbleImpactBandsTransferEnergyRespectCooldownAndCanDunk()
        {
            GameSession.Instance.BeginMatch(8, 1);
            yield return null;
            MatchDirector director = Object.FindObjectOfType<MatchDirector>();
            yield return new WaitForSeconds(ArenaLayout.KickoffSeconds + 0.2f);

            director.DebugPlacePlayer(1, new Vector2(0f, -ArenaLayout.HalfHeight * 0.67f), Vector2.up);
            director.DebugPlacePlayer(0, Vector2.zero, Vector2.right);
            director.DebugSetMarble(0, new Vector2(-1f, 0f), Vector2.right * 5f);
            for (int i = 0; i < 8; i++)
                yield return new WaitForFixedUpdate();
            Assert.That(director.Sim.Marbles[0].DangerousImpactCount, Is.Zero);

            director.DebugPlacePlayer(0, Vector2.zero, Vector2.right);
            director.DebugSetMarble(0, new Vector2(-1.2f, 0f), Vector2.right * 12f);
            for (int i = 0; i < 4; i++)
                yield return new WaitForFixedUpdate();
            Assert.That(director.Sim.Marbles[0].DangerousImpactCount, Is.EqualTo(1));
            Assert.That(director.Sim.Marbles[0].Velocity.magnitude, Is.LessThan(12f));

            yield return new WaitForSeconds(ArenaLayout.MarbleImpactCooldown + 0.05f);
            director.DebugPlacePlayer(0, new Vector2(ArenaLayout.HalfWidth - 0.25f, 0f), Vector2.right);
            director.DebugSetMarble(0, new Vector2(ArenaLayout.HalfWidth - 1.85f, 0f), Vector2.right * ArenaLayout.MarbleMaxSpeed);
            for (int i = 0; i < 25 && director.State == MatchState.Live; i++)
                yield return new WaitForFixedUpdate();
            Assert.That(director.ScoreP2, Is.EqualTo(1), "A dangerous marble should be able to push P1 into a dunk pocket.");
        }

        [UnityTest]
        public IEnumerator IsolatedMarbleCoastsToRestWithoutSpeedingUp()
        {
            GameSession.Instance.BeginMatch(8, 1);
            yield return null;
            MatchDirector director = Object.FindObjectOfType<MatchDirector>();
            yield return new WaitForSeconds(ArenaLayout.KickoffSeconds + 0.2f);

            director.DebugPlacePlayer(0, ArenaLayout.PlayerOneSpawn, Vector2.down);
            director.DebugPlacePlayer(1, ArenaLayout.PlayerTwoSpawn, Vector2.up);
            director.DebugSetMarble(0, Vector2.zero, Vector2.right * 6f);

            float previous = director.Sim.Marbles[0].Velocity.magnitude;
            for (int i = 0; i < 8; i++)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(director.Sim.Marbles[0].Velocity.magnitude, Is.LessThanOrEqualTo(previous + 0.02f));
                previous = director.Sim.Marbles[0].Velocity.magnitude;
            }

            for (int i = 0; i < 180; i++)
                yield return new WaitForFixedUpdate();
            Assert.That(director.Sim.Marbles[0].Velocity.magnitude, Is.LessThan(ArenaLayout.MarbleMinRunSpeed));
        }

        [UnityTest]
        public IEnumerator BasketCollectsOnWellHitBodyBouncesAndStealTransfers()
        {
            GameSession.Instance.BeginMatch(8, 1);
            yield return null;
            MatchDirector director = Object.FindObjectOfType<MatchDirector>();
            yield return new WaitForSeconds(ArenaLayout.KickoffSeconds + 0.2f);

            director.DebugPlacePlayer(0, Vector2.zero, Vector2.right);
            director.DebugSetMarble(0, new Vector2(-1.2f, 0f), Vector2.right * 5f);
            for (int i = 0; i < 12; i++)
                yield return new WaitForFixedUpdate();
            Assert.That(director.Sim.Players[0].Carried, Is.EqualTo(-1), "Body contact must not collect.");

            director.DebugPlacePlayer(0, Vector2.zero, Vector2.right);
            director.DebugSetMarble(0, Vector2.right * ArenaLayout.BasketOffset, Vector2.zero);
            director.DebugCollect(0, 0);
            Assert.That(director.Sim.Players[0].Carried, Is.EqualTo(0));

            director.DebugPlacePlayer(1, new Vector2(ArenaLayout.BasketOffset * 2f, 0f), Vector2.left);
            director.DebugCollect(1, 0);
            Assert.That(director.Sim.Players[1].Carried, Is.EqualTo(0));
            Assert.That(director.Sim.Players[0].Carried, Is.EqualTo(-1));
        }

        [UnityTest]
        public IEnumerator CarriedMarbleDoesNotScoreAndFreeMarbleDoes()
        {
            GameSession.Instance.BeginMatch(8, 1);
            yield return null;
            MatchDirector director = Object.FindObjectOfType<MatchDirector>();
            yield return new WaitForSeconds(ArenaLayout.KickoffSeconds + 0.2f);

            director.DebugPlacePlayer(0, Vector2.zero, Vector2.right);
            director.DebugSetMarble(0, Vector2.right * ArenaLayout.BasketOffset, Vector2.zero);
            director.DebugCollect(0, 0);
            Assert.That(director.Sim.Players[0].Carried, Is.EqualTo(0));

            director.DebugPlacePlayer(0, new Vector2(0f, ArenaLayout.HalfHeight - 0.2f), Vector2.up);
            for (int i = 0; i < 10; i++)
                yield return new WaitForFixedUpdate();
            Assert.That(director.State, Is.EqualTo(MatchState.Live));
            Assert.That(director.ScoreP1, Is.EqualTo(0));
            Assert.That(director.Sim.Players[0].Carried, Is.EqualTo(0));

            director.DebugShoot(0);
            director.DebugShoot(0);
            director.DebugShoot(0);
            Assert.That(director.Sim.Players[0].Carried, Is.EqualTo(-1));
            Assert.That(director.Sim.Marbles[0].Velocity.sqrMagnitude, Is.GreaterThan(0.01f));
        }

        [UnityTest]
        public IEnumerator FreeMarbleInNorthPocketStillScores()
        {
            GameSession.Instance.BeginMatch(8, 1);
            yield return null;
            MatchDirector director = Object.FindObjectOfType<MatchDirector>();
            yield return new WaitForSeconds(ArenaLayout.KickoffSeconds + 0.2f);

            director.DebugPlacePlayer(0, ArenaLayout.PlayerOneSpawn, Vector2.down);
            director.DebugPlacePlayer(1, ArenaLayout.PlayerTwoSpawn, Vector2.up);
            director.DebugSetMarble(0, new Vector2(0f, ArenaLayout.HalfHeight + ArenaLayout.Wall + 0.55f), Vector2.zero);
            for (int i = 0; i < 20 && director.State == MatchState.Live; i++)
                yield return new WaitForFixedUpdate();
            Assert.That(director.ScoreP1, Is.EqualTo(1));
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

        static int ActiveMarbleCount(MatchDirector director)
        {
            int count = 0;
            for (int i = 0; i < director.Sim.Marbles.Length; i++)
            {
                if (director.Sim.Marbles[i].Active)
                    count++;
            }

            return count;
        }
    }
}
