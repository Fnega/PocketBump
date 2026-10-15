using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PocketBump.Tests
{
    public sealed class PocketBumpQueuedInputTests
    {
        [UnityTearDown]
        public IEnumerator RestoreMenu()
        {
            Time.timeScale = 1f;
            GameSession.Instance.ErrorMessage = "";
            GameSession.Instance.ReturnToMainMenu();
            yield return null;
        }

        [UnityTest]
        public IEnumerator FixedCatchUpDrainsAlreadyQueuedInputsBeforeRollbackWindowExpires()
        {
            GameSession.Instance.BeginHost(8, 1);
            yield return null;
            var director = Object.FindObjectOfType<DashMatchDirector>();
            Assert.That(director, Is.Not.Null);
            using (var pair = new DashSceneSocketPair(director))
            {
                pair.DriveTo(220);
                int waitingTick = director.LanSession.RemoteContiguousInputTick + DashLanSession.MaximumInputLeadTicks;
                for (int i = 0; i < 14; i++) director.AdvanceOneTick();
                Assert.That(director.Sim.Tick, Is.EqualTo(waitingTick), "An ungenerated suffix paces with input-delay headroom left for checksum history.");
                Assert.That(director.WaitingForRemoteInput, Is.True);
                Assert.That(director.NetworkPaused, Is.False);
                // Actual peer samples enter the actual UDP queue. No rendered Update
                // runs between these manually driven catch-up FixedUpdates.
                for (int i = 0; i < 14; i++) pair.Peer.Advance((epoch, tick) => default);
                AssertQueueReady(DashSceneSocketPair.Socket(director.LanSession));
                InvokeFixed(director);
                Assert.That(director.NetworkPaused, Is.False, director.FailureReason);
                Assert.That(director.Sim.Tick, Is.EqualTo(waitingTick + 1));
                Assert.That(director.WaitingForRemoteInput, Is.False);
                Assert.That(director.LanSession.Rollback.Status, Is.EqualTo(RollbackStatus.Ok));
                for (int step = 0; step < 20; step++)
                {
                    int before = director.Sim.Tick;
                    pair.Peer.Advance((epoch, tick) => default);
                    AssertQueueReady(DashSceneSocketPair.Socket(director.LanSession));
                    InvokeFixed(director);
                    Assert.That(director.NetworkPaused, Is.False, "Catch-up step " + step);
                    Assert.That(director.Sim.Tick, Is.EqualTo(before + 1));
                }
                Assert.That(DashRollbackSession.BufferTicks, Is.EqualTo(12));
            }
        }

        [UnityTest]
        public IEnumerator FixedCatchUpStillFreezesForMissingInputDespiteQueuedHeartbeat()
        {
            GameSession.Instance.BeginHost(8, 1);
            yield return null;
            var director = Object.FindObjectOfType<DashMatchDirector>();
            using (var pair = new DashSceneSocketPair(director))
            {
                pair.DriveTo(220);
                for (int i = 0; i < 14; i++) director.AdvanceOneTick();
                Assert.That(director.WaitingForRemoteInput, Is.True);
                // A later REAL generated sample proves an interior hole. Drop this
                // fixture's queued original history, then deliver only its newest
                // actual sample so pacing must not conceal the missing 222..224.
                for (int i = 0; i < 4; i++) pair.Peer.Advance((epoch, tick) => default);
                Thread.Sleep(2);
                while (DashSceneSocketPair.Socket(director.LanSession).TryRecv(out _, out _)) { }
                int newest = pair.Peer.Lifecycle.HighestSentTick;
                var history = new byte[DashNetcode.HistoryLength];
                Assert.That(pair.Peer.Rollback.CopyLocalHistory(newest, history), Is.GreaterThan(0));
                Assert.That(DashSceneSocketPair.Socket(pair.Peer).Send(DashNetcode.EncodeInput(new NetInputPacket
                {
                    MatchId = pair.Peer.MatchId, Epoch = pair.Peer.Epoch, Seat = (byte)pair.Peer.LocalSeat,
                    NewestTick = newest, HistoryCount = 1, InputBits = history,
                    AckTick = pair.Peer.Rollback.ConfirmedTick, ChecksumTick = -1, Checksum = 0
                })), Is.True);
                AssertQueueReady(DashSceneSocketPair.Socket(director.LanSession));
                InvokeFixed(director);
                Assert.That(director.WaitingForRemoteInput, Is.False);
                Assert.That(director.LanSession.Rollback.ConfirmedTick, Is.EqualTo(221));
                Assert.That(director.LanSession.Rollback.NewestRemoteTick, Is.EqualTo(225));
                // Earlier suffix pacing must not shrink the actual rollback window:
                // a proven interior hole still allows simulation up to tick 234.
                int holeBoundaryTick = director.LanSession.Rollback.ConfirmedTick + DashRollbackSession.BufferTicks + 1;
                int remainingSafeSteps = holeBoundaryTick - director.Sim.Tick;
                Assert.That(remainingSafeSteps, Is.GreaterThanOrEqualTo(0));
                for (int i = 0; i < remainingSafeSteps; i++)
                {
                    int priorTick = director.Sim.Tick;
                    InvokeFixed(director);
                    Assert.That(director.NetworkPaused, Is.False, director.FailureReason);
                    Assert.That(director.WaitingForRemoteInput, Is.False, "Known interior holes cannot be relabelled as ungenerated suffixes.");
                    Assert.That(director.Sim.Tick, Is.EqualTo(priorTick + 1));
                }
                Assert.That(director.Sim.Tick, Is.EqualTo(234));
                int before = director.Sim.Checksum();
                Assert.That(DashSceneSocketPair.Socket(pair.Peer).Send(DashNetcode.EncodeLifecycle(new DashLifecycleMessage
                {
                    Kind = NetLifecycleKind.Heartbeat, MatchId = pair.Peer.MatchId,
                    Epoch = pair.Peer.Epoch, Seat = (byte)pair.Peer.LocalSeat,
                    Seed = pair.Peer.Seed, ContentHash = pair.Peer.Rules.ContentHash
                })), Is.True);
                AssertQueueReady(DashSceneSocketPair.Socket(director.LanSession));
                InvokeFixed(director);
                Assert.That(director.LanSession.Rollback.Status, Is.EqualTo(RollbackStatus.InputHole));
                Assert.That(director.Sim.Tick, Is.EqualTo(234));
                Assert.That(director.Sim.Checksum(), Is.EqualTo(before));
                Assert.That(director.NetworkPaused, Is.True);
                Assert.That(director.Hud.NetworkIssueReason, Is.EqualTo("INPUT HOLE"));
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                Assert.That(DashRollbackSession.BufferTicks, Is.EqualTo(12));
            }
        }

        static void AssertQueueReady(LanUdpPeer receiver)
        {
            var socket = (UdpClient)typeof(LanUdpPeer).GetField("_udp", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(receiver);
            Assert.That(SpinWait.SpinUntil(() => socket.Available > 0, 200), Is.True,
                "Fixture must place real v7 datagrams in the socket before FixedUpdate.");
        }

        static void InvokeFixed(DashMatchDirector director)
        {
            bool previous = director.AutomaticTicking;
            director.AutomaticTicking = true;
            try { typeof(DashMatchDirector).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(director, null); }
            finally { director.AutomaticTicking = previous; }
        }
    }

    // Shared formal-scene v7 transport fixture. No state mutation and no natural-match
    // completion claim: one authored director and one real loopback peer are manually driven.
    internal sealed class DashSceneSocketPair : IDisposable
    {
        readonly DashMatchDirector _director;
        public DashLanSession Peer { get; }
        public DashSceneSocketPair(DashMatchDirector director)
        {
            _director = director; director.AutomaticTicking = false;
            director.InputOverride = (seat, tick, state) => default;
            if (director.Kind == PlayKind.HostLan)
                Peer = DashLanSession.Join(director.Rules,
                    new IPEndPoint(IPAddress.Loopback, director.LanSession.LocalEndpoint.Port), () => Time.realtimeSinceStartup, 617u);
            else Peer = DashLanSession.Host(director.Rules, 6117, 37u, () => Time.realtimeSinceStartup, ArenaLayout.LanPort);
        }
        public void DriveTo(int target)
        {
            for (int guard = 0; (_director.Sim.Tick < target || Peer.State == null || Peer.State.Tick < target) && guard < target * 8 + 1800; guard++)
            {
                if (_director.Sim.Tick < target) _director.AdvanceOneTick();
                if (Peer.State == null || Peer.State.Tick < target) Peer.Advance((epoch, tick) => default);
                _director.PumpFrame(); Peer.Pump(); Thread.Sleep(1);
                Assert.That(_director.NetworkPaused, Is.False, _director.FailureReason);
                Assert.That(Peer.Frozen, Is.False, Peer.FailureReason);
            }
            Assert.That(_director.Sim.Tick, Is.EqualTo(target)); Assert.That(Peer.State?.Tick, Is.EqualTo(target));
            for (int i = 0; i < 4; i++) { _director.PumpFrame(); Peer.Pump(); Thread.Sleep(1); }
        }
        public static LanUdpPeer Socket(DashLanSession session) => (LanUdpPeer)typeof(DashLanSession)
            .GetField("_peer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
        public void Dispose() { Peer.Dispose(); }
    }
}
