import { useEffect, useState } from 'react'
import { Alert, Box, Card, CardContent, CircularProgress, FormControl, InputLabel, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import PointOfSaleRoundedIcon from '@mui/icons-material/PointOfSaleRounded'
import { Bar, CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from './api'

type Dim = { id: string; code: string; name: string; sequence: number }
type Month = {
  periodId: string; periodName: string; sequence: number
  budgetQuantity: number; actualQuantity: number; forecastQuantity: number
  budgetGrossSales: number; actualGrossSales: number; forecastGrossSales: number
  budgetDiscount: number; actualDiscount: number; forecastDiscount: number
  budgetReturn: number; actualReturn: number; forecastReturn: number
  budgetNetSales: number; actualNetSales: number; forecastNetSales: number
  budgetCogs: number; actualCogs: number; forecastCogs: number
  budgetGrossProfit: number; actualGrossProfit: number; forecastGrossProfit: number
}
type Row = {
  memberId: string; code: string; name: string
  budgetQuantity: number; actualQuantity: number; forecastQuantity: number
  budgetGrossSales: number; actualGrossSales: number; forecastGrossSales: number
  budgetNetSales: number; actualNetSales: number; forecastNetSales: number
  budgetCogs: number; actualCogs: number; forecastCogs: number
  budgetGrossProfit: number; actualGrossProfit: number; forecastGrossProfit: number
  actualNetSalesVariance: number; forecastNetSalesVariance: number
}
type Data = {
  versionId: string; versionNumber: number; versionName: string; currencyCode: string
  budgetQuantity: number; actualQuantity: number; forecastQuantity: number
  budgetFreeQuantity: number; actualFreeQuantity: number; forecastFreeQuantity: number
  budgetGrossSales: number; actualGrossSales: number; forecastGrossSales: number
  budgetDiscount: number; actualDiscount: number; forecastDiscount: number
  budgetReturn: number; actualReturn: number; forecastReturn: number
  budgetNetSales: number; actualNetSales: number; forecastNetSales: number
  budgetCogs: number; actualCogs: number; forecastCogs: number
  budgetCompanyDiscount: number; actualCompanyDiscount: number; forecastCompanyDiscount: number
  budgetGrossProfit: number; actualGrossProfit: number; forecastGrossProfit: number
  actualNetSalesVariance: number; forecastNetSalesVariance: number
  monthly: Month[]; dimensions: Dim[]; selectedDimensionId?: string | null; drilldown: Row[]
}
const n = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 1 })
function amount(v: number) { const a = Math.abs(v); if (a >= 1e12) return `${n.format(v / 1e12)} همت`; if (a >= 1e9) return `${n.format(v / 1e9)} میلیارد`; if (a >= 1e6) return `${n.format(v / 1e6)} میلیون`; return n.format(v) }
function err(e: unknown) { return typeof e === 'object' && e !== null && 'response' in e ? (e as { response?: { data?: { detail?: string } } }).response?.data?.detail ?? 'خطا در داشبورد فروش' : 'خطا در داشبورد فروش' }

