import { useEffect, useState } from 'react'
import { Alert, Box, Card, CardContent, FormControl, InputLabel, MenuItem, Select, Stack, Typography } from '@mui/material'
import { CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from './api'

type Model = { id: string; code: string; name: string }
type Measure = { id: string; code: string; name: string; unit?: string; valueType: number; isCalculated: boolean }
type Point = { periodId: string; periodName: string; sequence: number; actual?: number; predicted: number; isFuture: boolean }
type Result = { measureName: string; method: number; slope?: number; intercept?: number; rSquared?: number; points: Point[] }

const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })

export default function Forecasting({ companyId, fiscalYearId }: { companyId: string; fiscalYearId: string }) {
  const [models, setModels] = useState<Model[]>([]); const [modelId, setModelId] = useState(''); const [measures, setMeasures] = useState<Measure[]>([]); const [measureId, setMeasureId] = useState(''); const [method, setMethod] = useState(0); const [result, setResult] = useState<Result | null>(null); const [error, setError] = useState('')

  useEffect(() => { api.get<Model[]>('/reference/models', { params: { companyId } }).then(r => { setModels(r.data); setModelId(r.data[0]?.id ?? '') }).catch(() => setError('دریافت مدل‌های بودجه ناموفق بود.')) }, [companyId])
  useEffect(() => { if (!modelId) return; api.get<Measure[]>('/reference/measures', { params: { modelId } }).then(r => { setMeasures(r.data); setMeasureId(r.data.find(x => x.valueType === 0)?.id ?? r.data[0]?.id ?? '') }).catch(() => setError('دریافت مژرها ناموفق بود.')) }, [modelId])
  useEffect(() => { if (!measureId) return; setError(''); api.get<Result>('/forecast', { params: { companyId, fiscalYearId, measureId, method } }).then(r => setResult(r.data)).catch(() => { setResult(null); setError('برای این مژر Actual کافی جهت پیش‌بینی وجود ندارد.') }) }, [companyId, fiscalYearId, measureId, method])

  return <Stack spacing={2.5}>
    <Card elevation={0}><CardContent><Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} alignItems={{ md: 'center' }}><Box flexGrow={1}><Typography variant="h6" fontWeight={900}>Forecast مبتنی بر روند</Typography><Typography color="text.secondary">نسخه اول موتور پیش‌بینی با Linear Trend و میانگین متحرک سه‌دوره‌ای. روش‌های آماری و AI در همین Contract قابل اضافه شدن هستند.</Typography></Box><FormControl size="small" sx={{ minWidth: 200 }}><InputLabel>مدل</InputLabel><Select label="مدل" value={modelId} onChange={e => setModelId(e.target.value)}>{models.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl><FormControl size="small" sx={{ minWidth: 220 }}><InputLabel>مژر</InputLabel><Select label="مژر" value={measureId} onChange={e => setMeasureId(e.target.value)}>{measures.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl><FormControl size="small" sx={{ minWidth: 190 }}><InputLabel>روش</InputLabel><Select label="روش" value={method} onChange={e => setMethod(Number(e.target.value))}><MenuItem value={0}>روند خطی</MenuItem><MenuItem value={1}>میانگین متحرک ۳ دوره</MenuItem></Select></FormControl></Stack></CardContent></Card>
    {error && <Alert severity="warning">{error}</Alert>}
    {result && <><Box className="kpi-grid"><Metric title="مژر" value={result.measureName} /><Metric title="روش" value={result.method === 0 ? 'Linear Trend' : 'Moving Average 3'} /><Metric title="R²" value={result.rSquared == null ? '-' : number.format(result.rSquared)} /><Metric title="شیب" value={result.slope == null ? '-' : number.format(result.slope)} /></Box><Card elevation={0}><CardContent><Typography variant="h6" fontWeight={900} mb={2}>Actual و مقدار پیش‌بینی‌شده</Typography><Box sx={{ height: 420, direction: 'ltr' }}><ResponsiveContainer width="100%" height="100%"><ComposedChart data={result.points}><CartesianGrid strokeDasharray="3 3" vertical={false} /><XAxis dataKey="periodName" /><YAxis /><Tooltip formatter={v => number.format(Number(v))} /><Legend /><Line dataKey="actual" name="Actual" stroke="#00a6a6" strokeWidth={3} connectNulls={false} /><Line dataKey="predicted" name="Forecast" stroke="#ef8c22" strokeWidth={3} strokeDasharray="6 4" /></ComposedChart></ResponsiveContainer></Box></CardContent></Card></>}
  </Stack>
}

function Metric({ title, value }: { title: string; value: string }) { return <Card elevation={0} className="kpi-card"><CardContent><Typography color="text.secondary" fontWeight={700}>{title}</Typography><Typography variant="h6" fontWeight={900} mt={1}>{value}</Typography></CardContent></Card> }
