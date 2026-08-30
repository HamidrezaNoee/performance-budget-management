import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, FormControl, InputLabel,
  MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow,
  TextField, ToggleButton, ToggleButtonGroup, Typography
} from '@mui/material'
import AddRoundedIcon from '@mui/icons-material/AddRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import ShoppingCartCheckoutRoundedIcon from '@mui/icons-material/ShoppingCartCheckoutRounded'
import { api } from './api'

type Member = { id: string; dimensionId: string; code: string; name: string }
type Dimension = { id: string; code: string; name: string; sequence: number; isRequired: boolean; members: Member[] }
type Measure = { id: string; code: string; name: string; unit?: string | null; valueType: number; aggregation: number }
type Setup = {
  modelId: string
  modelName: string
  baseCurrencyCode: string
  dimensions: Dimension[]
  costTypes: Member[]
  quantityMeasure: Measure
  amountMeasure: Measure
  costAmountMeasure: Measure
  costRateMeasure: Measure
}
type Version = { id: string; scenarioId: string; versionNumber: number; name: string; status: number; isLocked: boolean }
type Plan = { id: string; companyId: string; fiscalYearId: string; budgetModelId: string; name: string; status: number; versions: Version[] }
type Period = { id: string; sequence: number; code: string; name: string; jalaliMonth: number; isClosed: boolean }
type PeriodValue = { periodId: string; periodName: string; sequence: number; value: number; factId?: string | null }
type CostSeries = { costTypeId: string; code: string; name: string; amounts: PeriodValue[]; rates: PeriodValue[] }
type PlanningData = { periods: Period[]; quantity: PeriodValue[]; amount: PeriodValue[]; costs: CostSeries[] }
type PlanningKind = 'budget' | 'forecast'

const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })
const compact = new Intl.NumberFormat('fa-IR', { notation: 'compact', maximumFractionDigits: 1 })

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; message?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.message ?? response?.data?.title ?? fallback
  }
  return fallback
}

function valueFor(values: PeriodValue[], periodId: string) {
  return Number(values.find(x => x.periodId === periodId)?.value ?? 0)
}

