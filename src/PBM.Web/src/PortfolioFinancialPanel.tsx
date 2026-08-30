import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Card, CardContent, CircularProgress, Stack, Table, TableBody, TableCell,
  TableContainer, TableHead, TableRow, Typography
} from '@mui/material'
import CorporateFareRoundedIcon from '@mui/icons-material/CorporateFareRounded'
import { Bar, CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from './api'

type Totals = {
  budgetNetSales: number; actualNetSales: number; forecastNetSales: number
  budgetGrossProfit: number; actualGrossProfit: number; forecastGrossProfit: number
  budgetOperatingProfit: number; actualOperatingProfit: number; forecastOperatingProfit: number
  budgetNetProfit: number; actualNetProfit: number; forecastNetProfit: number
  actualNetSalesVariance: number; forecastNetSalesVariance: number
  actualNetProfitVariance: number; forecastNetProfitVariance: number
  actualNetMarginPercent: number; budgetAchievementPercent: number
}
type CompanyRow = Totals & {
  companyId: string; companyCode: string; companyName: string; fiscalYearId: string; jalaliYear: number
}
type Data = {
  jalaliYear: number; currencyCode: string; accessibleCompanyCount: number; companiesWithFiscalYear: number
  totals: Totals; companies: CompanyRow[]
}

const nf = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 1 })
function amount(value: number) {
  const abs = Math.abs(value)
  if (abs >= 1e12) return `${nf.format(value / 1e12)} همت`
  if (abs >= 1e9) return `${nf.format(value / 1e9)} میلیارد`
  if (abs >= 1e6) return `${nf.format(value / 1e6)} میلیون`
  return nf.format(value)
}
function errorText(error: unknown) {
  if (typeof error === 'object' && error !== null && 'response' in error)
    return (error as { response?: { data?: { detail?: string } } }).response?.data?.detail ?? 'دریافت داشبورد تجمیعی شرکت‌ها ناموفق بود.'
  return 'دریافت داشبورد تجمیعی شرکت‌ها ناموفق بود.'
}

