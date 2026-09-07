using UnityEngine;

namespace PocketBump
{
    public static class MatchSim
    {
        public const float TickDt = ArenaLayout.SimTickDt;
        public static readonly int KickoffTicks = Mathf.RoundToInt(ArenaLayout.KickoffSeconds / TickDt);
        public static readonly int FreezeTicks = Mathf.RoundToInt(ArenaLayout.FreezeSeconds / TickDt);

        struct Aabb
        {
            public Vector2 Center;
            public Vector2 Half;

            public Aabb(Vector2 center, Vector2 size)
            {
                Center = center;
                Half = size * 0.5f;
            }

            public Vector2 Min => Center - Half;
            public Vector2 Max => Center + Half;
        }

        static readonly Aabb[] Walls;
        static readonly Aabb GateBox;
        static readonly Aabb NorthBlocker;
        static readonly Aabb SouthBlocker;
        static readonly Aabb EastBlocker;
        static readonly Aabb WestBlocker;
        static readonly Aabb NorthWell;
        static readonly Aabb SouthWell;
        static readonly Aabb EastWell;
        static readonly Aabb WestWell;

        static MatchSim()
        {
            float w = ArenaLayout.HalfWidth;
            float h = ArenaLayout.HalfHeight;
            float t = ArenaLayout.Wall;
            float gapN = ArenaLayout.MarblePocketWidth * 0.5f;
            float gapE = ArenaLayout.DunkPocketHeight * 0.5f;
            Walls = new[]
            {
                new Aabb(new Vector2((-w - gapN) * 0.5f, h + t * 0.5f), new Vector2(w - gapN, t)),
                new Aabb(new Vector2((w + gapN) * 0.5f, h + t * 0.5f), new Vector2(w - gapN, t)),
                new Aabb(new Vector2((-w - gapN) * 0.5f, -h - t * 0.5f), new Vector2(w - gapN, t)),
                new Aabb(new Vector2((w + gapN) * 0.5f, -h - t * 0.5f), new Vector2(w - gapN, t)),
                new Aabb(new Vector2(w + t * 0.5f, (h + gapE) * 0.5f), new Vector2(t, h - gapE)),
                new Aabb(new Vector2(w + t * 0.5f, (-h - gapE) * 0.5f), new Vector2(t, h - gapE)),
                new Aabb(new Vector2(-w - t * 0.5f, (h + gapE) * 0.5f), new Vector2(t, h - gapE)),
                new Aabb(new Vector2(-w - t * 0.5f, (-h - gapE) * 0.5f), new Vector2(t, h - gapE))
            };
            GateBox = new Aabb(Vector2.zero, new Vector2(w * 2f - 0.2f, 0.22f));
            NorthBlocker = new Aabb(new Vector2(0f, h), new Vector2(ArenaLayout.MarblePocketWidth, t));
            SouthBlocker = new Aabb(new Vector2(0f, -h), new Vector2(ArenaLayout.MarblePocketWidth, t));
            EastBlocker = new Aabb(new Vector2(w, 0f), new Vector2(t, ArenaLayout.DunkPocketHeight));
            WestBlocker = new Aabb(new Vector2(-w, 0f), new Vector2(t, ArenaLayout.DunkPocketHeight));
            float wellOff = t + 0.55f;
            NorthWell = new Aabb(new Vector2(0f, h + wellOff), new Vector2(ArenaLayout.MarblePocketWidth, 1.1f));
            SouthWell = new Aabb(new Vector2(0f, -h - wellOff), new Vector2(ArenaLayout.MarblePocketWidth, 1.1f));
            EastWell = new Aabb(new Vector2(w + wellOff, 0f), new Vector2(1.1f, ArenaLayout.DunkPocketHeight));
            WestWell = new Aabb(new Vector2(-w - wellOff, 0f), new Vector2(1.1f, ArenaLayout.DunkPocketHeight));
        }

        public static MatchSimState Create(MatchSettings settings)
        {
            settings = settings ?? new MatchSettings();
            settings.Clamp();
            var state = new MatchSimState
            {
                PointsToWin = settings.pointsToWin,
                MarbleCountSetting = settings.marbleCount,
                Phase = MatchState.Pregame,
                GateClosed = true
            };
            PlacePlayers(state);
            return state;
        }

