import { useEffect, useMemo, useState } from 'react'
import { Alert, Box, Card, CardContent, Chip, CircularProgress, FormControl, InputLabel, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from './api'

type Model = { id: string; code: string; name: string }
type Dimension = { id: string; code: string; name: string; sequence: number; isRequired: boolean }
type Measure = { id: string; code: string; name: string; unit?: string; valueType: number; aggregation: number; isCalculated: boolean }
type Item = { memberId: string; memberCode: string; memberName: string; budget: number; actual: number; commitment: number; forecast: number; variance: number; variancePercent?: number | null; achievementPercent?: number | null }
type Result = { versionId: string; versionNumber: number; measure: Measure; rowDimension: Dimension; totalBudget: number; totalActual: number; totalCommitment: number; totalForecast: number; items: Item[] }

const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })

function compact(value: number) {
  const abs = Math.abs(value)
  if (abs >= 1_000_000_000_000) return `${number.format(value / 1_000_000_000_000)} همت`
  if (abs >= 1_000_000_000) return `${number.format(value / 1_000_000_000)} میلیارد`
  if (abs >= 1_000_000) return `${number.format(value / 1_000_000)} میلیون`
  return number.format(value)
}

function apiError(error: unknown) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string } } }).response
    if (response?.data?.detail) return response.data.detail
  }
  return 'تحلیل انحراف ناموفق بود.'
}

