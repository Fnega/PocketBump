using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PocketBump.Tests
{
    public sealed class DashMatchDirectorTests
    {
        readonly List<GameObject> _objects = new List<GameObject>();
        float _fixedDelta;

        [SetUp] public void SaveClock() { _fixedDelta = Time.fixedDeltaTime; }
        [TearDown] public void Cleanup()
        {
            foreach (GameObject item in _objects) if (item != null) Object.DestroyImmediate(item);
            _objects.Clear(); Time.fixedDeltaTime = _fixedDelta;
        }

        [Test]
        public void LocalDirectorOwnsOneWorldAndCanonicalizesBothInputsOncePerTick()
        {
            var director = Create();
            var rules = ArenaRulesProfile.CreateProductionV1(5, 3);
            director.InitializeLocal(rules, 31u, sampler: Sampler(new Keyboard()));
            var expected = DashArenaSimulation.Create(rules, 31u); DashArenaSimulation.StartMatch(expected);
            int[] samples = new int[2];
            director.InputOverride = (seat, tick, state) =>
            {
                Assert.That(state, Is.SameAs(director.Sim));
                samples[seat]++;
                return new DashArenaInput { Move = seat == 0 ? new Vector2(.3f, 0) : new Vector2(1, 1) };
            };
            for (int i = 0; i < 220; i++)
            {
                director.PumpFrame(); director.AdvanceOneTick();
                DashArenaSimulation.Step(expected, default, DashArenaInput.Unpack(5));
            }
            CollectionAssert.AreEqual(new[] { 220, 220 }, samples);
            Assert.That(director.Sim.Checksum(), Is.EqualTo(expected.Checksum()));
            Assert.That(director.LanSession, Is.Null);
            Assert.That(director.Settings.pointsToWin, Is.EqualTo(5));
            Assert.That(director.Settings.marbleCount, Is.EqualTo(3));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(1f / 60f));
            Assert.That(director.EpochInitializationCount, Is.EqualTo(1));
        }

        [Test]
        public void InitializationIsOneShotAndPregameDoesNotSampleOrAdvance()
        {
            var director = Create();
            var rules = ArenaRulesProfile.CreateProductionV1();
            director.InitializeLocal(rules, 4u, false, Sampler(new Keyboard()));
            DashArenaState original = director.Sim;
            director.InitializeLocal(ArenaRulesProfile.CreateProductionV1(3, 8), 9u);
            Assert.That(director.Sim, Is.SameAs(original));
            director.InputOverride = (seat, tick, state) => { Assert.Fail("Pregame sampled."); return default; };
            Assert.That(director.AdvanceOneTick(), Is.Empty);
            Assert.That(director.State, Is.EqualTo(MatchState.Pregame));
            Assert.That(director.Sim.Tick, Is.Zero);
            director.InputOverride = null; director.StartMatch(); director.AdvanceOneTick();
            Assert.That(director.Sim.Tick, Is.EqualTo(1));
            Assert.That(director.EpochInitializationCount, Is.EqualTo(1));
        }

        [Test]
        public void ProductionRejectsProvisionalProfileWithoutInitializing()
        {
            var director = Create();
            Assert.Throws<ArgumentException>(() => director.InitializeLocal(ArenaRulesProfile.CreateGreybox()));
            Assert.That(director.Initialized, Is.False);
            Assert.That(director.Sim, Is.Null);
        }

        [Test]
        public void LocalMenuFreezesWorldWithoutChangingGlobalTimeAndClearsDashOnBothTransitions()
        {
            var keyboard = new Keyboard(); var director = Create();
            director.InitializeLocal(ArenaRulesProfile.CreateProductionV1(), 12u, sampler: Sampler(keyboard));
            for (int i = 0; i < director.Rules.KickoffTicks; i++) director.AdvanceOneTick();
            keyboard.FrameCount++; keyboard.Pressed.Add(KeyCode.K); keyboard.Held.Add(KeyCode.K);
            director.PumpFrame(); director.SetMenuOpen(true);
            int checksum = director.Sim.Checksum(), tick = director.Sim.Tick; float scale = Time.timeScale;
            for (int i = 0; i < 400; i++) { keyboard.FrameCount++; director.PumpFrame(); director.AdvanceOneTick(); }
            Assert.That(director.LocalMenuPaused, Is.True);
            Assert.That(director.Sim.Checksum(), Is.EqualTo(checksum)); Assert.That(director.Sim.Tick, Is.EqualTo(tick));
            Assert.That(Time.timeScale, Is.EqualTo(scale));
            director.SetMenuOpen(false); director.PumpFrame(); director.AdvanceOneTick();
            Assert.That(director.Sim.Tick, Is.EqualTo(tick + 1)); Assert.That(director.Sim.Players[0].DashTicks, Is.Zero);
            keyboard.Pressed.Clear(); keyboard.FrameCount++; director.PumpFrame(); director.AdvanceOneTick();
            Assert.That(director.Sim.Players[0].DashTicks, Is.Zero);
            keyboard.Pressed.Add(KeyCode.K); keyboard.FrameCount++; director.PumpFrame(); director.AdvanceOneTick();
            Assert.That(director.Sim.Players[0].DashTicks, Is.GreaterThan(0));
        }

        [TestCase(false)] [TestCase(true)]
        public void LanPersonalMenuKeepsBothPeersProgressingWithNeutralInputAndNormalLeave(bool joinMenu)
        {
            float now = 0f; var rules = ArenaRulesProfile.CreateProductionV1();
            var host = Create(); var join = Create();
            host.InitializeLan(DashLanSession.Host(rules, 1552, 45u, () => now), Sampler(new Keyboard()));
            join.InitializeLan(DashLanSession.Join(rules, new IPEndPoint(IPAddress.Loopback, host.LanSession.LocalEndpoint.Port), () => now, 43), Sampler(new Keyboard()));
            DashMatchDirector personal = joinMenu ? join : host, peer = joinMenu ? host : join;
            // Wait for startup before opening the menu; epoch initialization closes old menus.
            for (int guard = 0; (!host.ReadyForNetplay || !join.ReadyForNetplay) && guard < 500; guard++)
            { host.PumpFrame(); join.PumpFrame(); now += .01f; Thread.Sleep(1); }
            Assert.That(host.ReadyForNetplay && join.ReadyForNetplay, Is.True);
            personal.SetMenuOpen(true);
            personal.InputOverride = (_, __, ___) => { Assert.Fail("Menu must suppress gameplay intent, including override."); return default; };
            peer.InputOverride = (_, __, ___) => default;
            for (int guard = 0; (host.Sim.Tick < 420 || join.Sim.Tick < 420) && guard < 1800; guard++)
            {
                if (host.Sim.Tick < 420) host.AdvanceOneTick();
                if (join.Sim.Tick < 420) join.AdvanceOneTick();
                host.PumpFrame(); join.PumpFrame(); now += 1f / 60f; Thread.Sleep(1);
                Assert.That(host.NetworkPaused || join.NetworkPaused, Is.False, host.FailureReason + "/" + join.FailureReason);
            }
            Assert.That(personal.MenuOpen, Is.True); Assert.That(personal.LocalMenuPaused, Is.False);
            Assert.That(host.Sim.Tick, Is.EqualTo(420)); Assert.That(join.Sim.Tick, Is.EqualTo(420));
            Assert.That(host.Sim.Checksum(), Is.EqualTo(join.Sim.Checksum()));
            Assert.That(personal.Sim.Players.All(p => p.DashTicks == 0), Is.True);
            personal.LeaveNetwork(); Thread.Sleep(2); peer.PumpFrame();
            Assert.That(peer.FailureReason, Is.EqualTo("PEER LEFT"));
        }

        [Test]
        public void ShortDashPressIsConsumedOnceAndHoldingDoesNotChain()
        {
            var keyboard = new Keyboard(); var director = Create();
            director.InitializeLocal(ArenaRulesProfile.CreateProductionV1(), 2u, sampler: Sampler(keyboard));
            for (int i = 0; i < 180; i++) director.AdvanceOneTick();
            keyboard.FrameCount = 1; keyboard.Pressed.Add(KeyCode.K); keyboard.Held.Add(KeyCode.K);
            director.PumpFrame(); director.PumpFrame(); director.AdvanceOneTick();
            Assert.That(director.Sim.Players[0].DashTicks, Is.EqualTo(18));
            Assert.That(director.Sim.Players[1].DashTicks, Is.Zero);
            keyboard.FrameCount++; keyboard.Pressed.Clear();
            for (int i = 0; i < 240; i++) { director.PumpFrame(); director.AdvanceOneTick(); }
            Assert.That(director.Sim.Players[0].DashTicks, Is.Zero);
            Assert.That(director.Sim.Players[0].DashCooldownTicks, Is.Zero);
        }

        [Test]
        public void LocalTerminalFixtureFreezesThenCreatesCleanSameRulesEpochWithoutOldEdge()
        {
            var keyboard = new Keyboard(); var director = Create();
            var rules = ArenaRulesProfile.CreateProductionV1(5, 2);
            director.InitializeLocal(rules, 8u, sampler: Sampler(keyboard));
            Assert.That(director.RequestRematch(), Is.False);
            // Explicit terminal-state mechanism fixture; not natural gameplay evidence.
            director.Sim.Scores[0] = 5; director.Sim.Scores[1] = 2;
            director.Sim.Phase = MatchState.Scored; director.Sim.PhaseTicks = 1;
            director.Sim.Walls[0] = new DashWallState { Broken = true, RepairTicks = 400 };
            director.AdvanceOneTick();
            Assert.That(director.ResultsReady && director.CanModifyRules && director.CanRequestRematch, Is.True);
            Assert.That(director.Winner, Is.Zero);
            int hash = director.Sim.Checksum();
            director.InputOverride = (seat, tick, state) => { Assert.Fail("Terminal world sampled."); return default; };
            for (int i = 0; i < 4; i++) director.AdvanceOneTick();
            Assert.That(director.Sim.Checksum(), Is.EqualTo(hash));
            director.InputOverride = null;
            keyboard.FrameCount++; keyboard.Pressed.Add(KeyCode.K);
            director.PumpFrame();
            Assert.That(director.RequestRematch(), Is.True);
            Assert.That(director.Rules, Is.SameAs(rules));
            Assert.That(director.MatchEpoch, Is.EqualTo(1));
            Assert.That(director.Sim.Tick, Is.Zero);
            Assert.That(director.EpochInitializationCount, Is.EqualTo(2));
            Assert.That(director.Sim.Walls.All(w => !w.Broken && w.RepairTicks == 0), Is.True);
            CollectionAssert.AreEqual(new[] { 0, 0 }, director.Sim.Scores);
            director.PumpFrame();
            for (int i = 0; i < 181; i++) director.AdvanceOneTick();
            Assert.That(director.Sim.Players.All(p => p.DashTicks == 0), Is.True);
        }

        [Test]
        public void BrokenPresentationConsumerDoesNotStopOtherConsumersOrSimulation()
        {
            var director = Create(); director.InitializeLocal(ArenaRulesProfile.CreateProductionV1(), sampler: Sampler(new Keyboard()));
            var events = new List<DashArenaEvent>();
            director.ConfirmedEvents += _ => throw new InvalidOperationException("broken listener");
            director.ConfirmedEvents += batch => events.AddRange(batch);
            for (int i = 0; i < 181; i++) director.AdvanceOneTick();
            Assert.That(events.Count(e => e.Kind == DashArenaEventKind.GateOpened), Is.EqualTo(1));
            Assert.That(director.PresentationErrorCount, Is.EqualTo(1));
            Assert.That(director.LastPresentationError, Does.Contain("broken listener"));
            Assert.That(director.NetworkPaused, Is.False);
            Assert.That(director.Sim.Tick, Is.EqualTo(181));
        }

        [Test]
        public void NetworkDirectorsUseOnlyAssignedSeatsAndShareTheRealFourHundredTickWorld()
        {
            float now = 0f; var rules = ArenaRulesProfile.CreateProductionV1();
            var host = Create(); var join = Create();
            var hostSession = DashLanSession.Host(rules, 1551, 44u, () => now);
            var endpoint = new IPEndPoint(IPAddress.Loopback, hostSession.LocalEndpoint.Port);
            host.InitializeLan(hostSession, Sampler(new Keyboard()));
            join.InitializeLan(DashLanSession.Join(rules, endpoint, () => now, 42), Sampler(new Keyboard()));
            int hostSamples = 0, joinSamples = 0;
            host.InputOverride = (seat, tick, state) => { Assert.That(seat, Is.Zero); hostSamples++; return default; };
            join.InputOverride = (seat, tick, state) => { Assert.That(seat, Is.EqualTo(1)); joinSamples++; return default; };
            for (int guard = 0; (host.Sim.Tick < 400 || join.Sim.Tick < 400) && guard < 1800; guard++)
            {
                if (host.Sim.Tick < 400) host.AdvanceOneTick();
                if (join.Sim.Tick < 400) join.AdvanceOneTick();
                host.PumpFrame(); join.PumpFrame();
                Assert.That(host.NetworkPaused || join.NetworkPaused, Is.False, host.FailureReason + "/" + join.FailureReason);
                now += 1f / 60f; Thread.Sleep(1);
            }
            Assert.That(hostSamples, Is.EqualTo(400)); Assert.That(joinSamples, Is.EqualTo(400));
            Assert.That(host.Sim.Checksum(), Is.EqualTo(join.Sim.Checksum()));
            Assert.That(host.ReadyForNetplay && join.ReadyForNetplay, Is.True);
            Assert.That(host.EpochInitializationCount, Is.EqualTo(1)); Assert.That(join.EpochInitializationCount, Is.EqualTo(1));
            Assert.That(host.Lifecycle.Phase, Is.EqualTo(LanLifecyclePhase.Playing));
            Assert.That(join.Lifecycle.Phase, Is.EqualTo(LanLifecyclePhase.Playing));
            Assert.That(join.CanModifyRules, Is.False);
            int tickBefore = host.Sim.Tick;
            join.LeaveNetwork(); Thread.Sleep(2); host.PumpFrame();
            Assert.That(host.NetworkPaused, Is.True); Assert.That(host.FailureReason, Is.EqualTo("PEER LEFT"));
            host.AdvanceOneTick();
            Assert.That(host.Sim.Tick, Is.EqualTo(tickBefore));
        }

        [Test]
        public void HandshakeTimeoutPreservesPregameWithoutPausingGlobalClockOrSampling()
        {
            float now = 0f, scale = Time.timeScale;
            var director = Create();
            director.InitializeLan(DashLanSession.Host(ArenaRulesProfile.CreateProductionV1(), 812, 9u, () => now), Sampler(new Keyboard()));
            director.InputOverride = (seat, tick, state) => { Assert.Fail("Incomplete handshake sampled."); return default; };
            int hash = director.Sim.Checksum();
            director.AdvanceOneTick();
            now = 15.01f; director.PumpFrame();
            Assert.That(director.NetworkPaused, Is.True);
            Assert.That(director.FailureReason, Is.EqualTo("LAN HANDSHAKE TIMEOUT"));
            Assert.That(director.StatusMessage, Is.EqualTo(director.FailureReason));
            for (int i = 0; i < 10; i++) director.AdvanceOneTick();
            Assert.That(director.Sim.Checksum(), Is.EqualTo(hash));
            Assert.That(Time.timeScale, Is.EqualTo(scale));
            Assert.That(director.EpochInitializationCount, Is.Zero);
            Assert.That(director.LanSession.SocketBound, Is.False);
        }

        [Test]
        public void OccupiedEphemeralPortBecomesVisibleInitializationFailureAndKeepsOtherSocket()
        {
            using (var occupied = new LanUdpPeer())
            {
                Assert.That(occupied.BindEphemeral(), Is.True);
                var director = Create();
                director.InitializeLan(DashLanSession.Host(ArenaRulesProfile.CreateProductionV1(), 813, 9u,
                    () => 0f, occupied.LocalEndPoint.Port), Sampler(new Keyboard()));
                Assert.That(director.NetworkPaused, Is.True);
                Assert.That(director.FailureReason, Is.EqualTo("COULD NOT BIND PORT"));
                Assert.That(director.NetworkInitializationError, Is.Not.Empty);
                Assert.That(director.State, Is.EqualTo(MatchState.Pregame));
                director.AdvanceOneTick(); Assert.That(director.Sim.Tick, Is.Zero);
                director.LeaveNetwork();
                Assert.That(occupied.Bound, Is.True);
            }
        }

        DashMatchDirector Create()
        {
            var root = new GameObject("Isolated Dash Director Test"); _objects.Add(root);
            var director = root.AddComponent<DashMatchDirector>(); director.AutomaticTicking = false;
            return director;
        }

        static DashTickInputSampler Sampler(Keyboard keyboard) => new DashTickInputSampler(new DashControlBindings(new Store()), keyboard);
        sealed class Keyboard : IDashKeyboardInput
        {
            public int FrameCount { get; set; }
            public readonly HashSet<KeyCode> Held = new HashSet<KeyCode>();
            public readonly HashSet<KeyCode> Pressed = new HashSet<KeyCode>();
            public bool IsHeld(KeyCode key) => Held.Contains(key);
            public bool WasPressedThisFrame(KeyCode key) => Pressed.Contains(key);
        }
        sealed class Store : IDashBindingStore
        {
            public bool HasKey(string key) => false;
            public int GetInt(string key, int fallback) => fallback;
            public void SetInt(string key, int value) { }
            public void Save() { }
        }
    }
}
