import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Button, Card, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControl, InputLabel, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer,
  TableHead, TableRow, TextField, Typography
} from '@mui/material'
import { api } from './api'

type RecordItem = {
  id: string
  userId: string
  userDisplayName: string
  key: string
  scope: string
  status: number
  correlationId?: string | null
  createdAtUtc: string
  updatedAtUtc: string
  expiresAtUtc: string
  completedAtUtc?: string | null
  failureType?: string | null
}

type ResolutionAction = 0 | 1

const statusMeta = [
  { label: 'در حال پردازش', color: 'info' as const },
  { label: 'تکمیل‌شده', color: 'success' as const },
  { label: 'نیازمند تطبیق', color: 'error' as const }
]
const dateTime = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { dateStyle: 'short', timeStyle: 'short' })

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? fallback
  }
  return fallback
}

export default function IdempotencyAdmin({ roles }: { roles: string[] }) {
  const [records, setRecords] = useState<RecordItem[]>([])
  const [status, setStatus] = useState<number | ''>(2)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [selected, setSelected] = useState<RecordItem | null>(null)
  const [action, setAction] = useState<ResolutionAction>(0)
  const [comment, setComment] = useState('')

  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const canResolve = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN')
  const canCleanup = canResolve

  const reload = async () => {
    setBusy(true); setError('')
    try {
      const { data } = await api.get<RecordItem[]>('/admin/idempotency/', {
        params: { status: status === '' ? undefined : status, take: 500 }
      })
      setRecords(data)
    } catch (error) { setError(apiError(error, 'دریافت عملیات Idempotent ناموفق بود.')) }
    finally { setBusy(false) }
  }

  useEffect(() => { reload() }, [status])

  const openResolve = (record: RecordItem, nextAction: ResolutionAction) => {
    setSelected(record); setAction(nextAction); setComment(''); setError(''); setMessage('')
  }

  const resolve = async () => {
    if (!selected || !canResolve || comment.trim().length < 5) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.post(`/admin/idempotency/${selected.id}/resolve`, { action, comment: comment.trim() })
      setSelected(null); setComment('')
      setMessage(action === 0
        ? 'عملیات مبهم پس از تطبیق کسب‌وکار به‌عنوان تکمیل‌شده ثبت شد.'
        : 'قفل Idempotency پس از تطبیق آزاد شد؛ اکنون همان Business Command می‌تواند با کنترل اپراتور Retry شود.')
      await reload()
    } catch (error) { setError(apiError(error, 'ثبت نتیجه تطبیق Idempotency ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const cleanup = async () => {
    if (!canCleanup || !window.confirm('رکوردهای Completed که دوره نگهداری آن‌ها تمام شده پاک شوند؟ رکوردهای Uncertain هرگز توسط این عملیات حذف نمی‌شوند.')) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.delete<{ removed: number }>('/admin/idempotency/expired-completed', { params: { take: 5000 } })
      setMessage(`${data.removed.toLocaleString('fa-IR')} رکورد Completed منقضی‌شده پاک شد.`)
      await reload()
    } catch (error) { setError(apiError(error, 'پاکسازی رکوردهای Idempotency ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const uncertainCount = records.filter(x => x.status === 2).length

  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    {message && <Alert severity="success" onClose={() => setMessage('')}>{message}</Alert>}
    {!canResolve && <Alert severity="info">شما دسترسی مشاهده عملیات Idempotency را دارید. تصمیم تطبیق و آزادسازی Retry فقط برای مدیر سامانه فعال است.</Alert>}

    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>تطبیق عملیات Idempotent و Retryهای مبهم</Typography>
      <Typography color="text.secondary" mt={.5}>اگر یک Write مالی بعد از دریافت Idempotency-Key با Exception یا قطع ارتباط در وضعیت نامطمئن بماند، PBM آن Key را خودکار آزاد نمی‌کند. ابتدا اثر واقعی در بودجه/ERP/سند مالی بررسی می‌شود و سپس مدیر تصمیم می‌گیرد.</Typography>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} mt={2} alignItems={{ md: 'center' }}>
        <FormControl size="small" sx={{ minWidth: 210 }}><InputLabel>وضعیت</InputLabel><Select label="وضعیت" value={status} onChange={e => setStatus(String(e.target.value) === '' ? '' : Number(e.target.value))}><MenuItem value="">همه</MenuItem>{statusMeta.map((x, index) => <MenuItem key={x.label} value={index}>{x.label}</MenuItem>)}</Select></FormControl>
        <Button variant="outlined" onClick={reload} disabled={busy}>به‌روزرسانی</Button>
        {canCleanup && <Button variant="text" onClick={cleanup} disabled={busy}>پاکسازی Completedهای منقضی</Button>}
        <Chip label={`Uncertain قابل مشاهده: ${uncertainCount.toLocaleString('fa-IR')}`} color={uncertainCount > 0 ? 'error' : 'success'} variant="outlined" />
      </Stack>
    </CardContent></Card>

    <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <TableContainer sx={{ maxHeight: '65vh' }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>کاربر / زمان</TableCell><TableCell>Endpoint Scope</TableCell><TableCell>Idempotency Key</TableCell><TableCell>Correlation ID</TableCell><TableCell>وضعیت / خطا</TableCell><TableCell>انقضا</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>
        {records.map(record => {
          const meta = statusMeta[record.status] ?? statusMeta[2]
          return <TableRow key={record.id} hover>
            <TableCell><Typography fontWeight={800}>{record.userDisplayName}</Typography><Typography variant="caption" color="text.secondary">ایجاد: {dateTime.format(new Date(record.createdAtUtc))}<br/>آخرین تغییر: {dateTime.format(new Date(record.updatedAtUtc))}</Typography></TableCell>
            <TableCell sx={{ direction: 'ltr', textAlign: 'left', fontFamily: 'monospace', fontSize: 12, maxWidth: 300, wordBreak: 'break-word' }}>{record.scope}</TableCell>
            <TableCell sx={{ direction: 'ltr', textAlign: 'left', fontFamily: 'monospace', fontSize: 12, maxWidth: 260, wordBreak: 'break-all' }}>{record.key}</TableCell>
            <TableCell sx={{ direction: 'ltr', textAlign: 'left', fontFamily: 'monospace', fontSize: 12, maxWidth: 230, wordBreak: 'break-all' }}>{record.correlationId ?? '-'}</TableCell>
            <TableCell><Chip size="small" label={meta.label} color={meta.color} />{record.failureType && <Typography variant="caption" color="text.secondary" display="block" mt={.5}>{record.failureType}</Typography>}</TableCell>
            <TableCell sx={{ whiteSpace: 'nowrap' }}>{record.status === 2 ? <Typography color="error.main" variant="caption">Uncertain خودکار منقضی نمی‌شود</Typography> : dateTime.format(new Date(record.expiresAtUtc))}</TableCell>
            <TableCell>{record.status === 2 && canResolve ? <Stack direction="row" spacing={.5} flexWrap="wrap" useFlexGap><Button size="small" color="success" variant="contained" onClick={() => openResolve(record, 0)}>اثر انجام شده</Button><Button size="small" color="warning" variant="outlined" onClick={() => openResolve(record, 1)}>آزاد برای Retry</Button></Stack> : '-'}</TableCell>
          </TableRow>
        })}
        {!records.length && !busy && <TableRow><TableCell colSpan={7} align="center" sx={{ py: 6 }}>رکوردی با فیلتر انتخاب‌شده وجود ندارد.</TableCell></TableRow>}
      </TableBody></Table></TableContainer>
    </CardContent></Card>

    <Dialog open={!!selected} onClose={() => !busy && setSelected(null)} fullWidth maxWidth="sm">
      <DialogTitle>{action === 0 ? 'تأیید انجام‌شدن Business Command' : 'آزادسازی برای Retry کنترل‌شده'}</DialogTitle>
      <DialogContent>
        {selected && <Stack spacing={1.5} mt={1}>
          <Alert severity={action === 0 ? 'success' : 'warning'}>{action === 0
            ? 'فقط وقتی انتخاب کنید که با سند، دیتابیس یا سیستم مقصد تأیید کرده‌اید اثر کسب‌وکار قبلاً ثبت شده است.'
            : 'فقط وقتی آزاد کنید که بررسی کرده‌اید اثر کسب‌وکار قبلی ثبت نشده است. پس از حذف Guard، Retry می‌تواند Write را واقعاً اجرا کند.'}</Alert>
          <Typography variant="body2"><b>Scope:</b> <span dir="ltr">{selected.scope}</span></Typography>
          <Typography variant="body2"><b>Correlation:</b> <span dir="ltr">{selected.correlationId ?? '-'}</span></Typography>
          <TextField multiline minRows={4} label="شرح تطبیق و شواهد بررسی‌شده *" value={comment} onChange={e => setComment(e.target.value)} helperText="حداقل ۵ کاراکتر؛ این توضیح در Audit ثبت می‌شود." />
        </Stack>}
      </DialogContent>
      <DialogActions><Button onClick={() => setSelected(null)} disabled={busy}>انصراف</Button><Button variant="contained" color={action === 0 ? 'success' : 'warning'} onClick={resolve} disabled={busy || comment.trim().length < 5}>{action === 0 ? 'ثبت به‌عنوان Completed' : 'تأیید آزادسازی Retry'}</Button></DialogActions>
    </Dialog>
  </Stack>
}
