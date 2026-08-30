import { useEffect, useState } from 'react'
import { Alert, Box, Card, CardContent, Divider, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import { api } from './api'

type Audit = { id: string; userId?: string; entityType: string; entityId: string; action: string; oldValueJson?: string; newValueJson?: string; ipAddress?: string; createdAtUtc: string }
const faDateTime = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { dateStyle: 'short', timeStyle: 'short' })

export default function AuditAdmin() {
  const [audit, setAudit] = useState<Audit[]>([])
  const [error, setError] = useState('')

  useEffect(() => {
    api.get<Audit[]>('/audit/recent', { params: { take: 200 } })
      .then(r => setAudit(r.data))
      .catch(() => setError('دریافت تاریخچه تغییرات ناموفق بود.'))
  }, [])

  return <Card elevation={0}><CardContent sx={{ p: 0 }}>
    <Box p={2.5}>
      <Typography variant="h6" fontWeight={900}>تاریخچه تغییرات و Audit Trail</Typography>
      <Typography color="text.secondary">تغییرات حساس کاربران، بودجه، اطلاعات پایه، یکپارچه‌سازی و عملیات سیستمی برای ردیابی و ممیزی نگهداری می‌شوند.</Typography>
    </Box>
    {error && <Alert severity="error" sx={{ mx: 2.5, mb: 2 }}>{error}</Alert>}
    <Divider />
    <TableContainer sx={{ maxHeight: '65vh' }}><Table stickyHeader size="small"><TableHead><TableRow>
      <TableCell>زمان</TableCell><TableCell>موجودیت</TableCell><TableCell>عملیات</TableCell><TableCell>شناسه</TableCell><TableCell>مقدار جدید</TableCell>
    </TableRow></TableHead><TableBody>
      {audit.map(x => <TableRow key={x.id}>
        <TableCell sx={{ whiteSpace: 'nowrap' }}>{faDateTime.format(new Date(x.createdAtUtc))}</TableCell>
        <TableCell>{x.entityType}</TableCell><TableCell>{x.action}</TableCell>
        <TableCell sx={{ maxWidth: 180, overflow: 'hidden', textOverflow: 'ellipsis' }}>{x.entityId}</TableCell>
        <TableCell sx={{ maxWidth: 520, direction: 'ltr', fontFamily: 'monospace', fontSize: 12 }}>{x.newValueJson ?? '-'}</TableCell>
      </TableRow>)}
      {!audit.length && !error && <TableRow><TableCell colSpan={5} align="center" sx={{ py: 5, color: 'text.secondary' }}>تاریخچه‌ای برای نمایش وجود ندارد.</TableCell></TableRow>}
    </TableBody></Table></TableContainer>
  </CardContent></Card>
}
