#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// Development build command-line smoke harness. It is not compiled into release players.
namespace PocketBump
{
    public sealed class LanSmokeRunner : MonoBehaviour
    {
        const int DefaultTargetTick = 900;
        const int MinimumLiveTicks = 120;
        static bool _sampleSmokeInput;
        static LanSmokeRunner _active;
        static readonly Vector2[] SmokeDirections =
        {
            Vector2.right, new Vector2(1, 1), Vector2.up, new Vector2(-1, 1),
            Vector2.left, new Vector2(-1, -1), Vector2.down, new Vector2(1, -1)
        };
        string _role;
        string _resultPath;
        string _readyPath;
        bool _expectMismatch;
        string _scenario = "smoke";
        string _faultProfile = "none";
        string _leaveRole = "host";
        string _coordinationPath;
        string _stage = "starting";
        string _observedDeparture = "";
        string _expectedDeparture = "";
        bool _fixtureApplied;
        bool _failed;
        bool _expectingDeparture;
        bool _finished;
        bool _peerSilenceActivated;
        int _faultsBeforePeerSilence;
        int _successfulRematches;
        int _observedEpoch = -1;
        int _observedMatchId;
        int _initialMatchId;
        int _initialSeat = -1;
        float _scenarioTimeout = 600f;
        float _scenarioDeadline;
        float _stageDeadline;
        MatchSimState _driverState;
        LanFullMatchDriver[] _drivers;
        LanConfirmedGameplay _gameplay = new LanConfirmedGameplay();
        readonly List<LanEpochDiagnosticSummary> _summaries = new List<LanEpochDiagnosticSummary>();
        readonly HashSet<MatchSimState> _normalInitialStates = new HashSet<MatchSimState>();
        int _targetTick = DefaultTargetTick;
        int _liveTicks;
        int _lastObservedTick = -1;
        bool _hasLiveOrigin;
        int _checksumsAtFirstLive;
        int _checksumsSinceLive;
        readonly Vector2[] _liveOrigins = new Vector2[2];
        readonly float[] _maxDisplacement = new float[2];

        // Legacy v6 harness retained for focused regression tests. The production
        // command-line entry point belongs exclusively to DashLanSmokeRunner (v7).
        static void StartFromCommandLine()
        {
            string role = ArgumentValue("--lan-smoke=");
            _sampleSmokeInput = false;
            if (role != "host" && role != "join") return;
            _sampleSmokeInput = true;
            var go = new GameObject("LanSmokeRunner");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<LanSmokeRunner>();
            _active = runner;
            runner._role = role;
            runner._resultPath = ArgumentValue("--lan-smoke-result=");
            runner._readyPath = ArgumentValue("--lan-smoke-ready=");
            runner._expectMismatch = HasArgument("--lan-smoke-expect-mismatch");
            string scenario = ArgumentValue("--lan-scenario=");
            if (!string.IsNullOrEmpty(scenario)) runner._scenario = scenario;
            string profile = ArgumentValue("--lan-fault-profile=");
            if (!string.IsNullOrEmpty(profile)) runner._faultProfile = profile;
            string leaveRole = ArgumentValue("--lan-leave-role=");
            if (!string.IsNullOrEmpty(leaveRole)) runner._leaveRole = leaveRole;
            runner._coordinationPath = ArgumentValue("--lan-coordination=");
            if (float.TryParse(ArgumentValue("--lan-scenario-timeout="), NumberStyles.Float, CultureInfo.InvariantCulture, out float timeout))
                runner._scenarioTimeout = Mathf.Clamp(timeout, 30f, 3600f);
            if (int.TryParse(ArgumentValue("--lan-smoke-ticks="), out int requestedTicks))
                runner._targetTick = Mathf.Clamp(requestedTicks, MatchSim.KickoffTicks + MinimumLiveTicks, 36000);
        }

