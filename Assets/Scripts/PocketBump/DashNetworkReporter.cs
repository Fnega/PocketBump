using System;
using UnityEngine;

namespace PocketBump
{
    // Present in Release. Observes the ordinary director; contains no automation,
    // fault injection, alternate inputs or authority-changing operations.
    [DisallowMultipleComponent]
    public sealed class DashNetworkReporter : MonoBehaviour
    {
        public const float SnapshotIntervalSeconds = 5f;
        DashMatchDirector _director;
        DashNetworkReportStore _store;
        readonly DashNetworkTimeline _timeline = new DashNetworkTimeline();
        Func<DashNetworkTimelineSample> _captureSample;
        float _nextWrite;
        int _epoch = -1, _results = -1;
        string _phase = "";
        bool _observed, _terminalCaptured, _finished;
        public string LastWriteError { get; private set; } = "";
        public string LastReportPath => _store?.LatestPath ?? "";
        public int SuccessfulWrites => _store?.SuccessfulWrites ?? 0;

        // A supplied output directory lets isolated tests avoid touching player reports.
        // Production integration calls Bind(director) with the normal local directory.
        public void Bind(DashMatchDirector director, string reportDirectory = null)
        {
            if (_director != null) return;
            _director = director != null ? director : throw new ArgumentNullException(nameof(director));
            _store = new DashNetworkReportStore(reportDirectory ?? DashNetworkReports.DirectoryPath);
            _captureSample = () => DashNetworkTimelineSample.Capture(_director);
        }

        void LateUpdate() => Observe(Time.unscaledTime);

        public void Observe(float unscaledSeconds)
        {
            if (_finished || _director == null || !_director.Initialized || _director.Kind == PlayKind.Local) return;
            string phase = _director.Lifecycle?.Phase.ToString() ?? "Handshake";
            int results = _director.LanSession?.RetainedResults.Count ?? 0;
            bool terminal = _director.NetworkPaused;
            try { _timeline.Observe(unscaledSeconds, _captureSample, terminal); }
            catch (Exception error) { LastWriteError = error.GetType().Name + ": " + error.Message; return; }
            bool transition = _epoch != _director.MatchEpoch || _phase != phase || _results != results;
            if (!_observed || (terminal && !_terminalCaptured) || transition || unscaledSeconds >= _nextWrite)
            {
                string reason = terminal ? "failure" : !_observed ? "session_started" : transition ? "session_transition" : "periodic";
                CaptureNow(reason, unscaledSeconds);
                _observed = true; _epoch = _director.MatchEpoch; _phase = phase; _results = results;
                // Retry unavailable storage at the bounded interval, not each frame.
                _terminalCaptured = terminal;
                _nextWrite = unscaledSeconds + SnapshotIntervalSeconds;
            }
        }

        public bool CaptureNow(string eventName) => CaptureNow(eventName, Time.unscaledTime);

        bool CaptureNow(string eventName, float observedAt)
        {
            if (_director == null || !_director.Initialized || _director.Kind == PlayKind.Local || _store == null) return false;
            try
            {
                bool final = eventName == "leave" || eventName == "application_quit";
                _timeline.Observe(observedAt, _captureSample, _director.NetworkPaused, final);
                DashNetworkReport report = DashNetworkReport.Capture(_director, eventName);
                report.timeline = _timeline.Snapshot();
                report.timeline_frozen = _timeline.Frozen;
                bool written = _store.TryWrite(report);
                LastWriteError = _store.LastError;
                if (eventName == "leave" || eventName == "application_quit") _finished = true;
                return written;
            }
            catch (Exception error)
            {
                LastWriteError = error.GetType().Name + ": " + error.Message;
                return false;
            }
        }

        void OnApplicationQuit() { if (!_finished) CaptureNow("application_quit"); }
        void OnDisable() { if (!_finished) CaptureNow("observer_disabled"); }
    }
}