        public static void StartMatch(MatchSimState state)
        {
            if (state.Phase != MatchState.Pregame && state.Phase != MatchState.MatchOver)
                return;
            state.ScoreP1 = 0;
            state.ScoreP2 = 0;
            BeginKickoff(state);
        }

        public static SimEvents Step(MatchSimState state, PlayerInput input0, PlayerInput input1)
        {
            var events = new SimEvents { Scorer = -1, Winner = -1 };
            if (state.Phase == MatchState.MatchOver || state.Phase == MatchState.Pregame)
                return events;

            if (state.Phase == MatchState.Scored)
            {
                state.PhaseTicks--;
                if (state.PhaseTicks <= 0)
                {
                    if (MatchDirector.HasWinner(state.ScoreP1, state.ScoreP2, state.PointsToWin, out int winner))
                    {
                        state.Phase = MatchState.MatchOver;
                        ClearMarbles(state);
                        state.GateClosed = true;
                        events.MatchOver = true;
                        events.Winner = winner;
                    }
                    else
                    {
                        BeginKickoff(state);
                    }
                }

                state.Tick++;
                return events;
            }

            bool moving = state.Phase == MatchState.Kickoff || state.Phase == MatchState.Live;
            if (moving)
            {
                StepPlayer(state, 0, input0, ref events);
                StepPlayer(state, 1, input1, ref events);
                if (state.Phase == MatchState.Live)
                    StepMarbles(state);
                IntegrateAndCollide(state, ref events);
                WeldCarried(state);
                if (state.Phase == MatchState.Live)
                    ProcessBaskets(state, ref events);
            }

            if (state.Phase == MatchState.Live)
                TryScore(state, ref events);

            if (state.Phase == MatchState.Kickoff)
            {
                state.PhaseTicks--;
                if (state.PhaseTicks <= 0)
                {
                    state.GateClosed = false;
                    SpawnMarbles(state);
                    state.Phase = MatchState.Live;
                    events.GateOpened = true;
                }
            }

            TickCooldowns(state);
            state.Tick++;
            return events;
        }

        public static Vector2 ClusterOffset(int index, int count)
        {
            if (count <= 1 || index == 0) return ArenaLayout.MarbleCenter;
            float spacing = ArenaLayout.MarbleRadius * 2.15f;
            float angle = index * 2.399963f;
            float dist = spacing * Mathf.Sqrt(index);
            return ArenaLayout.MarbleCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
        }

        public static void DebugPlacePlayer(MatchSimState state, int index, Vector2 position, Vector2 facing)
        {
            PlayerSim p = state.Players[index];
            p.Position = position;
            p.Velocity = Vector2.zero;
            p.Facing = facing.sqrMagnitude < 0.0001f ? PlayerChargeMath.SpawnFacing(index) : facing.normalized;
            p.Boost = BoostPhase.Normal;
            p.BoostTime = 0f;
            p.ImpactCoast = 0f;
            p.Charge = ChargePhase.None;
            p.ChargeTime = 0f;
            state.Players[index] = p;
        }

        public static void DebugSetMarble(MatchSimState state, int index, Vector2 position, Vector2 velocity)
        {
            MarbleSim m = state.Marbles[index];
            m.Active = true;
            m.Position = position;
            m.Velocity = velocity;
            m.Carrier = -1;
            state.Marbles[index] = m;
            for (int i = 0; i < 2; i++)
            {
                if (state.Players[i].Carried == index)
                {
                    PlayerSim p = state.Players[i];
                    p.Carried = -1;
                    p.Charge = ChargePhase.None;
                    state.Players[i] = p;
                }
            }
        }

        public static bool DebugCollect(MatchSimState state, int player, int marble)
        {
            var events = new SimEvents();
            if (!WellOverlaps(state.Players[player], state.Marbles[marble].Position))
                return false;
            return TryCollect(state, player, marble, ref events);
        }

        public static bool DebugShoot(MatchSimState state, int player)
        {
            var events = new SimEvents();
            PlayerSim p = state.Players[player];
            PlayerInput shoot = new PlayerInput { ShootPressed = true };
            HandleShoot(ref p, player, shoot, state, ref events);
            state.Players[player] = p;
            return events.Fired0 || events.Fired1 || p.Charge != ChargePhase.None;
        }

