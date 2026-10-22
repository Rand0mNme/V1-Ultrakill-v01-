using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace V1Mod.Modules
{
    // ULTRAKILL sounds (.wav from the Sounds folder next to the dll; converted from .ogg beforehand with _tools/OggToWav).
    // Unity's built-in audio is disabled in RoR2 (the whole game plays through Wwise),
    // so we play them ourselves through NAudio straight to the sound card.
    public static class V1Sounds
    {
        public const int SampleRate = 44100;
        public const int Channels = 2;

        private static readonly Dictionary<string, float[]> clips = new Dictionary<string, float[]>();
        private static MixingSampleProvider mixer;
        private static IWavePlayer output;

        public static void LoadAll(string pluginDir)
        {
            string dir = Path.Combine(pluginDir, "Sounds");
            if (!Directory.Exists(dir))
            {
                Log.Warning("Sounds folder not found: " + dir);
                return;
            }
            foreach (string file in Directory.GetFiles(dir, "*.wav"))
            {
                try
                {
                    clips[Path.GetFileNameWithoutExtension(file)] = Decode(file);
                }
                catch (Exception e)
                {
                    Log.Warning("Failed to load sound " + file + ": " + e.Message);
                }
            }
            Log.Info("Sounds loaded: " + clips.Count);

            mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels)) { ReadFully = true };
            OpenOutput();
        }

        // RoR2 (Wwise) restarts audio when entering a run, and Windows drops our output (NoDriver).
        // So if the output stops, reopen it a second later.
        private static volatile bool needRestart;
        private static float nextRestart;
        private static int restarts;

        private static void OpenOutput()
        {
            try
            {
                output?.Dispose();
            }
            catch
            {
            }
            output = null;
            try
            {
                // keep audio latency low: players time windows like the coin split (67 ms) by sound
                var o = new WaveOutEvent { DesiredLatency = 40, NumberOfBuffers = 3 };
                o.PlaybackStopped += (s, e) =>
                {
                    if (restarts < 3 || e.Exception != null && restarts % 20 == 0)
                    {
                        Log.Warning("Audio stopped, reopening: " + (e.Exception != null ? e.Exception.Message : "no error"));
                    }
                    needRestart = true;
                };
                o.Init(new CountingProvider(mixer));
                o.Play();
                output = o;
                Log.Info("Audio device opened: " + o.PlaybackState);
            }
            catch (Exception e)
            {
                Log.Error("Failed to open audio device: " + e.Message);
                needRestart = true;
            }
        }

        // wav (16 bit, 44100 Hz, stereo) -> float
        private static float[] Decode(string file)
        {
            byte[] bytes = File.ReadAllBytes(file);
            int pos = 12;
            while (pos + 8 <= bytes.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
                int size = BitConverter.ToInt32(bytes, pos + 4);
                if (id == "data")
                {
                    int samples = Math.Min(size, bytes.Length - pos - 8) / 2;
                    float[] data = new float[samples];
                    for (int i = 0; i < samples; i++)
                    {
                        data[i] = BitConverter.ToInt16(bytes, pos + 8 + i * 2) / 32768f;
                    }
                    return data;
                }
                pos += 8 + size + (size & 1);
            }
            throw new InvalidDataException("no audio data in file");
        }

        public static bool Has(string name) => clips.ContainsKey(name);

        // the audio thread can't read the game settings itself, so the plugin updates this value every frame
        public static volatile float GameVolumeCached = 1f;
        public static volatile bool Paused;

        private static float nextDiag;
        private static int playCount;
        public static volatile int readCalls;

        public static void UpdateVolume()
        {
            GameVolumeCached = GameVolume;
            // a looping sound whose owner is gone (saw broke, character died, map changed) is silenced here,
            // otherwise it could play forever if some code failed to stop it
            for (int i = loops.Count - 1; i >= 0; i--)
            {
                SoundHandle h = loops[i];
                if (h.Stopped || h.Owner == null || !h.Owner)
                {
                    h.Stop();
                    loops.RemoveAt(i);
                }
            }
            // RoR2 pause stops time; mod sounds are silent and don't advance meanwhile
            Paused = RoR2.PauseManager.isPaused;
            if (needRestart && mixer != null && Time.unscaledTime > nextRestart)
            {
                needRestart = false;
                nextRestart = Time.unscaledTime + 1f;
                restarts++;
                OpenOutput();
            }
            if (Time.unscaledTime > nextDiag && output != null)
            {
                nextDiag = Time.unscaledTime + 30f;
                Log.Info("Audio diagnostics: state " + output.PlaybackState + ", reads " + readCalls + ", sounds " + playCount + ", volume " + GameVolumeCached + ", restarts " + restarts);
            }
        }

        // Effects volume from the RoR2 settings (master x sfx)
        public static float GameVolume
        {
            get
            {
                return ReadConVar("volume_master") * ReadConVar("volume_sfx");
            }
        }

        private static float ReadConVar(string name)
        {
            try
            {
                var cv = RoR2.Console.instance ? RoR2.Console.instance.FindConVar(name) : null;
                if (cv != null && float.TryParse(cv.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                {
                    return Mathf.Clamp01(v / 100f);
                }
            }
            catch
            {
            }
            return 1f;
        }

        // One-shot sound. For other players' characters the volume falls off with distance to the camera.
        private static readonly System.Collections.Generic.List<SoundHandle> loops = new System.Collections.Generic.List<SoundHandle>();

        public static SoundHandle Play(string name, GameObject owner, float volume, float pitch, bool firstPerson, bool loop = false)
        {
            if (mixer == null || !clips.TryGetValue(name, out float[] data))
            {
                return null;
            }
            float distanceFactor = 1f;
            if (!firstPerson && owner && Camera.main)
            {
                float d = Vector3.Distance(Camera.main.transform.position, owner.transform.position);
                distanceFactor = Mathf.Clamp01(1f - (d - 5f) / 145f);
            }
            var handle = new SoundHandle(data, loop)
            {
                Volume = volume * distanceFactor,
                Pitch = pitch,
            };
            mixer.AddMixerInput(handle);
            if (loop)
            {
                handle.Owner = owner;
                loops.Add(handle);
            }
            if (playCount++ < 5)
            {
                Log.Info("Playing sound " + name + ", volume " + handle.Volume + ", pitch " + pitch);
            }
            return handle;
        }
    }

    // A sound that can be changed on the fly (volume, pitch) and stopped.
    public class SoundHandle : ISampleProvider
    {
        private readonly float[] data;
        private readonly bool loop;
        private double position;
        private bool stopped;

        public float Volume;
        public float Pitch = 1f;

        public SoundHandle(float[] data, bool loop)
        {
            this.data = data;
            this.loop = loop;
        }

        public WaveFormat WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(V1Sounds.SampleRate, V1Sounds.Channels);

        public void Stop() => stopped = true;
        public bool Stopped => stopped;
        public GameObject Owner;

        public int Read(float[] buffer, int offset, int count)
        {
            if (stopped)
            {
                return 0;
            }
            if (V1Sounds.Paused)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }
            int frames = data.Length / 2;
            float vol = Volume * V1Sounds.GameVolumeCached;
            float pitch = Mathf.Max(0.01f, Pitch);
            int written = 0;
            for (int i = 0; i < count; i += 2)
            {
                if (position >= frames - 1)
                {
                    if (!loop)
                    {
                        break;
                    }
                    position -= frames - 1;
                }
                int p = (int)position;
                float t = (float)(position - p);
                int a = p * 2;
                int b = a + 2;
                buffer[offset + i] = (data[a] + (data[b] - data[a]) * t) * vol;
                buffer[offset + i + 1] = (data[a + 1] + (data[b + 1] - data[a + 1]) * t) * vol;
                position += pitch;
                written += 2;
            }
            return written;
        }
    }
}

namespace V1Mod.Modules
{
    // counts the sound card's reads from the mixer (for diagnostics)
    public class CountingProvider : NAudio.Wave.ISampleProvider
    {
        private readonly NAudio.Wave.ISampleProvider source;
        public CountingProvider(NAudio.Wave.ISampleProvider source) { this.source = source; }
        public NAudio.Wave.WaveFormat WaveFormat => source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            V1Sounds.readCalls++;
            return source.Read(buffer, offset, count);
        }
    }
}
