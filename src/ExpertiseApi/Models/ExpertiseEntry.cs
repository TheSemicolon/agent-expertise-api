using System.ComponentModel.DataAnnotations.Schema;
using NpgsqlTypes;
using Pgvector;

namespace ExpertiseApi.Models;

public class ExpertiseEntry
{
    public Guid Id { get; set; }

    public required string Domain { get; set; }

    public List<string> Tags { get; set; } = [];

    public required string Title { get; set; }

    public required string Body { get; set; }

    public EntryType EntryType { get; set; }

    public Severity Severity { get; set; }

    public required string Source { get; set; }

    public string? SourceVersion { get; set; }

    [Column(TypeName = "vector(384)")]
    public Vector? Embedding { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeprecatedAt { get; set; }

    public NpgsqlTsVector SearchVector { get; set; } = null!;
}