        static void BeginKickoff(MatchSimState state)
        {
            state.Phase = MatchState.Kickoff;
            state.PhaseTicks = KickoffTicks;
            state.GateClosed = true;
            state.LastScorer = -1;
            state.LastScoreReason = "";
            ClearMarbles(state);
            PlacePlayers(state);
        }

        static void PlacePlayers(MatchSimState state)
        {
            state.Players[0] = SpawnedPlayer(0, ArenaLayout.PlayerOneSpawn);
            state.Players[1] = SpawnedPlayer(1, ArenaLayout.PlayerTwoSpawn);
        }

        static PlayerSim SpawnedPlayer(int index, Vector2 spawn)
        {
            return new PlayerSim
            {
                Position = spawn,
                Velocity = Vector2.zero,
                Facing = PlayerChargeMath.SpawnFacing(index),
                Boost = BoostPhase.Normal,
                Carried = -1
            };
        }

        static void ClearMarbles(MatchSimState state)
        {
            for (int i = 0; i < state.Marbles.Length; i++)
                state.Marbles[i] = default;
            for (int i = 0; i < 2; i++)
            {
                PlayerSim p = state.Players[i];
                p.Carried = -1;
                p.Charge = ChargePhase.None;
                p.ChargeTime = 0f;
                state.Players[i] = p;
            }
        }

        static void SpawnMarbles(MatchSimState state)
        {
            ClearMarbles(state);
            int count = state.MarbleCountSetting;
            for (int i = 0; i < count; i++)
            {
                state.Marbles[i] = new MarbleSim
                {
                    Active = true,
                    Position = ClusterOffset(i, count),
                    Velocity = Vector2.zero,
                    Carrier = -1
                };
            }
        }

        static void TickCooldowns(MatchSimState state)
        {
            for (int i = 0; i < 2; i++)
            {
                PlayerSim p = state.Players[i];
                p.CollectLock = Mathf.Max(0f, p.CollectLock - TickDt);
                p.ImpactCoast = Mathf.Max(0f, p.ImpactCoast - TickDt);
                state.Players[i] = p;
            }

            for (int i = 0; i < state.Marbles.Length; i++)
            {
                MarbleSim m = state.Marbles[i];
                m.ImpactLock0 = Mathf.Max(0f, m.ImpactLock0 - TickDt);
                m.ImpactLock1 = Mathf.Max(0f, m.ImpactLock1 - TickDt);
                state.Marbles[i] = m;
            }
        }

        static void StepPlayer(MatchSimState state, int index, PlayerInput input, ref SimEvents events)
        {
            PlayerSim p = state.Players[index];
            if (p.Charge != ChargePhase.Power)
                p.Facing = PlayerChargeMath.StepFacing(p.Facing, input.Move, TickDt);

            if (PlayerChargeMath.ShouldCancelPower(p.Charge, input.Move))
            {
                p.Charge = ChargePhase.None;
                p.ChargeTime = 0f;
            }

            HandleShoot(ref p, index, input, state, ref events);

            if (p.Charge == ChargePhase.Power)
            {
                p.Velocity = Vector2.zero;
                p.Boost = BoostPhase.Normal;
                p.BoostTime = 0f;
                p.ChargeTime += TickDt;
                state.Players[index] = p;
                return;
            }

            if (p.Charge == ChargePhase.Direction)
                p.ChargeTime += TickDt;

            PlayerMovementMath.AdvanceBoost(ref p.Boost, ref p.BoostTime, input.BoostHeld && p.Charge != ChargePhase.Power, TickDt);
            if (!(input.Move.sqrMagnitude < 0.0001f && p.ImpactCoast > 0f))
            {
                p.Velocity = PlayerMovementMath.StepVelocity(
                    p.Velocity,
                    input.Move,
                    p.Boost == BoostPhase.Boosting,
                    TickDt);
            }

            p.Velocity *= 1f / (1f + ArenaLayout.PlayerLinearDrag * TickDt);
            state.Players[index] = p;
        }

