namespace DocumentAssistant.Data.Entities;

public sealed class Document
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeInBytes { get; set; }

    public string BlobName { get; set; } = string.Empty;

    public DateTime UploadedAtUtc { get; set; }
}
