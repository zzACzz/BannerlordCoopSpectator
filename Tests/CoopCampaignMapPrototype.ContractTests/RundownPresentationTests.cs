using System;
using CoopSpectator.Infrastructure;

internal static class RundownPresentationTests
{
    internal static void Run()
    {
        void Check(bool value) { if (!value) throw new Exception("Rundown presentation contract"); }
        var data = new CoopCampaignMapRundownPresentation
        {
            IsActive = true, IsExtended = true, ValueCategorizationAsInt = 1,
            TitleText = "Зміна", ExtendText = "Alt", CurrentExpectedChange = 17.125f,
            ExpectedChangeText = "Разом: <span style=\"PositiveChange\">17,13</span>"
        };
        data.Lines.Add(new CoopCampaignMapRundownLine { Name = "Витрати", Value = -3.333f, ValueAsString = "-3,33" });
        Check(data.IsValid());
        var response = new CoopCampaignMapTooltipPayload
        {
            SessionId = Guid.NewGuid().ToString("N"), EntityId = "barhint:" + Guid.NewGuid().ToString("N"),
            RequestGeneration = 2, Sequence = 3, IsExtended = true,
            Kind = CoopCampaignMapTooltipKind.BarRundown, Rundown = data.Clone()
        };
        Check(CoopCampaignMapTooltipPayloadCodec.TrySerialize(response, out var envelope));
        Check(CoopCampaignMapTooltipPayloadCodec.TryDeserialize(envelope, out var responseCopy) &&
            responseCopy.Rundown.ExpectedChangeText == data.ExpectedChangeText && responseCopy.Rows.Count == 0);
        Check(CoopCampaignMapTooltipPayloadCodec.MatchesInterest(responseCopy, response.SessionId, response.EntityId, 2, true, 2));
        Check(!CoopCampaignMapTooltipPayloadCodec.MatchesInterest(responseCopy, response.SessionId, response.EntityId, 2, false, 2));
        Check(!CoopCampaignMapTooltipPayloadCodec.MatchesInterest(responseCopy, response.SessionId, response.EntityId, 3, true, 2));
        Check(!CoopCampaignMapTooltipPayloadCodec.MatchesInterest(responseCopy, response.SessionId, response.EntityId, 2, true, 3));
        response.IsExtended = false; Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(response)); response.IsExtended = true;
        response.Rows.Add(new CoopCampaignMapTooltipRow()); Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(response)); response.Rows.Clear();
        response.Rundown.IsActive = false; Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(response)); response.Rundown.IsActive = true;
        response.Kind = CoopCampaignMapTooltipKind.MobileParty; Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(response));
        for (int size = 0; size < envelope.Length; size++)
        {
            var partial = new byte[size]; Array.Copy(envelope, partial, size);
            Check(!CoopCampaignMapTooltipPayloadCodec.TryDeserialize(partial, out var failed) && failed == null);
        }
        Check(CoopCampaignMapRundownCodec.TryEncode(data, out var bytes));
        Check(CoopCampaignMapRundownCodec.TryDecode(bytes, out var restored) && restored.IsExtended &&
            restored.CurrentExpectedChange == data.CurrentExpectedChange && restored.ExpectedChangeText == data.ExpectedChangeText &&
            restored.Lines[0].ValueAsString == data.Lines[0].ValueAsString && restored.Lines[0].Value == data.Lines[0].Value);
        for (int length = 0; length < bytes.Length; length++)
        {
            var shortMessage = new byte[length]; Array.Copy(bytes, shortMessage, length);
            Check(!CoopCampaignMapRundownCodec.TryDecode(shortMessage, out restored) && restored == null);
        }
        var corrupt = (byte[])bytes.Clone(); corrupt[4] = 2;
        Check(!CoopCampaignMapRundownCodec.TryDecode(corrupt, out _));
        corrupt = (byte[])bytes.Clone(); corrupt[0] ^= 1;
        Check(!CoopCampaignMapRundownCodec.TryDecode(corrupt, out _));
        corrupt = new byte[bytes.Length + 1]; Array.Copy(bytes, corrupt, bytes.Length);
        Check(!CoopCampaignMapRundownCodec.TryDecode(corrupt, out _));
        corrupt = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN), 0, corrupt, 10, 4);
        Check(!CoopCampaignMapRundownCodec.TryDecode(corrupt, out _));
        var invalidUtf = data.Clone(); invalidUtf.TitleText = "\ud800";
        Check(!CoopCampaignMapRundownCodec.TryEncode(invalidUtf, out _));
        Check(CoopCampaignMapRundownCodec.TryEncode(new CoopCampaignMapRundownPresentation(), out var empty) &&
            CoopCampaignMapRundownCodec.TryDecode(empty, out restored) && !restored.IsActive && restored.Lines.Count == 0);
        var copy = data.Clone();
        Check(copy.IsExtended && copy.CurrentExpectedChange == 17.125f && copy.Lines[0].ValueAsString == "-3,33");
        copy.Lines[0].Name = "changed";
        Check(data.Lines[0].Name == "Витрати");
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            copy = data.Clone(); copy.CurrentExpectedChange = bad; Check(!copy.IsValid());
            copy = data.Clone(); copy.Lines[0].Value = bad; Check(!copy.IsValid());
        }
        copy = data.Clone(); copy.ValueCategorizationAsInt = 3; Check(!copy.IsValid());
        copy = data.Clone(); copy.TitleText = "a\0b"; Check(!copy.IsValid());
        copy = data.Clone(); copy.Lines.Add(null); Check(!copy.IsValid());
        copy = data.Clone(); copy.Lines[0].ValueAsString = new string('x', 4097); Check(!copy.IsValid());
        copy = data.Clone();
        while (copy.Lines.Count < CoopCampaignMapRundownPresentation.MaxLines) copy.Lines.Add(data.Lines[0].Clone());
        Check(copy.IsValid()); copy.Lines.Add(data.Lines[0].Clone()); Check(!copy.IsValid());
        Console.WriteLine("Rundown presentation: native text/total preservation, deep copy and bounds passed.");
    }
}