        static void HandleShoot(ref PlayerSim p, int index, PlayerInput input, MatchSimState state, ref SimEvents events)
        {
            if (!input.ShootPressed) return;
            if (p.Charge == ChargePhase.None)
            {
                if (!PlayerChargeMath.CanStartCharge(p.Carried >= 0, p.Charge))
                    return;
                p.Charge = ChargePhase.Direction;
                p.ChargeTime = 0f;
                return;
            }

            if (p.Charge == ChargePhase.Direction)
            {
                p.LockedShotDir = PlayerChargeMath.LockedShotDirection(
                    p.Facing,
                    PlayerChargeMath.DirectionOffsetDegrees(p.ChargeTime));
                p.Charge = ChargePhase.Power;
                p.ChargeTime = 0f;
                p.Velocity = Vector2.zero;
                p.Boost = BoostPhase.Normal;
                p.BoostTime = 0f;
                return;
            }

            if (p.Charge == ChargePhase.Power)
                Fire(ref p, index, state, ref events);
        }

        static void Fire(ref PlayerSim p, int index, MatchSimState state, ref SimEvents events)
        {
            if (p.Carried < 0)
            {
                p.Charge = ChargePhase.None;
                p.ChargeTime = 0f;
                return;
            }

            int marbleIndex = p.Carried;
            float power = PlayerChargeMath.Power01(p.ChargeTime);
            Vector2 dir = p.LockedShotDir.sqrMagnitude < 0.0001f ? p.Facing : p.LockedShotDir.normalized;
            float speed = PlayerChargeMath.ShotSpeed(power);
            Vector2 basket = BasketOrigin(p);
            Vector2 launch = basket + dir * (ArenaLayout.BasketLength * 0.5f + ArenaLayout.MarbleRadius + 0.08f);
            MarbleSim m = state.Marbles[marbleIndex];
            m.Carrier = -1;
            m.Position = launch;
            m.Velocity = dir * speed;
            state.Marbles[marbleIndex] = m;
            p.Carried = -1;
            p.CollectLock = ArenaLayout.CollectLockSeconds;
            p.Charge = ChargePhase.None;
            p.ChargeTime = 0f;
            Vector2 recoil = PlayerChargeMath.RecoilImpulse(dir, power);
            p.Velocity += recoil / ArenaLayout.PlayerMass;
            p.ImpactCoast = ArenaLayout.ImpactCoastSeconds;
            if (index == 0)
            {
                events.Fired0 = true;
                events.FireAt0 = launch;
            }
            else
            {
                events.Fired1 = true;
                events.FireAt1 = launch;
            }
        }

        static void StepMarbles(MatchSimState state)
        {
            for (int i = 0; i < state.Marbles.Length; i++)
            {
                MarbleSim m = state.Marbles[i];
                if (!m.Active || m.Carrier >= 0) continue;
                m.Velocity *= 1f / (1f + ArenaLayout.MarbleLinearDrag * TickDt);
                if (m.Velocity.magnitude < ArenaLayout.MarbleMinRunSpeed * 0.25f &&
                    m.Velocity.sqrMagnitude < 0.04f)
                    m.Velocity = Vector2.zero;
                m.Velocity = Vector2.ClampMagnitude(m.Velocity, ArenaLayout.MarbleMaxSpeed);
                state.Marbles[i] = m;
            }
        }

        static void IntegrateAndCollide(MatchSimState state, ref SimEvents events)
        {
            for (int i = 0; i < 2; i++)
            {
                PlayerSim p = state.Players[i];
                p.Position += p.Velocity * TickDt;
                state.Players[i] = p;
            }

            for (int i = 0; i < state.Marbles.Length; i++)
            {
                MarbleSim m = state.Marbles[i];
                if (!m.Active || m.Carrier >= 0) continue;
                float speed = m.Velocity.magnitude;
                int sub = Mathf.Max(1, Mathf.CeilToInt(speed * TickDt / (ArenaLayout.MarbleRadius * 0.4f)));
                float sdt = TickDt / sub;
                for (int s = 0; s < sub; s++)
                {
                    m.Position += m.Velocity * sdt;
                    state.Marbles[i] = m;
                    ResolveStaticsForMarble(state, i);
                    m = state.Marbles[i];
                }
            }

            for (int iter = 0; iter < 6; iter++)
            {
                for (int i = 0; i < 2; i++)
                    ResolveStaticsForPlayer(state, i);
                for (int i = 0; i < state.Marbles.Length; i++)
                    ResolveStaticsForMarble(state, i);
                ResolvePlayers(state);
                ResolvePlayerMarbles(state, ref events);
                ResolveMarbles(state);
                ResolveBasketRims(state);
            }
        }

