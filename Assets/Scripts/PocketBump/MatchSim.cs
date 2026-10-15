using UnityEngine;

// 游戏的权威规则层：以固定 60 Hz 推进移动、碰撞、篮筐、计分和比赛状态机。
// 此层不读取实时输入、不驱动场景对象，也不调用 Physics2D，因此可以复制并回滚重演。
namespace PocketBump
{
    public static class MatchSim
    {
        public const float TickDt = ArenaLayout.SimTickDt;
        const float CircleNormalMinDistanceSquared = 0.00000001f;
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

        // 只构建一次静态碰撞区域。几何全部从 ArenaLayout 推导，固定构建和遍历顺序
        // 也是保证两端执行相同碰撞流程的一部分。
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
            // 这里只创建初始数据而不自动开赛，LAN 可以先完成握手，再让双方同时从 Tick 0 起步。
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
            // 只允许从未开始或已结束状态重开，避免中途调用破坏当前回合的可重演性。
            if (state.Phase != MatchState.Pregame && state.Phase != MatchState.MatchOver)
                return;
            state.ScoreP1 = 0;
            state.ScoreP2 = 0;
            BeginKickoff(state);
        }

        public static SimEvents Step(MatchSimState state, PlayerInput input0, PlayerInput input1)
        {
            // 一个 Tick 的顺序本身就是规则：移动 → 碰撞 → 焊接持球 → 抢夺 → 计分。
            // 固定顺序后，本地、远端和回滚重演才能对相同输入得到相同状态。
            var events = new SimEvents { Scorer = -1, Winner = -1 };
            if (state.Phase == MatchState.MatchOver || state.Phase == MatchState.Pregame)
                return events;

            if (state.Phase == MatchState.Scored)
            {
                // 得分冻结期间停止物理，让玩家看清结果；到期后再判断终局或重新开球。
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
                // 倒计时期间玩家可在各自半场移动，但门关闭且没有弹珠；结束时才开门并生成弹珠。
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
            // 黄金角螺旋让 1~8 颗弹珠围绕中心均匀散开，避免规则数量变化时重叠。
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
            m.StealContactLatched = false;
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
            MarbleSim m = state.Marbles[marble];
            if (m.Carrier < 0)
            {
                if (!WellOverlaps(state.Players[player], m.Position))
                    return false;
                return TryCollect(state, player, marble, false, ref events);
            }

            if (m.Carrier == player || m.StealContactLatched)
                return false;
            if (!BasketStealRegionsOverlap(state.Players[0], state.Players[1], 0f))
                return false;
            return TryCollect(state, player, marble, true, ref events);
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
                    Carrier = -1,
                    StealContactLatched = false
                };
            }
        }

        static void TickCooldowns(MatchSimState state)
        {
            // 冷却由固定 Tick 推进而不读取 Time.time，否则恢复旧状态后重演会产生不同结果。
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
            // 力量选择阶段角色必须站定并保持锁定方向；方向选择阶段仍允许边移动边瞄准。
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
            // ShootPressed 是 Tick 边沿：第一次选方向、第二次锁方向、第三次按当前力度发射。
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
            m.StealContactLatched = false;
            m.Position = launch;
            m.Velocity = dir * speed;
            state.Marbles[marbleIndex] = m;
            p.Carried = -1;
            p.CollectLock = ArenaLayout.CollectLockSeconds;
            p.Charge = ChargePhase.None;
            p.ChargeTime = 0f;
            Vector2 recoil = PlayerChargeMath.RecoilImpulse(dir, power);
            // impulse / mass 得到速度变化，让 PlayerMass 始终作为统一的惯性参数。
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
            // 只有自由弹珠参与阻力和限速；被持有弹珠会由 WeldCarried 直接跟随篮筐。
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
            // 玩家速度较稳定，可直接积分；高速小球则可能在一个 Tick 内跨过薄墙。
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
                // 按本 Tick 位移相对半径的比例自适应细分，降低离散检测造成的穿透。
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
                // 顺序冲量需要多轮迭代才能逐渐消除多物体接触穿透；固定 6 轮兼顾稳定与成本。
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
                MarbleSim m = state.Marbles[marble];
                if (!m.Active) continue;
                if (m.Carrier >= 0)
                {
                    ProcessCarriedSteal(state, marble, ref events);
                    continue;
                }

                for (int p = 0; p < 2; p++)
                {
                    if (state.Players[p].CollectLock > 0f) continue;
                    if (!WellOverlaps(state.Players[p], m.Position)) continue;
                    if (TryCollect(state, p, marble, false, ref events))
                        break;
                }
            }
        }

        static void ProcessCarriedSteal(MatchSimState state, int marble, ref SimEvents events)
        {
            MarbleSim m = state.Marbles[marble];
            bool retainedContact = BasketStealRegionsOverlap(
                state.Players[0],
                state.Players[1],
                ArenaLayout.BasketStealReleasePadding);
            if (m.StealContactLatched)
            {
                if (!retainedContact)
                {
                    m.StealContactLatched = false;
                    state.Marbles[marble] = m;
                }
                return;
            }

            if (!BasketStealRegionsOverlap(state.Players[0], state.Players[1], 0f))
                return;

            int thief = OpponentOf(m.Carrier);
            PlayerSim player = state.Players[thief];
            if (player.CollectLock > 0f || player.Carried >= 0)
                return;
            TryCollect(state, thief, marble, true, ref events);
        }

        static bool TryCollect(MatchSimState state, int player, int marble, bool latchSteal, ref SimEvents events)
        {
            MarbleSim m = state.Marbles[marble];
            if (!m.Active) return false;
            PlayerSim p = state.Players[player];
            if (p.CollectLock > 0f) return false;
            if (p.Carried >= 0 && p.Carried != marble) return false;
            if (m.Carrier == player) return false;
            if (latchSteal && (m.Carrier < 0 || m.StealContactLatched)) return false;
            if (!latchSteal && m.Carrier >= 0) return false;
            if (m.Carrier >= 0 && m.Carrier != player)
            {
                PlayerSim other = state.Players[m.Carrier];
                other.Carried = -1;
                other.Charge = ChargePhase.None;
                other.ChargeTime = 0f;
                state.Players[m.Carrier] = other;
            }

            m.Carrier = player;
            m.StealContactLatched = latchSteal;
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
            // 持球不使用物理约束，而是直接焊接到篮筐 well；这样结果稳定、状态也容易回滚。
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
            // 同 Tick 多事件必须有固定优先级，否则遍历顺序变化可能令两端比分不同。
            // 当前顺序：P1 掉落、P2 掉落、北侧弹珠、南侧弹珠，命中首项后结束回合。
            bool p1Dunk = CircleAabbOverlap(state.Players[0].Position, ArenaLayout.PlayerRadius, EastWell) ||
                          CircleAabbOverlap(state.Players[0].Position, ArenaLayout.PlayerRadius, WestWell);
            bool p2Dunk = CircleAabbOverlap(state.Players[1].Position, ArenaLayout.PlayerRadius, EastWell) ||
                          CircleAabbOverlap(state.Players[1].Position, ArenaLayout.PlayerRadius, WestWell);
            bool north = false;
            bool south = false;
            Vector2 northAt = default;
            Vector2 southAt = default;
            for (int i = 0; i < state.Marbles.Length; i++)
            {
                MarbleSim m = state.Marbles[i];
                if (!m.Active || m.Carrier >= 0) continue;
                if (!north && CircleAabbOverlap(m.Position, ArenaLayout.MarbleRadius, NorthWell))
                {
                    north = true;
                    northAt = m.Position;
                }
                if (!south && CircleAabbOverlap(m.Position, ArenaLayout.MarbleRadius, SouthWell))
                {
                    south = true;
                    southAt = m.Position;
                }
            }

            int scorer = -1;
            string reason = "";
            Vector2 scoreAt = default;
            if (p1Dunk)
            {
                scorer = 1;
                reason = "PLAYER 1 DUNKED";
                scoreAt = state.Players[0].Position;
            }
            else if (p2Dunk)
            {
                scorer = 0;
                reason = "PLAYER 2 DUNKED";
                scoreAt = state.Players[1].Position;
            }
            else if (north)
            {
                scorer = OpponentOf(0);
                reason = "P2 SCORES - P1 POCKET";
                scoreAt = northAt;
            }
            else if (south)
            {
                scorer = OpponentOf(1);
                reason = "P1 SCORES - P2 POCKET";
                scoreAt = southAt;
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
            events.ScoreAt = scoreAt;
        }

        static int OpponentOf(int player)
        {
            return player == 0 ? 1 : 0;
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

        static bool BasketStealRegionsOverlap(PlayerSim a, PlayerSim b, float padding)
        {
            Vector2 forwardA = a.Facing.sqrMagnitude < 0.0001f ? Vector2.down : a.Facing.normalized;
            Vector2 rightA = new Vector2(forwardA.y, -forwardA.x);
            Vector2 forwardB = b.Facing.sqrMagnitude < 0.0001f ? Vector2.down : b.Facing.normalized;
            Vector2 rightB = new Vector2(forwardB.y, -forwardB.x);
            Vector2 half = new Vector2(
                ArenaLayout.BasketWidth * 0.5f + padding,
                ArenaLayout.BasketLength * 0.5f + padding);
            Vector2 delta = BasketOrigin(b) - BasketOrigin(a);
            return OverlapsOnAxis(delta, rightA, rightA, forwardA, half, rightB, forwardB, half) &&
                   OverlapsOnAxis(delta, forwardA, rightA, forwardA, half, rightB, forwardB, half) &&
                   OverlapsOnAxis(delta, rightB, rightA, forwardA, half, rightB, forwardB, half) &&
                   OverlapsOnAxis(delta, forwardB, rightA, forwardA, half, rightB, forwardB, half);
        }

        static bool OverlapsOnAxis(
            Vector2 delta,
            Vector2 axis,
            Vector2 rightA,
            Vector2 forwardA,
            Vector2 halfA,
            Vector2 rightB,
            Vector2 forwardB,
            Vector2 halfB)
        {
            float distance = Mathf.Abs(Vector2.Dot(delta, axis));
            float radiusA = halfA.x * Mathf.Abs(Vector2.Dot(rightA, axis)) +
                            halfA.y * Mathf.Abs(Vector2.Dot(forwardA, axis));
            float radiusB = halfB.x * Mathf.Abs(Vector2.Dot(rightB, axis)) +
                            halfB.y * Mathf.Abs(Vector2.Dot(forwardB, axis));
            return distance <= radiusA + radiusB;
        }

        static Vector2 WorldToLocal(Vector2 world, Vector2 origin, Vector2 right, Vector2 forward)
        {
            // 投影到篮筐自身坐标系后，可把旋转 OBB 当成轴对齐矩形复用检测代码。
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
            // 找矩形上离圆心最近的点，再沿最短分离方向推出圆；圆心落在矩形内部时
            // 最近点等于圆心，因此下方分支改为选择距离最近的边。
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
            // 先按逆质量比例分离位置，再只对相向速度施加冲量，避免远离中的物体被二次反弹。
            Vector2 d = pb - pa;
            float minDist = ra + rb;
            float distSq = d.sqrMagnitude;
            if (distSq > minDist * minDist) return false;

            float dist = Mathf.Sqrt(distSq);
            // 同心/极近中心固定使用单位轴；真实距离仍用于穿透量，不能用微小距离放大伪法线。
            Vector2 n = distSq < CircleNormalMinDistanceSquared ? Vector2.right : d / dist;
            float pen = minDist - dist;
            float invA = 1f / ma;
            float invB = 1f / mb;
            pa -= n * pen * (invA / (invA + invB));
            pb += n * pen * (invB / (invA + invB));
            float vn = Vector2.Dot(va - vb, n);
            // n 从 A 指向 B，因此 va-vb 在 n 上为正才表示相向接近。
            if (vn <= 0f) return true;
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
            // 在 OBB 局部坐标中复用 Circle-AABB 求解，再把位置和速度旋回世界空间。
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
