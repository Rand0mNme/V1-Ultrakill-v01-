// Converts .ogg to .wav (16 bit, 44100 Hz, stereo) so the mod doesn't decode ogg in game.
using System;
using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

foreach (var src in Directory.GetFiles(args[0], "*.ogg"))
{
    var dst = Path.Combine(args[1], Path.GetFileNameWithoutExtension(src) + ".wav");
    using var reader = new VorbisWaveReader(src);
    ISampleProvider sp = reader;
    if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
    if (sp.WaveFormat.SampleRate != 44100) sp = new WdlResamplingSampleProvider(sp, 44100);
    WaveFileWriter.CreateWaveFile16(dst, sp);
    Console.WriteLine(dst);
}
