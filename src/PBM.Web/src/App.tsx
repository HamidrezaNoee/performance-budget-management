import { useEffect, useMemo, useState } from 'react'
import {
  Alert, AppBar, Box, Button, Card, CardContent, CircularProgress, Container, Divider, Drawer,
  FormControl, IconButton, InputLabel, List, ListItemButton, ListItemIcon, ListItemText, MenuItem, Select,
  Stack, TextField, Toolbar, Typography
} from '@mui/material'
import DashboardRoundedIcon from '@mui/icons-material/DashboardRounded'
import FactCheckRoundedIcon from '@mui/icons-material/FactCheckRounded'
import AccountBalanceWalletRoundedIcon from '@mui/icons-material/AccountBalanceWalletRounded'
import LocalShippingRoundedIcon from '@mui/icons-material/LocalShippingRounded'
import ShoppingCartCheckoutRoundedIcon from '@mui/icons-material/ShoppingCartCheckoutRounded'
import PointOfSaleRoundedIcon from '@mui/icons-material/PointOfSaleRounded'
import ReceiptLongRoundedIcon from '@mui/icons-material/ReceiptLongRounded'
import RequestQuoteRoundedIcon from '@mui/icons-material/RequestQuoteRounded'
import SwapHorizRoundedIcon from '@mui/icons-material/SwapHorizRounded'
import UploadFileRoundedIcon from '@mui/icons-material/UploadFileRounded'
import InsightsRoundedIcon from '@mui/icons-material/InsightsRounded'
import DifferenceRoundedIcon from '@mui/icons-material/DifferenceRounded'
import AutoGraphRoundedIcon from '@mui/icons-material/AutoGraphRounded'
import PaymentsRoundedIcon from '@mui/icons-material/PaymentsRounded'
import BusinessCenterRoundedIcon from '@mui/icons-material/BusinessCenterRounded'
import AssessmentRoundedIcon from '@mui/icons-material/AssessmentRounded'
import SyncAltRoundedIcon from '@mui/icons-material/SyncAltRounded'
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded'
import LockResetRoundedIcon from '@mui/icons-material/LockResetRounded'
import LogoutRoundedIcon from '@mui/icons-material/LogoutRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import ShieldRoundedIcon from '@mui/icons-material/ShieldRounded'
import LoginRoundedIcon from '@mui/icons-material/LoginRounded'
import { api, clearClientSession, setAccessToken } from './api'
import BudgetInbox from './BudgetInbox'
import BudgetPlanning from './BudgetPlanning'
import TradeSupplyChain from './TradeSupplyChain'
import PurchaseForecastPlanner from './PurchaseForecastPlanner'
import SalesPlanner from './SalesPlanner'
import ExpensePlanner from './ExpensePlanner'
import BudgetReservations from './BudgetReservations'
import BudgetTransfers from './BudgetTransfers'
import WorkbookImport from './WorkbookImport'
import KpiPerformance from './KpiPerformance'
import VarianceAnalysis from './VarianceAnalysis'
import Forecasting from './Forecasting'
import CashPlanning from './CashPlanning'
import CapexProjects from './CapexProjects'
import FinancialReports from './FinancialReports'
import ActualLedgerWorkspace from './ActualLedgerWorkspace'
import ReferenceAdmin from './ReferenceAdmin'
import ChangePasswordDialog from './ChangePasswordDialog'
import NotificationCenter from './NotificationCenter'
import ExecutiveDashboard from './ExecutiveDashboard'

type Company = { id: string; tenantId: string; code: string; name: string; industry?: string }
type FiscalYear = { id: string; code: string; name: string; jalaliYear: number }
type LoginResponse = { accessToken: string; displayName: string; roles: string[]; companyIds: string[]; writableCompanyIds: string[] }
type CaptchaResponse = { captchaId: string; challenge: string; expiresInSeconds: number }

