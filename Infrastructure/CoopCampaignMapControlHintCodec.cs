using System;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    // Inner presentation format only. The enclosing transport must authorize and
    // correlate owner/session/target/generation before presenting decoded content.
    public static class CoopCampaignMapControlHintCodec
    {
        private const int Magic = 0x434D4831; // CMH1
        public const int MaximumBytes = 1048576;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool TryEncode(CoopCampaignMapControlHint hint, out byte[] bytes)
        {
            bytes = null;
            if (hint == null || !hint.IsValid()) return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Utf8, true))
                {
                    writer.Write(Magic);
                    WriteText(writer, hint.Text);
                    writer.Write(hint.Rows.Count);
                    foreach (var row in hint.Rows)
                    {
                        WriteText(writer, row.Definition); WriteText(writer, row.Value);
                        writer.Write(row.TextHeight); writer.Write(row.PropertyModifier);
                        writer.Write(row.Red); writer.Write(row.Green);
                        writer.Write(row.Blue); writer.Write(row.Alpha);
                    }
                    writer.Flush();
                    if (stream.Length > MaximumBytes) return false;
                    bytes = stream.ToArray(); return true;
                }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
        }

        public static bool TryDecode(byte[] bytes, out CoopCampaignMapControlHint hint)
        {
            hint = null;
            if (bytes == null || bytes.Length < 12 || bytes.Length > MaximumBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    var decoded = new CoopCampaignMapControlHint { Text = ReadText(reader) };
                    int count = reader.ReadInt32();
                    if (count < 0 || count > CoopCampaignMapControlHint.MaxRows ||
                        count > (stream.Length - stream.Position) / 32) return false;
                    for (int i = 0; i < count; i++)
                        decoded.Rows.Add(new CoopCampaignMapTooltipRow
                        {
                            Definition = ReadText(reader), Value = ReadText(reader),
                            TextHeight = reader.ReadInt32(), PropertyModifier = reader.ReadInt32(),
                            Red = reader.ReadSingle(), Green = reader.ReadSingle(),
                            Blue = reader.ReadSingle(), Alpha = reader.ReadSingle()
                        });
                    if (stream.Position != stream.Length || !decoded.IsValid()) return false;
                    hint = decoded; return true;
                }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
        }

        private static void WriteText(BinaryWriter writer, string text)
        {
            byte[] bytes = Utf8.GetBytes(text);
            if (writer.BaseStream.Position + 4L + bytes.Length > MaximumBytes)
                throw new InvalidDataException();
            writer.Write(bytes.Length); writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > CoopCampaignMapControlHint.MaxTextCharacters * 4 ||
                length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException();
            string text = Utf8.GetString(reader.ReadBytes(length));
            if (text.Length > CoopCampaignMapControlHint.MaxTextCharacters) throw new InvalidDataException();
            return text;
        }
    }
}
