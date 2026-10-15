using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PocketBump
{
    // Small, dependency-free snapshot so a Development player can leave useful evidence after it exits.
    [Serializable]
    public sealed class LanDiagnosticSnapshot
    {
        const int MaxEvents = 8;
        readonly string[] _events = new string[MaxEvents];
        int _eventCount;
        int _nextEvent;
        readonly List<LanEpochDiagnosticSummary> _completedEpochs = new List<LanEpochDiagnosticSummary>();

        public string Role = "unknown";
        public string TerminalReason = "";
        public int SimulationTick = -1;
        public int LastRemoteTick = -1;
        public int LastAckTick = -1;
        public int ReceivedDatagrams;
        public int SendAttempts;
        public int SentDatagrams;
        public int SendFailures;
        public int AcceptedInputDatagrams;
        public int RejectedDatagrams;
        public int RollbackCount;
        public int ValidatedChecksums;
        public int DesyncTick = -1;
        public int DesyncLocalChecksum;
        public int DesyncRemoteChecksum;
        public string LocalEndpoint = "";
        public string RemoteEndpoint = "";
        public string LastSocketError = "";
        public float LastAcceptedPacketAgeSeconds = -1f;
        public string Scenario = "smoke";
        public string Stage = "handshake";
        public int MatchId;
        public int Epoch;
        public string LifecyclePhase = "";
        public int FinishTick = -1;
        public int FinishChecksum;
        public int FinalScore0;
        public int FinalScore1;
        public int Winner = -1;
        public int SettingsHash;
        public float ResultHoldSeconds;
        public int SuccessfulRematches;
        public int HeartbeatsSent;
        public int HeartbeatsReceived;
        public int ControlsSent;
        public int ControlsReceived;
        public string ExpectedDeparture = "";
        public string ObservedDeparture = "";
        public string FinishSource = "normal_gameplay";

        public void RetainEpoch(LanEpochDiagnosticSummary summary)
        {
            if (summary == null) return;
            for (int i = 0; i < _completedEpochs.Count; i++)
            {
                LanEpochDiagnosticSummary existing = _completedEpochs[i];
                if (existing.MatchId != summary.MatchId || existing.Epoch != summary.Epoch) continue;
                _completedEpochs[i] = summary;
                return;
            }
            if (_completedEpochs.Count == 8) _completedEpochs.RemoveAt(0);
            _completedEpochs.Add(summary);
        }

        // A confirmed epoch may be reported before a diagnostic hold completes
        // (for example an expected silent-peer timeout). Preserve the evidence
        // actually reached, including its source and elapsed hold, without implying
        // that the remaining scenario milestones completed.
        public void RetainCurrentConfirmedEpoch(LanConfirmedGameplay gameplay)
        {
            if (FinishTick < 0 || gameplay == null) return;
            RetainEpoch(new LanEpochDiagnosticSummary
            {
                MatchId = MatchId,
                Epoch = Epoch,
                FinishSource = FinishSource,
                FinishTick = FinishTick,
                Checksum = FinishChecksum,
                Score0 = FinalScore0,
                Score1 = FinalScore1,
                Winner = Winner,
                SettingsHash = SettingsHash,
                Collections = gameplay.Collections,
                Shots = gameplay.Shots,
                Collisions = gameplay.Collisions,
                Scores = gameplay.Scores,
                ConfirmedTicks = gameplay.LastTick + 1,
                ValidatedChecksums = ValidatedChecksums,
                HoldSeconds = ResultHoldSeconds
            });
        }

        public void AddEvent(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _events[_nextEvent] = message;
            _nextEvent = (_nextEvent + 1) % MaxEvents;
            _eventCount = Math.Min(_eventCount + 1, MaxEvents);
        }

        public string FormatReport(bool passed, string summary)
        {
            var text = new StringBuilder();
            text.AppendLine("result=" + (passed ? "PASS" : "FAIL"));
            text.AppendLine("role=" + Role);
            text.AppendLine("summary=" + Clean(summary));
            text.AppendLine("terminal_reason=" + Clean(TerminalReason));
            text.AppendLine("simulation_tick=" + SimulationTick);
            text.AppendLine("last_remote_tick=" + LastRemoteTick);
            text.AppendLine("last_ack_tick=" + LastAckTick);
            text.AppendLine("received_datagrams=" + ReceivedDatagrams);
            text.AppendLine("send_attempts=" + SendAttempts);
            text.AppendLine("sent_datagrams=" + SentDatagrams);
            text.AppendLine("send_failures=" + SendFailures);
            text.AppendLine("accepted_input_datagrams=" + AcceptedInputDatagrams);
            text.AppendLine("rejected_datagrams=" + RejectedDatagrams);
            text.AppendLine("protocol_version=" + NetplayProtocol.Version);
            text.AppendLine("rollback_count=" + RollbackCount);
            text.AppendLine("validated_checksums=" + ValidatedChecksums);
            text.AppendLine("desync_tick=" + DesyncTick);
            text.AppendLine("desync_local_checksum=" + DesyncLocalChecksum);
            text.AppendLine("desync_remote_checksum=" + DesyncRemoteChecksum);
            text.AppendLine("local_endpoint=" + Clean(LocalEndpoint));
            text.AppendLine("remote_endpoint=" + Clean(RemoteEndpoint));
            text.AppendLine("last_socket_error=" + Clean(LastSocketError));
            text.AppendLine("last_accepted_packet_age_seconds=" + LastAcceptedPacketAgeSeconds.ToString("0.000"));
            text.AppendLine("scenario=" + Clean(Scenario));
            text.AppendLine("stage=" + Clean(Stage));
            text.AppendLine("connection_id=" + MatchId);
            text.AppendLine("epoch=" + Epoch);
            text.AppendLine("lifecycle_phase=" + Clean(LifecyclePhase));
            text.AppendLine("finish_source=" + Clean(FinishSource));
            text.AppendLine("finish_tick=" + FinishTick);
            text.AppendLine("finish_checksum=" + FinishChecksum);
            text.AppendLine("final_score_0=" + FinalScore0);
            text.AppendLine("final_score_1=" + FinalScore1);
            text.AppendLine("winner=" + Winner);
            text.AppendLine("settings_hash=" + SettingsHash);
            text.AppendLine("result_hold_seconds=" + ResultHoldSeconds.ToString("F3", CultureInfo.InvariantCulture));
            text.AppendLine("successful_rematches=" + SuccessfulRematches);
            text.AppendLine("heartbeats_sent=" + HeartbeatsSent);
            text.AppendLine("heartbeats_received=" + HeartbeatsReceived);
            text.AppendLine("controls_sent=" + ControlsSent);
            text.AppendLine("controls_received=" + ControlsReceived);
            text.AppendLine("expected_departure=" + Clean(ExpectedDeparture));
            text.AppendLine("observed_departure=" + Clean(ObservedDeparture));
            text.AppendLine("completed_epoch_count=" + _completedEpochs.Count);
            for (int i = 0; i < _completedEpochs.Count; i++)
                _completedEpochs[i].AppendReport(text, "completed_" + i + "_");
            for (int i = 0; i < _eventCount; i++)
            {
                int index = (_nextEvent - _eventCount + i + MaxEvents) % MaxEvents;
                text.AppendLine("event_" + i + "=" + Clean(_events[index]));
            }
            return text.ToString();
        }

        static string Clean(string value)
        {
            return (value ?? "").Replace('\r', ' ').Replace('\n', ' ');
        }
    }

    [Serializable]
    public sealed class LanEpochDiagnosticSummary
    {
        public int MatchId;
        public int Epoch;
        public string FinishSource = "normal_gameplay";
        public int FinishTick = -1;
        public int Checksum;
        public int Score0;
        public int Score1;
        public int Winner = -1;
        public int SettingsHash;
        public int Collections;
        public int Shots;
        public int Collisions;
        public int Scores;
        public int ConfirmedTicks;
        public int ValidatedChecksums;
        public float HoldSeconds;

        public void AppendReport(StringBuilder report, string prefix)
        {
            report.AppendLine(prefix + "connection_id=" + MatchId);
            report.AppendLine(prefix + "epoch=" + Epoch);
            report.AppendLine(prefix + "finish_source=" + FinishSource);
            report.AppendLine(prefix + "finish_tick=" + FinishTick);
            report.AppendLine(prefix + "checksum=" + Checksum);
            report.AppendLine(prefix + "score_0=" + Score0);
            report.AppendLine(prefix + "score_1=" + Score1);
            report.AppendLine(prefix + "winner=" + Winner);
            report.AppendLine(prefix + "settings_hash=" + SettingsHash);
            report.AppendLine(prefix + "collections=" + Collections);
            report.AppendLine(prefix + "shots=" + Shots);
            report.AppendLine(prefix + "dynamic_collisions=" + Collisions);
            report.AppendLine(prefix + "score_events=" + Scores);
            report.AppendLine(prefix + "confirmed_ticks=" + ConfirmedTicks);
            report.AppendLine(prefix + "validated_checksums=" + ValidatedChecksums);
            report.AppendLine(prefix + "result_hold_seconds=" + HoldSeconds.ToString("F3", CultureInfo.InvariantCulture));
        }
    }

    // Receives only reconciled, input-complete ticks. The caller owns the epoch.
    // Rejecting duplicates prevents repeated terminal polling or replay from
    // turning one release/collision/score into multiple gameplay milestones.
    public sealed class LanConfirmedGameplay
    {
        public int LastTick { get; private set; } = -1;
        public int Collections { get; private set; }
        public int Shots { get; private set; }
        public int Collisions { get; private set; }
        public int Scores { get; private set; }
        public int Winners { get; private set; }
        public bool HasGameplayMilestones => Collections > 0 && Shots > 0 && Collisions > 0 && Scores > 0 && Winners > 0;

        public bool Commit(int tick, SimEvents events)
        {
            if (tick <= LastTick) return false;
            LastTick = tick;
            if (events.Collected0) Collections++;
            if (events.Collected1) Collections++;
            if (events.Fired0) Shots++;
            if (events.Fired1) Shots++;
            if (events.DangerousImpact) Collisions++;
            if (events.Scored) Scores++;
            if (events.MatchOver) Winners++;
            return true;
        }
    }
}
