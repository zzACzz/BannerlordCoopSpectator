using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Text;
using CoopSpectator.Infrastructure;

internal static class ConversationTableauTests
{
    private static int _assertions;

    internal static void Run()
    {
        var state = Fixture();
        Check(CoopCampaignMapConversationTableauCodec.IsValid(state), "fixture");
        Check(CoopCampaignMapConversationTableauCodec.TrySerialize(state, out byte[] bytes), "serialize");
        Check(CoopCampaignMapConversationTableauCodec.TryDeserialize(bytes, out var decoded), "deserialize");
        Check(CoopCampaignMapConversationTableauCodec.Equivalent(state, decoded), "all fields round trip");
        Compare(state, decoded, "round-trip");
        Compare(state, state.Clone(), "clone");
        foreach (float scale in new[] { 0.87f, 1.13f, 2.5f })
        {
            var scaled = state.Clone();
            scaled.Actors[0].Scale = scale;
            Check(CoopCampaignMapConversationTableauCodec.TrySerialize(scaled, out var scaledBytes) &&
                CoopCampaignMapConversationTableauCodec.TryDeserialize(scaledBytes, out var scaledResult) &&
                scaledResult.Actors[0].Scale == scale, "resolved non-unit actor scale round trip");
        }
        var copy = state.Clone();
        copy.CameraFrame[9] = 7;
        copy.Actors[0].Frame[9] = 8;
        copy.Actors[0].Visual.EquipmentItemIds[0] = "other_item";
        Check(state.CameraFrame[9] == 0 && state.Actors[0].Frame[9] == 1 &&
            state.Actors[0].Visual.EquipmentItemIds[0] == "helmet", "deep clone");
        Check(!CoopCampaignMapConversationTableauCodec.Equivalent(state, copy), "changed projection");
        Check(!CoopCampaignMapConversationTableauCodec.TrySerialize(null, out _), "null state");
        Check(!CoopCampaignMapConversationTableauCodec.TryDeserialize(new byte[65537], out _), "payload cap");
        Check(!CoopCampaignMapConversationTableauCodec.TryDeserialize(null, out _), "missing payload");

        var invalid = new List<Action<CoopCampaignMapConversationTableauState>>
        {
            s => s.AtmosphereName = "../conv_plains_noon_0",
            s => s.AtmosphereName = "other_scene",
            s => s.AtmosphereName = new string('a', 129),
            s => s.CameraFrame = null,
            s => s.CameraFrame = new float[11],
            s => s.CameraFrame[0] = float.NaN,
            s => s.CameraFrame[10] = float.PositiveInfinity,
            s => s.CameraFrame[9] = 10001,
            s => s.CameraFrame[0] = 0,
            s => s.CameraFrame[0] = -1,
            s => s.CameraFrame[3] = 0.5f,
            s => s.BaseCameraHorizontalFov = 0,
            s => s.BaseCameraHorizontalFov = float.NaN,
            s => s.BaseCameraHorizontalFov = 4,
            s => s.Actors = null,
            s => s.Actors.Clear(),
            s => s.Actors.Add(s.Actors[0].Clone()),
            s => s.Actors[0] = null,
            s => s.Actors[0].Visual = null,
            s => s.Actors[0].Frame = new float[12],
            s => s.Actors[0].Scale = 0,
            s => s.Actors[0].Scale = float.NegativeInfinity,
            s => s.Actors[0].ActionSetName = "path/to/action",
            s => s.Actors[0].InitialActionName = "exec_command",
            s => s.Actors[0].BannerCode = "<script>",
            s => s.Actors[0].BannerCode = new string('1', 513),
            s => s.Actors[0].Visual.HasBanner = true,
            s => s.Actors[0].Visual.BodyProperties = "",
            s => s.Actors[0].Visual.BodyProperties = new string('a', 2049),
            s => s.Actors[0].Visual.Race = -1,
            s => s.Actors[0].Visual.SkeletonType = -1,
            s => s.Actors[0].Visual.RightWieldedItemIndex = 99,
            s => s.Actors[0].Visual.MountCreationKey = "horse",
            s => s.Actors[0].Visual.EquipmentItemIds = new string[1],
            s => s.Actors[0].Visual.EquipmentItemIds[0] = "../item",
            s => s.Actors[0].Visual.EquipmentItemIds[0] = null
        };
        for (int i = 0; i < invalid.Count; i++)
        {
            var bad = Fixture();
            invalid[i](bad);
            Check(!CoopCampaignMapConversationTableauCodec.TrySerialize(bad, out _), "invalid field " + i);
        }
        copy = Fixture();
        copy.Actors[0].ClothColor1++;
        Check(CoopCampaignMapConversationTableauCodec.IsValid(copy) &&
            !CoopCampaignMapConversationTableauCodec.Equivalent(state, copy), "colors retained");

        for (int n = 0; n < bytes.Length; n++)
        {
            var truncated = new byte[n];
            Buffer.BlockCopy(bytes, 0, truncated, 0, n);
            Check(!CoopCampaignMapConversationTableauCodec.TryDeserialize(truncated, out _), "truncated " + n);
        }
        var trailing = new byte[bytes.Length + 1];
        Buffer.BlockCopy(bytes, 0, trailing, 0, bytes.Length);
        Check(!CoopCampaignMapConversationTableauCodec.TryDeserialize(trailing, out _), "trailing bytes");
        foreach (int offset in new[] { 0, 4, 8 })
        {
            var bad = (byte[])bytes.Clone();
            Buffer.BlockCopy(BitConverter.GetBytes(int.MaxValue), 0, bad, offset, 4);
            Check(!CoopCampaignMapConversationTableauCodec.TryDeserialize(bad, out _), "magic/schema/size " + offset);
        }
        var malformed = (byte[])bytes.Clone();
        malformed[12] = 0xff;
        Check(!CoopCampaignMapConversationTableauCodec.TryDeserialize(malformed, out _), "invalid UTF-8");
        malformed = (byte[])bytes.Clone();
        malformed[12 + Encoding.UTF8.GetByteCount(state.AtmosphereName)] = 2;
        Check(!CoopCampaignMapConversationTableauCodec.TryDeserialize(malformed, out _), "noncanonical boolean");
        var surrogate = Fixture();
        surrogate.Actors[0].Visual.BodyProperties = "<BodyProperties key=\"\ud800\" />";
        Check(!CoopCampaignMapConversationTableauCodec.TrySerialize(surrogate, out _), "invalid UTF-16");
        var random = new Random(727);
        for (int i = 0; i < 1000; i++)
        {
            var mutated = (byte[])bytes.Clone();
            mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
            // Any accepted mutation must still satisfy the complete descriptor contract.
            bool accepted = CoopCampaignMapConversationTableauCodec.TryDeserialize(mutated, out var candidate);
            Check(!accepted || CoopCampaignMapConversationTableauCodec.IsValid(candidate), "bounded mutation " + i);
        }

        // Exercise the actual existing bounded chunk codec without native packets.
        Check(CoopCampaignMapCatalogChunkCodec.TryEncode(bytes, out var payload, out _), "chunk encode");
        Check(CoopCampaignMapCatalogChunkAccumulator.TryCreate(1, 17, payload.LogicalByteCount,
            payload.WireByteCount, payload.ChunkCount, payload.CompressionKind, payload.PayloadHash,
            out var assembler, out _), "chunk admission");
        for (int i = payload.ChunkCount - 1; i >= 0; i--)
            Check(assembler.TryAccept(i, payload.ChunkCount, payload.Chunks[i], out _), "chunk accept " + i);
        Check(assembler.TryComplete(out byte[] reassembled, out _) &&
            CoopCampaignMapConversationTableauCodec.TryDeserialize(reassembled, out var unpacked) &&
            CoopCampaignMapConversationTableauCodec.Equivalent(state, unpacked), "chunk round trip");
        ValidateRevisionTransport(state, bytes, payload);
        Console.WriteLine("Conversation tableau descriptor assertions passed: " + _assertions);
    }

