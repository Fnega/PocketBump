using System;
using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed class DashNetworkTimelineTests
    {
        [Test]
        public void SamplingBoundsFactoryCallsAndRetainsRecentTwentySecondsInOrder()
        {
            var timeline = new DashNetworkTimeline();
            int captures = 0;
            Func<DashNetworkTimelineSample> capture = () => new DashNetworkTimelineSample { simulation_tick = ++captures };
            for (int i = 0; i <= 1000; i++) timeline.Observe(i / 16f, capture);
            Assert.That(captures, Is.EqualTo(251), "Only four samples per second, not one allocation per observed frame.");
            Assert.That(timeline.Count, Is.EqualTo(DashNetworkTimeline.Capacity));
            DashNetworkTimelineSample[] snapshot = timeline.Snapshot();
            Assert.That(snapshot[0].elapsed_seconds, Is.EqualTo(42.5f));
            Assert.That(snapshot[snapshot.Length - 1].elapsed_seconds, Is.EqualTo(62.5f));
            for (int i = 1; i < snapshot.Length; i++)
                Assert.That(snapshot[i].simulation_tick, Is.EqualTo(snapshot[i - 1].simulation_tick + 1));
        }

        [Test]
        public void TerminalSampleIsImmediateAndFrozenAgainstSubsequentFaultScreenFrames()
        {
            var timeline = new DashNetworkTimeline();
            var sample = new DashNetworkTimelineSample { received_datagrams = 10 };
            timeline.Observe(10f, () => sample);
            sample.failure_reason = "PEER TIMEOUT"; sample.input_progress_age_seconds = 5.01f;
            Assert.That(timeline.Observe(10.1f, () => sample, terminal: true), Is.True);
            string frozen = JsonUtility.ToJson(new DashNetworkReport { timeline = timeline.Snapshot() });
            for (int i = 1; i < 1000; i++)
                Assert.That(timeline.Observe(10.1f + i, () => { Assert.Fail("Frozen timeline sampled again."); return sample; }, true, true), Is.False);
            Assert.That(timeline.Frozen, Is.True);
            Assert.That(timeline.Count, Is.EqualTo(2));
            Assert.That(JsonUtility.ToJson(new DashNetworkReport { timeline = timeline.Snapshot() }), Is.EqualTo(frozen));
        }

        [Test]
        public void SnapshotsAndSourceInstancesCannotMutateRetainedEvidence()
        {
            var timeline = new DashNetworkTimeline();
            var original = new DashNetworkTimelineSample { simulation_tick = 55, last_receive_error = "RECEIVE: NetworkDown" };
            timeline.Observe(0f, () => original);
            original.simulation_tick = 99;
            DashNetworkTimelineSample[] exported = timeline.Snapshot();
            exported[0].simulation_tick = 300;
            Assert.That(timeline.Snapshot()[0].simulation_tick, Is.EqualTo(55));
            Assert.That(timeline.Snapshot()[0].last_receive_error, Is.EqualTo("RECEIVE: NetworkDown"));
        }

        [Test]
        public void GapsAreRecordedWithoutInventingSamplesDuringAnUnobservedStall()
        {
            var timeline = new DashNetworkTimeline();
            Func<DashNetworkTimelineSample> capture = () => new DashNetworkTimelineSample();
            timeline.Observe(0f, capture);
            timeline.Observe(.125f, capture);
            timeline.Observe(.25f, capture);
            timeline.Observe(3.25f, capture);
            DashNetworkTimelineSample[] result = timeline.Snapshot();
            Assert.That(result.Length, Is.EqualTo(3));
            Assert.That(result[1].observation_gap_max_seconds, Is.EqualTo(.125f));
            Assert.That(result[2].observation_gap_max_seconds, Is.EqualTo(3f));
            timeline.Observe(30f, capture);
            Assert.That(timeline.Count, Is.EqualTo(1), "Expired observations are removed rather than implying continuous coverage.");
        }

        [Test]
        public void InvalidOrReversedObservationTimeDoesNotCorruptChronology()
        {
            var timeline = new DashNetworkTimeline();
            Func<DashNetworkTimelineSample> capture = () => new DashNetworkTimelineSample();
            Assert.That(timeline.Observe(float.NaN, capture), Is.False);
            Assert.That(timeline.Observe(float.PositiveInfinity, capture), Is.False);
            Assert.That(timeline.Observe(-1f, capture), Is.False);
            timeline.Observe(10f, capture);
            Assert.That(timeline.Observe(9f, capture), Is.False);
            timeline.Observe(10.25f, capture);
            Assert.That(timeline.Snapshot()[1].elapsed_seconds, Is.EqualTo(.25f));
        }

        [Test]
        public void EmptyTimelineIsExplicitAndNewEvidenceRoundTripsWithoutChangingLegacyFields()
        {
            var timeline = new DashNetworkTimeline();
            Assert.That(timeline.Snapshot(), Is.Empty);
            timeline.Observe(0f, () => new DashNetworkTimelineSample
            {
                receive_failures = 3, pump_count = 20, failure_origin = "peer", peer_failure_reason = "PEER TIMEOUT",
                rejected_endpoints = 2, raw_receive_age_seconds = .7f, accepted_peer_age_seconds = 5.01f
            }, true);
            var report = new DashNetworkReport { match_id = 51, failure_reason = "PEER FAULT", timeline = timeline.Snapshot(), timeline_frozen = true };
            var restored = JsonUtility.FromJson<DashNetworkReport>(JsonUtility.ToJson(report));
            Assert.That(restored.schema, Is.EqualTo("pocketbump_lan_release_v1"));
            Assert.That(restored.evidence_version, Is.EqualTo(2));
            Assert.That(restored.timeline_schema, Is.EqualTo("pocketbump_network_timeline_v1"));
            Assert.That(restored.match_id, Is.EqualTo(51));
            Assert.That(restored.timeline_frozen, Is.True);
            Assert.That(restored.timeline[0].receive_failures, Is.EqualTo(3));
            Assert.That(restored.timeline[0].peer_failure_reason, Is.EqualTo("PEER TIMEOUT"));
            Assert.That(restored.timeline[0].accepted_peer_age_seconds, Is.EqualTo(5.01f));
        }
    }
}
