using System;
using System.Collections.Generic;
using UnityEngine;

namespace PocketBump
{
    // Pure 60 Hz ground-plane rules. The greybox and future director integration share
    // this stepper; views never supply authoritative collision or random state.
    public static class DashArenaSimulation
    {
        const float Epsilon = .00001f;
        const float Separation = .0001f;
        const int MaxContactsPerTick = 128;
        enum ContactKind { Pillar, Wall, Boundary, Bodies, Goal, Fall }
        struct Contact
        {
            public bool Found;
            public float Time;
            public ContactKind Kind;
            public int A, B, Index;
            public Vector2 Normal, Center, Half;
        }

        public static DashArenaState Create(ArenaRulesProfile rules, uint seed = 1, int epoch = 0)
        {
            var state = new DashArenaState(rules, seed, epoch);
            PlacePlayers(state);
            state.SpawnTicks = rules.BuffSpawnTicks;
            return state;
        }

        public static void StartMatch(DashArenaState state)
        {
            if (state.Phase != MatchState.Pregame && state.Phase != MatchState.MatchOver) return;
            state.Tick = 0;
            state.Scores[0] = state.Scores[1] = 0;
            state.RandomState = state.InitialSeed;
            state.NextPickupId = 1;
            state.LastScorer = -1;
            Array.Clear(state.Walls, 0, state.Walls.Length);
            BeginKickoff(state);
        }

        public static IReadOnlyList<DashArenaEvent> Step(DashArenaState state,
            DashArenaInput input0, DashArenaInput input1)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var events = new List<DashArenaEvent>();
            if (state.Phase == MatchState.Pregame || state.Phase == MatchState.MatchOver) return events;
            AdvanceTimers(state);
            ReleaseContacts(state);
            if (state.Phase == MatchState.Scored)
            {
                if (--state.PhaseTicks <= 0)
                {
                    if (MatchDirector.HasWinner(state.Scores[0], state.Scores[1], state.Rules.PointsToWin, out int winner))
                    {
                        state.Phase = MatchState.MatchOver;
                        Emit(state, events, DashArenaEventKind.MatchOver, winner, -1, Vector2.zero);
                    }
                    else BeginKickoff(state);
                }
                if (state.Phase != MatchState.MatchOver) RepairWalls(state, events);
                state.Tick++;
                return events;
            }

            UpdatePlayer(state, 0, input0, events);
            UpdatePlayer(state, 1, input1, events);
            for (int i = 0; i < state.Balls.Length; i++)
            {
                ref DashBallState ball = ref state.Balls[i];
                if (ball.Active) ball.Velocity = Vector2.MoveTowards(ball.Velocity, Vector2.zero, state.Rules.BallDrag / 60f);
            }

            int goalOwner = -1, goalBall = -1;
            AdvancePhysics(state, events, ref goalOwner, ref goalBall);
            if (state.Phase == MatchState.Kickoff)
            {
                if (--state.PhaseTicks <= 0)
                {
                    state.GateClosed = false;
                    state.Phase = MatchState.Live;
                    for (int i = 0; i < state.Balls.Length; i++)
                        state.Balls[i] = new DashBallState { Active = true, Position = state.Rules.BallSpawn(i) };
                    Emit(state, events, DashArenaEventKind.GateOpened, -1, -1, Vector2.zero);
                }
            }
            else if (goalOwner >= 0)
            {
                int scorer = 1 - goalOwner;
                state.Scores[scorer]++;
                state.LastScorer = scorer;
                state.Phase = MatchState.Scored;
                state.PhaseTicks = state.Rules.FreezeTicks;
                ClearBuffs(state);
                Emit(state, events, DashArenaEventKind.Goal, scorer, goalBall, state.Balls[goalBall].Position);
            }
            else
            {
                CollectPickups(state, events);
                SpawnPickup(state);
                RespawnPlayers(state, events);
            }
            RepairWalls(state, events);
            state.Tick++;
            return events;
        }

        static void AdvanceTimers(DashArenaState state)
        {
            for (int i = 0; i < 2; i++)
            {
                ref DashPlayerState p = ref state.Players[i];
                bool ended = p.DashTicks > 0 && --p.DashTicks == 0;
                if (ended) p.DashCooldownTicks = state.Rules.DashCooldownTicks;
                else if (p.DashCooldownTicks > 0) p.DashCooldownTicks--;
                if (p.StunTicks > 0) p.StunTicks--;
                if (p.RespawnTicks > 0) p.RespawnTicks--;
                if (p.GreenTicks > 0) p.GreenTicks--;
                if (p.PurpleTicks > 0) p.PurpleTicks--;
            }
            for (int i = 0; i < state.Walls.Length; i++)
                if (state.Walls[i].Broken && state.Walls[i].RepairTicks > 0) state.Walls[i].RepairTicks--;
        }

