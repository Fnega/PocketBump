using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

// 在真实场景生命周期中验证菜单、比赛、表现对象与模拟状态的集成流程。
// EditMode 已覆盖算法细节，这里保留必须依赖场景加载和 MonoBehaviour 的端到端检查。
namespace PocketBump.Tests
{
    public sealed class PocketBumpFlowTests
    {
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
                Assert.That(Object.FindObjectOfType<MatchDirector>(), Is.Null);
            }
            LogAssert.Expect(LogType.Log, "Exit requested (ignored while running in the Unity Editor).");
            Click("Exit");
            AssertHomeRegions(authored);
        }

        static void AssertHomeRegions(MainMenuController menu)
        {
            Transform home = menu.transform.Find("MainMenuCanvas/MainPanel");
            Assert.That(home.gameObject.activeInHierarchy, Is.True);
            string[] names = { "StartRegion/Start", "ServerRegion/HostLan", "ServerRegion/JoinLan", "ExitRegion/Exit" };
            foreach (string path in names)
            {
                Transform button = home.Find(path);
                Assert.That(button, Is.Not.Null, path);
                Assert.That(button.gameObject.activeInHierarchy, Is.True);
                Assert.That(button.parent.Find("Heading"), Is.Not.Null);
            }
            Assert.That(home.GetComponentsInChildren<Button>(true).Length, Is.EqualTo(4));
        }

        [UnityTest]
        // 从菜单改规则进入 Arena，再返回修改，验证跨场景 GameSession 的完整数据流。
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
        // 赛后重赛保留规则，返回主页离开比赛，覆盖结果面板的三个导航入口。
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
        // 在真实 View 同步下确认速度分级、冷却和弹珠击退最终可造成玩家掉落。
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
        // 无外力弹珠应单调减速至静止，防止碰撞/阻力实现意外注入能量。
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
        // 端到端区分篮筐收集、身体反弹和对手抢夺三种空间关系。
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
        // 被持有弹珠经过球门不计分，释放后进入相同区域才产生得分。
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
        // 自由弹珠进入 P1 防守的北门时，应由 P2 得分并结束当前回合。
        public IEnumerator FreeMarbleInNorthPocketScoresForPlayerTwo()
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
            Assert.That(director.ScoreP1, Is.EqualTo(0));
            Assert.That(director.ScoreP2, Is.EqualTo(1));
            Assert.That(director.State, Is.EqualTo(MatchState.Scored));
            Assert.That(director.Sim.LastScorer, Is.EqualTo(1));
            Assert.That(director.Sim.LastScoreReason, Is.EqualTo("P2 SCORES - P1 POCKET"));
        }

        [UnityTest]
        // 自由弹珠进入 P2 防守的南门时，应由 P1 得分并结束当前回合。
        public IEnumerator FreeMarbleInSouthPocketScoresForPlayerOne()
        {
            GameSession.Instance.BeginMatch(8, 1);
            yield return null;
            MatchDirector director = Object.FindObjectOfType<MatchDirector>();
            yield return new WaitForSeconds(ArenaLayout.KickoffSeconds + 0.2f);

            director.DebugPlacePlayer(0, ArenaLayout.PlayerOneSpawn, Vector2.down);
            director.DebugPlacePlayer(1, ArenaLayout.PlayerTwoSpawn, Vector2.up);
            director.DebugSetMarble(0, new Vector2(0f, -ArenaLayout.HalfHeight - ArenaLayout.Wall - 0.55f), Vector2.zero);
            for (int i = 0; i < 20 && director.State == MatchState.Live; i++)
                yield return new WaitForFixedUpdate();
            Assert.That(director.ScoreP1, Is.EqualTo(1));
            Assert.That(director.ScoreP2, Is.EqualTo(0));
            Assert.That(director.State, Is.EqualTo(MatchState.Scored));
            Assert.That(director.Sim.LastScorer, Is.EqualTo(0));
            Assert.That(director.Sim.LastScoreReason, Is.EqualTo("P1 SCORES - P2 POCKET"));
        }

        // 等待异步场景加载完成后再多让一帧，使 Awake/Start 创建的对象全部就绪。
        static IEnumerator LoadScene(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
            yield return null;
        }

        // 通过稳定对象名定位按钮，测试的是用户路径而非直接调用控制器私有方法。
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