        // Synthetic intent still travels through the normal local-input, UDP and rollback path.
        // Canonicalize with Pack/Unpack so both the local and remote simulation get identical vectors.
        public static bool TrySampleInput(int seat, int sampleTick, out PlayerInput input)
        {
            input = PlayerInput.Neutral;
            if (!_sampleSmokeInput) return false;
            int tick = Mathf.Max(0, sampleTick);
            int segment = ((tick + seat * 31) / 45) % 8;
            var sampled = new PlayerInput
            {
                Move = SmokeDirections[segment] * (seat == 0 ? 1 : -1),
                BoostHeld = (tick + seat * 37) % 151 < 30,
                ShootPressed = (tick + seat * 13) % 97 == 0
            };
            input = PlayerInput.Unpack(sampled.Pack());
            return true;
        }

        public static bool TrySampleInput(int seat, int sampleTick, MatchSimState state, out PlayerInput input)
        {
            if (_active == null || _active._scenario == "smoke") return TrySampleInput(seat, sampleTick, out input);
            _active.EnsureDriver(state);
            // Unity may run several fixed ticks before the next rendered Update.
            // Commit retained confirmed events before another snapshot can replace them.
            MatchDirector director = FindObjectOfType<MatchDirector>();
            if (director != null && director.ReadyForNetplay && ReferenceEquals(director.Sim, state))
                _active.ObserveConfirmed(director);
            input = _active._drivers[seat].Sample(seat, sampleTick, state);
            return _sampleSmokeInput;
        }

        void EnsureDriver(MatchSimState state)
        {
            if (ReferenceEquals(_driverState, state)) return;
            _driverState = state;
            _drivers = new[] { new LanFullMatchDriver(), new LanFullMatchDriver() };
            if (state != null && state.Tick == 0 && state.ScoreP1 == 0 && state.ScoreP2 == 0 && state.Phase == MatchState.Kickoff)
                _normalInitialStates.Add(state);
        }

        // Called exclusively before the initial rollback baseline is constructed.
        // It is never eligible for a rematch or for the later fresh connection.
        public static void ApplyInitialFixture(MatchSimState state, int epoch)
        {
            if (_active == null || _active._scenario != "lifecycle" || _active._fixtureApplied || epoch != 0) return;
            _active._fixtureApplied = true;
            state.ScoreP1 = state.PointsToWin - 1;
            state.ScoreP2 = 0;
            state.Phase = MatchState.Live;
            state.PhaseTicks = 0;
            state.GateClosed = false;
            MatchSim.DebugPlacePlayer(state, 1, new Vector2(ArenaLayout.HalfWidth - 0.35f, 0f), Vector2.right);
            PlayerSim player = state.Players[1];
            player.Velocity = Vector2.right * 6f;
            state.Players[1] = player;
            MatchSim.DebugSetMarble(state, 0, Vector2.zero, Vector2.zero);
        }

