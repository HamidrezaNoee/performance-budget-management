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
type Selection = { dimensionId: string; memberId: string }
type Availability = { budget: number; actual: number; commitment: number; available: number }
type Reservation = {
  id: string
  reservationNo: string
  companyId: string
  versionId: string
  versionNumber: number
  periodId: string
  periodName: string
  measureId: string
  measureName: string
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
  releasedAtUtc?: string | null
  consumedAtUtc?: string | null
  dimensions: Selection[]
}

const faDateTime = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { dateStyle: 'short', timeStyle: 'short' })
const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })
const statusMeta = [
  { label: 'درخواست‌شده', color: 'warning' as const },
  { label: 'تأییدشده', color: 'success' as const },
  { label: 'ردشده', color: 'error' as const },
  { label: 'آزادشده', color: 'default' as const },
  { label: 'مصرف‌شده', color: 'info' as const }
]

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? fallback
  }
  return fallback
}

export default function BudgetReservations({ companyId, fiscalYearId, roles }: { companyId: string; fiscalYearId: string; roles: string[] }) {
  const [reservations, setReservations] = useState<Reservation[]>([])
  const [plans, setPlans] = useState<Plan[]>([])
  const [models, setModels] = useState<Model[]>([])
  const [periods, setPeriods] = useState<Period[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [statusFilter, setStatusFilter] = useState<number | ''>('')
  const [dialogOpen, setDialogOpen] = useState(false)
  const [versionId, setVersionId] = useState('')
  const [periodId, setPeriodId] = useState('')
  const [measureId, setMeasureId] = useState('')
  const [amount, setAmount] = useState('')
  const [description, setDescription] = useState('')
  const [externalReference, setExternalReference] = useState('')
  const [dimensions, setDimensions] = useState<Dimension[]>([])
  const [members, setMembers] = useState<Record<string, Member[]>>({})
  const [selections, setSelections] = useState<Record<string, string>>({})
  const [measures, setMeasures] = useState<Measure[]>([])
  const [availability, setAvailability] = useState<Availability | null>(null)
  const [dialogBusy, setDialogBusy] = useState(false)

  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const canDecide = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || roleSet.has('BUDGET_MANAGER') || roleSet.has('CFO')
  const operationalVersions = useMemo(() => plans.flatMap(plan => plan.versions.filter(v => v.status === 4 || v.status === 7).map(v => ({ plan, version: v }))), [plans])
  const selectedVersionItem = operationalVersions.find(x => x.version.id === versionId)
  const selectedModel = models.find(x => x.id === selectedVersionItem?.plan.budgetModelId)
  const selectedMeasure = measures.find(x => x.id === measureId)

  const reloadReservations = async () => {
    if (!companyId) return
    setLoading(true); setError('')
    try {
      const { data } = await api.get<Reservation[]>('/reservations/', { params: { companyId, status: statusFilter === '' ? undefined : statusFilter, take: 300 } })
      setReservations(data)
    } catch (error) { setError(apiError(error, 'دریافت رزروهای بودجه ناموفق بود.')) }
    finally { setLoading(false) }
  }

  useEffect(() => {
    if (!companyId || !fiscalYearId) return
    setLoading(true); setError(''); setMessage(''); setReservations([]); setPlans([]); setModels([]); setPeriods([])
    Promise.all([
      api.get<Reservation[]>('/reservations/', { params: { companyId, take: 300 } }),
      api.get<Plan[]>('/budget/plans', { params: { companyId, fiscalYearId } }),
      api.get<Model[]>('/reference/models', { params: { companyId } }),
      api.get<Period[]>('/reference/periods', { params: { fiscalYearId } })
    ]).then(([reservationResponse, planResponse, modelResponse, periodResponse]) => {
      setReservations(reservationResponse.data)
      setPlans(planResponse.data)
      setModels(modelResponse.data)
      setPeriods(periodResponse.data)
    }).catch(error => setError(apiError(error, 'بارگذاری فضای رزرو بودجه ناموفق بود.'))).finally(() => setLoading(false))
  }, [companyId, fiscalYearId])

  useEffect(() => { if (companyId) reloadReservations() }, [statusFilter])

  useEffect(() => {
    if (!dialogOpen) return
    const first = operationalVersions[0]
    setVersionId(first?.version.id ?? '')
    setPeriodId(periods.find(x => !x.isClosed)?.id ?? '')
    setAmount(''); setDescription(''); setExternalReference(''); setAvailability(null); setSelections({}); setMeasures([]); setDimensions([]); setMembers({})
  }, [dialogOpen])

  useEffect(() => {
    if (!dialogOpen || !selectedVersionItem) return
    const modelId = selectedVersionItem.plan.budgetModelId
    setDialogBusy(true); setAvailability(null); setSelections({})
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
      const defaults: Record<string, string> = {}
      dims.forEach(dimension => { if (memberMap[dimension.id]?.length) defaults[dimension.id] = memberMap[dimension.id][0].id })
      setSelections(defaults)
    }).catch(error => setError(apiError(error, 'دریافت مختصات بودجه برای رزرو ناموفق بود.'))).finally(() => setDialogBusy(false))
  }, [dialogOpen, versionId])

  const coordinate = () => dimensions
    .filter(dimension => selections[dimension.id])
    .map(dimension => ({ dimensionId: dimension.id, memberId: selections[dimension.id] }))

  const checkAvailability = async () => {
    if (!versionId || !periodId || !measureId) return
    const missingRequired = dimensions.some(dimension => dimension.isRequired && !selections[dimension.id])
    if (missingRequired) { setError('تمام ابعاد اجباری بودجه را انتخاب کنید.'); return }
    setDialogBusy(true); setError('')
    try {
      const { data } = await api.post<Availability>('/reservations/availability', { versionId, periodId, measureId, dimensions: coordinate() })
      setAvailability(data)
    } catch (error) { setAvailability(null); setError(apiError(error, 'محاسبه مانده قابل رزرو ناموفق بود.')) }
    finally { setDialogBusy(false) }
  }

  const createReservation = async () => {
    const numericAmount = Number(amount.replace(/,/g, '').trim())
    if (!Number.isFinite(numericAmount) || numericAmount <= 0) { setError('مبلغ رزرو معتبر نیست.'); return }
    if (!description.trim()) { setError('شرح درخواست رزرو الزامی است.'); return }
    if (!availability) { setError('قبل از ثبت، مانده بودجه را بررسی کنید.'); return }
    if (numericAmount > availability.available) { setError('مبلغ درخواست از مانده قابل رزرو بیشتر است.'); return }

    setDialogBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<Reservation>('/reservations/', {
        companyId, versionId, periodId, measureId, amount: numericAmount,
        currencyCode: selectedMeasure?.valueType === 0 ? 'IRR' : null,
        description: description.trim(), dimensions: coordinate(), externalReference: externalReference.trim() || null
      })
      setDialogOpen(false)
      setMessage(`درخواست رزرو ${data.reservationNo} ثبت شد و برای بررسی در گردش قرار گرفت.`)
      setStatusFilter('')
      await reloadReservations()
    } catch (error) { setError(apiError(error, 'ثبت درخواست رزرو ناموفق بود.')) }
    finally { setDialogBusy(false) }
  }

  const decide = async (reservation: Reservation, action: 'approve' | 'reject' | 'release') => {
    const labels = { approve: 'تأیید', reject: 'رد', release: 'آزادسازی' }
    const comment = window.prompt(`توضیح ${labels[action]} رزرو ${reservation.reservationNo}:`, '')
    if (comment === null) return
    setLoading(true); setError(''); setMessage('')
    try {
      await api.post(`/reservations/${reservation.id}/${action}`, { comment: comment.trim() || null })
      setMessage(`${labels[action]} رزرو ${reservation.reservationNo} انجام شد.`)
      await reloadReservations()
    } catch (error) { setError(apiError(error, `${labels[action]} رزرو ناموفق بود.`)) }
    finally { setLoading(false) }
  }

  const consume = async (reservation: Reservation) => {
    const external = window.prompt(`شماره سند/مرجع مصرف رزرو ${reservation.reservationNo}:`, reservation.externalReference ?? '')
    if (external === null) return
    const comment = window.prompt('توضیح مصرف رزرو:', '')
    if (comment === null) return
    setLoading(true); setError(''); setMessage('')
    try {
      await api.post(`/reservations/${reservation.id}/consume`, { externalReference: external.trim() || null, comment: comment.trim() || null })
      setMessage(`رزرو ${reservation.reservationNo} به‌عنوان مصرف‌شده ثبت شد.`)
      await reloadReservations()
    } catch (error) { setError(apiError(error, 'ثبت مصرف رزرو ناموفق بود.')) }
    finally { setLoading(false) }
  }

  return <Stack spacing={2.5}>
    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2}>
        <Box><Typography variant="h6" fontWeight={900}>رزرو بودجه و مدیریت تعهدات</Typography><Typography color="text.secondary">قبل از ایجاد تعهد، مانده بودجه کنترل می‌شود و تأیید رزرو مستقیماً در Commitment بودجه منعکس خواهد شد.</Typography></Box>
        <Stack direction="row" spacing={1}>
          <FormControl size="small" sx={{ minWidth: 170 }}><InputLabel>وضعیت</InputLabel><Select value={statusFilter} label="وضعیت" onChange={e => setStatusFilter(e.target.value === '' ? '' : Number(e.target.value))}><MenuItem value="">همه</MenuItem>{statusMeta.map((item, index) => <MenuItem key={item.label} value={index}>{item.label}</MenuItem>)}</Select></FormControl>
          <Button variant="outlined" onClick={reloadReservations} disabled={loading}>به‌روزرسانی</Button>
          <Button variant="contained" onClick={() => setDialogOpen(true)} disabled={operationalVersions.length === 0}>درخواست رزرو</Button>
        </Stack>
      </Stack>
    </CardContent></Card>

    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    {message && <Alert severity="success" onClose={() => setMessage('')}>{message}</Alert>}
    {!operationalVersions.length && !loading && <Alert severity="info">برای ثبت رزرو، حداقل یک نسخه بودجه با وضعیت تأییدشده یا بسته‌شده لازم است.</Alert>}
    {loading && !reservations.length && <Box py={6} textAlign="center"><CircularProgress /></Box>}

    <Card elevation={0}><CardContent sx={{ p: 0 }}><TableContainer sx={{ maxHeight: '68vh' }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>شماره / شرح</TableCell><TableCell>دوره / مژر</TableCell><TableCell align="left">مبلغ</TableCell><TableCell>درخواست‌کننده</TableCell><TableCell>وضعیت</TableCell><TableCell>تاریخ</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>
      {reservations.map(item => {
        const meta = statusMeta[item.status] ?? statusMeta[0]
        return <TableRow key={item.id} hover><TableCell><Typography fontWeight={900} sx={{ direction: 'ltr', textAlign: 'right' }}>{item.reservationNo}</Typography><Typography variant="body2">{item.description}</Typography>{item.externalReference && <Typography variant="caption" color="text.secondary">مرجع: {item.externalReference}</Typography>}</TableCell><TableCell><Typography fontWeight={700}>{item.periodName}</Typography><Typography variant="caption" color="text.secondary">{item.measureName} — نسخه {item.versionNumber.toLocaleString('fa-IR')}</Typography></TableCell><TableCell align="left" sx={{ direction: 'ltr', whiteSpace: 'nowrap' }}>{number.format(item.amount)} {item.currencyCode ?? ''}</TableCell><TableCell>{item.requestedByDisplayName}</TableCell><TableCell><Chip size="small" label={meta.label} color={meta.color} /></TableCell><TableCell sx={{ whiteSpace: 'nowrap' }}>{faDateTime.format(new Date(item.createdAtUtc))}</TableCell><TableCell><Stack direction="row" spacing={.5} flexWrap="wrap" useFlexGap>
          {item.status === 0 && canDecide && <><Button size="small" color="success" variant="contained" onClick={() => decide(item, 'approve')}>تأیید</Button><Button size="small" color="error" onClick={() => decide(item, 'reject')}>رد</Button></>}
          {item.status === 1 && canDecide && <><Button size="small" color="warning" onClick={() => decide(item, 'release')}>آزادسازی</Button><Button size="small" variant="contained" onClick={() => consume(item)}>مصرف</Button></>}
          {item.decisionComment && <Typography variant="caption" color="text.secondary" alignSelf="center" title={item.decisionComment}>دارای توضیح</Typography>}
        </Stack></TableCell></TableRow>
      })}
      {!reservations.length && !loading && <TableRow><TableCell colSpan={7} align="center" sx={{ py: 6 }}><Typography fontWeight={800}>رزروی ثبت نشده است.</Typography></TableCell></TableRow>}
    </TableBody></Table></TableContainer></CardContent></Card>

    <Dialog open={dialogOpen} onClose={() => !dialogBusy && setDialogOpen(false)} fullWidth maxWidth="md">
      <DialogTitle>درخواست رزرو بودجه</DialogTitle>
      <DialogContent>
        <Stack spacing={2} mt={1}>
          {dialogBusy && <Box textAlign="center"><CircularProgress size={24} /></Box>}
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5}>
            <FormControl fullWidth><InputLabel>نسخه بودجه</InputLabel><Select value={versionId} label="نسخه بودجه" onChange={e => { setVersionId(e.target.value); setAvailability(null) }}>{operationalVersions.map(({ plan, version }) => <MenuItem key={version.id} value={version.id}>{plan.name} — نسخه {version.versionNumber} ({version.status === 4 ? 'تأییدشده' : 'بسته‌شده'})</MenuItem>)}</Select></FormControl>
            <FormControl fullWidth><InputLabel>دوره</InputLabel><Select value={periodId} label="دوره" onChange={e => { setPeriodId(e.target.value); setAvailability(null) }}>{periods.map(period => <MenuItem key={period.id} value={period.id} disabled={period.isClosed}>{period.name}{period.isClosed ? ' — بسته' : ''}</MenuItem>)}</Select></FormControl>
            <FormControl fullWidth><InputLabel>مژر بودجه</InputLabel><Select value={measureId} label="مژر بودجه" onChange={e => { setMeasureId(e.target.value); setAvailability(null) }}>{measures.map(measure => <MenuItem key={measure.id} value={measure.id}>{measure.name}</MenuItem>)}</Select></FormControl>
          </Stack>

          {selectedModel && <Typography variant="caption" color="text.secondary">مدل: {selectedModel.name}</Typography>}

          {dimensions.length > 0 && <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} flexWrap="wrap" useFlexGap>{dimensions.map(dimension => <FormControl key={dimension.id} sx={{ minWidth: 220, flex: 1 }}><InputLabel>{dimension.name}{dimension.isRequired ? ' *' : ''}</InputLabel><Select value={selections[dimension.id] ?? ''} label={`${dimension.name}${dimension.isRequired ? ' *' : ''}`} onChange={e => { setSelections(current => ({ ...current, [dimension.id]: e.target.value })); setAvailability(null) }}><MenuItem value=""><em>انتخاب نشده</em></MenuItem>{(members[dimension.id] ?? []).map(member => <MenuItem key={member.id} value={member.id}>{member.name} ({member.code})</MenuItem>)}</Select></FormControl>)}</Stack>}

          <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5}>
            <TextField fullWidth label="مبلغ رزرو" value={amount} onChange={e => setAmount(e.target.value)} inputProps={{ inputMode: 'decimal', style: { direction: 'ltr', textAlign: 'right' } }} />
            <TextField fullWidth label="شماره مرجع/درخواست" value={externalReference} onChange={e => setExternalReference(e.target.value)} />
          </Stack>
          <TextField multiline minRows={3} label="شرح رزرو و علت ایجاد تعهد" value={description} onChange={e => setDescription(e.target.value)} />

          <Stack direction="row" spacing={1} alignItems="center"><Button variant="outlined" onClick={checkAvailability} disabled={dialogBusy || !versionId || !periodId || !measureId}>بررسی مانده قابل رزرو</Button>{availability && <Typography variant="body2" color={Number(amount.replace(/,/g, '')) > availability.available ? 'error.main' : 'success.main'}>مانده قابل رزرو: {number.format(availability.available)} | بودجه: {number.format(availability.budget)} | عملکرد: {number.format(availability.actual)} | تعهد: {number.format(availability.commitment)}</Typography>}</Stack>
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={() => setDialogOpen(false)} disabled={dialogBusy}>انصراف</Button><Button variant="contained" onClick={createReservation} disabled={dialogBusy || !availability || !description.trim() || !amount.trim()}>ثبت و ارسال برای تأیید</Button></DialogActions>
    </Dialog>
  </Stack>
}
