using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CoopSpectator.Infrastructure;

internal static class TooltipPayloadTests
{
    private static int _count;
    private const string Session = "66686cb093aa4ed8aa0377a249ea09e3";

    internal static void Run()
    {
        var source = Fixture();
        var barInfo = Fixture(); barInfo.Kind = CoopCampaignMapTooltipKind.BarInfo;
        barInfo.EntityId = "barhint:" + Guid.NewGuid().ToString("N"); barInfo.Mode = 0;
        Check(CoopCampaignMapTooltipPayloadCodec.TrySerialize(barInfo, out var barBytes) &&
            CoopCampaignMapTooltipPayloadCodec.TryDeserialize(barBytes, out var barCopy) &&
            barCopy.Kind == CoopCampaignMapTooltipKind.BarInfo && barCopy.Rows.Count == barInfo.Rows.Count, "bar info roundtrip");
        barInfo.Mode = 1; Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(barInfo), "bar mode fixed to native generic");
        barInfo.Mode = 0; barInfo.EntityId = "party:a";
        Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(barInfo), "bar requires bar token");
        Check(CoopCampaignMapTooltipPayloadCodec.TrySerialize(source, out var bytes), "serialize");
        Check(CoopCampaignMapTooltipPayloadCodec.TryDeserialize(bytes, out var decoded), "decode");
        Check(CoopCampaignMapTooltipPayloadCodec.TrySerialize(decoded, out var again) && bytes.SequenceEqual(again),
            "byte-exact ordered row, color, layout and rich text roundtrip");
        Check(decoded.Rows[0].Value == "Авалюпс 🌍" && decoded.Rows[1].Definition == "Війська\nПоранені" &&
            decoded.Rows[1].PropertyModifier == 0x3fff && decoded.Rows[1].Red == 0.1234567f, "native fields preserved");
        source.Rows[0].Value = "mutated";
        Check(decoded.Rows[0].Value == "Авалюпс 🌍", "detached rows");

        for (int kind = 1; kind <= 5; kind++)
        for (int mode = 0; mode <= 4; mode++)
        foreach (bool extended in new[] { false, true })
        {
            source = Fixture(); source.Kind = (CoopCampaignMapTooltipKind)kind;
            source.Mode = mode; source.IsExtended = extended;
            Check(CoopCampaignMapTooltipPayloadCodec.TrySerialize(source, out var data) &&
                CoopCampaignMapTooltipPayloadCodec.TryDeserialize(data, out var copy) &&
                copy.Kind == source.Kind && copy.Mode == mode && copy.IsExtended == extended, "all native modes/routes");
        }

        source = Fixture();
        Check(CoopCampaignMapTooltipPayloadCodec.MatchesInterest(source, Session, "settlement:village_ES1_1", 7, false, 0), "current interest");
        Check(!CoopCampaignMapTooltipPayloadCodec.MatchesInterest(source, Guid.NewGuid().ToString("N"), source.EntityId, 7, false, 0), "old session");
        Check(!CoopCampaignMapTooltipPayloadCodec.MatchesInterest(source, Session, "another", 7, false, 0), "old target");
        Check(!CoopCampaignMapTooltipPayloadCodec.MatchesInterest(source, Session, source.EntityId, 8, false, 0), "old request");
        Check(!CoopCampaignMapTooltipPayloadCodec.MatchesInterest(source, Session, source.EntityId, 7, true, 0), "old extended mode");
        Check(!CoopCampaignMapTooltipPayloadCodec.MatchesInterest(source, Session, source.EntityId, 7, false, 3), "duplicate sequence");
        Check(!CoopCampaignMapTooltipPayloadCodec.MatchesInterest(source, Session, source.EntityId, 7, false, 4), "older sequence");
        Check(!CoopCampaignMapTooltipPayloadCodec.MatchesInterest(source, Session, source.EntityId, 7, false, -1), "invalid receiver sequence");

        Reject(x => x.SessionId = null, "null session");
        Reject(x => x.SessionId = new string('f', 37), "long session");
        Reject(x => x.SessionId = Guid.Empty.ToString("N"), "empty session");
        Reject(x => x.SessionId = new string('q', 32), "malformed session");
        Reject(x => x.EntityId = " ", "blank target");
        Reject(x => x.EntityId = "a\nb", "target control");
        Reject(x => x.EntityId = new string('a', 97), "long target");
        Reject(x => x.EntityId = "\ud800", "target invalid UTF16");
        Reject(x => x.RequestGeneration = 0, "zero generation");
        Reject(x => x.Sequence = -1, "negative sequence");
        Reject(x => x.Kind = (CoopCampaignMapTooltipKind)12, "unknown provider kind");
        Reject(x => x.Mode = 5, "unknown mode");
        Reject(x => x.Rows = null, "null rows");
        Reject(x => x.Rows.Clear(), "empty response is not a close message");
        Reject(x => x.Rows.Add(null), "null row");
        Reject(x => x.Rows[0].Definition = null, "null definition");
        Reject(x => x.Rows[0].Value = "\0", "control text");
        Reject(x => x.Rows[0].Value = "\ud800", "invalid UTF16");
        Reject(x => x.Rows[0].Value = new string('x', 4097), "oversized text");
        Reject(x => x.Rows[0].PropertyModifier = 0x4000, "unknown row flag");
        Reject(x => x.Rows[0].PropertyModifier = -1, "negative row flags");
        Reject(x => x.Rows[0].TextHeight = -2, "invalid height");
        Reject(x => x.Rows[0].TextHeight = 4097, "unbounded height");
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.1f, 1.1f })
        {
            Reject(x => x.Rows[0].Red = value, "invalid red");
            Reject(x => x.Rows[0].Green = value, "invalid green");
            Reject(x => x.Rows[0].Blue = value, "invalid blue");
            Reject(x => x.Rows[0].Alpha = value, "invalid alpha");
        }
        source = Fixture(); source.Rows.Clear();
        for (int i = 0; i < 512; i++) source.Rows.Add(new CoopCampaignMapTooltipRow());
        Check(CoopCampaignMapTooltipPayloadCodec.TrySerialize(source, out var maxRows), "maximum rows");
        Check(CoopCampaignMapTooltipPayloadCodec.TryDeserialize(maxRows, out var rowCopy) && rowCopy.Rows.Count == 512, "maximum rows decode");
        source.Rows.Add(new CoopCampaignMapTooltipRow());
        Check(!CoopCampaignMapTooltipPayloadCodec.TrySerialize(source, out _), "row cap");
        source.Rows.RemoveAt(512);
        foreach (var row in source.Rows) row.Value = new string('界', 4096);
        Check(!CoopCampaignMapTooltipPayloadCodec.TrySerialize(source, out _), "total cap before payload allocation");
        source = Fixture(); source.Rows[0].Value = new string('界', 4096);
        Check(CoopCampaignMapTooltipPayloadCodec.TrySerialize(source, out _), "multibyte character limit");
        source.SessionId = Guid.Parse(Session).ToString("D");
        Check(CoopCampaignMapTooltipPayloadCodec.TrySerialize(source, out _), "D format session");

        for (int length = 0; length < bytes.Length; length++)
            Check(!CoopCampaignMapTooltipPayloadCodec.TryDeserialize(bytes.Take(length).ToArray(), out var partial) &&
                partial == null, "truncated " + length);
        Check(!CoopCampaignMapTooltipPayloadCodec.TryDeserialize(null, out _), "null bytes");
        Check(!CoopCampaignMapTooltipPayloadCodec.TryDeserialize(new byte[1048577], out _), "incoming payload cap");
        Check(!CoopCampaignMapTooltipPayloadCodec.TryDeserialize(bytes.Concat(new byte[] { 0 }).ToArray(), out _), "trailing garbage");
        Corrupt(bytes, 0, int.MaxValue, "magic");
        Corrupt(bytes, 4, 1, "old version");
        Corrupt(bytes, 8, int.MaxValue, "string length prefix");
        Corrupt(bytes, 8, -1, "negative string length prefix");
        int flagsOffset = 16 + Encoding.UTF8.GetByteCount(Session) + Encoding.UTF8.GetByteCount(Fixture().EntityId) + 16;
        var malformed = (byte[])bytes.Clone(); malformed[flagsOffset] = 2;
        Check(!CoopCampaignMapTooltipPayloadCodec.TryDeserialize(malformed, out _), "noncanonical bool");
        Corrupt(bytes, flagsOffset + 9, int.MaxValue, "row count prefix");
        malformed = (byte[])bytes.Clone(); malformed[12] = 0xff;
        Check(!CoopCampaignMapTooltipPayloadCodec.TryDeserialize(malformed, out _), "malformed UTF8");
        var random = new Random(71231);
        for (int i = 0; i < 500; i++)
        {
            var candidate = (byte[])bytes.Clone();
            candidate[random.Next(candidate.Length)] = (byte)random.Next(256);
            bool accepted = CoopCampaignMapTooltipPayloadCodec.TryDeserialize(candidate, out var result);
            Check(accepted ? CoopCampaignMapTooltipPayloadCodec.IsValid(result) : result == null, "bounded corrupt-input decode");
        }
        Console.WriteLine("Tooltip payload assertions passed: " + _count);
    }

    private static CoopCampaignMapTooltipPayload Fixture()
    {
        return new CoopCampaignMapTooltipPayload {
            SessionId = Session, EntityId = "settlement:village_ES1_1", RequestGeneration = 7, Sequence = 3,
            Kind = CoopCampaignMapTooltipKind.Settlement, Mode = 1,
            Rows = new List<CoopCampaignMapTooltipRow> {
                new CoopCampaignMapTooltipRow { Value = "Авалюпс 🌍", PropertyModifier = 0x1000 },
                new CoopCampaignMapTooltipRow { Definition = "Війська\nПоранені", Value = "<img src=\"General\\Icons\\Coin@2x\"> 30\t?",
                    TextHeight = -1, PropertyModifier = 0x3fff, Red = 0.1234567f, Green = 1, Blue = 0.5f, Alpha = 1 },
                new CoopCampaignMapTooltipRow { PropertyModifier = 0x400 }
            }
        };
    }

    private static void Reject(Action<CoopCampaignMapTooltipPayload> change, string name)
    {
        var source = Fixture(); change(source);
        Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(source) &&
            !CoopCampaignMapTooltipPayloadCodec.TrySerialize(source, out var bytes) && bytes == null, name);
    }

    private static void Corrupt(byte[] source, int offset, int value, string name)
    {
        var copy = (byte[])source.Clone(); Array.Copy(BitConverter.GetBytes(value), 0, copy, offset, 4);
        Check(!CoopCampaignMapTooltipPayloadCodec.TryDeserialize(copy, out var payload) && payload == null, name);
    }

    private static void Check(bool condition, string name)
    {
        _count++;
        if (!condition) throw new InvalidOperationException("Tooltip payload: " + name);
    }
}
