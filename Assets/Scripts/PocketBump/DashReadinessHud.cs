using System;
using System.Collections.Generic;
using UnityEngine;

namespace PocketBump
{
    // Run after the director interpolates actor roots and the HUD lays out Regions.
    // Local candidates use shared ground geometry; LAN retains per-view overlays.
    [DefaultExecutionOrder(200)]
    public sealed class DashReadinessHud : MonoBehaviour
    {
        [Serializable] public sealed class ViewMarkers { public DashReadinessGraphic[] Actors = new DashReadinessGraphic[2]; }
        [SerializeField] DashMatchHud _hud;
        [SerializeField] ViewMarkers[] _views = { new ViewMarkers(), new ViewMarkers() };
        [SerializeField] DashGroundReadinessRing[] _localGroundRings = new DashGroundReadinessRing[2];
        [SerializeField] Renderer[] _identityFootprints = new Renderer[2];
        readonly bool[] _footprintWasEnabled = new bool[2];
        bool _footprintsCaptured, _localGroundRingsActive;
        DashArenaPresentation _occlusionSource;
        readonly List<Renderer> _occluders = new List<Renderer>();
        readonly Renderer[][] _actorGeometry = new Renderer[2][];
        public DashMatchHud Hud => _hud;
        public DashReadinessGraphic MarkerFor(int viewingSeat, int actor) => _views[viewingSeat].Actors[actor];
        public bool HasLocalGroundRings => _localGroundRings != null && _localGroundRings.Length == 2 &&
            _localGroundRings[0] != null && _localGroundRings[1] != null && _localGroundRings[0] != _localGroundRings[1];
        public bool LocalGroundRingsActive => _localGroundRingsActive;
        public DashGroundReadinessRing GroundRingFor(int actor) => _localGroundRings[actor];

        // Explicit scene-authoring integration. Existing UI objects/styles are kept
        // because LAN still uses precisely the earlier per-view representation.
        public bool InstallLocalGroundRings(DashGroundReadinessRing[] rings, Renderer[] footprints)
        {
            if (rings == null || rings.Length != 2 || rings[0] == null || rings[1] == null || rings[0] == rings[1])
                throw new ArgumentException("Exactly two different authored local ground rings are required.", nameof(rings));
            if (footprints == null || footprints.Length != 2)
                throw new ArgumentException("Two identity-footprint slots are required; an absent authored footprint may be null.", nameof(footprints));
            if (HasLocalGroundRings && _identityFootprints != null && _identityFootprints.Length == 2 &&
                _localGroundRings[0] == rings[0] && _localGroundRings[1] == rings[1] &&
                _identityFootprints[0] == footprints[0] && _identityFootprints[1] == footprints[1]) return false;
            HideGroundRings();
            _localGroundRings = (DashGroundReadinessRing[])rings.Clone();
            _identityFootprints = (Renderer[])footprints.Clone();
            return true;
        }

