import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Chip, FormControl, FormControlLabel,
  InputLabel, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead,
  TableRow, Typography
} from '@mui/material'
import { api } from './api'

type Model = { id: string; code: string; name: string; description?: string | null }
type Assumption = { code: string; name: string; unit?: string | null; description: string }
type Measure = {
  code: string; name: string; unit?: string | null; valueType: number; aggregation: number;
  isCalculated: boolean; formulaExpression?: string | null; displayOrder: number
}
type Template = {
  code: string; name: string; description: string; recommendedModelCodes: string[];
  assumptions: Assumption[]; measures: Measure[]
}
type Conflict = { entityType: string; code: string; reason: string }
type ApplyResult = {
  templateCode: string; budgetModelId: string; assumptionsCreated: number; measuresCreated: number;
  measuresUpdated: number; measuresUnchanged: number; versionsRecalculated: number; factsCreated: number;
  factsUpdated: number; formulasSkipped: number; conflicts: Conflict[]; validationErrors: string[];
  recalculationErrors: string[]
}

const valueTypeLabels = ['مبلغ', 'تعداد', 'نرخ', 'درصد', 'امتیاز']
const aggregationLabels = ['جمع', 'میانگین', 'کمینه', 'بیشینه', 'آخرین مقدار غیرخالی', 'بدون تجمیع']

function apiError(error: unknown) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? 'عملیات Template ناموفق بود.'
  }
  return 'عملیات Template ناموفق بود.'
}

