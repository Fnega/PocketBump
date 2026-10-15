using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class PocketBumpDiagnosticsAndDisplayTests
    {
        const string DisplayKey = "PocketBump.DisplayMode";

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(DisplayKey);
            PlayerPrefs.Save();
            DisplayModeSettings.ResetForTests();
        }

        [Test]
        public void DiagnosticReportContainsTerminalReasonCountersAndEvents()
        {
            var diagnostics = new LanDiagnosticSnapshot
            {
                Role = "join",
                TerminalReason = "INPUT HOLE",
                SimulationTick = 81,
                LastRemoteTick = 67,
                LastAckTick = 65,
                ReceivedDatagrams = 42,
                AcceptedInputDatagrams = 36,
                RejectedDatagrams = 6,
                RollbackCount = 4,
                ValidatedChecksums = 25,
                DesyncTick = 72,
                DesyncLocalChecksum = 123,
                DesyncRemoteChecksum = 456,
                LastAcceptedPacketAgeSeconds = 0.25f
            };
            diagnostics.AddEvent("handshake-complete");
            diagnostics.AddEvent("abort-INPUT HOLE");

            string report = diagnostics.FormatReport(false, "INPUT HOLE");

            Assert.That(report, Does.Contain("result=FAIL"));
            Assert.That(report, Does.Contain("role=join"));
            Assert.That(report, Does.Contain("terminal_reason=INPUT HOLE"));
            Assert.That(report, Does.Contain("received_datagrams=42"));
            Assert.That(report, Does.Contain("send_attempts=0"));
            Assert.That(report, Does.Contain("local_endpoint="));
            Assert.That(report, Does.Contain("rollback_count=4"));
            Assert.That(report, Does.Contain("validated_checksums=25"));
            Assert.That(report, Does.Contain("desync_tick=72"));
            Assert.That(report, Does.Contain("desync_local_checksum=123"));
            Assert.That(report, Does.Contain("desync_remote_checksum=456"));
            Assert.That(report, Does.Contain("event_1=abort-INPUT HOLE"));
        }

        [Test]
        public void UnconfiguredPeerReportsAClassifiedSendFailure()
        {
            using (var peer = new LanUdpPeer())
            {
                Assert.That(peer.Send(new byte[] { 1 }), Is.False);
                Assert.That(peer.SendAttempts, Is.EqualTo(1));
                Assert.That(peer.SentDatagrams, Is.EqualTo(0));
                Assert.That(peer.SendFailures, Is.EqualTo(1));
                Assert.That(peer.LastSocketError, Is.EqualTo("SOCKET_NOT_BOUND"));
            }
        }

        [Test]
        public void DisplayModeDefaultsToWindowedAndPersistsSelection()
        {
            PlayerPrefs.DeleteKey(DisplayKey);
            Assert.That(DisplayModeSettings.Load(), Is.EqualTo(DisplayModeChoice.Windowed));

            DisplayModeSettings.Save(DisplayModeChoice.Fullscreen);
            Assert.That(DisplayModeSettings.Load(), Is.EqualTo(DisplayModeChoice.Fullscreen));

            DisplayModeSettings.Save(DisplayModeChoice.Windowed);
            Assert.That(DisplayModeSettings.Load(), Is.EqualTo(DisplayModeChoice.Windowed));
        }
    }
}
