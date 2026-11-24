namespace NutriPath.Api.Models.Admin;

/// <summary>Administrative roles. A user with none of these is an ordinary consumer.</summary>
public static class AdminRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string UserAdmin = "UserAdmin";
    public const string AIManager = "AIManager";
    public const string NutritionReviewer = "NutritionReviewer";
    public const string SupportAdmin = "SupportAdmin";

    public static readonly IReadOnlyList<string> All = [SuperAdmin, UserAdmin, AIManager, NutritionReviewer, SupportAdmin];

    public static bool IsValid(string role) => All.Contains(role);
}

/// <summary>Fine-grained permissions checked by the admin authorization policies.</summary>
public static class Permissions
{
    public const string DashboardRead = "dashboard.read";
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string UsersSuspend = "users.suspend";
    public const string UsersRevokeSessions = "users.revoke_sessions";
    public const string UsersExport = "users.export";
    public const string AdminsRead = "admins.read";
    public const string AdminsManage = "admins.manage";
    public const string AdminsAssignRoles = "admins.assign_roles";
    public const string AiProvidersRead = "ai.providers.read";
    public const string AiProvidersManage = "ai.providers.manage";
    public const string AiSecretsRotate = "ai.secrets.rotate";
    public const string AiModelsManage = "ai.models.manage";
    public const string AiUsageRead = "ai.usage.read";
    public const string AiUsageManage = "ai.usage.manage";
    public const string AiEvaluate = "ai.evaluate";
    public const string KnowledgeRead = "knowledge.read";
    public const string KnowledgeCreate = "knowledge.create";
    public const string KnowledgeEdit = "knowledge.edit";
    public const string KnowledgeReview = "knowledge.review";
    public const string KnowledgeApprove = "knowledge.approve";
    public const string KnowledgeArchive = "knowledge.archive";
    public const string KnowledgeReindex = "knowledge.reindex";
    public const string FoodsRead = "foods.read";
    public const string FoodsManage = "foods.manage";
    public const string FoodsSync = "foods.sync";
    public const string SystemMonitor = "system.monitor";
    public const string SystemSettingsManage = "system.settings.manage";
    public const string SystemJobsManage = "system.jobs.manage";
    public const string AuditRead = "audit.read";
    public const string AuditExport = "audit.export";

    public static readonly IReadOnlyList<string> All =
    [
        DashboardRead, UsersRead, UsersManage, UsersSuspend, UsersRevokeSessions, UsersExport, AdminsRead,
        AdminsManage, AdminsAssignRoles, AiProvidersRead, AiProvidersManage, AiSecretsRotate, AiModelsManage,
        AiUsageRead, AiUsageManage, AiEvaluate, KnowledgeRead, KnowledgeCreate, KnowledgeEdit, KnowledgeReview,
        KnowledgeApprove, KnowledgeArchive, KnowledgeReindex, FoodsRead, FoodsManage, FoodsSync, SystemMonitor,
        SystemSettingsManage, SystemJobsManage, AuditRead, AuditExport,
    ];

    /// <summary>
    /// The role → permission matrix. Defined in code so changes are reviewed like any other
    /// security change. SupportAdmin deliberately has no access to credentials or health data.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> ByRole = new Dictionary<string, string[]>
    {
        [AdminRoles.SuperAdmin] = [.. All],
        [AdminRoles.UserAdmin] =
        [
            DashboardRead, UsersRead, UsersManage, UsersSuspend, UsersRevokeSessions, UsersExport, AiUsageRead,
            AiUsageManage, AuditRead,
        ],
        [AdminRoles.AIManager] =
        [
            DashboardRead, AiProvidersRead, AiProvidersManage, AiModelsManage, AiUsageRead, AiEvaluate,
            KnowledgeRead, KnowledgeCreate, KnowledgeEdit, KnowledgeReindex, SystemMonitor, SystemJobsManage,
        ],
        [AdminRoles.NutritionReviewer] =
        [
            DashboardRead, KnowledgeRead, KnowledgeReview, KnowledgeApprove, KnowledgeArchive, KnowledgeEdit,
            FoodsRead, FoodsManage, FoodsSync,
        ],
        [AdminRoles.SupportAdmin] = [DashboardRead, UsersRead],
    };

    public static IReadOnlySet<string> For(IEnumerable<string> roles) =>
        roles.Where(ByRole.ContainsKey).SelectMany(r => ByRole[r]).ToHashSet();
}
