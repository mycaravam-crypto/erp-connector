namespace Connector.Api.Authorization;

/// <summary>One grantable permission: its key (sent to and from the UI), a short label and what it allows.</summary>
public sealed record PermissionInfo(string Key, string Label, string Description);

/// <summary>The permissions belonging to one menu item. When the group has a <c>.view</c> permission, it decides
/// whether the menu item is shown at all, and every other permission in the group implies it.</summary>
public sealed record PermissionGroup(string Key, string Label, IReadOnlyList<PermissionInfo> Permissions);

/// <summary>
/// Everything a non-Admin role can be allowed to see and do, grouped by menu item. Admins always hold every
/// permission, plus the admin-only areas that aren't grantable at all (Connect, Users, Permissions). The keys are
/// stored in the database (<see cref="RolePermissionStore"/>), so renaming one silently revokes it.
/// </summary>
public static class Permissions
{
    public const string SourceSchemaView = "sourceSchema.view";

    public const string ExportMappingView = "exportMapping.view";
    public const string ExportMappingEdit = "exportMapping.edit";

    public const string ManagedExportView = "managedExport.view";
    public const string ManagedExportRun = "managedExport.run";
    public const string ManagedExportRelease = "managedExport.release";
    public const string ManagedExportDeliver = "managedExport.deliver";
    public const string ManagedExportSkip = "managedExport.skip";

    public const string ExportJobsView = "exportJobs.view";
    public const string ExportJobsCreate = "exportJobs.create";
    public const string ExportJobsEdit = "exportJobs.edit";
    public const string ExportJobsDelete = "exportJobs.delete";
    public const string ExportJobsTest = "exportJobs.test";
    public const string ExportJobsDownload = "exportJobs.download";

    public const string ImportJobsView = "importJobs.view";
    public const string ImportJobsCreate = "importJobs.create";
    public const string ImportJobsEdit = "importJobs.edit";
    public const string ImportJobsDelete = "importJobs.delete";
    public const string ImportJobsRun = "importJobs.run";
    public const string ImportJobsRelease = "importJobs.release";

    public const string IcdSchemaView = "icdSchema.view";

    public const string AuditView = "audit.view";

    public const string SettingsScheduler = "settings.scheduler";
    public const string SettingsBranding = "settings.branding";
    public const string SettingsGdpr = "settings.gdpr";
    public const string SettingsInstance = "settings.instance";

    public static readonly IReadOnlyList<PermissionGroup> Catalogue =
    [
        new(
            "sourceSchema",
            "Source Schema",
            [new(SourceSchemaView, "View", "Browse the tables and columns of the connected source system.")]
        ),
        new(
            "exportMapping",
            "CMDB Export Mapping",
            [
                new(ExportMappingView, "View", "See the export mapping, its presets and the preview."),
                new(ExportMappingEdit, "Edit", "Change the export mapping and create or delete presets."),
            ]
        ),
        new(
            "managedExport",
            "Managed Export",
            [
                new(ManagedExportView, "View", "See the export runs and their details."),
                new(ManagedExportRun, "Run", "Start an export run (\"Run Now\")."),
                new(
                    ManagedExportRelease,
                    "Release",
                    "Release a run as operator, or approve one as the second person (four-eyes)."
                ),
                new(ManagedExportDeliver, "Record delivery", "Record that a released run was delivered to the vendor."),
                new(ManagedExportSkip, "Skip", "Mark a pending or failed run as skipped."),
            ]
        ),
        new(
            "exportJobs",
            "Export Jobs",
            [
                new(ExportJobsView, "View", "See export jobs, their run history and preview."),
                new(ExportJobsCreate, "Create", "Create new export jobs and duplicate existing ones."),
                new(ExportJobsEdit, "Edit", "Change export jobs and switch them on or off."),
                new(ExportJobsDelete, "Delete", "Delete export jobs."),
                new(ExportJobsTest, "Test run", "Run a capped test (at most 50 records, no file)."),
                new(ExportJobsDownload, "Run & download", "Run an export job in full and download the file."),
            ]
        ),
        new(
            "importJobs",
            "Import Jobs",
            [
                new(ImportJobsView, "View", "See import jobs, their runs, preview and diffs."),
                new(ImportJobsCreate, "Create", "Create new import jobs and duplicate existing ones."),
                new(ImportJobsEdit, "Edit", "Change import jobs and switch them on or off."),
                new(ImportJobsDelete, "Delete", "Delete import jobs."),
                new(ImportJobsRun, "Run", "Upload a vendor file and stage it as an import run."),
                new(
                    ImportJobsRelease,
                    "Release / reject",
                    "Commit or reject a staged import, as operator or as the approving second person (four-eyes)."
                ),
            ]
        ),
        new("icdSchema", "ICD Schema", [new(IcdSchemaView, "View", "See the ICD export column contract.")]),
        new("audit", "Audit Log", [new(AuditView, "View", "Browse the audit log.")]),
        new(
            "settings",
            "Settings",
            [
                new(SettingsScheduler, "Scheduler", "See and change the daily export time and retention."),
                new(SettingsBranding, "Branding", "Change the app name, logo, favicon and login background."),
                new(SettingsGdpr, "GDPR denylist", "See and change the fields that may never be exported."),
                new(
                    SettingsInstance,
                    "Connector instance",
                    "See this installation's instance id and generate a new one."
                ),
            ]
        ),
    ];

    public static readonly IReadOnlySet<string> All = Catalogue
        .SelectMany(g => g.Permissions)
        .Select(p => p.Key)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>What the User role may do until an Admin first saves the permission screen: look at everything
    /// except Connect, Settings and the audit log, change nothing.</summary>
    public static readonly IReadOnlySet<string> DefaultUserPermissions = new HashSet<string>(StringComparer.Ordinal)
    {
        SourceSchemaView,
        ExportMappingView,
        ManagedExportView,
        ExportJobsView,
        ImportJobsView,
        IcdSchemaView,
    };

    /// <summary>
    /// The effective set for <paramref name="granted"/>: unknown keys are dropped (so a renamed or removed
    /// permission can't grant anything), and every permission adds its group's <c>.view</c> permission, since
    /// e.g. editing an export job you can't open makes no sense.
    /// </summary>
    public static IReadOnlySet<string> Normalize(IEnumerable<string> granted)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in granted.Where(All.Contains))
        {
            result.Add(key);
            var view = ViewOf(key);
            if (view is not null)
                result.Add(view);
        }
        return result;
    }

    // The ".view" permission of key's group, when that group has one.
    private static string? ViewOf(string key)
    {
        var view = string.Concat(key.AsSpan(0, key.IndexOf('.', StringComparison.Ordinal)), ".view");
        return All.Contains(view) ? view : null;
    }
}
