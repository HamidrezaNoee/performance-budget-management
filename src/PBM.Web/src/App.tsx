import { useEffect, useMemo, useState } from 'react'
import {
  Alert, AppBar, Box, Button, Card, CardContent, CircularProgress, Container, Divider, Drawer,
  FormControl, InputLabel, List, ListItemButton, ListItemIcon, ListItemText, MenuItem, Select,
  Stack, TextField, Toolbar, Typography
} from '@mui/material'
import DashboardRoundedIcon from '@mui/icons-material/DashboardRounded'
import AccountBalanceWalletRoundedIcon from '@mui/icons-material/AccountBalanceWalletRounded'
import UploadFileRoundedIcon from '@mui/icons-material/UploadFileRounded'
import InsightsRoundedIcon from '@mui/icons-material/InsightsRounded'
import AutoGraphRoundedIcon from '@mui/icons-material/AutoGraphRounded'
import AssessmentRoundedIcon from '@mui/icons-material/AssessmentRounded'
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded'
import LogoutRoundedIcon from '@mui/icons-material/LogoutRounded'
import { Bar, CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api, setAccessToken } from './api'
import BudgetPlanning from './BudgetPlanning'
import WorkbookImport from './WorkbookImport'
import KpiPerformance from './KpiPerformance'
import Forecasting from './Forecasting'
import FinancialReports from './FinancialReports'
import ReferenceAdmin from './ReferenceAdmin'

type Company = { id: string; tenantId: string; code: string; name: string; industry?: string }
type FiscalYear = { id: string; code: string; name: string; jalaliYear: number }
type MonthlyPoint = { periodId: string; periodName: string; sequence: number; budget: number; actual: number; commitment: number; forecast: number }
type DashboardSummary = { budget: number; actual: number; commitment: number; forecast: number; remaining: number; variance: number; budgetUtilizationPercent: number; monthly: MonthlyPoint[] }
type LoginResponse = { accessToken: string; displayName: string; roles: string[]; companyIds: string[] }

const money = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 0 })
const drawerWidth = 236

function formatAmount(value: number) {
  const abs = Math.abs(value)
  if (abs >= 1_000_000_000_000) return `${money.format(value / 1_000_000_000_000)} همت`
  if (abs >= 1_000_000_000) return `${money.format(value / 1_000_000_000)} میلیارد`
  if (abs >= 1_000_000) return `${money.format(value / 1_000_000)} میلیون`
  return money.format(value)
}

export default function App() {
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('pbm_token'))
  const [displayName, setDisplayName] = useState(() => localStorage.getItem('pbm_display_name') ?? '')
  useEffect(() => { setAccessToken(token) }, [token])

  const logout = () => {
    localStorage.removeItem('pbm_token'); localStorage.removeItem('pbm_display_name')
    setAccessToken(null); setToken(null); setDisplayName('')
  }

  if (!token) return <Login onLoggedIn={response => {
    localStorage.setItem('pbm_token', response.accessToken)
    localStorage.setItem('pbm_display_name', response.displayName)
    setToken(response.accessToken); setDisplayName(response.displayName)
  }} />

  return <Workspace displayName={displayName} onLogout={logout} />
}

