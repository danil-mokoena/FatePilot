using FatePilot.Core;
using System.Globalization;
using System.Text.Json;
using System.Numerics;

namespace FatePilot;

internal sealed class Combat : IDisposable
{
    private readonly Dalamud.Plugin.IDalamudPluginInterface pi = Services.Plugin;
    private Guid? lease;
    private Recovery? recovery;
    private bool active, ownsMap, releasing, clearMapRequested;
    private readonly Action<string> log;
    private string? cancelled;
    private string RecoveryPath => Path.Combine(pi.ConfigDirectory.FullName, "session-recovery.json");
    public bool NeedsRecovery => File.Exists(RecoveryPath);

    internal sealed record Recovery(bool AiEnabled, List<string> Presets, string TemporaryPreset);

    public Combat(Action<string> log)
    {
        this.log = log;
        pi.GetIpcProvider<int, string, object>(IntegrationIds.AbilitiesLeaseCallback).RegisterAction((reason, detail) =>
        {
            if (!releasing) cancelled = L.T("módulo de habilidades revogou o controle ({0}): {1}", reason, detail);
            lease = null;
        });
    }

    private List<string> Config(params string[] args) => pi.GetIpcSubscriber<IReadOnlyList<string>, bool, List<string>>(IntegrationIds.PositioningConfiguration).InvokeFunc(args, false);
    private bool ReadAi()
    {
        var value = Config("AIConfig", "Enabled");
        if (value.Count != 1 || !bool.TryParse(value[0], out var enabled))
            throw new InvalidOperationException(L.T("módulo de posicionamento não reconheceu AIConfig.Enabled: ") + string.Join("; ", value));
        return enabled;
    }
    private void WriteAi(bool enabled)
    {
        var result = Config("AIConfig", "Enabled", enabled.ToString());
        if (result.Count != 0) throw new InvalidOperationException(L.T("módulo de posicionamento recusou AIConfig.Enabled: ") + string.Join("; ", result));
    }
    private List<string> ActiveList() => pi.GetIpcSubscriber<List<string>>(IntegrationIds.PositioningPresetsGetActiveList).InvokeFunc();
    private void SetList(List<string> names)
    {
        if (!pi.GetIpcSubscriber<List<string>, bool>(IntegrationIds.PositioningPresetsSetActiveList).InvokeFunc(names))
            throw new InvalidOperationException(L.T("módulo de posicionamento recusou a lista de presets."));
    }

