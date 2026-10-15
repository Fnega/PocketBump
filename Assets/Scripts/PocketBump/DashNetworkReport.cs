using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PocketBump
{
    // A local observation only. Reports never participate in transport or simulation,
    // and do not certify that a match or a physical two-computer test has passed.
    [Serializable]
    public sealed class DashNetworkReport
    {
        public string schema = "pocketbump_lan_release_v1";
        public string evidence_scope = "single_client_observation";
        // Additive evidence fields: readers of legacy identity/counters remain compatible.
        public int evidence_version = 2;
        public string timeline_schema = "pocketbump_network_timeline_v1";
        public bool timeline_frozen;
        public float timeline_sample_interval_seconds = DashNetworkTimeline.SampleIntervalSeconds;
        public float timeline_retention_seconds = DashNetworkTimeline.RetentionSeconds;
        public DashNetworkTimelineSample[] timeline = Array.Empty<DashNetworkTimelineSample>();
        public string report_id, session_started_utc, captured_utc, event_name;
        public string product_name, application_version, build_guid, unity_version;
        public bool development_build;
        public string role, rules_hash, layout_hash, lifecycle, match_phase, rollback_status;
        public int process_id, write_sequence, protocol_version, local_seat, match_id, epoch, simulation_tick, confirmed_state_tick;
        public uint seed;
        public string attempted_endpoint, local_endpoint, remote_endpoint;
        public string failure_reason, initialization_error, socket_error, presentation_error;
        public string failure_origin, peer_failure_reason, last_receive_error;
        public int receive_polls, empty_receive_polls, receive_failures, pump_count;
        public int rejected_endpoints, rejected_controls, rejected_inputs, rejected_histories, rejected_handshakes;
        public float raw_receive_age_seconds = -1f, accepted_peer_age_seconds = -1f;
        public float last_pump_gap_seconds, max_pump_gap_seconds;
        public bool ready, socket_bound, left, waiting_for_remote_input;
        public int send_attempts, sent_datagrams, send_failures, received_datagrams, rejected_datagrams;
        public int accepted_input_datagrams, accepted_controls, controls_sent, heartbeats_sent, heartbeats_received;
        public int latest_local_input_tick, remote_contiguous_input_tick, latest_remote_input_tick, input_lag_ticks;
        public int input_wait_count, validated_checksums, checksum_tick = -1, checksum;
        public int desync_tick = -1, desync_local_checksum, desync_remote_checksum, presentation_error_count;
        public float input_wait_seconds, input_wait_max_seconds, input_progress_age_seconds;
        public int score0, score1;
        public DashNetworkResult[] retained_results = Array.Empty<DashNetworkResult>();

        public static DashNetworkReport Capture(DashMatchDirector director, string eventName)
        {
            if (director == null) throw new ArgumentNullException(nameof(director));
            DashLanSession session = director.LanSession;
            DashArenaState state = director.Sim;
            var result = new DashNetworkReport
            {
                process_id = CurrentProcessId(),
                captured_utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                event_name = eventName ?? "snapshot",
                product_name = Application.productName, application_version = Application.version,
                build_guid = Application.buildGUID, unity_version = Application.unityVersion,
                development_build = Debug.isDebugBuild,
                protocol_version = DashNetcode.Version,
                role = director.Kind == PlayKind.HostLan ? "host" : director.Kind == PlayKind.JoinLan ? "join" : "local",
                local_seat = director.LocalSeat, match_id = director.MatchId, epoch = director.MatchEpoch,
                seed = session?.Seed ?? state?.InitialSeed ?? 0u,
                rules_hash = director.Rules?.ContentHash ?? "", layout_hash = director.Rules?.LayoutHash ?? "",
                simulation_tick = state?.Tick ?? 0, confirmed_state_tick = director.ConfirmedStateTick,
                lifecycle = session?.Lifecycle?.Phase.ToString() ?? "Handshake",
                match_phase = director.State.ToString(), rollback_status = session?.Rollback?.Status.ToString() ?? "Unavailable",
                attempted_endpoint = director.NetworkInitializationEndpoint,
                local_endpoint = session?.LocalEndpoint?.ToString() ?? "",
                remote_endpoint = session?.RemoteEndpoint?.ToString() ?? "",
                failure_reason = director.FailureReason,
                failure_origin = session?.FailureOrigin ?? (director.NetworkPaused ? "local" : "none"),
                peer_failure_reason = session?.PeerFailureReason ?? "",
                receive_polls = session?.ReceivePolls ?? 0, empty_receive_polls = session?.EmptyReceivePolls ?? 0,
                receive_failures = session?.ReceiveFailures ?? 0, last_receive_error = session?.LastReceiveError ?? "",
                pump_count = session?.PumpCount ?? 0, last_pump_gap_seconds = session?.LastPumpGapSeconds ?? 0f,
                max_pump_gap_seconds = session?.MaxPumpGapSeconds ?? 0f,
                raw_receive_age_seconds = session?.RawReceiveAgeSeconds ?? -1f,
                accepted_peer_age_seconds = session?.AcceptedPeerAgeSeconds ?? -1f,
                rejected_endpoints = session?.RejectedEndpointDatagrams ?? 0,
                rejected_controls = session?.RejectedControlDatagrams ?? 0,
                rejected_inputs = session?.RejectedInputDatagrams ?? 0,
                rejected_histories = session?.RejectedHistoryDatagrams ?? 0,
                rejected_handshakes = session?.RejectedHandshakeDatagrams ?? 0,
                initialization_error = director.NetworkInitializationError.Length > 0 ? director.NetworkInitializationError : session?.InitializationError ?? "",
                socket_error = session?.LastSocketError ?? "",
                presentation_error = director.LastPresentationError.Length > 0 ? director.LastPresentationError : session?.LastPresentationError ?? "",
                ready = session?.Ready ?? false, socket_bound = session?.SocketBound ?? false, left = session?.Left ?? false,
                waiting_for_remote_input = director.WaitingForRemoteInput,
                send_attempts = session?.SendAttempts ?? 0, sent_datagrams = session?.SentDatagrams ?? 0,
                send_failures = session?.SendFailures ?? 0, received_datagrams = session?.ReceivedDatagrams ?? 0,
                rejected_datagrams = session?.RejectedDatagrams ?? 0,
                accepted_input_datagrams = session?.AcceptedInputDatagrams ?? 0,
                accepted_controls = session?.AcceptedControls ?? 0, controls_sent = session?.ControlsSent ?? 0,
                heartbeats_sent = session?.HeartbeatsSent ?? 0, heartbeats_received = session?.HeartbeatsReceived ?? 0,
                latest_local_input_tick = session?.LatestLocalInputTick ?? -1,
                remote_contiguous_input_tick = session?.RemoteContiguousInputTick ?? -1,
                latest_remote_input_tick = session?.LatestRemoteInputTick ?? -1,
                input_lag_ticks = session?.InputLagTicks ?? 0, input_wait_count = session?.InputWaitCount ?? 0,
                input_wait_seconds = session?.InputWaitSeconds ?? 0f,
                input_wait_max_seconds = session?.InputWaitMaxSeconds ?? 0f,
                input_progress_age_seconds = session?.InputProgressAgeSeconds ?? 0f,
                validated_checksums = session?.Rollback?.ValidatedChecksumCount ?? 0,
                desync_tick = session?.Rollback?.DesyncTick ?? -1,
                desync_local_checksum = session?.Rollback?.DesyncLocalChecksum ?? 0,
                desync_remote_checksum = session?.Rollback?.DesyncRemoteChecksum ?? 0,
                presentation_error_count = director.PresentationErrorCount + (session?.PresentationErrorCount ?? 0),
                score0 = state?.Scores[0] ?? 0, score1 = state?.Scores[1] ?? 0
            };
            if (session?.Rollback != null)
                session.Rollback.TryGetConfirmedChecksum(state.Tick - 1, out result.checksum_tick, out result.checksum);
            if (session != null)
            {
                result.retained_results = new DashNetworkResult[session.RetainedResults.Count];
                for (int i = 0; i < result.retained_results.Length; i++)
                {
                    DashLanEpochEvidence item = session.RetainedResults[i];
                    result.retained_results[i] = new DashNetworkResult
                    {
                        epoch = item.Epoch, seed = item.Seed, rules_hash = item.ContentHash,
                        finish_tick = item.Finish.FinishTick, checksum = item.Finish.Checksum,
                        score0 = item.Finish.Score0, score1 = item.Finish.Score1, winner = item.Finish.Winner
                    };
                }
            }
            return result;
        }

        static int CurrentProcessId()
        {
            using (var process = System.Diagnostics.Process.GetCurrentProcess()) return process.Id;
        }
    }

    [Serializable]
    public sealed class DashNetworkResult
    {
        public int epoch, finish_tick, checksum, score0, score1, winner;
        public uint seed;
        public string rules_hash;
    }

    // Publish complete generations without replacing existing files. Some player
    // filesystem configurations allow creation/rename but reject metadata-merging
    // replacement. Keep the latest two generations and the first immutable failure.
    public sealed class DashNetworkReportStore
    {
        readonly string _directory;
        readonly string _id = Guid.NewGuid().ToString("N");
        readonly string _started = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        readonly List<string> _snapshots = new List<string>();
        public string LastError { get; private set; } = "";
        public string LatestPath => _snapshots.Count == 0 ? SnapshotPath(1) : _snapshots[_snapshots.Count - 1];
        public string FailurePath => Path.Combine(_directory, _id + "-failure.json");
        public int SuccessfulWrites { get; private set; }

        public DashNetworkReportStore(string directory)
        { _directory = directory ?? throw new ArgumentNullException(nameof(directory)); }

        public bool TryWrite(DashNetworkReport report)
        {
            string pending = "";
            try
            {
                if (report == null) throw new ArgumentNullException(nameof(report));
                report.report_id = _id; report.session_started_utc = _started;
                report.write_sequence = SuccessfulWrites + 1;
                string json = JsonUtility.ToJson(report, true);
                Directory.CreateDirectory(_directory);
                // Keep failure evidence even when a later normal leave updates latest.
                if (!string.IsNullOrEmpty(report.failure_reason) && !File.Exists(FailurePath))
                {
                    pending = FailurePath + ".pending";
                    File.WriteAllText(pending, json, new UTF8Encoding(false));
                    File.Move(pending, FailurePath);
                }
                // An inaccessible old session generation must not prevent the first
                // independent failure report above. Before publishing more session
                // generations, retry pruning: no more than three can accumulate.
                PruneSnapshots();
                string published = SnapshotPath(report.write_sequence);
                pending = published + ".pending";
                File.WriteAllText(pending, json, new UTF8Encoding(false));
                File.Move(pending, published);
                _snapshots.Add(published);
                SuccessfulWrites++;
                PruneSnapshots();
                LastError = "";
                return true;
            }
            catch (Exception error)
            {
                // Reporting must not throw through a network/failure callback, including
                // restricted disk access, invalid paths or JSON serialization failures.
                LastError = error.GetType().Name + ": " + error.Message;
                if (pending.Length != 0)
                {
                    try { if (File.Exists(pending)) File.Delete(pending); }
                    catch (Exception) { /* Preserve the original useful error. */ }
                }
                return false;
            }
        }

        string SnapshotPath(int sequence) => Path.Combine(_directory,
            _id + "-" + sequence.ToString("D10", CultureInfo.InvariantCulture) + "-session.json");

        void PruneSnapshots()
        {
            while (_snapshots.Count > 2)
            {
                // These exact paths were created by this instance; never enumerate
                // or prune another client's or a player's existing report files.
                string oldest = _snapshots[0];
                if (File.Exists(oldest)) File.Delete(oldest);
                _snapshots.RemoveAt(0);
            }
        }
    }

    public static class DashNetworkReports
    {
        public static string DirectoryPath => Path.Combine(Application.persistentDataPath, "NetworkReports");

        public static bool TryOpenDirectory(out string error)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                // Called only by an explicit UI action; no shell command is composed.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = DirectoryPath, UseShellExecute = true });
                error = "";
                return true;
            }
            catch (Exception failure)
            {
                error = "无法打开报告目录：" + failure.Message + "\n" + DirectoryPath;
                return false;
            }
        }
    }
}
