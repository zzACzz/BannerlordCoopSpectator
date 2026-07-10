using System;
using TaleWorlds.Core;

namespace CoopSpectator.Infrastructure
{
    internal sealed class BannerAwareAgentOrigin : IAgentOriginBase
    {
        private readonly IAgentOriginBase _innerOrigin;
        private Banner _banner;

        private BannerAwareAgentOrigin(IAgentOriginBase innerOrigin, Banner banner)
        {
            _innerOrigin = innerOrigin ?? throw new ArgumentNullException(nameof(innerOrigin));
            _banner = banner;
        }

        public static IAgentOriginBase Ensure(IAgentOriginBase origin, Banner banner)
        {
            if (origin == null || banner == null)
                return origin;

            if (origin is BannerAwareAgentOrigin bannerAwareOrigin)
            {
                bannerAwareOrigin.SetBanner(banner);
                return bannerAwareOrigin;
            }

            string currentBannerCode = origin.Banner?.BannerCode;
            string expectedBannerCode = banner.BannerCode;
            if (!string.IsNullOrWhiteSpace(currentBannerCode) &&
                !string.IsNullOrWhiteSpace(expectedBannerCode) &&
                string.Equals(currentBannerCode, expectedBannerCode, StringComparison.Ordinal))
            {
                return origin;
            }

            return new BannerAwareAgentOrigin(origin, banner);
        }

        public bool IsUnderPlayersCommand => _innerOrigin.IsUnderPlayersCommand;

        public bool IsInSameArmyAsPlayer => _innerOrigin.IsInSameArmyAsPlayer;

        public uint FactionColor => _innerOrigin.FactionColor;

        public uint FactionColor2 => _innerOrigin.FactionColor2;

        public IBattleCombatant BattleCombatant => _innerOrigin.BattleCombatant;

        public int UniqueSeed => _innerOrigin.UniqueSeed;

        public int Seed => _innerOrigin.Seed;

        public Banner Banner => _banner ?? _innerOrigin.Banner;

        public BasicCharacterObject Troop => _innerOrigin.Troop;

        public bool HasThrownWeapon => _innerOrigin.HasThrownWeapon;

        public bool HasHeavyArmor => _innerOrigin.HasHeavyArmor;

        public bool HasShield => _innerOrigin.HasShield;

        public bool HasSpear => _innerOrigin.HasSpear;

        public void SetWounded()
        {
            _innerOrigin.SetWounded();
        }

        public void SetKilled()
        {
            _innerOrigin.SetKilled();
        }

        public void SetRouted(bool isOrderRetreat)
        {
            _innerOrigin.SetRouted(isOrderRetreat);
        }

        public void OnAgentRemoved(float agentHealth)
        {
            _innerOrigin.OnAgentRemoved(agentHealth);
        }

        public void OnScoreHit(
            BasicCharacterObject victim,
            BasicCharacterObject formationCaptain,
            int damage,
            bool isFatal,
            bool isTeamKill,
            WeaponComponentData attackerWeapon)
        {
            _innerOrigin.OnScoreHit(
                victim,
                formationCaptain,
                damage,
                isFatal,
                isTeamKill,
                attackerWeapon);
        }

        public void SetBanner(Banner banner)
        {
            if (banner == null)
                return;

            _banner = banner;
            _innerOrigin.SetBanner(banner);
        }

        public TroopTraitsMask GetTraitsMask()
        {
            return _innerOrigin.GetTraitsMask();
        }
    }
}