export default function DriverTemplatesAdmin({ companyId, canManage }: { companyId: string; canManage: boolean }) {
  const [models, setModels] = useState<Model[]>([])
  const [templates, setTemplates] = useState<Template[]>([])
  const [modelId, setModelId] = useState('')
  const [templateCode, setTemplateCode] = useState('')
  const [overwrite, setOverwrite] = useState(false)
  const [recalculate, setRecalculate] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [result, setResult] = useState<ApplyResult | null>(null)

  const selectedModel = useMemo(() => models.find(x => x.id === modelId), [models, modelId])
  const selectedTemplate = useMemo(() => templates.find(x => x.code === templateCode), [templates, templateCode])
  const recommended = !!selectedModel && !!selectedTemplate && selectedTemplate.recommendedModelCodes.includes(selectedModel.code)

  useEffect(() => {
    if (!companyId) return
    setBusy(true); setError(''); setResult(null)
    Promise.all([
      api.get<Model[]>('/reference/models', { params: { companyId } }),
      api.get<Template[]>('/driver-templates/')
    ]).then(([modelResponse, templateResponse]) => {
      setModels(modelResponse.data)
      setTemplates(templateResponse.data)
      setModelId(current => current && modelResponse.data.some(x => x.id === current) ? current : modelResponse.data[0]?.id ?? '')
      setTemplateCode(current => current && templateResponse.data.some(x => x.code === current) ? current : templateResponse.data[0]?.code ?? '')
    }).catch(e => setError(apiError(e))).finally(() => setBusy(false))
  }, [companyId])

  const apply = async () => {
    if (!canManage || !modelId || !templateCode) return
    setBusy(true); setError(''); setResult(null)
    try {
      const { data } = await api.post<ApplyResult>('/driver-templates/apply', {
        budgetModelId: modelId,
        templateCode,
        overwriteCompatibleDefinitions: overwrite,
        recalculateDraftVersions: recalculate
      })
      setResult(data)
    } catch (e) { setError(apiError(e)) }
    finally { setBusy(false) }
  }

  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    {!canManage && <Alert severity="info">مشاهده Templateها آزاد است؛ نصب روی مدل به نقش مدیر بودجه، مدیر مالی یا مدیر سامانه نیاز دارد.</Alert>}

    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>Templateهای Driver-Based Budgeting</Typography>
      <Typography color="text.secondary" mt={.5}>هر Template مجموعه‌ای از Measure، Formula و Assumptionهای استاندارد را به یک Budget Model اضافه می‌کند. نصب در صورت Conflict به‌صورت تراکنشی متوقف می‌شود و مدل نیمه‌کاره نمی‌ماند.</Typography>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} mt={2}>
        <FormControl size="small" sx={{ minWidth: 280 }}><InputLabel>Budget Model</InputLabel><Select label="Budget Model" value={modelId} onChange={e => { setModelId(e.target.value); setResult(null) }}>{models.map(x => <MenuItem key={x.id} value={x.id}>{x.name} — {x.code}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 280 }}><InputLabel>Template</InputLabel><Select label="Template" value={templateCode} onChange={e => { setTemplateCode(e.target.value); setResult(null) }}>{templates.map(x => <MenuItem key={x.code} value={x.code}>{x.name} — {x.code}</MenuItem>)}</Select></FormControl>
        {selectedModel && selectedTemplate && <Chip label={recommended ? 'مدل پیشنهادی Template' : 'قابل نصب روی مدل عمومی'} color={recommended ? 'success' : 'warning'} variant="outlined" />}
      </Stack>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} mt={1.5} alignItems={{ md: 'center' }}>
        <FormControlLabel control={<Checkbox checked={overwrite} onChange={e => setOverwrite(e.target.checked)} />} label="جایگزینی Definitionهای سازگار موجود" />
        <FormControlLabel control={<Checkbox checked={recalculate} onChange={e => setRecalculate(e.target.checked)} />} label="Recalculate نسخه‌های Draft بعد از نصب" />
        <Button variant="contained" onClick={apply} disabled={!canManage || busy || !modelId || !templateCode}>اعمال Template</Button>
      </Stack>
      {overwrite && <Alert severity="warning" sx={{ mt: 1.5 }}>اگر Measure دارای Fact تاریخی باشد، تغییر نوع داده، Aggregation یا Manual/Calculated Mode حتی با این گزینه مجاز نیست.</Alert>}
    </CardContent></Card>

    {selectedTemplate && <>
      <Card elevation={0}><CardContent>
        <Typography variant="h6" fontWeight={900}>{selectedTemplate.name}</Typography>
        <Typography color="text.secondary" mt={.5}>{selectedTemplate.description}</Typography>
        <Stack direction="row" spacing={.8} flexWrap="wrap" useFlexGap mt={1.5}>{selectedTemplate.recommendedModelCodes.map(x => <Chip key={x} label={`Model: ${x}`} size="small" />)}</Stack>
      </CardContent></Card>

      <Card elevation={0}><CardContent sx={{ p: 0 }}>
        <Box p={2.5}><Typography variant="h6" fontWeight={900}>Assumptionهای موردنیاز</Typography></Box>
        <TableContainer><Table size="small"><TableHead><TableRow><TableCell>کد</TableCell><TableCell>عنوان</TableCell><TableCell>واحد</TableCell><TableCell>شرح</TableCell></TableRow></TableHead><TableBody>{selectedTemplate.assumptions.map(x => <TableRow key={x.code}><TableCell sx={{ direction: 'ltr', fontFamily: 'monospace' }}>{x.code}</TableCell><TableCell>{x.name}</TableCell><TableCell>{x.unit ?? '-'}</TableCell><TableCell>{x.description}</TableCell></TableRow>)}</TableBody></Table></TableContainer>
      </CardContent></Card>

      <Card elevation={0}><CardContent sx={{ p: 0 }}>
        <Box p={2.5}><Typography variant="h6" fontWeight={900}>Measure و Formulaها</Typography></Box>
        <TableContainer><Table size="small"><TableHead><TableRow><TableCell>کد</TableCell><TableCell>عنوان</TableCell><TableCell>نوع</TableCell><TableCell>Aggregation</TableCell><TableCell>Formula</TableCell></TableRow></TableHead><TableBody>{selectedTemplate.measures.map(x => <TableRow key={x.code}><TableCell sx={{ direction: 'ltr', fontFamily: 'monospace' }}>{x.code}</TableCell><TableCell>{x.name}</TableCell><TableCell>{valueTypeLabels[x.valueType] ?? x.valueType}</TableCell><TableCell>{aggregationLabels[x.aggregation] ?? x.aggregation}</TableCell><TableCell sx={{ direction: 'ltr', fontFamily: 'monospace', fontSize: 12 }}>{x.formulaExpression ?? 'Manual input'}</TableCell></TableRow>)}</TableBody></Table></TableContainer>
      </CardContent></Card>
    </>}

    {result && <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>نتیجه نصب</Typography>
      {result.conflicts.length === 0 && result.validationErrors.length === 0
        ? <Alert severity="success" sx={{ mt: 1.5 }}>Template اعمال شد: {result.assumptionsCreated.toLocaleString('fa-IR')} Assumption، {result.measuresCreated.toLocaleString('fa-IR')} Measure جدید و {result.measuresUpdated.toLocaleString('fa-IR')} Measure به‌روزشده.</Alert>
        : <Alert severity="warning" sx={{ mt: 1.5 }}>به‌دلیل Conflict یا خطای Formula، تغییرات Template Commit نشدند.</Alert>}
      {result.versionsRecalculated > 0 && <Typography mt={1.5}>نسخه Draft باز محاسبه‌شده: {result.versionsRecalculated.toLocaleString('fa-IR')} — Fact ایجادشده: {result.factsCreated.toLocaleString('fa-IR')} — Fact به‌روزشده: {result.factsUpdated.toLocaleString('fa-IR')}</Typography>}
      {result.conflicts.map(x => <Alert key={`${x.entityType}-${x.code}`} severity="error" sx={{ mt: 1 }}>{x.entityType} / {x.code}: {x.reason}</Alert>)}
      {result.validationErrors.map(x => <Alert key={x} severity="error" sx={{ mt: 1 }}>{x}</Alert>)}
      {result.recalculationErrors.map(x => <Alert key={x} severity="warning" sx={{ mt: 1 }}>{x}</Alert>)}
    </CardContent></Card>}
  </Stack>
}
