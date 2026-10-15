#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PocketBump
{
    // Caller supplies ONLY Director.ConfirmedEvents, never speculative Step output.
    // Scope is one connection/epoch. Stable identity rejects repeated confirmed delivery.
    public sealed class DashLanConfirmedGameplay
    {
        const int MaximumEvents = 512;
        readonly HashSet<DashArenaEventIdentity> _seen = new HashSet<DashArenaEventIdentity>();
        readonly List<DashArenaEvent> _events = new List<DashArenaEvent>();
        readonly int[] _lastDash = { -100000, -100000 };
        readonly int[] _ballImpactSeat;
        readonly int[] _ballImpactTick;
        readonly bool[] _ballDashImpact;
        readonly ArenaRulesProfile _rules;
        public int Epoch { get; }
        public int[] Dashes { get; } = new int[2];
        public int[] Impacts { get; } = new int[2];
        public int[] CollisionGoals { get; } = new int[2];
        public int GoalCount { get; private set; }
        public int WinnerCount { get; private set; }
        public int LastTick { get; private set; } = -1;
        public bool Overflowed { get; private set; }
        public IReadOnlyList<DashArenaEvent> Events => _events.AsReadOnly();
        public bool HasNaturalMilestones => !Overflowed && Dashes[0] > 0 && Dashes[1] > 0 &&
            CollisionGoals[0] > 0 && CollisionGoals[1] > 0 && WinnerCount == 1;

        public DashLanConfirmedGameplay(ArenaRulesProfile rules, int epoch)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules)); Epoch = epoch;
            _ballImpactSeat = new int[rules.BallCount]; _ballImpactTick = new int[rules.BallCount];
            _ballDashImpact = new bool[rules.BallCount]; ClearBallLinks();
        }

        public void Commit(IReadOnlyList<DashArenaEvent> events)
        {
            foreach (DashArenaEvent item in events)
            {
                if (item.Epoch != Epoch || _seen.Contains(item.Identity)) continue;
                if (_events.Count >= MaximumEvents) { Overflowed = true; continue; }
                if (item.Tick < LastTick) throw new InvalidOperationException("Confirmed gameplay events arrived out of order.");
                _seen.Add(item.Identity); _events.Add(item); LastTick = item.Tick;
                int seat = item.Seat;
                if (item.Kind == DashArenaEventKind.DashStarted && seat >= 0 && seat < 2)
                { Dashes[seat]++; _lastDash[seat] = item.Tick; }
                if (item.Kind == DashArenaEventKind.Impact && seat >= 0 && seat < 2 &&
                    item.EntityId >= 2 && item.EntityId < 2 + _rules.BallCount)
                {
                    int ball = item.EntityId - 2;
                    Impacts[seat]++; _ballImpactSeat[ball] = seat; _ballImpactTick[ball] = item.Tick;
                    _ballDashImpact[ball] = item.Tick - _lastDash[seat] < _rules.DashTicks;
                }
                if (item.Kind == DashArenaEventKind.Goal)
                {
                    GoalCount++;
                    int ball = item.EntityId;
                    bool correctGoal = seat == 0 ? item.Position.y < -_rules.HalfHeight : item.Position.y > _rules.HalfHeight;
                    if (seat >= 0 && seat < 2 && ball >= 0 && ball < _rules.BallCount && correctGoal &&
                        _ballImpactSeat[ball] == seat && _ballImpactTick[ball] <= item.Tick && _ballDashImpact[ball])
                        CollisionGoals[seat]++;
                    ClearBallLinks(); // Re-kickoff balls must obtain fresh impact evidence.
                }
                if (item.Kind == DashArenaEventKind.MatchOver) WinnerCount++;
            }
        }

        void ClearBallLinks()
        {
            for (int i = 0; i < _ballImpactSeat.Length; i++)
            { _ballImpactSeat[i] = -1; _ballImpactTick[i] = -1; _ballDashImpact[i] = false; }
        }
    }

    public sealed class DashLanEpochSummary
    {
        public int MatchId, Epoch, ValidatedChecksums;
        public uint Seed;
        public string RulesHash, LayoutHash, LocalEndpoint;
        public string InitializationSource = "unobserved";
        public NetFinishTuple Finish;
        public bool HasFinish, UnilateralVerified;
        public float HoldSeconds;
        public DashLanConfirmedGameplay Gameplay;
        public DashLanInputPacingSnapshot InputPacing;

        public void Append(StringBuilder report, string prefix)
        {
            DashLanDiagnosticReport.Field(report, prefix + "connection_id", MatchId);
            DashLanDiagnosticReport.Field(report, prefix + "epoch", Epoch);
            DashLanDiagnosticReport.Field(report, prefix + "seed", Seed);
            DashLanDiagnosticReport.Field(report, prefix + "rules_hash", RulesHash);
            DashLanDiagnosticReport.Field(report, prefix + "layout_hash", LayoutHash);
            DashLanDiagnosticReport.Field(report, prefix + "initialization_source", InitializationSource);
            DashLanDiagnosticReport.Field(report, prefix + "finish_source", HasFinish ? InitializationSource : "not_completed");
            DashLanDiagnosticReport.Field(report, prefix + "finish_tick", HasFinish ? Finish.FinishTick : -1);
            DashLanDiagnosticReport.Field(report, prefix + "checksum", HasFinish ? Finish.Checksum : 0);
            DashLanDiagnosticReport.Field(report, prefix + "score0", Finish.Score0);
            DashLanDiagnosticReport.Field(report, prefix + "score1", Finish.Score1);
            DashLanDiagnosticReport.Field(report, prefix + "winner", HasFinish ? Finish.Winner : -1);
            DashLanDiagnosticReport.Field(report, prefix + "settings_hash", Finish.SettingsHash);
            DashLanDiagnosticReport.Field(report, prefix + "hold_seconds", HoldSeconds.ToString("F3", CultureInfo.InvariantCulture));
            DashLanDiagnosticReport.Field(report, prefix + "unilateral_verified", UnilateralVerified);
            DashLanDiagnosticReport.Field(report, prefix + "validated_checksums", ValidatedChecksums);
            DashLanDiagnosticReport.Field(report, prefix + "socket_endpoint", LocalEndpoint);
            for (int seat = 0; seat < 2; seat++)
            {
                DashLanDiagnosticReport.Field(report, prefix + "dash" + seat, Gameplay?.Dashes[seat] ?? 0);
                DashLanDiagnosticReport.Field(report, prefix + "collision_goals" + seat, Gameplay?.CollisionGoals[seat] ?? 0);
            }
            DashLanDiagnosticReport.Field(report, prefix + "goals", Gameplay?.GoalCount ?? 0);
            DashLanDiagnosticReport.Field(report, prefix + "winners", Gameplay?.WinnerCount ?? 0);
            InputPacing?.Append(report, prefix + "input_pacing.");
        }
    }

    // A copied observation, not authority. Completed epochs retain their own metrics
    // before the session resets counters; top-level metrics describe only the current epoch.
    public sealed class DashLanInputPacingSnapshot
    {
        int _connection, _epoch, _simulationTick, _local, _contiguous, _remote, _lag, _count;
        bool _waiting;
        float _seconds, _current, _maximum, _age;

        public static DashLanInputPacingSnapshot Capture(DashLanSession session) => new DashLanInputPacingSnapshot
        {
            _connection = session?.MatchId ?? 0, _epoch = session?.Epoch ?? 0,
            _simulationTick = session?.State?.Tick ?? 0, _local = session?.LatestLocalInputTick ?? -1,
            _contiguous = session?.RemoteContiguousInputTick ?? -1, _remote = session?.LatestRemoteInputTick ?? -1,
            _lag = session?.InputLagTicks ?? 0, _count = session?.InputWaitCount ?? 0,
            _waiting = session?.WaitingForRemoteInput ?? false, _seconds = session?.InputWaitSeconds ?? 0f,
            _current = session?.InputWaitCurrentSeconds ?? 0f, _maximum = session?.InputWaitMaxSeconds ?? 0f,
            _age = session?.InputProgressAgeSeconds ?? 0f
        };

        public void Append(StringBuilder report, string prefix)
        {
            Action<string, object> field = (key, value) => DashLanDiagnosticReport.Field(report, prefix + key, value);
            field("scope", "connection_epoch"); field("connection_id", _connection); field("epoch", _epoch);
            field("simulation_tick", _simulationTick); field("latest_local_input_tick", _local);
            field("remote_contiguous_input_tick", _contiguous); field("latest_remote_input_tick", _remote);
            field("input_lag_ticks", _lag); field("waiting_for_remote_input", _waiting);
            field("input_wait_count", _count); field("input_wait_seconds", _seconds);
            field("input_wait_current_seconds", _current); field("input_wait_max_seconds", _maximum);
            field("input_progress_age_seconds", _age);
        }
    }

    public static class DashLanDiagnosticReport
    {
        public const string Schema = "dash_arena_v1";
        // Describes existing protocol senders; it never changes fault injection behavior.
        public static string RequiredFaultSender(string profile)
        {
            switch (profile)
            {
                case "none": return "none";
                case "drop-offer":
                case "drop-commit":
                case "drop-start": return "host";
                case "drop-prepared":
                case "drop-commit-ack": return "join";
                case "drop-finish":
                case "drop-finish-ack":
                case "drop-consent":
                case "drop-consent-ack":
                case "drop-startup-input":
                case "drop-startup-ack":
                case "duplicate-reorder":
                case "stale-epoch":
                case "peer-silence": return "both";
                default: return "unknown";
            }
        }

        public static bool HasRequiredFaultInjection(string profile, string role, int injected, bool dropFirst = false)
        {
            if ((role != "host" && role != "join") || injected < 0 || (dropFirst && profile != "none")) return false;
            string sender = dropFirst ? "both" : RequiredFaultSender(profile);
            if (sender == "unknown") return false;
            bool required = sender == "both" || sender == role;
            return required ? injected > 0 : injected == 0;
        }

        public static bool IsActiveStallWait(bool started, bool ended, bool waiting, int producerLastInput,
            int consumerTick, int remoteContiguous, int newestRemote) =>
            started && !ended && waiting && producerLastInput >= 0 &&
            remoteContiguous == producerLastInput && newestRemote == remoteContiguous &&
            consumerTick - remoteContiguous == DashLanSession.MaximumInputLeadTicks;

        public static void Field(StringBuilder report, string name, object value)
        {
            string formatted = value is bool flag ? (flag ? "true" : "false") :
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            report.Append(name).Append('=').AppendLine(formatted.Replace('\r', ' ').Replace('\n', ' '));
        }
    }
}
#endif
