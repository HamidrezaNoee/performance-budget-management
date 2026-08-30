import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, CircularProgress, FormControl, InputLabel, MenuItem, Select,
  Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, ToggleButton,
  ToggleButtonGroup, Typography
} from '@mui/material'
import PointOfSaleRoundedIcon from '@mui/icons-material/PointOfSaleRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import { api } from './api'

type Member = { id: string; dimensionId: string; code: string; name: string }
type Dimension = { id: string; code: string; name: string; sequence: number; isRequired: boolean; members: Member[] }
type Measure = { id: string; code: string; name: string; unit?: string | null; valueType: number; isCalculated: boolean }
type Setup = { modelId: string; modelName: string; baseCurrencyCode: string; dimensions: Dimension[]; measures: Measure[] }
type Version = { id: string; scenarioId: string; versionNumber: number; name: string; status: number; isLocked: boolean }
type Plan = { id: string; budgetModelId: string; name: string; versions: Version[] }
type Period = { id: string; sequence: number; code: string; name: string; isClosed: boolean }
type PeriodValue = { periodId: string; periodName: string; sequence: number; value: number; factId?: string | null }
type Series = { measureCode: string; name: string; unit?: string | null; isCalculated: boolean; values: PeriodValue[] }
type Data = { periods: Period[]; series: Series[] }
type PlanningKind = 0 | 1 | 3

const primaryDimensionCodes = new Set(['PRODUCT', 'SUPPLIER', 'BRAND', 'CURRENCY'])
const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })
const compact = new Intl.NumberFormat('fa-IR', { notation: 'compact', maximumFractionDigits: 1 })

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error)
    return (error as { response?: { data?: { detail?: string } } }).response?.data?.detail ?? fallback
  return fallback
}
function value(data: Data | null, code: string, periodId: string) { return Number(data?.series.find(x => x.measureCode === code)?.values.find(x => x.periodId === periodId)?.value ?? 0) }

