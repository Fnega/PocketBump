using System;
using UnityEngine;

namespace PocketBump
{
    public struct PlayerSim
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 Facing;
        public BoostPhase Boost;
        public float BoostTime;
        public float ImpactCoast;
        public ChargePhase Charge;
        public float ChargeTime;
        public Vector2 LockedShotDir;
        public int Carried;
        public float CollectLock;
    }

    public struct MarbleSim
    {
        public bool Active;
        public Vector2 Position;
        public Vector2 Velocity;
        public int Carrier;
        public float ImpactLock0;
        public float ImpactLock1;
        public int DangerousImpactCount;
    }

    public sealed class MatchSimState
    {
        public int Tick;
        public MatchState Phase;
        public int PhaseTicks;
        public bool GateClosed;
        public int ScoreP1;
        public int ScoreP2;
        public int PointsToWin;
        public int MarbleCountSetting;
        public int LastScorer;
        public string LastScoreReason;
        public PlayerSim[] Players = new PlayerSim[2];
        public MarbleSim[] Marbles = new MarbleSim[MatchSettings.MaxMarbles];

        public MatchSimState Clone()
        {
            var copy = new MatchSimState
            {
                Tick = Tick,
                Phase = Phase,
                PhaseTicks = PhaseTicks,
                GateClosed = GateClosed,
                ScoreP1 = ScoreP1,
                ScoreP2 = ScoreP2,
                PointsToWin = PointsToWin,
                MarbleCountSetting = MarbleCountSetting,
                LastScorer = LastScorer,
                LastScoreReason = LastScoreReason
            };
            for (int i = 0; i < 2; i++)
                copy.Players[i] = Players[i];
            for (int i = 0; i < Marbles.Length; i++)
                copy.Marbles[i] = Marbles[i];
            return copy;
        }

        public void CopyFrom(MatchSimState other)
        {
            Tick = other.Tick;
            Phase = other.Phase;
            PhaseTicks = other.PhaseTicks;
            GateClosed = other.GateClosed;
            ScoreP1 = other.ScoreP1;
            ScoreP2 = other.ScoreP2;
            PointsToWin = other.PointsToWin;
            MarbleCountSetting = other.MarbleCountSetting;
            LastScorer = other.LastScorer;
            LastScoreReason = other.LastScoreReason;
            for (int i = 0; i < 2; i++)
                Players[i] = other.Players[i];
            for (int i = 0; i < Marbles.Length; i++)
                Marbles[i] = other.Marbles[i];
        }

        public int Checksum()
        {
            unchecked
            {
                int h = Tick;
                h = Mix(h, (int)Phase);
                h = Mix(h, PhaseTicks);
                h = Mix(h, GateClosed ? 1 : 0);
                h = Mix(h, ScoreP1);
                h = Mix(h, ScoreP2);
                h = Mix(h, LastScorer);
                for (int i = 0; i < 2; i++)
                {
                    h = Mix(h, Players[i].Position);
                    h = Mix(h, Players[i].Velocity);
                    h = Mix(h, Players[i].Facing);
                    h = Mix(h, (int)Players[i].Boost);
                    h = Mix(h, Players[i].BoostTime);
                    h = Mix(h, (int)Players[i].Charge);
                    h = Mix(h, Players[i].ChargeTime);
                    h = Mix(h, Players[i].Carried);
                }

                for (int i = 0; i < Marbles.Length; i++)
                {
                    h = Mix(h, Marbles[i].Active ? 1 : 0);
                    h = Mix(h, Marbles[i].Position);
                    h = Mix(h, Marbles[i].Velocity);
                    h = Mix(h, Marbles[i].Carrier);
                    h = Mix(h, Marbles[i].DangerousImpactCount);
                }

                return h;
            }
        }

        static int Mix(int h, int v)
        {
            unchecked
            {
                return (h * 16777619) ^ v;
            }
        }

        static int Mix(int h, Vector2 v)
        {
            return Mix(Mix(h, FloatBits(v.x)), FloatBits(v.y));
        }

        static int Mix(int h, float v)
        {
            return Mix(h, FloatBits(v));
        }

        static int FloatBits(float v)
        {
            byte[] bytes = BitConverter.GetBytes(v);
            return BitConverter.ToInt32(bytes, 0);
        }
    }

    public struct SimEvents
    {
        public bool GateOpened;
        public bool Scored;
        public int Scorer;
        public string ScoreReason;
        public bool MatchOver;
        public int Winner;
        public bool Collected0;
        public bool Collected1;
        public Vector2 CollectAt0;
        public Vector2 CollectAt1;
        public bool Fired0;
        public bool Fired1;
        public Vector2 FireAt0;
        public Vector2 FireAt1;
        public bool DangerousImpact;
        public Vector2 ImpactAt;
    }
}
