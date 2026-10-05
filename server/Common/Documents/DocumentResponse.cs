namespace DocumentAssistant.Common.Documents;

public sealed record DocumentResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeInBytes,
    DateTime UploadedAtUtc);
