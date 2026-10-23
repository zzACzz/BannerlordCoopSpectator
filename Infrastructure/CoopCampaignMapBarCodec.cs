using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    public static class CoopCampaignMapBarCodec
    {
        public const int MaximumBytes = 1048576;
        private const int Magic = 0x434D4234; // CMB4: host tutorial element
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        // Null is an explicit close. The containing channel must also bind the session.
        public static bool TryEncode(long revision, CoopCampaignMapBarPresentation bar, out byte[] bytes)
        {
            bytes = null;
            if (revision <= 0 || (bar != null && !bar.IsValid())) return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var w = new BinaryWriter(stream, Utf8, true))
                {
                    w.Write(Magic); w.Write(revision); w.Write(bar != null);
                    if (bar != null)
                    {
                        w.Write(bar.IsEnabled); w.Write(bar.IsGatherArmyVisible); w.Write(bar.CanGatherArmy);
                        w.Write(bar.IsCenterPanelEnabled); w.Write(bar.IsInfoBarEnabled); w.Write(bar.IsInfoBarExtended);
                        Text(w, bar.Date); Text(w, bar.PausedText); Text(w, bar.TutorialElementId);
                        w.Write(bar.Time); w.Write(bar.HoursInDay); w.Write(bar.TimeFlowState);
                        Infos(w, bar.PrimaryInfoItems); Infos(w, bar.SecondaryInfoItems);
                        w.Write(bar.NavigationItems.Count);
                        foreach (var item in bar.NavigationItems)
                        {
                            Text(w, item.ItemId); Text(w, item.AlertText);
                            w.Write(item.IsEnabled); w.Write(item.IsActive); w.Write(item.HasAlert);
                        }
                        w.Write(bar.ControlHints.Count);
                        foreach (var hint in bar.ControlHints)
                        { Text(w, hint.Key); Text(w, hint.HintToken); w.Write(hint.IsAvailable); }
                    }
                    w.Flush();
                    if (stream.Length > MaximumBytes) return false;
                    bytes = stream.ToArray(); return true;
                }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
        }

        public static bool TryDecode(byte[] bytes, long expectedRevision, out CoopCampaignMapBarPresentation bar)
        {
            bar = null;
            if (expectedRevision <= 0 || bytes == null || bytes.Length < 13 || bytes.Length > MaximumBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var r = new BinaryReader(stream, Utf8, true))
                {
                    if (r.ReadInt32() != Magic || r.ReadInt64() != expectedRevision) return false;
                    CoopCampaignMapBarPresentation decoded = null;
                    if (Boolean(r))
                    {
                        decoded = new CoopCampaignMapBarPresentation
                        {
                            IsEnabled = Boolean(r), IsGatherArmyVisible = Boolean(r), CanGatherArmy = Boolean(r),
                            IsCenterPanelEnabled = Boolean(r), IsInfoBarEnabled = Boolean(r), IsInfoBarExtended = Boolean(r),
                            Date = Text(r), PausedText = Text(r), TutorialElementId = Text(r, 128), Time = r.ReadDouble(),
                            HoursInDay = r.ReadInt32(), TimeFlowState = r.ReadInt32(),
                            PrimaryInfoItems = Infos(r), SecondaryInfoItems = Infos(r)
                        };
                        int count = Count(r);
                        for (int i = 0; i < count; i++)
                            decoded.NavigationItems.Add(new CoopCampaignMapBarNavigation
                            {
                                ItemId = Text(r, 128), AlertText = Text(r), IsEnabled = Boolean(r),
                                IsActive = Boolean(r), HasAlert = Boolean(r)
                            });
                        int hints = r.ReadInt32();
                        if (hints < 0 || hints > CoopCampaignMapBarPresentation.MaxControlHints) return false;
                        for (int i = 0; i < hints; i++)
                            decoded.ControlHints.Add(new CoopCampaignMapBarControlTarget
                            { Key = Text(r, 128), HintToken = Text(r, 40), IsAvailable = Boolean(r) });
                        if (!decoded.IsValid()) return false;
                    }
                    if (stream.Position != stream.Length) return false;
                    bar = decoded; return true;
                }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
        }

        private static void Infos(BinaryWriter w, List<CoopCampaignMapBarInfo> items)
        {
            w.Write(items.Count);
            foreach (var item in items)
            { Text(w, item.ItemId); Text(w, item.HintToken); Text(w, item.VisualId); Text(w, item.Value); w.Write(item.IntValue); w.Write(item.HasWarning); }
        }
        private static List<CoopCampaignMapBarInfo> Infos(BinaryReader r)
        {
            int count = Count(r);
            var result = new List<CoopCampaignMapBarInfo>(count);
            for (int i = 0; i < count; i++)
                result.Add(new CoopCampaignMapBarInfo
                { ItemId = Text(r, 128), HintToken = Text(r, 40), VisualId = Text(r, 128), Value = Text(r), IntValue = r.ReadInt32(), HasWarning = Boolean(r) });
            return result;
        }
        private static void Text(BinaryWriter w, string value)
        {
            byte[] bytes = Utf8.GetBytes(value);
            if (w.BaseStream.Position + 4 + bytes.Length > MaximumBytes) throw new InvalidDataException();
            w.Write(bytes.Length); w.Write(bytes);
        }
        private static string Text(BinaryReader r, int max = CoopCampaignMapBarPresentation.MaxTextLength)
        {
            int length = r.ReadInt32();
            if (length < 0 || length > max * 4 || length > r.BaseStream.Length - r.BaseStream.Position)
                throw new InvalidDataException();
            string value = Utf8.GetString(r.ReadBytes(length));
            if (value.Length > max) throw new InvalidDataException();
            return value;
        }
        private static int Count(BinaryReader r)
        {
            int count = r.ReadInt32();
            if (count < 0 || count > CoopCampaignMapBarPresentation.MaxItems) throw new InvalidDataException();
            return count;
        }
        private static bool Boolean(BinaryReader r)
        {
            byte value = r.ReadByte();
            if (value > 1) throw new InvalidDataException();
            return value == 1;
        }
    }
}
