using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.Patches;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.GauntletUI.TextureProviders.ImageIdentifiers;
using TaleWorlds.MountAndBlade.View.Tableaus;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;

namespace CoopSpectator.UI
{
    // Discovered by the stock TextureProviderFactory. The stock base owns
    // callback delivery, reference removal, cancellation and disposal.
    public sealed class CoopReplicaPortraitTextureProvider : ImageIdentifierTextureProvider
    {
        protected override void OnCreateImageWithId(string id, string additionalArgs)
        {
            if (!CoopReplicaPortraitPosePatch.IsInstalled || !GameNetwork.IsMultiplayer ||
                string.IsNullOrEmpty(id) || additionalArgs == null || additionalArgs.Length != 1 ||
                additionalArgs[0] < '0' || additionalArgs[0] > '7')
            { OnTextureCreated(null); return; }
            var character = CharacterCode.CreateFrom(id);
            if (!character.IsHero || (int)TaleWorlds.Core.FaceGen.GetMaturityTypeWithAge(character.BodyProperties.Age) <= 1)
            { OnTextureCreated(null); return; }
            var request = new CoopReplicaPortraitCreationData(character, OnTextureCreated,
                OnTextureCreationCancelled, IsBig, additionalArgs[0] - '0');
            ThumbnailCreationData = request;
            ThumbnailCacheManager.Current.CreateTexture(request);
        }
    }
}
