/*
   Speed-up path of the Sonic library, ported to C# for whole-clip processing.

   Original: Sonic library, Copyright 2010 Bill Cox
   https://github.com/waywardgeek/sonic
   Licensed under the Apache License, Version 2.0
   (http://www.apache.org/licenses/LICENSE-2.0).

   Changes from the original: C# port; only speed > 1 (PICOLA for 1..2x,
   partial pitch-period skipping for >= 2x); processes a whole clip at once
   instead of streaming; float in/out.
*/
using System;

namespace DiscoVoiceSpeed;

internal static class Sonic
{
    const int MinPitch = 65;     // Hz
    const int MaxPitch = 400;    // Hz
    const int AmdfFreq = 4000;   // down-sample target for the coarse pitch search

    /// <summary>
    /// Time-stretch interleaved float audio to play <paramref name="speed"/> times
    /// faster without changing pitch. Returns interleaved samples
    /// (frames ≈ input frames / speed).
    /// </summary>
    public static float[] SpeedUp(float[] input, int channels, int sampleRate, float speed)
    {
        int inFrames = input.Length / channels;
        int expectedFrames = (int)(inFrames / speed + 0.5f);
        if (speed <= 1.00001f || inFrames == 0 || expectedFrames < 1)
            return (float[])input.Clone();

        var s = new State(sampleRate, channels);

        // Input as 16-bit, followed by silence so the tail gets processed (like sonicFlushStream).
        int padFrames = 2 * s.MaxRequired;
        int totalFrames = inFrames + padFrames;
        var inBuf = new short[totalFrames * channels];
        for (int i = 0; i < input.Length; i++)
        {
            float v = input[i] * 32767f;
            inBuf[i] = (short)(v > 32767f ? 32767 : v < -32768f ? -32768 : v);
        }

        var outBuf = new short[(expectedFrames + 4 * s.MaxRequired) * channels];
        int outFrames = 0;

        if (totalFrames >= s.MaxRequired)
        {
            double samplePeriod = 1.0 / sampleRate;
            double timeError = 0;
            int position = 0;
            do
            {
                if (speed < 2.0f && timeError < 0.0)
                {
                    // PICOLA: copy input unmodified until we are back on schedule.
                    double toCopy = 1 - timeError * speed / (samplePeriod * (speed - 1.0));
                    int available = totalFrames - position;
                    int n = toCopy > available ? available : (int)toCopy;
                    EnsureOut(ref outBuf, outFrames + n, channels);
                    Array.Copy(inBuf, position * channels, outBuf, outFrames * channels, n * channels);
                    outFrames += n;
                    timeError += n * samplePeriod * (speed - 1.0) / speed;
                    position += n;
                }
                else
                {
                    int period = s.FindPitchPeriod(inBuf, position);
                    // speed >= 2: skip part of each pitch period instead of whole periods.
                    int newSamples = speed >= 2.0f ? (int)(period / (speed - 1.0f)) : period;
                    if (newSamples < 1) newSamples = 1;
                    EnsureOut(ref outBuf, outFrames + newSamples, channels);
                    OverlapAdd(newSamples, channels, outBuf, outFrames * channels,
                               inBuf, position * channels, inBuf, (position + period) * channels);
                    outFrames += newSamples;
                    position += period + newSamples;
                    if (speed < 2.0f)
                        timeError += newSamples * samplePeriod - (period + newSamples) * samplePeriod / speed;
                }
            } while (position + s.MaxRequired <= totalFrames);
        }

        // Trim the extra output generated from the padding silence.
        int frames = Math.Min(outFrames, expectedFrames);
        var result = new float[expectedFrames * channels];
        for (int i = 0; i < frames * channels; i++)
            result[i] = outBuf[i] / 32767f;
        return result;
    }

    static void EnsureOut(ref short[] buf, int frames, int channels)
    {
        if (frames * channels > buf.Length)
            Array.Resize(ref buf, Math.Max(frames * channels, buf.Length + (buf.Length >> 1)));
    }

