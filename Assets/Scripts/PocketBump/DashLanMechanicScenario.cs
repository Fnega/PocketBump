#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PocketBump
{
    // Explicit pre-baseline mechanism fixtures, NEVER natural-match evidence.
    // Runtime interaction thereafter is ordinary movement/Dash through UDP/rollback.
    public sealed class DashLanMechanicScenario
    {
        public static readonly string[] Names = { "stun-recovery", "green-impact", "purple-immunity", "blue-teleport",
            "blue-no-safe", "fall-respawn", "wall-player-repair", "wall-ball-repair" };
        public const uint FixtureSeed = 0x0d45aa11u;
        public string Name { get; }
        public int Seat { get; }
        public string FixtureId => "dash_mechanic_v1_" + Name + "_seat_" + Seat;
        public ArenaRulesProfile Rules { get; }
        public int InitialChecksum { get; private set; }
        public string[] RequiredMilestones { get; }
        public IReadOnlyList<DashMechanicMilestone> Milestones => _milestones.AsReadOnly();
        public string InvalidReason => _scoreChanged ? "mechanic_score_changed" : "";
        public bool Passed => InvalidReason.Length == 0 && MissingMilestones.Length == 0;
        public string MissingMilestones
        {
            get
            {
                var missing = new List<string>();
                foreach (string name in RequiredMilestones) if (!_names.Contains(name)) missing.Add(name);
                return string.Join(",", missing);
            }
        }
        readonly List<DashMechanicMilestone> _milestones = new List<DashMechanicMilestone>();
        readonly HashSet<string> _names = new HashSet<string>();
        DashArenaState _previous;
        int _lastConfirmedTick = -1, _stunTick = -1, _fallTick = -1, _brokenTick = -1, _pendingTick = -1;
        bool _scoreChanged;
        public float EnhancedBallSpeed { get; private set; }
        public float UnbuffedCounterfactualSpeed { get; private set; }
        public int ComparisonTick { get; private set; } = -1;
        public byte ComparisonInput0 { get; private set; }
        public byte ComparisonInput1 { get; private set; }

        public DashLanMechanicScenario(string name, int seat)
        {
            if (Array.IndexOf(Names, name) < 0) throw new ArgumentException("Unknown mechanic fixture: " + name);
            if (seat < 0 || seat > 1) throw new ArgumentOutOfRangeException(nameof(seat));
            Name = name; Seat = seat;
            Rules = ArenaRulesProfile.CreateProductionV1(8, 1);
            if (name == "blue-no-safe")
            {
                var builder = Rules.ToBuilder();
                builder.TeleportCandidates = new[] { new Vector2(10f, 22f) };
                Rules = builder.Freeze();
            }
            switch (name)
            {
                case "stun-recovery": RequiredMilestones = new[] { "stunned", "stun_recovered" }; break;
                case "green-impact": RequiredMilestones = new[] { "green_pickup", "green_enhanced_impact" }; break;
                case "purple-immunity": RequiredMilestones = new[] { "purple_pickup", "purple_dynamic_immunity", "purple_pillar_immunity" }; break;
                case "blue-teleport": RequiredMilestones = new[] { "blue_pickup", "blue_safe_relocation" }; break;
                case "blue-no-safe": RequiredMilestones = new[] { "blue_pickup", "blue_no_safe_destination" }; break;
                case "fall-respawn": RequiredMilestones = new[] { "fall_without_score", "safe_respawn_without_score" }; break;
                case "wall-player-repair": RequiredMilestones = new[] { "dash_started", "wall_broken", "player_occupied_repair_pending", "wall_restored_after_clearance" }; break;
                default: RequiredMilestones = new[] { "dash_started", "wall_broken", "ball_occupied_repair_pending", "ball_cleared_by_impact", "wall_restored_after_clearance" }; break;
            }
        }

        public DashArenaState CreateInitial(ArenaRulesProfile rules, uint seed, int epoch)
        {
            if (rules.ContentHash != Rules.ContentHash || epoch != 0)
                throw new InvalidOperationException("Mechanics are one explicitly identified fixture epoch, never a rematch baseline.");
            var state = DashArenaSimulation.Create(rules, seed, epoch);
            DashArenaSimulation.StartMatch(state);
            state.Phase = MatchState.Live; state.PhaseTicks = 0; state.GateClosed = false;
            // Balls start inactive except where declared by the particular mechanism.
            // This is setup before construction of the rollback baseline, not a live edit.
            Vector2 position = Vector2.zero;
            if (Name == "stun-recovery" || Name == "purple-immunity") position = rules.Pillars[0].Position + Vector2.down * 3f;
            if (Name == "fall-respawn") position = new Vector2(rules.HalfWidth - .9f, 0);
            if (Name.StartsWith("wall-", StringComparison.Ordinal)) position = rules.Walls[0].Position + Vector2.down * 3f;
            state.Players[Seat] = new DashPlayerState { Alive = true, Position = position, Facing = Vector2.up };
            if (Name == "green-impact")
                state.Balls[0] = new DashBallState { Active = true, Position = new Vector2(3f, 0) };
            if (Name == "purple-immunity")
                state.Balls[0] = new DashBallState { Active = true, Position = position + Vector2.down * 3f, Velocity = Vector2.up * 20f };
            if (Name == "blue-no-safe")
                state.Players[1 - Seat] = new DashPlayerState { Alive = true, Position = rules.TeleportCandidates[0], Facing = Vector2.up };
            if (Name == "wall-ball-repair")
            {
                // Wall 200 is genuinely Dash-broken. Independent wall 201 is a declared
                // initially broken repair fixture occupied by a stationary free ball.
                state.Walls[1] = new DashWallState { Broken = true, RepairTicks = rules.WallRepairTicks };
                state.Balls[0] = new DashBallState { Active = true, Position = rules.Walls[1].Position };
            }
            DashPickupKind? pickup = Name == "green-impact" ? DashPickupKind.Green :
                Name == "purple-immunity" ? DashPickupKind.Purple :
                Name.StartsWith("blue-", StringComparison.Ordinal) ? DashPickupKind.Blue : (DashPickupKind?)null;
            if (pickup.HasValue)
            {
                state.Pickups[0] = new DashPickupState { Active = true, Id = 1, Kind = pickup.Value, Position = position };
                state.NextPickupId = 2;
            }
            InitialChecksum = state.Checksum(); _previous = state.Clone();
            return state;
        }

        public DashArenaInput Sample(int seat, int sampleTick, DashArenaState state)
        {
            if (seat != Seat || sampleTick < 5 || !state.Players[seat].Alive || state.Players[seat].StunTicks > 0)
                return DashArenaInput.Neutral;
            var input = new DashArenaInput();
            if (Name == "stun-recovery" || Name == "purple-immunity")
                input.Move = sampleTick < 35 || Name == "stun-recovery" && sampleTick >= 80 && sampleTick < 100 ? Vector2.up : Vector2.zero;
            else if (Name == "green-impact") input.Move = sampleTick < 35 ? Vector2.right : Vector2.zero;
            else if (Name == "fall-respawn")
            { if (sampleTick < 30) { input.Move = Vector2.right; input.DashPressed = sampleTick == 5; } }
            else if (Name.StartsWith("wall-", StringComparison.Ordinal))
            {
                if (sampleTick < 23) { input.Move = Vector2.up; input.DashPressed = sampleTick == 5; }
                else if (Name == "wall-player-repair")
                    input.Move = Steer(state.Players[seat], Rules.Walls[0].Position + (sampleTick >= 660 ? Vector2.up * 5 : Vector2.zero));
                else
                {
                    Vector2 target = Rules.Walls[1].Position + Vector2.up * 3f;
                    if (sampleTick < 660) input.Move = Steer(state.Players[seat], target);
                    else if (sampleTick < 678) { input.Move = Vector2.down; input.DashPressed = sampleTick == 660; }
                    else input.Move = Steer(state.Players[seat], target);
                }
            }
            return DashArenaInput.Unpack(input.Pack());
        }

        Vector2 Steer(DashPlayerState player, Vector2 target)
        {
            Vector2 delta = target - player.Position, v = player.Velocity;
            float x = delta.x - Mathf.Sign(v.x) * v.x * v.x / (2f * Rules.Braking);
            float y = delta.y - Mathf.Sign(v.y) * v.y * v.y / (2f * Rules.Braking);
            return new Vector2(Mathf.Abs(x) > .12f ? Mathf.Sign(x) : 0f, Mathf.Abs(y) > .12f ? Mathf.Sign(y) : 0f);
        }

        public void Commit(DashConfirmedFrame frame)
        {
            DashArenaState state = frame.State;
            int tick = state.Tick - 1;
            if (tick <= _lastConfirmedTick) return;
            if (_previous == null || tick != _lastConfirmedTick + 1 || state.Epoch != 0 || state.Rules.ContentHash != Rules.ContentHash)
                throw new InvalidOperationException("Mechanic evidence requires contiguous corrected confirmed snapshots from its fixture baseline.");
            _lastConfirmedTick = tick;
            _scoreChanged |= state.Scores[0] != 0 || state.Scores[1] != 0;
            foreach (DashArenaEvent item in frame.Events)
            {
                if (item.Seat == Seat && item.Kind == DashArenaEventKind.Pickup)
                    Add(item.PickupKind.ToString().ToLowerInvariant() + "_pickup", tick, item.EntityId, "actual_confirmed_pickup");
                if (item.Seat == Seat && item.Kind == DashArenaEventKind.Stunned && state.Players[Seat].StunTicks == Rules.StunTicks)
                { _stunTick = tick; Add("stunned", tick, item.EntityId, "stun_ticks=" + Rules.StunTicks); }
                if (item.Seat == Seat && item.Kind == DashArenaEventKind.PillarHit && Name == "purple-immunity" &&
                    state.Players[Seat].PurpleTicks > 0 && state.Players[Seat].StunTicks == 0 &&
                    state.Players[Seat].ExternalVelocity == Vector2.zero &&
                    Vector2.Distance(state.Players[Seat].Position, Rules.Pillars[0].Position) >= Rules.PlayerRadius + Rules.Pillars[0].Radius - .001f)
                    Add("purple_pillar_immunity", tick, item.EntityId, "actual_pillar_contact_no_stun_no_penetration");
                if (item.Seat == Seat && item.Kind == DashArenaEventKind.Impact && item.EntityId == 2 && Name == "purple-immunity" &&
                    _previous.Players[Seat].PurpleTicks > 0 && state.Players[Seat].PurpleTicks > 0 &&
                    _previous.Balls[0].Velocity.magnitude >= Rules.DangerousBallSpeed &&
                    state.Players[Seat].ExternalVelocity == Vector2.zero &&
                    state.Balls[0].DangerousImpactCount == _previous.Balls[0].DangerousImpactCount)
                    Add("purple_dynamic_immunity", tick, item.EntityId, "actual_dangerous_ball_contact_without_external_recoil_or_danger_penalty");
                if (item.Seat == Seat && item.Kind == DashArenaEventKind.Impact && Name == "green-impact" &&
                    ComparisonTick < 0 && item.Tick == tick && item.Epoch == state.Epoch &&
                    item.EntityId == 2 && state.Players[Seat].GreenTicks > 0 && _previous.Players[Seat].GreenTicks > 0)
                {
                    // Focused readonly counterfactual, not another authority or a fixture
                    // passed off as natural gameplay. The actual impact must exist first.
                    DashArenaState baseline = _previous.Clone(); baseline.Players[Seat].GreenTicks = 0;
                    DashArenaSimulation.Step(baseline, frame.Input0, frame.Input1);
                    float enhanced = state.Balls[0].Velocity.magnitude;
                    float unbuffed = baseline.Balls[0].Velocity.magnitude;
                    if (enhanced - unbuffed > Rules.NormalShotImpulse * (Rules.GreenImpactMultiplier - 1f) - .05f)
                    {
                        // The milestone and all comparison fields are one first-success
                        // witness. Later contacts must not replace half of that evidence.
                        EnhancedBallSpeed = enhanced; UnbuffedCounterfactualSpeed = unbuffed;
                        ComparisonTick = tick; ComparisonInput0 = frame.Input0.Pack(); ComparisonInput1 = frame.Input1.Pack();
                        Add("green_enhanced_impact", tick, item.EntityId, "actual_speed_exceeds_unbuffed_readonly_replay");
                    }
                }
                if (item.Seat == Seat && item.Kind == DashArenaEventKind.Teleport && Name == "blue-teleport" &&
                    Vector2.Distance(state.Players[Seat].Position, _previous.Players[Seat].Position) > .01f &&
                    SafePlayer(state, Seat) && state.Players[Seat].Velocity == Vector2.zero)
                    Add("blue_safe_relocation", tick, item.EntityId, "actual_relocation_full_footprint_safe");
                if (item.Seat == Seat && item.Kind == DashArenaEventKind.TeleportFailed && Name == "blue-no-safe" &&
                    state.Players[Seat].Position == _previous.Players[Seat].Position && !state.Pickups[0].Active)
                    Add("blue_no_safe_destination", tick, item.EntityId, "no_safe_destination_not_successful_teleport");
                if (item.Seat == Seat && item.Kind == DashArenaEventKind.Fell && !_scoreChanged && !state.Players[Seat].Alive)
                { _fallTick = tick; Add("fall_without_score", tick, item.EntityId, "scores_unchanged_0_0"); }
                if (item.Seat == Seat && item.Kind == DashArenaEventKind.Respawned && !_scoreChanged && _fallTick >= 0 &&
                    tick - _fallTick >= Rules.RespawnTicks && state.Players[Seat].Alive && SafePlayer(state, Seat))
                    Add("safe_respawn_without_score", tick, item.EntityId, "full_footprint_safe_scores_unchanged_0_0");
                if (item.Seat == Seat && item.Kind == DashArenaEventKind.DashStarted)
                    Add("dash_started", tick, Seat, "actual_eligible_dash");
                if (item.Seat == Seat && item.Kind == DashArenaEventKind.WallBroken && item.EntityId == Rules.Walls[0].Id &&
                    state.Walls[0].Broken && state.Players[Seat].DashTicks > 0)
                { _brokenTick = tick; Add("wall_broken", tick, item.EntityId, "actual_high_speed_dash"); }
                int repairIndex = Name == "wall-ball-repair" ? 1 : 0;
                if (item.Kind == DashArenaEventKind.WallRepairPending && item.EntityId == Rules.Walls[repairIndex].Id &&
                    state.Walls[repairIndex].Broken && state.Walls[repairIndex].RepairTicks == 0)
                {
                    bool ball = Name == "wall-ball-repair";
                    bool occupied = ball ? BallOccupies(state, repairIndex) : PlayerOccupies(state, repairIndex, Seat);
                    bool waited = ball ? tick + 1 >= Rules.WallRepairTicks : _brokenTick >= 0 && tick - _brokenTick >= Rules.WallRepairTicks;
                    if (occupied && waited)
                    {
                        _pendingTick = tick;
                        Add(ball ? "ball_occupied_repair_pending" : "player_occupied_repair_pending", tick, item.EntityId,
                            "full_collider_blocks_repair_after_600_world_ticks");
                    }
                }
                if (Name == "wall-ball-repair" && item.Kind == DashArenaEventKind.Impact && item.Seat == Seat && item.EntityId == 2 &&
                    _pendingTick >= 0 && tick > _pendingTick && state.Balls[0].Velocity.sqrMagnitude > 0)
                    Add("ball_cleared_by_impact", tick, item.EntityId, "actual_input_driven_ball_impact_after_pending");
                if (item.Kind == DashArenaEventKind.WallRepaired && item.EntityId == Rules.Walls[repairIndex].Id &&
                    _pendingTick >= 0 && tick > _pendingTick && !state.Walls[repairIndex].Broken && !state.Walls[repairIndex].RepairPending &&
                    !BallOccupies(state, repairIndex) && !PlayerOccupies(state, repairIndex, 0) && !PlayerOccupies(state, repairIndex, 1))
                    Add("wall_restored_after_clearance", tick, item.EntityId, "one_confirmed_restoration_after_full_clearance");
            }
            if (Name == "stun-recovery" && _stunTick >= 0 && tick - _stunTick >= Rules.StunTicks &&
                state.Players[Seat].Alive && state.Players[Seat].StunTicks == 0 &&
                (Seat == 0 ? frame.Input0 : frame.Input1).Move.sqrMagnitude > 0 && state.Players[Seat].ControlVelocity.sqrMagnitude > 0)
                Add("stun_recovered", tick, Seat, "stun_timer_completed_and_movement_resumed");
            _previous = state;
        }

        void Add(string name, int tick, int entity, string outcome)
        {
            if (!_names.Add(name)) return;
            _milestones.Add(new DashMechanicMilestone(name, 0, tick, Seat, entity, outcome));
        }

        bool BallOccupies(DashArenaState state, int wall)
        {
            foreach (DashBallState ball in state.Balls)
                if (ball.Active && ArenaGeometry.CircleAabbOverlap(ball.Position, Rules.BallRadius + Rules.RepairClearance,
                    Rules.Walls[wall].Position, Rules.Walls[wall].HalfSize)) return true;
            return false;
        }
        bool PlayerOccupies(DashArenaState state, int wall, int seat) => state.Players[seat].Alive &&
            ArenaGeometry.CircleAabbOverlap(state.Players[seat].Position, Rules.PlayerRadius + Rules.RepairClearance,
                Rules.Walls[wall].Position, Rules.Walls[wall].HalfSize);

        bool SafePlayer(DashArenaState state, int seat)
        {
            Vector2 p = state.Players[seat].Position; float radius = Rules.PlayerRadius + Rules.RepairClearance;
            if (!ArenaGeometry.ContainsFootprint(p, radius, Rules.HalfWidth, Rules.HalfHeight)) return false;
            foreach (var pillar in Rules.Pillars)
                if ((p - pillar.Position).sqrMagnitude <= (radius + pillar.Radius) * (radius + pillar.Radius)) return false;
            for (int i = 0; i < Rules.Walls.Count; i++)
                if ((!state.Walls[i].Broken || state.Walls[i].RepairPending) &&
                    ArenaGeometry.CircleAabbOverlap(p, radius, Rules.Walls[i].Position, Rules.Walls[i].HalfSize)) return false;
            foreach (var ball in state.Balls)
                if (ball.Active && Vector2.Distance(p, ball.Position) <= radius + Rules.BallRadius) return false;
            return !state.Players[1 - seat].Alive || Vector2.Distance(p, state.Players[1 - seat].Position) > radius + Rules.PlayerRadius;
        }
    }

    public readonly struct DashMechanicMilestone
    {
        public readonly string Name, Outcome;
        public readonly int Epoch, Tick, Seat, Entity;
        public DashMechanicMilestone(string name, int epoch, int tick, int seat, int entity, string outcome)
        { Name = name; Epoch = epoch; Tick = tick; Seat = seat; Entity = entity; Outcome = outcome; }
    }
}
#endif