    public void Begin()
    {
        if (NeedsRecovery) throw new InvalidOperationException(L.T("Há uma sessão anterior a restaurar. Use o botão de recuperação no painel."));
        cancelled = null;
        var name = "FatePilot movement " + Guid.NewGuid().ToString("N")[..8];
        recovery = new(ReadAi(), ActiveList(), name);
        Directory.CreateDirectory(pi.ConfigDirectory.FullName);
        File.WriteAllText(RecoveryPath, JsonSerializer.Serialize(recovery));
        WriteAi(false);
        SetList([]);

        // Movement-only preset: no rotation, auto-target, heals, cooldowns or follow-slot module.
        var range = Services.Objects.LocalPlayer?.ClassJob.Value.Role is 1 or 2 ? "2.5" : "20";
        var preset = JsonSerializer.Serialize(new
        {
            Name = name,
            Modules = new Dictionary<string, object>
            {
                [IntegrationIds.PositioningAutorotationMiscAIStayCloseToTarget] = new[] { new { Track = "range", Option = range } },
                [IntegrationIds.PositioningAutorotationMiscAINormalMovement] = new[] { new { Track = "Destination", Option = "Pathfind" } },
            },
        });
        if (!pi.GetIpcSubscriber<string, bool, bool>(IntegrationIds.PositioningPresetsCreate).InvokeFunc(preset, false))
            throw new InvalidOperationException(L.T("módulo de posicionamento não conseguiu criar o preset de posicionamento."));
        var actual = pi.GetIpcSubscriber<string, string?>(IntegrationIds.PositioningPresetsGet).InvokeFunc(name);
        using (var doc = JsonDocument.Parse(actual ?? "{}"))
        {
            // The positioning service may silently discard unknown modules/tracks. Verify its serialized round-trip.
            var modules = doc.RootElement.GetProperty("Modules");
            foreach (var required in new[] { (IntegrationIds.PositioningAutorotationMiscAIStayCloseToTarget, "range", range), (IntegrationIds.PositioningAutorotationMiscAINormalMovement, "Destination", "Pathfind") })
                if (!modules.TryGetProperty(required.Item1, out var settings) || !settings.EnumerateArray().Any(s => s.GetProperty("Track").GetString() == required.Item2 && s.GetProperty("Option").GetString() == required.Item3))
                    throw new InvalidOperationException(L.T("Esta versão do módulo de posicionamento não aceita o módulo/opção: ") + required.Item1);
        }
        lease = pi.GetIpcSubscriber<string, string, string?, Guid?>(IntegrationIds.AbilitiesRegisterForLeaseWithCallback)
            .InvokeFunc("FatePilot", "FatePilot", "FatePilot") ?? throw new InvalidOperationException(L.T("módulo de habilidades não concedeu controle. Verifique IPC e outras automações."));
        Check(pi.GetIpcSubscriber<Guid, object>(IntegrationIds.AbilitiesSetCurrentJobAutoRotationReady).InvokeFunc(lease.Value), L.T("preparar classe"));
        SetAbilities(false);
        // Use our explicitly chosen FATE target, including the initial pull out of combat.
        foreach (var (option, value) in new (int, object)[] { (0, false), (1, 0), (3, true), (13, false), (19, true) })
            Check(pi.GetIpcSubscriber<Guid, object, object, object>(IntegrationIds.AbilitiesSetAutoRotationConfigState).InvokeFunc(lease.Value, option, value), L.T("opção {0}", option));
        log(L.T("módulo de habilidades: controle temporário concedido. módulo de posicionamento: preset de movimento validado."));
    }