const drawerWidth = 236
const isLocalDevelopment = ['localhost', '127.0.0.1'].includes(window.location.hostname)
const viewHashes = ['dashboard', 'inbox', 'budget', 'trade', 'purchase-forecast', 'sales', 'expenses', 'reservations', 'transfers', 'imports', 'kpi', 'variance', 'forecast', 'cash', 'capex', 'reports', 'actuals', 'settings'] as const

function viewIndexFromHash() {
  const hash = window.location.hash.replace(/^#/, '').toLowerCase()
  const index = viewHashes.findIndex(x => x === hash)
  return index >= 0 ? index : 0
}

function readStoredArray(key: string): string[] {
  try { return JSON.parse(localStorage.getItem(key) ?? '[]') as string[] }
  catch { return [] }
}

export default function App() {
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('pbm_token'))
  const [displayName, setDisplayName] = useState(() => localStorage.getItem('pbm_display_name') ?? '')
  const [roles, setRoles] = useState<string[]>(() => readStoredArray('pbm_roles'))
  const [writableCompanyIds, setWritableCompanyIds] = useState<string[]>(() => readStoredArray('pbm_writable_company_ids'))
  useEffect(() => { setAccessToken(token) }, [token])

  const logout = () => {
    clearClientSession()
    setToken(null); setDisplayName(''); setRoles([]); setWritableCompanyIds([])
  }

  if (!token) return <Login onLoggedIn={response => {
    localStorage.setItem('pbm_token', response.accessToken)
    localStorage.setItem('pbm_display_name', response.displayName)
    localStorage.setItem('pbm_roles', JSON.stringify(response.roles))
    localStorage.setItem('pbm_writable_company_ids', JSON.stringify(response.writableCompanyIds))
    setAccessToken(response.accessToken)
    setToken(response.accessToken); setDisplayName(response.displayName); setRoles(response.roles); setWritableCompanyIds(response.writableCompanyIds)
  }} />

  return <Workspace displayName={displayName} roles={roles} writableCompanyIds={writableCompanyIds} onLogout={logout} />
}

function Login({ onLoggedIn }: { onLoggedIn: (response: LoginResponse) => void }) {
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
      setCaptchaId(''); setCaptchaChallenge('')
      setError('دریافت کد امنیتی ناموفق بود. اتصال به سرور را بررسی کنید.')
    } finally { setCaptchaLoading(false) }
  }

  useEffect(() => { void loadCaptcha() }, [])

  const submit = async () => {
    if (!userName.trim() || !password) { setError('نام کاربری و رمز عبور الزامی است.'); return }
    if (!captchaId || !captchaAnswer.trim()) { setError('پاسخ کد امنیتی را وارد کنید.'); return }
    setBusy(true); setError('')
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
    } finally { setBusy(false) }
  }

  return <Box className="login-shell">
    <Box className="login-visual" aria-hidden="true"><Box className="login-visual-copy"><Typography className="login-visual-kicker">PBM Intelligence Platform</Typography><Typography variant="h4" fontWeight={900}>بودجه هوشمند، تصمیم دقیق</Typography><Typography>برنامه‌ریزی، پایش عملکرد و تحلیل مالی در یک محیط یکپارچه و چندشرکتی</Typography></Box></Box>
    <Box className="login-panel"><Card className="login-card" elevation={0}><CardContent sx={{ p: { xs: 3, sm: 4.5 } }}>
      <Box className="login-brand-row"><Box className="brand-mark">PBM</Box><Box><Typography variant="h5" fontWeight={900}>مدیریت بودجه و عملکرد</Typography><Typography className="login-subtitle">سامانه بودجه‌ریزی چندشرکتی و پایش عملکرد</Typography></Box></Box>
      {error && <Alert severity="error" className="login-alert">{error}</Alert>}
      <Stack spacing={2.1}>
        <TextField className="login-field" label="نام کاربری" value={userName} onChange={e => setUserName(e.target.value)} autoComplete="username" fullWidth />
        <TextField className="login-field" label="رمز عبور" type="password" value={password} onChange={e => setPassword(e.target.value)} autoComplete="current-password" fullWidth />
        <Box className="captcha-section"><Stack direction="row" alignItems="center" spacing={1} mb={1}><ShieldRoundedIcon fontSize="small" /><Typography fontWeight={800}>کد امنیتی</Typography><Typography variant="caption" className="captcha-expiry">اعتبار ۲ دقیقه</Typography></Stack><Box className="captcha-row"><Box className="captcha-challenge" dir="ltr">{captchaLoading ? <CircularProgress size={22} color="inherit" /> : (captchaChallenge || '—')}</Box><IconButton className="captcha-refresh" onClick={() => void loadCaptcha()} disabled={captchaLoading || busy} aria-label="دریافت کد امنیتی جدید"><RefreshRoundedIcon /></IconButton><TextField className="login-field captcha-answer" label="پاسخ" value={captchaAnswer} onChange={e => setCaptchaAnswer(e.target.value)} disabled={captchaLoading} inputProps={{ inputMode: 'numeric', dir: 'ltr' }} onKeyDown={e => e.key === 'Enter' && submit()} /></Box></Box>
        <Button className="login-submit" variant="contained" size="large" startIcon={<LoginRoundedIcon />} onClick={submit} disabled={busy || captchaLoading || !captchaId}>{busy ? <CircularProgress size={24} color="inherit" /> : 'ورود به سامانه'}</Button>
      </Stack>
      <Stack direction="row" spacing={1} alignItems="center" justifyContent="center" mt={2.5} className="login-security-note"><ShieldRoundedIcon sx={{ fontSize: 16 }} /><Typography variant="caption">ورود امن با کپچای یک‌بارمصرف و نشست کنترل‌شده</Typography></Stack>
    </CardContent></Card></Box>
  </Box>
}

