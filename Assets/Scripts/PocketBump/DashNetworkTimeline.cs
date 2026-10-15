using System;

namespace PocketBump
{
    // Diagnostic observations only: no input bytes, authority, or simulation references.
    [Serializable]
    public sealed class DashNetworkTimelineSample
    {
        public float elapsed_seconds, observation_gap_max_seconds;
        public int epoch, simulation_tick, confirmed_state_tick;
        public int send_attempts, sent_datagrams, send_failures, received_datagrams;
        public int receive_polls, empty_receive_polls, receive_failures;
        public int accepted_inputs, accepted_controls, rejected_datagrams;
        public int rejected_endpoints, rejected_controls, rejected_inputs, rejected_histories, rejected_handshakes;
        public int heartbeats_sent, heartbeats_received, pump_count;
        public int latest_local_input_tick, remote_contiguous_input_tick, latest_remote_input_tick;
        public float raw_receive_age_seconds = -1f, accepted_peer_age_seconds = -1f;
        public float input_progress_age_seconds, last_pump_gap_seconds, max_pump_gap_seconds;
        public bool waiting_for_remote_input, socket_bound;
        public string lifecycle, failure_reason, failure_origin, peer_failure_reason, last_receive_error;

        public static DashNetworkTimelineSample Capture(DashMatchDirector director)
        {
            DashLanSession session = director.LanSession;
            return new DashNetworkTimelineSample
            {
                epoch = director.MatchEpoch, simulation_tick = director.Sim?.Tick ?? 0,
                confirmed_state_tick = director.ConfirmedStateTick,
                send_attempts = session?.SendAttempts ?? 0, sent_datagrams = session?.SentDatagrams ?? 0,
                send_failures = session?.SendFailures ?? 0, received_datagrams = session?.ReceivedDatagrams ?? 0,
                receive_polls = session?.ReceivePolls ?? 0, empty_receive_polls = session?.EmptyReceivePolls ?? 0,
                receive_failures = session?.ReceiveFailures ?? 0, last_receive_error = session?.LastReceiveError ?? "",
                accepted_inputs = session?.AcceptedInputDatagrams ?? 0, accepted_controls = session?.AcceptedControls ?? 0,
                rejected_datagrams = session?.RejectedDatagrams ?? 0,
                rejected_endpoints = session?.RejectedEndpointDatagrams ?? 0,
                rejected_controls = session?.RejectedControlDatagrams ?? 0,
                rejected_inputs = session?.RejectedInputDatagrams ?? 0,
                rejected_histories = session?.RejectedHistoryDatagrams ?? 0,
                rejected_handshakes = session?.RejectedHandshakeDatagrams ?? 0,
                heartbeats_sent = session?.HeartbeatsSent ?? 0, heartbeats_received = session?.HeartbeatsReceived ?? 0,
                pump_count = session?.PumpCount ?? 0, last_pump_gap_seconds = session?.LastPumpGapSeconds ?? 0f,
                max_pump_gap_seconds = session?.MaxPumpGapSeconds ?? 0f,
                raw_receive_age_seconds = session?.RawReceiveAgeSeconds ?? -1f,
                accepted_peer_age_seconds = session?.AcceptedPeerAgeSeconds ?? -1f,
                input_progress_age_seconds = session?.InputProgressAgeSeconds ?? 0f,
                latest_local_input_tick = session?.LatestLocalInputTick ?? -1,
                remote_contiguous_input_tick = session?.RemoteContiguousInputTick ?? -1,
                latest_remote_input_tick = session?.LatestRemoteInputTick ?? -1,
                waiting_for_remote_input = director.WaitingForRemoteInput,
                socket_bound = session?.SocketBound ?? false,
                lifecycle = session?.Lifecycle?.Phase.ToString() ?? "Handshake",
                failure_reason = director.FailureReason,
                failure_origin = session?.FailureOrigin ?? (director.NetworkPaused ? "local" : "none"),
                peer_failure_reason = session?.PeerFailureReason ?? ""
            };
        }

        public DashNetworkTimelineSample Copy() => (DashNetworkTimelineSample)MemberwiseClone();
    }

    public sealed class DashNetworkTimeline
    {
        public const int Capacity = 81;
        public const float SampleIntervalSeconds = .25f, RetentionSeconds = 20f;
        readonly DashNetworkTimelineSample[] _samples = new DashNetworkTimelineSample[Capacity];
        int _head, _count;
        float _origin = -1f, _lastObserved = -1f, _nextSample, _gapMaximum;
        public bool Frozen { get; private set; }
        public int Count => _count;

        // Called once per rendered observation as well as for an explicit final capture.
        // The factory is evaluated only when due; no per-frame sample allocations.
        public bool Observe(float now, Func<DashNetworkTimelineSample> capture, bool terminal = false, bool force = false)
        {
            if (Frozen || float.IsNaN(now) || float.IsInfinity(now) || now < 0f) return false;
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            if (_origin < 0f) _origin = now;
            now = Math.Max(now, _lastObserved);
            if (_lastObserved >= 0f) _gapMaximum = Math.Max(_gapMaximum, now - _lastObserved);
            _lastObserved = now;
            if (_count > 0 && !force && !terminal && now < _nextSample) return false;
            DashNetworkTimelineSample sample = (capture() ?? throw new InvalidOperationException("Missing diagnostic sample.")).Copy();
            sample.elapsed_seconds = now - _origin;
            sample.observation_gap_max_seconds = _gapMaximum;
            while (_count > 0 && _samples[_head].elapsed_seconds < sample.elapsed_seconds - RetentionSeconds)
            {
                _samples[_head] = null; _head = (_head + 1) % Capacity; _count--;
            }
            if (_count == Capacity)
            {
                _samples[_head] = null; _head = (_head + 1) % Capacity; _count--;
            }
            _samples[(_head + _count) % Capacity] = sample; _count++;
            _nextSample = now + SampleIntervalSeconds; _gapMaximum = 0f;
            Frozen = terminal;
            return true;
        }

        public DashNetworkTimelineSample[] Snapshot()
        {
            var copy = new DashNetworkTimelineSample[_count];
            for (int i = 0; i < _count; i++) copy[i] = _samples[(_head + i) % Capacity].Copy();
            return copy;
        }
    }
}
