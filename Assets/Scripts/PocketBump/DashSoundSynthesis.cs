using System;

namespace PocketBump
{
    public enum DashSoundCue { Dash, Impact, WallBreak, GreenPickup, BluePickup, PurplePickup, Goal, Respawn }

    // Original procedural SFX: local oscillator/noise state, no samples or external assets.
    // Deliberately does not use UnityEngine.Random, gameplay RNG or simulation state.
    public static class DashSoundSynthesis
    {
        public const int SampleRate = 22050;
        const double Tau = Math.PI * 2;

        public static float[] Generate(DashSoundCue cue)
        {
            double duration;
            switch (cue)
            {
                case DashSoundCue.Dash: duration = .22; break;
                case DashSoundCue.Impact: duration = .13; break;
                case DashSoundCue.WallBreak: duration = .34; break;
                case DashSoundCue.GreenPickup:
                case DashSoundCue.BluePickup:
                case DashSoundCue.PurplePickup: duration = .38; break;
                case DashSoundCue.Goal: duration = .78; break;
                case DashSoundCue.Respawn: duration = .27; break;
                default: throw new ArgumentOutOfRangeException(nameof(cue));
            }
            var samples = new float[(int)Math.Ceiling(duration * SampleRate)];
            uint noiseState = 0x7af153u + (uint)cue * 977u;
            double previousNoise = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double t = i / (double)SampleRate, u = t / duration;
                noiseState ^= noiseState << 13; noiseState ^= noiseState >> 17; noiseState ^= noiseState << 5;
                double noise = (noiseState & 0xffff) / 32767.5 - 1;
                double value;
                switch (cue)
                {
                    case DashSoundCue.Dash:
                        value = ((noise - previousNoise) * .25 + Math.Sin(Tau * (220 * t + 780 * t * t)) * .12)
                            * Math.Sin(Math.PI * u) * Math.Exp(-1.5 * u);
                        break;
                    case DashSoundCue.Impact:
                        value = (Math.Sin(Tau * (165 * t - 370 * t * t)) * .65 + noise * .25 * Math.Exp(-45 * t))
                            * Math.Exp(-29 * t);
                        break;
                    case DashSoundCue.WallBreak:
                        value = (noise * .62 + Math.Sin(Tau * 115 * t) * .16) * Math.Exp(-9 * t)
                            * (.55 + .45 * Math.Abs(Math.Sin(Tau * 23 * t)));
                        break;
                    case DashSoundCue.Goal:
                        value = Bell(t, 523.25, .28) + Bell(t - .12, 659.25, .28)
                            + Bell(t - .24, 783.99, .28) + Bell(t - .36, 1046.5, .32);
                        break;
                    case DashSoundCue.Respawn:
                        value = Math.Sin(Tau * (440 * t + 620 * t * t)) * .33 * Math.Sin(Math.PI * u);
                        break;
                    default:
                        double pitch = cue == DashSoundCue.GreenPickup ? 523.25 : cue == DashSoundCue.BluePickup ? 587.33 : 659.25;
                        value = Bell(t, pitch, .30) + Bell(t - .09, pitch * 1.25, .30) + Bell(t - .18, pitch * 1.5, .30);
                        break;
                }
                previousNoise = noise;
                // Short attack/tail avoids clicks, including the very last sample.
                double envelope = Math.Min(1, t / .004) * Math.Min(1, (samples.Length - 1 - i) / (SampleRate * .025));
                samples[i] = (float)Math.Max(-.7, Math.Min(.7, value * envelope));
            }
            return samples;
        }

        static double Bell(double t, double frequency, double amplitude)
        {
            if (t < 0) return 0;
            return amplitude * (Math.Sin(Tau * frequency * t) + .15 * Math.Sin(Tau * frequency * 2 * t))
                * Math.Min(1, t / .006) * Math.Exp(-12 * t);
        }
    }
}
