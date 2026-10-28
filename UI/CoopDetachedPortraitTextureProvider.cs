using System;
using System.Collections.Generic;
using CoopSpectator.Patches;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.GauntletUI.TextureProviders.ImageIdentifiers;
using TaleWorlds.MountAndBlade.View.Tableaus;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;

namespace CoopSpectator.UI
{
    // Opaque local binding only; remote strings cannot choose equipment/providers.
    internal sealed class CoopDetachedPortraitBinding : IDisposable
    {
        private static readonly Dictionary<string, WeakReference> Bindings = new Dictionary<string, WeakReference>();
        internal readonly string Token = Guid.NewGuid().ToString("N");
        internal readonly string Code;
        internal readonly Equipment Equipment;
        internal readonly int Pose;
        internal CoopDetachedPortraitBinding(string code, Equipment equipment, int pose)
        {
            Code = code; Equipment = equipment; Pose = pose;
            lock (Bindings) Bindings.Add(Token, new WeakReference(this));
        }
        internal static CoopDetachedPortraitBinding Find(string token, string code)
        {
            if (token == null || token.Length != 32) return null;
            lock (Bindings)
                return Bindings.TryGetValue(token, out var weak) && weak.Target is CoopDetachedPortraitBinding binding &&
                    binding.Code == code ? binding : null;
        }
        public void Dispose() { lock (Bindings) Bindings.Remove(Token); }
    }

    internal sealed class CoopDetachedPortraitCreationData : CharacterThumbnailCreationData
    {
        internal readonly CoopDetachedPortraitBinding Binding;
        internal CoopDetachedPortraitCreationData(CharacterCode character, Action<Texture> completed,
            Action cancelled, bool big, CoopDetachedPortraitBinding binding) : base(character, completed, cancelled, big)
        {
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            // Fingerprinted equipment/body/size are already part of RenderId.
            // Opaque lifetime tokens must never fragment the thumbnail cache.
            RenderId += "_coop_detached_v1:" + binding.Pose.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public sealed class CoopDetachedPortraitTextureProvider : ImageIdentifierTextureProvider
    {
        protected override void OnCreateImageWithId(string id, string additionalArgs)
        {
            var binding = CoopDetachedPortraitBinding.Find(additionalArgs, id);
            if (!GameNetwork.IsClient || !CoopDetachedPortraitEquipmentPatch.IsInstalled || binding == null)
            { OnTextureCreated(null); return; }
            var request = new CoopDetachedPortraitCreationData(CharacterCode.CreateFrom(id), OnTextureCreated,
                OnTextureCreationCancelled, IsBig, binding);
            ThumbnailCreationData = request;
            ThumbnailCacheManager.Current.CreateTexture(request);
        }
    }
}
