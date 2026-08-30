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
  periodId: string
  periodName: string
  sequence: number
  budgetQuantity: number
  forecastQuantity: number
  budgetPurchaseAmount: number
  forecastPurchaseAmount: number
  budgetCostAmount: number
  forecastCostAmount: number
  budgetTotalAmount: number
  forecastTotalAmount: number
}
type CostRow = {
  costTypeId: string
  code: string
  name: string
  budgetAmount: number
  forecastAmount: number
  varianceAmount: number
}
type DrilldownRow = {
  memberId: string
  code: string
  name: string
  budgetQuantity: number
  forecastQuantity: number
  budgetPurchaseAmount: number
  forecastPurchaseAmount: number
  budgetCostAmount: number
  forecastCostAmount: number
  budgetTotalAmount: number
  forecastTotalAmount: number
  varianceAmount: number
}
type PurchaseDashboard = {
  versionId: string
  versionNumber: number
  versionName: string
  currencyCode: string
  budgetQuantity: number
  forecastQuantity: number
  budgetPurchaseAmount: number
  forecastPurchaseAmount: number
  budgetCostAmount: number
  forecastCostAmount: number
  budgetTotalAmount: number
  forecastTotalAmount: number
  varianceAmount: number
  monthly: Monthly[]
  costs: CostRow[]
  dimensions: DimensionOption[]
  selectedDimensionId?: string | null
  drilldown: DrilldownRow[]
}

const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })
const percent = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 1 })

function formatAmount(value: number) {
  const abs = Math.abs(value)
  if (abs >= 1_000_000_000_000) return `${number.format(value / 1_000_000_000_000)} همت`
  if (abs >= 1_000_000_000) return `${number.format(value / 1_000_000_000)} میلیارد`
  if (abs >= 1_000_000) return `${number.format(value / 1_000_000)} میلیون`
  return number.format(value)
}

function errorMessage(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string } } }).response
    return response?.data?.detail ?? fallback
  }
  return fallback
}

