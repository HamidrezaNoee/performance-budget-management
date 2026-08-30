import { useEffect, useMemo, useState } from 'react'
import { Alert, Box, Card, CardContent, Divider, Stack, Tab, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Tabs, Typography } from '@mui/material'
import { api } from './api'
import SecurityAdmin from './SecurityAdmin'
import LicenseAdmin from './LicenseAdmin'
import IntegrationCredentialsAdmin from './IntegrationCredentialsAdmin'
import IdempotencyAdmin from './IdempotencyAdmin'
import OutboxAdmin from './OutboxAdmin'

type Audit = { id: string; userId?: string; entityType: string; entityId: string; action: string; oldValueJson?: string; newValueJson?: string; ipAddress?: string; createdAtUtc: string }
const faDateTime = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { dateStyle: 'short', timeStyle: 'short' })

export default function ReferenceAdmin({ companyId, roles }: { companyId: string; roles: string[] }) {
  const [tab, setTab] = useState(0)
  const [audit, setAudit] = useState<Audit[]>([])
  const [error, setError] = useState('')
  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const canManageSecurity = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN')
  const canViewIdempotency = canManageSecurity || roleSet.has('AUDITOR') || roleSet.has('CFO')
  const canViewOutbox = canManageSecurity || roleSet.has('AUDITOR') || roleSet.has('CFO') || roleSet.has('BUDGET_MANAGER')
  const canViewAudit = canManageSecurity || roleSet.has('AUDITOR') || roleSet.has('CFO') || roleSet.has('BUDGET_MANAGER')

  useEffect(() => {
    if (!canViewAudit) { setAudit([]); return }
    api.get<Audit[]>('/audit/recent', { params: { take: 200 } })
      .then(r => setAudit(r.data))
      .catch(() => setError('دریافت تاریخچه تغییرات ناموفق بود.'))
  }, [canViewAudit])

  return <Stack spacing={2.5}>
    <Alert severity="info">
      تنظیمات فقط برای مدیریت کاربران و سطح دسترسی، لایسنس نرم‌افزار، اتصال ERP/حسابداری، کنترل درخواست‌های Idempotent و Retry، Outbox/Dead-letter و تاریخچه تغییرات است. اطلاعات پایه عملیاتی و بودجه‌ای در منوی «اطلاعات پایه» قرار دارند.
    </Alert>

    <Card elevation={0}><Tabs value={tab} onChange={(_, value) => setTab(value)} variant="scrollable" scrollButtons="auto">
      <Tab label="کاربران و سطح دسترسی" disabled={!canManageSecurity} />
      <Tab label="لایسنس نرم‌افزار" disabled={!canManageSecurity} />
      <Tab label="Service Account برای ERP / حسابداری" disabled={!canManageSecurity} />
      <Tab label="Idempotency و Retry" disabled={!canViewIdempotency} />
      <Tab label="Outbox و Dead-letter" disabled={!canViewOutbox} />
      <Tab label="تاریخچه تغییرات" disabled={!canViewAudit} />
    </Tabs></Card>

    {error && <Alert severity="error">{error}</Alert>}
    {tab === 0 && canManageSecurity && <SecurityAdmin showLicense={false} />}
    {tab === 1 && canManageSecurity && <LicenseAdmin />}
    {tab === 2 && canManageSecurity && <IntegrationCredentialsAdmin companyId={companyId} />}
    {tab === 3 && canViewIdempotency && <IdempotencyAdmin roles={roles} />}
    {tab === 4 && canViewOutbox && <OutboxAdmin roles={roles} />}
    {tab === 5 && canViewAudit && <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <Box p={2.5}><Typography variant="h6" fontWeight={900}>تاریخچه تغییرات (Audit Trail)</Typography><Typography color="text.secondary">تغییرات حساس کاربران، بودجه، اطلاعات پایه، یکپارچه‌سازی و عملیات سیستمی برای ردیابی و ممیزی نگهداری می‌شوند.</Typography></Box><Divider />
      <TableContainer sx={{ maxHeight: '65vh' }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>زمان</TableCell><TableCell>موجودیت</TableCell><TableCell>عملیات</TableCell><TableCell>شناسه</TableCell><TableCell>مقدار جدید</TableCell></TableRow></TableHead><TableBody>
        {audit.map(x => <TableRow key={x.id}><TableCell sx={{ whiteSpace: 'nowrap' }}>{faDateTime.format(new Date(x.createdAtUtc))}</TableCell><TableCell>{x.entityType}</TableCell><TableCell>{x.action}</TableCell><TableCell sx={{ maxWidth: 180, overflow: 'hidden', textOverflow: 'ellipsis' }}>{x.entityId}</TableCell><TableCell sx={{ maxWidth: 520, direction: 'ltr', fontFamily: 'monospace', fontSize: 12 }}>{x.newValueJson ?? '-'}</TableCell></TableRow>)}
      </TableBody></Table></TableContainer>
    </CardContent></Card>}
  </Stack>
}
