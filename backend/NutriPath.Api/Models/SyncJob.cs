namespace NutriPath.Api.Models;

public enum SyncJobStatus { Running, Completed, Failed }

/// <summary>
/// One row per sync run. This is what Phase 16 (data freshness &
/// monitoring) will read from — we're laying the groundwork now instead
/// of bolting logging on as an afterthought later.
/// </summary>
public class SyncJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DataSourceId { get; set; }
    public DataSource? DataSource { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public SyncJobStatus Status { get; set; } = SyncJobStatus.Running;

    public int RecordsFetched { get; set; }
    public int RecordsInserted { get; set; }
    public int RecordsUpdated { get; set; }
    public int RecordsFailed { get; set; }
    public string? ErrorMessage { get; set; }
}