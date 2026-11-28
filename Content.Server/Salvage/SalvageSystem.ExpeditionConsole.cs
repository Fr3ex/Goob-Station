// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Procedural;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Dataset;
using Robust.Shared.Prototypes;

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    public static readonly EntProtoId CoordinatesDisk = "CoordinatesDisk";
    public static readonly ProtoId<LocalizedDatasetPrototype> PlanetNames = "NamesBorer";

    private void OnSalvageClaimMessage(EntityUid uid, SalvageExpeditionConsoleComponent component, ClaimSalvageMessage args)
    {
        var station = _station.GetOwningStation(uid);

        if (!TryComp<SalvageExpeditionDataComponent>(station, out var data) || data.Claimed || data.Cooldown ||
            data.DiskPreparation == SalvageDiskPreparation.Writing)
        {
            UpdateConsole((uid, component));
            return;
        }

        if (!data.Missions.TryGetValue(args.Index, out var missionparams))
        {
            UpdateConsole((uid, component));
            return;
        }

        data.PreparingMission = missionparams;
        data.PrintingConsole = uid;
        data.DiskPreparation = SalvageDiskPreparation.Writing;
        data.DiskReadyAt = _timing.CurTime + SalvageExpeditionConsoleState.DiskPreparationDuration;
        data.ScanStream = _audio.Stop(data.ScanStream);
        data.ScanStream = _audio.PlayPvs(component.ScanSound, uid,
            component.ScanSound.Params.WithLoop(false).WithVolume(0f))?.Entity;
        UpdateConsoles((station.Value, data));
    }

    private void UpdateDiskPreparation(EntityUid station, SalvageExpeditionDataComponent data)
    {
        if (data.DiskPreparation != SalvageDiskPreparation.Writing)
            return;

        // Closing the UI is harmless; losing the issuing console cancels the reservation.
        if (data.PrintingConsole is not { } uid || Deleted(uid) ||
            !TryComp<SalvageExpeditionConsoleComponent>(uid, out var component) ||
            _station.GetOwningStation(uid) != station || data.PreparingMission is not { } missionparams)
        {
            data.ScanStream = _audio.Stop(data.ScanStream);
            data.DiskPreparation = SalvageDiskPreparation.Cancelled;
            data.PreparingMission = null;
            data.PrintingConsole = null;
            UpdateConsoles((station, data));
            return;
        }

        if (_timing.CurTime < data.DiskReadyAt)
            return;

        data.ScanStream = _audio.Stop(data.ScanStream);
        var cdUid = Spawn(CoordinatesDisk, Transform(uid).Coordinates);
        SpawnMission(missionparams, station, cdUid);

        data.ActiveMission = missionparams.Index;
        var mission = GetMission(_prototypeManager.Index<SalvageDifficultyPrototype>(missionparams.Difficulty), missionparams.Seed);
        data.NextOffer = _timing.CurTime + mission.Duration + TimeSpan.FromSeconds(1);

        _labelSystem.Label(cdUid, GetFTLName(_prototypeManager.Index(PlanetNames), missionparams.Seed));
        _audio.PlayPvs(component.PrintSound, uid);

        data.DiskPreparation = SalvageDiskPreparation.Ready;
        data.PreparingMission = null;
        data.PrintingConsole = null;
        UpdateConsoles((station, data));
    }

    private void OnDiskPreparationShutdown(Entity<SalvageExpeditionDataComponent> station, ref ComponentShutdown args)
    {
        station.Comp.ScanStream = _audio.Stop(station.Comp.ScanStream);
    }

    private void OnSalvageConsoleInit(Entity<SalvageExpeditionConsoleComponent> console, ref ComponentInit args)
    {
        UpdateConsole(console);
    }

    private void OnSalvageConsoleParent(Entity<SalvageExpeditionConsoleComponent> console, ref EntParentChangedMessage args)
    {
        UpdateConsole(console);
    }

    private void UpdateConsoles(Entity<SalvageExpeditionDataComponent> component)
    {
        var state = GetState(component);

        var query = AllEntityQuery<SalvageExpeditionConsoleComponent, UserInterfaceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var uiComp, out var xform))
        {
            var station = _station.GetOwningStation(uid, xform);

            if (station != component.Owner)
                continue;

            _ui.SetUiState((uid, uiComp), SalvageConsoleUiKey.Expedition, state);
        }
    }

    private void UpdateConsole(Entity<SalvageExpeditionConsoleComponent> component)
    {
        var station = _station.GetOwningStation(component);
        SalvageExpeditionConsoleState state;

        if (TryComp<SalvageExpeditionDataComponent>(station, out var dataComponent))
        {
            state = GetState(dataComponent);
        }
        else
        {
            state = new SalvageExpeditionConsoleState(TimeSpan.Zero, false, true, 0, new List<SalvageMissionParams>());
        }

        _ui.SetUiState(component.Owner, SalvageConsoleUiKey.Expedition, state);
    }
}