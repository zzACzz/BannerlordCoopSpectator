using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.UI
{
    public sealed class CoopBattleAiControlHintVM : ViewModel
    {
        private Vec2 _position;
        private bool _isVisible;
        private string _hintText = "H — Return control";

        [DataSourceProperty]
        public Vec2 Position
        {
            get => _position;
            private set
            {
                if (_position == value)
                    return;

                _position = value;
                OnPropertyChangedWithValue(value, nameof(Position));
            }
        }

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            private set
            {
                if (_isVisible == value)
                    return;

                _isVisible = value;
                OnPropertyChangedWithValue(value, nameof(IsVisible));
            }
        }

        [DataSourceProperty]
        public string HintText
        {
            get => _hintText;
            private set
            {
                if (string.Equals(_hintText, value, StringComparison.Ordinal))
                    return;

                _hintText = value ?? string.Empty;
                OnPropertyChangedWithValue(_hintText, nameof(HintText));
            }
        }

        public void Update(Agent agent, Camera camera)
        {
            if (agent == null || !agent.IsActive() || camera == null)
            {
                IsVisible = false;
                return;
            }

            Vec3 worldPosition;
            try
            {
                worldPosition = agent.GetEyeGlobalPosition();
                worldPosition.z += 0.75f;
            }
            catch
            {
                IsVisible = false;
                return;
            }

            MatrixFrame viewProjection = MatrixFrame.Identity;
            camera.GetViewProjMatrix(ref viewProjection);
            worldPosition.w = 1f;
            Vec3 projected = worldPosition * viewProjection;
            if (projected.w <= 0.0001f)
            {
                IsVisible = false;
                return;
            }

            projected.x /= projected.w;
            projected.y /= projected.w;
            projected *= 0.5f;
            projected.x += 0.5f;
            projected.y = 0.5f - projected.y;

            float x = projected.x * Screen.RealScreenResolutionWidth;
            float y = projected.y * Screen.RealScreenResolutionHeight;
            Position = new Vec2(x, y);
            IsVisible = x >= -180f &&
                        x <= Screen.RealScreenResolutionWidth + 180f &&
                        y >= -60f &&
                        y <= Screen.RealScreenResolutionHeight + 60f;
        }

        public void Hide()
        {
            IsVisible = false;
        }
    }
}
