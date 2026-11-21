using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Reminiscence;

internal static class MusicCache
{
    public const int ManifestVersion = 3;

    public static string DirectoryForPlugin()
    {
        string pluginDir = Path.GetDirectoryName(typeof(MusicCache).Assembly.Location);
        return Path.Combine(pluginDir, "reminiscence-music");
    }

    public static MusicManifest? Load(string directory)
    {
        string path = Path.Combine(directory, "manifest.json");
        if (!File.Exists(path))
            return null;

        MusicManifest manifest = ManifestReader.Read(File.ReadAllText(path));
        if (manifest.version != ManifestVersion || manifest.pieces == null)
            return null;
        return manifest;
    }
}

[Serializable]
internal sealed class MusicManifest
{
    public int version = 0;
    public bool tld = false;
    public MusicPiece[] pieces = Array.Empty<MusicPiece>();
}

[Serializable]
internal sealed class MusicPiece
{
    public string id = "";
    public string musicId = "";
    public string trackId = "";
    public string role = "";
    public string name = "";
    public float highTransitionSeconds = 0f;
    public bool layersShareLength = false;
    public MusicLayer[] layers = Array.Empty<MusicLayer>();
}

[Serializable]
internal sealed class MusicLayer
{
    public string id = "";
    public string file = "";
    public int sampleRate = 0;
    public int channels = 0;
    public int samples = 0;
}

internal static class ManifestReader
{
    public static MusicManifest Read(string json)
    {
        var reader = new JsonCursor(json);
        var manifest = new MusicManifest();
        var pieces = new List<MusicPiece>();
        reader.Object((key) =>
        {
            if (key == "version")
                manifest.version = reader.Int();
            else if (key == "tld")
                manifest.tld = reader.Bool();
            else if (key == "pieces")
                reader.Array(() => pieces.Add(ReadPiece(reader)));
            else
                reader.Skip();
        });
        manifest.pieces = pieces.ToArray();
        return manifest;
    }

    private static MusicPiece ReadPiece(JsonCursor reader)
    {
        var piece = new MusicPiece();
        var layers = new List<MusicLayer>();
        reader.Object((key) =>
        {
            if (key == "id")
                piece.id = reader.String();
            else if (key == "musicId")
                piece.musicId = reader.String();
            else if (key == "trackId")
                piece.trackId = reader.String();
            else if (key == "role")
                piece.role = reader.String();
            else if (key == "name")
                piece.name = reader.String();
            else if (key == "highTransitionSeconds")
                piece.highTransitionSeconds = reader.Float();
            else if (key == "layersShareLength")
                piece.layersShareLength = reader.Bool();
            else if (key == "layers")
                reader.Array(() => layers.Add(ReadLayer(reader)));
            else
                reader.Skip();
        });
        piece.layers = layers.ToArray();
        return piece;
    }

    private static MusicLayer ReadLayer(JsonCursor reader)
    {
        var layer = new MusicLayer();
        reader.Object((key) =>
        {
            if (key == "id")
                layer.id = reader.String();
            else if (key == "file")
                layer.file = reader.String();
            else if (key == "sampleRate")
                layer.sampleRate = reader.Int();
            else if (key == "channels")
                layer.channels = reader.Int();
            else if (key == "samples")
                layer.samples = reader.Int();
            else
                reader.Skip();
        });
        return layer;
    }

    private sealed class JsonCursor
    {
        private readonly string _text;
        private int _index;

        public JsonCursor(string text)
        {
            _text = text;
        }

        public void Object(Action<string> field)
        {
            Expect('{');
            if (Peek() == '}')
            {
                Take();
                return;
            }
            while (true)
            {
                string key = String();
                Expect(':');
                field(key);
                if (Peek() != ',')
                    break;
                Take();
            }
            Expect('}');
        }

        public void Array(Action element)
        {
            Expect('[');
            if (Peek() == ']')
            {
                Take();
                return;
            }
            while (true)
            {
                element();
                if (Peek() != ',')
                    break;
                Take();
            }
            Expect(']');
        }

        public void Skip()
        {
            char kind = Peek();
            if (kind == '"')
            {
                String();
                return;
            }
            if (kind == '{')
            {
                Object((_) => Skip());
                return;
            }
            if (kind == '[')
            {
                Array(Skip);
                return;
            }
            if (kind == 't' || kind == 'f' || kind == 'n')
            {
                while (_index < _text.Length && char.IsLetter(_text[_index]))
                    _index++;
                return;
            }
            while (_index < _text.Length && "-+.eE0123456789".IndexOf(_text[_index]) >= 0)
                _index++;
        }

        public string String()
        {
            Expect('"');
            var builder = new StringBuilder();
            while (_index < _text.Length)
            {
                char current = _text[_index++];
                if (current == '"')
                    return builder.ToString();
                if (current != '\\')
                {
                    builder.Append(current);
                    continue;
                }
                char escaped = _text[_index++];
                if (escaped == 'u')
                {
                    int code = int.Parse(_text.Substring(_index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    _index += 4;
                    builder.Append((char)code);
                    continue;
                }
                builder.Append(escaped switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => throw new InvalidDataException("Music manifest has an invalid string escape."),
                });
            }
            throw new InvalidDataException("Music manifest has an unclosed string.");
        }

        public int Int()
        {
            return int.Parse(Number(), CultureInfo.InvariantCulture);
        }

        public float Float()
        {
            return float.Parse(Number(), CultureInfo.InvariantCulture);
        }

        public bool Bool()
        {
            if (Peek() == 't')
            {
                Literal("true");
                return true;
            }
            Literal("false");
            return false;
        }

        private string Number()
        {
            SkipSpace();
            int start = _index;
            while (_index < _text.Length && "-+.eE0123456789".IndexOf(_text[_index]) >= 0)
                _index++;
            return _text.Substring(start, _index - start);
        }

        private void Literal(string word)
        {
            SkipSpace();
            for (int i = 0; i < word.Length; i++)
            {
                if (_text[_index + i] != word[i])
                    throw new InvalidDataException("Music manifest has an invalid value.");
            }
            _index += word.Length;
        }

        private char Peek()
        {
            SkipSpace();
            if (_index >= _text.Length)
                throw new InvalidDataException("Music manifest ended early.");
            return _text[_index];
        }

        private char Take()
        {
            char current = Peek();
            _index++;
            return current;
        }

        private void Expect(char expected)
        {
            char current = Take();
            if (current != expected)
                throw new InvalidDataException($"Music manifest expected '{expected}'.");
        }

        private void SkipSpace()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                _index++;
        }
    }
}
