import { useEffect, useState } from 'react'
import {
  Alert, Box, Card, CardContent, CircularProgress, FormControl, InputLabel, MenuItem, Select,
  Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography
} from '@mui/material'
import ShoppingCartCheckoutRoundedIcon from '@mui/icons-material/ShoppingCartCheckoutRounded'
import { Bar, CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from './api'

type DimensionOption = { id: string; code: string; name: string; sequence: number }
type Monthly = {
  periodId: string; periodName: string; sequence: number
  budgetQuantity: number; actualQuantity: number; forecastQuantity: number
  budgetPurchaseAmount: number; actualPurchaseAmount: number; forecastPurchaseAmount: number
  budgetCostAmount: number; actualCostAmount: number; forecastCostAmount: number
  budgetTotalAmount: number; actualTotalAmount: number; forecastTotalAmount: number
}
type CostRow = {
  costTypeId: string; code: string; name: string
  budgetAmount: number; actualAmount: number; forecastAmount: number
  actualVarianceAmount: number; forecastVarianceAmount: number
}
type DrilldownRow = {
  memberId: string; code: string; name: string
  budgetQuantity: number; actualQuantity: number; forecastQuantity: number
  budgetPurchaseAmount: number; actualPurchaseAmount: number; forecastPurchaseAmount: number
  budgetCostAmount: number; actualCostAmount: number; forecastCostAmount: number
  budgetTotalAmount: number; actualTotalAmount: number; forecastTotalAmount: number
  actualVarianceAmount: number; forecastVarianceAmount: number
}
type PurchaseDashboard = {
  versionId: string; versionNumber: number; versionName: string; currencyCode: string
  budgetQuantity: number; actualQuantity: number; forecastQuantity: number
  budgetPurchaseAmount: number; actualPurchaseAmount: number; forecastPurchaseAmount: number
  budgetCostAmount: number; actualCostAmount: number; forecastCostAmount: number
  budgetTotalAmount: number; actualTotalAmount: number; forecastTotalAmount: number
  actualVarianceAmount: number; forecastVarianceAmount: number
  monthly: Monthly[]; costs: CostRow[]; dimensions: DimensionOption[]; selectedDimensionId?: string | null; drilldown: DrilldownRow[]
}

const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })
const percent = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 1 })
function formatAmount(value: number) { const abs = Math.abs(value); if (abs >= 1e12) return `${number.format(value / 1e12)} همت`; if (abs >= 1e9) return `${number.format(value / 1e9)} میلیارد`; if (abs >= 1e6) return `${number.format(value / 1e6)} میلیون`; return number.format(value) }
function errorMessage(error: unknown, fallback: string) { if (typeof error === 'object' && error !== null && 'response' in error) return (error as { response?: { data?: { detail?: string } } }).response?.data?.detail ?? fallback; return fallback }

