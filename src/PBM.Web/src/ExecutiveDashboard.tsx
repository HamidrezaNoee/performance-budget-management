import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Card, CardContent, CircularProgress, FormControl, InputLabel, MenuItem, Select,
  Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography
} from '@mui/material'
import { Bar, CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from './api'
import PortfolioFinancialPanel from './PortfolioFinancialPanel'
import PurchaseDashboardPanel from './PurchaseDashboardPanel'
import SalesDashboardPanel from './SalesDashboardPanel'
import ExpenseDashboardPanel from './ExpenseDashboardPanel'

type MonthlyPoint = { periodId: string; periodName: string; sequence: number; budget: number; actual: number; commitment: number; forecast: number }
type DashboardSummary = { budget: number; actual: number; commitment: number; forecast: number; remaining: number; variance: number; budgetUtilizationPercent: number; monthly: MonthlyPoint[] }
type MetricOption = { code: string; name: string; unit?: string | null; currencyCode: string; displayOrder: number }
type MeasureSummary = { measureCode: string; measureName: string; unit?: string | null; currencyCode: string; summary: DashboardSummary }
type DimensionOption = { id: string; code: string; name: string; sequence: number }
type DrilldownRow = { memberId: string; code: string; name: string; budget: number; actual: number; commitment: number; forecast: number; remaining: number; variance: number; budgetUtilizationPercent: number }
type DrilldownResult = { dimensionId: string; dimensionCode: string; dimensionName: string; measureCode: string; measureName: string; unit?: string | null; currencyCode: string; totalMemberCount: number; rows: DrilldownRow[] }

const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 0 })
const percent = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 1 })
function formatAmount(value: number) { const abs = Math.abs(value); if (abs >= 1_000_000_000_000) return `${number.format(value / 1_000_000_000_000)} همت`; if (abs >= 1_000_000_000) return `${number.format(value / 1_000_000_000)} میلیارد`; if (abs >= 1_000_000) return `${number.format(value / 1_000_000)} میلیون`; return number.format(value) }
function errorMessage(error: any, fallback: string) { return error?.response?.data?.detail ?? fallback }

