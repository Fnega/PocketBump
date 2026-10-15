using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PocketBump.Tests
{
    public sealed class DashNetworkReportTests
    {
        readonly List<GameObject> _objects = new List<GameObject>();
        string _directory;
        float _fixedDelta;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "PocketBump-Report-Test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _fixedDelta = Time.fixedDeltaTime;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject item in _objects) if (item != null) Object.DestroyImmediate(item);
            _objects.Clear();
            Time.fixedDeltaTime = _fixedDelta;
            // Only this test's independently-created temporary directory is removed.
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public void SnapshotSerializesAvailableIdentityAndKeepsAuthorityUnchanged()
        {
            float now = 0f;
            DashMatchDirector director = Create(out _);
            director.InitializeLan(DashLanSession.Host(ArenaRulesProfile.CreateProductionV1(), 1751, 77u, () => now));
            director.PumpFrame();
            int checksum = director.Sim.Checksum(), sends = director.LanSession.SendAttempts;
            DashNetworkReport report = DashNetworkReport.Capture(director, "snapshot");
            var roundTrip = JsonUtility.FromJson<DashNetworkReport>(JsonUtility.ToJson(report));
            Assert.That(roundTrip.schema, Is.EqualTo("pocketbump_lan_release_v1"));
            Assert.That(roundTrip.evidence_scope, Is.EqualTo("single_client_observation"));
            Assert.That(roundTrip.application_version, Is.EqualTo(Application.version));
            Assert.That(roundTrip.build_guid, Is.EqualTo(Application.buildGUID));
            Assert.That(roundTrip.development_build, Is.EqualTo(Debug.isDebugBuild));
            Assert.That(roundTrip.protocol_version, Is.EqualTo(DashNetcode.Version));
            Assert.That(roundTrip.rules_hash, Is.EqualTo(director.Rules.ContentHash));
            Assert.That(roundTrip.layout_hash, Is.EqualTo(director.Rules.LayoutHash));
            Assert.That(roundTrip.role, Is.EqualTo("host"));
            Assert.That(roundTrip.local_endpoint, Is.EqualTo(director.LanSession.LocalEndpoint.ToString()));
            Assert.That(roundTrip.checksum_tick, Is.EqualTo(-1), "Unconfirmed baseline is not confirmed match evidence.");
            Assert.That(director.Sim.Checksum(), Is.EqualTo(checksum));
            Assert.That(director.LanSession.SendAttempts, Is.EqualTo(sends));
        }

        [Test]
        public void FailedPortBindingCapturesSocketErrorBeforeAnyEstablishedState()
        {
            using (var occupied = new LanUdpPeer())
            {
                Assert.That(occupied.BindEphemeral(), Is.True);
                DashMatchDirector director = Create(out DashNetworkReporter observer);
                director.InitializeLan(DashLanSession.Host(ArenaRulesProfile.CreateProductionV1(), 1752, 78u,
                    () => 0f, occupied.LocalEndPoint.Port));
                int checksum = director.Sim.Checksum();
                observer.Observe(0f);
                DashNetworkReport report = Read(observer.LastReportPath);
                Assert.That(report.event_name, Is.EqualTo("failure"));
                Assert.That(report.failure_reason, Is.EqualTo("COULD NOT BIND PORT"));
                Assert.That(report.initialization_error, Does.Contain("BIND:"));
                Assert.That(report.socket_error, Does.Contain("BIND:"));
                Assert.That(report.socket_bound, Is.False);
                Assert.That(report.ready, Is.False);
                Assert.That(report.simulation_tick, Is.Zero);
                Assert.That(director.NetworkPaused, Is.True);
                Assert.That(director.Sim.Checksum(), Is.EqualTo(checksum));
                Assert.That(occupied.Bound, Is.True);
            }
        }

        [Test]
        public void ObserverBoundsUnchangedWritesAndCapturesFailureImmediately()
        {
            float now = 0f;
            DashMatchDirector director = Create(out DashNetworkReporter observer);
            director.InitializeLan(DashLanSession.Host(ArenaRulesProfile.CreateProductionV1(), 1753, 79u, () => now));
            observer.Observe(0f);
            Assert.That(Read(observer.LastReportPath).event_name, Is.EqualTo("session_started"));
            for (int i = 0; i < 200; i++) observer.Observe(i * .01f);
            Assert.That(observer.SuccessfulWrites, Is.EqualTo(1));
            observer.Observe(DashNetworkReporter.SnapshotIntervalSeconds);
            Assert.That(observer.SuccessfulWrites, Is.EqualTo(2));
            Assert.That(Read(observer.LastReportPath).event_name, Is.EqualTo("periodic"));
            now = 16f; director.PumpFrame();
            observer.Observe(DashNetworkReporter.SnapshotIntervalSeconds + .1f);
            Assert.That(observer.SuccessfulWrites, Is.EqualTo(3));
            Assert.That(Read(observer.LastReportPath).failure_reason, Is.EqualTo("LAN HANDSHAKE TIMEOUT"));
            for (int i = 0; i < 200; i++) observer.Observe(5.2f);
            Assert.That(observer.SuccessfulWrites, Is.EqualTo(3));
        }

        [Test]
        public void NormalLeaveIsDurableAndSubsequentObserverFramesDoNotOverwriteIt()
        {
            DashMatchDirector director = Create(out DashNetworkReporter observer);
            director.InitializeLan(DashLanSession.Host(ArenaRulesProfile.CreateProductionV1(), 1754, 80u, () => 0f));
            observer.Observe(0f);
            Assert.That(observer.CaptureNow("leave"), Is.True);
            director.LeaveNetwork();
            observer.Observe(100f);
            DashNetworkReport report = Read(observer.LastReportPath);
            Assert.That(report.event_name, Is.EqualTo("leave"));
            Assert.That(report.failure_reason, Is.Empty);
            Assert.That(report.socket_bound, Is.True, "Before-leave capture keeps the bound endpoint and final live counters.");
            Assert.That(director.LanSession.Left, Is.True);
        }

        [Test]
        public void FirstFailureFileSurvivesLaterPeriodicAndLeaveSnapshots()
        {
            var store = new DashNetworkReportStore(_directory);
            Assert.That(store.TryWrite(new DashNetworkReport { event_name = "failure", failure_reason = "DESYNC", desync_tick = 41 }), Is.True);
            string failureBytes = File.ReadAllText(store.FailurePath);
            Assert.That(store.TryWrite(new DashNetworkReport { event_name = "leave", failure_reason = "DESYNC", desync_tick = 99 }), Is.True);
            Assert.That(File.ReadAllText(store.FailurePath), Is.EqualTo(failureBytes));
            Assert.That(Read(store.LatestPath).event_name, Is.EqualTo("leave"));
            Assert.That(Read(store.FailurePath).desync_tick, Is.EqualTo(41));
            Assert.That(Directory.GetFiles(_directory, "*.pending"), Is.Empty);
            Assert.That(Read(store.LatestPath).report_id, Is.EqualTo(Read(store.FailurePath).report_id));
        }

        [Test]
        public void IndependentProcessesHaveCollisionFreeReportNamesAndUnicodeRoundTrips()
        {
            var first = new DashNetworkReportStore(_directory);
            var second = new DashNetworkReportStore(_directory);
            const string message = "连接失败：\"测试\"\n下一行";
            Assert.That(first.TryWrite(new DashNetworkReport { initialization_error = message }), Is.True);
            Assert.That(second.TryWrite(new DashNetworkReport { role = "join" }), Is.True);
            Assert.That(first.LatestPath, Is.Not.EqualTo(second.LatestPath));
            Assert.That(Read(first.LatestPath).initialization_error, Is.EqualTo(message));
            Assert.That(Read(second.LatestPath).role, Is.EqualTo("join"));
        }

        [Test]
        public void UnavailableStorageReportsErrorWithoutChangingNetworkOrThrowing()
        {
            string fileAsDirectory = Path.Combine(_directory, "not-a-directory");
            File.WriteAllText(fileAsDirectory, "preserve");
            DashMatchDirector director = Create(out DashNetworkReporter observer, fileAsDirectory);
            director.InitializeLan(DashLanSession.Host(ArenaRulesProfile.CreateProductionV1(), 1755, 81u, () => 0f));
            int checksum = director.Sim.Checksum();
            Assert.DoesNotThrow(() => observer.Observe(0f));
            Assert.That(observer.LastWriteError, Is.Not.Empty);
            Assert.That(observer.SuccessfulWrites, Is.Zero);
            Assert.That(director.NetworkPaused, Is.False);
            Assert.That(director.LanSession.SocketBound, Is.True);
            Assert.That(director.Sim.Checksum(), Is.EqualTo(checksum));
            Assert.That(File.ReadAllText(fileAsDirectory), Is.EqualTo("preserve"));
        }

        [Test]
        public void LockedOldGenerationKeepsLatestCompleteSnapshotAndBoundsFailedPruning()
        {
            var store = new DashNetworkReportStore(_directory);
            Assert.That(store.TryWrite(new DashNetworkReport { simulation_tick = 5 }), Is.True);
            string first = store.LatestPath;
            Assert.That(store.TryWrite(new DashNetworkReport { simulation_tick = 6 }), Is.True);
            using (new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.That(store.TryWrite(new DashNetworkReport { simulation_tick = 7 }), Is.False,
                    "Publication succeeds but retained-generation pruning reports its error.");
                Assert.That(Read(store.LatestPath).simulation_tick, Is.EqualTo(7));
                Assert.That(store.SuccessfulWrites, Is.EqualTo(3));
                for (int i = 0; i < 10; i++)
                    Assert.That(store.TryWrite(new DashNetworkReport { simulation_tick = 8 }), Is.False);
                Assert.That(store.SuccessfulWrites, Is.EqualTo(3));
                Assert.That(Directory.GetFiles(_directory, "*-session.json").Length, Is.EqualTo(3),
                    "An inaccessible old file cannot cause unbounded new generations.");
                Assert.That(store.TryWrite(new DashNetworkReport
                    { simulation_tick = 8, failure_reason = "DESYNC", desync_tick = 41 }), Is.False);
                Assert.That(Read(store.FailurePath).failure_reason, Is.EqualTo("DESYNC"),
                    "An unrelated locked session generation cannot block first-failure evidence.");
                Assert.That(Read(store.FailurePath).desync_tick, Is.EqualTo(41));
                Assert.That(Read(store.LatestPath).simulation_tick, Is.EqualTo(7));
                Assert.That(Directory.GetFiles(_directory, "*-session.json").Length, Is.EqualTo(3));
            }
            Assert.That(store.LastError, Is.Not.Empty);
            Assert.That(store.TryWrite(new DashNetworkReport { simulation_tick = 9 }), Is.True);
            Assert.That(Read(store.LatestPath).simulation_tick, Is.EqualTo(9));
            Assert.That(Read(store.LatestPath).write_sequence, Is.EqualTo(4));
            Assert.That(Directory.GetFiles(_directory, "*-session.json").Length, Is.EqualTo(2));
            Assert.That(store.LastError, Is.Empty);
        }

        [Test]
        public void FailedNewPublicationPreservesPreviousGenerationAndDoesNotPruneUnknownFiles()
        {
            var store = new DashNetworkReportStore(_directory);
            Assert.That(store.TryWrite(new DashNetworkReport { simulation_tick = 5 }), Is.True);
            string first = store.LatestPath;
            string next = first.Replace("-0000000001-session.json", "-0000000002-session.json");
            Directory.CreateDirectory(next + ".pending"); // Only this test fixture blocks the next publication.
            string unknown = Path.Combine(_directory, "another-client-session.json");
            File.WriteAllText(unknown, "keep this unrelated report");
            Assert.That(store.TryWrite(new DashNetworkReport { simulation_tick = 6 }), Is.False);
            Assert.That(store.LatestPath, Is.EqualTo(first));
            Assert.That(Read(first).simulation_tick, Is.EqualTo(5));
            Assert.That(store.SuccessfulWrites, Is.EqualTo(1));
            Directory.Delete(next + ".pending");
            for (int i = 6; i < 12; i++) Assert.That(store.TryWrite(new DashNetworkReport { simulation_tick = i }), Is.True);
            Assert.That(File.ReadAllText(unknown), Is.EqualTo("keep this unrelated report"));
            Assert.That(Directory.GetFiles(_directory, "*-session.json").Length, Is.EqualTo(3));
            Assert.That(Read(store.LatestPath).simulation_tick, Is.EqualTo(11));
        }

        [Test]
        public void LocalModeDoesNotProduceMisleadingLanEvidence()
        {
            DashMatchDirector director = Create(out DashNetworkReporter observer);
            director.InitializeLocal(ArenaRulesProfile.CreateProductionV1());
            observer.Observe(0f);
            Assert.That(observer.CaptureNow("leave"), Is.False);
            Assert.That(Directory.GetFiles(_directory), Is.Empty);
        }

        [Test]
        public void LiveSocketSnapshotContainsBothEndpointsAndConfirmedProgress()
        {
            float now = 0f;
            DashMatchDirector host = Create(out _), join = Create(out _);
            host.InitializeLan(DashLanSession.Host(ArenaRulesProfile.CreateProductionV1(), 1756, 82u, () => now));
            join.InitializeLan(DashLanSession.Join(host.Rules, new IPEndPoint(IPAddress.Loopback, host.LanSession.LocalEndpoint.Port), () => now, 1757));
            host.InputOverride = (seat, tick, state) => default;
            join.InputOverride = (seat, tick, state) => default;
            for (int guard = 0; guard < 600 && (host.Sim.Tick < 240 || join.Sim.Tick < 240); guard++)
            {
                if (host.Sim.Tick < 240) host.AdvanceOneTick();
                if (join.Sim.Tick < 240) join.AdvanceOneTick();
                host.PumpFrame(); join.PumpFrame(); now += 1f / 60f;
                Thread.Sleep(1);
                Assert.That(host.NetworkPaused || join.NetworkPaused, Is.False);
            }
            DashNetworkReport report = DashNetworkReport.Capture(host, "periodic");
            Assert.That(report.simulation_tick, Is.EqualTo(240));
            Assert.That(report.confirmed_state_tick, Is.GreaterThan(230));
            Assert.That(report.checksum_tick, Is.GreaterThan(230));
            Assert.That(report.validated_checksums, Is.GreaterThan(0));
            Assert.That(report.send_attempts, Is.GreaterThan(0));
            Assert.That(report.sent_datagrams, Is.GreaterThan(0));
            Assert.That(report.received_datagrams, Is.GreaterThan(0));
            Assert.That(report.accepted_input_datagrams, Is.GreaterThan(0));
            Assert.That(report.send_failures, Is.Zero);
            Assert.That(report.local_endpoint, Is.EqualTo(host.LanSession.LocalEndpoint.ToString()));
            Assert.That(report.remote_endpoint, Is.EqualTo(host.LanSession.RemoteEndpoint.ToString()));
            Assert.That(report.match_id, Is.EqualTo(join.MatchId));
            Assert.That(report.retained_results, Is.Empty, "Transport progress alone is not a full match.");
            Assert.That(host.Sim.Checksum(), Is.EqualTo(join.Sim.Checksum()));
        }

        [Test]
        public void ReporterFreezesPreFailureTimelineEvenAfterLongFaultScreenAndLeave()
        {
            float now = 0f;
            DashMatchDirector director = Create(out DashNetworkReporter observer);
            director.InitializeLan(DashLanSession.Host(ArenaRulesProfile.CreateProductionV1(), 1851, 92u, () => now));
            for (int i = 0; i <= 60; i++)
            {
                now = i * .25f; director.PumpFrame(); observer.Observe(now);
            }
            DashNetworkReport failure = Read(observer.LastReportPath);
            Assert.That(failure.failure_reason, Is.EqualTo("LAN HANDSHAKE TIMEOUT"));
            Assert.That(failure.timeline_frozen, Is.True);
            Assert.That(failure.timeline.Length, Is.GreaterThan(10));
            Assert.That(failure.timeline[0].failure_reason, Is.Empty);
            Assert.That(failure.timeline[failure.timeline.Length - 1].failure_reason, Is.EqualTo(failure.failure_reason));
            Assert.That(failure.receive_polls, Is.GreaterThan(0));
            Assert.That(failure.receive_failures, Is.Zero);
            Assert.That(failure.pump_count, Is.GreaterThan(0));
            Assert.That(failure.raw_receive_age_seconds, Is.EqualTo(-1f));
            string frozen = JsonUtility.ToJson(new DashNetworkReport { timeline = failure.timeline });
            now = 200f; director.PumpFrame(); observer.Observe(now);
            observer.CaptureNow("leave");
            DashNetworkReport later = Read(observer.LastReportPath);
            Assert.That(JsonUtility.ToJson(new DashNetworkReport { timeline = later.timeline }), Is.EqualTo(frozen));
            Assert.That(later.timeline_frozen, Is.True);
        }

        [Test]
        public void FrozenTimelineSurvivesUnavailableStorageAndIsWrittenWhenStorageRecovers()
        {
            string blocked = Path.Combine(_directory, "temporarily-blocked");
            File.WriteAllText(blocked, "owned test fixture");
            float now = 0f;
            DashMatchDirector director = Create(out DashNetworkReporter observer, blocked);
            director.InitializeLan(DashLanSession.Host(ArenaRulesProfile.CreateProductionV1(), 1852, 93u, () => now));
            for (int i = 0; i <= 60; i++)
            {
                now = i * .25f; director.PumpFrame();
                Assert.DoesNotThrow(() => observer.Observe(now));
            }
            Assert.That(observer.SuccessfulWrites, Is.Zero);
            File.Delete(blocked); // Only this test's explicitly created blocking file.
            now = 25f; observer.Observe(now);
            DashNetworkReport recovered = Read(observer.LastReportPath);
            Assert.That(observer.LastWriteError, Is.Empty);
            Assert.That(recovered.timeline_frozen, Is.True);
            Assert.That(recovered.timeline[0].failure_reason, Is.Empty);
            Assert.That(recovered.timeline[recovered.timeline.Length - 1].elapsed_seconds, Is.EqualTo(15f));
            Assert.That(Directory.GetFiles(blocked, "*-failure.json"), Has.Length.EqualTo(1));
        }

        [Test]
        public void InitializationFailureBeforeSocketCreationIsLocalAndFreezesAvailableEvidence()
        {
            DashMatchDirector director = Create(out DashNetworkReporter observer);
            typeof(DashMatchDirector).GetMethod("FreezeInitialization",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(director, new object[] { ArenaRulesProfile.CreateProductionV1(), PlayKind.JoinLan, "BAD ADDRESS", "invalid endpoint" });
            Assert.That(director.LanSession, Is.Null);
            observer.Observe(0f);
            DashNetworkReport report = Read(observer.LastReportPath);
            Assert.That(report.failure_origin, Is.EqualTo("local"));
            Assert.That(report.failure_reason, Is.EqualTo("BAD ADDRESS"));
            Assert.That(report.timeline_frozen, Is.True);
            Assert.That(report.timeline, Has.Length.EqualTo(1));
            Assert.That(report.timeline[0].failure_origin, Is.EqualTo("local"));
            Assert.That(report.timeline[0].failure_reason, Is.EqualTo("BAD ADDRESS"));
            Assert.That(report.receive_polls, Is.Zero);
            Assert.That(report.raw_receive_age_seconds, Is.EqualTo(-1f));
        }

        DashMatchDirector Create(out DashNetworkReporter reporter, string directory = null)
        {
            var root = new GameObject("Isolated production report test"); _objects.Add(root);
            var director = root.AddComponent<DashMatchDirector>(); director.AutomaticTicking = false;
            reporter = root.AddComponent<DashNetworkReporter>();
            reporter.Bind(director, directory ?? _directory);
            return director;
        }

        static DashNetworkReport Read(string path) => JsonUtility.FromJson<DashNetworkReport>(File.ReadAllText(path));
    }
}