        IEnumerator Start()
        {
            yield return null;
            GameSession session = GameSession.Ensure();
            if (_scenario != "smoke")
            {
                if (_scenario != "lifecycle" && _scenario != "full-match")
                {
                    Finish(false, "Unknown diagnostic scenario: " + _scenario);
                    yield break;
                }
                yield return RunLifecycleScenario(session);
                yield break;
            }
            if (_role == "host") session.BeginHost(8, 1);
            else session.BeginJoin("127.0.0.1", 8, 1);

            bool readyWritten = false;

            float deadline = Time.realtimeSinceStartup + Mathf.Max(60f, _targetTick * MatchSim.TickDt * 3f + 15f);
            while (Time.realtimeSinceStartup < deadline)
            {
                MatchDirector director = FindObjectOfType<MatchDirector>();
                if (!readyWritten && director != null)
                {
                    WriteReady();
                    readyWritten = true;
                }
                if (_expectMismatch)
                {
                    if (session.ErrorMessage == "SETTINGS MISMATCH")
                    {
                        Finish(true, "Protocol mismatch rejected before gameplay.");
                        yield break;
                    }
                }
                else
                {
                    // Never turn a paused game into PASS merely because its tick counter is high enough.
                    if (!string.IsNullOrEmpty(session.ErrorMessage) || (director != null && director.NetworkPaused))
                    {
                        Finish(false, string.IsNullOrEmpty(session.ErrorMessage) ? "Network paused." : session.ErrorMessage,
                            director != null ? director.Diagnostics : session.LastLanDiagnostics);
                        yield break;
                    }
                    if (director != null && director.Sim != null)
                    {
                        ObserveLiveMovement(director.Sim, director.Diagnostics.ValidatedChecksums);
                        if (director.Sim.Tick >= _targetTick && _liveTicks >= MinimumLiveTicks &&
                            _maxDisplacement[0] > 0.25f && _maxDisplacement[1] > 0.25f &&
                            _checksumsSinceLive >= MinimumLiveTicks)
                        {
                            WriteResult(true, "Handshake and active two-player movement completed without network pause.", director.Diagnostics);
                            // The launcher closes both processes only after both have written reports.
                            // Continue exchanging input in the meantime, avoiding a PEER LEFT race.
                            float holdUntil = Time.realtimeSinceStartup + 30f;
                            while (Time.realtimeSinceStartup < holdUntil)
                            {
                                if (!string.IsNullOrEmpty(session.ErrorMessage) || director == null || director.NetworkPaused)
                                {
                                    Finish(false, string.IsNullOrEmpty(session.ErrorMessage) ? "Network paused while awaiting peer result." : session.ErrorMessage,
                                        session.LastLanDiagnostics);
                                    yield break;
                                }
                                yield return null;
                            }
                            Application.Quit(0);
                            yield break;
                        }
                    }
                }

                yield return null;
            }

            Finish(false, "Smoke test timed out.", session.LastLanDiagnostics);
        }

