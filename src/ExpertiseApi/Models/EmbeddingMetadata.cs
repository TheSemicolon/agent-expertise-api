namespace ExpertiseApi.Models;

public class EmbeddingMetadata
{
    public int Id { get; set; }

    public required string ModelName { get; set; }

    public int Dimensions { get; set; }

    public DateTime LastReembedAt { get; set; }
}
