import { useEffect, useState } from 'react'
import { Alert, Box, Button, Card, CardContent, CircularProgress, IconButton, Stack, TextField, Typography } from '@mui/material'
import LoginRoundedIcon from '@mui/icons-material/LoginRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import ShieldRoundedIcon from '@mui/icons-material/ShieldRounded'
import { api } from './api'
import type { AuthSession } from './authSession'

type LoginResponse = {
  accessToken: string
  displayName: string
  roles: string[]
  companyIds: string[]
  writableCompanyIds: string[]
}

type CaptchaResponse = { captchaId: string; challenge: string; expiresInSeconds: number }

const isLocalDevelopment = ['localhost', '127.0.0.1'].includes(window.location.hostname)

export default function LoginPage({ onLoggedIn }: { onLoggedIn: (session: AuthSession) => void }) {
  const [userName, setUserName] = useState(isLocalDevelopment ? 'admin' : '')
  const [password, setPassword] = useState('')
  const [captchaId, setCaptchaId] = useState('')
  const [captchaChallenge, setCaptchaChallenge] = useState('')
  const [captchaAnswer, setCaptchaAnswer] = useState('')
  const [captchaLoading, setCaptchaLoading] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const loadCaptcha = async () => {
    setCaptchaLoading(true)
    try {
      const { data } = await api.get<CaptchaResponse>('/auth/captcha', { headers: { 'Cache-Control': 'no-cache' } })
      setCaptchaId(data.captchaId)
      setCaptchaChallenge(data.challenge)
      setCaptchaAnswer('')
    } catch {
      setCaptchaId('')
      setCaptchaChallenge('')
      setError('دریافت کد امنیتی ناموفق بود. اتصال به سرور را بررسی کنید.')
    } finally {
      setCaptchaLoading(false)
    }
  }

  useEffect(() => { void loadCaptcha() }, [])

  const submit = async () => {
    if (!userName.trim() || !password) { setError('نام کاربری و رمز عبور الزامی است.'); return }
    if (!captchaId || !captchaAnswer.trim()) { setError('پاسخ کد امنیتی را وارد کنید.'); return }
    setBusy(true)
    setError('')
    try {
      const { data } = await api.post<LoginResponse>('/auth/login', { userName, password }, {
        headers: { 'X-PBM-Captcha-Id': captchaId, 'X-PBM-Captcha-Answer': captchaAnswer.trim() }
      })
      onLoggedIn(data)
    } catch (requestError: any) {
      const status = requestError?.response?.status
      const detail = requestError?.response?.data?.detail
      if (status === 400 && detail) setError(detail)
      else if (status === 429) setError('تعداد تلاش‌های ورود بیش از حد مجاز است. کمی صبر کنید و دوباره تلاش کنید.')
      else setError('ورود ناموفق بود. نام کاربری یا رمز عبور را بررسی کنید.')
      await loadCaptcha()
    } finally {
      setBusy(false)
    }
  }

  return <Box className="login-shell">
    <Box className="login-visual" aria-hidden="true"><Box className="login-visual-copy"><Typography className="login-visual-kicker">PBM Intelligence Platform</Typography><Typography variant="h4" fontWeight={900}>بودجه هوشمند، تصمیم دقیق</Typography><Typography>برنامه‌ریزی، پایش عملکرد و تحلیل مالی در یک محیط یکپارچه و چندشرکتی</Typography></Box></Box>
    <Box className="login-panel"><Card className="login-card" elevation={0}><CardContent sx={{ p: { xs: 3, sm: 4.5 } }}>
      <Box className="login-brand-row"><Box className="brand-mark">PBM</Box><Box><Typography variant="h5" fontWeight={900}>مدیریت بودجه و عملکرد</Typography><Typography className="login-subtitle">سامانه بودجه‌ریزی چندشرکتی و پایش عملکرد</Typography></Box></Box>
      {error && <Alert severity="error" className="login-alert">{error}</Alert>}
      <Stack spacing={2.1}>
        <TextField className="login-field" label="نام کاربری" value={userName} onChange={e => setUserName(e.target.value)} autoComplete="username" fullWidth />
        <TextField className="login-field" label="رمز عبور" type="password" value={password} onChange={e => setPassword(e.target.value)} autoComplete="current-password" fullWidth />
        <Box className="captcha-section"><Stack direction="row" alignItems="center" spacing={1} mb={1}><ShieldRoundedIcon fontSize="small" /><Typography fontWeight={800}>کد امنیتی</Typography><Typography variant="caption" className="captcha-expiry">اعتبار ۲ دقیقه</Typography></Stack><Box className="captcha-row"><Box className="captcha-challenge" dir="ltr">{captchaLoading ? <CircularProgress size={22} color="inherit" /> : (captchaChallenge || '—')}</Box><IconButton className="captcha-refresh" onClick={() => void loadCaptcha()} disabled={captchaLoading || busy} aria-label="دریافت کد امنیتی جدید"><RefreshRoundedIcon /></IconButton><TextField className="login-field captcha-answer" label="پاسخ" value={captchaAnswer} onChange={e => setCaptchaAnswer(e.target.value)} disabled={captchaLoading} inputProps={{ inputMode: 'numeric', dir: 'ltr' }} onKeyDown={e => e.key === 'Enter' && void submit()} /></Box></Box>
        <Button className="login-submit" variant="contained" size="large" startIcon={<LoginRoundedIcon />} onClick={() => void submit()} disabled={busy || captchaLoading || !captchaId}>{busy ? <CircularProgress size={24} color="inherit" /> : 'ورود به سامانه'}</Button>
      </Stack>
      <Stack direction="row" spacing={1} alignItems="center" justifyContent="center" mt={2.5} className="login-security-note"><ShieldRoundedIcon sx={{ fontSize: 16 }} /><Typography variant="caption">ورود امن با کپچای یک‌بارمصرف و نشست کنترل‌شده</Typography></Stack>
    </CardContent></Card></Box>
  </Box>
}
