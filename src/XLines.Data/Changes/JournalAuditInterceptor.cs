using System.Text.Json;
using System.Text.Json.Serialization;
using XLines.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace XLines.Data.Changes;

/// <summary>
/// The one place a journal write is stamped and recorded (ADR 0025).
///
/// <para>
/// On every save, before it is sent: an added or changed entry, parlay or operator setting gets
/// <c>UpdatedAtUtc</c>, which is what a merge between two machines orders versions by; an entry
/// or parlay gets a <see cref="JournalAudit"/> row holding its columns before and after; and a
/// deleted one gets a <see cref="JournalTombstone"/>, so the deletion travels to the other
/// machine instead of being undone by its copy. The form, settlement, an edit, a delete and an
/// import all reach the database through <c>SaveChanges</c>, so none of them can forget.
/// </para>
///
/// <para>
/// The context adds it to itself (<see cref="XLinesDbContext"/>'s <c>OnConfiguring</c>), so a
/// context made by a test or a tool is audited exactly as the hosts' are. Bulk
/// <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> bypass it; nothing uses them on the journal, and a
/// change that did would have to write its own audit.
/// </para>
///
/// <para>
/// An import (<see cref="XLinesDbContext.ImportingJournal"/>) keeps the timestamps the file
/// carries, since stamping them "now" would make every imported row newer than the machine it
/// came from, and is recorded as <see cref="JournalAuditAction.Imported"/>.
/// </para>
/// </summary>
public sealed class JournalAuditInterceptor : SaveChangesInterceptor
{
    public static readonly JournalAuditInterceptor Instance = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private JournalAuditInterceptor() { }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    /// <summary>A moment as Postgres keeps it: whole microseconds, so a value read back equals the one written.</summary>
    public static DateTime Now() => Truncate(DateTime.UtcNow);

    public static DateTime Truncate(DateTime utc)
        => new(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);

    private static void Stamp(DbContext? context)
    {
        if (context is not XLinesDbContext db)
            return;

        var rows = db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                        && e.Entity is JournalEntry or Parlay or AppSetting)
            .ToList();

        if (rows.Count == 0)
            return;

        var now = Now();
        var machine = Environment.MachineName;
        var importing = db.ImportingJournal;

        // Tombstones the caller already added in this save (an import carrying the file's own
        // deletion time), so a deletion is not given a second one.
        var tombstoned = db.ChangeTracker.Entries<JournalTombstone>()
            .Where(t => t.State == EntityState.Added)
            .Select(t => t.Entity.SyncId)
            .ToHashSet();

        foreach (var row in rows)
        {
            if (row.State == EntityState.Modified && !Changed(row))
                continue;

            if (row.State != EntityState.Deleted && (!importing || Stamped(row) == default))
                row.Property(nameof(JournalEntry.UpdatedAtUtc)).CurrentValue = now;

            if (row.Entity is AppSetting)
                continue;

            var (id, kind) = row.Entity switch
            {
                JournalEntry e => (row.State == EntityState.Deleted
                    ? (Guid)row.Property(nameof(JournalEntry.SyncId)).OriginalValue! : e.SyncId, JournalTombstone.EntryKind),
                Parlay p => (p.Id, JournalTombstone.ParlayKind),
                _ => throw new InvalidOperationException()
            };

            db.JournalAudit.Add(new JournalAudit
            {
                EntrySyncId = id,
                Entity = kind,
                Action = importing ? JournalAuditAction.Imported : row.State switch
                {
                    EntityState.Added => JournalAuditAction.Created,
                    EntityState.Deleted => JournalAuditAction.Deleted,
                    _ => JournalAuditAction.Updated
                },
                AtUtc = now,
                Machine = machine,
                Before = row.State == EntityState.Added ? null : Columns(row, original: true),
                After = row.State == EntityState.Deleted ? null : Columns(row, original: false)
            });

            if (row.State == EntityState.Deleted && tombstoned.Add(id))
                db.JournalTombstones.Add(new JournalTombstone { SyncId = id, Kind = kind, DeletedAtUtc = now });
        }
    }

    private static DateTime Stamped(EntityEntry row)
        => (DateTime)row.Property(nameof(JournalEntry.UpdatedAtUtc)).CurrentValue!;

    /// <summary>
    /// Whether a modified row has a column that really changed. EF marks a property modified
    /// when it is assigned, even with the value it already held; an edit that changes nothing
    /// is not a change, and stamping it would make the row look newer than it is.
    /// </summary>
    private static bool Changed(EntityEntry row)
        => row.Properties.Any(p => p.IsModified
                                   && p.Metadata.Name != nameof(JournalEntry.UpdatedAtUtc)
                                   && !Equals(p.OriginalValue, p.CurrentValue));

    /// <summary>The row's mapped columns as a JSON object, by property name.</summary>
    private static string Columns(EntityEntry row, bool original)
    {
        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in row.Properties)
            values[property.Metadata.Name] = original ? property.OriginalValue : property.CurrentValue;

        return JsonSerializer.Serialize(values, Json);
    }
}
