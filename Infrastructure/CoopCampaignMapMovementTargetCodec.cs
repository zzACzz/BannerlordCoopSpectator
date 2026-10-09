using System;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    // One bounded host-owned recipe, carried atomically with the map state.
    public static class CoopCampaignMapMovementTargetCodec
    {
        public const int MaxEncodedCharacters = 768;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool IsFreshAndReady(double ageSeconds, int catalogRevision, int dynamicRevision,
            int completedCatalogRevision, int completedDynamicRevision) =>
            CoopCampaignMapMovementTargetPolicy.IsFinite(ageSeconds) && ageSeconds >= 0 && ageSeconds < 2 &&
            catalogRevision >= 0 && dynamicRevision >= 0 &&
            completedCatalogRevision >= catalogRevision && completedDynamicRevision >= dynamicRevision;

        public static bool TryEncode(CoopCampaignMapMovementTargetPresentation value, out string encoded)
        {
            encoded = null;
            if (value == null) { encoded = string.Empty; return true; }
            if (!ValidId(value) || !CoopCampaignMapMovementTargetPolicy.TryPointScale(value.HostCameraDistance, out _) ||
                !CoopCampaignMapMovementTargetPolicy.TryAnimationIndex(value.AnimationElapsedSeconds, out _) ||
                !CoopCampaignMapMovementTargetLayout.TryBuild(value, out _)) return false;
            if (value.Kind == CoopCampaignMapMovementTargetKind.Party ? value.VisualOriginOffset == null : value.VisualOriginOffset != null)
                return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Utf8))
                {
                    writer.Write((byte)1);
                    writer.Write((byte)value.Kind);
                    writer.Write((byte)((value.AtSea ? 1 : 0) | (value.Fortification ? 2 : 0)));
                    byte[] id = Utf8.GetBytes(value.EntityId ?? string.Empty);
                    writer.Write((ushort)id.Length);
                    writer.Write(id);
                    writer.Write(value.Color);
                    WriteVector(writer, value.CircleFrame, 12);
                    WriteVector(writer, value.Origin, 3);
                    WriteVector(writer, value.VisualOriginOffset, 3);
                    WriteVector(writer, value.TerrainNormal, 3);
                    WriteVector(writer, value.Wind, 2);
                    writer.Write(value.HostCameraDistance);
                    writer.Write(value.AnimationElapsedSeconds);
                    writer.Flush();
                    if (stream.Length > MaxEncodedCharacters / 4 * 3) return false;
                    encoded = Convert.ToBase64String(stream.ToArray());
                    return true;
                }
            }
            catch (ArgumentException) { return false; }
        }

        public static bool TryDecode(string encoded, out CoopCampaignMapMovementTargetPresentation value)
        {
            value = null;
            if (encoded == null || encoded.Length > MaxEncodedCharacters) return false;
            if (encoded.Length == 0) return true;
            try
            {
                byte[] bytes = Convert.FromBase64String(encoded);
                if (Convert.ToBase64String(bytes) != encoded) return false;
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8))
                {
                    if (reader.ReadByte() != 1) return false;
                    var kind = (CoopCampaignMapMovementTargetKind)reader.ReadByte();
                    byte flags = reader.ReadByte();
                    if (flags > 3) return false;
                    int idLength = reader.ReadUInt16();
                    if (idLength > CoopCampaignMapPrototypeContract.MaxEntityIdCharacters * 4 ||
                        idLength > stream.Length - stream.Position) return false;
                    string id = Utf8.GetString(reader.ReadBytes(idLength));
                    var candidate = new CoopCampaignMapMovementTargetPresentation {
                        Kind = kind, EntityId = id.Length == 0 ? null : id,
                        AtSea = (flags & 1) != 0, Fortification = (flags & 2) != 0,
                        Color = reader.ReadUInt32(), CircleFrame = ReadVector(reader, 12),
                        Origin = ReadVector(reader, 3), VisualOriginOffset = ReadVector(reader, 3), TerrainNormal = ReadVector(reader, 3),
                        Wind = ReadVector(reader, 2), HostCameraDistance = reader.ReadDouble(),
                        AnimationElapsedSeconds = reader.ReadDouble()
                    };
                    if (stream.Position != stream.Length || !ValidId(candidate) ||
                        !CoopCampaignMapMovementTargetLayout.TryBuild(candidate, out _) ||
                        !TryEncode(candidate, out var canonical) || canonical != encoded) return false;
                    value = candidate;
                    return true;
                }
            }
            catch (IOException) { return false; }
            catch (ArgumentException) { return false; }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
        }

        public static bool IsAdmitted(CoopCampaignMapMovementTargetPresentation value,
            CoopCampaignMapPrototypeCatalogEntityState catalog, CoopCampaignMapPrototypeDynamicEntityState dynamicState)
        {
            if (value == null || value.Kind == CoopCampaignMapMovementTargetKind.Point) return true;
            if (catalog == null || dynamicState == null || !dynamicState.IsVisible ||
                !string.Equals(value.EntityId, catalog.EntityId, StringComparison.Ordinal) ||
                !string.Equals(value.EntityId, dynamicState.EntityId, StringComparison.Ordinal)) return false;
            return value.Kind == CoopCampaignMapMovementTargetKind.Settlement
                ? catalog.Kind == CoopCampaignMapPrototypeEntityKind.Settlement &&
                  value.Fortification == (catalog.SettlementKind == CoopCampaignMapPrototypeSettlementKind.Town ||
                    catalog.SettlementKind == CoopCampaignMapPrototypeSettlementKind.Castle)
                : value.Kind == CoopCampaignMapMovementTargetKind.Party &&
                  (catalog.Kind == CoopCampaignMapPrototypeEntityKind.MobileParty ||
                   catalog.Kind == CoopCampaignMapPrototypeEntityKind.MainParty);
        }

        private static bool ValidId(CoopCampaignMapMovementTargetPresentation value)
        {
            if (value.Kind == CoopCampaignMapMovementTargetKind.Point) return value.EntityId == null;
            string id = value.EntityId;
            if (string.IsNullOrWhiteSpace(id) || id.Length > CoopCampaignMapPrototypeContract.MaxEntityIdCharacters) return false;
            foreach (char c in id) if (char.IsControl(c)) return false;
            string prefix = value.Kind == CoopCampaignMapMovementTargetKind.Settlement ? "settlement:" : "party:";
            return id.StartsWith(prefix, StringComparison.Ordinal) && id.Length > prefix.Length;
        }

        private static void WriteVector(BinaryWriter writer, float[] vector, int length)
        {
            writer.Write((byte)(vector == null ? 0 : 1));
            if (vector == null) return;
            if (vector.Length != length) throw new ArgumentException("Vector length");
            foreach (float component in vector)
            {
                if (!CoopCampaignMapMovementTargetPolicy.IsFinite(component) || Math.Abs(component) > 100000)
                    throw new ArgumentException("Vector range");
                writer.Write(component);
            }
        }

        private static float[] ReadVector(BinaryReader reader, int length)
        {
            byte present = reader.ReadByte();
            if (present == 0) return null;
            if (present != 1) throw new FormatException("Vector tag");
            var vector = new float[length];
            for (int i = 0; i < length; i++) vector[i] = reader.ReadSingle();
            return vector;
        }
    }
}
