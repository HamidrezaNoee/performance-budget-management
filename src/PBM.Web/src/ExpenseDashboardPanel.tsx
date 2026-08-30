import { useEffect, useState } from 'react'
import { Alert, Box, Card, CardContent, CircularProgress, FormControl, InputLabel, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import PaymentsRoundedIcon from '@mui/icons-material/PaymentsRounded'
import { Bar, CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from './api'

type Dim = { id: string; code: string; name: string; sequence: number }
type Month = {
  periodId: string; periodName: string; sequence: number
  budgetExpense: number; actualExpense: number; forecastExpense: number
  budgetIncome: number; actualIncome: number; forecastIncome: number
  budgetNetCost: number; actualNetCost: number; forecastNetCost: number
}
type ClassRow = { memberId: string; code: string; name: string; budgetAmount: number; actualAmount: number; forecastAmount: number; actualVarianceAmount: number; forecastVarianceAmount: number }
type Drill = {
  memberId: string; code: string; name: string
  budgetExpense: number; actualExpense: number; forecastExpense: number
  budgetIncome: number; actualIncome: number; forecastIncome: number
  budgetNetCost: number; actualNetCost: number; forecastNetCost: number
  actualVarianceAmount: number; forecastVarianceAmount: number
}
type Data = {
  versionId: string; versionNumber: number; versionName: string; currencyCode: string
  budgetExpense: number; actualExpense: number; forecastExpense: number
  budgetIncome: number; actualIncome: number; forecastIncome: number
  budgetNetCost: number; actualNetCost: number; forecastNetCost: number
  actualVarianceAmount: number; forecastVarianceAmount: number
  monthly: Month[]; classes: ClassRow[]; dimensions: Dim[]; selectedDimensionId?: string | null; drilldown: Drill[]
}
const n = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 1 })
function amount(v: number) { const a = Math.abs(v); if (a >= 1e12) return `${n.format(v / 1e12)} همت`; if (a >= 1e9) return `${n.format(v / 1e9)} میلیارد`; if (a >= 1e6) return `${n.format(v / 1e6)} میلیون`; return n.format(v) }
function errorText(e: unknown) { return typeof e === 'object' && e !== null && 'response' in e ? (e as { response?: { data?: { detail?: string } } }).response?.data?.detail ?? 'دریافت داشبورد هزینه ناموفق بود.' : 'دریافت داشبورد هزینه ناموفق بود.' }

