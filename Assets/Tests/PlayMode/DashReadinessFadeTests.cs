using NUnit.Framework;
using UnityEngine;

namespace PocketBump.Tests
{
    public sealed partial class PocketBumpDashPresentationTests
    {
        [Test] public void ReadinessRespectsExistingPerCameraFadeWithoutAddingOpponentFadeTargets()
        {
            // The existing complete authored-view fixture exercises the actual shared
            // fade decision, not a mock visibility flag or arbitrary transparent wall.
            _state = DashArenaSimulation.Create(ArenaRulesProfile.CreateHandfeelCandidate(), 51);
            DashArenaSimulation.StartMatch(_state); _state.Phase = MatchState.Live;
            _state.Players[0].Position = new Vector2(-2, 0); _state.Players[1].Position = new Vector2(2, 0);
            _presentation.BindState(_state, false, 0);
            foreach (Camera camera in _views.GameplayCameras)
            {
                camera.transform.position = new Vector3(0, 10, -10); camera.transform.LookAt(Vector3.zero);
                camera.orthographicSize = 10; camera.aspect = 1;
            }
            _occluder.transform.position = new Vector3(2, 3, -3); _occluder.transform.localScale = new Vector3(2, 7, 2);
            var markers = _hud.GetComponent<DashReadinessHud>();
            int hash = _state.Checksum(); Material original = _occluder.sharedMaterial;
            Assert.That(_presentation.WouldFadeObstacleForCamera(_views.GameplayCameras[0], _occluder), Is.False,
                "An opponent alone must not expand the existing fade rule.");
            markers.Refresh(_state, _presentation, false, 0);
            Assert.That(markers.MarkerFor(0, 1).gameObject.activeSelf, Is.False);
            Assert.That(_presentation.WouldFadeObstacleForCamera(_views.GameplayCameras[1], _occluder), Is.True,
                "The same obstacle already fades for the second viewport's own player.");
            Assert.That(_occluder.sharedMaterial, Is.SameAs(original)); Assert.That(_state.Checksum(), Is.EqualTo(hash));

            _state.Balls[0].Active = true; _state.Balls[0].Position = _state.Players[1].Position;
            _views.Balls[0].Root.position = _views.Players[1].Root.position + Vector3.up * .3f;
            hash = _state.Checksum();
            Assert.That(_presentation.WouldFadeObstacleForCamera(_views.GameplayCameras[0], _occluder), Is.True,
                "An existing active-ball fade already makes this opponent visible.");
            markers.Refresh(_state, _presentation, false, 0);
            Assert.That(markers.MarkerFor(0, 1).gameObject.activeSelf, Is.True);
            Assert.That(_occluder.sharedMaterial, Is.SameAs(original)); Assert.That(_state.Checksum(), Is.EqualTo(hash));
            _presentation.BeginCameraOcclusion(_views.GameplayCameras[0]);
            Assert.That(_occluder.sharedMaterial, Is.Not.SameAs(original), "The query agrees with the actual unchanged render-time fade.");
            _presentation.EndCameraOcclusion(_views.GameplayCameras[0]);
            Assert.That(_occluder.sharedMaterial, Is.SameAs(original));
        }
    }
}