export default function ExecutiveDashboard({ companyId, fiscalYearId }: { companyId: string; fiscalYearId: string }) {
  const [metrics, setMetrics] = useState<MetricOption[]>([]); const [metricCode, setMetricCode] = useState(''); const [measureSummary, setMeasureSummary] = useState<MeasureSummary | null>(null)
  const [dimensions, setDimensions] = useState<DimensionOption[]>([]); const [dimensionId, setDimensionId] = useState(''); const [drilldown, setDrilldown] = useState<DrilldownResult | null>(null)
  const [loadingMetrics, setLoadingMetrics] = useState(false); const [loadingSummary, setLoadingSummary] = useState(false); const [loadingDrilldown, setLoadingDrilldown] = useState(false); const [error, setError] = useState('')

  useEffect(() => {
    let active = true; setMetrics([]); setMetricCode(''); setMeasureSummary(null); setDimensions([]); setDimensionId(''); setDrilldown(null); setLoadingSummary(false); setLoadingDrilldown(false); setError('')
    if (!companyId || !fiscalYearId) return () => { active = false }
    setLoadingMetrics(true)
    api.get<MetricOption[]>('/dashboard/metrics', { params: { companyId, fiscalYearId } }).then(response => { if (!active) return; setMetrics(response.data); setMetricCode(response.data[0]?.code ?? '') }).catch(e => { if (active) setError(errorMessage(e, 'دریافت شاخص‌های داشبورد ناموفق بود.')) }).finally(() => { if (active) setLoadingMetrics(false) })
    return () => { active = false }
  }, [companyId, fiscalYearId])

  useEffect(() => {
    let active = true; setMeasureSummary(null); setDimensions([]); setDimensionId(''); setDrilldown(null); setLoadingDrilldown(false); setError('')
    if (!companyId || !fiscalYearId || !metricCode) return () => { active = false }
    setLoadingSummary(true)
    Promise.all([
      api.get<MeasureSummary>('/dashboard/summary-by-measure', { params: { companyId, fiscalYearId, measureCode: metricCode } }),
      api.get<DimensionOption[]>('/dashboard/drilldown/dimensions', { params: { companyId, fiscalYearId, measureCode: metricCode } })
    ]).then(([s, d]) => { if (!active) return; setMeasureSummary(s.data); setDimensions(d.data); setDimensionId(d.data[0]?.id ?? '') }).catch(e => { if (active) setError(errorMessage(e, 'بارگذاری داشبورد برای شاخص انتخاب‌شده ناموفق بود.')) }).finally(() => { if (active) setLoadingSummary(false) })
    return () => { active = false }
  }, [companyId, fiscalYearId, metricCode])

  useEffect(() => {
    let active = true; setDrilldown(null); setLoadingDrilldown(false)
    if (!companyId || !fiscalYearId || !metricCode || !dimensionId) return () => { active = false }
    setError(''); setLoadingDrilldown(true)
    api.get<DrilldownResult>('/dashboard/drilldown', { params: { companyId, fiscalYearId, measureCode: metricCode, dimensionId, take: 50 } }).then(r => { if (active) setDrilldown(r.data) }).catch(e => { if (active) setError(errorMessage(e, 'دریافت ریزتحلیل بُعدی ناموفق بود.')) }).finally(() => { if (active) setLoadingDrilldown(false) })
    return () => { active = false }
  }, [companyId, fiscalYearId, metricCode, dimensionId])

  const selectedMetric = useMemo(() => metrics.find(x => x.code === metricCode), [metrics, metricCode])
  const summary = measureSummary?.summary ?? null
  const commercialPanels = <>
    <PortfolioFinancialPanel companyId={companyId} fiscalYearId={fiscalYearId} />
    <PurchaseDashboardPanel companyId={companyId} fiscalYearId={fiscalYearId} />
    <SalesDashboardPanel companyId={companyId} fiscalYearId={fiscalYearId} />
    <ExpenseDashboardPanel companyId={companyId} fiscalYearId={fiscalYearId} />
  </>

  if (loadingMetrics) return <Stack spacing={3}>{commercialPanels}<Box display="flex" justifyContent="center" py={8}><CircularProgress /></Box></Stack>
  if (error && !metrics.length) return <Stack spacing={3}>{commercialPanels}<Alert severity="error">{error}</Alert></Stack>
  if (!metrics.length) return <Stack spacing={3}>{commercialPanels}<Alert severity="info">برای شرکت و سال مالی انتخاب‌شده شاخص مبلغی عمومی قابل نمایش وجود ندارد.</Alert></Stack>

  return <Stack spacing={3}>
    {commercialPanels}
    {error && <Alert severity="error">{error}</Alert>}
    <Card elevation={0}><CardContent><Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems={{ md: 'center' }} justifyContent="space-between"><Box><Typography variant="h6" fontWeight={900}>شاخص داشبورد اجرایی</Typography><Typography color="text.secondary" variant="body2">کارت‌ها، روند ماهانه و Drill-down عمومی بر اساس یک Measure واحد هستند؛ پنل‌های Portfolio، خرید، فروش و هزینه در بالا گزارش‌های دامنه‌ای مستقل‌اند.</Typography></Box><FormControl size="small" sx={{ minWidth: 280 }}><InputLabel>شاخص مالی</InputLabel><Select value={metricCode} label="شاخص مالی" onChange={e => setMetricCode(e.target.value)}>{metrics.map(m => <MenuItem key={m.code} value={m.code}>{m.name} ({m.code})</MenuItem>)}</Select></FormControl></Stack>{selectedMetric && <Typography variant="caption" color="text.secondary" display="block" mt={1.5}>واحد: {selectedMetric.unit || 'مبلغ'} — ارز پایه: {selectedMetric.currencyCode}</Typography>}</CardContent></Card>
    {loadingSummary && <Box display="flex" justifyContent="center" py={6}><CircularProgress /></Box>}
    {!loadingSummary && summary && <>
      <Box className="kpi-grid">{[['بودجه مصوب', formatAmount(summary.budget)], ['عملکرد واقعی', formatAmount(summary.actual)], ['تعهدات', formatAmount(summary.commitment)], ['پیش‌بینی پایان سال', formatAmount(summary.forecast)], ['مانده بودجه', formatAmount(summary.remaining)], ['انحراف', formatAmount(summary.variance)], ['درصد تحقق', `${percent.format(summary.budgetUtilizationPercent)}٪`]].map(([label, value]) => <Card key={label} className="kpi-card" elevation={0}><CardContent><Typography color="text.secondary" variant="body2">{label}</Typography><Typography variant="h5" fontWeight={900} mt={1}>{value}</Typography></CardContent></Card>)}</Box>
      <Card elevation={0}><CardContent><Typography variant="h6" fontWeight={800} mb={2}>روند ماهانه {measureSummary?.measureName}</Typography><Box height={360}><ResponsiveContainer width="100%" height="100%"><ComposedChart data={summary.monthly}><CartesianGrid strokeDasharray="3 3"/><XAxis dataKey="periodName"/><YAxis/><Tooltip formatter={(v: unknown) => number.format(Number(v ?? 0))}/><Legend/><Bar dataKey="budget" name="بودجه" fill="#1d4ed8"/><Bar dataKey="actual" name="عملکرد" fill="#0f766e"/><Bar dataKey="commitment" name="تعهدات" fill="#7c3aed"/><Line type="monotone" dataKey="forecast" name="پیش‌بینی" stroke="#b45309" strokeWidth={2}/></ComposedChart></ResponsiveContainer></Box></CardContent></Card>
      <Card elevation={0}><CardContent><Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems={{ md: 'center' }} justifyContent="space-between" mb={2}><Box><Typography variant="h6" fontWeight={900}>Drill-down چندبعدی عمومی</Typography><Typography color="text.secondary" variant="body2">۵۰ عضو اول بُعد بر اساس عملکرد واقعی و سپس بودجه.</Typography></Box><FormControl size="small" sx={{ minWidth: 260 }} disabled={!dimensions.length}><InputLabel>بُعد تحلیل</InputLabel><Select value={dimensionId} label="بُعد تحلیل" onChange={e => setDimensionId(e.target.value)}>{dimensions.map(d => <MenuItem key={d.id} value={d.id}>{d.name} ({d.code})</MenuItem>)}</Select></FormControl></Stack>
        {!dimensions.length && <Alert severity="info">برای این شاخص بُعد قابل Drill-down تعریف نشده است.</Alert>}{loadingDrilldown && <Box display="flex" justifyContent="center" py={5}><CircularProgress size={28}/></Box>}{!loadingDrilldown && drilldown && <><Typography variant="caption" color="text.secondary" display="block" mb={1.5}>{drilldown.dimensionName} — {number.format(drilldown.totalMemberCount)} عضو دارای داده</Typography><TableContainer sx={{ maxHeight: 520 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>عضو</TableCell><TableCell>بودجه</TableCell><TableCell>عملکرد</TableCell><TableCell>تعهد</TableCell><TableCell>پیش‌بینی</TableCell><TableCell>مانده</TableCell><TableCell>انحراف</TableCell><TableCell>تحقق</TableCell></TableRow></TableHead><TableBody>{drilldown.rows.map(r => <TableRow key={r.memberId} hover><TableCell><Typography fontWeight={800}>{r.name}</Typography><Typography variant="caption" color="text.secondary">{r.code}</Typography></TableCell><TableCell>{formatAmount(r.budget)}</TableCell><TableCell>{formatAmount(r.actual)}</TableCell><TableCell>{formatAmount(r.commitment)}</TableCell><TableCell>{formatAmount(r.forecast)}</TableCell><TableCell>{formatAmount(r.remaining)}</TableCell><TableCell>{formatAmount(r.variance)}</TableCell><TableCell>{percent.format(r.budgetUtilizationPercent)}٪</TableCell></TableRow>)}{!drilldown.rows.length && <TableRow><TableCell colSpan={8}><Typography color="text.secondary" textAlign="center" py={3}>داده‌ای برای این بُعد ثبت نشده است.</Typography></TableCell></TableRow>}</TableBody></Table></TableContainer></>}
      </CardContent></Card>
    </>}
  </Stack>
}
