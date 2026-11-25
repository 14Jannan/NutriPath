namespace NutriPath.Api.Models;

/// <summary>Review workflow states for knowledge documents.</summary>
public static class KnowledgeStatus
{
    public const string Draft = "Draft";
    public const string PendingReview = "PendingReview";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Archived = "Archived";
}
