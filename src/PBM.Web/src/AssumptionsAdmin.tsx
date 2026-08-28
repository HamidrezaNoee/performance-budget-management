import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Chip, FormControl, InputLabel, MenuItem, Select,
  Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography
} from '@mui/material'
import { api } from './api'

type FiscalYear = { id: string; code: string; name: string; jalaliYear: number; isClosed: boolean }
type Period = { id: string; sequence: number; code: string; name: string; isClosed: boolean }
type Scenario = { id: string; code: string; name: string; isActive: boolean }
type Definition = { id: string; code: string; name: string; unit?: string | null; description?: string | null; isActive: boolean }
type AssumptionValue = {
  id: string
  definitionId: string
  definitionCode: string
  definitionName: string
  unit?: string | null
  companyId: string
  fiscalYearId: string
  scenarioId?: string | null
  scenarioName?: string | null
  periodId?: string | null
  periodName?: string | null
  value: number
  source?: string | null
  note?: string | null
  updatedAtUtc: string
}
type SaveResult = {
  value: AssumptionValue
  versionsRecalculated: number
  formulaFactsCreated: number
  formulaFactsUpdated: number
  formulasSkipped: number
  recalculationErrors: string[]
}

const faNumber = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 8 })

function apiError(error: unknown) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? 'عملیات فرضیات بودجه ناموفق بود.'
  }
  return 'عملیات فرضیات بودجه ناموفق بود.'
}