export default function PurchaseDashboardPanel({ companyId, fiscalYearId }: { companyId: string; fiscalYearId: string }) {
  const [data, setData] = useState<PurchaseDashboard | null>(null)
  const [dimensionId, setDimensionId] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const load = async (requestedDimensionId?: string) => {
    if (!companyId || !fiscalYearId) return
    setLoading(true); setError('')
    try {
      const response = await api.get<PurchaseDashboard | null>('/dashboard/purchase', {
        params: {
          companyId,
          fiscalYearId,
          dimensionId: requestedDimensionId || undefined,
          take: 50
        }
      })
      setData(response.data)
      setDimensionId(response.data?.selectedDimensionId ?? '')
    } catch (requestError) {
      setError(errorMessage(requestError, 'دریافت داشبورد بودجه خرید ناموفق بود.'))
    } finally { setLoading(false) }
  }

  useEffect(() => {
    setData(null); setDimensionId(''); setError('')
    void load()
  }, [companyId, fiscalYearId])

  if (loading && !data) return <Box display="flex" justifyContent="center" py={6}><CircularProgress /></Box>
  if (error && !data) return <Alert severity="error">{error}</Alert>
  if (!data) return <Alert severity="info">برای سال مالی انتخاب‌شده هنوز برنامه یا داده‌ای در مدل خرید (TRADE) وجود ندارد.</Alert>

  const budgetCostPercent = data.budgetPurchaseAmount === 0 ? 0 : data.budgetCostAmount / data.budgetPurchaseAmount * 100
  const forecastCostPercent = data.forecastPurchaseAmount === 0 ? 0 : data.forecastCostAmount / data.forecastPurchaseAmount * 100

  return <Stack spacing={2.5}>
    {error && <Alert severity="error">{error}</Alert>}

    <Card elevation={0} sx={{ background: 'linear-gradient(135deg, rgba(46,125,50,.08), rgba(25,118,210,.07))' }}>
      <CardContent>
        <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2}>
          <Box>
            <Stack direction="row" spacing={1} alignItems="center">
              <ShoppingCartCheckoutRoundedIcon color="primary" />
              <Typography variant="h6" fontWeight={900}>داشبورد بودجه و پیش‌بینی خرید</Typography>
            </Stack>
            <Typography color="text.secondary" variant="body2" mt={.75}>
              مبالغ خرید، هزینه‌های جانبی، تعداد، روند ماهانه و Drill-down دایمنشن‌ها از همان BudgetFactهای مدل TRADE خوانده می‌شوند.
            </Typography>
          </Box>
          <Typography variant="caption" color="text.secondary">
            نسخه {data.versionNumber.toLocaleString('fa-IR')} — {data.versionName} | ارز پایه: {data.currencyCode}
          </Typography>
        </Stack>
      </CardContent>
    </Card>

    <Box className="kpi-grid">
      {[
        ['بودجه تعداد خرید', number.format(data.budgetQuantity)],
        ['Forecast تعداد خرید', number.format(data.forecastQuantity)],
        ['بودجه مبلغ خرید', formatAmount(data.budgetPurchaseAmount)],
        ['Forecast مبلغ خرید', formatAmount(data.forecastPurchaseAmount)],
        ['بودجه هزینه‌های خرید', formatAmount(data.budgetCostAmount)],
        ['Forecast هزینه‌های خرید', formatAmount(data.forecastCostAmount)],
        ['بودجه کل خرید + هزینه', formatAmount(data.budgetTotalAmount)],
        ['Forecast کل خرید + هزینه', formatAmount(data.forecastTotalAmount)],
        ['انحراف Forecast از بودجه', formatAmount(data.varianceAmount)]
      ].map(([label, value]) => <Card key={label} className="kpi-card" elevation={0}><CardContent>
        <Typography color="text.secondary" variant="body2">{label}</Typography>
        <Typography variant="h5" fontWeight={900} mt={1}>{value}</Typography>
      </CardContent></Card>)}
    </Box>

    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={3} mb={2}>
        <Box><Typography color="text.secondary" variant="body2">نسبت هزینه به خرید در بودجه</Typography><Typography variant="h6" fontWeight={900}>{percent.format(budgetCostPercent)}٪</Typography></Box>
        <Box><Typography color="text.secondary" variant="body2">نسبت هزینه به خرید در Forecast</Typography><Typography variant="h6" fontWeight={900}>{percent.format(forecastCostPercent)}٪</Typography></Box>
      </Stack>
      <Typography variant="h6" fontWeight={900} mb={2}>روند ماهانه بودجه و Forecast خرید</Typography>
      <Box height={390}>
        <ResponsiveContainer width="100%" height="100%">
          <ComposedChart data={data.monthly}>
            <CartesianGrid strokeDasharray="3 3" />
            <XAxis dataKey="periodName" />
            <YAxis yAxisId="amount" />
            <YAxis yAxisId="qty" orientation="right" />
            <Tooltip formatter={(value: unknown) => number.format(Number(value ?? 0))} />
            <Legend />
            <Bar yAxisId="amount" dataKey="budgetTotalAmount" name="بودجه کل خرید" fill="#2e7d32" />
            <Bar yAxisId="amount" dataKey="forecastTotalAmount" name="Forecast کل خرید" fill="#1976d2" />
            <Line yAxisId="qty" type="monotone" dataKey="budgetQuantity" name="بودجه تعداد" stroke="#7b1fa2" strokeWidth={2} />
            <Line yAxisId="qty" type="monotone" dataKey="forecastQuantity" name="Forecast تعداد" stroke="#ed6c02" strokeWidth={2} />
          </ComposedChart>
        </ResponsiveContainer>
      </Box>
    </CardContent></Card>

    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900} mb={2}>تفکیک هزینه‌های خرید</Typography>
      <TableContainer sx={{ maxHeight: 420 }}><Table stickyHeader size="small">
        <TableHead><TableRow>
          <TableCell>نوع هزینه</TableCell>
          <TableCell align="left">بودجه</TableCell>
          <TableCell align="left">Forecast</TableCell>
          <TableCell align="left">انحراف</TableCell>
        </TableRow></TableHead>
        <TableBody>
          {data.costs.map(row => <TableRow key={`${row.costTypeId}:${row.code}`} hover>
            <TableCell><Typography fontWeight={800}>{row.name}</Typography><Typography variant="caption" color="text.secondary">{row.code}</Typography></TableCell>
            <TableCell align="left">{formatAmount(row.budgetAmount)}</TableCell>
            <TableCell align="left">{formatAmount(row.forecastAmount)}</TableCell>
            <TableCell align="left">{formatAmount(row.varianceAmount)}</TableCell>
          </TableRow>)}
          {!data.costs.length && <TableRow><TableCell colSpan={4}><Typography textAlign="center" color="text.secondary" py={2}>هزینه‌ای ثبت نشده است.</Typography></TableCell></TableRow>}
        </TableBody>
      </Table></TableContainer>
    </CardContent></Card>

    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2} mb={2}>
        <Box>
          <Typography variant="h6" fontWeight={900}>Drill-down دایمنشن‌های خرید</Typography>
          <Typography color="text.secondary" variant="body2">جمع بودجه و Forecast برای کالا، تأمین‌کننده، برند، قرارداد، پروژه، مرکز هزینه و سایر ابعاد متصل به TRADE.</Typography>
        </Box>
        <FormControl size="small" sx={{ minWidth: 260 }} disabled={!data.dimensions.length || loading}>
          <InputLabel>بُعد تحلیل</InputLabel>
          <Select value={dimensionId} label="بُعد تحلیل" onChange={event => { const next = event.target.value; setDimensionId(next); void load(next) }}>
            {data.dimensions.map(dimension => <MenuItem key={dimension.id} value={dimension.id}>{dimension.name} ({dimension.code})</MenuItem>)}
          </Select>
        </FormControl>
      </Stack>

      {loading && <Box display="flex" justifyContent="center" py={3}><CircularProgress size={26} /></Box>}
      {!loading && <TableContainer sx={{ maxHeight: 520 }}><Table stickyHeader size="small">
        <TableHead><TableRow>
          <TableCell>عضو بُعد</TableCell>
          <TableCell align="left">بودجه تعداد</TableCell>
          <TableCell align="left">Forecast تعداد</TableCell>
          <TableCell align="left">بودجه خرید</TableCell>
          <TableCell align="left">Forecast خرید</TableCell>
          <TableCell align="left">بودجه هزینه</TableCell>
          <TableCell align="left">Forecast هزینه</TableCell>
          <TableCell align="left">بودجه کل</TableCell>
          <TableCell align="left">Forecast کل</TableCell>
          <TableCell align="left">انحراف</TableCell>
        </TableRow></TableHead>
        <TableBody>
          {data.drilldown.map(row => <TableRow key={`${row.memberId}:${row.code}`} hover>
            <TableCell><Typography fontWeight={800}>{row.name}</Typography><Typography variant="caption" color="text.secondary">{row.code}</Typography></TableCell>
            <TableCell align="left">{number.format(row.budgetQuantity)}</TableCell>
            <TableCell align="left">{number.format(row.forecastQuantity)}</TableCell>
            <TableCell align="left">{formatAmount(row.budgetPurchaseAmount)}</TableCell>
            <TableCell align="left">{formatAmount(row.forecastPurchaseAmount)}</TableCell>
            <TableCell align="left">{formatAmount(row.budgetCostAmount)}</TableCell>
            <TableCell align="left">{formatAmount(row.forecastCostAmount)}</TableCell>
            <TableCell align="left">{formatAmount(row.budgetTotalAmount)}</TableCell>
            <TableCell align="left">{formatAmount(row.forecastTotalAmount)}</TableCell>
            <TableCell align="left">{formatAmount(row.varianceAmount)}</TableCell>
          </TableRow>)}
          {!data.drilldown.length && <TableRow><TableCell colSpan={10}><Typography textAlign="center" color="text.secondary" py={3}>برای بُعد انتخاب‌شده داده تفکیکی ثبت نشده است.</Typography></TableCell></TableRow>}
        </TableBody>
      </Table></TableContainer>}
    </CardContent></Card>
  </Stack>
}
