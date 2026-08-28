import { useEffect, useState } from 'react'
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import { api } from './api'

type InboxItem = {
  versionId: string
  budgetPlanId: string
  companyId: string
  companyName: string
  fiscalYearId: string
  fiscalYearName: string
  budgetModelId: string
  budgetModelName: string
  versionNumber: number
  versionName: string
  status: number
  isLocked: boolean
  updatedAtUtc: string
  canStartReview: boolean
  canApprove: boolean
  canReturn: boolean
  canReject: boolean
}

const statusLabels = ['پیش‌نویس', 'ارسال‌شده', 'در حال بررسی', 'برگشت‌شده', 'تأییدشده', 'ردشده', 'اصلاح‌شده', 'بسته‌شده']
const faDateTime = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { dateStyle: 'short', timeStyle: 'short' })

function apiError(error: unknown) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string } } }).response
    if (response?.data?.detail) return response.data.detail
  }
  return 'عملیات کارتابل ناموفق بود.'
}

export default function BudgetInbox({ companyId }: { companyId: string }) {
  const [items, setItems] = useState<InboxItem[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  const reload = async () => {
    if (!companyId) return
    setLoading(true); setError('')
    try { const { data } = await api.get<InboxItem[]>('/workflow/inbox', { params: { companyId } }); setItems(data) }
    catch (error) { setError(apiError(error)) }
    finally { setLoading(false) }
  }

  useEffect(() => { reload() }, [companyId])

  const transition = async (item: InboxItem, status: number, requiresComment: boolean, actionName: string) => {
    const comment = requiresComment ? window.prompt(`توضیح ${actionName} را وارد کنید:`) : window.prompt('توضیح اختیاری:')
    if (requiresComment && !comment?.trim()) return
    setLoading(true); setError(''); setMessage('')
    try {
      await api.post(`/budget/versions/${item.versionId}/status`, { status, comment: comment?.trim() || null })
      setMessage(`${actionName} برای «${item.versionName}» انجام شد.`)
      await reload()
    } catch (error) { setError(apiError(error)) }
    finally { setLoading(false) }
  }

  return <Stack spacing={2.5}>
    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2}>
        <Box><Typography variant="h6" fontWeight={900}>کارتابل بررسی و تأیید بودجه</Typography><Typography color="text.secondary">نسخه‌های ارسال‌شده، در حال بررسی و برگشت‌شده بر اساس نقش و دسترسی شرکت نمایش داده می‌شوند.</Typography></Box>
        <Button variant="outlined" onClick={reload} disabled={loading}>به‌روزرسانی</Button>
      </Stack>
    </CardContent></Card>

    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    {message && <Alert severity="success" onClose={() => setMessage('')}>{message}</Alert>}
    {loading && !items.length && <Box py={6} textAlign="center"><CircularProgress /></Box>}

    <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <TableContainer><Table size="small"><TableHead><TableRow><TableCell>مدل / نسخه</TableCell><TableCell>سال مالی</TableCell><TableCell>وضعیت</TableCell><TableCell>آخرین تغییر</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>
        {items.map(item => <TableRow key={item.versionId} hover><TableCell><Typography fontWeight={900}>{item.budgetModelName}</Typography><Typography variant="body2">نسخه {item.versionNumber.toLocaleString('fa-IR')} — {item.versionName}</Typography><Typography variant="caption" color="text.secondary">{item.companyName}</Typography></TableCell><TableCell>{item.fiscalYearName}</TableCell><TableCell><Chip size="small" label={statusLabels[item.status] ?? 'نامشخص'} color={item.status === 2 ? 'warning' : item.status === 3 ? 'error' : 'info'} /></TableCell><TableCell sx={{ whiteSpace: 'nowrap' }}>{faDateTime.format(new Date(item.updatedAtUtc))}</TableCell><TableCell><Stack direction="row" spacing={.7} flexWrap="wrap" useFlexGap>
          {item.canStartReview && <Button size="small" variant="contained" onClick={() => transition(item, 2, false, 'شروع بررسی')}>شروع بررسی</Button>}
          {item.canApprove && <Button size="small" variant="contained" color="success" onClick={() => transition(item, 4, false, 'تأیید')}>تأیید</Button>}
          {item.canReturn && <Button size="small" color="warning" onClick={() => transition(item, 3, true, 'برگشت')}>برگشت</Button>}
          {item.canReject && <Button size="small" color="error" onClick={() => transition(item, 5, true, 'رد')}>رد</Button>}
          {!item.canStartReview && !item.canApprove && !item.canReturn && !item.canReject && <Typography variant="caption" color="text.secondary">برای شما اقدام مستقیمی ندارد.</Typography>}
        </Stack></TableCell></TableRow>)}
        {!items.length && !loading && <TableRow><TableCell colSpan={5} align="center" sx={{ py: 6 }}><Typography fontWeight={800}>کارتابل خالی است.</Typography><Typography variant="body2" color="text.secondary">در حال حاضر نسخه‌ای منتظر بررسی یا اصلاح نیست.</Typography></TableCell></TableRow>}
      </TableBody></Table></TableContainer>
    </CardContent></Card>
  </Stack>
}
