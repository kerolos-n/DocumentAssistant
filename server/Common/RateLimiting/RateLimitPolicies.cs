namespace DocumentAssistant.Common.RateLimiting;

/// <summary>
/// Named rate-limit policies, applied to endpoints with <c>RequireRateLimiting</c>, and the
/// defaults behind the per-user limits. The limits can be overridden under the
/// <c>RateLimiting</c> configuration section.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Answering spends an embedding and a model call, so it gets the tighter window.</summary>
    public const string Questions = "questions";

    /// <summary>Uploading spends storage and ingestion capacity.</summary>
    public const string Uploads = "uploads";

    public const int DefaultQuestionPermitLimit = 5;

    /// <summary>Five minutes: enough for a burst of follow-up questions, not a scraping loop.</summary>
    public const int DefaultQuestionWindowSeconds = 300;

    public const int DefaultUploadPermitLimit = 5;

    /// <summary>An hour: uploads are heavier and less frequent than questions.</summary>
    public const int DefaultUploadWindowSeconds = 3600;
}
