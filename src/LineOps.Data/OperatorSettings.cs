using LineOps.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Data;

/// <summary>
/// The settings an operator owns, read and written where every host can see them.
///
/// <para>
/// Configuration is per process and per machine; these travel with the data. That matters for a
/// choice like automatic line polling, which the web host's scheduler and the worker's both act
/// on: a switch held in one process's configuration would leave the other spending, or not, on
/// its own.
/// </para>
/// </summary>
public static class OperatorSettings
{
    public static Task<string?> GetSettingAsync(this LineOpsDbContext db, string key, CancellationToken ct = default)
        => db.AppSettings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

    /// <summary>Stores a setting, or removes it when <paramref name="value"/> is null so configuration decides again.</summary>
    public static async Task SetSettingAsync(this LineOpsDbContext db, string key, string? value, CancellationToken ct = default)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);

        if (value is null)
        {
            if (row is not null)
                db.AppSettings.Remove(row);
        }
        else if (row is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Whether lines are scanned without being asked: the operator's stored choice, or
    /// <paramref name="configured"/> where none has been made.
    /// </summary>
    public static async Task<bool> LinesRunUnattendedAsync(this LineOpsDbContext db, bool configured, CancellationToken ct = default)
        => await db.GetSettingAsync(AppSetting.LinePolling, ct) switch
        {
            AppSetting.LinePollingScheduled => true,
            AppSetting.LinePollingManual => false,
            _ => configured
        };
}
