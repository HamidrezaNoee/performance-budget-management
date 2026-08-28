import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Dialog, DialogActions,
  DialogContent, DialogTitle, FormControl, InputLabel, MenuItem, Select, Stack, Table,
  TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography
} from '@mui/material'
import { api } from './api'

type Model = { id: string; code: string; name: string }
type Version = { id: string; scenarioId: string; versionNumber: number; name: string; status: number; isLocked: boolean }
type Plan = { id: string; budgetModelId: string; name: string; versions: Version[] }
type Period = { id: string; sequence: number; code: string; name: string; isClosed: boolean }
type Dimension = { id: string; code: string; name: string; sequence: number; isRequired: boolean }
type Member = { id: string; dimensionId: string; code: string; name: string }
type Measure = { id: string; code: string; name: string; unit?: string; valueType: number; isCalculated: boolean }
type DimensionInput = { dimensionId: string; sourceMemberId: string; destinationMemberId: string }
type Availability = { sourceBudget: number; sourceActual: number; sourceCommitment: number; sourceAvailable: number; destinationBudget: number }
type Transfer = {
  id: string
  transferNo: string
  companyId: string
  versionId: string
  versionNumber: number
  measureId: string
  measureName: string
  sourcePeriodId: string
  sourcePeriodName: string
  destinationPeriodId: string
  destinationPeriodName: string
  amount: number
  currencyCode?: string | null
  status: number
  description: string
  externalReference?: string | null
  requestedByUserId: string
  requestedByDisplayName: string
  decidedByUserId?: string | null
  decidedByDisplayName?: string | null
  decisionComment?: string | null
  createdAtUtc: string
  decidedAtUtc?: string | null
  dimensions: DimensionInput[]
}

const faDateTime = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { dateStyle: 'short', timeStyle: 'short' })
const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })
const statusMeta = [
  { label: 'در انتظار تصمیم', color: 'warning' as const },
  { label: 'تأییدشده', color: 'success' as const },
  { label: 'ردشده', color: 'error' as const }
]

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? fallback
  }
  return fallback
}