export default function ExpenseDashboardPanel({ companyId, fiscalYearId }: { companyId: string; fiscalYearId: string }) {
  const [data, setData] = useState<Data | null>(null); const [dimensionId, setDimensionId] = useState(''); const [busy, setBusy] = useState(false); const [error, setError] = useState('')
  const load = async (dim?: string) => { if (!companyId || !fiscalYearId) return; setBusy(true); setError(''); try { const r = await api.get<Data | null>('/dashboard/expenses', { params: { companyId, fiscalYearId, dimensionId: dim || undefined, take: 50 } }); setData(r.data); setDimensionId(r.data?.selectedDimensionId ?? '') } catch (e) { setError(errorText(e)) } finally { setBusy(false) } }
  useEffect(() => { setData(null); void load() }, [companyId, fiscalYearId])
  if (busy && !data) return <Box py={5} textAlign="center"><CircularProgress /></Box>
  if (error && !data) return <Alert severity="error">{error}</Alert>
  if (!data) return <Alert severity="info">برای هزینه‌ها هنوز برنامه EXPENSE وجود ندارد.</Alert>

  return <Stack spacing={2.5}>
    {error && <Alert severity="error">{error}</Alert>}
    <Card elevation={0} sx={{ background: 'linear-gradient(135deg, rgba(234,88,12,.06), rgba(124,58,237,.07))' }}><CardContent><Stack direction="row" spacing={1} alignItems="center"><PaymentsRoundedIcon color="primary"/><Typography variant="h6" fontWeight={900}>داشبورد Budget / Actual / Forecast هزینه‌ها و مراکز هزینه</Typography></Stack><Typography color="text.secondary" variant="body2">حقوق، اداری، بازاریابی، فروش، عملیاتی، مالی و غیرعملیاتی — نسخه {data.versionNumber.toLocaleString('fa-IR')}؛ Actual از Ledger/ERP یا Import کنترل‌شده.</Typography></CardContent></Card>

    <Box className="kpi-grid">{[
      ['بودجه هزینه‌ها', amount(data.budgetExpense)], ['عملکرد واقعی هزینه‌ها', amount(data.actualExpense)], ['Forecast هزینه‌ها', amount(data.forecastExpense)],
      ['بودجه درآمدهای جانبی', amount(data.budgetIncome)], ['عملکرد واقعی درآمدها', amount(data.actualIncome)], ['Forecast درآمدها', amount(data.forecastIncome)],
      ['بودجه خالص هزینه', amount(data.budgetNetCost)], ['عملکرد واقعی خالص هزینه', amount(data.actualNetCost)], ['Forecast خالص هزینه', amount(data.forecastNetCost)],
      ['انحراف Actual از بودجه', amount(data.actualVarianceAmount)], ['انحراف Forecast از بودجه', amount(data.forecastVarianceAmount)]
    ].map(([l, v]) => <Card key={l} className="kpi-card" elevation={0}><CardContent><Typography color="text.secondary" variant="body2">{l}</Typography><Typography variant="h5" fontWeight={900} mt={1}>{v}</Typography></CardContent></Card>)}</Box>

    <Card elevation={0}><CardContent><Typography variant="h6" fontWeight={900} mb={2}>روند ماهانه خالص هزینه</Typography><Box height={350}><ResponsiveContainer width="100%" height="100%"><ComposedChart data={data.monthly}><CartesianGrid strokeDasharray="3 3"/><XAxis dataKey="periodName"/><YAxis/><Tooltip formatter={(v: unknown) => n.format(Number(v ?? 0))}/><Legend/><Bar dataKey="budgetNetCost" name="بودجه خالص هزینه" fill="#2563eb"/><Bar dataKey="actualNetCost" name="Actual خالص هزینه" fill="#0f766e"/><Bar dataKey="forecastNetCost" name="Forecast خالص هزینه" fill="#7c3aed"/><Line dataKey="actualExpense" name="Actual هزینه ناخالص" stroke="#d97706" strokeWidth={2}/></ComposedChart></ResponsiveContainer></Box>
      <TableContainer sx={{ maxHeight: 440 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>ماه</TableCell><TableCell>Bud هزینه</TableCell><TableCell>Act هزینه</TableCell><TableCell>Fct هزینه</TableCell><TableCell>Bud درآمد</TableCell><TableCell>Act درآمد</TableCell><TableCell>Fct درآمد</TableCell><TableCell>Bud خالص</TableCell><TableCell>Act خالص</TableCell><TableCell>Fct خالص</TableCell></TableRow></TableHead><TableBody>{data.monthly.map(m => <TableRow key={m.periodId} hover><TableCell>{m.periodName}</TableCell><TableCell>{amount(m.budgetExpense)}</TableCell><TableCell>{amount(m.actualExpense)}</TableCell><TableCell>{amount(m.forecastExpense)}</TableCell><TableCell>{amount(m.budgetIncome)}</TableCell><TableCell>{amount(m.actualIncome)}</TableCell><TableCell>{amount(m.forecastIncome)}</TableCell><TableCell>{amount(m.budgetNetCost)}</TableCell><TableCell>{amount(m.actualNetCost)}</TableCell><TableCell>{amount(m.forecastNetCost)}</TableCell></TableRow>)}</TableBody></Table></TableContainer>
    </CardContent></Card>

    <Card elevation={0}><CardContent><Typography variant="h6" fontWeight={900} mb={2}>ترکیب طبقات هزینه / درآمد</Typography><TableContainer sx={{ maxHeight: 470 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>طبقه</TableCell><TableCell>بودجه</TableCell><TableCell>Actual</TableCell><TableCell>Forecast</TableCell><TableCell>انحراف Actual</TableCell><TableCell>انحراف Forecast</TableCell></TableRow></TableHead><TableBody>{data.classes.map(r => <TableRow key={`${r.memberId}:${r.code}`} hover><TableCell><Typography fontWeight={800}>{r.name}</Typography><Typography variant="caption">{r.code}</Typography></TableCell><TableCell>{amount(r.budgetAmount)}</TableCell><TableCell>{amount(r.actualAmount)}</TableCell><TableCell>{amount(r.forecastAmount)}</TableCell><TableCell>{amount(r.actualVarianceAmount)}</TableCell><TableCell>{amount(r.forecastVarianceAmount)}</TableCell></TableRow>)}</TableBody></Table></TableContainer></CardContent></Card>

    <Card elevation={0}><CardContent><Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} mb={2}><Box><Typography variant="h6" fontWeight={900}>Drill-down هزینه</Typography><Typography color="text.secondary" variant="body2">پیش‌فرض مرکز هزینه؛ قابل تغییر به واحد، ردیف هزینه، حساب، پروژه، برنامه و سایر ابعاد.</Typography></Box><FormControl size="small" sx={{ minWidth: 260 }}><InputLabel>بُعد تحلیل</InputLabel><Select value={dimensionId} label="بُعد تحلیل" onChange={e => { setDimensionId(e.target.value); void load(e.target.value) }}>{data.dimensions.map(d => <MenuItem key={d.id} value={d.id}>{d.name} ({d.code})</MenuItem>)}</Select></FormControl></Stack>{busy ? <Box textAlign="center" py={3}><CircularProgress size={25}/></Box> : <TableContainer sx={{ maxHeight: 540 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>عضو</TableCell><TableCell>Bud هزینه</TableCell><TableCell>Act هزینه</TableCell><TableCell>Fct هزینه</TableCell><TableCell>Act درآمد</TableCell><TableCell>Bud خالص</TableCell><TableCell>Act خالص</TableCell><TableCell>Fct خالص</TableCell><TableCell>انحراف Actual</TableCell><TableCell>انحراف Forecast</TableCell></TableRow></TableHead><TableBody>{data.drilldown.map(r => <TableRow key={`${r.memberId}:${r.code}`} hover><TableCell><Typography fontWeight={800}>{r.name}</Typography><Typography variant="caption">{r.code}</Typography></TableCell><TableCell>{amount(r.budgetExpense)}</TableCell><TableCell>{amount(r.actualExpense)}</TableCell><TableCell>{amount(r.forecastExpense)}</TableCell><TableCell>{amount(r.actualIncome)}</TableCell><TableCell>{amount(r.budgetNetCost)}</TableCell><TableCell>{amount(r.actualNetCost)}</TableCell><TableCell>{amount(r.forecastNetCost)}</TableCell><TableCell>{amount(r.actualVarianceAmount)}</TableCell><TableCell>{amount(r.forecastVarianceAmount)}</TableCell></TableRow>)}</TableBody></Table></TableContainer>}</CardContent></Card>
  </Stack>
}
