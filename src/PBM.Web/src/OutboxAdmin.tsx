import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Chip, FormControl, InputLabel, MenuItem,
  Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography
} from '@mui/material'
import { api } from './api'

type Summary = { pending: number; processing: number; completed: number; deadLetter: number }
type Message = {
  id: string
  messageType: string
  destination: string
  status: number
  attempts: number
  nextAttemptAtUtc: string
  lockedUntilUtc?: string | null
  completedAtUtc?: string | null
  correlationId?: string | null
  deduplicationKey?: string | null
  lastError?: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

const faDateTime = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { dateStyle: 'short', timeStyle: 'medium' })
const statusOptions = [
  { value: '', label: 'همه وضعیت‌ها' },
  { value: '0', label: 'در انتظار' },
  { value: '1', label: 'در حال ارسال' },
  { value: '2', label: 'تکمیل‌شده' },
  { value: '3', label: 'Dead-letter' }
]

function statusChip(status: number) {
  if (status === 0) return <Chip size="small" color="warning" variant="outlined" label="در انتظار" />
  if (status === 1) return <Chip size="small" color="info" variant="outlined" label="در حال ارسال" />
  if (status === 2) return <Chip size="small" color="success" variant="outlined" label="تکمیل‌شده" />
  return <Chip size="small" color="error" label="Dead-letter" />
}

function date(value?: string | null) {
  return value ? faDateTime.format(new Date(value)) : '-'
}

function errorText(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? fallback
  }
  return fallback
}

export default function OutboxAdmin({ roles }: { roles: string[] }) {
  const [summary, setSummary] = useState<Summary | null>(null)
  const [messages, setMessages] = useState<Message[]>([])
  const [status, setStatus] = useState('3')
  const [error, setError] = useState('')
  const [busyId, setBusyId] = useState('')
  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const canRetry = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN')

  const reload = async () => {
    setError('')
    try {
      const params: Record<string, string | number> = { take: 300 }
      if (status) params.status = status
      const [summaryResponse, messagesResponse] = await Promise.all([
        api.get<Summary>('/operations/outbox/summary'),
        api.get<Message[]>('/operations/outbox/messages', { params })
      ])
      setSummary(summaryResponse.data)
      setMessages(messagesResponse.data)
    } catch (error) {
      setError(errorText(error, 'دریافت وضعیت Outbox ناموفق بود.'))
    }
  }

  useEffect(() => { reload() }, [status])

  const retry = async (message: Message) => {
    if (!canRetry || message.status !== 3) return
    if (!window.confirm('این پیام از Dead-letter خارج و از ابتدا برای ارسال مجدد صف‌بندی شود؟')) return
    setBusyId(message.id); setError('')
    try {
      await api.post(`/operations/outbox/${message.id}/retry`)
      await reload()
    } catch (error) {
      setError(errorText(error, 'Retry پیام Outbox ناموفق بود.'))
    } finally { setBusyId('') }
  }

  return <Stack spacing={2}>
    {error && <Alert severity="error">{error}</Alert>}
    <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
      <Card elevation={0} sx={{ flex: 1 }}><CardContent><Typography color="text.secondary">Pending</Typography><Typography variant="h4" fontWeight={900}>{summary?.pending.toLocaleString('fa-IR') ?? '-'}</Typography></CardContent></Card>
      <Card elevation={0} sx={{ flex: 1 }}><CardContent><Typography color="text.secondary">Processing</Typography><Typography variant="h4" fontWeight={900}>{summary?.processing.toLocaleString('fa-IR') ?? '-'}</Typography></CardContent></Card>
      <Card elevation={0} sx={{ flex: 1 }}><CardContent><Typography color="text.secondary">Completed</Typography><Typography variant="h4" fontWeight={900}>{summary?.completed.toLocaleString('fa-IR') ?? '-'}</Typography></CardContent></Card>
      <Card elevation={0} sx={{ flex: 1 }}><CardContent><Typography color="text.secondary">Dead-letter</Typography><Typography variant="h4" fontWeight={900} color={summary?.deadLetter ? 'error.main' : 'text.primary'}>{summary?.deadLetter.toLocaleString('fa-IR') ?? '-'}</Typography></CardContent></Card>
    </Stack>

    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} alignItems={{ md: 'center' }}>
        <Box><Typography variant="h6" fontWeight={900}>Outbox و Dead-letter</Typography><Typography color="text.secondary">ارسال بیرونی از تراکنش اصلی جدا است. خطاها با Backoff مجدداً تلاش می‌شوند و بعد از سقف تعیین‌شده به Dead-letter می‌روند.</Typography></Box>
        <Stack direction="row" spacing={1}><FormControl size="small" sx={{ minWidth: 170 }}><InputLabel>وضعیت</InputLabel><Select value={status} label="وضعیت" onChange={e => setStatus(e.target.value)}>{statusOptions.map(x => <MenuItem key={x.value || 'all'} value={x.value}>{x.label}</MenuItem>)}</Select></FormControl><Button variant="outlined" onClick={reload}>به‌روزرسانی</Button></Stack>
      </Stack>
    </CardContent></Card>

    <Card elevation={0}><CardContent sx={{ p: 0 }}><TableContainer sx={{ maxHeight: '65vh' }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>زمان</TableCell><TableCell>نوع / مقصد</TableCell><TableCell>وضعیت</TableCell><TableCell align="left">تلاش</TableCell><TableCell>تلاش بعدی</TableCell><TableCell>Correlation</TableCell><TableCell>خطای آخر</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>
      {messages.map(row => <TableRow key={row.id} hover><TableCell sx={{ whiteSpace: 'nowrap' }}>{date(row.createdAtUtc)}</TableCell><TableCell><Typography fontWeight={800}>{row.messageType}</Typography><Typography variant="caption" color="text.secondary">{row.destination}</Typography></TableCell><TableCell>{statusChip(row.status)}</TableCell><TableCell align="left">{row.attempts.toLocaleString('fa-IR')}</TableCell><TableCell sx={{ whiteSpace: 'nowrap' }}>{row.status === 0 ? date(row.nextAttemptAtUtc) : row.status === 1 ? `قفل تا ${date(row.lockedUntilUtc)}` : row.status === 2 ? date(row.completedAtUtc) : '-'}</TableCell><TableCell sx={{ direction: 'ltr', fontFamily: 'monospace', maxWidth: 150, overflow: 'hidden', textOverflow: 'ellipsis' }}>{row.correlationId ?? '-'}</TableCell><TableCell sx={{ maxWidth: 360 }}><Typography variant="caption" color={row.status === 3 ? 'error' : 'text.secondary'}>{row.lastError ?? '-'}</Typography></TableCell><TableCell><Button size="small" disabled={!canRetry || row.status !== 3 || busyId === row.id} onClick={() => retry(row)}>Retry</Button></TableCell></TableRow>)}
      {!messages.length && <TableRow><TableCell colSpan={8} align="center" sx={{ py: 5 }}>پیامی در این وضعیت وجود ندارد.</TableCell></TableRow>}
    </TableBody></Table></TableContainer></CardContent></Card>
  </Stack>
}
