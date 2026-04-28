namespace ExpertiseApi.Models;

public class ExpertiseAuditLog
{
    public Guid Id { get; set; }

    public DateTime Timestamp { get; set; }

    public AuditAction Action { get; set; }

    public Guid EntryId { get; set; }

    public required string Tenant { get; set; }

    public required string Principal { get; set; }

    public string? Agent { get; set; }

    public string? BeforeHash { get; set; }

    public string? AfterHash { get; set; }

    public string? IpAddress { get; set; }
}