        IEnumerator RunLifecycleScenario(GameSession session)
        {
            if (string.IsNullOrEmpty(_coordinationPath) || !Directory.Exists(_coordinationPath))
            {
                Finish(false, "Lifecycle/full-match diagnostics require an existing shared coordination directory.");
                yield break;
            }
            _scenarioDeadline = Time.realtimeSinceStartup + _scenarioTimeout;
            SetStage("handshake", 30f);
            BeginConnection(session);
            MatchDirector director = null;
            while (!_failed)
            {
                director = FindObjectOfType<MatchDirector>();
                if (director != null) WriteReady();
                if (director != null && director.ReadyForNetplay) break;
                yield return null;
            }
            if (_failed) yield break;
            _initialMatchId = director.MatchId;
            _initialSeat = director.LocalSeat;
            int requiredRematches = _scenario == "lifecycle" ? 3 : 1;

            for (int epoch = 0; epoch <= requiredRematches && !_failed; epoch++)
            {
                SetStage(epoch == 0 ? "gameplay" : "rematch-live-" + epoch, 120f);
                while (!_failed && (director.MatchEpoch < epoch || director.Sim == null)) yield return null;
                if (_failed) yield break;
                if (director.MatchEpoch != epoch || director.MatchId != _initialMatchId || director.LocalSeat != _initialSeat)
                {
                    Fail("Rematch changed connection, seat, or skipped an epoch.", director);
                    yield break;
                }
                // The first sampled tick records the untouched production baseline.
                while (!_failed && director.Sim.Tick == 0) yield return null;
                bool seeded = _scenario == "lifecycle" && epoch == 0;
                if (!seeded && !_normalInitialStates.Contains(director.Sim))
                {
                    Fail("Match did not begin from normal zero-score tick-zero state.", director);
                    yield break;
                }
                if (epoch == requiredRematches)
                {
                    while (!_failed && !HasLiveProgress(director)) yield return null;
                    _successfulRematches = epoch;
                    break;
                }

                SetStage("gameplay-epoch-" + epoch, 120f);
                while (!_failed && !director.Lifecycle.ResultConfirmed) yield return null;
                if (_failed) yield break;
                ObserveConfirmed(director);
                if (!seeded && !_gameplay.HasGameplayMilestones)
                {
                    Fail("Confirmed result lacks collection, actual shot, dynamic impact, score, or legal winner evidence.", director);
                    yield break;
                }

                NetFinishTuple finish = director.Lifecycle.ConfirmedFinish;
                // These files schedule the diagnostic players' choices only. All
                // terminal agreement and rematch traffic still use the real socket.
                SetStage("confirmed-result-sync-epoch-" + epoch, 20f);
                WriteMarker(_role + ".confirmed." + epoch);
                while (!_failed && !BothMarkers("confirmed." + epoch)) yield return null;
                if (_failed) yield break;
                float holdStart = Time.realtimeSinceStartup;
                SetStage("results-hold-epoch-" + epoch, 20f);
                while (!_failed && Time.realtimeSinceStartup - holdStart < 8f)
                {
                    if (_faultProfile == "peer-silence" && !_peerSilenceActivated && Time.realtimeSinceStartup - holdStart >= 1f)
                    {
                        _expectedDeparture = "PEER TIMEOUT";
                        _faultsBeforePeerSilence = LanUdpPeer.InjectedFaults;
                        LanUdpPeer.ActivatePeerSilence();
                        _peerSilenceActivated = true;
                    }
                    if (director.Sim.Tick - 1 != finish.FinishTick || director.Sim.Checksum() != finish.Checksum)
                    {
                        Fail("Confirmed result changed during idle hold.", director);
                        yield break;
                    }
                    yield return null;
                }
                if (_failed) yield break;
                var summary = new LanEpochDiagnosticSummary
                {
                    MatchId = director.MatchId, Epoch = epoch,
                    FinishSource = seeded ? "seeded_fixture" : "normal_gameplay",
                    FinishTick = finish.FinishTick, Checksum = finish.Checksum,
                    Score0 = finish.Score0, Score1 = finish.Score1, Winner = finish.Winner,
                    SettingsHash = finish.SettingsHash,
                    Collections = _gameplay.Collections, Shots = _gameplay.Shots,
                    Collisions = _gameplay.Collisions, Scores = _gameplay.Scores,
                    ConfirmedTicks = _gameplay.LastTick + 1,
                    ValidatedChecksums = director.Diagnostics.ValidatedChecksums,
                    HoldSeconds = Time.realtimeSinceStartup - holdStart
                };
                _summaries.Add(summary);
                SetStage("hold-ready-epoch-" + epoch, 20f);
                WriteMarker(_role + ".hold-ready." + epoch);
                while (!_failed && !BothMarkers("hold-ready." + epoch)) yield return null;
                if (_failed) yield break;
                SetStage("unilateral-consent-epoch-" + epoch, 20f);
                if (_role == "host")
                {
                    director.RequestRematch();
                    float unilateralUntil = Time.realtimeSinceStartup + 1f;
                    while (!_failed && Time.realtimeSinceStartup < unilateralUntil)
                    {
                        if (director.MatchEpoch != epoch || director.Sim.Phase != MatchState.MatchOver)
                        {
                            Fail("Unilateral consent started a rematch.", director);
                            yield break;
                        }
                        yield return null;
                    }
                    if (_failed) yield break;
                    WriteMarker("host.unilateral-verified." + epoch);
                }
                else
                {
                    while (!_failed && !HasMarker("host.unilateral-verified." + epoch))
                    {
                        if (director.MatchEpoch != epoch || director.Sim.Phase != MatchState.MatchOver)
                        {
                            Fail("Host consent started a rematch before the joiner consented.", director);
                            yield break;
                        }
                        yield return null;
                    }
                    if (_failed) yield break;
                    director.RequestRematch();
                }
                if (_failed) yield break;
                SetStage("bilateral-rematch-" + (epoch + 1), 20f);
                while (!_failed && director.MatchEpoch == epoch) yield return null;
                _successfulRematches = epoch + 1;
            }
            if (_failed) yield break;

            if (_scenario == "lifecycle")
            {
                if (string.IsNullOrEmpty(_coordinationPath))
                {
                    Fail("Lifecycle leave/rejoin requires a coordination directory.", director);
                    yield break;
                }
                SetStage("planned-departure", 20f);
                _expectedDeparture = _role == _leaveRole ? "LOCAL LEAVE" : "PEER LEFT";
                _expectingDeparture = true;
                WriteMarker(_role + ".live");
                while (!_failed && !BothMarkers("live")) yield return null;
                if (_role == _leaveRole)
                {
                    session.ReturnToMainMenu();
                    _observedDeparture = "LOCAL LEAVE";
                }
                else
                {
                    while (!_failed && session.ErrorMessage != "PEER LEFT") yield return null;
                    if (_failed) yield break;
                    _observedDeparture = session.ErrorMessage;
                    session.ReturnToMainMenu();
                }
                yield return null;
                if (Time.timeScale != 1f || session.Kind != PlayKind.Local)
                {
                    Fail("Menu recovery did not restore normal time/local menu mode.", null);
                    yield break;
                }
                WriteMarker(_role + ".menu");
                while (!_failed && !BothMarkers("menu")) yield return null;
                SetStage("fresh-handshake", 30f);
                if (_role == "host")
                {
                    BeginConnection(session);
                    yield return null;
                    WriteMarker("host.fresh-ready");
                }
                else
                {
                    while (!_failed && !HasMarker("host.fresh-ready")) yield return null;
                    BeginConnection(session);
                }
                director = null;
                while (!_failed)
                {
                    director = FindObjectOfType<MatchDirector>();
                    if (director != null && director.ReadyForNetplay) break;
                    yield return null;
                }
                if (_failed) yield break;
                SetStage("fresh-live", 30f);
                while (!_failed && !HasLiveProgress(director)) yield return null;
                if (_failed) yield break;
                if (director.MatchId == _initialMatchId || director.MatchEpoch != 0 || director.LocalSeat != _initialSeat ||
                    !_normalInitialStates.Contains(director.Sim))
                {
                    Fail("Fresh session reused identity/epoch/seat state or repeated the fixture.", director);
                    yield break;
                }
            }

            if (_failed) yield break;
            SetStage("complete", 40f);
            _finished = true;
            WriteResult(true, "All selected scenario stages completed using real UDP/rollback input; scoring strategy is cooperative side-pocket dunks.", director.Diagnostics);
            float holdUntil = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < holdUntil)
            {
                if (director == null || director.NetworkPaused || !string.IsNullOrEmpty(session.ErrorMessage))
                {
                    Fail(string.IsNullOrEmpty(session.ErrorMessage) ? "Network paused while awaiting peer final report." : session.ErrorMessage, director);
                    yield break;
                }
                yield return null;
            }
            Application.Quit(0);
        }

