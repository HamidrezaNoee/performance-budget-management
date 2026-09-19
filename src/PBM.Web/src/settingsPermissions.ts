/** Presentation-only role gates. API authorization remains authoritative. */
export function resolveSettingsPermissions(roles: readonly string[]) {
  const roleSet = new Set(roles.map(role => role.toUpperCase()))
  const canManageSecurity = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN')
  const canViewIdempotency = canManageSecurity || roleSet.has('AUDITOR') || roleSet.has('CFO')
  const canViewOutbox = canManageSecurity || roleSet.has('AUDITOR') || roleSet.has('CFO') || roleSet.has('BUDGET_MANAGER')
  const canViewAudit = canViewOutbox

  return { canManageSecurity, canViewIdempotency, canViewOutbox, canViewAudit }
}