export default function AssumptionsAdmin({ companyId, canManage }: { companyId: string; canManage: boolean }) {
  const [years, setYears] = useState<FiscalYear[]>([])
  const [periods, setPeriods] = useState<Period[]>([])
  const [scenarios, setScenarios] = useState<Scenario[]>([])
  const [definitions, setDefinitions] = useState<Definition[]>([])
  const [values, setValues] = useState<AssumptionValue[]>([])
  const [yearId, setYearId] = useState('')
  const [scenarioId, setScenarioId] = useState('')
  const [periodId, setPeriodId] = useState('')
  const [draftValues, setDraftValues] = useState<Record<string, string>>({})
  const [draftNotes, setDraftNotes] = useState<Record<string, string>>({})
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [unit, setUnit] = useState('')
  const [description, setDescription] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  const selectedYear = years.find(x => x.id === yearId)
  const exactValues = useMemo(() => {
    const map: Record<string, AssumptionValue> = {}
    for (const item of values) {
      const sameScenario = scenarioId ? item.scenarioId === scenarioId : !item.scenarioId
      const samePeriod = periodId ? item.periodId === periodId : !item.periodId
      if (sameScenario && samePeriod) map[item.definitionId] = item
    }
    return map
  }, [values, scenarioId, periodId])

  const reloadDefinitions = async () => {
    const { data } = await api.get<Definition[]>('/assumptions/definitions')
    setDefinitions(data)
  }

  const reloadValues = async (fiscalYearId = yearId) => {
    if (!companyId || !fiscalYearId) { setValues([]); return }
    const { data } = await api.get<AssumptionValue[]>('/assumptions/values', { params: { companyId, fiscalYearId } })
    setValues(data)
  }

  useEffect(() => {
    if (!companyId) return
    setBusy(true); setError(''); setMessage('')
    Promise.all([
      api.get<FiscalYear[]>('/reference/fiscal-years', { params: { companyId } }),
      api.get<Scenario[]>('/scenarios/'),
      api.get<Definition[]>('/assumptions/definitions')
    ]).then(([yearResponse, scenarioResponse, definitionResponse]) => {
      setYears(yearResponse.data)
      setScenarios(scenarioResponse.data.filter(x => x.isActive))
      setDefinitions(definitionResponse.data)
      const firstYear = yearResponse.data.find(x => !x.isClosed) ?? yearResponse.data[0]
      setYearId(firstYear?.id ?? '')
    }).catch(error => setError(apiError(error))).finally(() => setBusy(false))
  }, [companyId])

  useEffect(() => {
    if (!yearId) { setPeriods([]); setValues([]); return }
    setBusy(true); setError('')
    Promise.all([
      api.get<Period[]>('/reference/periods', { params: { fiscalYearId: yearId } }),
      api.get<AssumptionValue[]>('/assumptions/values', { params: { companyId, fiscalYearId: yearId } })
    ]).then(([periodResponse, valueResponse]) => {
      setPeriods(periodResponse.data)
      setValues(valueResponse.data)
      setPeriodId('')
    }).catch(error => setError(apiError(error))).finally(() => setBusy(false))
  }, [companyId, yearId])

  useEffect(() => {
    const nextValues: Record<string, string> = {}
    const nextNotes: Record<string, string> = {}
    definitions.forEach(definition => {
      const existing = exactValues[definition.id]
      nextValues[definition.id] = existing ? String(existing.value) : ''
      nextNotes[definition.id] = existing?.note ?? ''
    })
    setDraftValues(nextValues)
    setDraftNotes(nextNotes)
  }, [definitions, exactValues])

  const createDefinition = async () => {
    if (!canManage || !code.trim() || !name.trim()) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.post('/assumptions/definitions', { code: code.trim(), name: name.trim(), unit: unit.trim() || null, description: description.trim() || null })
      setCode(''); setName(''); setUnit(''); setDescription('')
      await reloadDefinitions()
      setMessage('تعریف فرضیه/Driver ایجاد شد.')
    } catch (error) { setError(apiError(error)) }
    finally { setBusy(false) }
  }

  const saveValue = async (definition: Definition) => {
    if (!canManage || !yearId) return
    const raw = (draftValues[definition.id] ?? '').trim()
    if (!raw) { setError(`برای «${definition.name}» مقدار وارد کنید.`); return }
    const numeric = Number(raw.replace(/,/g, ''))
    if (!Number.isFinite(numeric)) { setError(`مقدار «${definition.name}» معتبر نیست.`); return }
    const existing = exactValues[definition.id]
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<SaveResult>('/assumptions/values', {
        id: existing?.id ?? null,
        definitionId: definition.id,
        companyId,
        fiscalYearId: yearId,
        scenarioId: scenarioId || null,
        periodId: periodId || null,
        value: numeric,
        source: 'ManualUI',
        note: (draftNotes[definition.id] ?? '').trim() || null,
        recalculateDraftVersions: true
      })
      await reloadValues()
      const recalculation = data.versionsRecalculated
        ? ` ${data.versionsRecalculated.toLocaleString('fa-IR')} نسخه Draft باز محاسبه شد؛ ${data.formulaFactsUpdated.toLocaleString('fa-IR')} مقدار فرمولی به‌روزرسانی شد.`
        : ''
      const warnings = data.recalculationErrors.length ? ` ${data.recalculationErrors.length.toLocaleString('fa-IR')} هشدار محاسباتی ثبت شد.` : ''
      setMessage(`مقدار «${definition.name}» ذخیره شد.${recalculation}${warnings}`)
    } catch (error) { setError(apiError(error)) }
    finally { setBusy(false) }
  }

  const deleteValue = async (definition: Definition) => {
    if (!canManage) return
    const existing = exactValues[definition.id]
    if (!existing) return
    if (!window.confirm(`مقدار Scope فعلی برای «${definition.name}» حذف شود؟ پس از حذف، مقدار عمومی‌تر در صورت وجود استفاده خواهد شد.`)) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.delete(`/assumptions/values/${existing.id}`, { params: { recalculateDraftVersions: true } })
      await reloadValues()
      setMessage('مقدار Scope حذف شد و نسخه‌های Draft مرتبط باز محاسبه شدند.')
    } catch (error) { setError(apiError(error)) }
    finally { setBusy(false) }
  }

  const inherited = (definitionId: string) => {
    if (exactValues[definitionId]) return null
    const candidates = values.filter(x => x.definitionId === definitionId)
      .filter(x => (!x.scenarioId || x.scenarioId === scenarioId) && (!x.periodId || x.periodId === periodId))
      .sort((a, b) => {
        const score = (x: AssumptionValue) => (x.scenarioId === scenarioId && !!scenarioId ? 2 : 0) + (x.periodId === periodId && !!periodId ? 1 : 0)
        return score(b) - score(a)
      })
    return candidates[0] ?? null
  }

  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    {message && <Alert severity="success" onClose={() => setMessage('')}>{message}</Alert>}

    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>فرضیات و محرک‌های بودجه</Typography>
      <Typography color="text.secondary" mt={.5}>مقادیر می‌توانند عمومی، سناریویی، سالانه یا دوره‌ای باشند. در Formula از شکل <Box component="code" sx={{ direction: 'ltr' }}>[ASSUMP:CODE]</Box> استفاده کنید.</Typography>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} mt={2}>
        <FormControl size="small" sx={{ minWidth: 190 }}><InputLabel>سال مالی</InputLabel><Select label="سال مالی" value={yearId} onChange={e => setYearId(e.target.value)}>{years.map(x => <MenuItem key={x.id} value={x.id}>{x.name}{x.isClosed ? ' — بسته' : ''}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 200 }}><InputLabel>سناریو</InputLabel><Select label="سناریو" value={scenarioId} onChange={e => setScenarioId(e.target.value)}><MenuItem value="">عمومی — همه سناریوها</MenuItem>{scenarios.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 190 }}><InputLabel>دوره</InputLabel><Select label="دوره" value={periodId} onChange={e => setPeriodId(e.target.value)}><MenuItem value="">مقدار سالانه / پیش‌فرض</MenuItem>{periods.map(x => <MenuItem key={x.id} value={x.id} disabled={x.isClosed}>{x.name}{x.isClosed ? ' — بسته' : ''}</MenuItem>)}</Select></FormControl>
        <Chip label={scenarioId ? 'Scope سناریویی' : 'Scope عمومی'} color={scenarioId ? 'primary' : 'default'} variant="outlined" />
        <Chip label={periodId ? 'مقدار دوره‌ای' : 'مقدار سالانه'} color={periodId ? 'secondary' : 'default'} variant="outlined" />
      </Stack>
    </CardContent></Card>

    {canManage && <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>تعریف Driver جدید</Typography>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.5} mt={2}>
        <TextField size="small" label="کد" value={code} onChange={e => setCode(e.target.value.toUpperCase())} placeholder="FX_USD" sx={{ direction: 'ltr' }} />
        <TextField size="small" label="نام" value={name} onChange={e => setName(e.target.value)} placeholder="نرخ دلار بودجه" />
        <TextField size="small" label="واحد" value={unit} onChange={e => setUnit(e.target.value)} placeholder="IRR/USD" />
        <TextField size="small" label="توضیح" value={description} onChange={e => setDescription(e.target.value)} sx={{ minWidth: 260 }} />
        <Button variant="contained" onClick={createDefinition} disabled={busy || !code.trim() || !name.trim()}>ایجاد</Button>
      </Stack>
    </CardContent></Card>}

    {!canManage && <Alert severity="info">فرضیات فقط قابل مشاهده هستند. تغییر Driverها به دسترسی نوشتن و نقش مدیر بودجه، مدیر مالی یا مدیر سامانه نیاز دارد.</Alert>}
    {selectedYear?.isClosed && <Alert severity="warning">سال مالی انتخاب‌شده بسته است؛ مقادیر فرضیات این سال فقط قابل مشاهده‌اند.</Alert>}

    <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <TableContainer sx={{ maxHeight: '66vh' }}><Table stickyHeader size="small"><TableHead><TableRow>
        <TableCell>Driver / متغیر فرمول</TableCell><TableCell>مقدار Scope فعلی</TableCell><TableCell>مقدار موروثی</TableCell><TableCell>یادداشت</TableCell><TableCell>عملیات</TableCell>
      </TableRow></TableHead><TableBody>
        {definitions.map(definition => {
          const exact = exactValues[definition.id]
          const fallback = inherited(definition.id)
          return <TableRow key={definition.id} hover>
            <TableCell><Typography fontWeight={900}>{definition.name}</Typography><Typography variant="caption" sx={{ direction: 'ltr', display: 'block' }}>[ASSUMP:{definition.code}] {definition.unit ? `— ${definition.unit}` : ''}</Typography>{definition.description && <Typography variant="caption" color="text.secondary">{definition.description}</Typography>}</TableCell>
            <TableCell sx={{ minWidth: 180 }}><TextField size="small" type="number" value={draftValues[definition.id] ?? ''} onChange={e => setDraftValues(current => ({ ...current, [definition.id]: e.target.value }))} disabled={!canManage || !!selectedYear?.isClosed || busy} inputProps={{ step: 'any' }} /></TableCell>
            <TableCell>{fallback ? <Box><Typography fontWeight={800}>{faNumber.format(fallback.value)}</Typography><Typography variant="caption" color="text.secondary">{fallback.scenarioName ?? 'عمومی'} / {fallback.periodName ?? 'سالانه'}</Typography></Box> : <Typography color="text.secondary">—</Typography>}</TableCell>
            <TableCell sx={{ minWidth: 230 }}><TextField size="small" value={draftNotes[definition.id] ?? ''} onChange={e => setDraftNotes(current => ({ ...current, [definition.id]: e.target.value }))} disabled={!canManage || !!selectedYear?.isClosed || busy} fullWidth /></TableCell>
            <TableCell><Stack direction="row" spacing={.7}><Button size="small" variant="contained" onClick={() => saveValue(definition)} disabled={!canManage || !!selectedYear?.isClosed || busy || !(draftValues[definition.id] ?? '').trim()}>ذخیره</Button>{exact && <Button size="small" color="error" onClick={() => deleteValue(definition)} disabled={!canManage || !!selectedYear?.isClosed || busy}>حذف Scope</Button>}</Stack></TableCell>
          </TableRow>
        })}
        {!definitions.length && <TableRow><TableCell colSpan={5} align="center" sx={{ py: 6 }}><Typography fontWeight={800}>هنوز Driver تعریف نشده است.</Typography></TableCell></TableRow>}
      </TableBody></Table></TableContainer>
    </Card>
  </Stack>
}