function Workspace({ displayName, roles, writableCompanyIds, onLogout }: { displayName: string; roles: string[]; writableCompanyIds: string[]; onLogout: () => void }) {
  const [companies, setCompanies] = useState<Company[]>([])
  const [years, setYears] = useState<FiscalYear[]>([])
  const [companyId, setCompanyId] = useState('')
  const [yearId, setYearId] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [activeView, setActiveView] = useState(viewIndexFromHash)
  const [passwordDialogOpen, setPasswordDialogOpen] = useState(false)

  useEffect(() => { const onHashChange = () => setActiveView(viewIndexFromHash()); window.addEventListener('hashchange', onHashChange); return () => window.removeEventListener('hashchange', onHashChange) }, [])
  const selectView = (index: number) => { setActiveView(index); const hash = viewHashes[index] ?? viewHashes[0]; if (window.location.hash !== `#${hash}`) window.location.hash = hash }

  const loadCompanies = async () => {
    try {
      const { data } = await api.get<Company[]>('/companies')
      setCompanies(data)
      setCompanyId(current => {
        const remembered = localStorage.getItem('pbm_selected_company_id') ?? ''
        const next = data.some(x => x.id === current) ? current : data.some(x => x.id === remembered) ? remembered : data[0]?.id ?? ''
        if (next) localStorage.setItem('pbm_selected_company_id', next)
        else localStorage.removeItem('pbm_selected_company_id')
        return next
      })
    } catch { setError('دریافت فهرست شرکت‌ها ناموفق بود.') }
  }

  const loadYears = async (targetCompanyId: string) => {
    if (!targetCompanyId) { setYears([]); setYearId(''); return }
    try {
      const { data } = await api.get<FiscalYear[]>('/reference/fiscal-years', { params: { companyId: targetCompanyId } })
      setYears(data)
      setYearId(current => {
        const storageKey = `pbm_selected_fiscal_year_id:${targetCompanyId}`
        const remembered = localStorage.getItem(storageKey) ?? ''
        const next = data.some(x => x.id === current) ? current : data.some(x => x.id === remembered) ? remembered : data[0]?.id ?? ''
        if (next) localStorage.setItem(storageKey, next)
        else localStorage.removeItem(storageKey)
        return next
      })
    } catch { setYears([]); setYearId(''); setError('دریافت سال مالی ناموفق بود.') }
  }

  const refreshWorkspaceData = async () => {
    setError('')
    await loadCompanies()
    if (companyId) await loadYears(companyId)
  }

  useEffect(() => {
    setLoading(true)
    void loadCompanies().finally(() => setLoading(false))
    const refresh = () => { void loadCompanies() }
    window.addEventListener('pbm:workspace-data-changed', refresh)
    return () => window.removeEventListener('pbm:workspace-data-changed', refresh)
  }, [])

  useEffect(() => {
    if (!companyId) { setYears([]); setYearId(''); return }
    localStorage.setItem('pbm_selected_company_id', companyId)
    void loadYears(companyId)
    const refresh = () => { void loadYears(companyId) }
    window.addEventListener('pbm:workspace-data-changed', refresh)
    return () => window.removeEventListener('pbm:workspace-data-changed', refresh)
  }, [companyId])

  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const canWriteCompany = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || writableCompanyIds.includes(companyId)
  const showCompanySelector = companies.length > 1
  const menu = [
    ['داشبورد', <DashboardRoundedIcon />], ['کارتابل تأیید', <FactCheckRoundedIcon />], ['مدیریت بودجه', <AccountBalanceWalletRoundedIcon />],
    ['زنجیره خرید، واردات و فروش', <LocalShippingRoundedIcon />], ['بودجه و Forecast خرید', <ShoppingCartCheckoutRoundedIcon />],
    ['بودجه و Forecast فروش', <PointOfSaleRoundedIcon />], ['هزینه‌ها و مراکز هزینه', <ReceiptLongRoundedIcon />],
    ['رزرو و تعهدات', <RequestQuoteRoundedIcon />], ['جابجایی بودجه', <SwapHorizRoundedIcon />], ['ورود اطلاعات اکسل', <UploadFileRoundedIcon />],
    ['عملکرد و KPI', <InsightsRoundedIcon />], ['تحلیل انحراف', <DifferenceRoundedIcon />], ['پیش‌بینی', <AutoGraphRoundedIcon />],
    ['نقدینگی و خزانه‌داری', <PaymentsRoundedIcon />], ['پروژه‌های سرمایه‌ای', <BusinessCenterRoundedIcon />], ['گزارش‌ها', <AssessmentRoundedIcon />],
    ['Actual و اتصال ERP', <SyncAltRoundedIcon />], ['تنظیمات و داده‌های پایه', <SettingsRoundedIcon />]
  ] as const
  const titles = [
    'داشبورد مدیریت بودجه', 'کارتابل بررسی و تأیید بودجه', 'برنامه‌ریزی و ورود بودجه',
    'خرید از مبدا، واردات، تحویل انبار و فروش', 'بودجه و پیش‌بینی خرید تعدادی، مبلغی و هزینه‌ها',
    'بودجه و پیش‌بینی فروش تعدادی و مبلغی', 'حقوق، هزینه‌ها و مراکز هزینه',
    'رزرو بودجه و مدیریت تعهدات', 'جابجایی و بازتخصیص بودجه', 'ورود و نگاشت اکسل',
    'عملکرد و KPI', 'تحلیل انحراف بودجه و عملکرد', 'پیش‌بینی', 'برنامه نقدینگی و خزانه‌داری',
    'پروژه‌های سرمایه‌ای و CAPEX', 'گزارش‌های مالی و مدیریتی', 'دفتر Actual و اتصال ERP / حسابداری', 'تنظیمات و داده‌های پایه'
  ]
  const writeSensitiveView = (activeView >= 1 && activeView <= 14) || activeView === 16

  return <>
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <AppBar position="fixed" elevation={0} sx={{ width: `calc(100% - ${drawerWidth}px)`, mr: `${drawerWidth}px`, bgcolor: '#071a2f' }}><Toolbar><Typography fontWeight={800} flexGrow={1}>Performance Budget Management</Typography><Stack direction="row" spacing={1} alignItems="center"><NotificationCenter /><Typography variant="caption" sx={{ opacity: .75 }}>{roles.join('، ')}</Typography><Typography variant="body2">{displayName}</Typography></Stack></Toolbar></AppBar>
      <Drawer variant="permanent" anchor="right" sx={{ width: drawerWidth, flexShrink: 0, '& .MuiDrawer-paper': { width: drawerWidth, boxSizing: 'border-box', bgcolor: '#0b2038', color: '#dce8f7', border: 0 } }}>
        <Box sx={{ p: 2.5 }}><Typography variant="h6" fontWeight={900}>PBM</Typography><Typography variant="caption" sx={{ opacity: .7 }}>بودجه و عملکرد سازمانی</Typography></Box><Divider sx={{ borderColor: 'rgba(255,255,255,.1)' }} />
        <List sx={{ px: 1 }}>{menu.map(([label, icon], index) => <ListItemButton key={label} selected={index === activeView} onClick={() => selectView(index)} sx={{ borderRadius: 2, mb: .5, '&.Mui-selected': { bgcolor: 'rgba(56,139,253,.18)' } }}><ListItemIcon sx={{ color: 'inherit', minWidth: 40 }}>{icon}</ListItemIcon><ListItemText primary={label} /></ListItemButton>)}</List>
        <Box flexGrow={1} /><List sx={{ p: 1 }}><ListItemButton onClick={() => setPasswordDialogOpen(true)} sx={{ borderRadius: 2 }}><ListItemIcon sx={{ color: 'inherit', minWidth: 40 }}><LockResetRoundedIcon /></ListItemIcon><ListItemText primary="تغییر رمز عبور" /></ListItemButton><ListItemButton onClick={onLogout} sx={{ borderRadius: 2 }}><ListItemIcon sx={{ color: 'inherit', minWidth: 40 }}><LogoutRoundedIcon /></ListItemIcon><ListItemText primary="خروج" /></ListItemButton></List>
      </Drawer>
      <Box component="main" sx={{ flexGrow: 1, pt: 11, pb: 5, minWidth: 0 }}><Container maxWidth="xl">
        <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2} mb={3}>
          <Box><Typography variant="h4" fontWeight={900}>{titles[activeView]}</Typography></Box>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
            {showCompanySelector && <FormControl size="small" sx={{ minWidth: 220 }}><InputLabel>شرکت</InputLabel><Select value={companyId} label="شرکت" displayEmpty onChange={e => { const value = e.target.value; setCompanyId(value); if (value) localStorage.setItem('pbm_selected_company_id', value) }}><MenuItem value="" disabled><em>انتخاب کنید</em></MenuItem>{companies.map(x => <MenuItem key={x.id} value={x.id}>{x.name} — {x.code}</MenuItem>)}</Select></FormControl>}
            <FormControl size="small" sx={{ minWidth: 190 }}><InputLabel>سال مالی</InputLabel><Select value={yearId} label="سال مالی" displayEmpty disabled={!companyId} onChange={e => { const value = e.target.value; setYearId(value); if (companyId && value) localStorage.setItem(`pbm_selected_fiscal_year_id:${companyId}`, value) }}><MenuItem value="" disabled><em>{years.length ? 'انتخاب کنید' : 'سال مالی تعریف نشده'}</em></MenuItem>{years.map(x => <MenuItem key={x.id} value={x.id}>{x.name} — {x.jalaliYear}</MenuItem>)}</Select></FormControl>
            <Button variant="outlined" startIcon={<RefreshRoundedIcon />} onClick={() => void refreshWorkspaceData()} disabled={loading}>بازخوانی</Button>
          </Stack>
        </Stack>
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        {!loading && companies.length === 0 && <Alert severity="warning" sx={{ mb: 2 }}>هیچ شرکت فعالی برای این کاربر در دسترس نیست. از تنظیمات، شرکت را تعریف یا دسترسی کاربر را بررسی کنید.</Alert>}
        {!loading && companyId && years.length === 0 && <Alert severity="info" sx={{ mb: 2 }}>برای شرکت انتخاب‌شده هنوز سال مالی تعریف نشده است. از «تنظیمات و داده‌های پایه ← تقویم مالی» سال مالی را ایجاد کنید.</Alert>}
        {writeSensitiveView && companyId && !canWriteCompany && <Alert severity="warning" sx={{ mb: 2 }}>دسترسی شما برای این شرکت فقط خواندنی است. عملیات ثبت/ارسال/تأیید انجام نخواهد شد.</Alert>}
        {loading && <Box display="flex" justifyContent="center" py={8}><CircularProgress /></Box>}
        {!loading && companyId && yearId && activeView === 0 && <ExecutiveDashboard companyId={companyId} fiscalYearId={yearId} />}
        {!loading && companyId && activeView === 1 && <BudgetInbox companyId={companyId} />}
        {!loading && companyId && yearId && activeView === 2 && <BudgetPlanning companyId={companyId} fiscalYearId={yearId} />}
        {!loading && companyId && yearId && activeView === 3 && <TradeSupplyChain companyId={companyId} fiscalYearId={yearId} canWrite={canWriteCompany} />}
        {!loading && companyId && yearId && activeView === 4 && <PurchaseForecastPlanner companyId={companyId} fiscalYearId={yearId} canWrite={canWriteCompany} />}
        {!loading && companyId && yearId && activeView === 5 && <SalesPlanner companyId={companyId} fiscalYearId={yearId} canWrite={canWriteCompany} />}
        {!loading && companyId && yearId && activeView === 6 && <ExpensePlanner companyId={companyId} fiscalYearId={yearId} canWrite={canWriteCompany} />}
        {!loading && companyId && yearId && activeView === 7 && <BudgetReservations companyId={companyId} fiscalYearId={yearId} roles={roles} />}
        {!loading && companyId && yearId && activeView === 8 && <BudgetTransfers companyId={companyId} fiscalYearId={yearId} roles={roles} />}
        {!loading && companyId && yearId && activeView === 9 && <WorkbookImport companyId={companyId} fiscalYearId={yearId} />}
        {!loading && companyId && yearId && activeView === 10 && <KpiPerformance companyId={companyId} fiscalYearId={yearId} />}
        {!loading && companyId && yearId && activeView === 11 && <VarianceAnalysis companyId={companyId} fiscalYearId={yearId} />}
        {!loading && companyId && yearId && activeView === 12 && <Forecasting companyId={companyId} fiscalYearId={yearId} />}
        {!loading && companyId && yearId && activeView === 13 && <CashPlanning companyId={companyId} fiscalYearId={yearId} canWrite={canWriteCompany} roles={roles} />}
        {!loading && companyId && yearId && activeView === 14 && <CapexProjects companyId={companyId} fiscalYearId={yearId} canWrite={canWriteCompany} roles={roles} />}
        {!loading && companyId && yearId && activeView === 15 && <FinancialReports companyId={companyId} fiscalYearId={yearId} />}
        {!loading && companyId && yearId && activeView === 16 && <ActualLedgerWorkspace companyId={companyId} fiscalYearId={yearId} canWrite={canWriteCompany} roles={roles} />}
        {!loading && activeView === 17 && <ReferenceAdmin companyId={companyId} roles={roles} />}
      </Container></Box>
    </Box>
    <ChangePasswordDialog open={passwordDialogOpen} onClose={() => setPasswordDialogOpen(false)} />
  </>
}
