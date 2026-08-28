import { useEffect, useState } from 'react'
import {
  Alert, Badge, Box, Button, Chip, CircularProgress, Dialog, DialogActions, DialogContent,
  DialogTitle, Divider, IconButton, Stack, Tooltip, Typography
} from '@mui/material'
import NotificationsRoundedIcon from '@mui/icons-material/NotificationsRounded'
import DoneAllRoundedIcon from '@mui/icons-material/DoneAllRounded'
import { api } from './api'

type Notification = {
  id: string
  companyId?: string | null
  category: string
  title: string
  message: string
  severity: number
  entityType?: string | null
  entityId?: string | null
  actionUrl?: string | null
  isRead: boolean
  createdAtUtc: string
  readAtUtc?: string | null
  expiresAtUtc?: string | null
}

const faDateTime = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { dateStyle: 'short', timeStyle: 'short' })

function severityLabel(severity: number) {
  if (severity === 1) return { label: 'موفق', color: 'success' as const }
  if (severity === 2) return { label: 'هشدار', color: 'warning' as const }
  if (severity === 3) return { label: 'مهم', color: 'error' as const }
  return { label: 'اطلاع', color: 'info' as const }
}

function apiError(error: unknown) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? 'دریافت اعلان‌ها ناموفق بود.'
  }
  return 'دریافت اعلان‌ها ناموفق بود.'
}

export default function NotificationCenter() {
  const [open, setOpen] = useState(false)
  const [items, setItems] = useState<Notification[]>([])
  const [unreadCount, setUnreadCount] = useState(0)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const loadCount = async () => {
    try {
      const { data } = await api.get<{ count: number }>('/notifications/unread-count')
      setUnreadCount(Math.max(0, data.count ?? 0))
    } catch {
      // Global API interceptor handles revoked sessions. A transient count failure should not interrupt the workspace.
    }
  }

  const loadItems = async () => {
    setLoading(true); setError('')
    try {
      const { data } = await api.get<Notification[]>('/notifications/', { params: { take: 100 } })
      setItems(data)
      setUnreadCount(data.filter(x => !x.isRead).length)
    } catch (error) { setError(apiError(error)) }
    finally { setLoading(false) }
  }

  useEffect(() => {
    loadCount()
    const timer = window.setInterval(loadCount, 60_000)
    return () => window.clearInterval(timer)
  }, [])

  useEffect(() => { if (open) loadItems() }, [open])

  const markRead = async (item: Notification) => {
    if (item.isRead) return
    try {
      await api.post(`/notifications/${item.id}/read`)
      setItems(current => current.map(x => x.id === item.id ? { ...x, isRead: true, readAtUtc: new Date().toISOString() } : x))
      setUnreadCount(current => Math.max(0, current - 1))
    } catch (error) { setError(apiError(error)) }
  }

  const markAllRead = async () => {
    setLoading(true); setError('')
    try {
      await api.post('/notifications/read-all')
      const readAtUtc = new Date().toISOString()
      setItems(current => current.map(x => ({ ...x, isRead: true, readAtUtc: x.readAtUtc ?? readAtUtc })))
      setUnreadCount(0)
    } catch (error) { setError(apiError(error)) }
    finally { setLoading(false) }
  }

  return <>
    <Tooltip title="اعلان‌ها">
      <IconButton color="inherit" onClick={() => setOpen(true)} aria-label="اعلان‌ها">
        <Badge badgeContent={unreadCount > 99 ? '99+' : unreadCount} color="error">
          <NotificationsRoundedIcon />
        </Badge>
      </IconButton>
    </Tooltip>

    <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="sm">
      <DialogTitle>
        <Stack direction="row" justifyContent="space-between" alignItems="center" spacing={1}>
          <Box><Typography variant="h6" fontWeight={900}>مرکز اعلان‌ها</Typography><Typography variant="caption" color="text.secondary">گردش تأیید بودجه و رویدادهای مهم سامانه</Typography></Box>
          <Chip size="small" label={`${unreadCount.toLocaleString('fa-IR')} خوانده‌نشده`} color={unreadCount ? 'primary' : 'default'} />
        </Stack>
      </DialogTitle>
      <DialogContent dividers sx={{ p: 0 }}>
        {error && <Alert severity="error" sx={{ m: 2 }}>{error}</Alert>}
        {loading && !items.length && <Box py={7} textAlign="center"><CircularProgress /></Box>}
        {!loading && !items.length && <Box py={7} textAlign="center"><Typography fontWeight={800}>اعلانی وجود ندارد.</Typography><Typography variant="body2" color="text.secondary" mt={1}>رویدادهای جدید گردش بودجه در این بخش نمایش داده می‌شوند.</Typography></Box>}
        <Stack divider={<Divider flexItem />}>
          {items.map(item => {
            const severity = severityLabel(item.severity)
            return <Box key={item.id} onClick={() => markRead(item)} sx={{ px: 2.5, py: 2, cursor: item.isRead ? 'default' : 'pointer', bgcolor: item.isRead ? 'transparent' : 'action.hover' }}>
              <Stack direction="row" justifyContent="space-between" spacing={1} alignItems="flex-start">
                <Box minWidth={0}>
                  <Stack direction="row" spacing={1} alignItems="center" mb={.7}>
                    <Typography fontWeight={item.isRead ? 700 : 900}>{item.title}</Typography>
                    {!item.isRead && <Box sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: 'primary.main', flex: '0 0 auto' }} />}
                  </Stack>
                  <Typography variant="body2" color="text.secondary" sx={{ whiteSpace: 'pre-wrap' }}>{item.message}</Typography>
                  <Typography variant="caption" color="text.secondary" display="block" mt={1}>{faDateTime.format(new Date(item.createdAtUtc))}</Typography>
                </Box>
                <Chip size="small" label={severity.label} color={severity.color} variant="outlined" />
              </Stack>
            </Box>
          })}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={() => setOpen(false)}>بستن</Button>
        <Button startIcon={<DoneAllRoundedIcon />} onClick={markAllRead} disabled={loading || unreadCount === 0}>خواندن همه</Button>
        <Button variant="outlined" onClick={loadItems} disabled={loading}>به‌روزرسانی</Button>
      </DialogActions>
    </Dialog>
  </>
}
