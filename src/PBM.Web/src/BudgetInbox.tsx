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
type Attachment = { id: string; versionId: string; commentId?: string | null; uploadedByUserId: string; uploadedByDisplayName: string; fileName: string; contentType: string; length: number; sha256: string; createdAtUtc: string }

const statusLabels = ['پیش‌نویس', 'ارسال‌شده', 'در حال بررسی', 'برگشت‌شده', 'تأییدشده', 'ردشده', 'اصلاح‌شده', 'بسته‌شده']
const faDateTime = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { dateStyle: 'short', timeStyle: 'short' })
const faNumber = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 1 })

function apiError(error: unknown) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; message?: string } } }).response
    if (response?.data?.detail) return response.data.detail
    if (response?.data?.message) return response.data.message
  }
  return 'عملیات کارتابل ناموفق بود.'
}

function formatBytes(value: number) {
  if (value >= 1024 * 1024) return `${faNumber.format(value / (1024 * 1024))} مگابایت`
  if (value >= 1024) return `${faNumber.format(value / 1024)} کیلوبایت`
  return `${value.toLocaleString('fa-IR')} بایت`
}

export default function BudgetInbox({ companyId }: { companyId: string }) {
  const [items, setItems] = useState<InboxItem[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [commentItem, setCommentItem] = useState<InboxItem | null>(null)
  const [comments, setComments] = useState<Comment[]>([])
  const [attachments, setAttachments] = useState<Attachment[]>([])
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

  const loadReviewDetails = async (item: InboxItem) => {
    setCommentItem(item); setCommentText(''); setCommentBusy(true); setError('')
    try {
      const [commentResponse, attachmentResponse] = await Promise.all([
        api.get<Comment[]>(`/budget/versions/${item.versionId}/comments`),
        api.get<Attachment[]>(`/budget/versions/${item.versionId}/attachments`)
      ])
      setComments(commentResponse.data); setAttachments(attachmentResponse.data)
    } catch (error) { setComments([]); setAttachments([]); setError(apiError(error)) }
    finally { setCommentBusy(false) }
  }

  const refreshReviewDetails = async () => {
    if (!commentItem) return
    const [commentResponse, attachmentResponse] = await Promise.all([
      api.get<Comment[]>(`/budget/versions/${commentItem.versionId}/comments`),
      api.get<Attachment[]>(`/budget/versions/${commentItem.versionId}/attachments`)
    ])
    setComments(commentResponse.data); setAttachments(attachmentResponse.data)
  }

  const addComment = async () => {
    if (!commentItem || !commentText.trim()) return
    setCommentBusy(true); setError('')
    try {
      await api.post(`/budget/versions/${commentItem.versionId}/comments`, { text: commentText.trim() })
      setCommentText('')
      await refreshReviewDetails()
    } catch (error) { setError(apiError(error)) }
    finally { setCommentBusy(false) }
  }

  const uploadAttachment = async (file: File) => {
    if (!commentItem) return
    if (file.size > 10 * 1024 * 1024) { setError('حداکثر حجم هر مستند ۱۰ مگابایت است.'); return }
    setCommentBusy(true); setError(''); setMessage('')
    try {
      const form = new FormData(); form.append('file', file)
      await api.post(`/budget/versions/${commentItem.versionId}/attachments`, form)
      await refreshReviewDetails()
      setMessage(`مستند «${file.name}» ثبت شد.`)
    } catch (error) { setError(apiError(error)) }
    finally { setCommentBusy(false) }
  }

  const downloadAttachment = async (attachment: Attachment) => {
    setCommentBusy(true); setError('')
    try {
      const response = await api.get(`/budget/attachments/${attachment.id}/content`, { responseType: 'blob' })
      const url = URL.createObjectURL(response.data as Blob)
      const link = document.createElement('a'); link.href = url; link.download = attachment.fileName; document.body.appendChild(link); link.click(); link.remove()
      URL.revokeObjectURL(url)
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
          <Button size="small" variant="outlined" onClick={() => loadReviewDetails(item)}>نظرات و مستندات</Button>
          {!item.canStartReview && !item.canApprove && !item.canReturn && !item.canReject && <Typography variant="caption" color="text.secondary" alignSelf="center">فقط مشاهده</Typography>}
        </Stack></TableCell></TableRow>)}
        {!items.length && !loading && <TableRow><TableCell colSpan={5} align="center" sx={{ py: 6 }}><Typography fontWeight={800}>کارتابل خالی است.</Typography><Typography variant="body2" color="text.secondary">در حال حاضر نسخه‌ای منتظر بررسی یا اصلاح نیست.</Typography></TableCell></TableRow>}
      </TableBody></Table></TableContainer>
    </CardContent></Card>

    <Dialog open={!!commentItem} onClose={() => !commentBusy && setCommentItem(null)} fullWidth maxWidth="md">
      <DialogTitle>نظرات و مستندات نسخه {commentItem?.versionNumber.toLocaleString('fa-IR')} — {commentItem?.versionName}</DialogTitle>
      <DialogContent>
        <Stack spacing={1.5} mt={1}>
          <Typography fontWeight={900}>ثبت نظر</Typography>
          <TextField multiline minRows={3} label="نظر جدید" value={commentText} onChange={e => setCommentText(e.target.value)} disabled={commentBusy} />
          <Button variant="contained" onClick={addComment} disabled={commentBusy || !commentText.trim()}>ثبت نظر</Button>
          <Divider />

          <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} spacing={1}>
            <Box><Typography fontWeight={900}>مستندات و شواهد</Typography><Typography variant="caption" color="text.secondary">PDF، Word، Excel، CSV، تصویر، متن و ZIP تا سقف ۱۰ مگابایت.</Typography></Box>
            <Button component="label" variant="outlined" disabled={commentBusy}>
              افزودن مستند
              <input hidden type="file" accept=".pdf,.doc,.docx,.xls,.xlsx,.csv,.txt,.png,.jpg,.jpeg,.zip" onChange={e => { const file = e.target.files?.[0]; e.currentTarget.value = ''; if (file) uploadAttachment(file) }} />
            </Button>
          </Stack>
          {attachments.map(attachment => <Box key={attachment.id} sx={{ p: 1.5, border: '1px solid #e8eef5', borderRadius: 2 }}><Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} spacing={1}><Box><Typography fontWeight={800}>{attachment.fileName}</Typography><Typography variant="caption" color="text.secondary">{formatBytes(attachment.length)} — {attachment.uploadedByDisplayName} — {faDateTime.format(new Date(attachment.createdAtUtc))}</Typography></Box><Button size="small" onClick={() => downloadAttachment(attachment)} disabled={commentBusy}>دریافت</Button></Stack></Box>)}
          {!attachments.length && !commentBusy && <Typography color="text.secondary" textAlign="center" py={1}>مستندی برای این نسخه ثبت نشده است.</Typography>}
          <Divider />

          <Typography fontWeight={900}>تاریخچه نظرات</Typography>
          {commentBusy && !comments.length && !attachments.length && <Box py={3} textAlign="center"><CircularProgress size={24} /></Box>}
          {comments.map(comment => <Box key={comment.id} sx={{ p: 1.5, border: '1px solid #e8eef5', borderRadius: 2 }}><Stack direction="row" justifyContent="space-between" spacing={1}><Typography fontWeight={800}>{comment.userDisplayName}</Typography><Typography variant="caption" color="text.secondary">{faDateTime.format(new Date(comment.createdAtUtc))}</Typography></Stack><Typography variant="body2" mt={1} sx={{ whiteSpace: 'pre-wrap' }}>{comment.text}</Typography></Box>)}
          {!comments.length && !commentBusy && <Typography color="text.secondary" textAlign="center" py={2}>هنوز نظری برای این نسخه ثبت نشده است.</Typography>}
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={() => setCommentItem(null)} disabled={commentBusy}>بستن</Button></DialogActions>
    </Dialog>
  </Stack>
}