    internal static CoopCampaignMapConversationTableauState Fixture()
    {
        var state = new CoopCampaignMapConversationTableauState
        {
            AtmosphereName = "conv_plains_noon_0", IsRaining = true,
            CameraFrame = Frame(0), BaseCameraHorizontalFov = 1.1f
        };
        for (int i = 0; i < 3; i++)
        {
            var visual = new CoopCampaignMapPrototypeAgentVisualState
            {
                BodyProperties = "<BodyProperties version=\"4\" />",
                MountCreationKey = string.Empty, IsFemale = i == 1, SkeletonType = i == 1 ? 1 : 0,
                RightWieldedItemIndex = -1, LeftWieldedItemIndex = -1,
                HasBanner = i == 1, AddColorRandomness = i == 2
            };
            for (int slot = 0; slot < visual.EquipmentItemIds.Length; slot++)
                visual.EquipmentItemIds[slot] = slot == 0 ? "helmet" : string.Empty;
            state.Actors.Add(new CoopCampaignMapConversationActorState
            {
                Visual = visual, Frame = Frame(i + 1), Scale = 1f + i * 0.1f,
                ClothColor1 = 0xff123456, ClothColor2 = 0xfffedcba,
                BannerCode = i == 1 ? "11.2.3.4" : string.Empty,
                ActionSetName = "as_human_warrior", InitialActionName = "act_inventory_idle_start"
            });
        }
        return state;
    }