        // Explicit authoring only. Not called from Awake, Bind, Refresh or play init.
        public static bool Install(DashMatchHud hud)
        {
            if (hud == null || hud.Seats == null || hud.Seats.Length != 2) throw new ArgumentException("An existing two-seat HUD is required.");
            var binding = hud.GetComponent<DashReadinessHud>();
            bool changed = binding == null;
            if (binding == null) { binding = hud.gameObject.AddComponent<DashReadinessHud>(); binding._hud = hud; }
            if (binding._hud == null) { binding._hud = hud; changed = true; }
            for (int seat = 0; seat < 2; seat++)
                for (int actor = 0; actor < 2; actor++)
                {
                    if (binding._views[seat].Actors[actor] != null) continue;
                    RectTransform region = hud.Seats[seat].Region;
                    string name = "DashReadiness" + actor;
                    Transform existing = region.Find(name);
                    DashReadinessGraphic marker;
                    if (existing != null)
                    {
                        marker = existing.GetComponent<DashReadinessGraphic>();
                        if (marker == null) throw new InvalidOperationException("An unrelated authored object already uses " + name + "; it was not replaced.");
                    }
                    else
                    {
                        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(region, false);
                        go.transform.SetAsFirstSibling();
                        marker = go.AddComponent<DashReadinessGraphic>();
                        marker.RectTransform.anchorMin = marker.RectTransform.anchorMax = new Vector2(actor == seat ? .4f : .65f, .4f);
                        marker.RectTransform.sizeDelta = actor == seat ? new Vector2(56f, 34f) : new Vector2(28f, 28f);
                        marker.IdentityColor = hud.PlayerIdentityColor(actor);
                        marker.DetailedPreview = actor == seat;
                    }
                    binding._views[seat].Actors[actor] = marker; changed = true;
                }
            return changed;
        }
        void LateUpdate()
        {
            DashMatchDirector director = _hud != null ? _hud.Director : null;
            if (director == null) { HideAll(); return; }
            Refresh(director.Sim, director.Presentation, director.Kind != PlayKind.Local, director.LocalSeat);
        }
        void OnDisable()
        {
            // Editor reload/unload must not persistently hide the authored ground
            // renderers. An explicitly activated preview still restores footprints.
            if (Application.isPlaying || _localGroundRingsActive) HideGroundRings();
            HideOverlayMarkers();
        }
        void HideAll()
        {
            HideGroundRings();
            HideOverlayMarkers();
        }
        void HideOverlayMarkers()
        {
            foreach (ViewMarkers view in _views)
                foreach (DashReadinessGraphic marker in view.Actors)
                    if (marker != null) marker.gameObject.SetActive(false);
        }
        void HideGroundRings()
        {
            _localGroundRingsActive = false;
            if (_localGroundRings != null)
                foreach (DashGroundReadinessRing ring in _localGroundRings)
                    if (ring != null) ring.Hide();
            if (!_footprintsCaptured) return;
            for (int actor = 0; actor < 2; actor++)
                if (_identityFootprints[actor] != null) _identityFootprints[actor].enabled = _footprintWasEnabled[actor];
            _footprintsCaptured = false;
        }
        void RefreshGroundRings(DashArenaState state, DashArenaPresentation presentation)
        {
            HideOverlayMarkers();
            _localGroundRingsActive = true;
            if (!_footprintsCaptured)
            {
                for (int actor = 0; actor < 2; actor++)
                    _footprintWasEnabled[actor] = _identityFootprints[actor] != null && _identityFootprints[actor].enabled;
                _footprintsCaptured = true;
            }
            for (int actor = 0; actor < 2; actor++)
            {
                if (_identityFootprints[actor] != null) _identityFootprints[actor].enabled = false;
                Transform root = presentation.Views.Players[actor].Root;
                if (root == null || !root.gameObject.activeInHierarchy) { _localGroundRings[actor].Hide(); continue; }
                // Local-only approved information policy: both cameras see the same
                // detailed world rings, including the opponent's remaining cooldown.
                _localGroundRings[actor].ApplyState(DashReadinessState.Evaluate(state, actor, actor), root.position);
            }
        }
        public void Refresh(DashArenaState state, DashArenaPresentation presentation, bool lan, int localSeat)
        {
            if (state == null || !state.Rules.UsesRefinedHandfeel || presentation == null || presentation.Views == null || _hud == null)
            { HideAll(); return; }
            if (!lan && HasLocalGroundRings)
            {
                RefreshGroundRings(state, presentation);
                return;
            }
            HideGroundRings();
            CaptureOccluders(presentation);
            var references = presentation.Views;
            for (int seat = 0; seat < 2; seat++)
                for (int actor = 0; actor < 2; actor++)
                {
                    DashReadinessGraphic marker = MarkerFor(seat, actor);
                    if (marker == null) continue;
                    DashReadinessState shown = DashReadinessState.Evaluate(state, actor, seat);
                    marker.SetState(shown); // Same-tick corrections are never cached away.
                    Camera camera = references.GameplayCameras[lan ? 0 : seat];
                    Transform root = references.Players[actor].Root;
                    Vector3 at = camera != null && root != null ? camera.WorldToViewportPoint(root.position) : Vector3.back;
                    bool visible = (!lan || seat == localSeat) && shown.Kind != DashReadinessKind.Hidden && camera != null && camera.enabled &&
                        root != null && root.gameObject.activeInHierarchy && at.z > 0f && at.x >= 0f && at.x <= 1f && at.y >= 0f && at.y <= 1f &&
                        BodyCanRender(camera, actor, root);
                    if (visible && actor != seat) visible = !FullyOccluded(camera, root.position, state.Rules.PlayerRadius);
                    marker.gameObject.SetActive(visible);
                    if (!visible) continue;
                    marker.RectTransform.anchorMin = marker.RectTransform.anchorMax = new Vector2(at.x, at.y);
                    marker.RectTransform.anchoredPosition = marker.FootOffset;
                }
        }
        void CaptureOccluders(DashArenaPresentation presentation)
        {
            if (_occlusionSource == presentation) return;
            _occlusionSource = presentation; _occluders.Clear();
            var refs = presentation.Views;
            for (int actor = 0; actor < 2; actor++)
            {
                Transform body = refs.Players[actor].Motion != null ? refs.Players[actor].Motion : refs.Players[actor].Root;
                _actorGeometry[actor] = body != null ? body.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            }
            if (refs.Pillars != null) foreach (var pillar in refs.Pillars)
                if (pillar?.Root != null) foreach (Renderer renderer in pillar.Root.GetComponentsInChildren<Renderer>(true))
                    if (!(renderer is LineRenderer) && (pillar.Bell == null || !renderer.transform.IsChildOf(pillar.Bell))) _occluders.Add(renderer);
            if (refs.Walls != null) foreach (var wall in refs.Walls)
                if (wall?.Solid != null) foreach (Renderer renderer in wall.Solid.GetComponentsInChildren<Renderer>(true))
                    if (!(renderer is LineRenderer)) _occluders.Add(renderer);
            if (refs.Gate != null) _occluders.AddRange(refs.Gate.GetComponentsInChildren<Renderer>(true));
        }
        bool BodyCanRender(Camera camera, int actor, Transform root)
        {
            Renderer[] geometry = _actorGeometry[actor];
            if (geometry == null || geometry.Length == 0) return (camera.cullingMask & (1 << root.gameObject.layer)) != 0;
            foreach (Renderer renderer in geometry)
                if (renderer != null && !(renderer is LineRenderer) && renderer.enabled && renderer.gameObject.activeInHierarchy &&
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0) return true;
            return false;
        }
        bool FullyOccluded(Camera camera, Vector3 feet, float radius)
        {
            // Nine body probes prevent a single foot ray from hiding a partly visible
            // robot. Renderers are tested per camera; no Collider or isVisible flag.
            for (int row = 0; row < 3; row++)
                for (int side = -1; side <= 1; side++)
                {
                    Vector3 target = feet + Vector3.up * (.25f + row * .72f) + camera.transform.right * (side * radius * .65f);
                    Vector3 at = camera.WorldToViewportPoint(target);
                    if (at.z <= 0f || at.x < 0f || at.x > 1f || at.y < 0f || at.y > 1f) continue;
                    Ray ray = camera.ViewportPointToRay(at);
                    float depth = Vector3.Dot(target - ray.origin, ray.direction);
                    bool covered = false;
                    foreach (Renderer blocker in _occluders)
                    {
                        if (blocker == null || !blocker.enabled || !blocker.gameObject.activeInHierarchy ||
                            (camera.cullingMask & (1 << blocker.gameObject.layer)) == 0) continue;
                        if (_occlusionSource.WouldFadeObstacleForCamera(camera, blocker)) continue;
                        if (blocker.bounds.IntersectRay(ray, out float hit) && hit < depth - .08f) { covered = true; break; }
                    }
                    if (!covered) return false;
                }
            return true;
        }
    }
}