        static void ResolveStaticsForPlayer(MatchSimState state, int index)
        {
            PlayerSim p = state.Players[index];
            float r = ArenaLayout.PlayerRadius;
            for (int i = 0; i < Walls.Length; i++)
                BounceCircleAabb(ref p.Position, ref p.Velocity, r, Walls[i], ArenaLayout.Bounciness);
            BounceCircleAabb(ref p.Position, ref p.Velocity, r, NorthBlocker, ArenaLayout.Bounciness);
            BounceCircleAabb(ref p.Position, ref p.Velocity, r, SouthBlocker, ArenaLayout.Bounciness);
            if (state.GateClosed)
                BounceCircleAabb(ref p.Position, ref p.Velocity, r, GateBox, ArenaLayout.Bounciness);
            state.Players[index] = p;
        }

        static void ResolveStaticsForMarble(MatchSimState state, int index)
        {
            MarbleSim m = state.Marbles[index];
            if (!m.Active || m.Carrier >= 0) return;
            float r = ArenaLayout.MarbleRadius;
            for (int i = 0; i < Walls.Length; i++)
                BounceCircleAabb(ref m.Position, ref m.Velocity, r, Walls[i], ArenaLayout.Bounciness);
            BounceCircleAabb(ref m.Position, ref m.Velocity, r, EastBlocker, ArenaLayout.Bounciness);
            BounceCircleAabb(ref m.Position, ref m.Velocity, r, WestBlocker, ArenaLayout.Bounciness);
            if (state.GateClosed)
                BounceCircleAabb(ref m.Position, ref m.Velocity, r, GateBox, ArenaLayout.Bounciness);
            state.Marbles[index] = m;
        }

        static void ResolvePlayers(MatchSimState state)
        {
            PlayerSim a = state.Players[0];
            PlayerSim b = state.Players[1];
            BounceCircles(
                ref a.Position,
                ref a.Velocity,
                ArenaLayout.PlayerRadius,
                ArenaLayout.PlayerMass,
                ref b.Position,
                ref b.Velocity,
                ArenaLayout.PlayerRadius,
                ArenaLayout.PlayerMass,
                ArenaLayout.PlayerPlayerBounciness);
            state.Players[0] = a;
            state.Players[1] = b;
        }

        static void ResolveMarbles(MatchSimState state)
        {
            for (int i = 0; i < state.Marbles.Length; i++)
            {
                MarbleSim a = state.Marbles[i];
                if (!a.Active || a.Carrier >= 0) continue;
                for (int j = i + 1; j < state.Marbles.Length; j++)
                {
                    MarbleSim b = state.Marbles[j];
                    if (!b.Active || b.Carrier >= 0) continue;
                    BounceCircles(
                        ref a.Position,
                        ref a.Velocity,
                        ArenaLayout.MarbleRadius,
                        ArenaLayout.MarbleMass,
                        ref b.Position,
                        ref b.Velocity,
                        ArenaLayout.MarbleRadius,
                        ArenaLayout.MarbleMass,
                        ArenaLayout.Bounciness);
                    state.Marbles[i] = a;
                    state.Marbles[j] = b;
                }
            }
        }

