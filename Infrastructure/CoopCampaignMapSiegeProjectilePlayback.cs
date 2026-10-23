using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    // Buffered translation between admitted samples, never ballistic prediction.
    // Orientation and visibility remain authoritative endpoints. Short flights
    // absent from both samples still require a separate trajectory/event protocol.
    internal sealed class CoopCampaignMapSiegeProjectilePlayback
    {
        private CoopCampaignMapSiegeOverlay _source;
        private CoopCampaignMapSiegeTime _time;
        private CoopCampaignMapSiegeProjectiles _current;
        private Dictionary<long, CoopCampaignMapSiegeProjectile> _previous;
        private CoopCampaignMapSiegeMachines _currentMachines;
        private Dictionary<long, CoopCampaignMapSiegeMachine> _previousMachines;
        internal CoopCampaignMapSiegeMachines Machines { get; private set; }
        private double _received, _duration, _previousHours;

        internal CoopCampaignMapSiegeProjectiles Sample(CoopCampaignMapSiegeOverlay source, double localSeconds)
        {
            if (double.IsNaN(localSeconds) || double.IsInfinity(localSeconds) || localSeconds < 0 || source == null)
            { Clear(); return null; }
            if (!ReferenceEquals(source, _source))
            {
                _source = source;
                if (source.Projectiles == null || !source.Projectiles.TrySnapshot(out var next) ||
                    (source.Time != null && !source.Time.IsValid()))
                { Clear(); return null; }
                var nextTime = source.Time;
                CoopCampaignMapSiegeMachines nextMachines = null;
                if (source.Machines != null && !source.Machines.TrySnapshot(out nextMachines))
                { Clear(); return null; }
                // Same epoch stale/out-of-order data must not rewind playback.
                if (_time != null && nextTime != null && _time.Epoch == nextTime.Epoch &&
                    nextTime.SampleSeconds <= _time.SampleSeconds) return Render(localSeconds);
                double interval = _time == null || nextTime == null ? 0 : nextTime.SampleSeconds - _time.SampleSeconds;
                bool continuous = _current != null && _time != null && nextTime != null &&
                    _time.Epoch == nextTime.Epoch && nextTime.CampaignHours > _time.CampaignHours &&
                    nextTime.TimeScale > 0 && nextTime.TimeScale == _time.TimeScale &&
                    interval > 0 && interval <= 0.75 && localSeconds >= _received;
                _previous = null;
                _previousMachines = null;
                if (continuous)
                {
                    _previousHours = _time.CampaignHours;
                    _previous = new Dictionary<long, CoopCampaignMapSiegeProjectile>();
                    foreach (var item in _current.Items) _previous.Add(item.ShotId, item);
                    if (_currentMachines != null)
                    {
                        _previousMachines = new Dictionary<long, CoopCampaignMapSiegeMachine>();
                        foreach (var item in _currentMachines.Items) _previousMachines.Add(item.Key, item);
                    }
                }
                _current = next; _time = nextTime?.Clone();
                _currentMachines = nextMachines;
                _received = localSeconds; _duration = continuous ? interval : 0;
            }
            return Render(localSeconds);
        }

        private CoopCampaignMapSiegeProjectiles Render(double localSeconds)
        {
            Machines = _currentMachines;
            if (_current == null || _previous == null || _duration <= 0) return RenderFlights(_current, _time?.CampaignHours);
            double elapsed = localSeconds - _received;
            if (elapsed < 0 || elapsed >= _duration) { _previous = null; return RenderFlights(_current, _time?.CampaignHours); }
            float t = (float)(elapsed / _duration);
            if (_currentMachines != null && _previousMachines != null)
            {
                _currentMachines.TrySnapshot(out var machines);
                foreach (var item in machines.Items)
                {
                    if (!_previousMachines.TryGetValue(item.Key, out var prior) || item.InstanceId <= 0 ||
                        item.InstanceId != prior.InstanceId || item.Prefab != prior.Prefab ||
                        item.AnimationCycleHours != prior.AnimationCycleHours ||
                        item.Animation == null || prior.Animation == null ||
                        item.Animation.ClipName != prior.Animation.ClipName ||
                        item.Animation.Progress < prior.Animation.Progress) continue;
                    item.Animation.Progress = prior.Animation.Progress +
                        (item.Animation.Progress - prior.Animation.Progress) * t;
                }
                Machines = machines;
            }
            _current.TrySnapshot(out var result);
            foreach (var item in result.Items)
            {
                if (item.Flight != null || !_previous.TryGetValue(item.ShotId, out var prior) || prior.Side != item.Side ||
                    prior.Prefab != item.Prefab || !prior.FlightChildVisible || !item.FlightChildVisible) continue;
                for (int i = 9; i < 12; i++) item.Frame[i] = prior.Frame[i] + (item.Frame[i] - prior.Frame[i]) * t;
            }
            return RenderFlights(result, _previousHours + (_time.CampaignHours - _previousHours) * t);
        }

        private static CoopCampaignMapSiegeProjectiles RenderFlights(CoopCampaignMapSiegeProjectiles source, double? hours)
        {
            if (source == null) return null;
            bool any = false;
            foreach (var item in source.Items) if (item.Flight != null) { any = true; break; }
            if (!any) return source;
            var result = new CoopCampaignMapSiegeProjectiles();
            foreach (var item in source.Items)
            {
                if (item.Flight == null) { result.Items.Add(item.Clone()); continue; }
                if (!hours.HasValue) continue; // no clock, no historical shot replay
                if (item.Flight.TryPosition(hours.Value, out var position))
                {
                    var rendered = item.Clone();
                    if (item.Flight.PresentationScale != null)
                    {
                        var referenceUp = new[] { item.Frame[6], item.Frame[7], item.Frame[8] };
                        if (!item.Flight.TryFrame(hours.Value, referenceUp, item.Flight.PresentationScale, out var frame)) continue;
                        rendered.Frame = frame;
                    }
                    else for (int i = 0; i < 3; i++) rendered.Frame[9 + i] = position[i];
                    rendered.FlightChildVisible = true;
                    result.Items.Add(rendered);
                }
                else if (!item.TrajectoryOnly && !item.FlightChildVisible && hours.Value <= item.Flight.StartHours)
                    result.Items.Add(item.Clone()); // actual native attachment, not a retained hidden root
            }
            return result;
        }

        internal void Clear()
        {
            _source = null; _time = null; _current = null; _previous = null;
            _currentMachines = null; _previousMachines = null; Machines = null;
            _received = 0; _duration = 0; _previousHours = 0;
        }
    }
}