        static void UpdatePlayer(DashArenaState state, int seat, DashArenaInput input, List<DashArenaEvent> events)
        {
            ref DashPlayerState p = ref state.Players[seat];
            var r = state.Rules;
            if (!p.Alive) return;
            if (!Finite(input.Move)) input.Move = Vector2.zero;
            input.Move = Vector2.ClampMagnitude(input.Move, 1f);
            p.ExternalVelocity = Vector2.MoveTowards(p.ExternalVelocity, Vector2.zero, r.ExternalDecay / 60f);
            if (p.StunTicks > 0) return; // Keep inertia, but no drive or queued action.
            if (state.Phase == MatchState.Live && input.DashPressed && p.DashTicks == 0 && p.DashCooldownTicks == 0)
            {
                p.DashDirection = input.Move.sqrMagnitude > Epsilon ? input.Move.normalized : p.Facing.normalized;
                if (p.DashDirection.sqrMagnitude < Epsilon) p.DashDirection = seat == 0 ? Vector2.down : Vector2.up;
                p.DashTicks = r.DashTicks;
                p.Facing = p.DashDirection;
                Emit(state, events, DashArenaEventKind.DashStarted, seat, seat, p.Position);
            }
            if (p.DashTicks > 0) p.ControlVelocity = p.DashDirection * r.DashSpeed;
            else if (input.Move.sqrMagnitude > Epsilon)
            {
                float angle = Mathf.MoveTowardsAngle(Mathf.Atan2(p.Facing.y, p.Facing.x) * Mathf.Rad2Deg,
                    Mathf.Atan2(input.Move.y, input.Move.x) * Mathf.Rad2Deg, 720f / 60f) * Mathf.Deg2Rad;
                p.Facing = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                p.ControlVelocity = Vector2.MoveTowards(p.ControlVelocity, input.Move * r.NormalSpeed, r.Acceleration / 60f);
            }
            else p.ControlVelocity = Vector2.MoveTowards(p.ControlVelocity, Vector2.zero, r.Braking / 60f);
        }

        static void InterruptDash(DashArenaState state, int seat)
        {
            ref DashPlayerState p = ref state.Players[seat];
            if (p.DashTicks == 0) return;
            p.DashTicks = 0;
            p.DashCooldownTicks = state.Rules.DashCooldownTicks;
        }

        static void AdvancePhysics(DashArenaState state, List<DashArenaEvent> events, ref int goalOwner, ref int goalBall)
        {
            float remaining = 1f / 60f;
            for (int iteration = 0; iteration < MaxContactsPerTick && remaining > Epsilon; iteration++)
            {
                Contact contact = FindContact(state, remaining);
                if (!contact.Found) { MoveBodies(state, remaining); return; }
                MoveBodies(state, contact.Time);
                remaining -= contact.Time;
                if (contact.Kind == ContactKind.Goal)
                {
                    int ball = contact.A - 2;
                    state.Balls[ball].Active = false;
                    if (goalOwner < 0 || contact.Index < goalOwner || (contact.Index == goalOwner && ball < goalBall))
                    { goalOwner = contact.Index; goalBall = ball; }
                }
                else if (contact.Kind == ContactKind.Fall) Fall(state, contact.A, events);
                else if (contact.Kind == ContactKind.Bodies) ResolveBodies(state, contact.A, contact.B, events);
                else ResolveStatic(state, contact, events);
            }
            // Pathological simultaneous constraints stop the remaining movement safely.
            // Never tunnel by blindly integrating the remainder after the finite budget.
        }

        static void MoveBodies(DashArenaState s, float dt)
        {
            for (int i = 0; i < 2; i++) if (s.Players[i].Alive) s.Players[i].Position += s.Players[i].Velocity * dt;
            for (int i = 0; i < s.Balls.Length; i++) if (s.Balls[i].Active) s.Balls[i].Position += s.Balls[i].Velocity * dt;
        }

