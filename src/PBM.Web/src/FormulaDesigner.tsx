import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Chip, FormControl, InputLabel, MenuItem, Select,
  Stack, TextField, Typography
} from '@mui/material'
import { api } from './api'

type Model = { id: string; code: string; name: string; description?: string | null }
type Measure = {
  id: string
  budgetModelId: string
  code: string
  name: string
  unit?: string | null
  valueType: number
  aggregation: number
  isCalculated: boolean
  formulaExpression?: string | null
  displayOrder: number
}
type Definition = { id: string; code: string; name: string; unit?: string | null; isActive: boolean }
type Validation = {
  isValid: boolean
  dependencies: string[]
  measureDependencies: string[]
  assumptionDependencies: string[]
  missingDependencies: string[]
  errors: string[]
}
type UpdateResult = {
  measure: Measure
  validation: Validation
  versionsRecalculated: number
  factsCreated: number
  factsUpdated: number
  formulasSkipped: number
  recalculationErrors: string[]
}

const valueTypes = [
  { value: 0, label: 'مبلغ' }, { value: 1, label: 'تعداد' }, { value: 2, label: 'نرخ' },
  { value: 3, label: 'درصد' }, { value: 4, label: 'امتیاز' }
]
const aggregations = [
  { value: 0, label: 'Sum' }, { value: 1, label: 'Average' }, { value: 2, label: 'Min' },
  { value: 3, label: 'Max' }, { value: 4, label: 'LastNonEmpty' }, { value: 5, label: 'None' }
]

function apiError(error: unknown) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? 'عملیات طراح فرمول ناموفق بود.'
  }
  return 'عملیات طراح فرمول ناموفق بود.'
}