export default function PortfolioFinancialPanel({ companyId, fiscalYearId }: { companyId: string; fiscalYearId: string }) {
  const [data, setData] = useState<Data | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    setData(null); setError('')
    if (!companyId || !fiscalYearId) return () => { active = false }
    setLoading(true)
    api.get<Data>('/dashboard/portfolio/financial-performance', { params: { companyId, fiscalYearId } })
      .then(response => { if (active) setData(response.data) })
      .catch(requestError => { if (active) setError(errorText(requestError)) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [companyId, fiscalYearId])

  const chartData = useMemo(() => data?.companies.map(row => ({
    company: row.companyName,
    budgetSales: row.budgetNetSales,
    actualSales: row.actualNetSales,
    forecastSales: row.forecastNetSales,
    actualNetProfit: row.actualNetProfit,
    achievement: row.budgetAchievementPercent
  })) ?? [], [data])

  if (loading && !data) return <Box display="flex" justifyContent="center" py={5}><CircularProgress /></Box>
  if (error && !data) return <Alert severity="error">{error}</Alert>
  if (!data) return null

  return <Stack spacing={2.5}>
    {error && <Alert severity="error">{error}</Alert>}
    <Card elevation={0} sx={{ background: 'linear-gradient(135deg, rgba(8,47,73,.07), rgba(79,70,229,.06))' }}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2}>
        <Box><Stack direction="row" spacing={1} alignItems="center"><CorporateFareRoundedIcon color="primary"/><Typography variant="h6" fontWeight={900}>پرتفوی عملکرد مالی شرکت‌ها — سال {data.jalaliYear.toLocaleString('fa-IR')}</Typography></Stack><Typography color="text.secondary" variant="body2" mt={.75}>رتبه‌بندی شرکت‌های مجاز بر اساس P&L یکسان: فروش خالص، سود ناخالص، سود عملیاتی و سود خالص Budget / Actual / Forecast.</Typography></Box>
        <Typography variant="caption" color="text.secondary">{data.companiesWithFiscalYear.toLocaleString('fa-IR')} شرکت دارای سال مالی از {data.accessibleCompanyCount.toLocaleString('fa-IR')} شرکت مجاز — {data.currencyCode}</Typography>
      </Stack>
    </CardContent></Card>

    <Box className="kpi-grid">{[
      ['بودجه فروش خالص گروه', amount(data.totals.budgetNetSales)],
      ['Actual فروش خالص گروه', amount(data.totals.actualNetSales)],
      ['Forecast فروش خالص گروه', amount(data.totals.forecastNetSales)],
      ['تحقق فروش گروه', `${nf.format(data.totals.budgetAchievementPercent)}٪`],
      ['Actual سود خالص گروه', amount(data.totals.actualNetProfit)],
      ['Forecast سود خالص گروه', amount(data.totals.forecastNetProfit)],
      ['انحراف Actual سود خالص', amount(data.totals.actualNetProfitVariance)],
      ['حاشیه سود خالص Actual', `${nf.format(data.totals.actualNetMarginPercent)}٪`]
    ].map(([label, value]) => <Card className="kpi-card" elevation={0} key={label}><CardContent><Typography color="text.secondary" variant="body2">{label}</Typography><Typography variant="h5" fontWeight={900} mt={1}>{value}</Typography></CardContent></Card>)}</Box>

    {chartData.length > 0 && <Card elevation={0}><CardContent><Typography variant="h6" fontWeight={900} mb={2}>مقایسه شرکت‌ها</Typography><Box height={390}><ResponsiveContainer width="100%" height="100%"><ComposedChart data={chartData}><CartesianGrid strokeDasharray="3 3"/><XAxis dataKey="company"/><YAxis yAxisId="amount" tickFormatter={v => amount(Number(v))}/><YAxis yAxisId="pct" orientation="right"/><Tooltip formatter={(v: unknown) => nf.format(Number(v ?? 0))}/><Legend/><Bar yAxisId="amount" dataKey="budgetSales" name="Budget فروش خالص" fill="#2563eb"/><Bar yAxisId="amount" dataKey="actualSales" name="Actual فروش خالص" fill="#0f766e"/><Bar yAxisId="amount" dataKey="forecastSales" name="Forecast فروش خالص" fill="#7c3aed"/><Line yAxisId="pct" dataKey="achievement" name="درصد تحقق فروش" stroke="#d97706" strokeWidth={2.5}/></ComposedChart></ResponsiveContainer></Box></CardContent></Card>}

    <Card elevation={0}><CardContent><Typography variant="h6" fontWeight={900} mb={2}>رتبه‌بندی عملکرد مالی شرکت‌ها</Typography><TableContainer sx={{ maxHeight: 560 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>رتبه / شرکت</TableCell><TableCell>Bud فروش</TableCell><TableCell>Act فروش</TableCell><TableCell>Fct فروش</TableCell><TableCell>تحقق فروش</TableCell><TableCell>Act سود ناخالص</TableCell><TableCell>Act سود عملیاتی</TableCell><TableCell>Bud سود خالص</TableCell><TableCell>Act سود خالص</TableCell><TableCell>Fct سود خالص</TableCell><TableCell>انحراف Act سود</TableCell><TableCell>حاشیه سود Act</TableCell></TableRow></TableHead><TableBody>{data.companies.map((row, index) => <TableRow hover key={row.companyId}><TableCell><Typography fontWeight={900}>{(index + 1).toLocaleString('fa-IR')}. {row.companyName}</Typography><Typography variant="caption" color="text.secondary">{row.companyCode}</Typography></TableCell><TableCell>{amount(row.budgetNetSales)}</TableCell><TableCell>{amount(row.actualNetSales)}</TableCell><TableCell>{amount(row.forecastNetSales)}</TableCell><TableCell>{nf.format(row.budgetAchievementPercent)}٪</TableCell><TableCell>{amount(row.actualGrossProfit)}</TableCell><TableCell>{amount(row.actualOperatingProfit)}</TableCell><TableCell>{amount(row.budgetNetProfit)}</TableCell><TableCell><Typography fontWeight={900}>{amount(row.actualNetProfit)}</Typography></TableCell><TableCell>{amount(row.forecastNetProfit)}</TableCell><TableCell>{amount(row.actualNetProfitVariance)}</TableCell><TableCell>{nf.format(row.actualNetMarginPercent)}٪</TableCell></TableRow>)}{!data.companies.length && <TableRow><TableCell colSpan={12}><Typography textAlign="center" color="text.secondary" py={3}>برای سال انتخاب‌شده شرکت دارای داده قابل مقایسه وجود ندارد.</Typography></TableCell></TableRow>}</TableBody></Table></TableContainer></CardContent></Card>
  </Stack>
}
