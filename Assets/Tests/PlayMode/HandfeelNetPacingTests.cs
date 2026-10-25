using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PocketBump.Tests
{
    // Real loopback sockets/directors/HUD, with an explicit tick-zero mechanism fixture
    // and manual clocks. Readiness-model assertions are not visual or physical-LAN QA.
    public sealed class HandfeelNetPacingTests
    {
        GameObject root;
        DashMatchDirector fast, slow;
        DashMatchHud hud;
        ArenaRulesProfile rules;
        int fastSeat, initialCooldown, sampleCalls;
        bool pendingDash;
        float now, oldScale, oldFixed;
        string reports, reportName;
        readonly List<DashArenaEvent> committed = new List<DashArenaEvent>();

        [SetUp]
        public void Setup()
        {
            oldScale = Time.timeScale; oldFixed = Time.fixedDeltaTime;
            Time.timeScale = 1f;
            now = 0f; sampleCalls = 0; pendingDash = false; committed.Clear();
            reportName = "PocketBump-HandfeelNet-Test-" + Guid.NewGuid().ToString("N");
            reports = Path.GetFullPath(Path.Combine(Path.GetTempPath(), reportName));
            Assert.That(Directory.Exists(reports), Is.False);
            Directory.CreateDirectory(reports);
            root = new GameObject("Candidate actual UDP pacing fixture");
        }

        [TearDown]
        public void Teardown()
        {
            try { if (root != null) Object.DestroyImmediate(root); }
            finally
            {
                Time.timeScale = oldScale; Time.fixedDeltaTime = oldFixed;
                // Destroy/report writers first. Delete only this fresh, validated, flat
                // GUID directory, never the real game's persistent NetworkReports.
                if (reports != null && Directory.Exists(reports))
                {
                    string resolved = Path.GetFullPath(reports);
                    Assert.That(resolved, Is.EqualTo(Path.GetFullPath(Path.Combine(Path.GetTempPath(), reportName))));
                    Assert.That(Path.GetFileName(resolved), Is.EqualTo(reportName));
                    Assert.That(File.GetAttributes(resolved) & FileAttributes.ReparsePoint, Is.EqualTo((FileAttributes)0));
                    Assert.That(Directory.GetDirectories(resolved), Is.Empty);
                    Directory.Delete(resolved, true);
                }
            }
        }

        [TestCase(0)] [TestCase(1)]
        public void ActualWaitFreezesAcceptedRecoilAndCooldownThenRecoversWithoutRepeatedContact(int seat)
        {
            CreatePair(seat, 120); EnterActualWait();
            DashArenaState before = fast.Sim.Clone();
            int tick = before.Tick;
            Assert.That(before.Players[seat].RecoilGraceTicks, Is.GreaterThan(0));
            Assert.That(before.Players[seat].ExternalVelocity.sqrMagnitude, Is.GreaterThan(0f));
            Assert.That(before.Players[seat].DashCooldownTicks, Is.GreaterThan(0));
            DashReadinessState marker = DashReadinessState.Evaluate(before, seat, seat);
            DashReadinessState opponentView = DashReadinessState.Evaluate(before, seat, 1 - seat);
            Assert.That(marker.Kind, Is.EqualTo(DashReadinessKind.Cooling));
            Assert.That(marker.Detailed, Is.True);
            Assert.That(opponentView.Kind, Is.EqualTo(DashReadinessKind.Disabled));
            int sends = fast.LanSession.SendAttempts, receives = fast.LanSession.ReceivedDatagrams;
            int samples = sampleCalls;
            for (int i = 0; i < 12; i++)
            {
                now += .05f; slow.PumpFrame(); Thread.Sleep(1); fast.PumpFrame(); fast.AdvanceOneTick();
                hud.RefreshFromDirector();
                AssertComplete(before, fast.Sim);
                AssertReadinessEqual(marker, DashReadinessState.Evaluate(fast.Sim, seat, seat));
                AssertReadinessEqual(opponentView, DashReadinessState.Evaluate(fast.Sim, seat, 1 - seat));
                Assert.That(hud.NetworkNoticeVisible, Is.False);
            }
            Assert.That(sampleCalls, Is.EqualTo(samples), "Blocked steps must not sample another input.");
            Assert.That(fast.LanSession.SendAttempts, Is.GreaterThan(sends));
            Assert.That(fast.LanSession.ReceivedDatagrams, Is.GreaterThan(receives));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            ResumeFromQueuedInput();
            Assert.That(fast.Sim.Tick, Is.EqualTo(tick), "Pump restores readiness without consuming a simulation update.");
            DriveBothTo(40);
            AssertComplete(fast.Sim, slow.Sim);
            CompareWithUninterruptedReference(40, -1);
            Assert.That(committed.Count(e => e.Kind == DashArenaEventKind.Impact && e.Seat == seat), Is.GreaterThan(0));
            Assert.That(committed.Select(e => e.Identity).Distinct().Count(), Is.EqualTo(committed.Count));
            Assert.That(hud.NetworkNoticeVisible, Is.False);
            Assert.That(fast.NetworkPaused || slow.NetworkPaused, Is.False);
        }

        [TestCase(0)] [TestCase(1)]
        public void ResumedCandidateConsumesPendingDashOnlyOnceAndMatchesOnTimeFullState(int seat)
        {
            CreatePair(seat, 0); EnterActualWait();
            int waitingTick = fast.Sim.Tick;
            int samples = sampleCalls;
            pendingDash = true;
            for (int i = 0; i < 6; i++) fast.AdvanceOneTick();
            Assert.That(pendingDash, Is.True);
            Assert.That(sampleCalls, Is.EqualTo(samples));
            ResumeFromQueuedInput();
            DriveBothTo(waitingTick + 25);
            Assert.That(pendingDash, Is.False);
            int appliedTick = waitingTick + DashRollbackSession.DelayTicks;
            Assert.That(committed.Count(e => e.Kind == DashArenaEventKind.DashStarted && e.Seat == seat), Is.EqualTo(1));
            DashArenaEvent dash = committed.Single(e => e.Kind == DashArenaEventKind.DashStarted && e.Seat == seat);
            Assert.That(dash.Tick, Is.EqualTo(appliedTick));
            AssertComplete(fast.Sim, slow.Sim);
            CompareWithUninterruptedReference(waitingTick + 25, appliedTick);
            Assert.That(hud.NetworkNoticeVisible, Is.False);
        }

        [TestCase(0)] [TestCase(1)]
        public void CandidateInputProgressTimeoutRemainsVisibleActionableAndFreezesRecoil(int seat)
        {
            CreatePair(seat, 120); EnterActualWait();
            DashArenaState frozen = fast.Sim.Clone();
            DashReadinessState marker = DashReadinessState.Evaluate(frozen, seat, seat);
            for (int i = 0; i < 60 && !fast.NetworkPaused; i++)
            {
                now += .1f; slow.PumpFrame(); Thread.Sleep(1); fast.PumpFrame();
            }
            Assert.That(fast.NetworkPaused, Is.True);
            Assert.That(fast.FailureReason, Is.EqualTo("INPUT PROGRESS TIMEOUT"));
            Assert.That(hud.NetworkIssueVisible, Is.True);
            Assert.That(hud.NetworkWaitingVisible, Is.False);
            Assert.That(hud.NetworkIssueReason, Is.EqualTo("INPUT PROGRESS TIMEOUT"));
            Assert.That(hud.NetworkIssueRect.anchorMin, Is.EqualTo(new Vector2(1f, 0f)));
            foreach (string name in new[] { "NetworkMainMenu", "NetworkOpenReports" })
            {
                Button action = hud.NetworkIssueRect.Find(name).GetComponent<Button>();
                Assert.That(action.gameObject.activeInHierarchy && action.interactable, Is.True, name);
            }
            for (int i = 0; i < 10; i++)
            {
                now += .1f; fast.PumpFrame(); fast.AdvanceOneTick();
                AssertComplete(frozen, fast.Sim);
                AssertReadinessEqual(marker, DashReadinessState.Evaluate(fast.Sim, seat, seat));
            }
            int menuRequests = 0;
            hud.MainMenuRequested += () => menuRequests++;
            Assert.That(menuRequests, Is.Zero, "The terminal fault must not automatically leave the match.");
            hud.NetworkIssueRect.Find("NetworkMainMenu").GetComponent<Button>().onClick.Invoke();
            Assert.That(menuRequests, Is.EqualTo(1));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [TestCase(0)] [TestCase(1)]
        public void StageOnlyFaultInjectionBecomesConfirmedDesyncOverRealSocketsWithTerminalControls(int seat)
        {
            CreatePair(seat, 120); DriveBothTo(16);
            DashMatchHud peerHud = DashMatchHud.Build(slow.transform, DashUiTheme.Load().Font);
            slow.Configure(null, peerHud);
            AssertComplete(fast.Sim, slow.Sim);
            Assert.That(fast.Sim.Players[seat].RecoilGraceTicks, Is.EqualTo(6));
            // Explicit one-field fault injection AFTER an actual accepted collision.
            // The failure flag/checksum queue is never forged: real packets must expose it.
            int previousChecksum = fast.Sim.Checksum();
            fast.Sim.Players[seat].RecoilGraceTicks--;
            Assert.That(fast.Sim.Checksum(), Is.Not.EqualTo(previousChecksum));
            for (int i = 0; i < 100 && !(fast.NetworkPaused && slow.NetworkPaused); i++)
            {
                fast.AdvanceOneTick(); slow.AdvanceOneTick();
                fast.PumpFrame(); slow.PumpFrame(); now += .002f; Thread.Sleep(1);
            }
            DashMatchDirector[] peers = { fast, slow };
            int detectors = peers.Count(peer => peer.LanSession.FailureOrigin == "local" &&
                peer.LanSession.Rollback.Status == RollbackStatus.Desync);
            Assert.That(detectors, Is.GreaterThanOrEqualTo(1),
                "At least one endpoint must actually validate a contradictory checksum, not merely receive a fault string.");
            foreach (DashMatchDirector peer in peers)
            {
                DashLanSession session = peer.LanSession;
                Assert.That(peer.NetworkPaused, Is.True);
                if (session.FailureOrigin == "local")
                {
                    Assert.That(peer.FailureReason, Is.EqualTo("DESYNC"));
                    Assert.That(session.Rollback.Status, Is.EqualTo(RollbackStatus.Desync));
                    Assert.That(session.PeerFailureReason, Is.Empty);
                    Assert.That(peer.Hud.NetworkIssueReason, Is.EqualTo("DESYNC"));
                }
                else
                {
                    // Pump drains UDP before checking retained checksums. The peer
                    // sees a remote first cause if Fault arrives first; otherwise both
                    // peers may independently detect DESYNC before either Fault arrives.
                    // Never assume the faster simulation is the detecting endpoint.
                    Assert.That(detectors, Is.EqualTo(1));
                    Assert.That(session.FailureOrigin, Is.EqualTo("peer"));
                    Assert.That(peer.FailureReason, Is.EqualTo("PEER FAULT"));
                    Assert.That(session.PeerFailureReason, Is.EqualTo("DESYNC"));
                    Assert.That(peer.Hud.NetworkIssueReason, Is.EqualTo("PEER FAULT: DESYNC"));
                }
                Assert.That(peer.Hud.NetworkIssueVisible, Is.True);
                Assert.That(peer.Hud.NetworkWaitingVisible, Is.False);
            }
            DashArenaState frozen = fast.Sim.Clone();
            DashArenaState peerFrozen = slow.Sim.Clone();
            int menuRequests = 0;
            hud.MainMenuRequested += () => menuRequests++;
            peerHud.MainMenuRequested += () => menuRequests++;
            for (int i = 0; i < 10; i++)
            {
                now += .05f;
                fast.PumpFrame(); fast.AdvanceOneTick(); slow.PumpFrame(); slow.AdvanceOneTick();
            }
            AssertComplete(frozen, fast.Sim);
            AssertComplete(peerFrozen, slow.Sim);
            Assert.That(menuRequests, Is.Zero);
            foreach (DashMatchHud view in new[] { hud, peerHud })
                foreach (string name in new[] { "NetworkMainMenu", "NetworkOpenReports" })
                {
                    Button action = view.NetworkIssueRect.Find(name).GetComponent<Button>();
                    Assert.That(action.gameObject.activeInHierarchy && action.interactable, Is.True);
                }
            peerHud.NetworkIssueRect.Find("NetworkMainMenu").GetComponent<Button>().onClick.Invoke();
            Assert.That(menuRequests, Is.EqualTo(1), "Only explicit navigation leaves the frozen match.");
        }

        void CreatePair(int seat, int cooldown)
        {
            fastSeat = seat; initialCooldown = cooldown;
            rules = ArenaRulesProfile.CreateHandfeelCandidate();
            DashMatchDirector host = Director("Host"), join = Director("Join");
            host.InitializeLan(DashLanSession.Host(rules, 7193, 29u, () => now,
                initialStateFactory: Initial, allowHandfeelCandidate: true));
            join.InitializeLan(DashLanSession.Join(rules, new IPEndPoint(IPAddress.Loopback, host.LanSession.LocalEndpoint.Port),
                () => now, 892u, initialStateFactory: Initial, allowHandfeelCandidate: true));
            fast = seat == 0 ? host : join; slow = seat == 0 ? join : host;
            fast.InputOverride = (_, __, ___) =>
            {
                sampleCalls++;
                bool dash = pendingDash; pendingDash = false;
                return new DashArenaInput { Move = Direction, DashPressed = dash };
            };
            slow.InputOverride = (_, __, ___) => default;
            hud = DashMatchHud.Build(fast.transform, DashUiTheme.Load().Font);
            fast.Configure(null, hud);
            fast.ConfirmedEvents += events => committed.AddRange(events);
            DriveBothTo(5);
        }

        DashMatchDirector Director(string name)
        {
            var child = new GameObject(name); child.transform.SetParent(root.transform);
            var director = child.AddComponent<DashMatchDirector>(); director.AutomaticTicking = false;
            child.AddComponent<DashNetworkReporter>().Bind(director, reports);
            return director;
        }

        Vector2 Direction => fastSeat == 0 ? Vector2.right : Vector2.left;

        DashArenaState Initial(ArenaRulesProfile profile, uint seed, int epoch)
        {
            var state = DashArenaSimulation.Create(profile, seed, epoch);
            DashArenaSimulation.StartMatch(state);
            state.Phase = MatchState.Live; state.PhaseTicks = 0; state.GateClosed = false;
            state.Players[fastSeat].Position = -Direction * 2f;
            state.Players[fastSeat].Facing = Direction;
            state.Players[fastSeat].DashCooldownTicks = initialCooldown;
            state.Players[1 - fastSeat].Position = new Vector2(0f, 15f);
            // With two input-delay ticks and normal 0.6/tick acceleration, this
            // contact occurs on update 15, just before the genuine lead-limit wait.
            state.Balls[0] = new DashBallState { Active = true, Position = Direction * .1f };
            return state;
        }

        void EnterActualWait()
        {
            for (int i = 0; i < 40 && !fast.WaitingForRemoteInput; i++)
            {
                fast.AdvanceOneTick(); fast.PumpFrame(); slow.PumpFrame(); now += .001f; Thread.Sleep(1);
            }
            Assert.That(fast.WaitingForRemoteInput, Is.True);
            Assert.That(fast.Sim.Tick, Is.EqualTo(16), "The contact must be immediately before the real lead-limit wait.");
            Assert.That(fast.Sim.Players[fastSeat].RecoilGraceTicks, Is.EqualTo(6), "A real collision, not a forged field, arms recoil.");
            Assert.That(fast.Sim.Tick - fast.LanSession.RemoteContiguousInputTick, Is.EqualTo(DashLanSession.MaximumInputLeadTicks));
            Assert.That(hud.NetworkNoticeVisible, Is.False);
            Assert.That(fast.NetworkPaused, Is.False);
        }

        void ResumeFromQueuedInput()
        {
            slow.AdvanceOneTick();
            for (int i = 0; i < 20 && fast.WaitingForRemoteInput; i++) { Thread.Sleep(1); fast.PumpFrame(); }
            Assert.That(fast.WaitingForRemoteInput, Is.False);
        }

        void DriveBothTo(int target)
        {
            for (int guard = 0; guard < target * 10 + 200 && (fast.Sim.Tick < target || slow.Sim.Tick < target); guard++)
            {
                if (fast.Sim.Tick < target) fast.AdvanceOneTick();
                if (slow.Sim.Tick < target) slow.AdvanceOneTick();
                fast.PumpFrame(); slow.PumpFrame(); now += .002f; Thread.Sleep(1);
                Assert.That(fast.NetworkPaused || slow.NetworkPaused, Is.False, fast.FailureReason + "/" + slow.FailureReason);
            }
            Assert.That(fast.Sim.Tick, Is.EqualTo(target)); Assert.That(slow.Sim.Tick, Is.EqualTo(target));
            for (int i = 0; i < 4; i++) { fast.PumpFrame(); slow.PumpFrame(); Thread.Sleep(1); }
        }

        void CompareWithUninterruptedReference(int target, int dashAppliedTick)
        {
            DashArenaState reference = Initial(rules, 29u, 0);
            var events = new List<DashArenaEvent>();
            for (int tick = 0; tick < target; tick++)
            {
                var input = new DashArenaInput { Move = tick < 2 ? Vector2.zero : Direction, DashPressed = tick == dashAppliedTick };
                events.AddRange(DashArenaSimulation.Step(reference, fastSeat == 0 ? input : default, fastSeat == 1 ? input : default));
            }
            AssertComplete(reference, fast.Sim);
            CollectionAssert.AreEqual(events, committed,
                "Recovery/retries must neither add contact assistance nor duplicate confirmed Dash/impact events.");
        }

        static void AssertReadinessEqual(DashReadinessState expected, DashReadinessState actual)
        {
            Assert.That(actual.Kind, Is.EqualTo(expected.Kind));
            Assert.That(actual.Detailed, Is.EqualTo(expected.Detailed));
            Assert.That(actual.Progress, Is.EqualTo(expected.Progress));
        }

        static void AssertComplete(DashArenaState expected, DashArenaState actual)
        {
            Assert.That(actual.Rules.ContentHash, Is.EqualTo(expected.Rules.ContentHash));
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            foreach (FieldInfo field in typeof(DashArenaState).GetFields(flags))
                Exact(field.GetValue(expected), field.GetValue(actual), field.Name);
            foreach (PropertyInfo property in typeof(DashArenaState).GetProperties(flags))
            {
                if (!property.PropertyType.IsArray) continue;
                var first = (Array)property.GetValue(expected); var second = (Array)property.GetValue(actual);
                Assert.That(second.Length, Is.EqualTo(first.Length), property.Name);
                for (int i = 0; i < first.Length; i++) Exact(first.GetValue(i), second.GetValue(i), property.Name + "[" + i + "]");
            }
            Assert.That(actual.Checksum(), Is.EqualTo(expected.Checksum()));
        }

        static void Exact(object expected, object actual, string path)
        {
            Type type = expected.GetType();
            if (type == typeof(float))
                Assert.That(BitConverter.ToInt32(BitConverter.GetBytes((float)actual), 0),
                    Is.EqualTo(BitConverter.ToInt32(BitConverter.GetBytes((float)expected), 0)), path);
            else if (type.IsPrimitive || type.IsEnum) Assert.That(actual, Is.EqualTo(expected), path);
            else foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                Exact(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name);
        }
    }
}
