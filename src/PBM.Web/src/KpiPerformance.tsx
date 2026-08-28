import { useEffect, useMemo, useState } from 'react'
import { Alert, Box, Button, Card, CardContent, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography } from '@mui/material'
import AddRoundedIcon from '@mui/icons-material/AddRounded'
import { api } from './api'

type Period = { id: string; sequence: number; name: string }
type Kpi = { id: string; code: string; name: string; description?: string; unit?: string; weight: number; minimum?: number | null; maximum?: number | null; frequency: string; formulaExpression?: string }
type KpiValue = { id: string; kpiId: string; companyId: string; periodId: string; target: number; actual: number; score?: number; achievementPercent: number }

type DraftKpi = { code: string; name: string; description: string; unit: string; weight: number; frequency: string; formulaExpression: string; minimum: string; maximum: string }
const emptyKpi: DraftKpi = { code: '', name: '', description: '', unit: '%', weight: 0, frequency: 'Monthly', formulaExpression: '', minimum: '', maximum: '' }

const scoreNumber = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 1 })

function scoreMode(kpi: Pick<Kpi, 'minimum' | 'maximum'>) {
  if (kpi.minimum != null && kpi.maximum != null) return 'بازه هدف'
  if (kpi.maximum != null) return 'کمتر بهتر است'
  return 'بیشتر بهتر است'
}

function clientScore(target: number, actual: number, minimum?: number | null, maximum?: number | null) {
  const cap = 150
  if (minimum != null && maximum != null) {
    if (actual >= minimum && actual <= maximum) return 100
    if (actual < minimum) return minimum <= 0 ? 0 : Math.max(0, Math.min(cap, actual / minimum * 100))
    return actual <= 0 || maximum <= 0 ? 0 : Math.max(0, Math.min(cap, maximum / actual * 100))
  }
  if (maximum != null && minimum == null) {
    const reference = target > 0 ? target : maximum
    if (reference <= 0) return actual <= 0 ? 100 : 0
    if (actual <= 0) return cap
    return Math.max(0, Math.min(cap, reference / actual * 100))
  }
  const reference = target > 0 ? target : (minimum ?? 0)
  if (reference <= 0) return actual >= 0 ? 100 : 0
  return Math.max(0, Math.min(cap, actual / reference * 100))
}