        static void ResolvePlayerMarbles(MatchSimState state, ref SimEvents events)
        {
            for (int p = 0; p < 2; p++)
            {
                PlayerSim player = state.Players[p];
                for (int i = 0; i < state.Marbles.Length; i++)
                {
                    MarbleSim m = state.Marbles[i];
                    if (!m.Active || m.Carrier >= 0) continue;
                    float impact = Mathf.Max(m.Velocity.magnitude, (m.Velocity - player.Velocity).magnitude);
                    bool hit = BounceCircles(
                        ref player.Position,
                        ref player.Velocity,
                        ArenaLayout.PlayerRadius,
                        ArenaLayout.PlayerMass,
                        ref m.Position,
                        ref m.Velocity,
                        ArenaLayout.MarbleRadius,
                        ArenaLayout.MarbleMass,
                        ArenaLayout.Bounciness);
                    if (!hit) continue;

                    float extra = MarbleMotor.CalculateAdditionalImpulse(impact);
                    float lockTime = p == 0 ? m.ImpactLock0 : m.ImpactLock1;
                    if (extra > 0f && lockTime <= 0f)
                    {
                        Vector2 dir = player.Position - m.Position;
                        if (dir.sqrMagnitude < 0.001f) dir = m.Velocity;
                        if (dir.sqrMagnitude < 0.001f) dir = Vector2.up;
                        dir.Normalize();
                        player.Velocity += dir * (extra / ArenaLayout.PlayerMass);
                        player.ImpactCoast = ArenaLayout.ImpactCoastSeconds;
                        if (p == 0) m.ImpactLock0 = ArenaLayout.MarbleImpactCooldown;
                        else m.ImpactLock1 = ArenaLayout.MarbleImpactCooldown;
                        if (impact >= ArenaLayout.MarbleDangerousSpeed)
                        {
                            m.DangerousImpactCount++;
                            m.Velocity *= ArenaLayout.MarbleImpactEnergyRetention;
                            events.DangerousImpact = true;
                            events.ImpactAt = m.Position;
                        }
                    }

                    state.Players[p] = player;
                    state.Marbles[i] = m;
                }
            }
        }

        static void ResolveBasketRims(MatchSimState state)
        {
            for (int p = 0; p < 2; p++)
            {
                PlayerSim player = state.Players[p];
                Vector2 origin = BasketOrigin(player);
                Vector2 forward = player.Facing.sqrMagnitude < 0.0001f ? Vector2.down : player.Facing.normalized;
                Vector2 right = new Vector2(forward.y, -forward.x);
                float w = ArenaLayout.BasketWidth;
                float h = ArenaLayout.BasketLength;
                float rim = ArenaLayout.BasketRim;
                for (int i = 0; i < state.Marbles.Length; i++)
                {
                    MarbleSim m = state.Marbles[i];
                    if (!m.Active || m.Carrier >= 0) continue;
                    BounceCircleObb(ref m.Position, ref m.Velocity, ArenaLayout.MarbleRadius, origin, right, forward,
                        new Vector2(0f, -h * 0.5f + rim * 0.5f), new Vector2(w, rim));
                    BounceCircleObb(ref m.Position, ref m.Velocity, ArenaLayout.MarbleRadius, origin, right, forward,
                        new Vector2(-w * 0.5f + rim * 0.5f, rim * 0.15f), new Vector2(rim, h - rim));
                    BounceCircleObb(ref m.Position, ref m.Velocity, ArenaLayout.MarbleRadius, origin, right, forward,
                        new Vector2(w * 0.5f - rim * 0.5f, rim * 0.15f), new Vector2(rim, h - rim));
                    state.Marbles[i] = m;
                }
            }
        }

        static void ProcessBaskets(MatchSimState state, ref SimEvents events)
        {
            for (int marble = 0; marble < state.Marbles.Length; marble++)
            {
                if (!state.Marbles[marble].Active) continue;
                for (int p = 0; p < 2; p++)
                {
                    if (state.Players[p].CollectLock > 0f) continue;
                    if (!WellOverlaps(state.Players[p], state.Marbles[marble].Position)) continue;
                    if (TryCollect(state, p, marble, ref events))
                        break;
                }
            }
        }

        static bool TryCollect(MatchSimState state, int player, int marble, ref SimEvents events)
        {
            MarbleSim m = state.Marbles[marble];
            if (!m.Active) return false;
            PlayerSim p = state.Players[player];
            if (p.CollectLock > 0f) return false;
            if (p.Carried >= 0 && p.Carried != marble) return false;
            if (m.Carrier == player) return false;
            if (m.Carrier >= 0 && m.Carrier != player)
            {
                PlayerSim other = state.Players[m.Carrier];
                other.Carried = -1;
                other.Charge = ChargePhase.None;
                other.ChargeTime = 0f;
                state.Players[m.Carrier] = other;
            }

            m.Carrier = player;
            m.Velocity = Vector2.zero;
            p.Carried = marble;
            state.Marbles[marble] = m;
            state.Players[player] = p;
            Vector2 at = BasketOrigin(p);
            if (player == 0)
            {
                events.Collected0 = true;
                events.CollectAt0 = at;
            }
            else
            {
                events.Collected1 = true;
                events.CollectAt1 = at;
            }

            return true;
        }