function Login({ onLoggedIn }: { onLoggedIn: (response: LoginResponse) => void }) {
  const [userName, setUserName] = useState('admin')
  const [password, setPassword] = useState('ChangeMe123!')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const submit = async () => {
    setBusy(true); setError('')
    try { const { data } = await api.post<LoginResponse>('/auth/login', { userName, password }); onLoggedIn(data) }
    catch { setError('ورود ناموفق بود. نام کاربری یا رمز عبور را بررسی کنید.') }
    finally { setBusy(false) }
  }

  return <Box className="login-shell"><Card className="login-card" elevation={0}><CardContent sx={{ p: 4 }}>
    <Box className="brand-mark">PBM</Box>
    <Typography variant="h5" fontWeight={800} mt={2}>مدیریت بودجه و عملکرد</Typography>
    <Typography color="text.secondary" mt={1} mb={3}>سامانه بودجه‌ریزی چندشرکتی و پایش عملکرد</Typography>
    {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    <Stack spacing={2}>
      <TextField label="نام کاربری" value={userName} onChange={e => setUserName(e.target.value)} fullWidth />
      <TextField label="رمز عبور" type="password" value={password} onChange={e => setPassword(e.target.value)} fullWidth onKeyDown={e => e.key === 'Enter' && submit()} />
      <Button variant="contained" size="large" onClick={submit} disabled={busy}>{busy ? <CircularProgress size={24} color="inherit" /> : 'ورود به سامانه'}</Button>
    </Stack>
    <Typography variant="caption" color="text.secondary" display="block" mt={2}>کاربر توسعه: admin / ChangeMe123!</Typography>
  </CardContent></Card></Box>
}

function Workspace({ displayName, onLogout }: { displayName: string; onLogout: () => void }) {
  const [companies, setCompanies] = useState<Company[]>([])
  const [years, setYears] = useState<FiscalYear[]>([])
  const [companyId, setCompanyId] = useState('')
  const [yearId, setYearId] = useState('')
  const [summary, setSummary] = useState<DashboardSummary | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [activeView, setActiveView] = useState(0)

  useEffect(() => {
    api.get<Company[]>('/companies').then(response => {
      setCompanies(response.data); if (response.data.length) setCompanyId(response.data[0].id)
    }).catch(() => setError('دریافت فهرست شرکت‌ها ناموفق بود.')).finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    if (!companyId) return
    setYearId(''); setSummary(null)
    api.get<FiscalYear[]>('/reference/fiscal-years', { params: { companyId } }).then(response => {
      setYears(response.data); if (response.data.length) setYearId(response.data[0].id)
    }).catch(() => setError('دریافت سال مالی ناموفق بود.'))
  }, [companyId])

  useEffect(() => {
    if (!companyId || !yearId) return
    setLoading(true)
    api.get<DashboardSummary>('/dashboard/summary', { params: { companyId, fiscalYearId: yearId } })
      .then(response => setSummary(response.data)).catch(() => setError('بارگذاری داشبورد ناموفق بود.')).finally(() => setLoading(false))
  }, [companyId, yearId])

  const selectedCompany = useMemo(() => companies.find(x => x.id === companyId), [companies, companyId])
  const menu = [
    ['داشبورد', <DashboardRoundedIcon />], ['مدیریت بودجه', <AccountBalanceWalletRoundedIcon />],
    ['ورود اطلاعات اکسل', <UploadFileRoundedIcon />], ['عملکرد و KPI', <InsightsRoundedIcon />],
    ['پیش‌بینی', <AutoGraphRoundedIcon />], ['گزارش‌ها', <AssessmentRoundedIcon />],
    ['تنظیمات و داده‌های پایه', <SettingsRoundedIcon />]
  ] as const
  const titles = ['داشبورد مدیریت بودجه', 'برنامه‌ریزی و ورود بودجه', 'ورود و نگاشت اکسل', 'عملکرد و KPI', 'پیش‌بینی', 'گزارش‌های مالی و مدیریتی', 'تنظیمات و داده‌های پایه']

  return <Box sx={{ display: 'flex', minHeight: '100vh' }}>
    <AppBar position="fixed" elevation={0} sx={{ width: `calc(100% - ${drawerWidth}px)`, mr: `${drawerWidth}px`, bgcolor: '#071a2f' }}>
      <Toolbar><Typography fontWeight={800} flexGrow={1}>Performance Budget Management</Typography><Typography variant="body2">{displayName}</Typography></Toolbar>
    </AppBar>
    <Drawer variant="permanent" anchor="right" sx={{ width: drawerWidth, flexShrink: 0, '& .MuiDrawer-paper': { width: drawerWidth, boxSizing: 'border-box', bgcolor: '#0b2038', color: '#dce8f7', border: 0 } }}>
      <Box sx={{ p: 2.5 }}><Typography variant="h6" fontWeight={900}>PBM</Typography><Typography variant="caption" sx={{ opacity: .7 }}>بودجه و عملکرد سازمانی</Typography></Box>
      <Divider sx={{ borderColor: 'rgba(255,255,255,.1)' }} />
      <List sx={{ px: 1 }}>{menu.map(([label, icon], index) => <ListItemButton key={label} selected={index === activeView} onClick={() => setActiveView(index)} sx={{ borderRadius: 2, mb: .5, '&.Mui-selected': { bgcolor: 'rgba(56,139,253,.18)' } }}><ListItemIcon sx={{ color: 'inherit', minWidth: 40 }}>{icon}</ListItemIcon><ListItemText primary={label} /></ListItemButton>)}</List>
      <Box flexGrow={1} />
      <List sx={{ p: 1 }}><ListItemButton onClick={onLogout} sx={{ borderRadius: 2 }}><ListItemIcon sx={{ color: 'inherit', minWidth: 40 }}><LogoutRoundedIcon /></ListItemIcon><ListItemText primary="خروج" /></ListItemButton></List>
    </Drawer>
    <Box component="main" sx={{ flexGrow: 1, pt: 11, pb: 5, minWidth: 0 }}><Container maxWidth="xl">
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2} mb={3}>
        <Box><Typography variant="h4" fontWeight={900}>{titles[activeView]}</Typography><Typography color="text.secondary">{selectedCompany?.name ?? 'انتخاب شرکت'} — سال مالی {years.find(x => x.id === yearId)?.jalaliYear ?? '-'}</Typography></Box>
        <Stack direction="row" spacing={1.5}>
          <FormControl size="small" sx={{ minWidth: 220 }}><InputLabel>شرکت</InputLabel><Select value={companyId} label="شرکت" onChange={e => setCompanyId(e.target.value)}>{companies.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl>
          <FormControl size="small" sx={{ minWidth: 160 }}><InputLabel>سال مالی</InputLabel><Select value={yearId} label="سال مالی" onChange={e => setYearId(e.target.value)}>{years.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl>
        </Stack>
      </Stack>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {activeView === 0 && <DashboardContent loading={loading} summary={summary} />}
      {activeView === 1 && companyId && yearId && <BudgetPlanning companyId={companyId} fiscalYearId={yearId} />}
      {activeView === 2 && companyId && yearId && <WorkbookImport companyId={companyId} fiscalYearId={yearId} />}
      {activeView === 3 && companyId && yearId && <KpiPerformance companyId={companyId} fiscalYearId={yearId} />}
      {activeView === 4 && companyId && yearId && <Forecasting companyId={companyId} fiscalYearId={yearId} />}
      {activeView === 5 && companyId && yearId && <FinancialReports companyId={companyId} fiscalYearId={yearId} />}
      {activeView === 6 && companyId && <ReferenceAdmin companyId={companyId} />}
    </Container></Box>
  </Box>
}

function DashboardContent({ loading, summary }: { loading: boolean; summary: DashboardSummary | null }) {
  if (loading) return <Box py={8} textAlign="center"><CircularProgress /></Box>
  if (!summary) return null
  return <>
    <Box className="kpi-grid">
      <Kpi title="بودجه" value={formatAmount(summary.budget)} subtitle="کل بودجه ثبت‌شده" />
      <Kpi title="عملکرد واقعی" value={formatAmount(summary.actual)} subtitle={`${money.format(summary.budgetUtilizationPercent)}٪ مصرف بودجه`} />
      <Kpi title="بودجه در دسترس" value={formatAmount(summary.remaining)} subtitle={`تعهدات: ${formatAmount(summary.commitment)}`} />
      <Kpi title="پیش‌بینی" value={formatAmount(summary.forecast)} subtitle={`انحراف: ${formatAmount(summary.variance)}`} />
    </Box>
    <Card elevation={0} sx={{ mt: 3 }}><CardContent sx={{ p: 3 }}><Typography variant="h6" fontWeight={800} mb={2}>روند ماهانه بودجه و عملکرد</Typography><Box sx={{ height: 390, direction: 'ltr' }}><ResponsiveContainer width="100%" height="100%"><ComposedChart data={summary.monthly}><CartesianGrid strokeDasharray="3 3" vertical={false} /><XAxis dataKey="periodName" /><YAxis tickFormatter={value => `${Math.round(Number(value) / 1_000_000_000)}B`} /><Tooltip formatter={value => formatAmount(Number(value))} /><Legend /><Bar dataKey="budget" name="بودجه" fill="#0b5cad" radius={[6, 6, 0, 0]} /><Bar dataKey="actual" name="عملکرد" fill="#00a6a6" radius={[6, 6, 0, 0]} /><Line type="monotone" dataKey="forecast" name="پیش‌بینی" stroke="#ef8c22" strokeWidth={3} dot={false} /></ComposedChart></ResponsiveContainer></Box></CardContent></Card>
  </>
}

function Kpi({ title, value, subtitle }: { title: string; value: string; subtitle: string }) {
  return <Card elevation={0} className="kpi-card"><CardContent><Typography color="text.secondary" fontWeight={700}>{title}</Typography><Typography variant="h5" fontWeight={900} mt={1}>{value}</Typography><Typography variant="caption" color="text.secondary">{subtitle}</Typography></CardContent></Card>
}
