import { useEffect, useState } from 'react'
import {
  Alert, Box, Card, CardContent, CircularProgress, FormControl, InputLabel, MenuItem, Select,
  Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography
} from '@mui/material'
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded'
import { Bar, CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from './api'

type SalesRow = {
  memberCode: string; memberName: string; companyCount: number
  budgetNetSales: number; actualNetSales: number; forecastNetSales: number
  actualNetSalesVariance: number; forecastNetSalesVariance: number
  budgetGrossProfit: number; actualGrossProfit: number; forecastGrossProfit: number
  budgetAchievementPercent: number; actualContributionPercent: number
}
type SalesData = { jalaliYear: number; currencyCode: string; dimensionCode: string; dimensionName: string; companiesWithFiscalYear: number; totalActualNetSales: number; rows: SalesRow[] }
type ExpenseRow = {
  memberCode: string; memberName: string; companyCount: number
  budgetNetCost: number; actualNetCost: number; forecastNetCost: number
  actualVarianceAmount: number; forecastVarianceAmount: number
  budgetAchievementPercent: number; actualContributionPercent: number
}
type ExpenseData = { jalaliYear: number; currencyCode: string; dimensionCode: string; dimensionName: string; companiesWithFiscalYear: number; totalActualNetCost: number; rows: ExpenseRow[] }

const salesDimensions = [
  ['PRODUCT','کالا / محصول'], ['BRAND','برند'], ['SUPPLIER','تأمین‌کننده / کمپانی'], ['CUSTOMER','مشتری'],
  ['REGION','منطقه'], ['CONTRACT','قرارداد'], ['DEPARTMENT','واحد سازمانی'], ['COSTCENTER','مرکز هزینه'],
  ['PROGRAM','برنامه'], ['ACTIVITY','فعالیت'], ['PROJECT','پروژه']
] as const
const expenseDimensions = [
  ['COSTCENTER','مرکز هزینه'], ['DEPARTMENT','واحد سازمانی'], ['EXPENSEITEM','ردیف هزینه'], ['ACCOUNT','حساب'],
  ['PROGRAM','برنامه'], ['ACTIVITY','فعالیت'], ['PROJECT','پروژه'], ['REGION','منطقه'], ['CONTRACT','قرارداد']
] as const
const nf = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 1 })
function amount(value: number) { const abs = Math.abs(value); if (abs >= 1e12) return `${nf.format(value / 1e12)} همت`; if (abs >= 1e9) return `${nf.format(value / 1e9)} میلیارد`; if (abs >= 1e6) return `${nf.format(value / 1e6)} میلیون`; return nf.format(value) }
function errorText(error: unknown) { if (typeof error === 'object' && error !== null && 'response' in error) return (error as { response?: { data?: { detail?: string } } }).response?.data?.detail ?? 'دریافت تحلیل بُعدی پرتفوی ناموفق بود.'; return 'دریافت تحلیل بُعدی پرتفوی ناموفق بود.' }