        static void WeldCarried(MatchSimState state)
        {
            for (int p = 0; p < 2; p++)
            {
                int carried = state.Players[p].Carried;
                if (carried < 0) continue;
                MarbleSim m = state.Marbles[carried];
                m.Position = WellWorld(state.Players[p]);
                m.Velocity = Vector2.zero;
                m.Carrier = p;
                state.Marbles[carried] = m;
            }
        }

        static void TryScore(MatchSimState state, ref SimEvents events)
        {
            bool p1Dunk = CircleAabbOverlap(state.Players[0].Position, ArenaLayout.PlayerRadius, EastWell) ||
                          CircleAabbOverlap(state.Players[0].Position, ArenaLayout.PlayerRadius, WestWell);
            bool p2Dunk = CircleAabbOverlap(state.Players[1].Position, ArenaLayout.PlayerRadius, EastWell) ||
                          CircleAabbOverlap(state.Players[1].Position, ArenaLayout.PlayerRadius, WestWell);
            bool north = false;
            bool south = false;
            for (int i = 0; i < state.Marbles.Length; i++)
            {
                MarbleSim m = state.Marbles[i];
                if (!m.Active || m.Carrier >= 0) continue;
                if (CircleAabbOverlap(m.Position, ArenaLayout.MarbleRadius, NorthWell)) north = true;
                if (CircleAabbOverlap(m.Position, ArenaLayout.MarbleRadius, SouthWell)) south = true;
            }

            int scorer = -1;
            string reason = "";
            if (p1Dunk)
            {
                scorer = 1;
                reason = "PLAYER 1 DUNKED";
            }
            else if (p2Dunk)
            {
                scorer = 0;
                reason = "PLAYER 2 DUNKED";
            }
            else if (north)
            {
                scorer = 0;
                reason = "P1 POCKET";
            }
            else if (south)
            {
                scorer = 1;
                reason = "P2 POCKET";
            }

            if (scorer < 0) return;
            if (scorer == 0) state.ScoreP1++;
            else state.ScoreP2++;
            state.Phase = MatchState.Scored;
            state.PhaseTicks = FreezeTicks;
            state.LastScorer = scorer;
            state.LastScoreReason = reason;
            events.Scored = true;
            events.Scorer = scorer;
            events.ScoreReason = reason;
        }

        static Vector2 BasketOrigin(PlayerSim p)
        {
            Vector2 facing = p.Facing.sqrMagnitude < 0.0001f ? Vector2.down : p.Facing.normalized;
            return p.Position + facing * ArenaLayout.BasketOffset;
        }

        static Vector2 WellWorld(PlayerSim p)
        {
            Vector2 origin = BasketOrigin(p);
            Vector2 forward = p.Facing.sqrMagnitude < 0.0001f ? Vector2.down : p.Facing.normalized;
            return origin + forward * (ArenaLayout.BasketRim * 0.2f);
        }

        static bool WellOverlaps(PlayerSim p, Vector2 marblePos)
        {
            Vector2 origin = BasketOrigin(p);
            Vector2 forward = p.Facing.sqrMagnitude < 0.0001f ? Vector2.down : p.Facing.normalized;
            Vector2 right = new Vector2(forward.y, -forward.x);
            Vector2 local = WorldToLocal(marblePos, origin, right, forward);
            local -= new Vector2(0f, ArenaLayout.BasketRim * 0.2f);
            float hw = (ArenaLayout.BasketWidth - ArenaLayout.BasketRim * 2.2f) * 0.5f;
            float hh = (ArenaLayout.BasketLength - ArenaLayout.BasketRim * 1.6f) * 0.5f;
            return CircleAabbLocal(local, ArenaLayout.MarbleRadius, hw, hh);
        }

        static Vector2 WorldToLocal(Vector2 world, Vector2 origin, Vector2 right, Vector2 forward)
        {
            Vector2 d = world - origin;
            return new Vector2(Vector2.Dot(d, right), Vector2.Dot(d, forward));
        }

