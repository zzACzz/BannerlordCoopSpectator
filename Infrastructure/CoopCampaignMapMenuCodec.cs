using System;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    public static class CoopCampaignMapMenuCodec
    {
        public const int MaxPayloadBytes = 1048576;
        private const int Magic = 0x434D4E37;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        // A null presentation is a versioned close, not a missing update.
        public static bool TrySerialize(long revision, CoopCampaignMapMenuPresentation menu, out byte[] payload)
        {
            payload = null;
            if (revision <= 0 || (menu != null && !menu.IsValid())) return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Utf8, true))
                {
                    writer.Write(Magic); writer.Write(revision); writer.Write(menu != null);
                    if (menu != null)
                    {
                        WriteText(writer, menu.MenuId); WriteText(writer, menu.Title);
                        WriteText(writer, menu.Context); WriteText(writer, menu.Background);
                        writer.Write(menu.IsNight); writer.Write(menu.IsEncounterMenu); writer.Write(menu.IsInSiegeMode);
                        writer.Write(menu.Options.Count);
                        foreach (var option in menu.Options)
                        {
                            WriteText(writer, option.Text); WriteText(writer, option.Hint); WriteText(writer, option.LeaveType);
                            writer.Write(option.ItemType); writer.Write(option.BattleSize);
                            writer.Write(option.IsEnabled); writer.Write(option.IsWaitActive);
                            writer.Write(option.IsHighlightEnabled); writer.Write(option.IsNavalBattle);
                        }
                        writer.Write(menu.ProgressItems.Count);
                        foreach (var progress in menu.ProgressItems)
                        {
                            WriteText(writer, progress.Text); WriteText(writer, progress.ProgressText);
                            writer.Write(progress.Progress);
                        }
                        writer.Write(menu.PlunderItems.Count);
                        foreach (var loot in menu.PlunderItems)
                        { WriteText(writer, loot.ItemId); writer.Write(loot.Amount); WriteText(writer, loot.TooltipId); }
                        WriteEncounter(writer, menu.Encounter);
                        writer.Write(menu.SiegeOverlay != null);
                        if (menu.SiegeOverlay != null)
                        {
                            if (!CoopCampaignMapSiegeOverlayCodec.TryEncode(menu.SiegeOverlay, out var siegeBytes)) return false;
                            writer.Write(siegeBytes.Length); writer.Write(siegeBytes);
                        }
                    }
                    writer.Flush();
                    if (stream.Length > MaxPayloadBytes) return false;
                    payload = stream.ToArray(); return true;
                }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
        }

        public static bool TryDeserialize(byte[] payload, out long revision, out CoopCampaignMapMenuPresentation menu)
        {
            revision = 0; menu = null;
            if (payload == null || payload.Length < 13 || payload.Length > MaxPayloadBytes) return false;
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    long decodedRevision = reader.ReadInt64();
                    if (decodedRevision <= 0) return false;
                    CoopCampaignMapMenuPresentation decoded = null;
                    if (ReadBool(reader))
                    {
                        decoded = new CoopCampaignMapMenuPresentation
                        {
                            MenuId = ReadText(reader, 256), Title = ReadText(reader),
                            Context = ReadText(reader), Background = ReadText(reader, 256),
                            IsNight = ReadBool(reader), IsEncounterMenu = ReadBool(reader), IsInSiegeMode = ReadBool(reader)
                        };
                        int count = ReadCount(reader, CoopCampaignMapMenuPresentation.MaxOptions);
                        for (int i = 0; i < count; i++) decoded.Options.Add(new CoopCampaignMapMenuOption
                        {
                            Text = ReadText(reader), Hint = ReadText(reader), LeaveType = ReadText(reader, 256),
                            ItemType = reader.ReadInt32(), BattleSize = reader.ReadInt32(),
                            IsEnabled = ReadBool(reader), IsWaitActive = ReadBool(reader),
                            IsHighlightEnabled = ReadBool(reader), IsNavalBattle = ReadBool(reader)
                        });
                        count = ReadCount(reader, CoopCampaignMapMenuPresentation.MaxProgressItems);
                        for (int i = 0; i < count; i++) decoded.ProgressItems.Add(new CoopCampaignMapMenuProgress
                        { Text = ReadText(reader), ProgressText = ReadText(reader), Progress = reader.ReadSingle() });
                        count = ReadCount(reader, CoopCampaignMapMenuPresentation.MaxPlunderItems);
                        for (int i = 0; i < count; i++) decoded.PlunderItems.Add(new CoopCampaignMapMenuPlunder
                        { ItemId = ReadText(reader, 256), Amount = reader.ReadInt32(), TooltipId = ReadText(reader, 40) });
                        decoded.Encounter = ReadEncounter(reader);
                        if (ReadBool(reader))
                        {
                            int siegeLength = reader.ReadInt32();
                            if (siegeLength < CoopCampaignMapSiegeOverlayCodec.MinimumBytes || siegeLength > CoopCampaignMapSiegeOverlayCodec.MaximumBytes ||
                                siegeLength > stream.Length - stream.Position ||
                                !CoopCampaignMapSiegeOverlayCodec.TryDecode(reader.ReadBytes(siegeLength), out var siege)) return false;
                            decoded.SiegeOverlay = siege;
                        }
                        if (!decoded.IsValid()) return false;
                    }
                    if (stream.Position != stream.Length) return false;
                    revision = decodedRevision; menu = decoded; return true;
                }
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (ArgumentException) { return false; }
        }

        private static void WriteEncounter(BinaryWriter w, CoopCampaignMapEncounterPresentation e)
        {
            w.Write(e != null);
            if (e == null) return;
            WriteText(w, e.TitleText); w.Write(e.IsSiege); w.Write(e.IsNaval);
            WriteText(w, e.AttackerBanner); WriteText(w, e.DefenderBanner);
            WriteText(w, e.AttackerCountLabel); WriteText(w, e.DefenderCountLabel);
            WriteText(w, e.AttackerMorale); WriteText(w, e.DefenderMorale);
            WriteText(w, e.AttackerFood); WriteText(w, e.DefenderFood); WriteText(w, e.DefenderWallHitPoints);
            w.Write(e.AttackerShipCount); w.Write(e.DefenderShipCount);
            w.Write(e.AttackerPower); w.Write(e.DefenderPower);
            w.Write(e.InitialAttackerPower); w.Write(e.InitialDefenderPower);
            WriteText(w, e.AttackerColor); WriteText(w, e.DefenderColor);
            WriteParties(w, e.Attackers); WriteParties(w, e.Defenders);
        }

        private static CoopCampaignMapEncounterPresentation ReadEncounter(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            return new CoopCampaignMapEncounterPresentation
            {
                TitleText = ReadText(r), IsSiege = ReadBool(r), IsNaval = ReadBool(r),
                AttackerBanner = ReadText(r, CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters),
                DefenderBanner = ReadText(r, CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters),
                AttackerCountLabel = ReadText(r), DefenderCountLabel = ReadText(r),
                AttackerMorale = ReadText(r), DefenderMorale = ReadText(r),
                AttackerFood = ReadText(r), DefenderFood = ReadText(r), DefenderWallHitPoints = ReadText(r),
                AttackerShipCount = r.ReadInt32(), DefenderShipCount = r.ReadInt32(),
                AttackerPower = r.ReadDouble(), DefenderPower = r.ReadDouble(),
                InitialAttackerPower = r.ReadDouble(), InitialDefenderPower = r.ReadDouble(),
                AttackerColor = ReadText(r, 9), DefenderColor = ReadText(r, 9),
                Attackers = ReadParties(r), Defenders = ReadParties(r)
            };
        }

        private static void WriteParties(BinaryWriter w, System.Collections.Generic.List<CoopCampaignMapEncounterParty> parties)
        {
            w.Write(parties.Count);
            foreach (var p in parties)
            {
                WriteText(w, p.PortraitCode); WriteText(w, p.BannerCode);
                w.Write(p.PortraitPose);
                WriteText(w, p.TooltipId);
                WriteText(w, p.NameText); WriteText(w, p.PartySizeLabel);
                WriteText(w, p.DescriptionText); WriteText(w, p.LocationText);
                WriteText(w, p.PowerText); WriteText(w, p.ProfessionText);
                w.Write(p.Relation); w.Write(p.ShipCount);
                w.Write(p.IsPlayer); w.Write(p.IsEnemy); w.Write(p.IsAlly); w.Write(p.IsNeutral);
                w.Write(p.IsCharacterInPrison); w.Write(p.IsHighlightEnabled);
                w.Write(p.QuestMarkers.Count);
                foreach (int q in p.QuestMarkers) w.Write(q);
            }
        }

        private static System.Collections.Generic.List<CoopCampaignMapEncounterParty> ReadParties(BinaryReader r)
        {
            int count = ReadCount(r, CoopCampaignMapEncounterPresentation.MaxPartiesPerSide);
            var result = new System.Collections.Generic.List<CoopCampaignMapEncounterParty>(count);
            for (int i = 0; i < count; i++)
            {
                var p = new CoopCampaignMapEncounterParty
                {
                    PortraitCode = ReadText(r, CoopConversationHudCodec.MaxPortraitCharacters),
                    BannerCode = ReadText(r, CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters),
                    PortraitPose = r.ReadInt32(),
                    TooltipId = ReadText(r, 42),
                    NameText = ReadText(r), PartySizeLabel = ReadText(r),
                    DescriptionText = ReadText(r), LocationText = ReadText(r),
                    PowerText = ReadText(r), ProfessionText = ReadText(r),
                    Relation = r.ReadInt32(), ShipCount = r.ReadInt32(),
                    IsPlayer = ReadBool(r), IsEnemy = ReadBool(r), IsAlly = ReadBool(r), IsNeutral = ReadBool(r),
                    IsCharacterInPrison = ReadBool(r), IsHighlightEnabled = ReadBool(r)
                };
                int quests = ReadCount(r, 32);
                for (int q = 0; q < quests; q++) p.QuestMarkers.Add(r.ReadInt32());
                result.Add(p);
            }
            return result;
        }

        private static void WriteText(BinaryWriter writer, string text)
        {
            byte[] bytes = Utf8.GetBytes(text);
            if (writer.BaseStream.Position + 4 + bytes.Length > MaxPayloadBytes)
                throw new InvalidDataException("Menu payload limit exceeded.");
            writer.Write(bytes.Length); writer.Write(bytes);
        }
        private static string ReadText(BinaryReader reader, int limit = CoopCampaignMapMenuPresentation.MaxTextCharacters)
        {
            int bytes = reader.ReadInt32();
            if (bytes < 0 || bytes > limit * 4 || bytes > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Invalid menu text length.");
            string text = Utf8.GetString(reader.ReadBytes(bytes));
            if (text.Length > limit) throw new InvalidDataException("Menu text limit exceeded.");
            return text;
        }
        private static bool ReadBool(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value > 1) throw new InvalidDataException("Invalid menu boolean.");
            return value == 1;
        }
        private static int ReadCount(BinaryReader reader, int limit)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > limit) throw new InvalidDataException("Invalid menu list length.");
            return count;
        }
    }
}
