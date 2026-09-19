import { Alert } from '@mui/material'
import ReferenceAdmin from './ReferenceAdmin'
import SecurityAdmin from './SecurityAdmin'
import LicenseAdmin from './LicenseAdmin'
import IntegrationCredentialsAdmin from './IntegrationCredentialsAdmin'
import IdempotencyAdmin from './IdempotencyAdmin'
import OutboxAdmin from './OutboxAdmin'
import AuditAdmin from './AuditAdmin'

export default function SystemSettingsWorkspace({ companyId, roles, section }: { companyId: string; roles: string[]; section: string }) {
  const roleSet = new Set(roles.map(role => role.toUpperCase()))
  const canManageSecurity = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN')
  const canViewIdempotency = canManageSecurity || roleSet.has('AUDITOR') || roleSet.has('CFO')
  const canViewOutbox = canViewIdempotency || roleSet.has('BUDGET_MANAGER')
  const canViewAudit = canViewOutbox

  if (!section) return <ReferenceAdmin companyId={companyId} roles={roles} />
  if (section === 'security/users' || section === 'access-hierarchy/organization')
    return canManageSecurity ? <SecurityAdmin showLicense={false} /> : <Alert severity="warning">دسترسی مدیریت کاربران و امنیت برای این نقش فعال نیست.</Alert>
  if (section === 'license/overview')
    return canManageSecurity ? <LicenseAdmin /> : <Alert severity="warning">دسترسی مدیریت لایسنس برای این نقش فعال نیست.</Alert>
  if (section === 'service-accounts/manage')
    return canManageSecurity ? <IntegrationCredentialsAdmin companyId={companyId} /> : <Alert severity="warning">دسترسی Service Account برای این نقش فعال نیست.</Alert>
  if (section === 'integration/idempotency')
    return canViewIdempotency ? <IdempotencyAdmin roles={roles} /> : <Alert severity="warning">دسترسی مشاهده Idempotency و Retry برای این نقش فعال نیست.</Alert>
  if (section === 'messaging/outbox')
    return canViewOutbox ? <OutboxAdmin roles={roles} /> : <Alert severity="warning">دسترسی مدیریت پیام برای این نقش فعال نیست.</Alert>
  if (section === 'audit/audit-log')
    return canViewAudit ? <AuditAdmin /> : <Alert severity="warning">دسترسی مشاهده Audit برای این نقش فعال نیست.</Alert>

  return <Alert severity="info">این مسیر دیگر در منوی فعال سامانه وجود ندارد.</Alert>
}