        static bool CircleAabbLocal(Vector2 local, float radius, float halfW, float halfH)
        {
            float cx = Mathf.Clamp(local.x, -halfW, halfW);
            float cy = Mathf.Clamp(local.y, -halfH, halfH);
            float dx = local.x - cx;
            float dy = local.y - cy;
            return dx * dx + dy * dy <= radius * radius;
        }

        static bool CircleAabbOverlap(Vector2 center, float radius, Aabb box)
        {
            Vector2 closest = ClosestPoint(box, center);
            return (center - closest).sqrMagnitude <= radius * radius;
        }

        static Vector2 ClosestPoint(Aabb box, Vector2 p)
        {
            Vector2 min = box.Min;
            Vector2 max = box.Max;
            return new Vector2(Mathf.Clamp(p.x, min.x, max.x), Mathf.Clamp(p.y, min.y, max.y));
        }

        static void BounceCircleAabb(ref Vector2 pos, ref Vector2 vel, float radius, Aabb box, float bounciness)
        {
            if (!ResolveCircleAabb(ref pos, radius, box, out Vector2 n)) return;
            float vn = Vector2.Dot(vel, n);
            if (vn < 0f)
                vel -= (1f + bounciness) * vn * n;
        }

        static bool ResolveCircleAabb(ref Vector2 pos, float radius, Aabb box, out Vector2 normal)
        {
            Vector2 closest = ClosestPoint(box, pos);
            Vector2 delta = pos - closest;
            if (delta.sqrMagnitude > 0.0000001f)
            {
                float dist = delta.magnitude;
                if (dist >= radius)
                {
                    normal = Vector2.zero;
                    return false;
                }

                normal = delta / dist;
                pos += normal * (radius - dist);
                return true;
            }

            Vector2 min = box.Min;
            Vector2 max = box.Max;
            float left = pos.x - min.x;
            float right = max.x - pos.x;
            float down = pos.y - min.y;
            float up = max.y - pos.y;
            float smallest = Mathf.Min(Mathf.Min(left, right), Mathf.Min(down, up));
            if (smallest == left) { normal = Vector2.left; pos.x = min.x - radius; }
            else if (smallest == right) { normal = Vector2.right; pos.x = max.x + radius; }
            else if (smallest == down) { normal = Vector2.down; pos.y = min.y - radius; }
            else { normal = Vector2.up; pos.y = max.y + radius; }
            return true;
        }

        static bool BounceCircles(
            ref Vector2 pa,
            ref Vector2 va,
            float ra,
            float ma,
            ref Vector2 pb,
            ref Vector2 vb,
            float rb,
            float mb,
            float bounciness)
        {
            Vector2 d = pb - pa;
            float minDist = ra + rb;
            float distSq = d.sqrMagnitude;
            if (distSq > minDist * minDist || distSq < 0.00000001f)
            {
                if (distSq >= minDist * minDist) return false;
                d = Vector2.right;
                distSq = 0.00000001f;
            }

            float dist = Mathf.Sqrt(distSq);
            Vector2 n = d / dist;
            float pen = minDist - dist;
            float invA = 1f / ma;
            float invB = 1f / mb;
            pa -= n * pen * (invA / (invA + invB));
            pb += n * pen * (invB / (invA + invB));
            float vn = Vector2.Dot(va - vb, n);
            if (vn > 0f) return true;
            float j = -(1f + bounciness) * vn / (invA + invB);
            va += n * (j * invA);
            vb -= n * (j * invB);
            return true;
        }

        static void BounceCircleObb(
            ref Vector2 pos,
            ref Vector2 vel,
            float radius,
            Vector2 origin,
            Vector2 right,
            Vector2 forward,
            Vector2 localCenter,
            Vector2 size)
        {
            Vector2 worldCenter = origin + right * localCenter.x + forward * localCenter.y;
            Vector2 local = WorldToLocal(pos, worldCenter, right, forward);
            var box = new Aabb(Vector2.zero, size);
            Vector2 localPos = local;
            Vector2 localVel = new Vector2(Vector2.Dot(vel, right), Vector2.Dot(vel, forward));
            BounceCircleAabb(ref localPos, ref localVel, radius, box, ArenaLayout.Bounciness);
            pos = worldCenter + right * localPos.x + forward * localPos.y;
            vel = right * localVel.x + forward * localVel.y;
        }
    }
}