export default function VarianceAnalysis({ companyId, fiscalYearId }: { companyId: string; fiscalYearId: string }) {
  const [models, setModels] = useState<Model[]>([])
  const [dimensions, setDimensions] = useState<Dimension[]>([])
  const [measures, setMeasures] = useState<Measure[]>([])
  const [modelId, setModelId] = useState('')
  const [dimensionId, setDimensionId] = useState('')
  const [measureId, setMeasureId] = useState('')
  const [take, setTake] = useState(20)
  const [result, setResult] = useState<Result | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!companyId) return
    setResult(null); setError('')
    api.get<Model[]>('/reference/models', { params: { companyId } }).then(response => {
      setModels(response.data)
      setModelId(current => response.data.some(x => x.id === current) ? current : response.data[0]?.id ?? '')
    }).catch(() => setError('دریافت مدل‌های بودجه ناموفق بود.'))
  }, [companyId])

  useEffect(() => {
    if (!modelId) return
    setResult(null); setLoading(true); setError('')
    Promise.all([
      api.get<Dimension[]>('/reference/dimensions', { params: { modelId } }),
      api.get<Measure[]>('/reference/measures', { params: { modelId } })
    ]).then(([dimensionResponse, measureResponse]) => {
      setDimensions(dimensionResponse.data); setMeasures(measureResponse.data)
      setDimensionId(current => dimensionResponse.data.some(x => x.id === current) ? current : dimensionResponse.data[0]?.id ?? '')
      const preferred = measureResponse.data.find(x => x.valueType === 0) ?? measureResponse.data[0]
      setMeasureId(current => measureResponse.data.some(x => x.id === current) ? current : preferred?.id ?? '')
    }).catch(() => setError('دریافت ابعاد و شاخص‌های مدل ناموفق بود.')).finally(() => setLoading(false))
  }, [modelId])

  useEffect(() => {
    if (!companyId || !fiscalYearId || !modelId || !dimensionId || !measureId) return
    setLoading(true); setError('')
    api.post<Result>('/analytics/variance', {
      companyId, fiscalYearId, budgetModelId: modelId, measureId, rowDimensionId: dimensionId, filters: [], take
    }).then(response => setResult(response.data)).catch(error => { setResult(null); setError(apiError(error)) }).finally(() => setLoading(false))
  }, [companyId, fiscalYearId, modelId, dimensionId, measureId, take])

  const variance = (result?.totalActual ?? 0) - (result?.totalBudget ?? 0)
  const achievement = result?.totalBudget ? (result.totalActual / result.totalBudget) * 100 : null
  const chartData = useMemo(() => result?.items.slice(0, 12).map(item => ({ name: item.memberName, budget: item.budget, actual: item.actual })) ?? [], [result])

  return <Stack spacing={2.5}>
    <Card elevation={0}><CardContent>
      <Box><Typography variant="h6" fontWeight={900}>تحلیل انحراف بودجه و عملکرد</Typography><Typography color="text.secondary" mb={2}>بزرگ‌ترین انحراف‌ها را روی هر مدل، مژر و بُعد سازمانی/تحلیلی مشاهده کنید.</Typography></Box>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5}>
        <FormControl size="small" sx={{ minWidth: 230 }}><InputLabel>مدل بودجه</InputLabel><Select value={modelId} label="مدل بودجه" onChange={e => setModelId(e.target.value)}>{models.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 220 }}><InputLabel>مژر</InputLabel><Select value={measureId} label="مژر" onChange={e => setMeasureId(e.target.value)}>{measures.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 200 }}><InputLabel>تحلیل بر اساس</InputLabel><Select value={dimensionId} label="تحلیل بر اساس" onChange={e => setDimensionId(e.target.value)}>{dimensions.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 150 }}><InputLabel>تعداد ردیف</InputLabel><Select value={take} label="تعداد ردیف" onChange={e => setTake(Number(e.target.value))}><MenuItem value={10}>۱۰</MenuItem><MenuItem value={20}>۲۰</MenuItem><MenuItem value={50}>۵۰</MenuItem><MenuItem value={100}>۱۰۰</MenuItem></Select></FormControl>
      </Stack>
    </CardContent></Card>

    {error && <Alert severity="error">{error}</Alert>}
    {loading && <Box py={6} textAlign="center"><CircularProgress /></Box>}

    {!loading && result && <>
      <Box className="kpi-grid">
        <Metric title="بودجه" value={compact(result.totalBudget)} />
        <Metric title="عملکرد واقعی" value={compact(result.totalActual)} />
        <Metric title="انحراف" value={`${variance >= 0 ? '+' : ''}${compact(variance)}`} />
        <Metric title="درصد تحقق" value={achievement == null ? '-' : `${number.format(achievement)}٪`} />
      </Box>

      <Card elevation={0}><CardContent>
        <Stack direction="row" justifyContent="space-between" alignItems="center" mb={2}><Box><Typography variant="h6" fontWeight={900}>بیشترین انحراف‌ها</Typography><Typography variant="body2" color="text.secondary">نسخه {result.versionNumber} — {result.rowDimension.name} — {result.measure.name}</Typography></Box><Chip label={`${result.items.length.toLocaleString('fa-IR')} ردیف`} /></Stack>
        {chartData.length > 0 && <Box sx={{ height: 360, direction: 'ltr' }}><ResponsiveContainer width="100%" height="100%"><BarChart data={chartData}><CartesianGrid strokeDasharray="3 3" vertical={false} /><XAxis dataKey="name" interval={0} angle={-20} textAnchor="end" height={90} /><YAxis tickFormatter={value => compact(Number(value))} /><Tooltip formatter={value => compact(Number(value))} /><Legend /><Bar dataKey="budget" name="بودجه" fill="#0b5cad" /><Bar dataKey="actual" name="عملکرد" fill="#00a6a6" /></BarChart></ResponsiveContainer></Box>}
      </CardContent></Card>

      <Card elevation={0}><CardContent sx={{ p: 0 }}><TableContainer sx={{ maxHeight: '65vh' }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>کد / شرح</TableCell><TableCell align="left">بودجه</TableCell><TableCell align="left">عملکرد</TableCell><TableCell align="left">تعهد</TableCell><TableCell align="left">پیش‌بینی</TableCell><TableCell align="left">انحراف</TableCell><TableCell align="left">درصد انحراف</TableCell><TableCell align="left">تحقق</TableCell></TableRow></TableHead><TableBody>{result.items.map(item => <TableRow key={item.memberId} hover><TableCell><Typography fontWeight={800}>{item.memberName}</Typography><Typography variant="caption" color="text.secondary">{item.memberCode}</Typography></TableCell><TableCell align="left">{compact(item.budget)}</TableCell><TableCell align="left">{compact(item.actual)}</TableCell><TableCell align="left">{compact(item.commitment)}</TableCell><TableCell align="left">{compact(item.forecast)}</TableCell><TableCell align="left"><Typography fontWeight={800} color={item.variance > 0 ? 'error.main' : item.variance < 0 ? 'success.main' : 'text.primary'}>{item.variance >= 0 ? '+' : ''}{compact(item.variance)}</Typography></TableCell><TableCell align="left">{item.variancePercent == null ? '-' : `${item.variancePercent >= 0 ? '+' : ''}${number.format(item.variancePercent)}٪`}</TableCell><TableCell align="left">{item.achievementPercent == null ? '-' : `${number.format(item.achievementPercent)}٪`}</TableCell></TableRow>)}</TableBody></Table></TableContainer></CardContent></Card>
    </>}
  </Stack>
}

function Metric({ title, value }: { title: string; value: string }) {
  return <Card elevation={0} className="kpi-card"><CardContent><Typography color="text.secondary" fontWeight={700}>{title}</Typography><Typography variant="h6" fontWeight={900} mt={1}>{value}</Typography></CardContent></Card>
}
