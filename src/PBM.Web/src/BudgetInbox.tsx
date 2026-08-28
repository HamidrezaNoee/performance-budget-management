import { useEffect, useState } from 'react'
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, Divider, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography } from '@mui/material'
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

type Comment = { id: string; versionId: string; userId: string; userDisplayName: string; text: string; createdAtUtc: string }

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
  const [commentItem, setCommentItem] = useState<InboxItem | null>(null)
  const [comments, setComments] = useState<Comment[]>([])
  const [commentText, setCommentText] = useState('')
  const [commentBusy, setCommentBusy] = useState(false)

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

  const loadComments = async (item: InboxItem) => {
    setCommentItem(item); setCommentText(''); setCommentBusy(true); setError('')
    try { const { data } = await api.get<Comment[]>(`/budget/versions/${item.versionId}/comments`); setComments(data) }
    catch (error) { setComments([]); setError(apiError(error)) }
    finally { setCommentBusy(false) }
  }

  const addComment = async () => {
    if (!commentItem || !commentText.trim()) return
    setCommentBusy(true); setError('')
    try {
      await api.post(`/budget/versions/${commentItem.versionId}/comments`, { text: commentText.trim() })
      setCommentText('')
      const { data } = await api.get<Comment[]>(`/budget/versions/${commentItem.versionId}/comments`)
      setComments(data)
    } catch (error) { setError(apiError(error)) }
    finally { setCommentBusy(false) }
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
          <Button size="small" variant="outlined" onClick={() => loadComments(item)}>نظرات</Button>
          {!item.canStartReview && !item.canApprove && !item.canReturn && !item.canReject && <Typography variant="caption" color="text.secondary" alignSelf="center">فقط مشاهده</Typography>}
        </Stack></TableCell></TableRow>)}
        {!items.length && !loading && <TableRow><TableCell colSpan={5} align="center" sx={{ py: 6 }}><Typography fontWeight={800}>کارتابل خالی است.</Typography><Typography variant="body2" color="text.secondary">در حال حاضر نسخه‌ای منتظر بررسی یا اصلاح نیست.</Typography></TableCell></TableRow>}
      </TableBody></Table></TableContainer>
    </CardContent></Card>

    <Dialog open={!!commentItem} onClose={() => !commentBusy && setCommentItem(null)} fullWidth maxWidth="sm">
      <DialogTitle>نظرات نسخه {commentItem?.versionNumber.toLocaleString('fa-IR')} — {commentItem?.versionName}</DialogTitle>
      <DialogContent>
        <Stack spacing={1.5} mt={1}>
          <TextField multiline minRows={3} label="نظر جدید" value={commentText} onChange={e => setCommentText(e.target.value)} disabled={commentBusy} />
          <Button variant="contained" onClick={addComment} disabled={commentBusy || !commentText.trim()}>ثبت نظر</Button>
          <Divider />
          {commentBusy && !comments.length && <Box py={3} textAlign="center"><CircularProgress size={24} /></Box>}
          {comments.map(comment => <Box key={comment.id} sx={{ p: 1.5, border: '1px solid #e8eef5', borderRadius: 2 }}><Stack direction="row" justifyContent="space-between" spacing={1}><Typography fontWeight={800}>{comment.userDisplayName}</Typography><Typography variant="caption" color="text.secondary">{faDateTime.format(new Date(comment.createdAtUtc))}</Typography></Stack><Typography variant="body2" mt={1} sx={{ whiteSpace: 'pre-wrap' }}>{comment.text}</Typography></Box>)}
          {!comments.length && !commentBusy && <Typography color="text.secondary" textAlign="center" py={2}>هنوز نظری برای این نسخه ثبت نشده است.</Typography>}
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={() => setCommentItem(null)} disabled={commentBusy}>بستن</Button></DialogActions>
    </Dialog>
  </Stack>
}
