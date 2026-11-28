// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared.Salvage.Expeditions;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client.Salvage.UI;

/// <summary>
/// Displays server-owned disk preparation; never issues disks or completes the operation locally.
/// </summary>
public sealed class SalvageDiskPreparationControl : BoxContainer
{
    [Dependency] private readonly IGameTiming _timing = default!;
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new() { MinValue = 0, MaxValue = 1, SetHeight = 12 };
    private readonly Scanner _scanner = new() { SetHeight = 260, HorizontalExpand = true };
    private SalvageDiskPreparation _stage;
    private TimeSpan _readyAt;

    public SalvageDiskPreparationControl()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        Visible = false;
        _status.Margin = new Thickness(0, 4, 0, 8);
        _status.FontColorOverride = Color.FromHex("#A6F3EB");
        AddChild(_status);
        AddChild(_scanner);
        AddChild(_progress);
        _progress.ForegroundStyleBoxOverride = new StyleBoxFlat(Color.FromHex("#5ED6BF"));
    }

    public void UpdateState(SalvageExpeditionConsoleState state)
    {
        _stage = state.DiskPreparation;
        _readyAt = state.DiskReadyAt;
        Visible = _stage != SalvageDiskPreparation.Idle;
        var missionIndex = _stage == SalvageDiskPreparation.Writing
            ? state.PreparingMission : state.ActiveMission;
        foreach (var mission in state.Missions)
        {
            if (mission.Index == missionIndex)
                _scanner.Seed = mission.Seed;
        }
        Refresh();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_stage == SalvageDiskPreparation.Writing)
            Refresh();
    }

    private void Refresh()
    {
        var writing = _stage == SalvageDiskPreparation.Writing;
        var progress = writing
            ? Math.Clamp(1f - (float) ((_readyAt - _timing.CurTime) /
                SalvageExpeditionConsoleState.DiskPreparationDuration), 0f, 1f)
            : 1f;

        _scanner.Visible = writing || _stage == SalvageDiskPreparation.Ready;
        _progress.Visible = writing;
        _scanner.Progress = progress;
        _progress.Value = progress;
        var key = _stage switch
        {
            SalvageDiskPreparation.Ready => "salvage-disk-ready",
            SalvageDiskPreparation.Cancelled => "salvage-disk-cancelled",
            _ when progress < 0.32f => "salvage-disk-searching",
            _ when progress < 0.48f => "salvage-disk-approaching",
            _ when progress < 0.688f => "salvage-disk-scanning",
            _ when progress < 0.818f => "salvage-disk-locked",
            _ when progress < 1f => "salvage-disk-writing",
            _ => "salvage-disk-waiting",
        };
        _status.Text = Loc.GetString(key);
    }

    private sealed class Scanner : Control
    {
        public float Progress;
        public int Seed;

        private static readonly Color Background = Color.FromHex("#071018");
        private static readonly Color Grid = Color.FromHex("#132A39");
        private static readonly Color Ocean = Color.FromHex("#102D43");
        private static readonly Color Cyan = Color.FromHex("#62E8E0");
        private static readonly Color Land = Color.FromHex("#53BDA7");
        private static readonly Color Gold = Color.FromHex("#F4C879");

        // Continuous velocity and acceleration at each transition boundary.
        private static float Ease(float value)
        {
            var t = Math.Clamp(value, 0f, 1f);
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        private static Vector2 OrbitPoint(Vector2 center, float radius, float angle)
        {
            // Tilt the orbital ellipse in screen space.
            var x = MathF.Cos(angle) * radius * 1.55f;
            var y = MathF.Sin(angle) * radius * 0.38f;
            return center + new Vector2(x * 0.96f - y * 0.28f, x * 0.28f + y * 0.96f);
        }

        private static Vector3 Sphere(float latitude, float longitude)
        {
            var c = MathF.Cos(latitude);
            return new Vector3(c * MathF.Sin(longitude), MathF.Sin(latitude), c * MathF.Cos(longitude));
        }

        private static Vector2 Project(Vector3 point, Vector2 center, float radius)
        {
            return center + new Vector2(point.X, -point.Y) * radius;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            var width = (float) PixelSize.X;
            var height = (float) PixelSize.Y;
            if (width <= 0 || height <= 0)
                return;

            var scale = UIScale;
            var viewportCenter = new Vector2(width * 0.5f, height * 0.5f);
            var foundPlanet = new Vector2(width * 0.68f, height * 0.38f);
            var zoom = Math.Clamp((Progress - 0.32f) / 0.16f, 0, 1);
            zoom = Ease(zoom);
            var center = Vector2.Lerp(foundPlanet, viewportCenter, zoom);
            var fullRadius = MathF.Min(height * 0.30f, width * 0.22f);
            var radius = 4f * scale + (fullRadius - 4f * scale) * zoom;
            var time = Progress * (float) SalvageExpeditionConsoleState.DiskPreparationDuration.TotalSeconds;
            var planetProgress = Math.Clamp((Progress - 0.48f) / 0.52f, 0, 1);
            var rotation = Ease(planetProgress / 0.4f) + zoom * 0.4f;
            var seedPhase = (Seed & 1023) * 0.013f;
            handle.DrawRect(new UIBox2(Vector2.Zero, new Vector2(width, height)), Background);

            // Stable star field: opening the console again does not randomize the display.
            for (var i = 0; i < 55; i++)
            {
                var x = ((i * 137 + (Seed & 255)) % 997) / 997f * width;
                var y = ((i * 89 + 37) % 241) / 241f * height;
                var star = center + (new Vector2(x, y) - foundPlanet) * (1 + zoom * 0.7f);
                if (star.X < 0 || star.X > width || star.Y < 0 || star.Y > height)
                    continue;
                if (Progress >= 0.32f && Progress < 0.48f)
                    handle.DrawLine(star, Vector2.Lerp(star, viewportCenter, 0.08f * MathF.Sin(zoom * MathF.PI)), Grid);
                handle.DrawCircle(star, (i % 7 == 0 ? 1.2f : 0.65f) * scale,
                    i % 7 == 0 ? Color.FromHex("#617C91") : Grid);
            }

            for (var x = 0f; x < width; x += 40f * scale)
                handle.DrawLine(new Vector2(x, 0), new Vector2(x, height), Grid);
            for (var y = 0f; y < height; y += 40f * scale)
                handle.DrawLine(new Vector2(0, y), new Vector2(width, y), Grid);

            if (Progress < 0.32f)
            {
                DrawSearch(handle, width, height, scale, time, foundPlanet);
                return;
            }

            // Atmospheric halo and outer telemetry ring.
            for (var i = 5; i >= 1; i--)
                handle.DrawCircle(center, radius + i * 2f * scale,
                    new Color(0.15f, 0.65f, 0.78f, 0.12f / i), false);
            for (var i = 0; i < 64; i++)
            {
                var angle = i * MathF.Tau / 64;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var outer = radius * 1.35f;
                handle.DrawLine(center + direction * outer,
                    center + direction * (outer + (i % 8 == 0 ? 6 : 2) * scale), Grid);
            }

            // Back half of the orbit is occluded by the globe.
            DrawOrbit(handle, center, radius, time, false);
            handle.DrawCircle(center, radius, Ocean);

            // Procedural holographic continents on a rotating sphere. This is illustrative,
            // not a terrain map: no undiscovered gameplay information is exposed.
            for (var latStep = -7; latStep <= 7; latStep++)
            {
                var lat = latStep * MathF.PI / 16;
                for (var lonStep = 0; lonStep < 48; lonStep++)
                {
                    var lon = lonStep * MathF.Tau / 48;
                    var point = Sphere(lat, lon + rotation);
                    if (point.Z <= 0)
                        continue;
                    var terrain = MathF.Sin(lon * 3 + seedPhase + MathF.Sin(lat * 4)) +
                        0.55f * MathF.Cos(lon * 5 - lat * 3 + seedPhase);
                    var light = 0.35f + 0.65f * Math.Clamp(point.Z * 0.7f - point.X * 0.5f + point.Y * 0.2f, 0, 1);
                    var color = terrain > 0.25f ? Land : Color.FromHex("#27617F");
                    handle.DrawCircle(Project(point, center, radius),
                        (terrain > 0.25f ? 2.1f : 0.8f) * scale,
                        new Color(color.R * light, color.G * light, color.B * light, 1f));
                }
            }

            // Curved latitude / longitude lines reinforce the sphere's depth.
            for (var latitude = -2; latitude <= 2; latitude++)
            {
                for (var i = 0; i < 40; i++)
                {
                    var a = Sphere(latitude * MathF.PI / 6, -MathF.PI / 2 + i * MathF.PI / 40);
                    var b = Sphere(latitude * MathF.PI / 6, -MathF.PI / 2 + (i + 1) * MathF.PI / 40);
                    handle.DrawLine(Project(a, center, radius), Project(b, center, radius), Grid);
                }
            }
            for (var meridian = 0; meridian < 10; meridian++)
            {
                var lon = meridian * MathF.Tau / 10 + rotation;
                for (var i = 0; i < 24; i++)
                {
                    var a = Sphere(-MathF.PI / 2 + i * MathF.PI / 24, lon);
                    var b = Sphere(-MathF.PI / 2 + (i + 1) * MathF.PI / 24, lon);
                    if (a.Z > 0 && b.Z > 0)
                        handle.DrawLine(Project(a, center, radius), Project(b, center, radius), Grid);
                }
            }
            handle.DrawCircle(center, radius, Cyan, false);
            DrawOrbit(handle, center, radius, time, true);

            if (Progress < 0.48f)
            {
                // The planet is still being brought into the scanning view.
            }
            else if (planetProgress < 0.4f)
            {
                // A scan sweeps across the globe, with a soft trailing band.
                var y = center.Y - radius + 2 * radius * Ease(planetProgress / 0.4f);
                for (var i = 8; i >= 0; i--)
                {
                    var scanY = y - i * 2f * scale;
                    var dy = (scanY - center.Y) / radius;
                    if (MathF.Abs(dy) >= 1)
                        continue;
                    var halfWidth = radius * MathF.Sqrt(1 - dy * dy);
                    handle.DrawLine(new Vector2(center.X - halfWidth, scanY),
                        new Vector2(center.X + halfWidth, scanY),
                        new Color(0.38f, 0.91f, 0.88f, (1f - i / 9f) * 0.8f));
                }
            }
            else
            {
                var target = Project(Sphere(0.28f, 0.38f), center, radius);
                var locked = Ease((planetProgress - 0.4f) / 0.25f);
                var distance = (30 - 22 * locked) * scale;
                var length = 6f * scale;
                var color = planetProgress >= 0.65f ? Gold : Cyan;
                for (var sx = -1; sx <= 1; sx += 2)
                for (var sy = -1; sy <= 1; sy += 2)
                {
                    var corner = target + new Vector2(sx * distance, sy * distance);
                    handle.DrawLine(corner, corner - new Vector2(sx * length, 0), color);
                    handle.DrawLine(corner, corner - new Vector2(0, sy * length), color);
                }
                handle.DrawCircle(target, 2f * scale, color);
                if (planetProgress < 1)
                {
                    var pulse = (time * 2) % 1f;
                    handle.DrawCircle(target, (10 + pulse * 22) * scale,
                        new Color(color.R, color.G, color.B, (1 - pulse) * 0.6f), false);
                }
                var end = new Vector2(MathF.Min(width - 16 * scale, center.X + radius * 2.1f), center.Y - radius * 0.6f);
                var elbow = new Vector2(center.X + radius * 1.4f, end.Y);
                handle.DrawLine(target + new Vector2(10, -10) * scale, elbow, color);
                handle.DrawLine(elbow, end, color);
            }

            // Data packets light up sequentially during disk writing.
            var writing = Math.Clamp((planetProgress - 0.65f) / 0.35f, 0, 1);
            if (planetProgress >= 0.65f && Progress < 1f)
            {
                for (var packet = 0; packet < 6; packet++)
                {
                    var phase = (time * 0.8f + packet / 6f) % 1f;
                    var from = new Vector2(center.X - radius * 2.2f, center.Y + radius * 0.8f);
                    var to = center + new Vector2(-radius * 0.5f, radius * 0.3f);
                    var position = Vector2.Lerp(from, to, phase);
                    var alpha = MathF.Sin(phase * MathF.PI) * MathF.Sin(writing * MathF.PI);
                    handle.DrawCircle(position, 2f * scale, new Color(0.96f, 0.78f, 0.47f, alpha));
                }
            }
            var barWidth = MathF.Min(240f * scale, width * 0.6f);
            for (var i = 0; i < 24; i++)
            {
                var x = center.X - barWidth / 2 + i * barWidth / 24;
                handle.DrawRect(new UIBox2(new Vector2(x, height - 12 * scale),
                    new Vector2(x + barWidth / 24 - 2 * scale, height - 8 * scale)),
                    (i + 1) / 24f <= writing ? Gold : Grid);
            }
        }

        private void DrawSearch(DrawingHandleScreen handle, float width, float height, float scale, float time, Vector2 foundPlanet)
        {
            var origin = new Vector2(width * 0.5f, height * 0.5f);
            var search = Progress / 0.32f;
            var radarRadius = MathF.Min(width * 0.42f, height * 0.43f);
            for (var ring = 1; ring <= 3; ring++)
                handle.DrawCircle(origin, radarRadius * ring / 3, Grid, false);

            // A narrow sweep with a fading tail, instead of a full-screen flash.
            for (var trail = 20; trail >= 0; trail--)
            {
                var angle = time * 3.4f - trail * 0.025f;
                var end = origin + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radarRadius;
                handle.DrawLine(origin, end, new Color(0.38f, 0.91f, 0.88f, (1 - trail / 21f) * 0.45f));
            }

            for (var wave = 0; wave < 2; wave++)
            {
                var phase = (time * 0.65f + wave * 0.5f) % 1f;
                var opacity = MathF.Sin(phase * MathF.PI) * 0.22f;
                handle.DrawCircle(origin, radarRadius * phase,
                    new Color(0.38f, 0.91f, 0.88f, opacity), false);
            }

            var scanX = width * (0.5f - 0.5f * MathF.Cos(search * MathF.PI * 2));
            handle.DrawLine(new Vector2(scanX, 8 * scale), new Vector2(scanX, height - 8 * scale), Cyan);

            // Six candidate signals. The final one becomes the zoomed planet.
            for (var i = 0; i < 6; i++)
            {
                var candidate = Candidate(i, width, height, foundPlanet);
                var acquired = i == 5 && search >= 0.75f;
                var color = acquired ? Gold : Color.FromHex("#456B82");
                handle.DrawCircle(candidate, (acquired ? 4 : 2.5f) * scale, color);
                handle.DrawCircle(candidate, 8 * scale, color, false);
                if (i < 5 && search > (i + 1) * 0.15f)
                {
                    var d = new Vector2(4, 4) * scale;
                    handle.DrawLine(candidate - d, candidate + d, Grid);
                    handle.DrawLine(candidate + new Vector2(-d.X, d.Y), candidate + new Vector2(d.X, -d.Y), Grid);
                }
            }

            var step = Math.Clamp(search / 0.15f, 0f, 5f);
            var index = (int) step;
            var from = Candidate(index, width, height, foundPlanet);
            var to = Candidate(Math.Min(index + 1, 5), width, height, foundPlanet);
            var target = Vector2.Lerp(from, to, Ease(step - index));
            var locked = search >= 0.75f;
            var reticle = locked ? Gold : Cyan;
            var box = (locked ? 15f : 22f) * scale;
            for (var sx = -1; sx <= 1; sx += 2)
            for (var sy = -1; sy <= 1; sy += 2)
            {
                var corner = target + new Vector2(sx, sy) * box;
                handle.DrawLine(corner, corner - new Vector2(sx * 7 * scale, 0), reticle);
                handle.DrawLine(corner, corner - new Vector2(0, sy * 7 * scale), reticle);
            }
            if (locked)
            {
                var pulse = (search - 0.75f) / 0.25f;
                handle.DrawCircle(foundPlanet, (12 + pulse * 30) * scale,
                    new Color(0.96f, 0.78f, 0.47f, 1 - pulse), false);
            }

            // Spectrum bars indicate the receiver is searching, not waiting idly.
            for (var i = 0; i < 32; i++)
            {
                var x = 14 * scale + i * 5 * scale;
                if (x + 3 * scale >= width)
                    break;
                var amplitude = (3 + 13 * MathF.Abs(MathF.Sin(time * 4 + i * 0.71f))) * scale;
                handle.DrawRect(new UIBox2(new Vector2(x, height - 12 * scale - amplitude),
                    new Vector2(x + 3 * scale, height - 12 * scale)), locked ? Gold : Cyan);
            }
        }

        private Vector2 Candidate(int index, float width, float height, Vector2 foundPlanet)
        {
            if (index >= 5)
                return foundPlanet;
            var phase = (Seed & 255) / 255f;
            return new Vector2(width * (0.16f + ((index * 0.173f + phase * 0.2f) % 0.65f)),
                height * (0.23f + ((index * 0.213f + phase * 0.1f) % 0.5f)));
        }

        private static void DrawOrbit(DrawingHandleScreen handle, Vector2 center, float radius, float time, bool front)
        {
            for (var i = 0; i < 80; i++)
            {
                var angle = i * MathF.Tau / 80;
                if ((MathF.Sin(angle) >= 0) != front || i % 4 == 0)
                    continue;
                handle.DrawLine(OrbitPoint(center, radius, angle),
                    OrbitPoint(center, radius, angle + MathF.Tau / 80), front ? Cyan : Grid);
            }
            var satelliteAngle = time * 1.8f;
            if ((MathF.Sin(satelliteAngle) >= 0) == front)
                handle.DrawCircle(OrbitPoint(center, radius, satelliteAngle), radius * 0.035f, Gold);
        }
    }
}