        void Update()
        {
            if (_scenario == "smoke" || _failed || _finished || _scenarioDeadline <= 0f) return;
            GameSession session = GameSession.Ensure();
            MatchDirector director = FindObjectOfType<MatchDirector>();
            if (!string.IsNullOrEmpty(session.ErrorMessage) || director != null && director.NetworkPaused)
            {
                string reason = session.ErrorMessage;
                if (_faultProfile == "peer-silence" && _peerSilenceActivated &&
                    LanUdpPeer.InjectedFaults > _faultsBeforePeerSilence &&
                    _stage.StartsWith("results-hold-", StringComparison.Ordinal) && reason == "PEER TIMEOUT")
                {
                    _expectedDeparture = "PEER TIMEOUT";
                    _observedDeparture = reason;
                    _finished = true;
                    _failed = true; // stop the ordinary positive-scenario coroutine
                    WriteResult(true, "Expected negative profile: silent peer produced PEER TIMEOUT during confirmed results.", director != null ? director.Diagnostics : session.LastLanDiagnostics);
                    Application.Quit(0);
                    return;
                }
                if (!(_expectingDeparture && _role != _leaveRole && reason == "PEER LEFT"))
                {
                    Fail(string.IsNullOrEmpty(reason) ? "Unexpected network pause." : reason, director);
                    return;
                }
            }
            if (Time.realtimeSinceStartup > _scenarioDeadline || Time.realtimeSinceStartup > _stageDeadline)
            {
                Fail("Scenario/stage deadline expired before all required milestones: " + _stage, director);
                return;
            }
            if (director != null && director.ReadyForNetplay) ObserveConfirmed(director);
        }