export default function BudgetTransfers({ companyId, fiscalYearId, roles }: { companyId: string; fiscalYearId: string; roles: string[] }) {
  const [transfers, setTransfers] = useState<Transfer[]>([])
  const [plans, setPlans] = useState<Plan[]>([])
  const [models, setModels] = useState<Model[]>([])
  const [periods, setPeriods] = useState<Period[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [statusFilter, setStatusFilter] = useState<number | ''>('')
  const [dialogOpen, setDialogOpen] = useState(false)
  const [versionId, setVersionId] = useState('')
  const [measureId, setMeasureId] = useState('')
  const [sourcePeriodId, setSourcePeriodId] = useState('')
  const [destinationPeriodId, setDestinationPeriodId] = useState('')
  const [amount, setAmount] = useState('')
  const [description, setDescription] = useState('')
  const [externalReference, setExternalReference] = useState('')
  const [dimensions, setDimensions] = useState<Dimension[]>([])
  const [members, setMembers] = useState<Record<string, Member[]>>({})
  const [sourceMembers, setSourceMembers] = useState<Record<string, string>>({})
  const [destinationMembers, setDestinationMembers] = useState<Record<string, string>>({})
  const [measures, setMeasures] = useState<Measure[]>([])
  const [availability, setAvailability] = useState<Availability | null>(null)
  const [dialogBusy, setDialogBusy] = useState(false)

  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const canDecide = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || roleSet.has('CFO') || roleSet.has('CEO')
  const operationalVersions = useMemo(() => plans.flatMap(plan => plan.versions.filter(v => v.status === 4 || v.status === 7).map(v => ({ plan, version: v }))), [plans])
  const selectedVersionItem = operationalVersions.find(x => x.version.id === versionId)
  const selectedModel = models.find(x => x.id === selectedVersionItem?.plan.budgetModelId)
  const selectedMeasure = measures.find(x => x.id === measureId)

  const reloadTransfers = async () => {
    if (!companyId || !fiscalYearId) return
    setLoading(true); setError('')
    try {
      const { data } = await api.get<Transfer[]>('/transfers/', { params: { companyId, fiscalYearId, status: statusFilter === '' ? undefined : statusFilter, take: 300 } })
      setTransfers(data)
    } catch (error) { setError(apiError(error, 'دریافت درخواست‌های جابجایی بودجه ناموفق بود.')) }
    finally { setLoading(false) }
  }

  useEffect(() => {
    if (!companyId || !fiscalYearId) return
    setLoading(true); setError(''); setMessage(''); setTransfers([]); setPlans([]); setModels([]); setPeriods([])
    Promise.all([
      api.get<Transfer[]>('/transfers/', { params: { companyId, fiscalYearId, take: 300 } }),
      api.get<Plan[]>('/budget/plans', { params: { companyId, fiscalYearId } }),
      api.get<Model[]>('/reference/models', { params: { companyId } }),
      api.get<Period[]>('/reference/periods', { params: { fiscalYearId } })
    ]).then(([transferResponse, planResponse, modelResponse, periodResponse]) => {
      setTransfers(transferResponse.data); setPlans(planResponse.data); setModels(modelResponse.data); setPeriods(periodResponse.data)
    }).catch(error => setError(apiError(error, 'بارگذاری فضای جابجایی بودجه ناموفق بود.'))).finally(() => setLoading(false))
  }, [companyId, fiscalYearId])

  useEffect(() => { if (companyId && fiscalYearId) reloadTransfers() }, [statusFilter])

  useEffect(() => {
    if (!dialogOpen) return
    const firstVersion = operationalVersions[0]
    const openPeriods = periods.filter(x => !x.isClosed)
    setVersionId(firstVersion?.version.id ?? '')
    setSourcePeriodId(openPeriods[0]?.id ?? '')
    setDestinationPeriodId(openPeriods[1]?.id ?? openPeriods[0]?.id ?? '')
    setAmount(''); setDescription(''); setExternalReference(''); setAvailability(null); setSourceMembers({}); setDestinationMembers({}); setMeasures([]); setDimensions([]); setMembers({})
  }, [dialogOpen])

  useEffect(() => {
    if (!dialogOpen || !selectedVersionItem) return
    const modelId = selectedVersionItem.plan.budgetModelId
    setDialogBusy(true); setAvailability(null)
    Promise.all([
      api.get<Dimension[]>('/reference/dimensions', { params: { modelId } }),
      api.get<Measure[]>('/reference/measures', { params: { modelId } })
    ]).then(async ([dimensionResponse, measureResponse]) => {
      const dims = dimensionResponse.data
      const amountMeasures = measureResponse.data.filter(x => x.valueType === 0 && !x.isCalculated)
      setDimensions(dims); setMeasures(amountMeasures); setMeasureId(amountMeasures[0]?.id ?? '')
      const memberEntries = await Promise.all(dims.map(async dimension => [dimension.id, (await api.get<Member[]>('/reference/dimension-members', { params: { dimensionId: dimension.id, companyId } })).data] as const))
      const memberMap = Object.fromEntries(memberEntries)
      setMembers(memberMap)
      const sourceDefaults: Record<string, string> = {}
      const destinationDefaults: Record<string, string> = {}
      dims.forEach(dimension => {
        if (memberMap[dimension.id]?.length) {
          sourceDefaults[dimension.id] = memberMap[dimension.id][0].id
          destinationDefaults[dimension.id] = memberMap[dimension.id][1]?.id ?? memberMap[dimension.id][0].id
        }
      })
      setSourceMembers(sourceDefaults); setDestinationMembers(destinationDefaults)
    }).catch(error => setError(apiError(error, 'دریافت مختصات بودجه برای جابجایی ناموفق بود.'))).finally(() => setDialogBusy(false))
  }, [dialogOpen, versionId])

  const dimensionInputs = () => dimensions
    .filter(dimension => sourceMembers[dimension.id] && destinationMembers[dimension.id])
    .map(dimension => ({ dimensionId: dimension.id, sourceMemberId: sourceMembers[dimension.id], destinationMemberId: destinationMembers[dimension.id] }))

  const requestPayload = () => ({
    companyId,
    versionId,
    measureId,
    sourcePeriodId,
    destinationPeriodId,
    amount: Number(amount.replace(/,/g, '').trim()) || 0,
    currencyCode: selectedMeasure?.valueType === 0 ? 'IRR' : null,
    description: description.trim() || 'بررسی مانده جابجایی بودجه',
    dimensions: dimensionInputs(),
    externalReference: externalReference.trim() || null
  })

  const validateCoordinate = () => {
    const missing = dimensions.some(dimension => dimension.isRequired && (!sourceMembers[dimension.id] || !destinationMembers[dimension.id]))
    if (missing) { setError('برای تمام ابعاد اجباری، عضو مبدأ و مقصد را انتخاب کنید.'); return false }
    const sameDimensions = dimensions.every(dimension => sourceMembers[dimension.id] === destinationMembers[dimension.id])
    if (sourcePeriodId === destinationPeriodId && sameDimensions) { setError('مبدأ و مقصد جابجایی نمی‌توانند کاملاً یکسان باشند.'); return false }
    return true
  }

  const checkAvailability = async () => {
    if (!versionId || !sourcePeriodId || !destinationPeriodId || !measureId || !validateCoordinate()) return
    setDialogBusy(true); setError('')
    try {
      const { data } = await api.post<Availability>('/transfers/availability', requestPayload())
      setAvailability(data)
    } catch (error) { setAvailability(null); setError(apiError(error, 'محاسبه مانده قابل انتقال ناموفق بود.')) }
    finally { setDialogBusy(false) }
  }

  const createTransfer = async () => {
    const numericAmount = Number(amount.replace(/,/g, '').trim())
    if (!Number.isFinite(numericAmount) || numericAmount <= 0) { setError('مبلغ جابجایی معتبر نیست.'); return }
    if (!description.trim()) { setError('شرح و دلیل جابجایی بودجه الزامی است.'); return }
    if (!validateCoordinate()) return
    if (!availability) { setError('قبل از ثبت، مانده قابل انتقال را بررسی کنید.'); return }
    if (numericAmount > availability.sourceAvailable) { setError('مبلغ جابجایی از مانده قابل انتقال مبدأ بیشتر است.'); return }

    setDialogBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<Transfer>('/transfers/', requestPayload())
      setDialogOpen(false)
      setMessage(`درخواست جابجایی ${data.transferNo} ثبت و برای تأیید ارسال شد.`)
      setStatusFilter('')
      await reloadTransfers()
    } catch (error) { setError(apiError(error, 'ثبت درخواست جابجایی بودجه ناموفق بود.')) }
    finally { setDialogBusy(false) }
  }

  const decide = async (transfer: Transfer, action: 'approve' | 'reject') => {
    const label = action === 'approve' ? 'تأیید' : 'رد'
    const comment = window.prompt(`توضیح ${label} درخواست ${transfer.transferNo}:`, '')
    if (comment === null) return
    setLoading(true); setError(''); setMessage('')
    try {
      await api.post(`/transfers/${transfer.id}/${action}`, { comment: comment.trim() || null })
      setMessage(`${label} درخواست ${transfer.transferNo} انجام شد.`)
      await reloadTransfers()
    } catch (error) { setError(apiError(error, `${label} جابجایی بودجه ناموفق بود.`)) }
    finally { setLoading(false) }
  }

  return <Stack spacing={2.5}>
    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2}>
        <Box><Typography variant="h6" fontWeight={900}>جابجایی و بازتخصیص بودجه</Typography><Typography color="text.secondary">جابجایی فقط از بودجه قابل‌مصرف مبدأ انجام می‌شود؛ در تأیید نهایی مبلغ کل بودجه ثابت می‌ماند و تنها بین دوره/مختصات جابه‌جا می‌شود.</Typography></Box>
        <Stack direction="row" spacing={1}>
          <FormControl size="small" sx={{ minWidth: 170 }}><InputLabel>وضعیت</InputLabel><Select value={statusFilter} label="وضعیت" onChange={e => setStatusFilter(e.target.value === '' ? '' : Number(e.target.value))}><MenuItem value="">همه</MenuItem>{statusMeta.map((item, index) => <MenuItem key={item.label} value={index}>{item.label}</MenuItem>)}</Select></FormControl>
          <Button variant="outlined" onClick={reloadTransfers} disabled={loading}>به‌روزرسانی</Button>
          <Button variant="contained" onClick={() => setDialogOpen(true)} disabled={operationalVersions.length === 0}>درخواست جابجایی</Button>
        </Stack>
      </Stack>
    </CardContent></Card>

    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    {message && <Alert severity="success" onClose={() => setMessage('')}>{message}</Alert>}
    {!operationalVersions.length && !loading && <Alert severity="info">برای جابجایی بودجه، نسخه تأییدشده یا نهایی در سال مالی انتخاب‌شده لازم است.</Alert>}
    {loading && !transfers.length && <Box py={6} textAlign="center"><CircularProgress /></Box>}

    <Card elevation={0}><CardContent sx={{ p: 0 }}><TableContainer sx={{ maxHeight: '68vh' }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>شماره / شرح</TableCell><TableCell>از / به</TableCell><TableCell>مژر / نسخه</TableCell><TableCell align="left">مبلغ</TableCell><TableCell>درخواست‌کننده</TableCell><TableCell>وضعیت</TableCell><TableCell>تاریخ</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>
      {transfers.map(item => {
        const meta = statusMeta[item.status] ?? statusMeta[0]
        return <TableRow key={item.id} hover><TableCell><Typography fontWeight={900} sx={{ direction: 'ltr', textAlign: 'right' }}>{item.transferNo}</Typography><Typography variant="body2">{item.description}</Typography>{item.externalReference && <Typography variant="caption" color="text.secondary">مرجع: {item.externalReference}</Typography>}</TableCell><TableCell><Typography variant="body2">{item.sourcePeriodName} ← {item.destinationPeriodName}</Typography></TableCell><TableCell><Typography fontWeight={700}>{item.measureName}</Typography><Typography variant="caption" color="text.secondary">نسخه {item.versionNumber.toLocaleString('fa-IR')}</Typography></TableCell><TableCell align="left" sx={{ direction: 'ltr', whiteSpace: 'nowrap' }}>{number.format(item.amount)} {item.currencyCode ?? ''}</TableCell><TableCell>{item.requestedByDisplayName}</TableCell><TableCell><Chip size="small" label={meta.label} color={meta.color} /></TableCell><TableCell sx={{ whiteSpace: 'nowrap' }}>{faDateTime.format(new Date(item.createdAtUtc))}</TableCell><TableCell><Stack direction="row" spacing={.5} flexWrap="wrap" useFlexGap>{item.status === 0 && canDecide && <><Button size="small" color="success" variant="contained" onClick={() => decide(item, 'approve')}>تأیید</Button><Button size="small" color="error" onClick={() => decide(item, 'reject')}>رد</Button></>}{item.decisionComment && <Typography variant="caption" color="text.secondary" title={item.decisionComment}>دارای توضیح</Typography>}</Stack></TableCell></TableRow>
      })}
      {!transfers.length && !loading && <TableRow><TableCell colSpan={8} align="center" sx={{ py: 6 }}><Typography fontWeight={800}>درخواست جابجایی برای سال مالی انتخاب‌شده ثبت نشده است.</Typography></TableCell></TableRow>}
    </TableBody></Table></TableContainer></CardContent></Card>

    <Dialog open={dialogOpen} onClose={() => !dialogBusy && setDialogOpen(false)} fullWidth maxWidth="lg">
      <DialogTitle>درخواست جابجایی بودجه</DialogTitle>
      <DialogContent>
        <Stack spacing={2} mt={1}>
          {dialogBusy && <Box textAlign="center"><CircularProgress size={24} /></Box>}
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5}>
            <FormControl fullWidth><InputLabel>نسخه بودجه</InputLabel><Select value={versionId} label="نسخه بودجه" onChange={e => { setVersionId(e.target.value); setAvailability(null) }}>{operationalVersions.map(({ plan, version }) => <MenuItem key={version.id} value={version.id}>{plan.name} — نسخه {version.versionNumber}</MenuItem>)}</Select></FormControl>
            <FormControl fullWidth><InputLabel>مژر</InputLabel><Select value={measureId} label="مژر" onChange={e => { setMeasureId(e.target.value); setAvailability(null) }}>{measures.map(measure => <MenuItem key={measure.id} value={measure.id}>{measure.name}</MenuItem>)}</Select></FormControl>
            <FormControl fullWidth><InputLabel>دوره مبدأ</InputLabel><Select value={sourcePeriodId} label="دوره مبدأ" onChange={e => { setSourcePeriodId(e.target.value); setAvailability(null) }}>{periods.map(period => <MenuItem key={period.id} value={period.id} disabled={period.isClosed}>{period.name}{period.isClosed ? ' — بسته' : ''}</MenuItem>)}</Select></FormControl>
            <FormControl fullWidth><InputLabel>دوره مقصد</InputLabel><Select value={destinationPeriodId} label="دوره مقصد" onChange={e => { setDestinationPeriodId(e.target.value); setAvailability(null) }}>{periods.map(period => <MenuItem key={period.id} value={period.id} disabled={period.isClosed}>{period.name}{period.isClosed ? ' — بسته' : ''}</MenuItem>)}</Select></FormControl>
          </Stack>

          {selectedModel && <Typography variant="caption" color="text.secondary">مدل: {selectedModel.name}</Typography>}

          <Card variant="outlined"><CardContent><Typography fontWeight={900} mb={1.5}>مختصات مبدأ و مقصد</Typography><Stack spacing={1.5}>{dimensions.map(dimension => <Stack key={dimension.id} direction={{ xs: 'column', md: 'row' }} spacing={1.5} alignItems={{ md: 'center' }}><Typography sx={{ minWidth: 180 }} fontWeight={700}>{dimension.name}{dimension.isRequired ? ' *' : ''}</Typography><FormControl fullWidth size="small"><InputLabel>مبدأ</InputLabel><Select value={sourceMembers[dimension.id] ?? ''} label="مبدأ" onChange={e => { setSourceMembers(current => ({ ...current, [dimension.id]: e.target.value })); setAvailability(null) }}><MenuItem value=""><em>انتخاب نشده</em></MenuItem>{(members[dimension.id] ?? []).map(member => <MenuItem key={member.id} value={member.id}>{member.name} ({member.code})</MenuItem>)}</Select></FormControl><FormControl fullWidth size="small"><InputLabel>مقصد</InputLabel><Select value={destinationMembers[dimension.id] ?? ''} label="مقصد" onChange={e => { setDestinationMembers(current => ({ ...current, [dimension.id]: e.target.value })); setAvailability(null) }}><MenuItem value=""><em>انتخاب نشده</em></MenuItem>{(members[dimension.id] ?? []).map(member => <MenuItem key={member.id} value={member.id}>{member.name} ({member.code})</MenuItem>)}</Select></FormControl></Stack>)}</Stack></CardContent></Card>

          <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5}>
            <TextField fullWidth label="مبلغ جابجایی" value={amount} onChange={e => { setAmount(e.target.value); setAvailability(null) }} inputProps={{ inputMode: 'decimal', style: { direction: 'ltr', textAlign: 'right' } }} />
            <TextField fullWidth label="شماره مرجع/مصوبه" value={externalReference} onChange={e => setExternalReference(e.target.value)} />
          </Stack>
          <TextField multiline minRows={3} label="شرح و دلیل جابجایی بودجه" value={description} onChange={e => setDescription(e.target.value)} />
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={1} alignItems={{ md: 'center' }}><Button variant="outlined" onClick={checkAvailability} disabled={dialogBusy || !versionId || !measureId || !sourcePeriodId || !destinationPeriodId}>بررسی مانده مبدأ</Button>{availability && <Typography variant="body2" color={Number(amount.replace(/,/g, '')) > availability.sourceAvailable ? 'error.main' : 'success.main'}>بودجه مبدأ: {number.format(availability.sourceBudget)} | عملکرد: {number.format(availability.sourceActual)} | تعهد: {number.format(availability.sourceCommitment)} | قابل انتقال: {number.format(availability.sourceAvailable)} | بودجه فعلی مقصد: {number.format(availability.destinationBudget)}</Typography>}</Stack>
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={() => setDialogOpen(false)} disabled={dialogBusy}>انصراف</Button><Button variant="contained" onClick={createTransfer} disabled={dialogBusy || !availability || !description.trim() || !amount.trim()}>ثبت و ارسال برای تأیید</Button></DialogActions>
    </Dialog>
  </Stack>
}