        static Contact FindContact(DashArenaState s, float duration)
        {
            Contact best = default;
            for (int a = 0; a < 2 + s.Balls.Length; a++)
            {
                if (!Active(s, a)) continue;
                Vector2 pos = Position(s, a), vel = Velocity(s, a);
                float radius = Radius(s, a);
                for (int i = 0; i < s.Rules.Pillars.Count; i++)
                {
                    var pillar = s.Rules.Pillars[i];
                    if (SweepCircle(pos, vel, pillar.Position, Vector2.zero, radius + pillar.Radius, duration, out float t))
                        Consider(ref best, new Contact { Found = true, Time = t, Kind = ContactKind.Pillar, A = a, Index = i, Center = pillar.Position });
                }
                for (int i = 0; i < s.Walls.Length; i++)
                {
                    if (s.Walls[i].Broken) continue;
                    var wall = s.Rules.Walls[i];
                    BoxCandidate(ref best, pos, vel, radius, duration, a, ContactKind.Wall, i, wall.Position, wall.HalfSize);
                }
                FindBoundaries(s, a, pos, vel, radius, duration, ref best);
                for (int b = a + 1; b < 2 + s.Balls.Length; b++)
                    if (Active(s, b) && SweepCircle(pos, vel, Position(s, b), Velocity(s, b), radius + Radius(s, b), duration, out float t))
                        Consider(ref best, new Contact { Found = true, Time = t, Kind = ContactKind.Bodies, A = a, B = b });
            }
            return best;
        }

