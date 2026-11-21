using System;
using System.IO;
using UnityEngine;

namespace Reminiscence;

internal static class WavPcm
{
    public static AudioClip ToClip(string path, string name)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (ReadId(reader) != "RIFF")
            throw new InvalidDataException($"{path} is not a RIFF wav");
        reader.ReadInt32();
        if (ReadId(reader) != "WAVE")
            throw new InvalidDataException($"{path} is not a WAVE file");

        short channels = 0;
        short bits = 0;
        int rate = 0;
        byte[]? data = null;
        while (stream.Position + 8 <= stream.Length)
        {
            string id = ReadId(reader);
            int size = reader.ReadInt32();
            if (id == "fmt ")
            {
                short format = reader.ReadInt16();
                channels = reader.ReadInt16();
                rate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
                int consumed = 16;
                if (size > consumed)
                    reader.ReadBytes(size - consumed);
                if (format != 1 || bits != 16)
                    throw new InvalidDataException($"{path} is not 16-bit PCM");
            }
            else if (id == "data")
            {
                data = reader.ReadBytes(size);
            }
            else
            {
                reader.ReadBytes(size);
            }

            if ((size & 1) == 1 && stream.Position < stream.Length)
                reader.ReadByte();
        }

        if (data == null || channels < 1 || rate < 1)
            throw new InvalidDataException($"{path} has no PCM data");

        int frameCount = data.Length / (channels * 2);
        var samples = new float[frameCount * channels];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(data, i * 2) / 32768f;

        AudioClip clip = AudioClip.Create(name, frameCount, channels, rate, false);
        if (!clip.SetData(samples, 0))
            throw new InvalidDataException($"Unity rejected the PCM for {name}");
        return clip;
    }

    private static string ReadId(BinaryReader reader)
    {
        return new string(reader.ReadChars(4));
    }
}
