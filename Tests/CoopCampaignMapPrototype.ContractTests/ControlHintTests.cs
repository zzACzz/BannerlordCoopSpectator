using System;
using CoopSpectator.Infrastructure;

internal static class ControlHintTests
{
    internal static void Run()
    {
        var hint = new CoopCampaignMapControlHint();
        Check(!hint.IsValid(), "Empty hint rejected");
        hint.Text = "Pause (1)";
        Check(hint.IsValid(), "Plain hint admitted");
        hint.Rows.Add(new CoopCampaignMapTooltipRow { Definition = "Morning" });
        Check(!hint.IsValid(), "Mixed shapes rejected");
        hint.Text = string.Empty;
        Check(hint.IsValid(), "Structured hint admitted");
        var copy = hint.Clone();
        copy.Rows[0].Definition = "Night";
        Check(hint.Rows[0].Definition == "Morning", "Rows deeply detached");
        copy.Rows[0].Red = float.NaN;
        Check(!copy.IsValid(), "Nonfinite color rejected");
        copy = hint.Clone(); copy.Rows[0].Alpha = 2;
        Check(!copy.IsValid(), "Out-of-range color rejected");
        copy = hint.Clone(); copy.Rows[0].TextHeight = -2;
        Check(!copy.IsValid(), "Invalid height rejected");
        copy = hint.Clone(); copy.Rows[0].PropertyModifier = -1;
        Check(!copy.IsValid(), "Invalid flags rejected");
        for (int i = 1; i < CoopCampaignMapControlHint.MaxRows; i++) hint.Rows.Add(new CoopCampaignMapTooltipRow());
        Check(hint.IsValid(), "Row limit admitted");
        hint.Rows.Add(new CoopCampaignMapTooltipRow());
        Check(!hint.IsValid(), "Excess rows rejected");
        foreach (var text in new[] { "\0", "\ud800", "\udc00", new string('x', 4097) })
            Check(!new CoopCampaignMapControlHint { Text = text }.IsValid(), "Invalid text rejected");
        Check(new CoopCampaignMapControlHint { Text = "Ранок\n\ud83c\udf05" }.IsValid(), "Unicode preserved");
        RoundTrip(new CoopCampaignMapControlHint { Text = "Ранок\n\ud83c\udf05" });
        var structured = new CoopCampaignMapControlHint();
        structured.Rows.Add(new CoopCampaignMapTooltipRow
        { Definition = "Morning", Value = "Reset camera", TextHeight = -1, PropertyModifier = 3,
            Red = .1f, Green = .2f, Blue = .3f, Alpha = .4f });
        RoundTrip(structured);
        foreach (var presentation in new[] { structured, new CoopCampaignMapControlHint { Text = "Pause" } })
        {
            var payload = new CoopCampaignMapTooltipPayload
            {
                SessionId = Guid.NewGuid().ToString("N"), EntityId = "barhint:" + Guid.NewGuid().ToString("N"),
                RequestGeneration = 1, Sequence = 1, Kind = CoopCampaignMapTooltipKind.BarControl,
                ControlHint = presentation
            };
            Check(CoopCampaignMapTooltipPayloadCodec.TrySerialize(payload, out var frame), "Control envelope encode");
            Check(CoopCampaignMapTooltipPayloadCodec.TryDeserialize(frame, out var received) &&
                received.ControlHint.Text == presentation.Text && received.ControlHint.Rows.Count == presentation.Rows.Count,
                "Control envelope preserves shape");
            Check(CoopCampaignMapTooltipPayloadCodec.MatchesInterest(received, payload.SessionId,
                payload.EntityId, 1, false, 0), "Control correlation retained");
            Check(!CoopCampaignMapTooltipPayloadCodec.MatchesInterest(received, payload.SessionId,
                payload.EntityId, 2, false, 0), "Stale control generation rejected");
            for (int i = 0; i < frame.Length; i++)
            {
                var prefix = new byte[i]; Array.Copy(frame, prefix, i);
                Check(!CoopCampaignMapTooltipPayloadCodec.TryDeserialize(prefix, out _), "Control envelope truncation");
            }
            payload.Rows.Add(new CoopCampaignMapTooltipRow());
            Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(payload), "Control mixed outer rows rejected");
            payload.Rows.Clear(); payload.EntityId = "party:test";
            Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(payload), "Control non-bar target rejected");
        }
        Check(!CoopCampaignMapControlHintCodec.TryEncode(hint, out _), "Invalid source cannot encode");
        Check(!CoopCampaignMapControlHintCodec.TryDecode(new byte[1048577], out _), "Oversized input rejected");
    }
    private static void RoundTrip(CoopCampaignMapControlHint value)
    {
        Check(CoopCampaignMapControlHintCodec.TryEncode(value, out var bytes), "Encode");
        Check(CoopCampaignMapControlHintCodec.TryDecode(bytes, out var decoded), "Decode");
        Check(decoded.Text == value.Text && decoded.Rows.Count == value.Rows.Count, "Shape preserved");
        for (int i = 0; i < value.Rows.Count; i++)
        {
            var a = value.Rows[i]; var b = decoded.Rows[i];
            Check(a.Definition == b.Definition && a.Value == b.Value && a.TextHeight == b.TextHeight &&
                a.PropertyModifier == b.PropertyModifier && a.Red == b.Red && a.Green == b.Green &&
                a.Blue == b.Blue && a.Alpha == b.Alpha, "All row fields preserved");
        }
        for (int i = 0; i < bytes.Length; i++)
        {
            var truncated = new byte[i]; Array.Copy(bytes, truncated, i);
            Check(!CoopCampaignMapControlHintCodec.TryDecode(truncated, out var rejected) && rejected == null,
                "Truncation fails closed");
        }
        var trailing = new byte[bytes.Length + 1]; Array.Copy(bytes, trailing, bytes.Length);
        Check(!CoopCampaignMapControlHintCodec.TryDecode(trailing, out _), "Trailing data rejected");
        bytes[0] ^= 1;
        Check(!CoopCampaignMapControlHintCodec.TryDecode(bytes, out _), "Wrong format rejected");
    }
    private static void Check(bool pass, string message)
    { if (!pass) throw new Exception("Control hint: " + message); }
}