        static void FindBoundaries(DashArenaState s, int body, Vector2 p, Vector2 v, float radius, float dt, ref Contact best)
        {
            var r = s.Rules;
            float w = r.HalfWidth, h = r.HalfHeight, thick = r.WallThickness, goal = r.GoalWidth * .5f, death = r.DeathOpening * .5f;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int offset = sign < 0 ? 0 : 4;
                if (body < 2)
                    BoxCandidate(ref best, p, v, radius, dt, body, ContactKind.Boundary, offset,
                        new Vector2(0, sign * (h + thick / 2)), new Vector2(w + thick, thick / 2));
                else
                {
                    BoxCandidate(ref best, p, v, radius, dt, body, ContactKind.Boundary, offset,
                        new Vector2(-(w + goal) / 2, sign * (h + thick / 2)), new Vector2((w - goal) / 2, thick / 2));
                    BoxCandidate(ref best, p, v, radius, dt, body, ContactKind.Boundary, offset + 1,
                        new Vector2((w + goal) / 2, sign * (h + thick / 2)), new Vector2((w - goal) / 2, thick / 2));
                }
                if (body >= 2 || s.Phase != MatchState.Live)
                    BoxCandidate(ref best, p, v, radius, dt, body, ContactKind.Boundary, 8 + offset,
                        new Vector2(sign * (w + thick / 2), 0), new Vector2(thick / 2, h + thick));
                else
                {
                    BoxCandidate(ref best, p, v, radius, dt, body, ContactKind.Boundary, 8 + offset,
                        new Vector2(sign * (w + thick / 2), -(h + death) / 2), new Vector2(thick / 2, (h - death) / 2));
                    BoxCandidate(ref best, p, v, radius, dt, body, ContactKind.Boundary, 9 + offset,
                        new Vector2(sign * (w + thick / 2), (h + death) / 2), new Vector2(thick / 2, (h - death) / 2));
                }
                if (s.Phase == MatchState.Live)
                {
                    bool ball = body >= 2;
                    float axis = ball ? p.y : p.x, speed = ball ? v.y : v.x;
                    float border = (ball ? h : w) + radius;
                    if (speed * sign > Epsilon)
                    {
                        float t = (sign * border - axis) / speed;
                        float lateral = ball ? p.x + v.x * t : p.y + v.y * t;
                        if (t >= -Epsilon && t <= dt && Mathf.Abs(lateral) <= (ball ? goal : death) - radius + Epsilon)
                            Consider(ref best, new Contact { Found = true, Time = Mathf.Max(0, t), A = body,
                                Kind = ball ? ContactKind.Goal : ContactKind.Fall, Index = sign > 0 ? 0 : 1 });
                    }
                }
            }
            if (s.GateClosed)
                BoxCandidate(ref best, p, v, radius, dt, body, ContactKind.Boundary, 20, Vector2.zero, new Vector2(w, r.KickoffHalfDepth));
        }

        static void BoxCandidate(ref Contact best, Vector2 p, Vector2 v, float radius, float dt,
            int body, ContactKind kind, int index, Vector2 center, Vector2 half)
        {
            if (SweepBox(p, v, radius, center, half, dt, out float t, out Vector2 normal))
                Consider(ref best, new Contact { Found = true, Time = t, Kind = kind, A = body,
                    Index = index, Center = center, Half = half, Normal = normal });
        }

        static void Consider(ref Contact best, Contact candidate)
        {
            if (!best.Found || candidate.Time < best.Time - Epsilon ||
                (Mathf.Abs(candidate.Time - best.Time) <= Epsilon &&
                 ((int)candidate.Kind < (int)best.Kind || (candidate.Kind == best.Kind &&
                  (candidate.A < best.A || (candidate.A == best.A &&
                   (candidate.Index < best.Index || (candidate.Index == best.Index && candidate.B < best.B)))))))) best = candidate;
        }

        static bool SweepCircle(Vector2 a, Vector2 va, Vector2 b, Vector2 vb, float radius, float limit, out float t)
        {
            t = 0;
            Vector2 d = a - b, v = va - vb;
            float c = d.sqrMagnitude - radius * radius, approach = Vector2.Dot(d, v);
            if (c < -Epsilon) return true;
            if (c <= Epsilon) return approach < -Epsilon;
            float aa = v.sqrMagnitude;
            if (aa < Epsilon || approach >= 0) return false;
            float discriminant = approach * approach - aa * c;
            if (discriminant < 0) return false;
            t = (-approach - Mathf.Sqrt(discriminant)) / aa;
            return t >= 0 && t <= limit;
        }

        static bool SweepBox(Vector2 p, Vector2 v, float radius, Vector2 center, Vector2 half,
            float limit, out float time, out Vector2 normal)
        {
            Vector2 min = center - half, max = center + half;
            Vector2 closest = new Vector2(Mathf.Clamp(p.x, min.x, max.x), Mathf.Clamp(p.y, min.y, max.y));
            Vector2 delta = p - closest;
            time = 0;
            normal = delta.sqrMagnitude > Epsilon * Epsilon ? delta.normalized : BoxInsideNormal(p, center, half);
            float penetration = radius * radius - delta.sqrMagnitude;
            if (penetration > Epsilon || (penetration >= -Epsilon && Vector2.Dot(v, normal) < -Epsilon)) return true;
            time = float.PositiveInfinity;
            for (int axis = 0; axis < 2; axis++)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float speed = axis == 0 ? v.x : v.y;
                    if (speed * sign >= -Epsilon) continue;
                    float edge = (axis == 0 ? center.x + sign * half.x : center.y + sign * half.y) + sign * radius;
                    float candidate = (edge - (axis == 0 ? p.x : p.y)) / speed;
                    float tangent = axis == 0 ? p.y + v.y * candidate : p.x + v.x * candidate;
                    float lower = axis == 0 ? min.y : min.x, upper = axis == 0 ? max.y : max.x;
                    if (candidate >= 0 && candidate <= limit && candidate < time && tangent >= lower && tangent <= upper)
                    { time = candidate; normal = axis == 0 ? new Vector2(sign, 0) : new Vector2(0, sign); }
                }
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    Vector2 corner = center + new Vector2(x * half.x, y * half.y);
                    if (SweepCircle(p, v, corner, Vector2.zero, radius, limit, out float candidate) && candidate < time)
                    { time = candidate; normal = (p + v * candidate - corner).normalized; }
                }
            return time <= limit;
        }

        static Vector2 BoxInsideNormal(Vector2 p, Vector2 center, Vector2 half)
        {
            Vector2 local = p - center;
            return half.x - Mathf.Abs(local.x) <= half.y - Mathf.Abs(local.y)
                ? new Vector2(local.x < 0 ? -1 : 1, 0) : new Vector2(0, local.y < 0 ? -1 : 1);
        }

        static void ResolveStatic(DashArenaState s, Contact hit, List<DashArenaEvent> events)
        {
            int body = hit.A;
            Vector2 position = Position(s, body), normal;
            float radius = Radius(s, body);
            var r = s.Rules;
            if (hit.Kind == ContactKind.Pillar)
            {
                Vector2 d = position - hit.Center;
                normal = d.sqrMagnitude > Epsilon * Epsilon ? d.normalized : Vector2.right;
                position = hit.Center + normal * (r.Pillars[hit.Index].Radius + radius + Separation);
            }
            else
            {
                normal = hit.Normal;
                Vector2 closest = new Vector2(Mathf.Clamp(position.x, hit.Center.x - hit.Half.x, hit.Center.x + hit.Half.x),
                    Mathf.Clamp(position.y, hit.Center.y - hit.Half.y, hit.Center.y + hit.Half.y));
                if ((position - closest).sqrMagnitude < Epsilon * Epsilon)
                    closest = hit.Center + new Vector2(normal.x != 0 ? normal.x * hit.Half.x : position.x - hit.Center.x,
                        normal.y != 0 ? normal.y * hit.Half.y : position.y - hit.Center.y);
                position = closest + normal * (radius + Separation);
            }
            float closing = -Vector2.Dot(Velocity(s, body), normal);
            if (hit.Kind == ContactKind.Wall && body < 2 && s.Players[body].DashTicks > 0 && closing >= r.WallBreakSpeed)
            {
                s.Walls[hit.Index] = new DashWallState { Broken = true, RepairTicks = r.WallRepairTicks };
                s.WallContacts[body * s.Walls.Length + hit.Index] = true;
                Emit(s, events, DashArenaEventKind.WallBroken, body, r.Walls[hit.Index].Id, hit.Center);
                return;
            }
            SetPosition(s, body, position);
            if (closing > 0)
            {
                // Armor never penetrates geometry, but does not receive a pillar bounce.
                float restitution = body < 2 && s.Players[body].PurpleTicks > 0 ? 0 : r.Bounciness;
                AddVelocity(s, body, normal * ((1 + restitution) * closing), false);
            }
            if (hit.Kind == ContactKind.Pillar && body < 2)
            {
                int latch = body * r.Pillars.Count + hit.Index;
                if (!s.CylinderContacts[latch])
                    Emit(s, events, DashArenaEventKind.PillarHit, body, r.Pillars[hit.Index].Id, position);
                if (!s.CylinderContacts[latch] && s.Players[body].PurpleTicks == 0)
                {
                    InterruptDash(s, body);
                    s.Players[body].StunTicks = r.StunTicks;
                    Emit(s, events, DashArenaEventKind.Stunned, body, r.Pillars[hit.Index].Id, position);
                }
                s.CylinderContacts[latch] = true;
            }
            if (hit.Kind == ContactKind.Wall)
            {
                bool[] contacts = body < 2 ? s.WallContacts : s.BallWallContacts;
                int index = (body < 2 ? body : body - 2) * s.Walls.Length + hit.Index;
                if (!contacts[index]) Emit(s, events, DashArenaEventKind.WallHit, body < 2 ? body : -1, r.Walls[hit.Index].Id, hit.Center);
                contacts[index] = true;
            }
        }

        static void ResolveBodies(DashArenaState s, int a, int b, List<DashArenaEvent> events)
        {
            Vector2 pa = Position(s, a), pb = Position(s, b), d = pb - pa;
            Vector2 n = d.sqrMagnitude > Epsilon * Epsilon ? d.normalized : Vector2.right;
            var r = s.Rules;
            float massA = a < 2 ? r.PlayerMass : r.BallMass, massB = b < 2 ? r.PlayerMass : r.BallMass;
            float invA = 1f / massA, invB = 1f / massB;
            float penetration = Mathf.Max(0, Radius(s, a) + Radius(s, b) - d.magnitude) + Separation;
            SetPosition(s, a, pa - n * (penetration * invA / (invA + invB)));
            SetPosition(s, b, pb + n * (penetration * invB / (invA + invB)));
            float closing = Vector2.Dot(Velocity(s, a) - Velocity(s, b), n);
            bool latched = a < 2 && b < 2 ? s.PlayerContacts[0] : a < 2 && s.PlayerBallContacts[a * s.Balls.Length + b - 2];
            if (a < 2 && b < 2) s.PlayerContacts[0] = true;
            else if (a < 2) s.PlayerBallContacts[a * s.Balls.Length + b - 2] = true;
            if (closing <= 0) return;
            float beforeBallSpeed = b >= 2 ? s.Balls[b - 2].Velocity.magnitude : 0;
            if (a < 2 && s.Players[a].PurpleTicks > 0) invA = 0;
            if (b < 2 && s.Players[b].PurpleTicks > 0) invB = 0;
            if (invA + invB > 0)
            {
                float restitution = b < 2 ? r.PlayerBounciness : r.Bounciness;
                float impulse = (1 + restitution) * closing / (invA + invB);
                AddVelocity(s, a, -n * impulse * invA);
                AddVelocity(s, b, n * impulse * invB);
            }
            else
            {
                // Two immune drivers still obey the positional nonpenetration constraint.
                StopDrivingInto(s, a, n);
                StopDrivingInto(s, b, -n);
            }
            if (latched || a >= 2) return;
            float strengthA = Outgoing(s, a, b >= 2);
            if (b < 2)
            {
                AddVelocity(s, a, -n * Outgoing(s, b, false));
                AddVelocity(s, b, n * strengthA);
            }
            else
            {
                AddVelocity(s, b, n * strengthA);
                if (beforeBallSpeed >= r.DangerousBallSpeed && s.Players[a].PurpleTicks == 0)
                {
                    AddVelocity(s, a, -n * (closing * r.DangerousImpulseScale));
                    s.Balls[b - 2].Velocity *= r.DangerousEnergyRetention;
                    s.Balls[b - 2].DangerousImpactCount++;
                }
            }
            Emit(s, events, DashArenaEventKind.Impact, a, b, (pa + pb) / 2f);
        }

        static float Outgoing(DashArenaState s, int seat, bool ball)
        {
            var p = s.Players[seat]; var r = s.Rules;
            float force = ball ? (p.DashTicks > 0 ? r.DashShotImpulse : r.NormalShotImpulse)
                : (p.DashTicks > 0 ? r.DashPlayerImpulse : r.NormalPlayerImpulse);
            return force * (p.GreenTicks > 0 ? r.GreenImpactMultiplier : 1f);
        }

        static void StopDrivingInto(DashArenaState s, int seat, Vector2 toward)
        {
            ref DashPlayerState p = ref s.Players[seat];
            float closing = Vector2.Dot(p.Velocity, toward);
            if (closing > 0) p.ControlVelocity -= toward * closing;
        }

        static void AddVelocity(DashArenaState s, int body, Vector2 delta, bool dynamic = true)
        {
            if (body >= 2)
            {
                s.Balls[body - 2].Velocity = Vector2.ClampMagnitude(s.Balls[body - 2].Velocity + delta, s.Rules.BallMaxSpeed);
                return;
            }
            ref DashPlayerState p = ref s.Players[body];
            if (dynamic && p.PurpleTicks > 0) return;
            if (dynamic) p.ExternalVelocity = Vector2.ClampMagnitude(p.ExternalVelocity + delta, s.Rules.MaxExternalSpeed);
            else
            {
                // Static constraints resolve total velocity; keep active drive distinct.
                p.ControlVelocity += delta;
            }
        }

        static void Fall(DashArenaState s, int seat, List<DashArenaEvent> events)
        {
            InterruptDash(s, seat);
            ref DashPlayerState p = ref s.Players[seat];
            p.Alive = false;
            p.RespawnTicks = s.Rules.RespawnTicks;
            p.GreenTicks = p.PurpleTicks = p.StunTicks = 0;
            p.ControlVelocity = p.ExternalVelocity = Vector2.zero;
            ClearPlayerContacts(s, seat);
            Emit(s, events, DashArenaEventKind.Fell, seat, seat, p.Position);
        }

        static void RespawnPlayers(DashArenaState s, List<DashArenaEvent> events)
        {
            for (int seat = 0; seat < 2; seat++)
            {
                if (s.Players[seat].Alive || s.Players[seat].RespawnTicks > 0) continue;
                foreach (Vector2 point in s.Rules.RespawnCandidates(seat))
                {
                    if (!SafePosition(s, point, s.Rules.PlayerRadius, seat, true)) continue;
                    ref DashPlayerState p = ref s.Players[seat];
                    p.Alive = true;
                    p.Position = point;
                    p.ControlVelocity = p.ExternalVelocity = Vector2.zero;
                    ClearPlayerContacts(s, seat);
                    Emit(s, events, DashArenaEventKind.Respawned, seat, seat, point);
                    break;
                }
            }
        }

        static void RepairWalls(DashArenaState s, List<DashArenaEvent> events)
        {
            for (int i = 0; i < s.Walls.Length; i++)
            {
                ref DashWallState wall = ref s.Walls[i];
                if (!wall.Broken || wall.RepairTicks > 0) continue;
                if (WallOccupied(s, i))
                {
                    if (!wall.RepairPending) Emit(s, events, DashArenaEventKind.WallRepairPending, -1, s.Rules.Walls[i].Id, s.Rules.Walls[i].Position);
                    wall.RepairPending = true;
                }
                else
                {
                    wall = default;
                    Emit(s, events, DashArenaEventKind.WallRepaired, -1, s.Rules.Walls[i].Id, s.Rules.Walls[i].Position);
                }
            }
        }

        static bool WallOccupied(DashArenaState s, int index)
        {
            var wall = s.Rules.Walls[index];
            for (int body = 0; body < 2 + s.Balls.Length; body++)
                if (Active(s, body) && OverlapsBox(Position(s, body), Radius(s, body) + s.Rules.RepairClearance, wall.Position, wall.HalfSize)) return true;
            return false;
        }

        static void SpawnPickup(DashArenaState s)
        {
            if (s.SpawnTicks > 0 && --s.SpawnTicks > 0) return;
            int slot = Array.FindIndex(s.Pickups, item => !item.Active);
            if (slot < 0 || s.NextPickupId == int.MaxValue) return;
            var available = new List<Vector2>();
            foreach (Vector2 p in s.Rules.PickupCandidates)
            {
                if (!SafePosition(s, p, s.Rules.PickupRadius, -1, true)) continue;
                bool overlaps = false;
                foreach (var item in s.Pickups)
                    if (item.Active && (item.Position - p).sqrMagnitude <= 4 * s.Rules.PickupRadius * s.Rules.PickupRadius) overlaps = true;
                if (!overlaps) available.Add(p);
            }
            if (available.Count == 0) return;
            ArenaRandom.TryChoosePickup(ref s.RandomState, available.Count, out int location, out int kind);
            s.Pickups[slot] = new DashPickupState { Active = true, Id = s.NextPickupId++, Kind = (DashPickupKind)kind, Position = available[location] };
            s.SpawnTicks = s.Rules.BuffSpawnTicks;
        }

        static void CollectPickups(DashArenaState s, List<DashArenaEvent> events)
        {
            var order = new List<int>();
            for (int i = 0; i < s.Pickups.Length; i++) if (s.Pickups[i].Active) order.Add(i);
            order.Sort((a, b) => s.Pickups[a].Id.CompareTo(s.Pickups[b].Id));
            bool[] teleported = new bool[2];
            foreach (int slot in order)
            {
                var item = s.Pickups[slot];
                int receiver = -1; float best = float.PositiveInfinity;
                float radius = s.Rules.PlayerRadius + s.Rules.PickupRadius;
                for (int seat = 0; seat < 2; seat++)
                {
                    if (!s.Players[seat].Alive || teleported[seat]) continue;
                    float distance = (s.Players[seat].Position - item.Position).sqrMagnitude;
                    if (distance <= radius * radius && distance < best) { best = distance; receiver = seat; }
                }
                if (receiver < 0) continue;
                s.Pickups[slot].Active = false;
                ref DashPlayerState player = ref s.Players[receiver];
                if (item.Kind == DashPickupKind.Green) player.GreenTicks = s.Rules.GreenTicks;
                else if (item.Kind == DashPickupKind.Purple) { player.PurpleTicks = s.Rules.PurpleTicks; player.StunTicks = 0; }
                else teleported[receiver] = Teleport(s, receiver, item.Id, events);
                Emit(s, events, DashArenaEventKind.Pickup, receiver, item.Id, item.Position, item.Kind);
            }
        }

        static bool Teleport(DashArenaState s, int seat, int pickupId, List<DashArenaEvent> events)
        {
            uint draw = ArenaRandom.Next(ref s.RandomState); // Exactly one draw, even on failure.
            var candidates = s.Rules.TeleportCandidates;
            for (int offset = 0; offset < candidates.Count; offset++)
            {
                Vector2 point = candidates[((int)(draw % (uint)candidates.Count) + offset) % candidates.Count];
                if (!SafePosition(s, point, s.Rules.PlayerRadius, seat, true)) continue;
                InterruptDash(s, seat);
                s.Players[seat].Position = point;
                s.Players[seat].ControlVelocity = s.Players[seat].ExternalVelocity = Vector2.zero;
                ClearPlayerContacts(s, seat);
                Emit(s, events, DashArenaEventKind.Teleport, seat, pickupId, point);
                return true;
            }
            Emit(s, events, DashArenaEventKind.TeleportFailed, seat, pickupId, s.Players[seat].Position);
            return false;
        }

        static bool SafePosition(DashArenaState s, Vector2 p, float radius, int ignoreSeat, bool pendingSolid)
        {
            var r = s.Rules;
            if (Mathf.Abs(p.x) + radius >= r.HalfWidth || Mathf.Abs(p.y) + radius >= r.HalfHeight) return false;
            foreach (var pillar in r.Pillars)
                if ((p - pillar.Position).sqrMagnitude <= Square(radius + pillar.Radius + r.RepairClearance)) return false;
            for (int i = 0; i < s.Walls.Length; i++)
                if ((!s.Walls[i].Broken || (pendingSolid && s.Walls[i].RepairPending)) &&
                    OverlapsBox(p, radius + r.RepairClearance, r.Walls[i].Position, r.Walls[i].HalfSize)) return false;
            for (int i = 0; i < 2; i++)
                if (i != ignoreSeat && s.Players[i].Alive && (p - s.Players[i].Position).sqrMagnitude <= Square(radius + r.PlayerRadius + r.RepairClearance)) return false;
            foreach (var ball in s.Balls)
                if (ball.Active && (p - ball.Position).sqrMagnitude <= Square(radius + r.BallRadius + r.RepairClearance)) return false;
            return true;
        }

        static void ReleaseContacts(DashArenaState s)
        {
            var r = s.Rules;
            if (!s.Players[0].Alive || !s.Players[1].Alive ||
                (s.Players[0].Position - s.Players[1].Position).sqrMagnitude > Square(2 * r.PlayerRadius + r.ContactReleasePadding)) s.PlayerContacts[0] = false;
            for (int seat = 0; seat < 2; seat++)
            {
                var p = s.Players[seat];
                for (int i = 0; i < s.Balls.Length; i++)
                    if (!p.Alive || !s.Balls[i].Active || (p.Position - s.Balls[i].Position).sqrMagnitude > Square(r.PlayerRadius + r.BallRadius + r.ContactReleasePadding))
                        s.PlayerBallContacts[seat * s.Balls.Length + i] = false;
                for (int i = 0; i < r.Pillars.Count; i++)
                    if (!p.Alive || (p.Position - r.Pillars[i].Position).sqrMagnitude > Square(r.PlayerRadius + r.Pillars[i].Radius + r.ContactReleasePadding))
                        s.CylinderContacts[seat * r.Pillars.Count + i] = false;
                for (int i = 0; i < s.Walls.Length; i++)
                    if (!p.Alive || !OverlapsBox(p.Position, r.PlayerRadius + r.ContactReleasePadding, r.Walls[i].Position, r.Walls[i].HalfSize))
                        s.WallContacts[seat * s.Walls.Length + i] = false;
            }
            for (int b = 0; b < s.Balls.Length; b++)
                for (int w = 0; w < s.Walls.Length; w++)
                    if (!s.Balls[b].Active || !OverlapsBox(s.Balls[b].Position, r.BallRadius + r.ContactReleasePadding, r.Walls[w].Position, r.Walls[w].HalfSize))
                        s.BallWallContacts[b * s.Walls.Length + w] = false;
        }

        static void ClearPlayerContacts(DashArenaState s, int seat)
        {
            s.PlayerContacts[0] = false;
            Array.Clear(s.PlayerBallContacts, seat * s.Balls.Length, s.Balls.Length);
            Array.Clear(s.CylinderContacts, seat * s.Rules.Pillars.Count, s.Rules.Pillars.Count);
            Array.Clear(s.WallContacts, seat * s.Walls.Length, s.Walls.Length);
        }

        static void BeginKickoff(DashArenaState s)
        {
            s.Phase = MatchState.Kickoff;
            s.PhaseTicks = s.Rules.KickoffTicks;
            s.GateClosed = true;
            PlacePlayers(s);
            Array.Clear(s.Balls, 0, s.Balls.Length);
            Array.Clear(s.BallWallContacts, 0, s.BallWallContacts.Length);
            ClearBuffs(s);
        }

        static void PlacePlayers(DashArenaState s)
        {
            for (int seat = 0; seat < 2; seat++)
            {
                s.Players[seat] = new DashPlayerState { Alive = true, Position = s.Rules.SpawnForSeat(seat), Facing = seat == 0 ? Vector2.down : Vector2.up };
                ClearPlayerContacts(s, seat);
            }
        }

        static void ClearBuffs(DashArenaState s)
        {
            for (int i = 0; i < 2; i++) s.Players[i].GreenTicks = s.Players[i].PurpleTicks = 0;
            Array.Clear(s.Pickups, 0, s.Pickups.Length);
            s.SpawnTicks = s.Rules.BuffSpawnTicks;
        }

        static bool Active(DashArenaState s, int body) => body < 2 ? s.Players[body].Alive : s.Balls[body - 2].Active;
        static Vector2 Position(DashArenaState s, int body) => body < 2 ? s.Players[body].Position : s.Balls[body - 2].Position;
        static Vector2 Velocity(DashArenaState s, int body) => body < 2 ? s.Players[body].Velocity : s.Balls[body - 2].Velocity;
        static float Radius(DashArenaState s, int body) => body < 2 ? s.Rules.PlayerRadius : s.Rules.BallRadius;
        static void SetPosition(DashArenaState s, int body, Vector2 position)
        { if (body < 2) s.Players[body].Position = position; else s.Balls[body - 2].Position = position; }
        static bool OverlapsBox(Vector2 p, float radius, Vector2 center, Vector2 half)
        {
            Vector2 closest = new Vector2(Mathf.Clamp(p.x, center.x - half.x, center.x + half.x), Mathf.Clamp(p.y, center.y - half.y, center.y + half.y));
            return (p - closest).sqrMagnitude <= radius * radius;
        }
        static float Square(float v) => v * v;
        static bool Finite(Vector2 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y);
        static void Emit(DashArenaState s, List<DashArenaEvent> events, DashArenaEventKind kind, int seat,
            int id, Vector2 position, DashPickupKind? pickupKind = null)
        { events.Add(new DashArenaEvent(s.Tick, s.Epoch, seat, id, kind, position, events.Count, pickupKind)); }
    }
}
