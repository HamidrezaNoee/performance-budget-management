import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, FormControl, InputLabel, MenuItem, Select,
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

const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })
const compact = new Intl.NumberFormat('fa-IR', { notation: 'compact', maximumFractionDigits: 1 })

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    return (error as { response?: { data?: { detail?: string } } }).response?.data?.detail ?? fallback
  }
  return fallback
}

function value(data: Data | null, code: string, periodId: string) {
  return Number(data?.series.find(x => x.measureCode === code)?.values.find(x => x.periodId === periodId)?.value ?? 0)
}

export default function SalesPlanner({ companyId, fiscalYearId, canWrite }: { companyId: string; fiscalYearId: string; canWrite: boolean }) {
  const [setup, setSetup] = useState<Setup | null>(null)
  const [plan, setPlan] = useState<Plan | null>(null)
  const [versionId, setVersionId] = useState('')
  const [selections, setSelections] = useState<Record<string, string>>({})
  const [valueKind, setValueKind] = useState<0 | 3>(0)
  const [data, setData] = useState<Data | null>(null)
  const [busy, setBusy] = useState(false)
  const [savingKey, setSavingKey] = useState('')
  const [error, setError] = useState('')

  const versions = useMemo(() => [...(plan?.versions ?? [])].sort((a, b) => b.versionNumber - a.versionNumber), [plan])
  const version = versions.find(x => x.id === versionId) ?? versions[0]
  const editable = canWrite && !!version && version.status === 0 && !version.isLocked
  const selectedDimensions = useMemo(() => !setup ? [] : setup.dimensions
    .filter(d => !!selections[d.id]).map(d => ({ dimensionId: d.id, memberId: selections[d.id] })), [setup, selections])
  const requiredReady = useMemo(() => !!setup && setup.dimensions
    .filter(d => d.isRequired || d.code.toUpperCase() === 'PRODUCT').every(d => !!selections[d.id]), [setup, selections])

  const loadSetup = async () => {
    if (!companyId || !fiscalYearId) return
    setBusy(true); setError(''); setData(null)
    try {
      const [setupResponse, plansResponse] = await Promise.all([
        api.get<Setup>('/sales-planning/setup', { params: { companyId } }),
        api.get<Plan[]>('/budget/plans', { params: { companyId, fiscalYearId } })
      ])
      setSetup(setupResponse.data)
      const p = plansResponse.data.find(x => x.budgetModelId === setupResponse.data.modelId) ?? null
      setPlan(p)
      const latest = [...(p?.versions ?? [])].sort((a, b) => b.versionNumber - a.versionNumber)[0]
      setVersionId(latest?.id ?? '')
      const defaults: Record<string, string> = {}
      setupResponse.data.dimensions.forEach(d => {
        if ((d.isRequired || d.code.toUpperCase() === 'PRODUCT') && d.members.length) defaults[d.id] = d.members[0].id
      })
      setSelections(defaults)
    } catch (e) { setError(apiError(e, 'دریافت تنظیمات فروش ناموفق بود.')) }
    finally { setBusy(false) }
  }

  useEffect(() => { void loadSetup() }, [companyId, fiscalYearId])

  const query = async () => {
    if (!version || !requiredReady) { setData(null); return }
    setBusy(true); setError('')
    try {
      const response = await api.post<Data>('/sales-planning/query', { versionId: version.id, dimensions: selectedDimensions, valueKind })
      setData(response.data)
    } catch (e) { setError(apiError(e, 'بارگذاری بودجه/Forecast فروش ناموفق بود.')) }
    finally { setBusy(false) }
  }

  useEffect(() => { if (version && requiredReady) void query(); else setData(null) }, [version?.id, valueKind, requiredReady, JSON.stringify(selectedDimensions)])

  const createPlan = async () => {
    if (!setup || !canWrite) return
    setBusy(true); setError('')
    try {
      const response = await api.post<Plan>('/budget/plans', { companyId, fiscalYearId, budgetModelId: setup.modelId, name: 'برنامه خرید و فروش' })
      setPlan(response.data)
      setVersionId(response.data.versions[0]?.id ?? '')
    } catch (e) { setError(apiError(e, 'ایجاد برنامه TRADE ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const save = async (code: string, periodId: string, raw: string) => {
    if (!editable || !version || !requiredReady) return
    const numeric = Number(raw)
    if (!Number.isFinite(numeric) || numeric < 0) { setError('مقدار فروش باید عددی و نامنفی باشد.'); return }
    const key = `${code}:${periodId}`
    setSavingKey(key); setError('')
    try {
      await api.post('/sales-planning/cell', { versionId: version.id, periodId, measureCode: code, value: numeric, dimensions: selectedDimensions, valueKind, note: null })
      await query()
    } catch (e) { setError(apiError(e, 'ذخیره مقدار فروش ناموفق بود.')) }
    finally { setSavingKey('') }
  }

  const total = (code: string) => data?.series.find(x => x.measureCode === code)?.values.reduce((s, x) => s + Number(x.value || 0), 0) ?? 0
  const average = (code: string) => {
    const values = data?.series.find(x => x.measureCode === code)?.values.map(x => Number(x.value || 0)).filter(x => x !== 0) ?? []
    return values.length ? values.reduce((a, b) => a + b, 0) / values.length : 0
  }

  if (busy && !setup) return <Box py={8} textAlign="center"><CircularProgress /></Box>

  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    <Card elevation={0} sx={{ background: 'linear-gradient(135deg, rgba(2,132,199,.08), rgba(124,58,237,.06))' }}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} alignItems={{ md: 'center' }}>
        <Box><Stack direction="row" spacing={1} alignItems="center"><PointOfSaleRoundedIcon color="primary" /><Typography variant="h5" fontWeight={900}>بودجه و پیش‌بینی چندبعدی فروش</Typography></Stack>
          <Typography color="text.secondary" mt={1}>ثبت ماهانه تعداد، نرخ و مبلغ فروش در سطح کالا و ترکیب دلخواه مشتری، کمپانی، برند، منطقه، قرارداد، مرکز هزینه، پروژه و سایر ابعاد TRADE.</Typography></Box>
        <Button startIcon={<RefreshRoundedIcon />} onClick={() => void loadSetup()} disabled={busy}>بازخوانی</Button>
      </Stack>
    </CardContent></Card>

    {!plan && <Alert severity="info" action={canWrite ? <Button color="inherit" onClick={createPlan}>ایجاد برنامه</Button> : undefined}>برای سال مالی انتخاب‌شده برنامه TRADE وجود ندارد.</Alert>}

    {setup && <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.3} flexWrap="wrap" useFlexGap alignItems={{ lg: 'center' }}>
        <ToggleButtonGroup exclusive size="small" value={valueKind} onChange={(_, v) => v !== null && setValueKind(v)}>
          <ToggleButton value={0}>بودجه فروش</ToggleButton><ToggleButton value={3}>Forecast فروش</ToggleButton>
        </ToggleButtonGroup>
        {setup.dimensions.map(d => <FormControl key={d.id} size="small" sx={{ minWidth: 205 }}><InputLabel>{d.name}{d.isRequired || d.code === 'PRODUCT' ? ' *' : ''}</InputLabel>
          <Select value={selections[d.id] ?? ''} label={`${d.name}${d.isRequired || d.code === 'PRODUCT' ? ' *' : ''}`} onChange={e => setSelections(x => ({ ...x, [d.id]: e.target.value }))}>
            {!d.isRequired && d.code !== 'PRODUCT' && <MenuItem value=""><em>بدون تفکیک</em></MenuItem>}
            {d.members.map(m => <MenuItem key={m.id} value={m.id}>{m.name} — {m.code}</MenuItem>)}
          </Select></FormControl>)}
        {plan && <FormControl size="small" sx={{ minWidth: 220 }}><InputLabel>نسخه</InputLabel><Select value={version?.id ?? ''} label="نسخه" onChange={e => setVersionId(e.target.value)}>{versions.map(v => <MenuItem key={v.id} value={v.id}>{v.name} — نسخه {v.versionNumber.toLocaleString('fa-IR')}</MenuItem>)}</Select></FormControl>}
      </Stack>
      {!requiredReady && <Alert severity="warning" sx={{ mt: 2 }}>کالا و تمام Dimensionهای اجباری را انتخاب کنید.</Alert>}
      {version && !editable && <Alert severity="warning" sx={{ mt: 2 }}>نسخه انتخاب‌شده Draft باز نیست و فقط قابل مشاهده است.</Alert>}
    </CardContent></Card>}

    {data && <>
      <Box className="kpi-grid">
        {[
          ['تعداد فروش', number.format(total('SALES_QTY'))], ['تعداد آفر', number.format(total('FREE_SALES_QTY'))],
          ['متوسط نرخ فروش', compact.format(average('SALES_PRICE'))], ['فروش ناخالص', compact.format(total('GROSS_SALES'))],
          ['تخفیف فروش', compact.format(total('SALES_DISCOUNT'))], ['برگشت از فروش', compact.format(total('SALES_RETURN'))],
          ['فروش خالص', compact.format(total('NET_SALES'))], ['بهای تمام‌شده', compact.format(total('COGS_AMOUNT'))],
          ['سود ناخالص', compact.format(total('SALES_GROSS_MARGIN'))]
        ].map(([label, val]) => <Card key={label} className="kpi-card" elevation={0}><CardContent><Typography color="text.secondary" variant="body2">{label}</Typography><Typography variant="h5" fontWeight={900} mt={1}>{val}</Typography></CardContent></Card>)}
      </Box>

      <Card elevation={0}><CardContent sx={{ p: 0 }}>
        <Box p={2.5}><Typography variant="h6" fontWeight={900}>جزئیات ماهانه فروش</Typography><Typography color="text.secondary">همان منطق فایل بودجه: جزئیات تعدادی و مبلغی هر کالا در هر ماه؛ ستون‌های محاسباتی به‌صورت خودکار به‌روز می‌شوند.</Typography></Box>
        <TableContainer sx={{ maxHeight: '70vh' }}><Table stickyHeader size="small" sx={{ minWidth: 1800 }}>
          <TableHead><TableRow>
            {['ماه','تعداد فروش','تعداد آفر','نرخ فروش','فروش ناخالص','تخفیف ریالی','برگشت فروش','تخفیف جنسی','فروش خالص','بهای تمام‌شده','تخفیف کمپانی خرید','سود ناخالص'].map(x => <TableCell key={x} sx={{ minWidth: x === 'ماه' ? 110 : 145 }}>{x}</TableCell>)}
          </TableRow></TableHead>
          <TableBody>{data.periods.map(p => {
            const editableCodes = ['SALES_QTY','FREE_SALES_QTY','SALES_PRICE','SALES_DISCOUNT','SALES_RETURN','FOC_SALES_AMOUNT','COGS_AMOUNT','PURCHASE_COMPANY_DISCOUNT']
            const columns = ['SALES_QTY','FREE_SALES_QTY','SALES_PRICE','GROSS_SALES','SALES_DISCOUNT','SALES_RETURN','FOC_SALES_AMOUNT','NET_SALES','COGS_AMOUNT','PURCHASE_COMPANY_DISCOUNT','SALES_GROSS_MARGIN']
            return <TableRow key={p.id} hover><TableCell><Typography fontWeight={800}>{p.name}</Typography></TableCell>{columns.map(code => {
              const calculated = !editableCodes.includes(code)
              const val = value(data, code, p.id)
              return <TableCell key={code}>{calculated ? <Typography fontWeight={800}>{number.format(val)}</Typography> : <TextField size="small" type="number" value={val} disabled={!editable || p.isClosed || savingKey === `${code}:${p.id}`}
                onChange={e => setData(current => !current ? current : { ...current, series: current.series.map(s => s.measureCode !== code ? s : { ...s, values: s.values.map(v => v.periodId === p.id ? { ...v, value: Number(e.target.value) } : v) }) })}
                onBlur={e => void save(code, p.id, e.target.value)} inputProps={{ min: 0, step: 'any' }} />}</TableCell>
            })}</TableRow>
          })}</TableBody>
        </Table></TableContainer>
      </Card>
    </>}
  </Stack>
}