export default function PortfolioDimensionPanel({ companyId, fiscalYearId }: { companyId: string; fiscalYearId: string }) {
  const [salesDimension, setSalesDimension] = useState('PRODUCT')
  const [expenseDimension, setExpenseDimension] = useState('COSTCENTER')
  const [sales, setSales] = useState<SalesData | null>(null)
  const [expenses, setExpenses] = useState<ExpenseData | null>(null)
  const [salesLoading, setSalesLoading] = useState(false)
  const [expenseLoading, setExpenseLoading] = useState(false)
  const [salesError, setSalesError] = useState('')
  const [expenseError, setExpenseError] = useState('')

  useEffect(() => {
    let active = true
    if (!companyId || !fiscalYearId || !salesDimension) return () => { active = false }
    setSalesLoading(true); setSalesError('')
    api.get<SalesData>('/dashboard/portfolio/sales-dimension', { params: { companyId, fiscalYearId, dimensionCode: salesDimension, take: 50 } })
      .then(r => { if (active) setSales(r.data) })
      .catch(e => { if (active) { setSales(null); setSalesError(errorText(e)) } })
      .finally(() => { if (active) setSalesLoading(false) })
    return () => { active = false }
  }, [companyId, fiscalYearId, salesDimension])

  useEffect(() => {
    let active = true
    if (!companyId || !fiscalYearId || !expenseDimension) return () => { active = false }
    setExpenseLoading(true); setExpenseError('')
    api.get<ExpenseData>('/dashboard/portfolio/expense-dimension', { params: { companyId, fiscalYearId, dimensionCode: expenseDimension, take: 50 } })
      .then(r => { if (active) setExpenses(r.data) })
      .catch(e => { if (active) { setExpenses(null); setExpenseError(errorText(e)) } })
      .finally(() => { if (active) setExpenseLoading(false) })
    return () => { active = false }
  }, [companyId, fiscalYearId, expenseDimension])

  return <Stack spacing={2.5}>
    <Card elevation={0} sx={{ background: 'linear-gradient(135deg, rgba(15,118,110,.06), rgba(37,99,235,.05))' }}><CardContent><Stack direction="row" spacing={1} alignItems="center"><AccountTreeRoundedIcon color="primary"/><Typography variant="h6" fontWeight={900}>تحلیل بین‌شرکتی Dimensionها</Typography></Stack><Typography color="text.secondary" variant="body2" mt={.75}>سهم و انحراف کالا/برند/تأمین‌کننده و مراکز هزینه/واحدها/برنامه‌ها در کل شرکت‌های قابل دسترس.</Typography></CardContent></Card>

    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} alignItems={{ md: 'center' }} mb={2}><Box><Typography variant="h6" fontWeight={900}>Contribution فروش و سود ناخالص</Typography><Typography color="text.secondary" variant="body2">رتبه بر اساس Actual فروش خالص و سهم از کل فروش واقعی گروه.</Typography></Box><FormControl size="small" sx={{ minWidth: 245 }}><InputLabel>بعد فروش</InputLabel><Select value={salesDimension} label="بعد فروش" onChange={e => setSalesDimension(e.target.value)}>{salesDimensions.map(([code,name]) => <MenuItem key={code} value={code}>{name} ({code})</MenuItem>)}</Select></FormControl></Stack>
      {salesError && <Alert severity="error" sx={{ mb: 2 }}>{salesError}</Alert>}
      {salesLoading && !sales ? <Box py={4} textAlign="center"><CircularProgress size={28}/></Box> : sales && <>
        <Box height={330} mb={2}><ResponsiveContainer width="100%" height="100%"><ComposedChart data={sales.rows.slice(0, 15)}><CartesianGrid strokeDasharray="3 3"/><XAxis dataKey="memberName"/><YAxis yAxisId="amount" tickFormatter={v => amount(Number(v))}/><YAxis yAxisId="pct" orientation="right"/><Tooltip formatter={(v: unknown) => nf.format(Number(v ?? 0))}/><Legend/><Bar yAxisId="amount" dataKey="actualNetSales" name="Actual فروش خالص" fill="#0f766e"/><Bar yAxisId="amount" dataKey="forecastNetSales" name="Forecast فروش خالص" fill="#7c3aed"/><Line yAxisId="pct" dataKey="actualContributionPercent" name="سهم از کل Actual" stroke="#d97706" strokeWidth={2}/></ComposedChart></ResponsiveContainer></Box>
        <TableContainer sx={{ maxHeight: 500 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>عضو</TableCell><TableCell>شرکت‌ها</TableCell><TableCell>Bud فروش</TableCell><TableCell>Act فروش</TableCell><TableCell>Fct فروش</TableCell><TableCell>انحراف Act</TableCell><TableCell>تحقق</TableCell><TableCell>Act سود ناخالص</TableCell><TableCell>سهم از کل</TableCell></TableRow></TableHead><TableBody>{sales.rows.map(row => <TableRow hover key={row.memberCode}><TableCell><Typography fontWeight={800}>{row.memberName}</Typography><Typography variant="caption" color="text.secondary">{row.memberCode}</Typography></TableCell><TableCell>{row.companyCount.toLocaleString('fa-IR')}</TableCell><TableCell>{amount(row.budgetNetSales)}</TableCell><TableCell>{amount(row.actualNetSales)}</TableCell><TableCell>{amount(row.forecastNetSales)}</TableCell><TableCell>{amount(row.actualNetSalesVariance)}</TableCell><TableCell>{nf.format(row.budgetAchievementPercent)}٪</TableCell><TableCell>{amount(row.actualGrossProfit)}</TableCell><TableCell>{nf.format(row.actualContributionPercent)}٪</TableCell></TableRow>)}</TableBody></Table></TableContainer>
      </>}
    </CardContent></Card>

    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} alignItems={{ md: 'center' }} mb={2}><Box><Typography variant="h6" fontWeight={900}>Contribution هزینه و مراکز هزینه</Typography><Typography color="text.secondary" variant="body2">رتبه بر اساس Actual خالص هزینه و سهم از کل هزینه واقعی گروه.</Typography></Box><FormControl size="small" sx={{ minWidth: 245 }}><InputLabel>بعد هزینه</InputLabel><Select value={expenseDimension} label="بعد هزینه" onChange={e => setExpenseDimension(e.target.value)}>{expenseDimensions.map(([code,name]) => <MenuItem key={code} value={code}>{name} ({code})</MenuItem>)}</Select></FormControl></Stack>
      {expenseError && <Alert severity="error" sx={{ mb: 2 }}>{expenseError}</Alert>}
      {expenseLoading && !expenses ? <Box py={4} textAlign="center"><CircularProgress size={28}/></Box> : expenses && <>
        <Box height={330} mb={2}><ResponsiveContainer width="100%" height="100%"><ComposedChart data={expenses.rows.slice(0, 15)}><CartesianGrid strokeDasharray="3 3"/><XAxis dataKey="memberName"/><YAxis yAxisId="amount" tickFormatter={v => amount(Number(v))}/><YAxis yAxisId="pct" orientation="right"/><Tooltip formatter={(v: unknown) => nf.format(Number(v ?? 0))}/><Legend/><Bar yAxisId="amount" dataKey="actualNetCost" name="Actual خالص هزینه" fill="#b91c1c"/><Bar yAxisId="amount" dataKey="forecastNetCost" name="Forecast خالص هزینه" fill="#7c3aed"/><Line yAxisId="pct" dataKey="actualContributionPercent" name="سهم از کل Actual" stroke="#d97706" strokeWidth={2}/></ComposedChart></ResponsiveContainer></Box>
        <TableContainer sx={{ maxHeight: 500 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>عضو</TableCell><TableCell>شرکت‌ها</TableCell><TableCell>Bud خالص هزینه</TableCell><TableCell>Act خالص هزینه</TableCell><TableCell>Fct خالص هزینه</TableCell><TableCell>انحراف Act</TableCell><TableCell>تحقق/مصرف</TableCell><TableCell>سهم از کل</TableCell></TableRow></TableHead><TableBody>{expenses.rows.map(row => <TableRow hover key={row.memberCode}><TableCell><Typography fontWeight={800}>{row.memberName}</Typography><Typography variant="caption" color="text.secondary">{row.memberCode}</Typography></TableCell><TableCell>{row.companyCount.toLocaleString('fa-IR')}</TableCell><TableCell>{amount(row.budgetNetCost)}</TableCell><TableCell>{amount(row.actualNetCost)}</TableCell><TableCell>{amount(row.forecastNetCost)}</TableCell><TableCell>{amount(row.actualVarianceAmount)}</TableCell><TableCell>{nf.format(row.budgetAchievementPercent)}٪</TableCell><TableCell>{nf.format(row.actualContributionPercent)}٪</TableCell></TableRow>)}</TableBody></Table></TableContainer>
      </>}
    </CardContent></Card>
  </Stack>
}