        void ObserveConfirmed(MatchDirector director)
        {
            if (_observedMatchId != director.MatchId || _observedEpoch != director.MatchEpoch)
            {
                _observedMatchId = director.MatchId;
                _observedEpoch = director.MatchEpoch;
                _gameplay = new LanConfirmedGameplay();
                EnsureDriver(director.Sim);
            }
            for (int tick = _gameplay.LastTick + 1; tick <= director.ConfirmedStateTick; tick++)
            {
                if (!director.TryGetConfirmedEvents(tick, out SimEvents events))
                {
                    Fail("Confirmed event history was not retained at tick " + tick, director);
                    return;
                }
                if (!_gameplay.Commit(tick, events)) continue;
                _drivers[0].ObserveConfirmed(events);
                _drivers[1].ObserveConfirmed(events);
            }
        }

        bool HasLiveProgress(MatchDirector director)
        {
            return director.ReadyForNetplay && director.Sim.Phase == MatchState.Live &&
                   director.ConfirmedStateTick >= MatchSim.KickoffTicks + MinimumLiveTicks &&
                   director.Diagnostics.ValidatedChecksums >= MinimumLiveTicks;
        }

        void BeginConnection(GameSession session)
        {
            _expectingDeparture = false;
            if (_role == "host") session.BeginHost(8, 1);
            else session.BeginJoin("127.0.0.1", 8, 1);
        }

