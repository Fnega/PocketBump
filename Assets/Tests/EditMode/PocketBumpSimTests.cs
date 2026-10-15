using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 直接驱动 MatchSim 与 RollbackSession，验证确定性、碰撞规则、回滚和协议编码。
// 绕过场景与帧率后，失败可以精确归因到权威模拟或网络会话逻辑。
namespace PocketBump.Tests
{
    public sealed class PocketBumpSimTests
    {
        // 统一创建已进入倒计时的状态，避免每个测试重复配置比赛初态。
        static MatchSimState Started(int marbles = 1)
        {
            var state = MatchSim.Create(new MatchSettings { marbleCount = marbles });
            MatchSim.StartMatch(state);
            return state;
        }

        // 精确推进 180 Tick 到 Live，测试同时约束三秒倒计时的 Tick 语义。
        static void StepLive(MatchSimState state)
        {
            PlayerInput n = PlayerInput.Neutral;
            for (int i = 0; i < MatchSim.KickoffTicks; i++)
                MatchSim.Step(state, n, n);
            Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
        }

        [Test]
        // 同初态与同输入带必须产生同校验和，这是回滚成立的首要前提。
        public void SameInputTapeProducesEqualChecksums()
        {
            var a = Started(3);
            var b = Started(3);
            var move = new PlayerInput { Move = Vector2.down, BoostHeld = true };
            for (int i = 0; i < 90; i++)
            {
                PlayerInput i0 = i % 3 == 0 ? move : PlayerInput.Neutral;
                PlayerInput i1 = i % 4 == 0 ? new PlayerInput { Move = Vector2.up } : PlayerInput.Neutral;
                MatchSim.Step(a, i0, i1);
                MatchSim.Step(b, i0, i1);
            }

            Assert.That(a.Checksum(), Is.EqualTo(b.Checksum()));
            Assert.That(a.ScoreP1, Is.EqualTo(b.ScoreP1));
            Assert.That(a.Players[0].Carried, Is.EqualTo(b.Players[0].Carried));
        }

        [Test]
        // 只有篮筐 well 能收集，角色身体重叠仍按普通碰撞处理。
        public void BasketScoopsAndBodyDoesNot()
        {
            var state = Started();
            StepLive(state);
            MatchSim.DebugPlacePlayer(state, 0, Vector2.zero, Vector2.right);
            MatchSim.DebugSetMarble(state, 0, Vector2.zero, Vector2.zero);
            Assert.That(MatchSim.DebugCollect(state, 0, 0), Is.False);
            Assert.That(state.Players[0].Carried, Is.EqualTo(-1));

            Vector2 well = Vector2.right * ArenaLayout.BasketOffset;
            MatchSim.DebugSetMarble(state, 0, well, Vector2.zero);
            Assert.That(MatchSim.DebugCollect(state, 0, 0), Is.True);
            Assert.That(state.Players[0].Carried, Is.EqualTo(0));
        }

        [Test]
        // 空篮筐可转移持有权，而被携带弹珠不应触发球门得分。
        public void ScoopStealsAndCarriedMarbleDoesNotScore()
        {
            var state = Started();
            StepLive(state);
            MatchSim.DebugPlacePlayer(state, 0, Vector2.zero, Vector2.right);
            MatchSim.DebugSetMarble(state, 0, Vector2.right * ArenaLayout.BasketOffset, Vector2.zero);
            Assert.That(MatchSim.DebugCollect(state, 0, 0), Is.True);

            MatchSim.DebugPlacePlayer(state, 1, new Vector2(ArenaLayout.BasketOffset * 2f, 0f), Vector2.left);
            MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(state.Players[1].Carried, Is.EqualTo(0));
            Assert.That(state.Players[0].Carried, Is.EqualTo(-1));

            MatchSim.DebugPlacePlayer(state, 1, new Vector2(0f, ArenaLayout.HalfHeight - 0.2f), Vector2.up);
            MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
            Assert.That(state.ScoreP1, Is.EqualTo(0));
        }

