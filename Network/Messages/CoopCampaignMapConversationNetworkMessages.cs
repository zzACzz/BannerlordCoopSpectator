using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapConversationSnapshotMessage :
        GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 31, true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, true);
        private static readonly CompressionInfo.Integer CountCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.MaxConversationOptions,
                true);

        public CoopCampaignMapConversationSnapshotMessage(
            CoopCampaignMapConversationState state)
        {
            ProtocolVersion = CoopCampaignMapPrototypeContract.ProtocolVersion;
            Revision = Math.Max(0, state?.Revision ?? 0);
            IsActive = state?.IsActive ?? false;
            SpeakerName = IsActive
                ? Bound(
                    state?.SpeakerName,
                    CoopCampaignMapPrototypeContract
                        .MaxConversationSpeakerCharacters)
                : string.Empty;
            DialogText = IsActive
                ? Bound(
                    state?.DialogText,
                    CoopCampaignMapPrototypeContract
                        .MaxConversationDialogCharacters)
                : string.Empty;
            GoldText = IsActive
                ? Bound(
                    state?.GoldText,
                    CoopCampaignMapPrototypeContract
                        .MaxConversationGoldCharacters)
                : string.Empty;
            ContinueText = IsActive
                ? Bound(
                    state?.ContinueText,
                    CoopCampaignMapPrototypeContract
                        .MaxConversationContinueCharacters)
                : string.Empty;
            OptionCount = IsActive
                ? Math.Max(
                    0,
                    Math.Min(
                        CoopCampaignMapPrototypeContract.MaxConversationOptions,
                        state?.Options?.Count ?? 0))
                : 0;
        }

        public CoopCampaignMapConversationSnapshotMessage()
        {
        }

        public int ProtocolVersion { get; private set; }

        public int Revision { get; private set; }

        public bool IsActive { get; private set; }

        public string SpeakerName { get; private set; } = string.Empty;

        public string DialogText { get; private set; } = string.Empty;

        public string GoldText { get; private set; } = string.Empty;

        public string ContinueText { get; private set; } = string.Empty;

        public int OptionCount { get; private set; }

        public CoopCampaignMapConversationState ToState()
        {
            return new CoopCampaignMapConversationState
            {
                Revision = Revision,
                IsActive = IsActive,
                SpeakerName = SpeakerName ?? string.Empty,
                DialogText = DialogText ?? string.Empty,
                GoldText = GoldText ?? string.Empty,
                ContinueText = ContinueText ?? string.Empty
            };
        }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            IsActive = ReadBoolFromPacket(ref valid);
            SpeakerName = ReadStringFromPacket(ref valid) ?? string.Empty;
            DialogText = ReadStringFromPacket(ref valid) ?? string.Empty;
            GoldText = ReadStringFromPacket(ref valid) ?? string.Empty;
            ContinueText = ReadStringFromPacket(ref valid) ?? string.Empty;
            OptionCount = ReadIntFromPacket(CountCompression, ref valid);
            return valid &&
                   CoopCampaignMapPrototypeContract.TryValidateConversationHeader(
                       ToState(),
                       OptionCount,
                       out _);
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteIntToPacket(Math.Max(0, Revision), RevisionCompression);
            WriteBoolToPacket(IsActive);
            WriteStringToPacket(Bound(
                SpeakerName,
                CoopCampaignMapPrototypeContract
                    .MaxConversationSpeakerCharacters));
            WriteStringToPacket(Bound(
                DialogText,
                CoopCampaignMapPrototypeContract
                    .MaxConversationDialogCharacters));
            WriteStringToPacket(Bound(
                GoldText,
                CoopCampaignMapPrototypeContract
                    .MaxConversationGoldCharacters));
            WriteStringToPacket(Bound(
                ContinueText,
                CoopCampaignMapPrototypeContract
                    .MaxConversationContinueCharacters));
            WriteIntToPacket(
                Math.Max(
                    0,
                    Math.Min(
                        CoopCampaignMapPrototypeContract.MaxConversationOptions,
                        OptionCount)),
                CountCompression);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() =>
            MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat() =>
            "CoopCampaignMapConversationSnapshot Revision=" + Revision +
            " Active=" + IsActive +
            " Options=" + OptionCount;

        private static string Bound(string value, int maximumCharacters)
        {
            return CoopCampaignMapPrototypeContract.BoundConversationText(
                value,
                maximumCharacters);
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapConversationOptionMessage :
        GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 31, true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, true);
        private static readonly CompressionInfo.Integer CountCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.MaxConversationOptions,
                true);
        private static readonly CompressionInfo.Integer IndexCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.MaxConversationOptions - 1,
                true);

        public CoopCampaignMapConversationOptionMessage(
            int revision,
            int index,
            int expectedCount,
            CoopCampaignMapConversationOptionState option)
        {
            ProtocolVersion = CoopCampaignMapPrototypeContract.ProtocolVersion;
            Revision = Math.Max(0, revision);
            Index = Math.Max(
                0,
                Math.Min(
                    CoopCampaignMapPrototypeContract.MaxConversationOptions - 1,
                    index));
            ExpectedCount = Math.Max(
                0,
                Math.Min(
                    CoopCampaignMapPrototypeContract.MaxConversationOptions,
                    expectedCount));
            Text = CoopCampaignMapPrototypeContract.BoundConversationText(
                option?.Text,
                CoopCampaignMapPrototypeContract.MaxConversationOptionCharacters);
            IsEnabled = option?.IsEnabled ?? false;
            IsSpecial = option?.IsSpecial ?? false;
        }

        public CoopCampaignMapConversationOptionMessage()
        {
        }

        public int ProtocolVersion { get; private set; }

        public int Revision { get; private set; }

        public int Index { get; private set; }

        public int ExpectedCount { get; private set; }

        public string Text { get; private set; } = string.Empty;

        public bool IsEnabled { get; private set; }

        public bool IsSpecial { get; private set; }

        public CoopCampaignMapConversationOptionState ToOption()
        {
            return new CoopCampaignMapConversationOptionState
            {
                Text = Text ?? string.Empty,
                IsEnabled = IsEnabled,
                IsSpecial = IsSpecial
            };
        }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            Index = ReadIntFromPacket(IndexCompression, ref valid);
            ExpectedCount = ReadIntFromPacket(CountCompression, ref valid);
            Text = ReadStringFromPacket(ref valid) ?? string.Empty;
            IsEnabled = ReadBoolFromPacket(ref valid);
            IsSpecial = ReadBoolFromPacket(ref valid);
            return valid &&
                   ExpectedCount > 0 &&
                   Index < ExpectedCount &&
                   CoopCampaignMapPrototypeContract.IsValidConversationOption(
                       ToOption());
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteIntToPacket(Math.Max(0, Revision), RevisionCompression);
            WriteIntToPacket(
                Math.Max(
                    0,
                    Math.Min(
                        CoopCampaignMapPrototypeContract.MaxConversationOptions - 1,
                        Index)),
                IndexCompression);
            WriteIntToPacket(
                Math.Max(
                    0,
                    Math.Min(
                        CoopCampaignMapPrototypeContract.MaxConversationOptions,
                        ExpectedCount)),
                CountCompression);
            WriteStringToPacket(
                CoopCampaignMapPrototypeContract.BoundConversationText(
                    Text,
                    CoopCampaignMapPrototypeContract
                        .MaxConversationOptionCharacters));
            WriteBoolToPacket(IsEnabled);
            WriteBoolToPacket(IsSpecial);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() =>
            MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat() =>
            "CoopCampaignMapConversationOption Revision=" + Revision +
            " Index=" + Index +
            " Count=" + ExpectedCount;
    }
}