export default function SalesDashboardPanel({ companyId, fiscalYearId }: { companyId: string; fiscalYearId: string }) {
  const [data, setData] = useState<Data | null>(null); const [dimensionId, setDimensionId] = useState(''); const [busy, setBusy] = useState(false); const [error, setError] = useState('')
  const load = async (dim?: string) => { if (!companyId || !fiscalYearId) return; setBusy(true); setError(''); try { const r = await api.get<Data | null>('/dashboard/sales', { params: { companyId, fiscalYearId, dimensionId: dim || undefined, take: 50 } }); setData(r.data); setDimensionId(r.data?.selectedDimensionId ?? '') } catch (e) { setError(err(e)) } finally { setBusy(false) } }
  useEffect(() => { setData(null); void load() }, [companyId, fiscalYearId])
  if (busy && !data) return <Box py={5} textAlign="center"><CircularProgress /></Box>
  if (error && !data) return <Alert severity="error">{error}</Alert>
  if (!data) return <Alert severity="info">برای فروش هنوز داده TRADE ثبت نشده است.</Alert>

  const margin = (profit: number, sales: number) => sales === 0 ? 0 : profit / sales * 100
  return <Stack spacing={2.5}>
    {error && <Alert severity="error">{error}</Alert>}
    <Card elevation={0} sx={{ background: 'linear-gradient(135deg, rgba(2,132,199,.07), rgba(124,58,237,.06))' }}><CardContent><Stack direction="row" spacing={1} alignItems="center"><PointOfSaleRoundedIcon color="primary" /><Typography variant="h6" fontWeight={900}>داشبورد Budget / Actual / Forecast فروش</Typography></Stack><Typography color="text.secondary" variant="body2">نسخه {data.versionNumber.toLocaleString('fa-IR')} — {data.versionName}؛ Actual از Ledger/ERP یا Import کنترل‌شده خوانده می‌شود.</Typography></CardContent></Card>

    <Box className="kpi-grid">{[
      ['بودجه فروش خالص', amount(data.budgetNetSales)], ['عملکرد واقعی فروش خالص', amount(data.actualNetSales)], ['Forecast فروش خالص', amount(data.forecastNetSales)],
      ['بودجه تعداد فروش', n.format(data.budgetQuantity)], ['عملکرد واقعی تعداد', n.format(data.actualQuantity)], ['Forecast تعداد', n.format(data.forecastQuantity)],
      ['بودجه COGS', amount(data.budgetCogs)], ['عملکرد واقعی COGS', amount(data.actualCogs)], ['Forecast COGS', amount(data.forecastCogs)],
      ['بودجه سود ناخالص', amount(data.budgetGrossProfit)], ['عملکرد واقعی سود ناخالص', amount(data.actualGrossProfit)], ['Forecast سود ناخالص', amount(data.forecastGrossProfit)],
      ['انحراف Actual از بودجه', amount(data.actualNetSalesVariance)], ['انحراف Forecast از بودجه', amount(data.forecastNetSalesVariance)]
    ].map(([l, v]) => <Card key={l} className="kpi-card" elevation={0}><CardContent><Typography color="text.secondary" variant="body2">{l}</Typography><Typography variant="h5" fontWeight={900} mt={1}>{v}</Typography></CardContent></Card>)}</Box>

    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={4}>
        <Box><Typography color="text.secondary">حاشیه سود ناخالص بودجه</Typography><Typography variant="h6" fontWeight={900}>{n.format(margin(data.budgetGrossProfit, data.budgetNetSales))}٪</Typography></Box>
        <Box><Typography color="text.secondary">حاشیه سود ناخالص واقعی</Typography><Typography variant="h6" fontWeight={900}>{n.format(margin(data.actualGrossProfit, data.actualNetSales))}٪</Typography></Box>
        <Box><Typography color="text.secondary">حاشیه سود ناخالص Forecast</Typography><Typography variant="h6" fontWeight={900}>{n.format(margin(data.forecastGrossProfit, data.forecastNetSales))}٪</Typography></Box>
      </Stack>
      <Typography variant="h6" fontWeight={900} mt={2}>روند ماهانه فروش</Typography>
      <Box height={360}><ResponsiveContainer width="100%" height="100%"><ComposedChart data={data.monthly}><CartesianGrid strokeDasharray="3 3"/><XAxis dataKey="periodName"/><YAxis yAxisId="a"/><YAxis yAxisId="q" orientation="right"/><Tooltip formatter={(v: unknown) => n.format(Number(v ?? 0))}/><Legend/><Bar yAxisId="a" dataKey="budgetNetSales" name="بودجه فروش خالص" fill="#2563eb"/><Bar yAxisId="a" dataKey="actualNetSales" name="Actual فروش خالص" fill="#0f766e"/><Bar yAxisId="a" dataKey="forecastNetSales" name="Forecast فروش خالص" fill="#7c3aed"/><Line yAxisId="q" dataKey="actualQuantity" name="Actual تعداد" stroke="#d97706" strokeWidth={2}/></ComposedChart></ResponsiveContainer></Box>
      <TableContainer sx={{ maxHeight: 470 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>ماه</TableCell><TableCell>Bud تعداد</TableCell><TableCell>Act تعداد</TableCell><TableCell>Fct تعداد</TableCell><TableCell>Bud خالص</TableCell><TableCell>Act خالص</TableCell><TableCell>Fct خالص</TableCell><TableCell>Act تخفیف</TableCell><TableCell>Act برگشت</TableCell><TableCell>Bud COGS</TableCell><TableCell>Act COGS</TableCell><TableCell>Fct COGS</TableCell><TableCell>Bud سود</TableCell><TableCell>Act سود</TableCell><TableCell>Fct سود</TableCell></TableRow></TableHead><TableBody>{data.monthly.map(m => <TableRow key={m.periodId} hover><TableCell>{m.periodName}</TableCell><TableCell>{n.format(m.budgetQuantity)}</TableCell><TableCell>{n.format(m.actualQuantity)}</TableCell><TableCell>{n.format(m.forecastQuantity)}</TableCell><TableCell>{amount(m.budgetNetSales)}</TableCell><TableCell>{amount(m.actualNetSales)}</TableCell><TableCell>{amount(m.forecastNetSales)}</TableCell><TableCell>{amount(m.actualDiscount)}</TableCell><TableCell>{amount(m.actualReturn)}</TableCell><TableCell>{amount(m.budgetCogs)}</TableCell><TableCell>{amount(m.actualCogs)}</TableCell><TableCell>{amount(m.forecastCogs)}</TableCell><TableCell>{amount(m.budgetGrossProfit)}</TableCell><TableCell>{amount(m.actualGrossProfit)}</TableCell><TableCell>{amount(m.forecastGrossProfit)}</TableCell></TableRow>)}</TableBody></Table></TableContainer>
    </CardContent></Card>

    <Card elevation={0}><CardContent><Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} mb={2}><Box><Typography variant="h6" fontWeight={900}>Drill-down فروش</Typography><Typography color="text.secondary" variant="body2">کالا، کمپانی، مشتری، برند، منطقه، قرارداد و سایر ابعاد فروش.</Typography></Box><FormControl size="small" sx={{ minWidth: 250 }}><InputLabel>بُعد تحلیل</InputLabel><Select value={dimensionId} label="بُعد تحلیل" onChange={e => { setDimensionId(e.target.value); void load(e.target.value) }}>{data.dimensions.map(d => <MenuItem value={d.id} key={d.id}>{d.name} ({d.code})</MenuItem>)}</Select></FormControl></Stack>
      <TableContainer sx={{ maxHeight: 520 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>عضو</TableCell><TableCell>Bud تعداد</TableCell><TableCell>Act تعداد</TableCell><TableCell>Fct تعداد</TableCell><TableCell>Bud خالص</TableCell><TableCell>Act خالص</TableCell><TableCell>Fct خالص</TableCell><TableCell>Act COGS</TableCell><TableCell>Act سود</TableCell><TableCell>انحراف Actual</TableCell><TableCell>انحراف Forecast</TableCell></TableRow></TableHead><TableBody>{data.drilldown.map(r => <TableRow key={`${r.memberId}:${r.code}`} hover><TableCell><Typography fontWeight={800}>{r.name}</Typography><Typography variant="caption">{r.code}</Typography></TableCell><TableCell>{n.format(r.budgetQuantity)}</TableCell><TableCell>{n.format(r.actualQuantity)}</TableCell><TableCell>{n.format(r.forecastQuantity)}</TableCell><TableCell>{amount(r.budgetNetSales)}</TableCell><TableCell>{amount(r.actualNetSales)}</TableCell><TableCell>{amount(r.forecastNetSales)}</TableCell><TableCell>{amount(r.actualCogs)}</TableCell><TableCell>{amount(r.actualGrossProfit)}</TableCell><TableCell>{amount(r.actualNetSalesVariance)}</TableCell><TableCell>{amount(r.forecastNetSalesVariance)}</TableCell></TableRow>)}</TableBody></Table></TableContainer>
    </CardContent></Card>
  </Stack>
}