    private static float[] Frame(float x) => new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, x, 2, 3 };
    private static void ValidateRevisionTransport(CoopCampaignMapConversationTableauState state,
        byte[] bytes, CoopCampaignMapCatalogChunkedPayload payload)
    {
        var receiver = new CoopCampaignMapConversationTableauAssembler();
        CoopCampaignMapConversationTableauState completed = null;
        for (int i = payload.ChunkCount - 1; i >= 0; i--)
        {
            Check(Accept(receiver, 5, payload, i, out completed), "scoped chunk");
            if (i > 0) Check(completed == null, "incomplete hidden");
        }
        Compare(state, completed, "scoped reassembly");
        Check(!Accept(receiver, 5, payload, 0, out _), "completed duplicate");
        Check(!Accept(receiver, 4, payload, 0, out _), "old revision");
        receiver.DiscardThrough(6);
        Check(!Accept(receiver, 6, payload, 0, out _), "close barrier");
        Check(!Accept(receiver, 5, payload, 0, out _), "delayed open after close");
        receiver.Reset();
        Check(Accept(receiver, 0, payload, 0, out _), "new mission revision reset");

        // Uncompressed multi-chunk payload provides deterministic partial assembly.
        string hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        int count = (bytes.Length + 255) / 256;
        byte[] first = new byte[256];
        Buffer.BlockCopy(bytes, 0, first, 0, 256);
        receiver.Reset();
        Check(receiver.TryAccept(10, bytes.Length, bytes.Length, count,
            CoopCampaignMapCatalogCompressionKind.None, hash, 0, first, out completed) && completed == null,
            "partial transfer");
        Check(!receiver.TryAccept(10, bytes.Length + 1, bytes.Length, count,
            CoopCampaignMapCatalogCompressionKind.None, hash, 0, first, out _), "mixed metadata");
        Check(!receiver.TryAccept(10, bytes.Length, bytes.Length, count,
            CoopCampaignMapCatalogCompressionKind.None, new string('0', 64), 0, first, out _), "mixed hash");
        var corrupt = (byte[])first.Clone(); corrupt[0] ^= 1;
        Check(!receiver.TryAccept(10, bytes.Length, bytes.Length, count,
            CoopCampaignMapCatalogCompressionKind.None, hash, 0, corrupt, out _), "conflicting duplicate");
        Check(receiver.TryAccept(11, bytes.Length, bytes.Length, count,
            CoopCampaignMapCatalogCompressionKind.None, hash, 0, first, out _), "new revision supersedes");
        Check(!receiver.TryAccept(10, bytes.Length, bytes.Length, count,
            CoopCampaignMapCatalogCompressionKind.None, hash, 0, first, out _), "old partial rejected");
        receiver.DiscardThrough(12);
        Check(!receiver.TryAccept(11, bytes.Length, bytes.Length, count,
            CoopCampaignMapCatalogCompressionKind.None, hash, 0, first, out _), "partial close discard");
        receiver.Reset();
        Check(!receiver.TryAccept(1, 65537, 256, 1, CoopCampaignMapCatalogCompressionKind.Gzip,
            hash, 0, first, out _), "decompression budget before allocation");
        Check(!receiver.TryAccept(1, 256, 65537, 257, CoopCampaignMapCatalogCompressionKind.None,
            hash, 0, first, out _), "wire budget before allocation");
        Check(!receiver.TryAccept(1, 256, 256, 1, (CoopCampaignMapCatalogCompressionKind)99,
            hash, 0, first, out _), "unknown compression");
        Check(!receiver.TryAccept(1, 256, 256, 1, CoopCampaignMapCatalogCompressionKind.None,
            "bad", 0, first, out _), "bad hash");
        receiver.Reset();
        for (int i = 0; i < count; i++)
        {
            var chunk = new byte[Math.Min(256, bytes.Length - 256 * i)];
            Buffer.BlockCopy(bytes, 256 * i, chunk, 0, chunk.Length);
            bool accepted = receiver.TryAccept(20, bytes.Length, bytes.Length, count,
                CoopCampaignMapCatalogCompressionKind.None, new string('0', 64), i, chunk, out completed);
            Check(i == count - 1 ? !accepted && completed == null : accepted, "integrity rejection " + i);
        }
    }

    private static bool Accept(CoopCampaignMapConversationTableauAssembler receiver, int revision,
        CoopCampaignMapCatalogChunkedPayload payload, int index, out CoopCampaignMapConversationTableauState result) =>
        receiver.TryAccept(revision, payload.LogicalByteCount, payload.WireByteCount, payload.ChunkCount,
            payload.CompressionKind, payload.PayloadHash, index, payload.Chunks[index], out result);

    private static void Compare(object expected, object actual, string path)
    {
        if (expected == null || actual == null) { Check(expected == actual, path); return; }
        Type type = expected.GetType();
        Check(type == actual.GetType(), path + " type");
        if (type.IsValueType || expected is string) { Check(expected.Equals(actual), path); return; }
        Check(!ReferenceEquals(expected, actual), path + " detached");
        if (expected is IList list)
        {
            var other = (IList)actual;
            Check(list.Count == other.Count, path + " count");
            for (int i = 0; i < list.Count; i++) Compare(list[i], other[i], path + "[" + i + "]");
            return;
        }
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            Compare(property.GetValue(expected), property.GetValue(actual), path + "." + property.Name);
    }
    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException("Conversation tableau: " + message);
    }
}