export default function PurchaseDashboardPanel({ companyId, fiscalYearId }: { companyId: string; fiscalYearId: string }) {
  const [data, setData] = useState<PurchaseDashboard | null>(null)
  const [dimensionId, setDimensionId] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const load = async (requestedDimensionId?: string) => {
    if (!companyId || !fiscalYearId) return
    setLoading(true); setError('')
    try {
      const response = await api.get<PurchaseDashboard | null>('/dashboard/purchase', { params: { companyId, fiscalYearId, dimensionId: requestedDimensionId || undefined, take: 50 } })
      setData(response.data); setDimensionId(response.data?.selectedDimensionId ?? '')
    } catch (requestError) { setError(errorMessage(requestError, 'دریافت داشبورد خرید ناموفق بود.')) }
    finally { setLoading(false) }
  }

  useEffect(() => { setData(null); setDimensionId(''); setError(''); void load() }, [companyId, fiscalYearId])
  if (loading && !data) return <Box display="flex" justifyContent="center" py={6}><CircularProgress /></Box>
  if (error && !data) return <Alert severity="error">{error}</Alert>
  if (!data) return <Alert severity="info">برای سال مالی انتخاب‌شده هنوز برنامه یا داده‌ای در مدل خرید (TRADE) وجود ندارد.</Alert>

  const ratio = (cost: number, purchase: number) => purchase === 0 ? 0 : cost / purchase * 100
  return <Stack spacing={2.5}>
    {error && <Alert severity="error">{error}</Alert>}
    <Card elevation={0} sx={{ background: 'linear-gradient(135deg, rgba(46,125,50,.08), rgba(25,118,210,.07))' }}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2}>
        <Box><Stack direction="row" spacing={1} alignItems="center"><ShoppingCartCheckoutRoundedIcon color="primary"/><Typography variant="h6" fontWeight={900}>داشبورد Budget / Actual / Forecast خرید</Typography></Stack><Typography color="text.secondary" variant="body2" mt={.75}>تعداد، مبلغ خرید، هزینه‌های جانبی و Drill-down ابعاد از BudgetFactهای TRADE؛ Actual از Ledger/ERP یا Import کنترل‌شده.</Typography></Box>
        <Typography variant="caption" color="text.secondary">نسخه {data.versionNumber.toLocaleString('fa-IR')} — {data.versionName} | ارز پایه: {data.currencyCode}</Typography>
      </Stack>
    </CardContent></Card>

    <Box className="kpi-grid">{[
      ['بودجه تعداد خرید', number.format(data.budgetQuantity)], ['Actual تعداد خرید', number.format(data.actualQuantity)], ['Forecast تعداد خرید', number.format(data.forecastQuantity)],
      ['بودجه مبلغ خرید', formatAmount(data.budgetPurchaseAmount)], ['Actual مبلغ خرید', formatAmount(data.actualPurchaseAmount)], ['Forecast مبلغ خرید', formatAmount(data.forecastPurchaseAmount)],
      ['بودجه هزینه‌های خرید', formatAmount(data.budgetCostAmount)], ['Actual هزینه‌های خرید', formatAmount(data.actualCostAmount)], ['Forecast هزینه‌های خرید', formatAmount(data.forecastCostAmount)],
      ['بودجه کل خرید + هزینه', formatAmount(data.budgetTotalAmount)], ['Actual کل خرید + هزینه', formatAmount(data.actualTotalAmount)], ['Forecast کل خرید + هزینه', formatAmount(data.forecastTotalAmount)],
      ['انحراف Actual از بودجه', formatAmount(data.actualVarianceAmount)], ['انحراف Forecast از بودجه', formatAmount(data.forecastVarianceAmount)]
    ].map(([label, value]) => <Card key={label} className="kpi-card" elevation={0}><CardContent><Typography color="text.secondary" variant="body2">{label}</Typography><Typography variant="h5" fontWeight={900} mt={1}>{value}</Typography></CardContent></Card>)}</Box>

    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={3} mb={2}>
        <Box><Typography color="text.secondary" variant="body2">نسبت هزینه به خرید — Budget</Typography><Typography variant="h6" fontWeight={900}>{percent.format(ratio(data.budgetCostAmount, data.budgetPurchaseAmount))}٪</Typography></Box>
        <Box><Typography color="text.secondary" variant="body2">نسبت هزینه به خرید — Actual</Typography><Typography variant="h6" fontWeight={900}>{percent.format(ratio(data.actualCostAmount, data.actualPurchaseAmount))}٪</Typography></Box>
        <Box><Typography color="text.secondary" variant="body2">نسبت هزینه به خرید — Forecast</Typography><Typography variant="h6" fontWeight={900}>{percent.format(ratio(data.forecastCostAmount, data.forecastPurchaseAmount))}٪</Typography></Box>
      </Stack>
      <Typography variant="h6" fontWeight={900} mb={2}>روند ماهانه خرید</Typography>
      <Box height={390}><ResponsiveContainer width="100%" height="100%"><ComposedChart data={data.monthly}><CartesianGrid strokeDasharray="3 3"/><XAxis dataKey="periodName"/><YAxis yAxisId="amount"/><YAxis yAxisId="qty" orientation="right"/><Tooltip formatter={(value: unknown) => number.format(Number(value ?? 0))}/><Legend/><Bar yAxisId="amount" dataKey="budgetTotalAmount" name="Budget کل خرید" fill="#2563eb"/><Bar yAxisId="amount" dataKey="actualTotalAmount" name="Actual کل خرید" fill="#0f766e"/><Bar yAxisId="amount" dataKey="forecastTotalAmount" name="Forecast کل خرید" fill="#7c3aed"/><Line yAxisId="qty" dataKey="actualQuantity" name="Actual تعداد" stroke="#d97706" strokeWidth={2}/></ComposedChart></ResponsiveContainer></Box>
      <TableContainer sx={{ maxHeight: 460, mt: 2 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>ماه</TableCell><TableCell>Bud تعداد</TableCell><TableCell>Act تعداد</TableCell><TableCell>Fct تعداد</TableCell><TableCell>Bud خرید</TableCell><TableCell>Act خرید</TableCell><TableCell>Fct خرید</TableCell><TableCell>Bud هزینه</TableCell><TableCell>Act هزینه</TableCell><TableCell>Fct هزینه</TableCell><TableCell>Bud کل</TableCell><TableCell>Act کل</TableCell><TableCell>Fct کل</TableCell></TableRow></TableHead><TableBody>{data.monthly.map(month => <TableRow key={month.periodId} hover><TableCell><Typography fontWeight={800}>{month.periodName}</Typography></TableCell><TableCell>{number.format(month.budgetQuantity)}</TableCell><TableCell>{number.format(month.actualQuantity)}</TableCell><TableCell>{number.format(month.forecastQuantity)}</TableCell><TableCell>{formatAmount(month.budgetPurchaseAmount)}</TableCell><TableCell>{formatAmount(month.actualPurchaseAmount)}</TableCell><TableCell>{formatAmount(month.forecastPurchaseAmount)}</TableCell><TableCell>{formatAmount(month.budgetCostAmount)}</TableCell><TableCell>{formatAmount(month.actualCostAmount)}</TableCell><TableCell>{formatAmount(month.forecastCostAmount)}</TableCell><TableCell>{formatAmount(month.budgetTotalAmount)}</TableCell><TableCell>{formatAmount(month.actualTotalAmount)}</TableCell><TableCell>{formatAmount(month.forecastTotalAmount)}</TableCell></TableRow>)}</TableBody></Table></TableContainer>
    </CardContent></Card>

    <Card elevation={0}><CardContent><Typography variant="h6" fontWeight={900} mb={2}>تفکیک هزینه‌های خرید</Typography><TableContainer sx={{ maxHeight: 430 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>نوع هزینه</TableCell><TableCell>Budget</TableCell><TableCell>Actual</TableCell><TableCell>Forecast</TableCell><TableCell>انحراف Actual</TableCell><TableCell>انحراف Forecast</TableCell></TableRow></TableHead><TableBody>{data.costs.map(row => <TableRow key={`${row.costTypeId}:${row.code}`} hover><TableCell><Typography fontWeight={800}>{row.name}</Typography><Typography variant="caption" color="text.secondary">{row.code}</Typography></TableCell><TableCell>{formatAmount(row.budgetAmount)}</TableCell><TableCell>{formatAmount(row.actualAmount)}</TableCell><TableCell>{formatAmount(row.forecastAmount)}</TableCell><TableCell>{formatAmount(row.actualVarianceAmount)}</TableCell><TableCell>{formatAmount(row.forecastVarianceAmount)}</TableCell></TableRow>)}{!data.costs.length && <TableRow><TableCell colSpan={6}><Typography textAlign="center" color="text.secondary" py={2}>هزینه‌ای ثبت نشده است.</Typography></TableCell></TableRow>}</TableBody></Table></TableContainer></CardContent></Card>

    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2} mb={2}><Box><Typography variant="h6" fontWeight={900}>Drill-down ابعاد خرید</Typography><Typography color="text.secondary" variant="body2">کالا، تأمین‌کننده، برند، قرارداد، پروژه، مرکز هزینه و سایر ابعاد TRADE.</Typography></Box><FormControl size="small" sx={{ minWidth: 260 }} disabled={!data.dimensions.length || loading}><InputLabel>بُعد تحلیل</InputLabel><Select value={dimensionId} label="بُعد تحلیل" onChange={event => { const next = event.target.value; setDimensionId(next); void load(next) }}>{data.dimensions.map(dimension => <MenuItem key={dimension.id} value={dimension.id}>{dimension.name} ({dimension.code})</MenuItem>)}</Select></FormControl></Stack>
      {loading ? <Box display="flex" justifyContent="center" py={3}><CircularProgress size={26}/></Box> : <TableContainer sx={{ maxHeight: 540 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>عضو</TableCell><TableCell>Bud تعداد</TableCell><TableCell>Act تعداد</TableCell><TableCell>Fct تعداد</TableCell><TableCell>Bud خرید</TableCell><TableCell>Act خرید</TableCell><TableCell>Fct خرید</TableCell><TableCell>Act هزینه</TableCell><TableCell>Bud کل</TableCell><TableCell>Act کل</TableCell><TableCell>Fct کل</TableCell><TableCell>انحراف Actual</TableCell><TableCell>انحراف Forecast</TableCell></TableRow></TableHead><TableBody>{data.drilldown.map(row => <TableRow key={`${row.memberId}:${row.code}`} hover><TableCell><Typography fontWeight={800}>{row.name}</Typography><Typography variant="caption" color="text.secondary">{row.code}</Typography></TableCell><TableCell>{number.format(row.budgetQuantity)}</TableCell><TableCell>{number.format(row.actualQuantity)}</TableCell><TableCell>{number.format(row.forecastQuantity)}</TableCell><TableCell>{formatAmount(row.budgetPurchaseAmount)}</TableCell><TableCell>{formatAmount(row.actualPurchaseAmount)}</TableCell><TableCell>{formatAmount(row.forecastPurchaseAmount)}</TableCell><TableCell>{formatAmount(row.actualCostAmount)}</TableCell><TableCell>{formatAmount(row.budgetTotalAmount)}</TableCell><TableCell>{formatAmount(row.actualTotalAmount)}</TableCell><TableCell>{formatAmount(row.forecastTotalAmount)}</TableCell><TableCell>{formatAmount(row.actualVarianceAmount)}</TableCell><TableCell>{formatAmount(row.forecastVarianceAmount)}</TableCell></TableRow>)}{!data.drilldown.length && <TableRow><TableCell colSpan={13}><Typography textAlign="center" color="text.secondary" py={3}>برای بُعد انتخاب‌شده داده‌ای ثبت نشده است.</Typography></TableCell></TableRow>}</TableBody></Table></TableContainer>}
    </CardContent></Card>
  </Stack>
}