        void SetStage(string stage, float deadlineSeconds)
        {
            _stage = stage;
            _stageDeadline = Time.realtimeSinceStartup + deadlineSeconds;
            string line = "stage=" + stage + " realtime=" + Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture);
            Debug.Log("LAN diagnostic " + line);
            if (!string.IsNullOrEmpty(_resultPath))
                try { File.AppendAllText(_resultPath + ".stages", line + "\n"); }
                catch (Exception ex) { Debug.LogError("Could not write diagnostic stage: " + ex.Message); }
        }

        void WriteMarker(string name)
        {
            try { File.WriteAllText(Path.Combine(_coordinationPath, name), _stage); }
            catch (Exception ex) { Fail("Could not write coordination marker: " + ex.Message, null); }
        }

        bool HasMarker(string name) => File.Exists(Path.Combine(_coordinationPath, name));
        bool BothMarkers(string suffix) => HasMarker("host." + suffix) && HasMarker("join." + suffix);

        void Fail(string message, MatchDirector director)
        {
            if (_failed) return;
            _failed = true;
            Finish(false, message, director != null ? director.Diagnostics : GameSession.Ensure().LastLanDiagnostics);
        }

        void ObserveLiveMovement(MatchSimState state, int validatedChecksums)
        {
            int advancedTicks = _lastObservedTick < 0 ? 0 : Mathf.Max(0, state.Tick - _lastObservedTick);
            _lastObservedTick = state.Tick;
            if (state.Phase != MatchState.Live) return;
            _liveTicks += advancedTicks;
            if (!_hasLiveOrigin)
            {
                for (int seat = 0; seat < 2; seat++) _liveOrigins[seat] = state.Players[seat].Position;
                _checksumsAtFirstLive = validatedChecksums;
                _hasLiveOrigin = true;
            }
            _checksumsSinceLive = validatedChecksums - _checksumsAtFirstLive;
            for (int seat = 0; seat < 2; seat++)
                _maxDisplacement[seat] = Mathf.Max(_maxDisplacement[seat], Vector2.Distance(_liveOrigins[seat], state.Players[seat].Position));
        }

        void WriteReady()
        {
            if (string.IsNullOrEmpty(_readyPath)) return;
            try { File.WriteAllText(_readyPath, _role + " ready"); }
            catch (Exception ex) { Debug.LogError("Could not write LAN smoke ready marker: " + ex.Message); }
        }

        void Finish(bool passed, string message, LanDiagnosticSnapshot diagnostics = null)
        {
            WriteResult(passed, message, diagnostics);
            Application.Quit(passed ? 0 : 2);
        }

        void WriteResult(bool passed, string message, LanDiagnosticSnapshot diagnostics = null)
        {
            diagnostics = diagnostics ?? GameSession.Ensure().LastLanDiagnostics ?? new LanDiagnosticSnapshot { Role = _role };
            if (string.IsNullOrEmpty(diagnostics.Role) || diagnostics.Role == "unknown") diagnostics.Role = _role;
            diagnostics.Scenario = _scenario;
            diagnostics.Stage = _scenario == "smoke" ? (passed ? "complete" : "smoke") : _stage;
            diagnostics.FinishSource = _scenario == "smoke" ? "not_completed" :
                (_scenario == "lifecycle" && _fixtureApplied && diagnostics.MatchId == _initialMatchId && diagnostics.Epoch == 0
                    ? "seeded_fixture" : "normal_gameplay");
            diagnostics.SuccessfulRematches = _successfulRematches;
            diagnostics.ExpectedDeparture = _expectedDeparture;
            diagnostics.ObservedDeparture = _observedDeparture;
            foreach (LanEpochDiagnosticSummary summary in _summaries) diagnostics.RetainEpoch(summary);
            if (diagnostics.FinishTick >= 0) diagnostics.RetainCurrentConfirmedEpoch(_gameplay);
            string result = diagnostics.FormatReport(passed, message) +
                "\nwhole_scenario_final=true" +
                "\nfault_profile=" + _faultProfile +
                "\ninjected_faults=" + LanUdpPeer.InjectedFaults +
                "\npeer_silence_activated=" + (_peerSilenceActivated ? "true" : "false") +
                "\npeer_silence_injected_faults=" + (_peerSilenceActivated ? LanUdpPeer.InjectedFaults - _faultsBeforePeerSilence : 0) +
                "\nscenario_timeout_seconds=" + _scenarioTimeout.ToString("F0", CultureInfo.InvariantCulture) +
                "\nconfirmed_collections=" + _gameplay.Collections +
                "\nconfirmed_shots=" + _gameplay.Shots +
                "\nconfirmed_dynamic_collisions=" + _gameplay.Collisions +
                "\nconfirmed_scores=" + _gameplay.Scores +
                "\nscoring_strategy=" + (_scenario == "smoke" ? "not_applicable" : "cooperative_side_pocket_dunks") +
                "\nsmoke_target_tick=" + _targetTick +
                "\nsmoke_live_ticks=" + _liveTicks +
                "\nsmoke_checksums_since_live=" + _checksumsSinceLive +
                "\nsmoke_player_0_max_displacement=" + _maxDisplacement[0].ToString("F3", CultureInfo.InvariantCulture) +
                "\nsmoke_player_1_max_displacement=" + _maxDisplacement[1].ToString("F3", CultureInfo.InvariantCulture) +
                "\nsmoke_input_pattern=" + (_scenario == "smoke" ? "changing_direction_boost_shoot" : "collect_shoot_impact_cooperative_dunk") + "\n";
            Debug.Log(result);
            if (!string.IsNullOrEmpty(_resultPath))
            {
                try
                {
                    string pending = _resultPath + ".pending";
                    File.WriteAllText(pending, result);
                    if (File.Exists(_resultPath)) File.Replace(pending, _resultPath, null);
                    else File.Move(pending, _resultPath);
                }
                catch (Exception ex) { Debug.LogError("Could not write LAN smoke result: " + ex.Message); }
            }
        }

        static string ArgumentValue(string prefix)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return args[i].Substring(prefix.Length);
            return "";
        }

        static bool HasArgument(string expected)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], expected, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
#endif