export default function SalesPlanner({ companyId, fiscalYearId, canWrite }: { companyId: string; fiscalYearId: string; canWrite: boolean }) {
  const [setup, setSetup] = useState<Setup | null>(null); const [plan, setPlan] = useState<Plan | null>(null); const [versionId, setVersionId] = useState('')
  const [selections, setSelections] = useState<Record<string, string>>({}); const [valueKind, setValueKind] = useState<PlanningKind>(0); const [data, setData] = useState<Data | null>(null)
  const [busy, setBusy] = useState(false); const [savingKey, setSavingKey] = useState(''); const [error, setError] = useState('')
  const versions = useMemo(() => [...(plan?.versions ?? [])].sort((a, b) => b.versionNumber - a.versionNumber), [plan])
  const version = versions.find(x => x.id === versionId) ?? versions[0]
  const isActual = valueKind === 1
  const editable = canWrite && !isActual && !!version && version.status === 0 && !version.isLocked
  const visibleDimensions = useMemo(() => setup?.dimensions.filter(d => primaryDimensionCodes.has(d.code.toUpperCase())) ?? [], [setup])
  const selectedDimensions = useMemo(() => visibleDimensions.filter(d => !!selections[d.id]).map(d => ({ dimensionId: d.id, memberId: selections[d.id] })), [visibleDimensions, selections])
  const requiredReady = useMemo(() => !!setup && visibleDimensions.filter(d => d.isRequired || d.code.toUpperCase() === 'PRODUCT').every(d => !!selections[d.id]), [setup, visibleDimensions, selections])

  const loadSetup = async () => {
    if (!companyId || !fiscalYearId) return
    setBusy(true); setError(''); setData(null)
    try {
      const [s, p] = await Promise.all([api.get<Setup>('/sales-planning/setup', { params: { companyId } }), api.get<Plan[]>('/budget/plans', { params: { companyId, fiscalYearId } })])
      setSetup(s.data); const current = p.data.find(x => x.budgetModelId === s.data.modelId) ?? null; setPlan(current)
      const latest = [...(current?.versions ?? [])].sort((a, b) => b.versionNumber - a.versionNumber)[0]; setVersionId(latest?.id ?? '')
      setSelections({})
    } catch (e) { setError(apiError(e, 'دریافت تنظیمات فروش ناموفق بود.')) } finally { setBusy(false) }
  }
  useEffect(() => { void loadSetup() }, [companyId, fiscalYearId])

  const query = async () => {
    if (!version || !requiredReady) { setData(null); return }
    setBusy(true); setError('')
    try { const r = await api.post<Data>('/sales-planning/query', { versionId: version.id, dimensions: selectedDimensions, valueKind }); setData(r.data) }
    catch (e) { setError(apiError(e, 'بارگذاری Budget / Actual / Forecast فروش ناموفق بود.')) } finally { setBusy(false) }
  }
  useEffect(() => { if (version && requiredReady) void query(); else setData(null) }, [version?.id, valueKind, requiredReady, JSON.stringify(selectedDimensions)])

  const createPlan = async () => {
    if (!setup || !canWrite) return; setBusy(true); setError('')
    try { const r = await api.post<Plan>('/budget/plans', { companyId, fiscalYearId, budgetModelId: setup.modelId, name: 'برنامه خرید و فروش' }); setPlan(r.data); setVersionId(r.data.versions[0]?.id ?? '') }
    catch (e) { setError(apiError(e, 'ایجاد برنامه TRADE ناموفق بود.')) } finally { setBusy(false) }
  }
  const save = async (code: string, periodId: string, raw: string) => {
    if (!editable || !version || !requiredReady) return
    const numeric = Number(raw); if (!Number.isFinite(numeric) || numeric < 0) { setError('مقدار فروش باید عددی و نامنفی باشد.'); return }
    const key = `${code}:${periodId}`; setSavingKey(key); setError('')
    try { await api.post('/sales-planning/cell', { versionId: version.id, periodId, measureCode: code, value: numeric, dimensions: selectedDimensions, valueKind, note: null }); await query() }
    catch (e) { setError(apiError(e, 'ذخیره مقدار فروش ناموفق بود.')) } finally { setSavingKey('') }
  }

  const total = (code: string) => data?.series.find(x => x.measureCode === code)?.values.reduce((s, x) => s + Number(x.value || 0), 0) ?? 0
  const average = (code: string) => { const values = data?.series.find(x => x.measureCode === code)?.values.map(x => Number(x.value || 0)).filter(x => x !== 0) ?? []; return values.length ? values.reduce((a, b) => a + b, 0) / values.length : 0 }
  if (busy && !setup) return <Box py={8} textAlign="center"><CircularProgress /></Box>

  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    <Card elevation={0} sx={{ background: 'linear-gradient(135deg, rgba(2,132,199,.08), rgba(124,58,237,.06))' }}><CardContent><Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} alignItems={{ md: 'center' }}><Box><Stack direction="row" spacing={1} alignItems="center"><PointOfSaleRoundedIcon color="primary" /><Typography variant="h5" fontWeight={900}>Budget / Actual / Forecast چندبعدی فروش</Typography></Stack><Typography color="text.secondary" mt={1}>فعلاً فرم فروش فقط بر کالا، تأمین‌کننده، برند و ارز متمرکز است؛ سایر Dimensionها در اطلاعات پایه نگهداری می‌شوند و بعداً به فرم عملیاتی اضافه می‌شوند.</Typography></Box><Button startIcon={<RefreshRoundedIcon />} onClick={() => void loadSetup()} disabled={busy}>بازخوانی</Button></Stack></CardContent></Card>
    {!plan && <Alert severity="info" action={canWrite ? <Button color="inherit" onClick={createPlan}>ایجاد برنامه</Button> : undefined}>برای سال مالی انتخاب‌شده برنامه TRADE وجود ندارد.</Alert>}
    {setup && <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.3} flexWrap="wrap" useFlexGap alignItems={{ lg: 'center' }}>
        <ToggleButtonGroup exclusive size="small" value={valueKind} onChange={(_, v: PlanningKind | null) => v !== null && setValueKind(v)}><ToggleButton value={0}>بودجه فروش</ToggleButton><ToggleButton value={1}>عملکرد واقعی</ToggleButton><ToggleButton value={3}>Forecast فروش</ToggleButton></ToggleButtonGroup>
        {visibleDimensions.map(d => <FormControl key={d.id} size="small" sx={{ minWidth: 205 }}>
          <InputLabel>{d.name}{d.isRequired || d.code.toUpperCase() === 'PRODUCT' ? ' *' : ''}</InputLabel>
          <Select
            displayEmpty
            value={selections[d.id] ?? ''}
            label={`${d.name}${d.isRequired || d.code.toUpperCase() === 'PRODUCT' ? ' *' : ''}`}
            onChange={e => setSelections(x => ({ ...x, [d.id]: e.target.value }))}
            renderValue={selected => {
              if (!selected) return <Typography component="span" color="text.secondary">انتخاب کنید</Typography>
              const member = d.members.find(m => m.id === selected)
              return member ? `${member.name} — ${member.code}` : ''
            }}
          >
            <MenuItem value=""><em>انتخاب کنید</em></MenuItem>
            {d.members.map(m => <MenuItem key={m.id} value={m.id}>{m.name} — {m.code}</MenuItem>)}
          </Select>
        </FormControl>)}
        {plan && <FormControl size="small" sx={{ minWidth: 220 }}><InputLabel>نسخه</InputLabel><Select value={version?.id ?? ''} label="نسخه" onChange={e => setVersionId(e.target.value)}>{versions.map(v => <MenuItem key={v.id} value={v.id}>{v.name} — نسخه {v.versionNumber.toLocaleString('fa-IR')}</MenuItem>)}</Select></FormControl>}
      </Stack>
      {!requiredReady && <Alert severity="warning" sx={{ mt: 2 }}>برای ادامه ابتدا کالا را انتخاب کنید.</Alert>}
      <Typography variant="caption" color="text.secondary" display="block" mt={1.2}>نسخه یک Dimension نیست؛ نسخه اولیه با برنامه ایجاد می‌شود و اصلاحیه‌ها از گردش نسخه بودجه مدیریت می‌شوند.</Typography>
      {isActual && <Alert severity="info" sx={{ mt: 2 }}>عملکرد واقعی فقط خواندنی است و از Actual Ledger، ERP یا Import کنترل‌شده وارد می‌شود؛ از این فرم قابل تغییر نیست.</Alert>}
      {version && !isActual && !editable && <Alert severity="warning" sx={{ mt: 2 }}>نسخه انتخاب‌شده Draft باز نیست و فقط قابل مشاهده است.</Alert>}
    </CardContent></Card>}

    {data && <>
      <Box className="kpi-grid">{[
        ['تعداد فروش', number.format(total('SALES_QTY'))], ['تعداد آفر', number.format(total('FREE_SALES_QTY'))], ['متوسط نرخ فروش', compact.format(average('SALES_PRICE'))],
        ['فروش ناخالص', compact.format(total('GROSS_SALES'))], ['کل تخفیفات فروش', compact.format(total('SALES_DISCOUNT') + total('FOC_SALES_AMOUNT'))], ['برگشت از فروش', compact.format(total('SALES_RETURN'))],
        ['فروش خالص', compact.format(total('NET_SALES'))], ['قیمت تمام‌شده فروش', compact.format(total('SALES_COGS_TOTAL'))], ['سود ناخالص', compact.format(total('SALES_GROSS_MARGIN'))]
      ].map(([label, val]) => <Card key={label} className="kpi-card" elevation={0}><CardContent><Typography color="text.secondary" variant="body2">{label}</Typography><Typography variant="h5" fontWeight={900} mt={1}>{val}</Typography></CardContent></Card>)}</Box>
      <Card elevation={0}><CardContent sx={{ p: 0 }}><Box p={2.5}><Typography variant="h6" fontWeight={900}>جزئیات ماهانه فروش</Typography><Typography color="text.secondary">تخفیفات فروش = تخفیف ریالی + جایزه جنسی؛ قیمت تمام‌شده فروش = بهای عادی + بهای جایزه جنسی.</Typography></Box><TableContainer sx={{ maxHeight: '70vh' }}><Table stickyHeader size="small" sx={{ minWidth: 2200 }}><TableHead><TableRow>{['ماه','تعداد فروش','تعداد آفر','نرخ فروش','فروش ناخالص','تخفیف ریالی','برگشت فروش','فروش/تخفیف جنسی','فروش خالص','بهای فروش عادی','بهای جایزه جنسی','جمع قیمت تمام‌شده فروش','تخفیف کمپانی خرید','سود ناخالص'].map(x => <TableCell key={x} sx={{ minWidth: x === 'ماه' ? 110 : 145 }}>{x}</TableCell>)}</TableRow></TableHead><TableBody>{data.periods.map(p => {
        const editableCodes = ['SALES_QTY','FREE_SALES_QTY','SALES_PRICE','SALES_DISCOUNT','SALES_RETURN','FOC_SALES_AMOUNT','COGS_AMOUNT','FOC_COST','PURCHASE_COMPANY_DISCOUNT']
        const columns = ['SALES_QTY','FREE_SALES_QTY','SALES_PRICE','GROSS_SALES','SALES_DISCOUNT','SALES_RETURN','FOC_SALES_AMOUNT','NET_SALES','COGS_AMOUNT','FOC_COST','SALES_COGS_TOTAL','PURCHASE_COMPANY_DISCOUNT','SALES_GROSS_MARGIN']
        return <TableRow key={p.id} hover><TableCell><Typography fontWeight={800}>{p.name}</Typography></TableCell>{columns.map(code => { const calculated = !editableCodes.includes(code); const val = value(data, code, p.id); return <TableCell key={code}>{calculated || isActual ? <Typography fontWeight={calculated ? 800 : 600}>{number.format(val)}</Typography> : <TextField size="small" type="number" value={val} disabled={!editable || p.isClosed || savingKey === `${code}:${p.id}`} onChange={e => setData(current => !current ? current : { ...current, series: current.series.map(s => s.measureCode !== code ? s : { ...s, values: s.values.map(v => v.periodId === p.id ? { ...v, value: Number(e.target.value) } : v) }) })} onBlur={e => void save(code, p.id, e.target.value)} inputProps={{ min: 0, step: 'any' }} />}</TableCell> })}</TableRow>
      })}</TableBody></Table></TableContainer></CardContent></Card>
    </>}
  </Stack>
}