    static void OverlapAdd(int numSamples, int channels, short[] o, int oOff,
                           short[] down, int dOff, short[] up, int uOff)
    {
        for (int c = 0; c < channels; c++)
        {
            int oi = oOff + c, di = dOff + c, ui = uOff + c;
            for (int t = 0; t < numSamples; t++)
            {
                o[oi] = (short)((down[di] * (numSamples - t) + up[ui] * t) / numSamples);
                oi += channels; di += channels; ui += channels;
            }
        }
    }

    sealed class State
    {
        readonly int _channels, _sampleRate, _minPeriod, _maxPeriod;
        public readonly int MaxRequired;
        readonly short[] _down;
        int _prevPeriod, _prevMinDiff;

        public State(int sampleRate, int channels)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _minPeriod = sampleRate / MaxPitch;
            _maxPeriod = sampleRate / MinPitch;
            MaxRequired = 2 * _maxPeriod;
            _down = new short[MaxRequired];
        }

        int Skip => _sampleRate > AmdfFreq ? _sampleRate / AmdfFreq : 1;

        // Average `skip` frames (and all channels) into the down-sample buffer.
        void DownSample(short[] samples, int frameOffset, int skip)
        {
            int n = MaxRequired / skip;
            int perValue = _channels * skip;
            int si = frameOffset * _channels;
            for (int i = 0; i < n; i++)
            {
                int value = 0;
                for (int j = 0; j < perValue; j++) value += samples[si++];
                _down[i] = (short)(value / perValue);
            }
        }

        // AMDF search over [minPeriod, maxPeriod] on a mono buffer.
        static int FindInRange(short[] samples, int offset, int minPeriod, int maxPeriod,
                               out int retMinDiff, out int retMaxDiff)
        {
            int bestPeriod = 0, worstPeriod = 255;
            long minDiff = 1, maxDiff = 0;
            for (int period = minPeriod; period <= maxPeriod; period++)
            {
                long diff = 0;
                int si = offset, pi = offset + period;
                for (int i = 0; i < period; i++)
                {
                    int d = samples[si++] - samples[pi++];
                    diff += (ushort)(d >= 0 ? d : -d);
                }
                if (bestPeriod == 0 || diff * bestPeriod < minDiff * period)
                {
                    minDiff = diff;
                    bestPeriod = period;
                }
                if (diff * worstPeriod > maxDiff * period)
                {
                    maxDiff = diff;
                    worstPeriod = period;
                }
            }
            retMinDiff = (int)(minDiff / bestPeriod);
            retMaxDiff = (int)(maxDiff / worstPeriod);
            return bestPeriod;
        }

        bool PrevPeriodBetter(int minDiff, int maxDiff)
        {
            if (minDiff == 0 || _prevPeriod == 0) return false;
            if (maxDiff > minDiff * 3) return false;          // reasonable match this period
            if (minDiff * 2 <= _prevMinDiff * 3) return false; // mismatch not much worse
            return true;
        }

        public int FindPitchPeriod(short[] samples, int frameOffset)
        {
            int minPeriod = _minPeriod, maxPeriod = _maxPeriod;
            int skip = Skip, period, minDiff, maxDiff;

            if (_channels == 1 && skip == 1)
            {
                period = FindInRange(samples, frameOffset, minPeriod, maxPeriod, out minDiff, out maxDiff);
            }
            else
            {
                DownSample(samples, frameOffset, skip);
                period = FindInRange(_down, 0, minPeriod / skip, maxPeriod / skip, out minDiff, out maxDiff);
                if (skip != 1)
                {
                    period *= skip;
                    minPeriod = Math.Max(period - (skip << 2), _minPeriod);
                    maxPeriod = Math.Min(period + (skip << 2), _maxPeriod);
                    if (_channels == 1)
                    {
                        period = FindInRange(samples, frameOffset, minPeriod, maxPeriod, out minDiff, out maxDiff);
                    }
                    else
                    {
                        DownSample(samples, frameOffset, 1);
                        period = FindInRange(_down, 0, minPeriod, maxPeriod, out minDiff, out maxDiff);
                    }
                }
            }
            int ret = PrevPeriodBetter(minDiff, maxDiff) ? _prevPeriod : period;
            _prevMinDiff = minDiff;
            _prevPeriod = period;
            return ret;
        }
    }
}
