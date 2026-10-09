// Mirrors Connector.Api.Authorization.Permissions — the keys the server checks and /api/auth/me returns. Only the
// ones the UI gates something on are listed here.
export const Permission = {
  SourceSchemaView: 'sourceSchema.view',
  ExportMappingView: 'exportMapping.view',
  ExportMappingEdit: 'exportMapping.edit',
  ManagedExportView: 'managedExport.view',
  ManagedExportRun: 'managedExport.run',
  ManagedExportRelease: 'managedExport.release',
  ManagedExportDeliver: 'managedExport.deliver',
  ManagedExportSkip: 'managedExport.skip',
  ExportJobsView: 'exportJobs.view',
  ExportJobsCreate: 'exportJobs.create',
  ExportJobsEdit: 'exportJobs.edit',
  ExportJobsDelete: 'exportJobs.delete',
  ExportJobsTest: 'exportJobs.test',
  ExportJobsDownload: 'exportJobs.download',
  ImportJobsView: 'importJobs.view',
  ImportJobsCreate: 'importJobs.create',
  ImportJobsEdit: 'importJobs.edit',
  ImportJobsDelete: 'importJobs.delete',
  ImportJobsRun: 'importJobs.run',
  ImportJobsRelease: 'importJobs.release',
  IcdSchemaView: 'icdSchema.view',
  AuditView: 'audit.view',
  SettingsScheduler: 'settings.scheduler',
  SettingsBranding: 'settings.branding',
  SettingsGdpr: 'settings.gdpr',
  SettingsInstance: 'settings.instance',
} as const
export type Permission = (typeof Permission)[keyof typeof Permission]

export const SETTINGS_PERMISSIONS: readonly Permission[] = [
  Permission.SettingsScheduler,
  Permission.SettingsBranding,
  Permission.SettingsGdpr,
  Permission.SettingsInstance,
]

/** What a route needs: Admin, or at least one of `anyOf`. A route without this is open to every signed-in user. */
export interface RouteAccess {
  admin?: boolean
  anyOf?: readonly Permission[]
}