    public void HealthCheck()
    {
        if (cancelled != null) throw new InvalidOperationException(cancelled);
        if (recovery == null) return;
        var current = ActiveList();
        if (active && current.Count == 0 && !Services.Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat]
            && Config("AutorotationConfig", "ClearPresetOnCombatEnd").SequenceEqual(new[] { "True" }))
        {
            SetAbilities(false);
            active = false;
            ClearMap();
        }
        var expected = active ? new[] { recovery.TemporaryPreset } : [];
        if (ReadAi() || !current.SequenceEqual(expected))
            throw new InvalidOperationException(L.T("O controle do módulo de posicionamento mudou fora do FatePilot. Sessão interrompida para evitar conflito."));
    }

    private static void Check(object result, string operation)
    {
        var code = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        if (code is not (0 or 1 or 13)) throw new InvalidOperationException(L.T("módulo de habilidades recusou {0}, código {1}.", operation, code));
    }
    private void SetAbilities(bool enabled)
    {
        if (lease == null) throw new InvalidOperationException(cancelled ?? L.T("Controle do módulo de habilidades indisponível."));
        Check(pi.GetIpcSubscriber<Guid, bool, object>(IntegrationIds.AbilitiesSetAutoRotationState).InvokeFunc(lease.Value, enabled), L.T("rotação"));
    }

    public void SetEnabled(bool enabled)
    {
        if (active == enabled) return;
        if (recovery == null) throw new InvalidOperationException(L.T("Sessão de combate não inicializada."));
        if (enabled)
        {
            // Build terrain once per encounter only if there is no pre-existing temporary map.
            if (!pi.GetIpcSubscriber<bool>(IntegrationIds.PositioningObstacleMapHasTempMap).InvokeFunc() && !ownsMap)
            {
                var position = Services.Objects.LocalPlayer?.Position ?? Vector3.Zero;
                ownsMap = pi.GetIpcSubscriber<Vector3, float, bool, bool>(IntegrationIds.PositioningObstacleMapGenerate).InvokeFunc(position, 100, false);
            }
            SetList([recovery.TemporaryPreset]);
            SetAbilities(true);
        }
        else
        {
            SetAbilities(false);
            SetList([]);
            ClearMap();
        }
        active = enabled;
    }

    private void ClearMap()
    {
        if (!ownsMap) return;
        clearMapRequested = true;
        CleanupMap();
    }

    public void CleanupMap()
    {
        if (!ownsMap || !clearMapRequested) return;
        var status = (TaskStatus)Convert.ToInt32(pi.GetIpcSubscriber<object>(IntegrationIds.PositioningObstacleMapGetGenerationStatus).InvokeFunc(), CultureInfo.InvariantCulture);
        if (status is not (TaskStatus.RanToCompletion or TaskStatus.Canceled or TaskStatus.Faulted)) return;
        pi.GetIpcSubscriber<bool>(IntegrationIds.PositioningObstacleMapClearTempMap).InvokeFunc();
        ownsMap = false;
        clearMapRequested = false;
    }

    public void End()
    {
        List<string> errors = [];
        void Attempt(Action action) { try { action(); } catch (Exception ex) { errors.Add(ex.Message); } }
        if (lease is { } id)
        {
            releasing = true;
            Attempt(() => SetAbilities(false));
            Attempt(() => { pi.GetIpcSubscriber<Guid, object>(IntegrationIds.AbilitiesReleaseControl).InvokeAction(id); lease = null; });
            releasing = false;
        }
        Attempt(ClearMap);
        if (recovery is { } old)
        {
            Attempt(() => Restore(old, false));
            if (errors.Count == 0) { recovery = null; File.Delete(RecoveryPath); }
        }
        active = false;
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" | ", errors));
    }

    private void Restore(Recovery old, bool force)
    {
        var current = ActiveList();
        if (force || current.Count == 0 || current.All(x => x == old.TemporaryPreset))
        {
            if (old.Presets.SequenceEqual(new[] { "" }))
                pi.GetIpcSubscriber<bool>(IntegrationIds.PositioningPresetsSetForceDisabled).InvokeFunc();
            else SetList(old.Presets.Where(x => x != IntegrationIds.PositioningFollowPreset).ToList());
            WriteAi(old.AiEnabled);
        }
        else log(L.T("módulo de posicionamento foi alterado por outra origem; os presets atuais foram preservados."));
        pi.GetIpcSubscriber<string, bool>(IntegrationIds.PositioningPresetsDelete).InvokeFunc(old.TemporaryPreset);
    }

    public void Recover()
    {
        if (!NeedsRecovery) return;
        if (new FileInfo(RecoveryPath).Length > 65536) throw new InvalidOperationException(L.T("Registro de recuperação inválido."));
        var old = JsonSerializer.Deserialize<Recovery>(File.ReadAllText(RecoveryPath)) ?? throw new InvalidOperationException(L.T("Registro de recuperação inválido."));
        if (old.Presets == null || old.Presets.Count > 100 || old.Presets.Any(x => x == null || x.Length > 512)
            || old.TemporaryPreset == null || !System.Text.RegularExpressions.Regex.IsMatch(old.TemporaryPreset, @"\AFatePilot movement [a-f0-9]{8}\z"))
            throw new InvalidOperationException(L.T("Registro de recuperação inválido."));
        Restore(old, true);
        recovery = null;
        File.Delete(RecoveryPath);
        log(L.T("Configuração do módulo de posicionamento restaurada da sessão anterior."));
    }

    public void Dispose()
    {
        try { End(); }
        finally
        {
            pi.GetIpcProvider<int, string, object>(IntegrationIds.AbilitiesLeaseCallback).UnregisterAction();
            if (ownsMap)
                _ = Services.Framework.RunOnTick(() => { try { CleanupMap(); } catch (Exception) { Services.Log.Warning("FatePilot map cleanup failed"); } }, delayTicks: 2);
        }
    }
}
