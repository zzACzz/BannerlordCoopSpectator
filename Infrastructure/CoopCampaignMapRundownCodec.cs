using System;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    // Inner presentation only. Must be wrapped in an admitted, correlated hint response.
    public static class CoopCampaignMapRundownCodec
    {
        public const int MaximumBytes = 1048576;
        private const int Magic = 0x434D5231; // CMR1
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool TryEncode(CoopCampaignMapRundownPresentation data, out byte[] bytes)
        {
            bytes = null;
            if (data == null || !data.IsValid()) return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Utf8, true))
                {
                    writer.Write(Magic); writer.Write(data.IsActive); writer.Write(data.IsExtended);
                    writer.Write(data.ValueCategorizationAsInt); writer.Write(data.CurrentExpectedChange);
                    WriteText(writer, data.TitleText); WriteText(writer, data.ExpectedChangeText); WriteText(writer, data.ExtendText);
                    writer.Write(data.Lines.Count);
                    foreach (var line in data.Lines)
                    { WriteText(writer, line.Name); WriteText(writer, line.ValueAsString); writer.Write(line.Value); }
                    writer.Flush();
                    if (stream.Length > MaximumBytes) return false;
                    bytes = stream.ToArray(); return true;
                }
            }
            catch (ArgumentException) { return false; }
            catch (IOException) { return false; }
        }

        public static bool TryDecode(byte[] bytes, out CoopCampaignMapRundownPresentation data)
        {
            data = null;
            if (bytes == null || bytes.Length < 30 || bytes.Length > MaximumBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    var candidate = new CoopCampaignMapRundownPresentation
                    {
                        IsActive = ReadBoolean(reader), IsExtended = ReadBoolean(reader),
                        ValueCategorizationAsInt = reader.ReadInt32(), CurrentExpectedChange = reader.ReadSingle(),
                        TitleText = ReadText(reader), ExpectedChangeText = ReadText(reader), ExtendText = ReadText(reader)
                    };
                    int count = reader.ReadInt32();
                    if (count < 0 || count > CoopCampaignMapRundownPresentation.MaxLines) return false;
                    for (int i = 0; i < count; i++)
                        candidate.Lines.Add(new CoopCampaignMapRundownLine
                        { Name = ReadText(reader), ValueAsString = ReadText(reader), Value = reader.ReadSingle() });
                    if (stream.Position != stream.Length || !candidate.IsValid()) return false;
                    data = candidate; return true;
                }
            }
            catch (ArgumentException) { return false; }
            catch (IOException) { return false; }
        }

        private static void WriteText(BinaryWriter writer, string value)
        {
            byte[] bytes = Utf8.GetBytes(value);
            if (writer.BaseStream.Position + 4 + bytes.Length > MaximumBytes) throw new IOException("Rundown size exceeded");
            writer.Write(bytes.Length); writer.Write(bytes);
        }
        private static string ReadText(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > CoopCampaignMapBarPresentation.MaxTextLength * 4 ||
                count > reader.BaseStream.Length - reader.BaseStream.Position) throw new IOException("Invalid rundown text size");
            string value = Utf8.GetString(reader.ReadBytes(count));
            if (!CoopCampaignMapBarPresentation.Text(value)) throw new IOException("Invalid rundown text");
            return value;
        }
        private static bool ReadBoolean(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value > 1) throw new IOException("Invalid rundown boolean");
            return value == 1;
        }
    }
}