export default function FormulaDesigner({ companyId, canManage }: { companyId: string; canManage: boolean }) {
  const [models, setModels] = useState<Model[]>([])
  const [measures, setMeasures] = useState<Measure[]>([])
  const [assumptions, setAssumptions] = useState<Definition[]>([])
  const [modelId, setModelId] = useState('')
  const [measureId, setMeasureId] = useState('')
  const [expression, setExpression] = useState('')
  const [validation, setValidation] = useState<Validation | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  const [newCode, setNewCode] = useState('')
  const [newName, setNewName] = useState('')
  const [newUnit, setNewUnit] = useState('')
  const [newValueType, setNewValueType] = useState(0)
  const [newAggregation, setNewAggregation] = useState(0)
  const [newDisplayOrder, setNewDisplayOrder] = useState(100)

  const [metaName, setMetaName] = useState('')
  const [metaUnit, setMetaUnit] = useState('')
  const [metaValueType, setMetaValueType] = useState(0)
  const [metaAggregation, setMetaAggregation] = useState(0)
  const [metaDisplayOrder, setMetaDisplayOrder] = useState(0)

  const selectedMeasure = useMemo(() => measures.find(x => x.id === measureId), [measures, measureId])

  const loadMeasures = async (selectedModelId: string, preferredMeasureId?: string) => {
    if (!selectedModelId) { setMeasures([]); setMeasureId(''); return }
    const { data } = await api.get<Measure[]>('/formula-designer/measures', { params: { budgetModelId: selectedModelId } })
    setMeasures(data)
    const desired = preferredMeasureId ?? measureId
    const current = data.find(x => x.id === desired) ?? data.find(x => x.isCalculated) ?? data[0]
    setMeasureId(current?.id ?? '')
  }

  useEffect(() => {
    if (!companyId) return
    setBusy(true); setError(''); setMessage('')
    Promise.all([
      api.get<Model[]>('/reference/models', { params: { companyId } }),
      api.get<Definition[]>('/assumptions/definitions')
    ]).then(([modelResponse, assumptionResponse]) => {
      setModels(modelResponse.data)
      setAssumptions(assumptionResponse.data.filter(x => x.isActive))
      setModelId(modelResponse.data[0]?.id ?? '')
    }).catch(error => setError(apiError(error))).finally(() => setBusy(false))
  }, [companyId])

  useEffect(() => {
    if (!modelId) { setMeasures([]); setMeasureId(''); return }
    setBusy(true); setValidation(null); setError('')
    loadMeasures(modelId).catch(error => setError(apiError(error))).finally(() => setBusy(false))
  }, [modelId])

  useEffect(() => {
    setExpression(selectedMeasure?.formulaExpression ?? '')
    setMetaName(selectedMeasure?.name ?? '')
    setMetaUnit(selectedMeasure?.unit ?? '')
    setMetaValueType(selectedMeasure?.valueType ?? 0)
    setMetaAggregation(selectedMeasure?.aggregation ?? 0)
    setMetaDisplayOrder(selectedMeasure?.displayOrder ?? 0)
    setValidation(null)
    setMessage('')
  }, [measureId, selectedMeasure?.formulaExpression, selectedMeasure?.name, selectedMeasure?.unit, selectedMeasure?.valueType, selectedMeasure?.aggregation, selectedMeasure?.displayOrder])

  const createMeasure = async () => {
    if (!canManage || !modelId || !newCode.trim() || !newName.trim()) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<Measure>('/formula-designer/measures', {
        budgetModelId: modelId,
        code: newCode.trim().toUpperCase(),
        name: newName.trim(),
        unit: newUnit.trim() || null,
        valueType: newValueType,
        aggregation: newAggregation,
        displayOrder: newDisplayOrder,
        formulaExpression: null
      })
      setNewCode(''); setNewName(''); setNewUnit('')
      setMessage(`Measure «${data.name}» ایجاد شد. اکنون می‌توانید فرمول آن را تعریف کنید یا آن را به‌صورت ورودی دستی نگه دارید.`)
      await loadMeasures(modelId, data.id)
    } catch (error) { setError(apiError(error)) }
    finally { setBusy(false) }
  }

  const saveMetadata = async () => {
    if (!canManage || !selectedMeasure || !metaName.trim()) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.put<Measure>(`/formula-designer/measures/${selectedMeasure.id}/metadata`, {
        name: metaName.trim(), unit: metaUnit.trim() || null, valueType: metaValueType,
        aggregation: metaAggregation, displayOrder: metaDisplayOrder
      })
      await loadMeasures(modelId, data.id)
      setMessage(`مشخصات Measure «${data.name}» ذخیره شد.`)
    } catch (error) { setError(apiError(error)) }
    finally { setBusy(false) }
  }

  const deleteMeasure = async () => {
    if (!canManage || !selectedMeasure) return
    if (!window.confirm(`Measure «${selectedMeasure.name}» حذف شود؟ حذف فقط برای Measure کاملاً بدون استفاده مجاز است.`)) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.delete(`/formula-designer/measures/${selectedMeasure.id}`)
      setMeasureId(''); setExpression(''); setValidation(null)
      await loadMeasures(modelId)
      setMessage('Measure بدون استفاده حذف شد.')
    } catch (error) { setError(apiError(error)) }
    finally { setBusy(false) }
  }

  const validate = async () => {
    if (!modelId || !measureId || !expression.trim()) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<Validation>('/formula-designer/validate', {
        budgetModelId: modelId, measureId, expression: expression.trim()
      })
      setValidation(data)
      if (data.isValid) setMessage('فرمول معتبر است و Dependency cycle یا متغیر ناشناخته ندارد.')
    } catch (error) { setError(apiError(error)); setValidation(null) }
    finally { setBusy(false) }
  }

  const saveFormula = async () => {
    if (!canManage || !measureId || !expression.trim()) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.put<UpdateResult>(`/formula-designer/measures/${measureId}/formula`, {
        expression: expression.trim(), recalculateDraftVersions: true
      })
      setValidation(data.validation)
      await loadMeasures(modelId, data.measure.id)
      const recalc = data.versionsRecalculated
        ? ` ${data.versionsRecalculated.toLocaleString('fa-IR')} نسخه Draft باز محاسبه شد؛ ${data.factsCreated.toLocaleString('fa-IR')} Fact ایجاد و ${data.factsUpdated.toLocaleString('fa-IR')} Fact به‌روزرسانی شد.`
        : ''
      const warnings = data.recalculationErrors.length ? ` ${data.recalculationErrors.length.toLocaleString('fa-IR')} هشدار محاسباتی ثبت شد.` : ''
      setMessage(`فرمول «${data.measure.name}» ذخیره شد.${recalc}${warnings}`)
    } catch (error) { setError(apiError(error)) }
    finally { setBusy(false) }
  }

  const clearFormula = async () => {
    if (!canManage || !selectedMeasure?.isCalculated) return
    if (!window.confirm(`فرمول «${selectedMeasure.name}» حذف و Measure به ورودی دستی تبدیل شود؟ این عملیات فقط وقتی مجاز است که Fact فرمولی در نسخه غیر Draft وجود نداشته باشد.`)) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.delete(`/formula-designer/measures/${selectedMeasure.id}/formula`)
      await loadMeasures(modelId, selectedMeasure.id)
      setExpression(''); setValidation(null)
      setMessage('فرمول حذف شد و Factهای فرمولی نسخه‌های Draft پاک شدند.')
    } catch (error) { setError(apiError(error)) }
    finally { setBusy(false) }
  }

  const insertToken = (token: string) => {
    const addition = `[${token}]`
    setExpression(current => current ? `${current} ${addition}` : addition)
    setValidation(null)
  }

  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    {message && <Alert severity="success" onClose={() => setMessage('')}>{message}</Alert>}
    {!canManage && <Alert severity="info">Measureها و فرمول‌ها قابل مشاهده و Validate هستند؛ تغییر Definition یا Formula به نقش مدیر بودجه، مدیر مالی یا مدیر سامانه نیاز دارد و برای مدل مشترک کنترل Scope شرکت سمت سرور نیز اعمال می‌شود.</Alert>}

    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>مدل و Measure</Typography>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} mt={2}>
        <FormControl size="small" sx={{ minWidth: 250 }}><InputLabel>مدل بودجه</InputLabel><Select label="مدل بودجه" value={modelId} onChange={e => setModelId(e.target.value)}>{models.map(x => <MenuItem key={x.id} value={x.id}>{x.name} — {x.code}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 300 }}><InputLabel>Measure</InputLabel><Select label="Measure" value={measureId} onChange={e => setMeasureId(e.target.value)}>{measures.map(x => <MenuItem key={x.id} value={x.id}>{x.name} — {x.code}{x.isCalculated ? ' [Calculated]' : ''}</MenuItem>)}</Select></FormControl>
        {selectedMeasure && <Chip label={selectedMeasure.isCalculated ? 'محاسباتی' : 'ورودی دستی'} color={selectedMeasure.isCalculated ? 'primary' : 'default'} variant="outlined" />}
      </Stack>
    </CardContent></Card>

    {canManage && <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>ایجاد Measure جدید</Typography>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.2} mt={2} flexWrap="wrap" useFlexGap>
        <TextField size="small" label="کد" value={newCode} onChange={e => setNewCode(e.target.value.toUpperCase())} placeholder="NET_SALES" sx={{ direction: 'ltr' }} />
        <TextField size="small" label="نام" value={newName} onChange={e => setNewName(e.target.value)} />
        <TextField size="small" label="واحد" value={newUnit} onChange={e => setNewUnit(e.target.value)} />
        <FormControl size="small" sx={{ minWidth: 130 }}><InputLabel>نوع مقدار</InputLabel><Select label="نوع مقدار" value={newValueType} onChange={e => setNewValueType(Number(e.target.value))}>{valueTypes.map(x => <MenuItem key={x.value} value={x.value}>{x.label}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 150 }}><InputLabel>Aggregation</InputLabel><Select label="Aggregation" value={newAggregation} onChange={e => setNewAggregation(Number(e.target.value))}>{aggregations.map(x => <MenuItem key={x.value} value={x.value}>{x.label}</MenuItem>)}</Select></FormControl>
        <TextField size="small" type="number" label="ترتیب" value={newDisplayOrder} onChange={e => setNewDisplayOrder(Number(e.target.value))} sx={{ width: 100 }} />
        <Button variant="contained" onClick={createMeasure} disabled={busy || !modelId || !newCode.trim() || !newName.trim()}>ایجاد Measure</Button>
      </Stack>
    </CardContent></Card>}

    {selectedMeasure && <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>مشخصات Measure</Typography>
      <Typography variant="caption" color="text.secondary">کد پایدار: {selectedMeasure.code}. بعد از ایجاد Fact، تغییر Value Type و Aggregation برای حفظ معنای تاریخی مسدود می‌شود.</Typography>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.2} mt={2}>
        <TextField size="small" label="نام" value={metaName} onChange={e => setMetaName(e.target.value)} disabled={!canManage || busy} />
        <TextField size="small" label="واحد" value={metaUnit} onChange={e => setMetaUnit(e.target.value)} disabled={!canManage || busy} />
        <FormControl size="small" sx={{ minWidth: 130 }}><InputLabel>نوع مقدار</InputLabel><Select label="نوع مقدار" value={metaValueType} onChange={e => setMetaValueType(Number(e.target.value))} disabled={!canManage || busy}>{valueTypes.map(x => <MenuItem key={x.value} value={x.value}>{x.label}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 150 }}><InputLabel>Aggregation</InputLabel><Select label="Aggregation" value={metaAggregation} onChange={e => setMetaAggregation(Number(e.target.value))} disabled={!canManage || busy}>{aggregations.map(x => <MenuItem key={x.value} value={x.value}>{x.label}</MenuItem>)}</Select></FormControl>
        <TextField size="small" type="number" label="ترتیب" value={metaDisplayOrder} onChange={e => setMetaDisplayOrder(Number(e.target.value))} disabled={!canManage || busy} sx={{ width: 100 }} />
        <Button variant="outlined" onClick={saveMetadata} disabled={!canManage || busy || !metaName.trim()}>ذخیره مشخصات</Button>
        <Button color="error" onClick={deleteMeasure} disabled={!canManage || busy}>حذف Measure</Button>
      </Stack>
    </CardContent></Card>}

    {selectedMeasure && <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', lg: 'row' }} justifyContent="space-between" spacing={2}>
        <Box><Typography variant="h6" fontWeight={900}>Formula</Typography><Typography color="text.secondary">Measureها با <Box component="code">[MEASURE_CODE]</Box> و فرضیات با <Box component="code">[ASSUMP:CODE]</Box> ارجاع داده می‌شوند.</Typography></Box>
        <Stack direction="row" spacing={1}><Button variant="outlined" onClick={validate} disabled={busy || !expression.trim()}>Validate</Button><Button variant="contained" onClick={saveFormula} disabled={!canManage || busy || !expression.trim()}>ذخیره و باز محاسبه Draftها</Button>{selectedMeasure.isCalculated && <Button color="warning" onClick={clearFormula} disabled={!canManage || busy}>تبدیل به Manual</Button>}</Stack>
      </Stack>
      <TextField multiline minRows={5} fullWidth value={expression} onChange={e => { setExpression(e.target.value); setValidation(null) }} sx={{ mt: 2, '& textarea': { direction: 'ltr', fontFamily: 'monospace' } }} placeholder="[SALES_QTY] * [ASSUMP:UNIT_PRICE]" disabled={busy} />
      <Typography variant="caption" color="text.secondary" display="block" mt={1}>توابع امن: ABS، MIN، MAX، ROUND. اگر درصد به شکل 25 ثبت شده، در Formula از `/ 100` استفاده کنید.</Typography>
    </CardContent></Card>}

    {validation && <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>نتیجه اعتبارسنجی</Typography>
      <Alert severity={validation.isValid ? 'success' : 'error'} sx={{ mt: 1.5 }}>{validation.isValid ? 'فرمول معتبر است.' : 'فرمول نیاز به اصلاح دارد.'}</Alert>
      <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap mt={2}>
        {validation.measureDependencies.map(x => <Chip key={x} label={`Measure: ${x}`} />)}
        {validation.assumptionDependencies.map(x => <Chip key={x} label={x} color="secondary" variant="outlined" />)}
        {validation.missingDependencies.map(x => <Chip key={x} label={`ناشناخته: ${x}`} color="error" />)}
      </Stack>
      {validation.errors.map(item => <Typography key={item} color="error" variant="body2" mt={1}>• {item}</Typography>)}
    </CardContent></Card>}

    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>درج سریع متغیر</Typography>
      <Typography variant="body2" color="text.secondary" mb={1.5}>با کلیک، Token به انتهای Formula اضافه می‌شود.</Typography>
      <Stack direction="row" spacing={.8} flexWrap="wrap" useFlexGap>
        {measures.filter(x => x.id !== measureId).map(x => <Button key={x.id} size="small" variant="outlined" onClick={() => insertToken(x.code)}>{x.code}</Button>)}
        {assumptions.map(x => <Button key={x.id} size="small" color="secondary" variant="outlined" onClick={() => insertToken(`ASSUMP:${x.code}`)}>ASSUMP:{x.code}</Button>)}
      </Stack>
    </CardContent></Card>
  </Stack>
}
