using System;
using System.IO;
using UnityEngine;

namespace Silence1
{
    // Measurement only (-visitMeasure): copies the listener's final mix, sample-exact, from a given DSP sample.
    // Sits on the AudioListener's GameObject, so it sees exactly what goes to the output device.
    public sealed class VisitTap : MonoBehaviour
    {
        float[] buffer = Array.Empty<float>();
        long from;
        int frames, channels;
        volatile int written;
        volatile bool armed;
        int rate;

        public bool Done => armed && written >= frames;
        public int Channels => channels;

        void Awake() => rate = AudioSettings.outputSampleRate;

        public void Arm(long startSample, int lengthFrames)
        {
            armed = false;
            int ch = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            if (buffer.Length < lengthFrames * ch) buffer = new float[lengthFrames * ch];
            Array.Clear(buffer, 0, lengthFrames * ch);
            from = startSample;
            frames = lengthFrames;
            channels = ch;
            written = 0;
            armed = true;
        }

        void OnAudioFilterRead(float[] data, int ch)
        {
            if (!armed || written >= frames) return;
            // dspTime read inside the callback is the first sample of this buffer (Unity's own metronome example).
            long s0 = (long)Math.Round(AudioSettings.dspTime * rate);
            int n = data.Length / ch, copy = Math.Min(ch, channels);
            int last = written;
            for (int i = 0; i < n; i++)
            {
                long k = s0 + i - from;
                if (k < 0 || k >= frames) continue;
                for (int c = 0; c < copy; c++) buffer[k * channels + c] = data[i * ch + c];
                last = (int)Math.Max(last, k + 1);
            }
            written = last;
        }

        // 32-bit float WAV: the SILENCE1 exterior layer sits too low for a 16-bit file to be a fair record.
        public void Save(string path)
        {
            int count = frames * channels;
            using var w = new BinaryWriter(File.Create(path));
            w.Write("RIFF".ToCharArray()); w.Write(36 + count * 4); w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)3); w.Write((short)channels);
            w.Write(rate); w.Write(rate * channels * 4); w.Write((short)(channels * 4)); w.Write((short)32);
            w.Write("data".ToCharArray()); w.Write(count * 4);
            for (int i = 0; i < count; i++) w.Write(buffer[i]);
            armed = false;
        }
    }
}
