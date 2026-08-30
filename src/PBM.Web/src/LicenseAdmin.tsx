import { useEffect, useState } from 'react'
import { Alert, Box, Card, CardContent, Stack, Typography } from '@mui/material'
import { api } from './api'

type LicenseUsage = { maxUsers: number; activeUsers: number; maxCompanies: number; activeCompanies: number; expiresAtUtc: string; isActive: boolean }

const faDate = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { year: 'numeric', month: '2-digit', day: '2-digit' })

export default function LicenseAdmin() {
  const [license, setLicense] = useState<LicenseUsage | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    api.get<LicenseUsage>('/admin/security/license-usage')
      .then(r => setLicense(r.data))
      .catch(() => setError('دریافت وضعیت لایسنس ناموفق بود.'))
  }, [])

  return <Stack spacing={2.5}>
    {error && <Alert severity="error">{error}</Alert>}
    {!error && !license && <Alert severity="info">در حال دریافت وضعیت لایسنس...</Alert>}
    {license && <>
      <Alert severity={license.isActive ? 'success' : 'error'}>
        وضعیت لایسنس: {license.isActive ? 'فعال' : 'غیرفعال'} — تاریخ انقضا: {faDate.format(new Date(license.expiresAtUtc))}
      </Alert>
      <Box className="kpi-grid">
        <Metric title="کاربران فعال" value={`${license.activeUsers.toLocaleString('fa-IR')} / ${license.maxUsers.toLocaleString('fa-IR')}`} />
        <Metric title="شرکت‌های فعال" value={`${license.activeCompanies.toLocaleString('fa-IR')} / ${license.maxCompanies.toLocaleString('fa-IR')}`} />
        <Metric title="اعتبار" value={license.isActive ? 'معتبر' : 'نامعتبر'} />
        <Metric title="انقضا" value={faDate.format(new Date(license.expiresAtUtc))} />
      </Box>
    </>}
  </Stack>
}

function Metric({ title, value }: { title: string; value: string }) {
  return <Card elevation={0} className="kpi-card"><CardContent><Typography color="text.secondary" fontWeight={700}>{title}</Typography><Typography variant="h6" fontWeight={900} mt={1}>{value}</Typography></CardContent></Card>
}