export default function PurchaseForecastPlanner({
  companyId,
  fiscalYearId,
  canWrite
}: {
  companyId: string
  fiscalYearId: string
  canWrite: boolean
}) {
  const [setup, setSetup] = useState<Setup | null>(null)
  const [plan, setPlan] = useState<Plan | null>(null)
  const [versionId, setVersionId] = useState('')
  const [selections, setSelections] = useState<Record<string, string>>({})
  const [data, setData] = useState<PlanningData | null>(null)
  const [planningKind, setPlanningKind] = useState<PlanningKind>('budget')
  const [costMode, setCostMode] = useState<'amount' | 'rate'>('amount')
  const [newCostCode, setNewCostCode] = useState('')
  const [newCostName, setNewCostName] = useState('')
  const [busy, setBusy] = useState(false)
  const [savingKey, setSavingKey] = useState('')
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  const valueKind = planningKind === 'budget' ? 0 : 3
  const planningLabel = planningKind === 'budget' ? 'بودجه خرید' : 'Forecast خرید'
  const versions = useMemo(() => [...(plan?.versions ?? [])].sort((a, b) => b.versionNumber - a.versionNumber), [plan])
  const version = versions.find(x => x.id === versionId) ?? versions[0]
  const editable = canWrite && !!version && version.status === 0 && !version.isLocked
  const selectedDimensions = useMemo(() => {
    if (!setup) return []
    return setup.dimensions
      .filter(dimension => !!selections[dimension.id])
      .map(dimension => ({ dimensionId: dimension.id, memberId: selections[dimension.id] }))
  }, [setup, selections])
  const requiredReady = useMemo(() => !!setup && setup.dimensions
    .filter(x => x.isRequired || x.code.toUpperCase() === 'PRODUCT')
    .every(x => !!selections[x.id]), [setup, selections])

  const reloadSetupAndPlan = async () => {
    if (!companyId || !fiscalYearId) return
    setBusy(true); setError(''); setMessage(''); setData(null)
    try {
      const [setupResponse, planResponse] = await Promise.all([
        api.get<Setup>('/purchase-forecast/setup', { params: { companyId } }),
        api.get<Plan[]>('/budget/plans', { params: { companyId, fiscalYearId } })
      ])
      const nextSetup = setupResponse.data
      setSetup(nextSetup)
      const nextPlan = planResponse.data.find(x => x.budgetModelId === nextSetup.modelId) ?? null
      setPlan(nextPlan)
      const latest = [...(nextPlan?.versions ?? [])].sort((a, b) => b.versionNumber - a.versionNumber)[0]
      setVersionId(latest?.id ?? '')

      const defaults: Record<string, string> = {}
      nextSetup.dimensions.forEach(dimension => {
        if ((dimension.isRequired || dimension.code.toUpperCase() === 'PRODUCT') && dimension.members.length)
          defaults[dimension.id] = dimension.members[0].id
      })
      setSelections(defaults)
    } catch (requestError) {
      setError(apiError(requestError, 'دریافت تنظیمات برنامه‌ریزی خرید ناموفق بود.'))
    } finally { setBusy(false) }
  }

  useEffect(() => { void reloadSetupAndPlan() }, [companyId, fiscalYearId])

  const query = async () => {
    if (!version || !requiredReady) { setData(null); return }
    setBusy(true); setError('')
    try {
      const response = await api.post<PlanningData>('/purchase-forecast/query', {
        versionId: version.id,
        dimensions: selectedDimensions,
        valueKind
      })
      setData(response.data)
    } catch (requestError) {
      setError(apiError(requestError, `بارگذاری ${planningLabel} ناموفق بود.`))
    } finally { setBusy(false) }
  }

  useEffect(() => {
    if (version && requiredReady) void query()
    else setData(null)
  }, [version?.id, requiredReady, valueKind, JSON.stringify(selectedDimensions)])

  const createPlan = async () => {
    if (!setup || !canWrite) return
    setBusy(true); setError(''); setMessage('')
    try {
      const response = await api.post<Plan>('/budget/plans', {
        companyId,
        fiscalYearId,
        budgetModelId: setup.modelId,
        name: 'برنامه بودجه و پیش‌بینی خرید، واردات و فروش'
      })
      setPlan(response.data)
      const latest = [...response.data.versions].sort((a, b) => b.versionNumber - a.versionNumber)[0]
      setVersionId(latest?.id ?? '')
      setMessage('برنامه TRADE برای بودجه و پیش‌بینی خرید ایجاد شد.')
    } catch (requestError) {
      setError(apiError(requestError, 'ایجاد برنامه خرید ناموفق بود.'))
    } finally { setBusy(false) }
  }

  const createCostType = async () => {
    if (!canWrite || !newCostCode.trim() || !newCostName.trim()) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.post('/purchase-forecast/cost-types', {
        companyId,
        code: newCostCode.trim().toUpperCase(),
        name: newCostName.trim()
      })
      setNewCostCode(''); setNewCostName('')
      const response = await api.get<Setup>('/purchase-forecast/setup', { params: { companyId } })
      setSetup(response.data)
      setMessage('نوع هزینه جدید اضافه شد و در بودجه و Forecast خرید قابل استفاده است.')
      if (version && requiredReady) await query()
    } catch (requestError) {
      setError(apiError(requestError, 'ایجاد نوع هزینه خرید ناموفق بود.'))
    } finally { setBusy(false) }
  }

  const saveCell = async (measureCode: string, periodId: string, value: number, costTypeId?: string) => {
    if (!editable || !version || !requiredReady || !Number.isFinite(value)) return
    const key = `${measureCode}:${costTypeId ?? 'base'}:${periodId}`
    setSavingKey(key); setError(''); setMessage('')
    try {
      await api.post('/purchase-forecast/cell', {
        versionId: version.id,
        periodId,
        measureCode,
        value,
        dimensions: selectedDimensions,
        costTypeId: costTypeId ?? null,
        note: null,
        valueKind
      })
      await query()
    } catch (requestError) {
      setError(apiError(requestError, `ذخیره ${planningLabel} ناموفق بود.`))
    } finally { setSavingKey('') }
  }

  const totalQuantity = data?.quantity.reduce((sum, x) => sum + Number(x.value || 0), 0) ?? 0
  const totalAmount = data?.amount.reduce((sum, x) => sum + Number(x.value || 0), 0) ?? 0
  const totalCosts = data?.costs.reduce((sum, cost) => sum + cost.amounts.reduce((s, x) => s + Number(x.value || 0), 0), 0) ?? 0
  const totalPlanning = totalAmount + totalCosts

  if (busy && !setup) return <Box py={8} display="flex" justifyContent="center"><CircularProgress /></Box>

  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    {message && <Alert severity="success" onClose={() => setMessage('')}>{message}</Alert>}

    <Card elevation={0} sx={{ background: 'linear-gradient(135deg, rgba(25,118,210,.08), rgba(46,125,50,.07))' }}>
      <CardContent>
        <Stack direction={{ xs: 'column', lg: 'row' }} justifyContent="space-between" spacing={2} alignItems={{ lg: 'center' }}>
          <Box>
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <ShoppingCartCheckoutRoundedIcon color="primary" />
              <Typography variant="h5" fontWeight={900}>بودجه و پیش‌بینی چندبعدی خرید کالا</Typography>
              <Chip label={planningKind === 'budget' ? 'Budget' : 'Forecast'} color={planningKind === 'budget' ? 'success' : 'primary'} variant="outlined" />
            </Stack>
            <Typography color="text.secondary" mt={1}>
              ثبت ماهانه تعداد، مبلغ خرید و هزینه‌های جانبی به تفکیک کالا و هر ترکیب دلخواه از تأمین‌کننده، برند، ارز، قرارداد، منطقه، واحد، مرکز هزینه، حساب، برنامه، فعالیت، پروژه و منبع تأمین مالی.
            </Typography>
          </Box>
          <Stack direction="row" spacing={1} alignItems="center">
            <ToggleButtonGroup exclusive size="small" value={planningKind} onChange={(_, value) => value && setPlanningKind(value)}>
              <ToggleButton value="budget">بودجه خرید</ToggleButton>
              <ToggleButton value="forecast">Forecast خرید</ToggleButton>
            </ToggleButtonGroup>
            <Button startIcon={<RefreshRoundedIcon />} onClick={() => void reloadSetupAndPlan()} disabled={busy}>بازخوانی</Button>
          </Stack>
        </Stack>
      </CardContent>
    </Card>

    {!plan && <Alert severity="info" action={canWrite ? <Button color="inherit" onClick={createPlan}>ایجاد برنامه</Button> : undefined}>
      برای سال مالی انتخاب‌شده برنامه TRADE وجود ندارد. {canWrite ? 'با دکمه «ایجاد برنامه» آن را بسازید.' : 'برای ایجاد برنامه با مدیر بودجه تماس بگیرید.'}
    </Alert>}

    {setup && <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>مختصات {planningLabel}</Typography>
      <Typography color="text.secondary" mb={2}>کالا اجباری است؛ سایر Dimensionها را فقط زمانی انتخاب کنید که می‌خواهید داده در آن سطح تفکیک شود.</Typography>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.3} flexWrap="wrap" useFlexGap>
        {setup.dimensions.map(dimension => <FormControl key={dimension.id} size="small" sx={{ minWidth: 205 }}>
          <InputLabel>{dimension.name}{dimension.isRequired || dimension.code.toUpperCase() === 'PRODUCT' ? ' *' : ''}</InputLabel>
          <Select
            label={`${dimension.name}${dimension.isRequired || dimension.code.toUpperCase() === 'PRODUCT' ? ' *' : ''}`}
            value={selections[dimension.id] ?? ''}
            onChange={e => setSelections(current => ({ ...current, [dimension.id]: e.target.value }))}
          >
            {!dimension.isRequired && dimension.code.toUpperCase() !== 'PRODUCT' && <MenuItem value=""><em>بدون تفکیک</em></MenuItem>}
            {dimension.members.map(member => <MenuItem key={member.id} value={member.id}>{member.name} — {member.code}</MenuItem>)}
          </Select>
        </FormControl>)}
        {plan && <FormControl size="small" sx={{ minWidth: 220 }}>
          <InputLabel>نسخه</InputLabel>
          <Select label="نسخه" value={version?.id ?? ''} onChange={e => setVersionId(e.target.value)}>
            {versions.map(x => <MenuItem key={x.id} value={x.id}>{x.name} — نسخه {x.versionNumber.toLocaleString('fa-IR')}</MenuItem>)}
          </Select>
        </FormControl>}
      </Stack>
      {!requiredReady && <Alert severity="warning" sx={{ mt: 2 }}>برای ادامه، حداقل کالا و تمام Dimensionهای اجباری را انتخاب کنید.</Alert>}
      {version && !editable && <Alert severity="warning" sx={{ mt: 2 }}>این نسخه Draft باز نیست؛ اطلاعات فقط قابل مشاهده است.</Alert>}
    </CardContent></Card>}

    {data && <>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
        {[
          [`تعداد ${planningLabel}`, number.format(totalQuantity), 'واحد'],
          ['مبلغ پایه خرید', compact.format(totalAmount), setup?.baseCurrencyCode ?? ''],
          ['هزینه‌های جانبی', compact.format(totalCosts), setup?.baseCurrencyCode ?? ''],
          [`کل ${planningLabel}`, compact.format(totalPlanning), setup?.baseCurrencyCode ?? '']
        ].map(([label, value, unit]) => <Card key={label} elevation={0} sx={{ flex: 1 }}><CardContent>
          <Typography color="text.secondary" variant="body2">{label}</Typography>
          <Typography variant="h5" fontWeight={900} mt={1}>{value}</Typography>
          <Typography variant="caption" color="text.secondary">{unit}</Typography>
        </CardContent></Card>)}
      </Stack>

      <Card elevation={0}><CardContent>
        <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2} mb={2}>
          <Box>
            <Typography variant="h6" fontWeight={900}>هزینه‌های قابل تعریف خرید</Typography>
            <Typography color="text.secondary">هزینه جدید به‌صورت عضو Dimension «نوع هزینه خرید» ایجاد می‌شود و در Budget و Forecast همان مختصات قابل ثبت است.</Typography>
          </Box>
          <ToggleButtonGroup exclusive size="small" value={costMode} onChange={(_, value) => value && setCostMode(value)}>
            <ToggleButton value="amount">مبلغ هزینه</ToggleButton>
            <ToggleButton value="rate">درصد هزینه</ToggleButton>
          </ToggleButtonGroup>
        </Stack>
        {canWrite && <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.2} mb={2}>
          <TextField size="small" label="کد هزینه" value={newCostCode} onChange={e => setNewCostCode(e.target.value.toUpperCase())} placeholder="INSPECTION_FEE" sx={{ direction: 'ltr', minWidth: 180 }} />
          <TextField size="small" label="نام هزینه" value={newCostName} onChange={e => setNewCostName(e.target.value)} placeholder="هزینه بازرسی مبدا" sx={{ minWidth: 240 }} />
          <Button startIcon={<AddRoundedIcon />} variant="outlined" onClick={createCostType} disabled={busy || !newCostCode.trim() || !newCostName.trim()}>افزودن هزینه</Button>
        </Stack>}
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
          {setup?.costTypes.map(cost => <Chip key={cost.id} label={`${cost.name} (${cost.code})`} variant="outlined" />)}
        </Stack>
      </CardContent></Card>

      <Card elevation={0}><CardContent sx={{ p: 0 }}>
        <Box p={2.5}>
          <Typography variant="h6" fontWeight={900}>{planningLabel} ماهانه</Typography>
          <Typography color="text.secondary">در حالت درصد، ثبت نرخ هزینه باعث محاسبه مبلغ هزینه بر مبنای مبلغ خرید همان ماه و همان ترکیب Dimensionها می‌شود.</Typography>
        </Box>
        <TableContainer sx={{ maxHeight: '68vh' }}>
          <Table stickyHeader size="small" sx={{ minWidth: 950 + (data.costs.length * 150) }}>
            <TableHead><TableRow>
              <TableCell sx={{ minWidth: 120 }}>ماه</TableCell>
              <TableCell sx={{ minWidth: 150 }}>تعداد خرید</TableCell>
              <TableCell sx={{ minWidth: 180 }}>مبلغ خرید</TableCell>
              {data.costs.map(cost => <TableCell key={cost.costTypeId} sx={{ minWidth: 165 }}>{cost.name}{costMode === 'rate' ? ' ٪' : ''}</TableCell>)}
              <TableCell sx={{ minWidth: 180 }}>جمع ماه</TableCell>
            </TableRow></TableHead>
            <TableBody>{data.periods.map(period => {
              const qty = valueFor(data.quantity, period.id)
              const amount = valueFor(data.amount, period.id)
              const monthlyCosts = data.costs.reduce((sum, cost) => sum + valueFor(cost.amounts, period.id), 0)
              return <TableRow key={period.id} hover>
                <TableCell><Typography fontWeight={800}>{period.name}</Typography></TableCell>
                <TableCell><TextField
                  size="small" type="number" value={qty}
                  disabled={!editable || period.isClosed || savingKey === `PURCHASE_FORECAST_QTY:base:${period.id}`}
                  onChange={e => setData(current => current ? { ...current, quantity: current.quantity.map(x => x.periodId === period.id ? { ...x, value: Number(e.target.value) } : x) } : current)}
                  onBlur={e => void saveCell('PURCHASE_FORECAST_QTY', period.id, Number(e.target.value))}
                  inputProps={{ min: 0, step: 'any' }}
                /></TableCell>
                <TableCell><TextField
                  size="small" type="number" value={amount}
                  disabled={!editable || period.isClosed || savingKey === `PURCHASE_FORECAST_AMOUNT:base:${period.id}`}
                  onChange={e => setData(current => current ? { ...current, amount: current.amount.map(x => x.periodId === period.id ? { ...x, value: Number(e.target.value) } : x) } : current)}
                  onBlur={e => void saveCell('PURCHASE_FORECAST_AMOUNT', period.id, Number(e.target.value))}
                  inputProps={{ min: 0, step: 'any' }}
                /></TableCell>
                {data.costs.map(cost => {
                  const values = costMode === 'amount' ? cost.amounts : cost.rates
                  const measureCode = costMode === 'amount' ? 'PURCHASE_COST_AMOUNT' : 'PURCHASE_COST_RATE'
                  const key = `${measureCode}:${cost.costTypeId}:${period.id}`
                  return <TableCell key={cost.costTypeId}><TextField
                    size="small" type="number" value={valueFor(values, period.id)}
                    disabled={!editable || period.isClosed || savingKey === key}
                    onChange={e => {
                      const value = Number(e.target.value)
                      setData(current => current ? {
                        ...current,
                        costs: current.costs.map(item => item.costTypeId !== cost.costTypeId ? item : {
                          ...item,
                          [costMode === 'amount' ? 'amounts' : 'rates']: (costMode === 'amount' ? item.amounts : item.rates)
                            .map(x => x.periodId === period.id ? { ...x, value } : x)
                        })
                      } : current)
                    }}
                    onBlur={e => void saveCell(measureCode, period.id, Number(e.target.value), cost.costTypeId)}
                    inputProps={{ min: 0, step: 'any' }}
                  /></TableCell>
                })}
                <TableCell><Typography fontWeight={900}>{number.format(amount + monthlyCosts)}</Typography></TableCell>
              </TableRow>
            })}</TableBody>
          </Table>
        </TableContainer>
      </CardContent></Card>
    </>}
  </Stack>
}
