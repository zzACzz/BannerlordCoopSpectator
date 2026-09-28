using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class ConversationPayloadTests
{
    private static int _count;
    internal static void Run()
    {
        var source = new CoopCampaignMapConversationState
        {
            Revision = 5, IsActive = true, SpeakerName = "Трістанія", DialogText = "Вітаю! 🌍",
            GoldText = "100", ContinueText = "Далі", Tableau = ConversationTableauTests.Fixture(),
            Options = new List<CoopCampaignMapConversationOptionState>
            {
                new CoopCampaignMapConversationOptionState { Text = "Відповідь", IsEnabled = true },
                new CoopCampaignMapConversationOptionState { Text = "Інша", IsSpecial = true }
            }
        };
        Check(CoopCampaignMapConversationPayloadCodec.TrySerialize(source, out var bytes), "serialize");
        Check(CoopCampaignMapConversationPayloadCodec.TryDeserialize(bytes, out var decoded) &&
            decoded.Revision == 5 && CoopCampaignMapPrototypeContract.AreConversationContentsEquivalent(source, decoded), "round trip");
        Check(CoopCampaignMapConversationTableauCodec.Equivalent(source.Tableau, decoded.Tableau), "visual round trip");
        var clone = source.Clone();
        clone.Tableau.Actors[0].Visual.EquipmentItemIds[0] = "changed";
        Check(source.Tableau.Actors[0].Visual.EquipmentItemIds[0] != "changed", "deep state clone");
        Check(!CoopCampaignMapPrototypeContract.AreConversationContentsEquivalent(source, clone), "visual-only revision");
        clone = source.Clone(); clone.Tableau = null;
        Check(CoopCampaignMapConversationPayloadCodec.TrySerialize(clone, out var textBytes) &&
            CoopCampaignMapConversationPayloadCodec.TryDeserialize(textBytes, out decoded) && decoded.Tableau == null,
            "text-only fallback");
        var close = CoopCampaignMapPrototypeContract.CreateInactiveConversation(6);
        Check(CoopCampaignMapConversationPayloadCodec.TrySerialize(close, out var closeBytes), "close serialize");
        close.Tableau = source.Tableau;
        Check(!CoopCampaignMapConversationPayloadCodec.TrySerialize(close, out _), "inactive visual rejected");

        for (int i = 0; i < bytes.Length; i++)
        {
            var truncated = new byte[i]; Array.Copy(bytes, truncated, i);
            Check(!CoopCampaignMapConversationPayloadCodec.TryDeserialize(truncated, out _), "truncated " + i);
        }
        var suffix = bytes.Concat(new byte[] { 0 }).ToArray();
        Check(!CoopCampaignMapConversationPayloadCodec.TryDeserialize(suffix, out _), "suffix rejected");
        Check(!CoopCampaignMapConversationPayloadCodec.TryDeserialize(new byte[1048577], out _), "allocation cap");
        var malformed = (byte[])bytes.Clone(); malformed[8] = 2;
        Check(!CoopCampaignMapConversationPayloadCodec.TryDeserialize(malformed, out _), "boolean rejected");
        malformed = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(int.MaxValue), 0, malformed, 9, 4);
        Check(!CoopCampaignMapConversationPayloadCodec.TryDeserialize(malformed, out _), "string prefix rejected");

        Check(CoopCampaignMapCatalogChunkCodec.TryEncode(bytes, out var payload, out _), "chunk encode");
        var receiver = new CoopCampaignMapConversationPayloadAssembler();
        for (int i = payload.ChunkCount - 1; i >= 0; i--)
        {
            Check(Accept(receiver, 5, payload, i, out decoded), "reverse chunks");
            if (i != 0) Check(decoded == null, "no partial presentation");
        }
        Check(CoopCampaignMapPrototypeContract.AreConversationContentsEquivalent(source, decoded), "atomic dialogue plus tableau");
        Check(!Accept(receiver, 5, payload, 0, out _), "stale complete");
        receiver.Reset();
        for (int i = 0; i < payload.ChunkCount; i++)
        {
            bool accepted = Accept(receiver, 7, payload, i, out decoded);
            Check(i == payload.ChunkCount - 1 ? !accepted && decoded == null : accepted, "embedded revision match");
        }
        Check(CoopCampaignMapCatalogChunkCodec.TryEncode(closeBytes, out var closePayload, out _), "close encode");
        receiver.Reset();
        Check(Accept(receiver, 5, payload, 0, out _), "partial open");
        for (int i = 0; i < closePayload.ChunkCount; i++) Check(Accept(receiver, 6, closePayload, i, out decoded), "close chunks");
        Check(decoded != null && !decoded.IsActive && decoded.Tableau == null, "close supersedes partial");
        Check(!Accept(receiver, 5, payload, 0, out _), "late scene cannot reopen");

        var host = new CoopCampaignMapPrototypeHostSnapshot
        {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = "0123456789abcdef0123456789abcdef", Revision = 1, IsActive = true,
            UpdatedUtc = DateTime.UtcNow, Conversation = source
        };
        var lines = CoopCampaignMapPrototypeBridgeCodec.Serialize(host);
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(lines, out var fromBridge, out _) &&
            CoopCampaignMapPrototypeContract.AreConversationContentsEquivalent(source, fromBridge.Conversation), "bridge visual round trip");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Where(s => !s.StartsWith("ConversationTableau=")).ToArray(), out _, out _), "required bridge schema field");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Select(s => s.StartsWith("ConversationTableau=") ? "ConversationTableau=!" : s).ToArray(), out _, out _), "corrupt bridge visual");

        var largest = source.Clone();
        largest.DialogText = new string('界', CoopCampaignMapPrototypeContract.MaxConversationDialogCharacters);
        largest.Options.Clear();
        for (int i = 0; i < CoopCampaignMapPrototypeContract.MaxConversationOptions; i++)
            largest.Options.Add(new CoopCampaignMapConversationOptionState { Text = new string('界', CoopCampaignMapPrototypeContract.MaxConversationOptionCharacters) });
        Check(CoopCampaignMapConversationPayloadCodec.TrySerialize(largest, out var largestBytes) &&
            CoopCampaignMapConversationPayloadCodec.TryDeserialize(largestBytes, out decoded), "maximum localized text");
        Check(largestBytes.Length > 400000 && largestBytes.Length <= CoopCampaignMapConversationPayloadCodec.MaxPayloadBytes,
            "maximum text is chunked, not one native packet");
        ValidateWiring();
        Console.WriteLine("Conversation payload assertions passed: " + _count);
    }

    private static void ValidateWiring()
    {
        string root = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        var controller = File.ReadAllText(Path.Combine(root, "Mission", "CoopCampaignMapPrototypeNetworkController.cs"));
        var messages = File.ReadAllText(Path.Combine(root, "Network", "Messages", "CoopCampaignMapConversationNetworkMessages.cs"));
        var capture = File.ReadAllText(Path.Combine(root, "Campaign", "CoopCampaignMapConversationCapture.cs"));
        Check(capture.Contains("CoopCampaignMapConversationTableauCapture.TryCapture") && capture.Contains("captured.Tableau = tableau"), "capture wired");
        Check(controller.Contains("MaxConversationChunksPerPeerPerTick = 2") && controller.Contains("send.NextChunk++") &&
            controller.Contains("_conversationSends.Remove(networkPeer.Index)") && controller.Contains("ReferenceEquals(current, send.Peer)"), "pacing and exact peer lifetime");
        Check(controller.Contains("CoopCampaignMapConversationPayloadAssembler") && controller.Contains("TickServerConversationTransports();"), "atomic assembler wired");
        Check(messages.Contains("ReadByteArrayFromPacket") && !messages.Contains("WriteStringToPacket(DialogText") &&
            !messages.Contains("FromClient") && !controller.Contains("CoopCampaignMapConversationOptionMessage"), "bounded server-only packets");
    }
    private static bool Accept(CoopCampaignMapConversationPayloadAssembler receiver, int revision,
        CoopCampaignMapCatalogChunkedPayload payload, int index, out CoopCampaignMapConversationState state) =>
        receiver.TryAccept(revision, payload.LogicalByteCount, payload.WireByteCount, payload.ChunkCount,
            payload.CompressionKind, payload.PayloadHash, index, payload.Chunks[index], out state);
    private static void Check(bool condition, string label)
    {
        _count++;
        if (!condition) throw new InvalidOperationException("Conversation payload: " + label);
    }
}
