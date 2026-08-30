import { Alert } from '@mui/material'
import ReferenceAdmin from './ReferenceAdmin'
import SecurityAdmin from './SecurityAdmin'
import LicenseAdmin from './LicenseAdmin'
import IntegrationCredentialsAdmin from './IntegrationCredentialsAdmin'
import IdempotencyAdmin from './IdempotencyAdmin'
import OutboxAdmin from './OutboxAdmin'
import AuditAdmin from './AuditAdmin'
import CatalogPlaceholder from './CatalogPlaceholder'

export default function SystemSettingsWorkspace({ companyId, roles, section }: { companyId: string; roles: string[]; section: string }) {
  const roleSet = new Set(roles.map(x => x.toUpperCase()))
  const canManageSecurity = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN')
  const canViewIdempotency = canManageSecurity || roleSet.has('AUDITOR') || roleSet.has('CFO')
  const canViewOutbox = canManageSecurity || roleSet.has('AUDITOR') || roleSet.has('CFO') || roleSet.has('BUDGET_MANAGER')
  const canViewAudit = canManageSecurity || roleSet.has('AUDITOR') || roleSet.has('CFO') || roleSet.has('BUDGET_MANAGER')

  if (!section) return <ReferenceAdmin companyId={companyId} roles={roles} />

  if (section.startsWith('security/') || section.startsWith('access-hierarchy/'))
    return canManageSecurity ? <SecurityAdmin showLicense={false} /> : <Alert severity="warning">دسترسی مدیریت کاربران و امنیت برای این نقش فعال نیست.</Alert>

  if (section.startsWith('license/'))
    return canManageSecurity ? <LicenseAdmin /> : <Alert severity="warning">دسترسی مدیریت لایسنس برای این نقش فعال نیست.</Alert>

  if (section.startsWith('service-accounts/'))
    return canManageSecurity ? <IntegrationCredentialsAdmin companyId={companyId} /> : <Alert severity="warning">دسترسی Service Account برای این نقش فعال نیست.</Alert>

  const externalLabels: Record<string, string> = {
    'external-systems/erp': 'اتصال ERP', 'external-systems/accounting': 'اتصال سیستم حسابداری',
    'external-systems/crm': 'اتصال CRM', 'external-systems/bpms': 'اتصال BPMS',
    'external-systems/bi': 'اتصال BI', 'external-systems/apis': 'سایر APIها'
  }
  if (externalLabels[section]) return <CatalogPlaceholder title={externalLabels[section]} fields={['نام اتصال', 'Endpoint', 'Authentication', 'Timeout', 'وضعیت', 'آخرین تست']} />

  if (section === 'integration/idempotency' || section === 'integration/retry-policy')
    return canViewIdempotency ? <IdempotencyAdmin roles={roles} /> : <Alert severity="warning">دسترسی مشاهده تنظیمات Idempotency/Retry برای این نقش فعال نیست.</Alert>

  const integrationLabels: Record<string, string> = {
    'integration/endpoint': 'Endpointهای Integration', 'integration/authentication': 'Authentication اتصال‌ها',
    'integration/timeout': 'Timeout اتصال‌ها', 'integration/mapping': 'Mapping یکپارچه‌سازی'
  }
  if (integrationLabels[section]) return <CatalogPlaceholder title={integrationLabels[section]} fields={['سیستم', 'کلید تنظیم', 'مقدار', 'محیط', 'وضعیت']} />

  if (section === 'messaging/idempotency' || section === 'messaging/retry')
    return canViewIdempotency ? <IdempotencyAdmin roles={roles} /> : <Alert severity="warning">دسترسی مشاهده این بخش برای نقش شما فعال نیست.</Alert>

  if (['messaging/outbox', 'messaging/dead-letter', 'messaging/reprocessing'].includes(section))
    return canViewOutbox ? <OutboxAdmin roles={roles} /> : <Alert severity="warning">دسترسی مدیریت پیام برای نقش شما فعال نیست.</Alert>

  if (section === 'messaging/inbox') return <CatalogPlaceholder title="Inbox پیام‌ها" fields={['Message Id', 'Source', 'Received At', 'Status', 'Processed At', 'Retry Count']} />

  if (['audit/change-history', 'audit/change-log', 'audit/audit-log', 'audit/master-data-changes'].includes(section))
    return canViewAudit ? <AuditAdmin /> : <Alert severity="warning">دسترسی مشاهده Audit برای نقش شما فعال نیست.</Alert>

  const auditPlaceholders: Record<string, string> = {
    'audit/login-history': 'Login History', 'audit/integration-log': 'Integration Log', 'audit/error-log': 'Error Log'
  }
  if (auditPlaceholders[section]) return <CatalogPlaceholder title={auditPlaceholders[section]} fields={['زمان', 'کاربر / سیستم', 'عملیات', 'نتیجه', 'Correlation Id', 'جزئیات']} />

  return <CatalogPlaceholder title="تنظیمات سامانه" description="مسیر این بخش ایجاد شده و فرم اختصاصی آن در ادامه تکمیل می‌شود." />
}
