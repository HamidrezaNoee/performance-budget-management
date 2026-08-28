import { useEffect, useState } from 'react'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Typography } from '@mui/material'
import { api } from './api'

function apiError(error: unknown) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? 'تغییر رمز عبور ناموفق بود.'
  }
  return 'تغییر رمز عبور ناموفق بود.'
}

export default function ChangePasswordDialog({ open, onClose, onChanged }: { open: boolean; onClose: () => void; onChanged: () => void }) {
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [changed, setChanged] = useState(false)

  useEffect(() => {
    if (!open) {
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
      setError('')
      setMessage('')
      setChanged(false)
    }
  }, [open])

  const close = () => {
    if (busy) return
    if (changed) onChanged()
    else onClose()
  }

  const save = async () => {
    setError(''); setMessage('')
    if (!currentPassword) { setError('رمز عبور فعلی را وارد کنید.'); return }
    if (newPassword.length < 12 || !/[A-Z]/.test(newPassword) || !/[a-z]/.test(newPassword) || !/\d/.test(newPassword)) {
      setError('رمز جدید باید حداقل ۱۲ کاراکتر و شامل حرف بزرگ، حرف کوچک و عدد باشد.')
      return
    }
    if (newPassword !== confirmPassword) { setError('تکرار رمز عبور با رمز جدید یکسان نیست.'); return }
    if (newPassword === currentPassword) { setError('رمز جدید باید با رمز فعلی متفاوت باشد.'); return }

    setBusy(true)
    try {
      await api.post('/account/change-password', { currentPassword, newPassword })
      setCurrentPassword(''); setNewPassword(''); setConfirmPassword('')
      setChanged(true)
      setMessage('رمز عبور با موفقیت تغییر کرد. نشست فعلی برای امنیت باطل شده است؛ با رمز جدید دوباره وارد شوید.')
    } catch (error) { setError(apiError(error)) }
    finally { setBusy(false) }
  }

  return <Dialog open={open} onClose={close} fullWidth maxWidth="xs">
    <DialogTitle>تغییر رمز عبور</DialogTitle>
    <DialogContent>
      <Stack spacing={2} mt={1}>
        <Typography variant="body2" color="text.secondary">برای امنیت حساب، رمز جدید حداقل ۱۲ کاراکتر و شامل حرف بزرگ، حرف کوچک و عدد باشد.</Typography>
        {error && <Alert severity="error">{error}</Alert>}
        {message && <Alert severity="success">{message}</Alert>}
        {!changed && <>
          <TextField autoFocus type="password" label="رمز عبور فعلی" value={currentPassword} onChange={e => setCurrentPassword(e.target.value)} autoComplete="current-password" disabled={busy} />
          <TextField type="password" label="رمز عبور جدید" value={newPassword} onChange={e => setNewPassword(e.target.value)} autoComplete="new-password" disabled={busy} />
          <TextField type="password" label="تکرار رمز عبور جدید" value={confirmPassword} onChange={e => setConfirmPassword(e.target.value)} autoComplete="new-password" disabled={busy} onKeyDown={e => e.key === 'Enter' && save()} />
        </>}
      </Stack>
    </DialogContent>
    <DialogActions>
      {changed
        ? <Button variant="contained" onClick={onChanged}>ورود مجدد</Button>
        : <><Button onClick={onClose} disabled={busy}>بستن</Button><Button variant="contained" onClick={save} disabled={busy || !currentPassword || !newPassword || !confirmPassword}>تغییر رمز</Button></>}
    </DialogActions>
  </Dialog>
}