export default function KpiPerformance({ companyId, fiscalYearId }: { companyId: string; fiscalYearId: string }) {
  const [periods, setPeriods] = useState<Period[]>([])
  const [kpis, setKpis] = useState<Kpi[]>([])
  const [values, setValues] = useState<KpiValue[]>([])
  const [error, setError] = useState('')
  const [dialogOpen, setDialogOpen] = useState(false)
  const [draft, setDraft] = useState<DraftKpi>(emptyKpi)

  const reload = async () => {
    setError('')
    try {
      const [periodResponse, kpiResponse, valueResponse] = await Promise.all([
        api.get<Period[]>('/reference/periods', { params: { fiscalYearId } }),
        api.get<Kpi[]>('/performance/kpis'),
        api.get<KpiValue[]>('/performance/kpi-values', { params: { companyId, fiscalYearId } })
      ])
      setPeriods(periodResponse.data); setKpis(kpiResponse.data); setValues(valueResponse.data)
    } catch { setError('دریافت اطلاعات KPI ناموفق بود.') }
  }

  useEffect(() => { reload() }, [companyId, fiscalYearId])
  const valueMap = useMemo(() => new Map(values.map(x => [`${x.kpiId}|${x.periodId}`, x])), [values])

  const save = async (kpiId: string, periodId: string, target: number, actual: number) => {
    try {
      const { data } = await api.post<KpiValue>('/performance/kpi-values', { kpiId, companyId, periodId, target, actual })
      setValues(current => [...current.filter(x => !(x.kpiId === kpiId && x.periodId === periodId)), data])
    } catch { setError('ذخیره مقدار KPI ناموفق بود.') }
  }

  const createKpi = async () => {
    const minimum = draft.minimum.trim() === '' ? null : Number(draft.minimum)
    const maximum = draft.maximum.trim() === '' ? null : Number(draft.maximum)
    if (minimum != null && !Number.isFinite(minimum)) { setError('حداقل KPI معتبر نیست.'); return }
    if (maximum != null && !Number.isFinite(maximum)) { setError('حداکثر KPI معتبر نیست.'); return }
    if (minimum != null && maximum != null && minimum > maximum) { setError('حداقل KPI نمی‌تواند از حداکثر بیشتر باشد.'); return }
    try {
      const { data } = await api.post<Kpi>('/performance/kpis', {
        code: draft.code,
        name: draft.name,
        description: draft.description,
        unit: draft.unit,
        weight: draft.weight,
        frequency: draft.frequency,
        formulaExpression: draft.formulaExpression,
        minimum,
        maximum
      })
      setKpis(current => [...current, data]); setDraft(emptyKpi); setDialogOpen(false)
    } catch { setError('ایجاد KPI ناموفق بود. کد باید یکتا، بازه معتبر و وزن بین صفر تا صد باشد.') }
  }

  return <Stack spacing={2.5}>
    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2}>
        <Box><Typography variant="h6" fontWeight={900}>شاخص‌های عملکرد</Typography><Typography color="text.secondary">هدف، عملکرد واقعی و امتیاز تحقق هر KPI را با منطق بیشتر/کمتر/بازه هدف ثبت کنید.</Typography></Box>
        <Button variant="contained" startIcon={<AddRoundedIcon />} onClick={() => setDialogOpen(true)}>تعریف KPI</Button>
      </Stack>
    </CardContent></Card>
    {error && <Alert severity="error">{error}</Alert>}
    <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <TableContainer sx={{ maxHeight: '68vh' }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell sx={{ minWidth: 245, right: 0, zIndex: 4 }}>شاخص</TableCell>{periods.map(p => <TableCell key={p.id} align="center" sx={{ minWidth: 205 }}>{p.name}<Typography variant="caption" display="block" color="text.secondary">هدف / واقعی / امتیاز</Typography></TableCell>)}</TableRow></TableHead><TableBody>{kpis.map(kpi => <TableRow key={kpi.id} hover><TableCell sx={{ position: 'sticky', right: 0, bgcolor: '#fff', zIndex: 2 }}><Typography fontWeight={900} variant="body2">{kpi.name}</Typography><Typography variant="caption" color="text.secondary">{kpi.code} | وزن {kpi.weight}٪ | {kpi.unit ?? '-'} | {scoreMode(kpi)}</Typography>{kpi.minimum != null && <Typography variant="caption" display="block" color="text.secondary">حداقل: {kpi.minimum.toLocaleString('fa-IR')}</Typography>}{kpi.maximum != null && <Typography variant="caption" display="block" color="text.secondary">حداکثر: {kpi.maximum.toLocaleString('fa-IR')}</Typography>}</TableCell>{periods.map(period => <KpiCell key={period.id} kpi={kpi} value={valueMap.get(`${kpi.id}|${period.id}`)} onSave={(target, actual) => save(kpi.id, period.id, target, actual)} />)}</TableRow>)}</TableBody></Table></TableContainer>
    </CardContent></Card>
    <Dialog open={dialogOpen} onClose={() => setDialogOpen(false)} fullWidth maxWidth="sm"><DialogTitle>تعریف شاخص جدید</DialogTitle><DialogContent><Stack spacing={2} mt={1}><Alert severity="info">اگر فقط «حداکثر» وارد شود، KPI از نوع «کمتر بهتر است» خواهد بود؛ فقط «حداقل» یا هیچ‌کدام یعنی «بیشتر بهتر است»؛ واردکردن هر دو یعنی مقدار مطلوب باید داخل بازه باشد.</Alert><TextField label="کد KPI" value={draft.code} onChange={e => setDraft(x => ({ ...x, code: e.target.value }))} /><TextField label="نام KPI" value={draft.name} onChange={e => setDraft(x => ({ ...x, name: e.target.value }))} /><TextField label="شرح" multiline minRows={2} value={draft.description} onChange={e => setDraft(x => ({ ...x, description: e.target.value }))} /><Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}><TextField label="واحد" value={draft.unit} onChange={e => setDraft(x => ({ ...x, unit: e.target.value }))} fullWidth /><TextField label="وزن (%)" type="number" value={draft.weight} onChange={e => setDraft(x => ({ ...x, weight: Number(e.target.value) }))} fullWidth /></Stack><Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}><TextField label="حداقل اختیاری" type="number" value={draft.minimum} onChange={e => setDraft(x => ({ ...x, minimum: e.target.value }))} fullWidth /><TextField label="حداکثر اختیاری" type="number" value={draft.maximum} onChange={e => setDraft(x => ({ ...x, maximum: e.target.value }))} fullWidth /></Stack><TextField label="فرمول اختیاری" placeholder="[ACTUAL] / [TARGET] * 100" value={draft.formulaExpression} onChange={e => setDraft(x => ({ ...x, formulaExpression: e.target.value }))} /></Stack></DialogContent><DialogActions><Button onClick={() => setDialogOpen(false)}>انصراف</Button><Button variant="contained" onClick={createKpi} disabled={!draft.code.trim() || !draft.name.trim()}>ثبت شاخص</Button></DialogActions></Dialog>
  </Stack>
}

function KpiCell({ kpi, value, onSave }: { kpi: Kpi; value?: KpiValue; onSave: (target: number, actual: number) => void }) {
  const [target, setTarget] = useState(value?.target ?? 0)
  const [actual, setActual] = useState(value?.actual ?? 0)
  useEffect(() => { setTarget(value?.target ?? 0); setActual(value?.actual ?? 0) }, [value?.id, value?.target, value?.actual])
  const score = value && value.target === target && value.actual === actual
    ? value.achievementPercent
    : clientScore(target, actual, kpi.minimum, kpi.maximum)
  return <TableCell sx={{ p: .75 }}><Stack direction="row" spacing={.5} alignItems="center"><TextField size="small" type="number" value={target} onChange={e => setTarget(Number(e.target.value))} onBlur={() => onSave(target, actual)} inputProps={{ style: { textAlign: 'center', width: 54 }, 'aria-label': 'هدف' }} /><TextField size="small" type="number" value={actual} onChange={e => setActual(Number(e.target.value))} onBlur={() => onSave(target, actual)} inputProps={{ style: { textAlign: 'center', width: 54 }, 'aria-label': 'عملکرد واقعی' }} /><Box sx={{ minWidth: 48, textAlign: 'center', fontWeight: 900, color: score >= 100 ? 'success.main' : score >= 80 ? 'warning.main' : 'error.main' }}>{scoreNumber.format(score)}٪</Box></Stack></TableCell>
}
