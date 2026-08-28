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

  const selectedMeasure = useMemo(() => measures.find(x => x.id === measureId), [measures, measureId])

  const loadMeasures = async (selectedModelId: string) => {
    if (!selectedModelId) { setMeasures([]); setMeasureId(''); return }
    const { data } = await api.get<Measure[]>('/formula-designer/measures', { params: { budgetModelId: selectedModelId } })
    setMeasures(data)
    const current = data.find(x => x.id === measureId) ?? data.find(x => x.isCalculated) ?? data[0]
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
    setValidation(null)
    setMessage('')
  }, [measureId, selectedMeasure?.formulaExpression])

  const validate = async () => {
    if (!modelId || !measureId || !expression.trim()) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<Validation>('/formula-designer/validate', {
        budgetModelId: modelId,
        measureId,
        expression: expression.trim()
      })
      setValidation(data)
      if (data.isValid) setMessage('فرمول معتبر است و Dependency cycle یا متغیر ناشناخته ندارد.')
    } catch (error) { setError(apiError(error)); setValidation(null) }
    finally { setBusy(false) }
  }

  const save = async () => {
    if (!canManage || !measureId || !expression.trim()) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.put<UpdateResult>(`/formula-designer/measures/${measureId}`, {
        expression: expression.trim(),
        recalculateDraftVersions: true
      })
      setValidation(data.validation)
      await loadMeasures(modelId)
      const recalc = data.versionsRecalculated
        ? ` ${data.versionsRecalculated.toLocaleString('fa-IR')} نسخه Draft باز محاسبه شد و ${data.factsUpdated.toLocaleString('fa-IR')} Fact فرمولی به‌روزرسانی شد.`
        : ''
      const warnings = data.recalculationErrors.length
        ? ` ${data.recalculationErrors.length.toLocaleString('fa-IR')} هشدار محاسباتی ثبت شد.`
        : ''
      setMessage(`فرمول «${data.measure.name}» ذخیره شد.${recalc}${warnings}`)
    } catch (error) { setError(apiError(error)) }
    finally { setBusy(false) }
  }

  const clearFormula = async () => {
    if (!canManage || !selectedMeasure?.isCalculated) return
    if (!window.confirm(`فرمول «${selectedMeasure.name}» حذف و Measure به ورودی دستی تبدیل شود؟ این عملیات فقط وقتی مجاز است که Fact فرمولی در نسخه غیر Draft وجود نداشته باشد.`)) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.delete(`/formula-designer/measures/${selectedMeasure.id}`)
      await loadMeasures(modelId)
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
    {!canManage && <Alert severity="info">فرمول‌ها قابل مشاهده و Validate هستند؛ ذخیره تغییرات به نقش مدیر بودجه، مدیر مالی یا مدیر سامانه نیاز دارد و برای مدل‌های مشترک کنترل دسترسی شرکت نیز اعمال می‌شود.</Alert>}

    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>طراح فرمول Measure</Typography>
      <Typography color="text.secondary" mt={.5}>فرمول‌ها در موتور امن PBM اجرا می‌شوند. Measureها با <Box component="code">[MEASURE_CODE]</Box> و فرضیات با <Box component="code">[ASSUMP:CODE]</Box> ارجاع داده می‌شوند.</Typography>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} mt={2}>
        <FormControl size="small" sx={{ minWidth: 250 }}><InputLabel>مدل بودجه</InputLabel><Select label="مدل بودجه" value={modelId} onChange={e => setModelId(e.target.value)}>{models.map(x => <MenuItem key={x.id} value={x.id}>{x.name} — {x.code}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 300 }}><InputLabel>Measure</InputLabel><Select label="Measure" value={measureId} onChange={e => setMeasureId(e.target.value)}>{measures.map(x => <MenuItem key={x.id} value={x.id}>{x.name} — {x.code}{x.isCalculated ? ' [Calculated]' : ''}</MenuItem>)}</Select></FormControl>
        {selectedMeasure && <Chip label={selectedMeasure.isCalculated ? 'محاسباتی' : 'ورودی دستی'} color={selectedMeasure.isCalculated ? 'primary' : 'default'} variant="outlined" />}
      </Stack>
    </CardContent></Card>

    {selectedMeasure && <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', lg: 'row' }} justifyContent="space-between" spacing={2}>
        <Box><Typography fontWeight={900}>{selectedMeasure.name}</Typography><Typography variant="body2" color="text.secondary">کد: {selectedMeasure.code} {selectedMeasure.unit ? `— واحد: ${selectedMeasure.unit}` : ''}</Typography></Box>
        <Stack direction="row" spacing={1}><Button variant="outlined" onClick={validate} disabled={busy || !expression.trim()}>Validate</Button><Button variant="contained" onClick={save} disabled={!canManage || busy || !expression.trim()}>ذخیره و باز محاسبه Draftها</Button>{selectedMeasure.isCalculated && <Button color="error" onClick={clearFormula} disabled={!canManage || busy}>حذف فرمول</Button>}</Stack>
      </Stack>
      <TextField multiline minRows={5} fullWidth value={expression} onChange={e => { setExpression(e.target.value); setValidation(null) }} sx={{ mt: 2, '& textarea': { direction: 'ltr', fontFamily: 'monospace' } }} placeholder="[SALES_QTY] * [ASSUMP:UNIT_PRICE]" disabled={busy} />
      <Typography variant="caption" color="text.secondary" display="block" mt={1}>توابع امن موجود: ABS، MIN، MAX، ROUND. برای درصدی که به‌صورت 25 ثبت شده است از `/ 100` استفاده کنید.</Typography>
    </CardContent></Card>}

    {validation && <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>نتیجه اعتبارسنجی</Typography>
      <Alert severity={validation.isValid ? 'success' : 'error'} sx={{ mt: 1.5 }}>{validation.isValid ? 'فرمول معتبر است.' : 'فرمول نیاز به اصلاح دارد.'}</Alert>
      <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap mt={2}>
        {validation.measureDependencies.map(x => <Chip key={x} label={`Measure: ${x}`} />)}
        {validation.assumptionDependencies.map(x => <Chip key={x} label={x} color="secondary" variant="outlined" />)}
        {validation.missingDependencies.map(x => <Chip key={x} label={`ناشناخته: ${x}`} color="error" />)}
      </Stack>
      {validation.errors.map(error => <Typography key={error} color="error" variant="body2" mt={1}>• {error}</Typography>)}
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