        [Test]
        // 回归曾出现的同 Tick 抢回 Bug，确保座位 0/1 的抢夺规则对称。
        public void PlayerOneStealsFromPlayerTwoThroughStepWithoutStealBack()
        {
            var state = Started();
            StepLive(state);
            MatchSim.DebugPlacePlayer(state, 1, new Vector2(ArenaLayout.BasketOffset * 2f, 0f), Vector2.left);
            MatchSim.DebugSetMarble(state, 0, Vector2.right * ArenaLayout.BasketOffset, Vector2.zero);
            Assert.That(MatchSim.DebugCollect(state, 1, 0), Is.True);
            Assert.That(state.Players[1].Carried, Is.EqualTo(0));

            MatchSim.DebugPlacePlayer(state, 0, Vector2.zero, Vector2.right);
            MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(state.Players[0].Carried, Is.EqualTo(0));
            Assert.That(state.Players[1].Carried, Is.EqualTo(-1));
        }

        [Test]
        // 两个座位使用同一篮筐接触规则，不依赖固定遍历优先级。
        public void VisibleBasketContactStealsSymmetrically()
        {
            MatchSimState fromP1 = LiveWithCarrier(
                0, Vector2.zero, Vector2.right,
                new Vector2(ArenaLayout.BasketOffset * 2f, 0f), Vector2.left);
            SimEvents p2Steal = MatchSim.Step(fromP1, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(fromP1.Players[1].Carried, Is.EqualTo(0));
            Assert.That(fromP1.Players[0].Carried, Is.EqualTo(-1));
            Assert.That(p2Steal.Collected1, Is.True);

            MatchSimState fromP2 = LiveWithCarrier(
                1, new Vector2(ArenaLayout.BasketOffset * 2f, 0f), Vector2.left,
                Vector2.zero, Vector2.right);
            SimEvents p1Steal = MatchSim.Step(fromP2, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(fromP2.Players[0].Carried, Is.EqualTo(0));
            Assert.That(fromP2.Players[1].Carried, Is.EqualTo(-1));
            Assert.That(p1Steal.Collected0, Is.True);
        }

        [Test]
        // 同一接触周期只能转移一次；持续重叠不能在后续 Tick 抢回或重复发事件。
        public void SustainedBasketContactTransfersOnceAndKeepsNewCarrier()
        {
            MatchSimState state = LiveWithCarrier(
                0, Vector2.zero, Vector2.right,
                new Vector2(ArenaLayout.BasketOffset * 2f, 0f), Vector2.left);
            int collectionEvents = 0;
            SimEvents first = MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            if (first.Collected0 || first.Collected1) collectionEvents++;

            for (int i = 0; i < 30; i++)
            {
                SimEvents next = MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
                if (next.Collected0 || next.Collected1) collectionEvents++;
                Assert.That(state.Players[1].Carried, Is.EqualTo(0), "Possession changed on sustained-contact tick " + i);
                Assert.That(state.Marbles[0].StealContactLatched, Is.True);
            }

            Assert.That(collectionEvents, Is.EqualTo(1));
        }

        [Test]
        // 明确分离会结束旧接触周期，重新进入后允许一次方向相反的抢夺。
        public void SeparationAndReentryAllowsCounterSteal()
        {
            MatchSimState state = LiveWithCarrier(
                0, Vector2.zero, Vector2.right,
                new Vector2(ArenaLayout.BasketOffset * 2f, 0f), Vector2.left);
            MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(state.Players[1].Carried, Is.EqualTo(0));
            Assert.That(state.Marbles[0].StealContactLatched, Is.True);

            MatchSim.DebugPlacePlayer(state, 0, new Vector2(-4f, 0f), Vector2.right);
            MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(state.Players[1].Carried, Is.EqualTo(0));
            Assert.That(state.Marbles[0].StealContactLatched, Is.False);

            MatchSim.DebugPlacePlayer(state, 0, Vector2.zero, Vector2.right);
            SimEvents counter = MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(state.Players[0].Carried, Is.EqualTo(0));
            Assert.That(state.Players[1].Carried, Is.EqualTo(-1));
            Assert.That(counter.Collected0, Is.True);
        }

        [Test]
        // 抢夺区域覆盖正面、侧面和擦边可见篮筐接触，但不把身体接触或间隙当成抢夺。
        public void StealGeometryCoversVisibleContactsWithoutBroadeningFreeCollection()
        {
            MatchSimState front = LiveWithCarrier(
                0, Vector2.zero, Vector2.right,
                new Vector2(ArenaLayout.BasketOffset * 2f, 0f), Vector2.left);
            MatchSim.Step(front, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(front.Players[1].Carried, Is.EqualTo(0));

            MatchSimState side = LiveWithCarrier(
                0, Vector2.zero, Vector2.right,
                new Vector2(ArenaLayout.BasketOffset, 1.2f), Vector2.down);
            MatchSim.Step(side, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(side.Players[1].Carried, Is.EqualTo(0));

            MatchSimState glancing = LiveWithCarrier(
                0, Vector2.zero, Vector2.right,
                new Vector2(ArenaLayout.BasketOffset * 2f, 0.9f), Vector2.left);
            MatchSim.Step(glancing, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(glancing.Players[1].Carried, Is.EqualTo(0));

            MatchSimState separated = LiveWithCarrier(
                0, Vector2.zero, Vector2.right,
                new Vector2(ArenaLayout.BasketOffset * 2f, 1.2f), Vector2.left);
            MatchSim.Step(separated, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(separated.Players[0].Carried, Is.EqualTo(0));

            MatchSimState bodyOnly = LiveWithCarrier(
                0, Vector2.zero, Vector2.right,
                new Vector2(ArenaLayout.PlayerRadius * 2f, 0f), Vector2.right);
            MatchSim.Step(bodyOnly, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(bodyOnly.Players[0].Carried, Is.EqualTo(0));

            MatchSimState free = Started();
            StepLive(free);
            MatchSim.DebugPlacePlayer(free, 0, Vector2.zero, Vector2.right);
            MatchSim.DebugSetMarble(free, 0, Vector2.zero, Vector2.zero);
            Assert.That(MatchSim.DebugCollect(free, 0, 0), Is.False);
            MatchSim.DebugSetMarble(free, 0, Vector2.right * ArenaLayout.BasketOffset, Vector2.zero);
            Assert.That(MatchSim.DebugCollect(free, 0, 0), Is.True);
        }

        [Test]
        // 接触锁属于可回滚状态，并在发射、调试释放和回合重置时清除。
        public void StealLatchClonesChecksumsAndClearsAcrossLifecycle()
        {
            MatchSimState state = LiveWithCarrier(
                0, Vector2.zero, Vector2.right,
                new Vector2(ArenaLayout.BasketOffset * 2f, 0f), Vector2.left);
            PlayerSim charging = state.Players[0];
            charging.Charge = ChargePhase.Power;
            charging.ChargeTime = 0.4f;
            state.Players[0] = charging;
            MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(state.Players[0].Charge, Is.EqualTo(ChargePhase.None));
            Assert.That(state.Marbles[0].StealContactLatched, Is.True);

            MatchSimState clone = state.Clone();
            Assert.That(clone.Marbles[0].StealContactLatched, Is.True);
            Assert.That(clone.Checksum(), Is.EqualTo(state.Checksum()));
            MarbleSim changed = clone.Marbles[0];
            changed.StealContactLatched = false;
            clone.Marbles[0] = changed;
            Assert.That(clone.Checksum(), Is.Not.EqualTo(state.Checksum()));

            Assert.That(MatchSim.DebugShoot(state, 1), Is.True);
            Assert.That(MatchSim.DebugShoot(state, 1), Is.True);
            Assert.That(MatchSim.DebugShoot(state, 1), Is.True);
            Assert.That(state.Marbles[0].StealContactLatched, Is.False);

            MarbleSim stale = state.Marbles[0];
            stale.StealContactLatched = true;
            state.Marbles[0] = stale;
            MatchSim.DebugSetMarble(state, 0, Vector2.zero, Vector2.zero);
            Assert.That(state.Marbles[0].StealContactLatched, Is.False);

            stale = state.Marbles[0];
            stale.StealContactLatched = true;
            state.Marbles[0] = stale;
            state.Phase = MatchState.Scored;
            state.PhaseTicks = 1;
            MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(state.Marbles[0].StealContactLatched, Is.False);
            Assert.That(state.Marbles[0].Active, Is.False);
        }

        [Test]
        // 迟到输入触发恢复与重演后，持续接触锁、事件次数和最终校验和必须与基线一致。
        public void RollbackReplayPreservesSustainedStealContact()
        {
            MatchSimState initial = LiveWithCarrier(
                0, Vector2.zero, Vector2.right,
                new Vector2(ArenaLayout.BasketOffset * 2f, 0f), Vector2.left);
            initial.Tick = 0;
            var baseline = new RollbackSession(initial.Clone(), 0);
            var replayed = new RollbackSession(initial.Clone(), 0);
            var corrected = new PlayerInput { BoostHeld = true };
            baseline.ReceiveRemote(2, corrected);

            int baselineCollections = 0;
            for (int i = 0; i < 8; i++)
            {
                SimEvents events = baseline.Advance(PlayerInput.Neutral);
                if (events.Collected0 || events.Collected1) baselineCollections++;
            }

            int replayedCollections = 0;
            for (int i = 0; i < 5; i++)
            {
                SimEvents events = replayed.Advance(PlayerInput.Neutral);
                if (events.Collected0 || events.Collected1) replayedCollections++;
            }

            Assert.That(replayed.TryReceiveRemote(2, corrected), Is.True);
            SimEvents correctedEvents = replayed.Advance(PlayerInput.Neutral);
            if (correctedEvents.Collected0 || correctedEvents.Collected1) replayedCollections++;
            Assert.That(replayed.LastRollbackTick, Is.EqualTo(2));
            for (int i = 6; i < 8; i++)
            {
                SimEvents events = replayed.Advance(PlayerInput.Neutral);
                if (events.Collected0 || events.Collected1) replayedCollections++;
            }

            Assert.That(replayedCollections, Is.EqualTo(baselineCollections));
            Assert.That(replayed.State.Players[1].Carried, Is.EqualTo(0));
            Assert.That(replayed.State.Marbles[0].StealContactLatched, Is.True);
            Assert.That(replayed.State.Checksum(), Is.EqualTo(baseline.State.Checksum()));
        }

        [Test]
        // 锁定玩法调参：满力射击产生足够反冲，侧面 dunk 口保持设计尺寸。
        public void FullPowerShotEastRecoilsWestAndDunkOpeningIsTripled()
        {
            Assert.That(ArenaLayout.DunkPocketHeight, Is.EqualTo(6.45f).Within(0.001f));
            Assert.That(ArenaLayout.MarblePocketWidth, Is.EqualTo(3.4f).Within(0.001f));

            var shot = Started();
            StepLive(shot);
            MatchSim.DebugPlacePlayer(shot, 0, Vector2.zero, Vector2.right);
            MatchSim.DebugSetMarble(shot, 0, Vector2.right * ArenaLayout.BasketOffset, Vector2.zero);
            Assert.That(MatchSim.DebugCollect(shot, 0, 0), Is.True);
            Assert.That(MatchSim.DebugShoot(shot, 0), Is.True);
            Assert.That(MatchSim.DebugShoot(shot, 0), Is.True);
            PlayerSim p = shot.Players[0];
            p.ChargeTime = ArenaLayout.ChargeNeedlePeriod * 0.25f;
            p.Velocity = Vector2.zero;
            shot.Players[0] = p;
            MatchSim.DebugShoot(shot, 0);
            Assert.That(shot.Players[0].Carried, Is.EqualTo(-1));
            Assert.That(shot.Players[0].Velocity.x, Is.LessThanOrEqualTo(-8f));
        }

        [Test]
        // 南北门都是各自玩家防守的门；自由弹珠入门后必须由门主人的对手得分。
        public void MarbleGoalsAwardOpponentOfDefendedPocket()
        {
            Vector2 northGoal = new Vector2(0f, ArenaLayout.HalfHeight + ArenaLayout.Wall + 0.55f);
            var north = Started();
            StepLive(north);
            MatchSim.DebugSetMarble(north, 0, northGoal, Vector2.zero);
            SimEvents northEvent = MatchSim.Step(north, PlayerInput.Neutral, PlayerInput.Neutral);

            Assert.That(north.ScoreP1, Is.EqualTo(0));
            Assert.That(north.ScoreP2, Is.EqualTo(1));
            Assert.That(north.Phase, Is.EqualTo(MatchState.Scored));
            Assert.That(northEvent.Scorer, Is.EqualTo(1));
            Assert.That(northEvent.ScoreReason, Is.EqualTo("P2 SCORES - P1 POCKET"));
            Assert.That(northEvent.ScoreAt, Is.EqualTo(northGoal));

            Vector2 southGoal = new Vector2(0f, -ArenaLayout.HalfHeight - ArenaLayout.Wall - 0.55f);
            var south = Started();
            StepLive(south);
            MatchSim.DebugSetMarble(south, 0, southGoal, Vector2.zero);
            SimEvents southEvent = MatchSim.Step(south, PlayerInput.Neutral, PlayerInput.Neutral);

            Assert.That(south.ScoreP1, Is.EqualTo(1));
            Assert.That(south.ScoreP2, Is.EqualTo(0));
            Assert.That(south.Phase, Is.EqualTo(MatchState.Scored));
            Assert.That(southEvent.Scorer, Is.EqualTo(0));
            Assert.That(southEvent.ScoreReason, Is.EqualTo("P1 SCORES - P2 POCKET"));
            Assert.That(southEvent.ScoreAt, Is.EqualTo(southGoal));
        }

        [Test]
        // 同 Tick 多事件只计一次，且玩家掉落优先于弹珠进球；同时覆盖发射状态迁移。
        public void SimultaneousDunkBeatsMarbleAndChargeRecoilFires()
        {
            var state = Started();
            StepLive(state);
            MatchSim.DebugPlacePlayer(state, 0, new Vector2(ArenaLayout.HalfWidth + ArenaLayout.Wall + 0.55f, 0f), Vector2.right);
            MatchSim.DebugSetMarble(state, 0, new Vector2(0f, -ArenaLayout.HalfHeight - ArenaLayout.Wall - 0.55f), Vector2.zero);
            SimEvents scoreEvent = MatchSim.Step(state, PlayerInput.Neutral, PlayerInput.Neutral);
            Assert.That(state.ScoreP2, Is.EqualTo(1));
            Assert.That(state.ScoreP1, Is.EqualTo(0));
            Assert.That(state.Phase, Is.EqualTo(MatchState.Scored));
            Assert.That(scoreEvent.Scorer, Is.EqualTo(1));
            Assert.That(scoreEvent.ScoreReason, Is.EqualTo("PLAYER 1 DUNKED"));
            Assert.That(scoreEvent.ScoreAt, Is.EqualTo(state.Players[0].Position));

            var shot = Started();
            StepLive(shot);
            MatchSim.DebugPlacePlayer(shot, 0, Vector2.zero, Vector2.right);
            MatchSim.DebugSetMarble(shot, 0, Vector2.right * ArenaLayout.BasketOffset, Vector2.zero);
            MatchSim.DebugCollect(shot, 0, 0);
            MatchSim.DebugShoot(shot, 0);
            MatchSim.DebugShoot(shot, 0);
            Vector2 before = shot.Players[0].Velocity;
            MatchSim.DebugShoot(shot, 0);
            Assert.That(shot.Players[0].Carried, Is.EqualTo(-1));
            Assert.That(shot.Marbles[0].Velocity.sqrMagnitude, Is.GreaterThan(0.01f));
            Assert.That(shot.Players[0].Velocity.x, Is.LessThan(before.x));
        }

        [Test]
        // 覆盖弹珠自然停下、危险击退冷却和固定 180 Tick 开球流程。
        public void MarbleCoastsDangerousKnockbackHasCooldownAndKickoffIs180Ticks()
        {
            var state = Started();
            Assert.That(state.Phase, Is.EqualTo(MatchState.Kickoff));
            PlayerInput n = PlayerInput.Neutral;
            for (int i = 0; i < MatchSim.KickoffTicks - 1; i++)
                MatchSim.Step(state, n, n);
            Assert.That(state.Phase, Is.EqualTo(MatchState.Kickoff));
            MatchSim.Step(state, n, n);
            Assert.That(state.Phase, Is.EqualTo(MatchState.Live));
            Assert.That(state.Marbles[0].Active, Is.True);

            MatchSim.DebugSetMarble(state, 0, Vector2.zero, Vector2.right * 6f);
            MatchSim.DebugPlacePlayer(state, 0, ArenaLayout.PlayerOneSpawn, Vector2.down);
            MatchSim.DebugPlacePlayer(state, 1, ArenaLayout.PlayerTwoSpawn, Vector2.up);
            float previous = state.Marbles[0].Velocity.magnitude;
            for (int i = 0; i < 8; i++)
            {
                MatchSim.Step(state, n, n);
                Assert.That(state.Marbles[0].Velocity.magnitude, Is.LessThanOrEqualTo(previous + 0.02f));
                previous = state.Marbles[0].Velocity.magnitude;
            }

            for (int i = 0; i < 180; i++)
                MatchSim.Step(state, n, n);
            Assert.That(state.Marbles[0].Velocity.magnitude, Is.LessThan(ArenaLayout.MarbleMinRunSpeed));

            var hit = Started();
            StepLive(hit);
            MatchSim.DebugPlacePlayer(hit, 0, Vector2.zero, Vector2.right);
            MatchSim.DebugSetMarble(hit, 0, new Vector2(-1.2f, 0f), Vector2.right * 12f);
            for (int i = 0; i < 8; i++)
                MatchSim.Step(hit, n, n);
            Assert.That(hit.Marbles[0].DangerousImpactCount, Is.EqualTo(1));
            int counted = hit.Marbles[0].DangerousImpactCount;
            for (int i = 0; i < 3; i++)
                MatchSim.Step(hit, n, n);
            Assert.That(hit.Marbles[0].DangerousImpactCount, Is.EqualTo(counted));
        }

        [Test]
        // 迟到真实输入回滚后的最终状态必须等于“从一开始就知道输入”的基线；过旧缺口应中止。
        public void RollbackLateRemoteMatchesBaselineAndHoleAborts()
        {
            const int frames = 70;
            var tape0 = new PlayerInput[frames];
            var tape1 = new PlayerInput[frames];
            for (int i = 0; i < frames; i++)
            {
                if (i % 6 == 0) tape0[i] = new PlayerInput { Move = Vector2.down };
                if (i % 5 == 0) tape1[i] = new PlayerInput { Move = Vector2.up, BoostHeld = true };
            }

            var baseline = Started();
            int delay = RollbackSession.DelayTicks;
            int total = frames + 8;
            for (int t = 0; t < total; t++)
            {
                PlayerInput s0 = t < frames ? tape0[t] : PlayerInput.Neutral;
                PlayerInput s1 = t < frames ? tape1[t] : PlayerInput.Neutral;
                PlayerInput i0 = t >= delay ? (t - delay < frames ? tape0[t - delay] : PlayerInput.Neutral) : PlayerInput.Neutral;
                PlayerInput i1 = t >= delay ? (t - delay < frames ? tape1[t - delay] : PlayerInput.Neutral) : PlayerInput.Neutral;
                MatchSim.Step(baseline, i0, i1);
            }

            var sessA = new RollbackSession(Started(), 0);
            var sessB = new RollbackSession(Started(), 1);
            var pendingA = new List<(int arrive, int tick, PlayerInput input)>();
            var pendingB = new List<(int arrive, int tick, PlayerInput input)>();

            for (int t = 0; t < frames; t++)
            {
                Deliver(sessA, pendingA, t);
                Deliver(sessB, pendingB, t);
                sessA.Advance(tape0[t]);
                sessB.Advance(tape1[t]);
                Assert.That(sessA.Status, Is.EqualTo(RollbackStatus.Ok));
                Assert.That(sessB.Status, Is.EqualTo(RollbackStatus.Ok));
                int arrive = t % 4 == 0 ? t + 5 : t + 1;
                pendingB.Add((arrive, t + delay, tape0[t]));
                pendingA.Add((arrive, t + delay, tape1[t]));
            }

            for (int extra = 0; extra < 8; extra++)
            {
                int t = frames + extra;
                Deliver(sessA, pendingA, t);
                Deliver(sessB, pendingB, t);
                sessA.Advance(PlayerInput.Neutral);
                sessB.Advance(PlayerInput.Neutral);
            }

            Assert.That(sessA.State.Checksum(), Is.EqualTo(sessB.State.Checksum()));
            Assert.That(sessA.State.Checksum(), Is.EqualTo(baseline.Checksum()));

            var hole = new RollbackSession(Started(), 0);
            RollbackStatus status = RollbackStatus.Ok;
            int limit = RollbackSession.BufferTicks + RollbackSession.DelayTicks + 2;
            for (int i = 0; i < limit; i++)
            {
                hole.Advance(PlayerInput.Neutral);
                status = hole.Status;
                if (status == RollbackStatus.InputHole) break;
            }

            Assert.That(status, Is.EqualTo(RollbackStatus.InputHole));
        }

        [Test]
        // 收到校验值时若真实输入尚未连续确认，必须等待回滚完成后再比较修正过的历史。
        public void LateInputRefreshesHistoricalChecksumBeforeQueuedValidation()
        {
            var baseline = new RollbackSession(Started(), 0);
            var replayed = new RollbackSession(Started(), 0);
            var corrected = new PlayerInput { Move = Vector2.up, BoostHeld = true };
            baseline.ReceiveRemote(2, corrected);

            for (int i = 0; i < 3; i++)
            {
                baseline.Advance(PlayerInput.Neutral);
                replayed.Advance(PlayerInput.Neutral);
            }

            int expectedTickTwo = baseline.ChecksumAt(2);
            Assert.That(replayed.ChecksumAt(2), Is.Not.EqualTo(expectedTickTwo));
            Assert.That(replayed.QueueRemoteChecksum(2, expectedTickTwo), Is.True);

            // Tick 2 尚未确认，此帧不能拿预测历史误报 DESYNC。
            baseline.Advance(PlayerInput.Neutral);
            replayed.Advance(PlayerInput.Neutral);
            Assert.That(replayed.Status, Is.EqualTo(RollbackStatus.Ok));

            replayed.ReceiveRemote(2, corrected);
            baseline.Advance(PlayerInput.Neutral);
            replayed.Advance(PlayerInput.Neutral);

            Assert.That(replayed.LastRollbackTick, Is.EqualTo(2));
            Assert.That(replayed.Status, Is.EqualTo(RollbackStatus.Ok));
            Assert.That(replayed.ChecksumAt(2), Is.EqualTo(expectedTickTwo));
            Assert.That(replayed.State.Checksum(), Is.EqualTo(baseline.State.Checksum()));
        }

        [Test]
        public void ConfirmedChecksumMismatchStillReportsDesync()
        {
            var session = new RollbackSession(Started(), 0);
            session.ReceiveRemote(2, new PlayerInput { Move = Vector2.down });
            for (int i = 0; i < 3; i++)
                session.Advance(PlayerInput.Neutral);

            Assert.That(session.ConfirmedTick, Is.GreaterThanOrEqualTo(2));
            Assert.That(session.TryGetConfirmedChecksum(2, out int tick, out int checksum), Is.True);
            Assert.That(tick, Is.EqualTo(2));
            Assert.That(session.QueueRemoteChecksum(tick, checksum ^ 1), Is.True);

            session.Advance(PlayerInput.Neutral);
            Assert.That(session.Status, Is.EqualTo(RollbackStatus.Desync));
        }

        [Test]
        // 握手必须拒绝规则不一致，并让通过的 Joiner 获得约定的第二座位。
        public void HandshakeRejectsSettingsMismatchAndJoinAckAssignsSeatTwo()
        {
            Assert.That(
                NetplayProtocol.CanAcceptJoin(8, 1, NetplayProtocol.Version, 8, 1, NetplayProtocol.SettingsHash(8, 1)),
                Is.True);
            Assert.That(
                NetplayProtocol.CanAcceptJoin(8, 1, NetplayProtocol.Version, 5, 3, NetplayProtocol.SettingsHash(5, 3)),
                Is.False);
            byte[] ack = LanUdpPeer.EncodeJoinAck(true, 1, 42, 8, 1);
            Assert.That(LanUdpPeer.TryReadHeader(ack, out byte type, out int offset), Is.True);
            Assert.That(type, Is.EqualTo(NetplayProtocol.JoinAck));
            Assert.That(ack[offset + 1], Is.EqualTo(1));
        }

        // 测试用可控网络：按指定延迟把一端输入交付给另一端，便于稳定制造预测与回滚。
        static void Deliver(
            RollbackSession session,
            List<(int arrive, int tick, PlayerInput input)> pending,
            int now)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].arrive > now) continue;
                session.ReceiveRemote(pending[i].tick, pending[i].input);
                pending.RemoveAt(i);
            }
        }

        static MatchSimState LiveWithCarrier(
            int carrier,
            Vector2 carrierPosition,
            Vector2 carrierFacing,
            Vector2 otherPosition,
            Vector2 otherFacing)
        {
            MatchSimState state = Started();
            StepLive(state);
            int other = carrier == 0 ? 1 : 0;
            MatchSim.DebugPlacePlayer(state, carrier, carrierPosition, carrierFacing);
            MatchSim.DebugPlacePlayer(state, other, otherPosition, otherFacing);
            Vector2 facing = carrierFacing.sqrMagnitude < 0.0001f
                ? PlayerChargeMath.SpawnFacing(carrier)
                : carrierFacing.normalized;
            MatchSim.DebugSetMarble(
                state,
                0,
                carrierPosition + facing * ArenaLayout.BasketOffset,
                Vector2.zero);
            Assert.That(MatchSim.DebugCollect(state, carrier, 0), Is.True);
            return state;
        }
    }
}
