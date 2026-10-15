#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PocketBump
{
    // Development-only scene-level acceptance. Drives the actual production director,
    // input transport, confirmed ledger, result buttons and menu recovery path.
    public sealed class DashLanSmokeRunner : MonoBehaviour
    {
        const int MinimumLiveTicks = 120;
        const int MinimumChecksums = 120;
        const float MatchDeadlineSeconds = 180f;
        const float PacingStallSeconds = .75f;
        static readonly Vector2[] SmokeDirections = { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
        readonly List<DashLanEpochSummary> _epochs = new List<DashLanEpochSummary>();
        readonly DashLanFullMatchDriver _driver = new DashLanFullMatchDriver();
        DashLanMechanicScenario _mechanic;
        string _role, _resultPath, _readyPath, _coordinationPath;
        string _scenario = "smoke", _faultProfile = "none", _leaveRole = "host", _stage = "starting";
        string _expectedDeparture = "", _observedDeparture = "";
        DashMatchDirector _director;
        DashLanEpochSummary _current;
        bool _failed, _terminalWritten, _expectingDeparture, _peerSilenceActivated, _liveOnly, _readyWritten;
        bool _expectMismatch, _dropFirstRequested;
        int _faultsBeforePeerSilence, _successfulRematches, _initialMatchId, _initialSeat = -1;
        int _targetTick = 900, _liveTicks, _lastObservedTick = -1, _checksumsAtFirstLive, _checksumsSinceLive;
        bool _hasLiveOrigin;
        readonly Vector2[] _liveOrigins = new Vector2[2];
        readonly float[] _maxDisplacement = new float[2];
        float _scenarioTimeout = 600f, _scenarioDeadline, _stageDeadline;
        float _holdStart = -1f;
        int _retiredDirectorErrors, _retiredSessionErrors;
        string _firstSocketEndpoint = "";
        string _pacingStallRole = "host";
        bool _pacingStallActive, _pacingObserving, _pacingWaitObserved, _pacingResumeObserved, _pacingCheckpointObserved;
        int _pacingConnection, _pacingEpoch, _pacingStallCount, _pacingSkippedFixedSteps, _pacingPumpCalls;
        int _pacingStallStartTick = -1, _pacingStallEndTick = -1, _pacingSharedStallTick = -1;
        int _pacingStallLatestLocalInputTick = -1;
        int _pacingWaitBaseline, _pacingWaitCountAtObservation, _pacingWaitTick = -1, _pacingResumeTick = -1;
        int _pacingWaitContiguous = -1, _pacingResumeContiguous = -1;
        int _pacingRecoveryConfirmed = -1, _pacingRecoveryChecksums, _pacingCheckpointTick = -1, _pacingCheckpointChecksum;
        float _pacingStallStartedAt, _pacingActualStallSeconds, _pacingObservedWaitSeconds;
        string _silenceRole = "host", _faultFirstPath = "", _faultRetainedPath = "", _faultTimelineHash = "";
        bool _directionalArmed, _directionalActivated, _faultTimelineRetained;
        int _directionalFaultBaseline, _faultTimelineSamples;
        float _faultHoldSeconds;
        DashNetworkReport _faultFirstReport;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartFromCommandLine()
        {
            string role = ArgumentValue("--lan-smoke=");
            if (role != "host" && role != "join") return;
            var go = new GameObject("Dash LAN Diagnostics");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<DashLanSmokeRunner>();
            runner._role = role;
            runner._resultPath = ArgumentValue("--lan-smoke-result=");
            runner._readyPath = ArgumentValue("--lan-smoke-ready=");
            runner._coordinationPath = ArgumentValue("--lan-coordination=");
            runner._scenario = ValueOr("--lan-scenario=", "smoke");
            runner._faultProfile = ValueOr("--lan-fault-profile=", "none");
            runner._leaveRole = ValueOr("--lan-leave-role=", "host");
            runner._pacingStallRole = ValueOr("--lan-pacing-stall-role=", "host");
            runner._silenceRole = ValueOr("--lan-silence-role=", "host");
            runner._expectMismatch = HasArgument("--lan-smoke-expect-mismatch");
            runner._dropFirstRequested = HasArgument("--lan-smoke-drop-first");
            if (float.TryParse(ArgumentValue("--lan-scenario-timeout="), NumberStyles.Float, CultureInfo.InvariantCulture, out float timeout))
                runner._scenarioTimeout = Mathf.Clamp(timeout, 30f, 3600f);
            if (int.TryParse(ArgumentValue("--lan-smoke-ticks="), out int target))
                runner._targetTick = Mathf.Clamp(target, 180 + MinimumLiveTicks, 36000);
        }

        IEnumerator Start()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            yield return null;
            if (_dropFirstRequested && _faultProfile != "none")
            { Fail("Do not combine --lan-smoke-drop-first with --lan-fault-profile; each fault needs independent evidence."); yield break; }
            if (DashLanDiagnosticReport.RequiredFaultSender(_faultProfile) == "unknown")
            { Fail("Unknown diagnostic fault profile: " + _faultProfile); yield break; }
            if (_scenario != "smoke" && _scenario != "lifecycle" && _scenario != "full-match" && _scenario != "mechanics" && _scenario != "pacing" && _scenario != "fault-evidence")
            { Fail("Unknown diagnostic scenario: " + _scenario); yield break; }
            if (_leaveRole != "host" && _leaveRole != "join")
            { Fail("Unknown leave role: " + _leaveRole); yield break; }
            if ((_scenario == "lifecycle" || _scenario == "full-match" || _scenario == "pacing" || _scenario == "fault-evidence") && (string.IsNullOrEmpty(_coordinationPath) || !Directory.Exists(_coordinationPath)))
            { Fail("Lifecycle/full-match requires an existing shared coordination directory."); yield break; }
            if (_scenario == "pacing" && (_faultProfile != "none" || _dropFirstRequested || _expectMismatch ||
                (_pacingStallRole != "host" && _pacingStallRole != "join")))
            { Fail("Pacing is an independent healthy scenario with one explicit host/join stall role."); yield break; }
            if (_scenario == "fault-evidence" && (_faultProfile != "none" || _dropFirstRequested || _expectMismatch ||
                (_silenceRole != "host" && _silenceRole != "join")))
            { Fail("Fault evidence requires one explicit silent host/join sender without other fault profiles."); yield break; }
            if (_scenario == "mechanics")
            {
                string mechanicName = ArgumentValue("--lan-mechanic=");
                if (!int.TryParse(ArgumentValue("--lan-mechanic-seat="), out int mechanicSeat) || mechanicSeat < 0 || mechanicSeat > 1 ||
                    Array.IndexOf(DashLanMechanicScenario.Names, mechanicName) < 0)
                { Fail("Mechanics requires an explicit supported --lan-mechanic and --lan-mechanic-seat=0|1."); yield break; }
                _mechanic = new DashLanMechanicScenario(mechanicName, mechanicSeat);
                DashMatchDirector.DiagnosticSessionFactory = CreateMechanicSession;
            }
            _scenarioDeadline = Time.realtimeSinceStartup + (_scenario == "smoke" ?
                Mathf.Max(60f, _targetTick / 60f * 3f + 15f) : _scenarioTimeout);
            SetStage("handshake", 30f);
            BeginConnection();
            while (!_failed && (_director == null || !_director.ReadyForNetplay)) yield return null;
            if (_failed) yield break;
            _initialMatchId = _director.MatchId; _initialSeat = _director.LocalSeat;
            _firstSocketEndpoint = _director.LanSession.LocalEndpoint?.ToString() ?? "";
            if (_scenario == "smoke") { yield return RunSmoke(); yield break; }
            if (_scenario == "mechanics") { yield return RunMechanics(); yield break; }
            if (_scenario == "pacing") { yield return RunPacing(); yield break; }
            if (_scenario == "fault-evidence") { yield return RunFaultEvidence(); yield break; }
            yield return RunLifecycle();
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachDirector();

        void AttachDirector()
        {
            DashMatchDirector found = FindObjectOfType<DashMatchDirector>();
            if (found == null || ReferenceEquals(found, _director)) return;
            if (!ReferenceEquals(_director, null))
            {
                _retiredDirectorErrors += _director.PresentationErrorCount;
                _retiredSessionErrors += _director.LanSession?.PresentationErrorCount ?? 0;
                _director.ConfirmedEvents -= OnConfirmed; _director.EpochInitialized -= OnEpoch;
                if (_director.LanSession != null) _director.LanSession.ConfirmedFrame -= OnConfirmedFrame;
            }
            _director = found;
            found.InputOverride = (seat, tick, state) => _mechanic != null ? _mechanic.Sample(seat, tick, state) :
                _scenario == "smoke" || _scenario == "pacing" || _liveOnly ? SmokeInput(seat, tick) : _driver.Sample(seat, tick, state);
            found.ConfirmedEvents += OnConfirmed; found.EpochInitialized += OnEpoch;
            if ((_mechanic != null || _scenario == "pacing") && found.LanSession != null) found.LanSession.ConfirmedFrame += OnConfirmedFrame;
            if (found.LanSession?.State != null) OnEpoch(found.MatchEpoch);
            if (!_readyWritten && found.Initialized && (found.LanSession?.SocketBound ?? false))
            {
                _readyWritten = true;
                if (!string.IsNullOrEmpty(_readyPath)) File.WriteAllText(_readyPath, _role + " ready\nprotocol_version=" + DashNetcode.Version + "\n");
            }
        }

        void OnEpoch(int epoch)
        {
            if (_current != null && _current.MatchId == _director.MatchId && _current.Epoch == epoch) return;
            DashArenaState state = _director.Sim;
            DashArenaState expected = DashArenaSimulation.Create(_director.Rules, state.InitialSeed, epoch);
            DashArenaSimulation.StartMatch(expected);
            _current = new DashLanEpochSummary
            {
                MatchId = _director.MatchId, Epoch = epoch, Seed = state.InitialSeed,
                RulesHash = state.Rules.ContentHash, LayoutHash = state.Rules.LayoutHash,
                InitializationSource = _mechanic == null ? (state.Checksum() == expected.Checksum() ? "normal_gameplay" : "unobserved_or_modified") :
                    (state.Tick == 0 && state.Checksum() == _mechanic.InitialChecksum ? "labeled_fixture" : "unobserved_or_modified"),
                Gameplay = new DashLanConfirmedGameplay(state.Rules, epoch),
                LocalEndpoint = _director.LanSession?.LocalEndpoint?.ToString() ?? ""
            };
            _epochs.Add(_current);
            _liveTicks = 0; _lastObservedTick = -1; _hasLiveOrigin = false; _checksumsSinceLive = 0;
            _maxDisplacement[0] = _maxDisplacement[1] = 0f;
        }

        void OnConfirmed(IReadOnlyList<DashArenaEvent> events) => _current?.Gameplay.Commit(events);
        void OnConfirmedFrame(DashConfirmedFrame frame)
        {
            _mechanic?.Commit(frame);
            // Frame.State is the state AFTER executing its tick: State.Tick - 1.
            if (_scenario == "pacing" && _pacingCheckpointTick >= 0 && frame.State.Tick - 1 == _pacingCheckpointTick &&
                frame.State.Epoch == _pacingEpoch && _director.MatchId == _pacingConnection)
            { _pacingCheckpointObserved = true; _pacingCheckpointChecksum = frame.State.Checksum(); }
        }

        DashLanSession CreateMechanicSession(PlayKind kind, string address, ArenaRulesProfile ignored)
        {
            if (kind == PlayKind.HostLan)
            {
                int matchId = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0);
                return DashLanSession.Host(_mechanic.Rules, matchId == 0 ? 1 : matchId, DashLanMechanicScenario.FixtureSeed,
                    () => Time.realtimeSinceStartup, ArenaLayout.LanPort, initialStateFactory: _mechanic.CreateInitial);
            }
            return DashLanSession.Join(_mechanic.Rules, new IPEndPoint(IPAddress.Parse(address), ArenaLayout.LanPort),
                () => Time.realtimeSinceStartup, initialStateFactory: _mechanic.CreateInitial);
        }

        IEnumerator RunMechanics()
        {
            SetStage("mechanics-" + _mechanic.Name + "-seat-" + _mechanic.Seat, 45f);
            while (!_failed && !(_mechanic.Passed && _director.ConfirmedStateTick >= 300 &&
                _director.LanSession.Rollback.ValidatedChecksumCount >= MinimumChecksums)) yield return null;
            if (_failed) yield break;
            if (_current.InitializationSource != "labeled_fixture")
            { Fail("Mechanic baseline was not retained as an explicit fixture."); yield break; }
            yield return Complete("Declared bounded mechanic fixture passed with corrected confirmed outcomes; not a natural-match claim.");
        }

        IEnumerator RunSmoke()
        {
            SetStage("smoke-live", Mathf.Max(45f, _targetTick / 60f * 3f));
            while (!_failed && !(_director.Sim.Tick >= _targetTick && HasLiveProgress())) yield return null;
            if (!_failed) yield return Complete("Handshake and confirmed movement/checksum smoke passed; no natural full-match claim.");
        }

        IEnumerator RunPacing()
        {
            SetStage("pacing-stable-live", 45f);
            while (!_failed && !HasLiveProgress()) yield return null;
            if (_failed) yield break;
            _pacingConnection = _director.MatchId; _pacingEpoch = _director.MatchEpoch;
            WriteMarker(_role + ".pacing-ready");
            while (!_failed && !BothMarkers("pacing-ready")) yield return null;
            if (_failed) yield break;
            string observer = _pacingStallRole == "host" ? "join" : "host";
            SetStage("pacing-stall-and-wait", 15f);
            if (_role == observer)
            {
                // Arm while not already waiting, before allowing the other process to stall.
                while (!_failed && _director.LanSession.WaitingForRemoteInput) yield return null;
                if (_failed) yield break;
                _pacingWaitBaseline = _director.LanSession.InputWaitCount;
                _pacingObserving = true;
                WriteMarker(observer + ".pacing-observing");
                while (!_failed && !_pacingResumeObserved) yield return null;
                if (_failed) yield break;
                WritePacingNumber("pacing-resumed", _pacingResumeTick);
            }
            else
            {
                while (!_failed && !HasMarker(observer + ".pacing-observing")) yield return null;
                if (_failed) yield break;
                _pacingStallStartTick = _director.Sim.Tick;
                _pacingSharedStallTick = _pacingStallStartTick;
                _pacingStallLatestLocalInputTick = _director.LanSession.LatestLocalInputTick;
                WritePacingNumber("pacing-last-local-input", _pacingStallLatestLocalInputTick);
                WritePacingNumber("pacing-start", _pacingStallStartTick);
                if (_failed) yield break;
                _pacingStallStartedAt = Time.realtimeSinceStartup;
                _pacingStallActive = true;
                _director.AutomaticTicking = false;
                while (!_failed && Time.realtimeSinceStartup - _pacingStallStartedAt < PacingStallSeconds) yield return null;
                _pacingActualStallSeconds = Time.realtimeSinceStartup - _pacingStallStartedAt;
                _pacingStallEndTick = _director.Sim.Tick;
                _pacingStallActive = false;
                _director.AutomaticTicking = true;
                if (_failed) yield break;
                if (_pacingStallCount != 1 || _pacingSkippedFixedSteps == 0 || _pacingPumpCalls == 0 ||
                    _pacingStallStartTick != _pacingStallEndTick || _pacingActualStallSeconds >= 2f)
                { Fail("Pacing stall was not a bounded real fixed-step freeze with continued Pump."); yield break; }
                WriteMarker("pacing-stall-ended");
                if (!HasMarker(observer + ".pacing-wait-observed"))
                { Fail("Stall ended without peer observation of its exact exhausted input prefix."); yield break; }
                while (!_failed && !HasMarker("pacing-resumed")) yield return null;
                if (_failed) yield break;
                if (!TryReadPacingNumber("pacing-resumed", out int otherResumeTick)) yield break;
                // Shared future checkpoint allows both reports to prove identical corrected state,
                // rather than comparing unrelated last-frame hashes or a mere request counter.
                WritePacingNumber("pacing-checkpoint", Math.Max(_director.Sim.Tick, otherResumeTick) + 180);
            }
            while (!_failed && !HasMarker("pacing-checkpoint")) yield return null;
            if (_failed || !TryReadPacingNumber("pacing-checkpoint", out _pacingCheckpointTick)) yield break;
            _pacingRecoveryConfirmed = _director.ConfirmedStateTick;
            _pacingRecoveryChecksums = _director.LanSession.Rollback.ValidatedChecksumCount;
            SetStage("pacing-confirmed-recovery", 30f);
            while (!_failed && !(_pacingCheckpointObserved &&
                _director.ConfirmedStateTick - _pacingRecoveryConfirmed >= MinimumLiveTicks &&
                _director.LanSession.Rollback.ValidatedChecksumCount - _pacingRecoveryChecksums >= MinimumChecksums &&
                !_director.LanSession.WaitingForRemoteInput && HasLiveProgress())) yield return null;
            if (!_failed) yield return Complete("Real bounded simulation stall, actual peer wait/resume and matching confirmed recovery checkpoint passed.");
        }

        IEnumerator RunFaultEvidence()
        {
            // Result hold isolates accepted-peer timeout from the separate live-input
            // progress deadline. The ordinary production driver must earn this result.
            SetStage("fault-natural-match", MatchDeadlineSeconds);
            while (!_failed && !_director.Lifecycle.ResultConfirmed) yield return null;
            if (_failed) yield break;
            CaptureFinish();
            if (!ValidNaturalFinish()) { Fail("Fault evidence requires an ordinary naturally completed match."); yield break; }
            SetStage("fault-result-ready", 20f);
            _directionalArmed = true;
            _directionalFaultBaseline = LanUdpPeer.InjectedFaults;
            WriteMarker(_role + ".fault-result-ready");
            while (!_failed && !BothMarkers("fault-result-ready")) yield return null;
            if (_failed) yield break;
            SetStage("fault-directional-result-hold", 12f);
            if (_role == _silenceRole)
            {
                LanUdpPeer.ActivateDirectionalSilence(); _directionalActivated = true;
                WriteMarker("fault-directional-activated");
            }
            while (!_failed && string.IsNullOrEmpty(_director.FailureReason)) yield return null;
            if (_failed) yield break;
            if (!ExpectedDirectionalFailure()) { Fail("Directional silence produced the wrong terminal cause."); yield break; }
            if (!RetainFaultSnapshot("fault-first", out _faultFirstPath, out _faultFirstReport)) yield break;
            _faultTimelineHash = TimelineHash(_faultFirstReport);
            _faultTimelineSamples = _faultFirstReport.timeline.Length;
            float started = Time.realtimeSinceStartup;
            int frozenTick = _director.Sim.Tick, frozenChecksum = _director.Sim.Checksum();
            SetStage("fault-frozen-timeline-hold", 30f);
            while (!_failed && Time.realtimeSinceStartup - started < DashNetworkTimeline.RetentionSeconds + 1f)
            {
                if (!ExpectedDirectionalFailure() || _director.Sim.Tick != frozenTick || _director.Sim.Checksum() != frozenChecksum)
                { Fail("Terminal cause or frozen result changed during evidence retention."); yield break; }
                yield return null;
            }
            if (_failed) yield break;
            _faultHoldSeconds = Time.realtimeSinceStartup - started;
            if (!RetainFaultSnapshot("fault-retained", out _faultRetainedPath, out DashNetworkReport retained)) yield break;
            _faultTimelineRetained = TimelineHash(retained) == _faultTimelineHash &&
                retained.failure_reason == _faultFirstReport.failure_reason && retained.failure_origin == _faultFirstReport.failure_origin &&
                retained.peer_failure_reason == _faultFirstReport.peer_failure_reason && retained.timeline_frozen;
            int injected = LanUdpPeer.InjectedFaults - _directionalFaultBaseline;
            if (!_faultTimelineRetained || (_role == _silenceRole ? injected <= 0 : injected != 0) || PresentationErrors() != 0)
            { Fail("Actual directional injection, immutable timeline or presentation verification failed."); yield break; }
            SetStage("fault-evidence-complete", 60f);
            if (_failed) yield break;
            _terminalWritten = true;
            WriteResult(true, "Natural confirmed result, one-way sender silence, local timeout/peer fault and >20s frozen timeline verified. Not a live-interruption or physical-campus-network claim.");
            yield return HoldAfterFinal(true);
        }

        bool ExpectedDirectionalFailure()
        {
            DashLanSession lan = _director?.LanSession;
            if (lan == null || !_directionalArmed) return false;
            return _role == _silenceRole
                ? lan.FailureReason == "PEER FAULT" && lan.FailureOrigin == "peer" && lan.PeerFailureReason == "PEER TIMEOUT"
                : lan.FailureReason == "PEER TIMEOUT" && lan.FailureOrigin == "local" && lan.PeerFailureReason == "";
        }

        bool RetainFaultSnapshot(string suffix, out string path, out DashNetworkReport report)
        {
            path = ""; report = null;
            try
            {
                DashNetworkReporter reporter = _director.GetComponent<DashNetworkReporter>();
                if (reporter == null || !reporter.CaptureNow("development_" + suffix))
                    throw new IOException("Production reporter could not retain diagnostic evidence: " + reporter?.LastWriteError);
                report = JsonUtility.FromJson<DashNetworkReport>(File.ReadAllText(reporter.LastReportPath));
                if (report == null || !report.development_build || report.protocol_version != DashNetcode.Version ||
                    report.match_id != _initialMatchId || report.epoch != 0 || report.role != _role ||
                    !report.timeline_frozen || report.evidence_version != 2 || report.timeline == null || report.timeline.Length < 5 ||
                    report.failure_reason != _director.FailureReason || report.failure_origin != _director.LanSession.FailureOrigin ||
                    report.peer_failure_reason != _director.LanSession.PeerFailureReason)
                    throw new InvalidDataException("Production fault snapshot lacks matching identity, terminal cause or frozen timeline.");
                DashNetworkTimelineSample first = report.timeline[0], last = report.timeline[report.timeline.Length - 1];
                if (!string.IsNullOrEmpty(first.failure_reason) || first.elapsed_seconds >= last.elapsed_seconds ||
                    last.failure_reason != report.failure_reason || last.failure_origin != report.failure_origin ||
                    last.peer_failure_reason != report.peer_failure_reason || last.receive_polls <= 0 ||
                    (_role != _silenceRole && last.accepted_peer_age_seconds < DashLanLifecycle.DisconnectSeconds))
                    throw new InvalidDataException("Timeline lacks healthy pre-failure history and a matching terminal sample.");
                path = _resultPath + "." + suffix + ".json";
                // Export only content bytes. File.Copy may try to carry source EFS
                // metadata from AppData onto a destination volume that rejects it.
                string sourcePath = reporter.LastReportPath;
                string expectedHash = FileHashIfPresent(sourcePath);
                using (FileStream source = File.OpenRead(sourcePath))
                using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    source.CopyTo(destination);
                if (FileHashIfPresent(path) != expectedHash)
                    throw new IOException("Exported diagnostic report differs from the production snapshot bytes.");
                return true;
            }
            catch (Exception error) { Fail("Could not retain production fault evidence: " + error.Message); return false; }
        }

        [Serializable] sealed class TimelineEvidence { public DashNetworkTimelineSample[] samples; }
        static string TimelineHash(DashNetworkReport report)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new TimelineEvidence { samples = report.timeline }));
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        void FixedUpdate()
        {
            // AutomaticTicking is disabled only on this diagnostic peer. These are real
            // scheduled fixed callbacks whose simulation steps are deliberately omitted.
            if (!_pacingStallActive || _failed || _director == null) return;
            if (_pacingSkippedFixedSteps++ == 0) _pacingStallCount++;
            if (_director.AutomaticTicking || _director.Sim.Tick != _pacingStallStartTick ||
                _director.LanSession.LatestLocalInputTick != _pacingStallLatestLocalInputTick)
                Fail("Simulation advanced during the declared pacing stall.");
        }

        void ObservePacing()
        {
            if (_scenario != "pacing" || _director == null) return;
            if (_pacingConnection != 0 && (_director.MatchId != _pacingConnection || _director.MatchEpoch != _pacingEpoch))
            { Fail("Pacing observation crossed its connection/epoch scope."); return; }
            if (_pacingStallActive)
            {
                // This replaces only the Director's automatic Update pump, not Advance.
                _director.PumpFrame(); _pacingPumpCalls++;
            }
            if (!_pacingObserving || _pacingResumeObserved) return;
            if (_pacingSharedStallTick < 0)
            {
                if (!HasMarker("pacing-start")) return;
                if (!TryReadPacingNumber("pacing-start", out _pacingSharedStallTick)) return;
                if (!TryReadPacingNumber("pacing-last-local-input", out _pacingStallLatestLocalInputTick)) return;
            }
            DashLanSession lan = _director.LanSession;
            bool ended = HasMarker("pacing-stall-ended");
            if (!_pacingWaitObserved && ended)
            { Fail("No causal peer input-wait was observed before the real stall ended."); return; }
            if (lan.InputWaitCount > _pacingWaitBaseline && DashLanDiagnosticReport.IsActiveStallWait(
                _pacingSharedStallTick >= 0, ended, lan.WaitingForRemoteInput, _pacingStallLatestLocalInputTick,
                _director.Sim.Tick, lan.RemoteContiguousInputTick, lan.LatestRemoteInputTick))
            {
                if (!_pacingWaitObserved)
                {
                    _pacingWaitObserved = true; _pacingWaitTick = _director.Sim.Tick;
                    _pacingWaitContiguous = lan.RemoteContiguousInputTick;
                    _pacingWaitCountAtObservation = lan.InputWaitCount;
                    WriteMarker(_role + ".pacing-wait-observed");
                }
                _pacingObservedWaitSeconds = Math.Max(_pacingObservedWaitSeconds, lan.InputWaitCurrentSeconds);
            }
            if (_pacingWaitObserved && !_pacingResumeObserved && !lan.WaitingForRemoteInput && HasMarker("pacing-stall-ended") &&
                lan.RemoteContiguousInputTick > _pacingWaitContiguous && _director.Sim.Tick > _pacingWaitTick)
            {
                _pacingResumeObserved = true; _pacingResumeTick = _director.Sim.Tick;
                _pacingResumeContiguous = lan.RemoteContiguousInputTick;
            }
        }

        void WritePacingNumber(string name, int value)
        {
            try
            {
                string destination = Path.Combine(_coordinationPath, name), pending = destination + ".pending";
                File.WriteAllText(pending, value.ToString(CultureInfo.InvariantCulture));
                File.Move(pending, destination);
            }
            catch (Exception error) { Fail("Could not write pacing coordination: " + error.Message); }
        }

        bool TryReadPacingNumber(string name, out int value)
        {
            value = -1;
            try
            {
                if (int.TryParse(File.ReadAllText(Path.Combine(_coordinationPath, name)), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out value) && value >= 0) return true;
                Fail("Invalid pacing coordination tick: " + name);
            }
            catch (Exception error) { Fail("Could not read pacing coordination: " + error.Message); }
            return false;
        }

        IEnumerator RunLifecycle()
        {
            int requiredRematches = _scenario == "lifecycle" ? 3 : 1;
            for (int epoch = 0; epoch <= requiredRematches && !_failed; epoch++)
            {
                SetStage(epoch == requiredRematches ? "rematch-live-" + epoch : "gameplay-epoch-" + epoch, MatchDeadlineSeconds);
                while (!_failed && _director.MatchEpoch < epoch) yield return null;
                if (_failed) yield break;
                if (_director.MatchEpoch != epoch || _director.MatchId != _initialMatchId || _director.LocalSeat != _initialSeat ||
                    _director.LanSession.LocalEndpoint?.ToString() != _firstSocketEndpoint || _current?.InitializationSource != "normal_gameplay")
                { Fail("Rematch changed connection, socket, seat, epoch ordering or ordinary production baseline."); yield break; }
                if (epoch == requiredRematches)
                {
                    while (!_failed && !HasLiveProgress()) yield return null;
                    if (_failed) yield break;
                    _successfulRematches = epoch;
                    break;
                }
                while (!_failed && !_director.Lifecycle.ResultConfirmed) yield return null;
                if (_failed) yield break;
                CaptureFinish();
                if (!ValidNaturalFinish())
                { Fail("Confirmed natural finish lacks per-seat actual Dash, ball impact/goal, legal score or winner evidence."); yield break; }
                SetStage("confirmed-result-sync-epoch-" + epoch, 20f);
                WriteMarker(_role + ".confirmed." + epoch);
                while (!_failed && !BothMarkers("confirmed." + epoch)) yield return null;
                if (_failed) yield break;
                _holdStart = Time.realtimeSinceStartup;
                SetStage("results-hold-epoch-" + epoch, 20f);
                while (!_failed && Time.realtimeSinceStartup - _holdStart < 8f)
                {
                    _current.HoldSeconds = Time.realtimeSinceStartup - _holdStart;
                    if (_faultProfile == "peer-silence" && !_peerSilenceActivated && _current.HoldSeconds >= 1f)
                    {
                        _expectedDeparture = "PEER TIMEOUT";
                        _faultsBeforePeerSilence = LanUdpPeer.InjectedFaults;
                        LanUdpPeer.ActivatePeerSilence(); _peerSilenceActivated = true;
                    }
                    if (_director.Sim.Tick - 1 != _current.Finish.FinishTick || _director.Sim.Checksum() != _current.Finish.Checksum)
                    { Fail("Confirmed result mutated during result hold."); yield break; }
                    yield return null;
                }
                if (_failed) yield break;
                _current.HoldSeconds = Time.realtimeSinceStartup - _holdStart;
                _holdStart = -1f;
                SetStage("hold-ready-epoch-" + epoch, 20f);
                WriteMarker(_role + ".hold-ready." + epoch);
                while (!_failed && !BothMarkers("hold-ready." + epoch)) yield return null;
                if (_failed) yield break;
                SetStage("unilateral-consent-epoch-" + epoch, 20f);
                if (_role == "host")
                {
                    if (!_director.RequestRematch()) { Fail("Host rematch intent was rejected."); yield break; }
                    float until = Time.realtimeSinceStartup + 1f;
                    while (!_failed && Time.realtimeSinceStartup < until)
                    {
                        if (_director.MatchEpoch != epoch || _director.State != MatchState.MatchOver)
                        { Fail("Unilateral consent started a rematch."); yield break; }
                        yield return null;
                    }
                    if (_failed) yield break;
                    _current.UnilateralVerified = true;
                    // Select the final-epoch movement driver before publishing consent.
                    _liveOnly = epoch + 1 == requiredRematches;
                    WriteMarker("host.unilateral-verified." + epoch);
                }
                else
                {
                    while (!_failed && !HasMarker("host.unilateral-verified." + epoch))
                    {
                        if (_director.MatchEpoch != epoch || _director.State != MatchState.MatchOver)
                        { Fail("Remote-only consent started a rematch."); yield break; }
                        yield return null;
                    }
                    if (_failed) yield break;
                    _current.UnilateralVerified = true;
                    _liveOnly = epoch + 1 == requiredRematches;
                    if (!_director.RequestRematch()) { Fail("Joiner rematch intent was rejected."); yield break; }
                }
                SetStage("bilateral-rematch-" + (epoch + 1), 20f);
                while (!_failed && _director.MatchEpoch == epoch) yield return null;
                if (_failed) yield break;
                _successfulRematches = epoch + 1;
            }
            if (_failed) yield break;
            if (_scenario == "lifecycle") yield return LeaveAndRejoin();
            if (!_failed) yield return Complete("Every selected natural gameplay, result agreement, hold, consent and recovery stage passed.");
        }

        IEnumerator LeaveAndRejoin()
        {
            SetStage("planned-departure", 20f);
            _expectedDeparture = _role == _leaveRole ? "LOCAL LEAVE" : "PEER LEFT";
            _expectingDeparture = true;
            WriteMarker(_role + ".live");
            while (!_failed && !BothMarkers("live")) yield return null;
            if (_failed) yield break;
            GameSession session = GameSession.Ensure();
            if (_role == _leaveRole)
            { session.ReturnToMainMenu(); _observedDeparture = "LOCAL LEAVE"; }
            else
            {
                while (!_failed && _director != null && _director.FailureReason != "PEER LEFT") yield return null;
                if (_failed || _director == null) { if (!_failed) Fail("Peer disappeared without a current Goodbye."); yield break; }
                _observedDeparture = _director.FailureReason; session.ReturnToMainMenu();
            }
            yield return null;
            if (Time.timeScale != 1f || session.Kind != PlayKind.Local || SceneManager.GetActiveScene().name != GameSession.MainMenuScene)
            { Fail("Actual Main Menu recovery did not restore local mode and normal time."); yield break; }
            WriteMarker(_role + ".menu");
            while (!_failed && !BothMarkers("menu")) yield return null;
            if (_failed) yield break;
            SetStage("fresh-handshake", 30f);
            if (_role == "host")
            {
                BeginConnection();
                while (!_failed && (_director == null || !(_director.LanSession?.SocketBound ?? false))) yield return null;
                WriteMarker("host.fresh-ready");
            }
            else
            {
                while (!_failed && !HasMarker("host.fresh-ready")) yield return null;
                if (_failed) yield break;
                BeginConnection();
            }
            while (!_failed && (_director == null || !_director.ReadyForNetplay)) yield return null;
            if (_failed) yield break;
            SetStage("fresh-live", 30f);
            while (!_failed && !HasLiveProgress()) yield return null;
            if (_failed) yield break;
            if (_director.MatchId == _initialMatchId || _director.MatchEpoch != 0 || _director.LocalSeat != _initialSeat ||
                _current.InitializationSource != "normal_gameplay")
                Fail("Fresh connection reused an old identity/epoch/seat or lacked a production baseline.");
        }

        void Update()
        {
            if (_failed || _terminalWritten || _scenarioDeadline <= 0f) return;
            AttachDirector();
            ObservePacing();
            string reason = _director != null ? _director.FailureReason : GameSession.Ensure().ErrorMessage;
            if (!string.IsNullOrEmpty(reason))
            {
                if (_expectMismatch && _scenario == "smoke" && reason == "SETTINGS MISMATCH" &&
                    (_director?.Sim?.Tick ?? 0) == 0)
                { _failed = true; StartCoroutine(CompleteExpectedFailure("Compatibility rejection before gameplay.")); return; }
                if (_faultProfile == "peer-silence" && _peerSilenceActivated &&
                    LanUdpPeer.InjectedFaults > _faultsBeforePeerSilence && _stage.StartsWith("results-hold-", StringComparison.Ordinal) &&
                    reason == "PEER TIMEOUT" && ValidNaturalFinish())
                {
                    _observedDeparture = reason; _failed = true;
                    _current.HoldSeconds = Time.realtimeSinceStartup - _holdStart;
                    StartCoroutine(CompleteExpectedFailure("Explicit result-hold peer-silence fault produced the expected timeout."));
                    return;
                }
                if (!(_expectingDeparture && _role != _leaveRole && reason == "PEER LEFT") &&
                    !(_scenario == "fault-evidence" && ExpectedDirectionalFailure()))
                { Fail(reason); return; }
            }
            if (Time.realtimeSinceStartup > _scenarioDeadline || Time.realtimeSinceStartup > _stageDeadline)
            { Fail("Scenario/stage deadline expired; missing required stage: " + _stage); return; }
            if (_director != null && _director.ReadyForNetplay) ObserveLiveMovement();
            if (_mechanic != null && _mechanic.InvalidReason.Length != 0) Fail("Invalid mechanic run: " + _mechanic.InvalidReason);
        }

        bool ValidNaturalFinish()
        {
            if (_current == null || !_current.HasFinish || _current.InitializationSource != "normal_gameplay" ||
                !_current.Gameplay.HasNaturalMilestones) return false;
            NetFinishTuple finish = _current.Finish;
            return MatchDirector.HasWinner(finish.Score0, finish.Score1, _director.Rules.PointsToWin, out int winner) &&
                winner == finish.Winner && finish.Score0 + finish.Score1 == _current.Gameplay.GoalCount &&
                _director.Lifecycle.ResultConfirmed && _director.Lifecycle.ConfirmedFinish.Equals(finish);
        }

        void CaptureFinish()
        {
            if (_current == null || !(_director?.Lifecycle?.ResultConfirmed ?? false)) return;
            _current.HasFinish = true; _current.Finish = _director.Lifecycle.ConfirmedFinish;
            _current.ValidatedChecksums = _director.LanSession.Rollback.ValidatedChecksumCount;
            _current.InputPacing = DashLanInputPacingSnapshot.Capture(_director.LanSession);
        }

        void ObserveLiveMovement()
        {
            DashArenaState state = _director.Sim;
            int advanced = _lastObservedTick < 0 ? 0 : Mathf.Max(0, state.Tick - _lastObservedTick);
            _lastObservedTick = state.Tick;
            if (state.Phase != MatchState.Live) return;
            _liveTicks += advanced;
            int validated = _director.LanSession.Rollback.ValidatedChecksumCount;
            if (!_hasLiveOrigin)
            {
                for (int seat = 0; seat < 2; seat++) _liveOrigins[seat] = state.Players[seat].Position;
                _checksumsAtFirstLive = validated; _hasLiveOrigin = true;
            }
            _checksumsSinceLive = validated - _checksumsAtFirstLive;
            for (int seat = 0; seat < 2; seat++)
                _maxDisplacement[seat] = Mathf.Max(_maxDisplacement[seat], Vector2.Distance(_liveOrigins[seat], state.Players[seat].Position));
        }

        bool HasLiveProgress() => _director != null && _director.ReadyForNetplay && _director.State == MatchState.Live &&
            _liveTicks >= MinimumLiveTicks && _checksumsSinceLive >= MinimumChecksums &&
            _director.ConfirmedStateTick >= _director.Rules.KickoffTicks + MinimumLiveTicks &&
            _maxDisplacement[0] > .25f && _maxDisplacement[1] > .25f;

        static DashArenaInput SmokeInput(int seat, int tick) => DashArenaInput.Unpack(new DashArenaInput
        {
            Move = SmokeDirections[(tick / 45 + seat * 2) % SmokeDirections.Length],
            DashPressed = tick >= 180 && tick % 240 == 200
        }.Pack());

        void BeginConnection()
        {
            _expectingDeparture = false;
            GameSession session = GameSession.Ensure();
            if (_role == "host") session.BeginHost(8, 1); else session.BeginJoin("127.0.0.1", 8, 1);
            AttachDirector();
        }

        IEnumerator Complete(string message)
        {
            if (PresentationErrors() != 0) { Fail("Presentation callback errors prohibit diagnostic PASS."); yield break; }
            if (!DashLanDiagnosticReport.HasRequiredFaultInjection(_faultProfile, _role, LanUdpPeer.InjectedFaults, _dropFirstRequested))
            { Fail("Actual fault injection does not match the profile's required sending role."); yield break; }
            SetStage("complete", 60f);
            if (_failed) yield break; // A stage-write failure must never be overwritten by PASS.
            _terminalWritten = true;
            WriteResult(true, message);
            yield return HoldAfterFinal(false);
        }

        IEnumerator CompleteExpectedFailure(string message)
        {
            if (PresentationErrors() != 0) { WriteResult(false, "Presentation callback errors prohibit expected-failure PASS."); Application.Quit(2); yield break; }
            if (!DashLanDiagnosticReport.HasRequiredFaultInjection(_faultProfile, _role, LanUdpPeer.InjectedFaults, _dropFirstRequested))
            { WriteResult(false, "Expected-failure injection does not match the profile's required sending role."); Application.Quit(2); yield break; }
            _terminalWritten = true; WriteResult(true, message);
            yield return HoldAfterFinal(true);
        }

        IEnumerator HoldAfterFinal(bool expectedFailure)
        {
            // No Quit/Goodbye race with the other peer's last progress observation.
            // The launcher owns both processes and normally stops them after both reports.
            float until = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < until)
            {
                if (!expectedFailure && (_director == null || _director.NetworkPaused || PresentationErrors() != 0))
                { _failed = false; Fail("Network/presentation failure while retaining final-report liveness."); yield break; }
                yield return null;
            }
            Application.Quit(0);
        }

        int PresentationErrors() => _retiredDirectorErrors + _retiredSessionErrors +
            (_director != null ? _director.PresentationErrorCount + (_director.LanSession?.PresentationErrorCount ?? 0) : 0);

        void SetStage(string stage, float seconds)
        {
            _stage = stage; _stageDeadline = Time.realtimeSinceStartup + seconds;
            string line = "stage=" + stage + " realtime=" + Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture) +
                " deadline_seconds=" + seconds.ToString("F3", CultureInfo.InvariantCulture);
            Debug.Log("Dash LAN diagnostic " + line);
            if (!string.IsNullOrEmpty(_resultPath))
                try { File.AppendAllText(_resultPath + ".stages", line + "\n"); }
                catch (Exception error) { Fail("Could not write stage trail: " + error.Message); }
        }

        void WriteMarker(string name)
        {
            try { File.WriteAllText(Path.Combine(_coordinationPath, name), _stage); }
            catch (Exception error) { Fail("Could not write coordination marker: " + error.Message); }
        }
        bool HasMarker(string name) => File.Exists(Path.Combine(_coordinationPath, name));
        bool BothMarkers(string suffix) => HasMarker("host." + suffix) && HasMarker("join." + suffix);

        void Fail(string message)
        {
            if (_failed) return;
            _failed = true; CaptureFinish(); WriteResult(false, message); Application.Quit(2);
        }

        void WriteResult(bool passed, string message)
        {
            CaptureFinish();
            var report = new StringBuilder();
            Action<string, object> field = (name, value) => DashLanDiagnosticReport.Field(report, name, value);
            DashLanSession lan = _director != null ? _director.LanSession : null;
            DashArenaState state = lan?.State;
            ArenaRulesProfile rules = _director != null ? _director.Rules : ArenaRulesProfile.CreateProductionV1();
            field("result", passed ? "PASS" : "FAIL"); field("whole_scenario_final", true);
            field("report_schema", DashLanDiagnosticReport.Schema); field("protocol_version", DashNetcode.Version);
            field("expects_version_mismatch", _expectMismatch);
            field("advertised_protocol_version", DevelopmentLanFaults.AdvertisedVersion(DashNetcode.Version));
            field("role", _role); field("scenario", _scenario); field("stage", _stage); field("summary", message);
            field("terminal_reason", _director != null ? _director.FailureReason : GameSession.Ensure().ErrorMessage);
            field("failure_origin", lan?.FailureOrigin ?? "none"); field("peer_failure_reason", lan?.PeerFailureReason ?? "");
            field("scope", "same_machine_two_process"); field("two_computer_validated", false);
            field("profile_id", _mechanic == null ? "production_v1" : "mechanic_fixture"); field("rules_hash", rules?.ContentHash); field("layout_hash", rules?.LayoutHash);
            field("seed", lan?.Seed ?? 0); field("seed_policy", DashNetcode.SeedPolicy);
            field("action_vocabulary", "movement,dash"); field("scoring_strategy", _scenario == "smoke" || _scenario == "pacing" || _mechanic != null ? "not_applicable" : "cooperative_centerline_collision_goals");
            field("connection_id", lan?.MatchId ?? 0); field("first_connection_id", _initialMatchId); field("epoch", lan?.Epoch ?? 0);
            field("local_seat", lan?.LocalSeat ?? -1); field("first_socket_endpoint", _firstSocketEndpoint);
            field("simulation_tick", state?.Tick ?? 0); field("last_remote_tick", lan?.Rollback?.NewestRemoteTick ?? -1);
            field("last_ack_tick", lan?.Lifecycle?.LastPeerAck ?? -1); field("confirmed_state_tick", lan?.ConfirmedStateTick ?? -1);
            field("validated_checksums", lan?.Rollback?.ValidatedChecksumCount ?? 0);
            DashLanInputPacingSnapshot.Capture(lan).Append(report, "input_pacing.");
            field("received_datagrams", lan?.ReceivedDatagrams ?? 0); field("accepted_input_datagrams", lan?.AcceptedInputDatagrams ?? 0);
            field("rejected_datagrams", lan?.RejectedDatagrams ?? 0); field("controls_received", lan?.AcceptedControls ?? 0);
            field("controls_sent", lan?.ControlsSent ?? 0); field("heartbeats_sent", lan?.HeartbeatsSent ?? 0);
            field("heartbeats_received", lan?.HeartbeatsReceived ?? 0);
            field("send_attempts", lan?.SendAttempts ?? 0); field("sent_datagrams", lan?.SentDatagrams ?? 0); field("send_failures", lan?.SendFailures ?? 0);
            field("local_endpoint", lan?.LocalEndpoint?.ToString()); field("remote_endpoint", lan?.RemoteEndpoint?.ToString());
            field("last_socket_error", lan?.LastSocketError ?? (_director != null ? _director.NetworkInitializationError : ""));
            field("initial_state_error", lan?.InitializationError);
            field("lifecycle_phase", lan?.Lifecycle?.Phase.ToString()); field("successful_rematches", _successfulRematches);
            field("expected_departure", _expectedDeparture); field("observed_departure", _observedDeparture);
            field("fault_profile", _faultProfile); field("injected_faults", LanUdpPeer.InjectedFaults);
            string requiredFaultSender = _scenario == "fault-evidence" ? _silenceRole :
                _dropFirstRequested ? "both" : DashLanDiagnosticReport.RequiredFaultSender(_faultProfile);
            field("fault_required_sender", requiredFaultSender);
            field("local_fault_injection_required", requiredFaultSender == "both" || requiredFaultSender == _role);
            field("peer_silence_activated", _peerSilenceActivated);
            field("peer_silence_injected_faults", _peerSilenceActivated ? LanUdpPeer.InjectedFaults - _faultsBeforePeerSilence : 0);
            field("scenario_timeout_seconds", _scenarioTimeout); field("match_deadline_seconds", MatchDeadlineSeconds);
            field("presentation_error_count", PresentationErrors());
            field("director_presentation_error_count", _retiredDirectorErrors + (_director != null ? _director.PresentationErrorCount : 0));
            field("session_presentation_error_count", _retiredSessionErrors + (lan?.PresentationErrorCount ?? 0));
            field("last_presentation_error", _director != null ? _director.LastPresentationError : "");
            field("last_session_presentation_error", lan?.LastPresentationError);
            field("initialization_source", _current?.InitializationSource ?? "unobserved");
            bool anyNaturalFinish = _epochs.Exists(epoch => epoch.HasFinish && epoch.InitializationSource == "normal_gameplay");
            field("finish_source", anyNaturalFinish ? "normal_gameplay" : "not_completed");
            field("current_epoch_finish_source", _current != null && _current.HasFinish ? _current.InitializationSource : "not_completed");
            field("finish_tick", _current != null && _current.HasFinish ? _current.Finish.FinishTick : -1);
            field("finish_checksum", _current?.Finish.Checksum ?? 0); field("final_score_0", _current?.Finish.Score0 ?? 0);
            field("final_score_1", _current?.Finish.Score1 ?? 0); field("winner", _current != null && _current.HasFinish ? _current.Finish.Winner : -1);
            field("result_hold_seconds", _current?.HoldSeconds ?? 0f);
            field("smoke_target_tick", _targetTick); field("smoke_live_ticks", _liveTicks);
            field("smoke_checksums_since_live", _checksumsSinceLive);
            field("current_epoch_live_ticks", _liveTicks); field("current_epoch_checksums_since_live", _checksumsSinceLive);
            field("smoke_player_0_max_displacement", _maxDisplacement[0]); field("smoke_player_1_max_displacement", _maxDisplacement[1]);
            field("smoke_input_pattern", _mechanic != null ? "declared_mechanic_movement_dash" :
                _scenario == "smoke" || _scenario == "pacing" ? "changing_direction_dash" : "movement_dash_collision_goals");
            if (_scenario == "fault-evidence")
            {
                field("fault_evidence_scope", "same_machine_two_process_confirmed_result_hold");
                field("fault_silence_role", _silenceRole); field("fault_injection_method", "suppress_selected_process_outbound_after_confirmed_result");
                field("fault_silence_activated", _directionalActivated);
                field("fault_injected_datagrams", LanUdpPeer.InjectedFaults - _directionalFaultBaseline);
                field("fault_disconnect_seconds", DashLanLifecycle.DisconnectSeconds);
                field("fault_input_progress_timeout_seconds", DashLanSession.InputProgressTimeoutSeconds);
                field("fault_timeline_retention_seconds", DashNetworkTimeline.RetentionSeconds);
                field("fault_frozen_hold_seconds", _faultHoldSeconds); field("fault_timeline_retained", _faultTimelineRetained);
                field("fault_timeline_samples", _faultTimelineSamples); field("fault_timeline_sha256", _faultTimelineHash);
                field("fault_first_report", _faultFirstPath); field("fault_retained_report", _faultRetainedPath);
                field("fault_first_report_sha256", FileHashIfPresent(_faultFirstPath));
                field("fault_retained_report_sha256", FileHashIfPresent(_faultRetainedPath));
            }
            if (_scenario == "pacing")
            {
                field("pacing_scope", "same_machine_two_process_connection_epoch");
                field("pacing_connection_id", _pacingConnection); field("pacing_epoch", _pacingEpoch);
                field("pacing_stall_role", _pacingStallRole); field("pacing_requested_stall_seconds", PacingStallSeconds);
                field("pacing_maximum_input_lead_ticks", DashLanSession.MaximumInputLeadTicks);
                field("pacing_rollback_buffer_ticks", DashRollbackSession.BufferTicks);
                field("pacing_input_delay_ticks", DashRollbackSession.DelayTicks);
                field("pacing_stall_method", "automatic_ticking_disabled_manual_update_pump");
                field("pacing_stall_count", _pacingStallCount); field("pacing_skipped_fixed_steps", _pacingSkippedFixedSteps);
                field("pacing_stall_pump_calls", _pacingPumpCalls); field("pacing_actual_stall_seconds", _pacingActualStallSeconds);
                field("pacing_stall_start_tick", _pacingStallStartTick); field("pacing_stall_end_tick", _pacingStallEndTick);
                field("pacing_shared_stall_tick", _pacingSharedStallTick);
                field("pacing_stall_latest_local_input_tick", _pacingStallLatestLocalInputTick);
                field("pacing_wait_count_baseline", _pacingWaitBaseline); field("pacing_wait_count_at_observation", _pacingWaitCountAtObservation);
                field("pacing_wait_observed", _pacingWaitObserved); field("pacing_resume_observed", _pacingResumeObserved);
                field("pacing_wait_observed_during_stall", _pacingWaitObserved);
                field("pacing_observed_wait_seconds", _pacingObservedWaitSeconds);
                field("pacing_wait_tick", _pacingWaitTick); field("pacing_resume_tick", _pacingResumeTick);
                field("pacing_wait_contiguous_tick", _pacingWaitContiguous); field("pacing_resume_contiguous_tick", _pacingResumeContiguous);
                field("pacing_recovery_start_confirmed_tick", _pacingRecoveryConfirmed);
                field("pacing_recovery_start_checksums", _pacingRecoveryChecksums);
                field("pacing_recovery_confirmed_ticks", (lan?.ConfirmedStateTick ?? -1) - _pacingRecoveryConfirmed);
                field("pacing_recovery_validated_checksums", (lan?.Rollback?.ValidatedChecksumCount ?? 0) - _pacingRecoveryChecksums);
                field("pacing_checkpoint_observed", _pacingCheckpointObserved); field("pacing_checkpoint_tick", _pacingCheckpointTick);
                field("pacing_checkpoint_checksum", _pacingCheckpointChecksum);
            }
            int count = 0;
            foreach (DashLanEpochSummary epoch in _epochs) if (epoch.HasFinish) count++;
            field("completed_epochs", count);
            int index = 0, eventIndex = 0;
            foreach (DashLanEpochSummary epoch in _epochs)
            {
                if (epoch.HasFinish) epoch.Append(report, "epoch." + index++ + ".");
                foreach (DashArenaEvent item in epoch.Gameplay.Events)
                {
                    string prefix = "event." + eventIndex++ + ".";
                    field(prefix + "connection_id", epoch.MatchId); field(prefix + "epoch", item.Epoch); field(prefix + "tick", item.Tick);
                    field(prefix + "seat", item.Seat); field(prefix + "entity", item.EntityId); field(prefix + "kind", item.Kind);
                    field(prefix + "ordinal", item.Ordinal); field(prefix + "rules_hash", epoch.RulesHash);
                    field(prefix + "x", item.Position.x); field(prefix + "y", item.Position.y);
                }
            }
            field("confirmed_event_count", eventIndex);
            for (int seat = 0; seat < 2; seat++)
            {
                int dashes = 0, collisionGoals = 0;
                foreach (var epoch in _epochs) { dashes += epoch.Gameplay.Dashes[seat]; collisionGoals += epoch.Gameplay.CollisionGoals[seat]; }
                field("confirmed_seat_" + seat + "_dash", dashes);
                field("confirmed_seat_" + seat + "_collision_goals", collisionGoals);
            }
            if (_mechanic != null)
            {
                field("mechanic_case", _mechanic.Name); field("mechanic_seat", _mechanic.Seat);
                field("fixture_id", _mechanic.FixtureId); field("fixture_initial_checksum", _mechanic.InitialChecksum);
                field("fixture_prebroken_wall_id", _mechanic.Name == "wall-ball-repair" ? rules.Walls[1].Id : -1);
                field("mechanic_pass", _mechanic.Passed); field("required_milestones", string.Join(",", _mechanic.RequiredMilestones));
                field("mechanic_invalid_reason", _mechanic.InvalidReason);
                field("missing_milestones", _mechanic.MissingMilestones); field("milestone_count", _mechanic.Milestones.Count);
                field("comparison_type", _mechanic.Name == "green-impact" ? "readonly_previous_confirmed_state_actual_inputs_remove_green_only" : "not_applicable");
                field("green_actual_ball_speed", _mechanic.EnhancedBallSpeed);
                field("green_unbuffed_counterfactual_ball_speed", _mechanic.UnbuffedCounterfactualSpeed);
                field("green_comparison_tick", _mechanic.ComparisonTick);
                field("green_comparison_input0", _mechanic.ComparisonInput0); field("green_comparison_input1", _mechanic.ComparisonInput1);
                for (int i = 0; i < _mechanic.Milestones.Count; i++)
                {
                    DashMechanicMilestone milestone = _mechanic.Milestones[i]; string prefix = "milestone." + i + ".";
                    field(prefix + "name", milestone.Name); field(prefix + "epoch", milestone.Epoch); field(prefix + "tick", milestone.Tick);
                    field(prefix + "seat", milestone.Seat); field(prefix + "entity", milestone.Entity); field(prefix + "outcome", milestone.Outcome);
                    field(prefix + "rules_hash", rules.ContentHash);
                }
            }
            Debug.Log(report.ToString());
            if (string.IsNullOrEmpty(_resultPath)) return;
            try
            {
                string pending = _resultPath + ".pending";
                File.WriteAllText(pending, report.ToString());
                if (File.Exists(_resultPath)) File.Replace(pending, _resultPath, null); else File.Move(pending, _resultPath);
            }
            catch (Exception error) { Debug.LogError("Could not retain diagnostic report: " + error.Message); Application.Quit(2); }
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (!ReferenceEquals(_director, null))
            {
                _director.ConfirmedEvents -= OnConfirmed; _director.EpochInitialized -= OnEpoch;
                if (_director.LanSession != null) _director.LanSession.ConfirmedFrame -= OnConfirmedFrame;
            }
            if (_mechanic != null) DashMatchDirector.DiagnosticSessionFactory = null;
        }
        static string FileHashIfPresent(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        static string ValueOr(string prefix, string fallback) { string value = ArgumentValue(prefix); return value.Length == 0 ? fallback : value; }
        static string ArgumentValue(string prefix)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return argument.Substring(prefix.Length);
            return "";
        }
        static bool HasArgument(string name)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
                if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
#endif
