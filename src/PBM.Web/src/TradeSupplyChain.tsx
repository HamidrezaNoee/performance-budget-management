import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, FormControl, InputLabel,
  MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow,
  TextField, Typography
} from '@mui/material'
import AddRoundedIcon from '@mui/icons-material/AddRounded'
import CalculateRoundedIcon from '@mui/icons-material/CalculateRounded'
import Inventory2RoundedIcon from '@mui/icons-material/Inventory2Rounded'
import LocalShippingRoundedIcon from '@mui/icons-material/LocalShippingRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import StorefrontRoundedIcon from '@mui/icons-material/StorefrontRounded'
import AccountBalanceRoundedIcon from '@mui/icons-material/AccountBalanceRounded'
import { api } from './api'

type Model = { id: string; code: string; name: string }
type Version = { id: string; scenarioId: string; versionNumber: number; name: string; status: number; isLocked: boolean }
type Plan = { id: string; budgetModelId: string; name: string; status: number; versions: Version[] }
type Dimension = { id: string; code: string; name: string; sequence: number; isRequired: boolean }
type Member = { id: string; dimensionId: string; code: string; name: string }
type Measure = { id: string; code: string; name: string; unit?: string | null; valueType: number; aggregation: number; isCalculated: boolean; formulaExpression?: string | null }
type Period = { id: string; sequence: number; name: string; isClosed?: boolean }
type GridCell = { periodId: string; factId?: string; value: number }
type GridRow = { memberId: string; code: string; name: string; cells: GridCell[] }
type Grid = { periods: Period[]; measure: Measure; rowDimension: Dimension; rows: GridRow[] }
type LoadedMeasure = { measure: Measure; periods: Period[]; cells: GridCell[] }

type Stage = {
  code: string
  title: string
  subtitle: string
  color: 'info' | 'primary' | 'warning' | 'success'
  measures: string[]
}

const LAST_NON_EMPTY = 4

const stages: Stage[] = [
  {
    code: 'origin',
    title: '۱. خرید از مبدا',
    subtitle: 'CPT، نرخ ارز، مقدار و مبلغ خرید ارزی/ریالی',
    color: 'info',
    measures: ['CPT_UNIT_PRICE', 'FX_RATE', 'IMPORT_QTY', 'IMPORT_FX', 'BASE_UNIT_COST', 'PURCHASE_IRR_AMOUNT']
  },
  {
    code: 'import',
    title: '۲. ثبت سفارش، حمل و گمرک',
    subtitle: 'ثبت سفارش، بانک، بیمه، تعرفه، ارزش افزوده و هزینه تا انبار',
    color: 'warning',
    measures: [
      'ORDER_REG_RATE', 'ORDER_REG_FEE_CALC', 'BANK_FEE_RATE', 'BANK_FEE_CALC',
      'INSURANCE_RATE', 'INSURANCE_CALC', 'CUSTOMS_TARIFF_RATE', 'CUSTOMS_DUTY_CALC',
      'VAT_RATE', 'VAT_AMOUNT', 'FREIGHT_IRR', 'CLEARANCE_FEE', 'INLAND_TRANSPORT',
      'OTHER_IMPORT_COST', 'TRADE_LANDED_COST_TOTAL', 'TRADE_LANDED_COST_PER_UNIT'
    ]
  },
  {
    code: 'warehouse',
    title: '۳. تحویل و گردش انبار',
    subtitle: 'موجودی اول دوره، خرید، جایزه، سمپل، ضایعات، بهای تمام‌شده و موجودی پایان',
    color: 'primary',
    measures: [
      'OPENING_QTY', 'OPENING_VALUE', 'IMPORT_QTY', 'AVAILABLE_QTY', 'COGS_QTY', 'COGS_AMOUNT',
      'FREE_SALES_QTY', 'FOC_COST', 'SAMPLE_QTY', 'SAMPLE_AMOUNT', 'WASTE_QTY', 'WASTE_AMOUNT',
      'TOTAL_COGS_AMOUNT', 'CLOSING_QTY', 'CLOSING_VALUE'
    ]
  },
  {
    code: 'sales',
    title: '۴. فروش و حاشیه سود',
    subtitle: 'فروش مقداری/ریالی، جایزه، تخفیف، فروش خالص و حاشیه سود',
    color: 'success',
    measures: [
      'SALES_QTY', 'FREE_SALES_QTY', 'SALES_PRICE', 'FOC_SALES_AMOUNT', 'GROSS_SALES',
      'SALES_DISCOUNT', 'NET_SALES', 'TOTAL_COGS_AMOUNT', 'TRADE_GROSS_MARGIN', 'TRADE_GROSS_MARGIN_PERCENT'
    ]
  }
]

const valueKinds = [
  { value: 0, label: 'بودجه' },
  { value: 1, label: 'عملکرد واقعی' },
  { value: 2, label: 'تعهدات' },
  { value: 3, label: 'پیش‌بینی' }
]

const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })
const compactNumber = new Intl.NumberFormat('fa-IR', { notation: 'compact', maximumFractionDigits: 1 })

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; message?: string } } }).response
    if (response?.data?.detail) return response.data.detail
    if (response?.data?.message) return response.data.message
  }
  return fallback
}

function sum(cells?: GridCell[]) {
  return (cells ?? []).reduce((total, cell) => total + Number(cell.value || 0), 0)
}

function lastPeriodValue(data?: LoadedMeasure) {
  if (!data?.periods.length) return 0
  const ordered = [...data.periods].sort((a, b) => a.sequence - b.sequence)
  const last = ordered[ordered.length - 1]
  return Number(data.cells.find(x => x.periodId === last.id)?.value ?? 0)
}

function aggregate(data: LoadedMeasure) {
  return data.measure.aggregation === LAST_NON_EMPTY ? lastPeriodValue(data) : sum(data.cells)
}

export default function TradeSupplyChain({ companyId, fiscalYearId, canWrite }: { companyId: string; fiscalYearId: string; canWrite: boolean }) {
  const [tradeModel, setTradeModel] = useState<Model | null>(null)
  const [plan, setPlan] = useState<Plan | null>(null)
  const [versionId, setVersionId] = useState('')
  const [dimensions, setDimensions] = useState<Dimension[]>([])
  const [members, setMembers] = useState<Record<string, Member[]>>({})
  const [measures, setMeasures] = useState<Measure[]>([])
  const [productId, setProductId] = useState('')
  const [supplierId, setSupplierId] = useState('')
  const [valueKind, setValueKind] = useState(0)
  const [stageCode, setStageCode] = useState(stages[0].code)
  const [stageData, setStageData] = useState<Record<string, LoadedMeasure>>({})
  const [overviewData, setOverviewData] = useState<Record<string, LoadedMeasure>>({})
  const [busy, setBusy] = useState(false)
  const [savingKey, setSavingKey] = useState('')
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  const activeStage = stages.find(x => x.code === stageCode) ?? stages[0]
  const productDimension = dimensions.find(x => x.code.toUpperCase() === 'PRODUCT')
  const supplierDimension = dimensions.find(x => x.code.toUpperCase() === 'SUPPLIER')
  const products = productDimension ? members[productDimension.id] ?? [] : []
  const suppliers = supplierDimension ? members[supplierDimension.id] ?? [] : []
  const versions = useMemo(() => [...(plan?.versions ?? [])].sort((a, b) => b.versionNumber - a.versionNumber), [plan])
  const version = versions.find(x => x.id === versionId) ?? versions[0]
  const editable = canWrite && !!version && version.status === 0 && !version.isLocked
  const measureByCode = useMemo(() => Object.fromEntries(measures.map(x => [x.code.toUpperCase(), x])) as Record<string, Measure>, [measures])

  const fixedFilters = () => supplierDimension && supplierId
    ? [{ dimensionId: supplierDimension.id, memberId: supplierId }]
    : []

  const initialize = async () => {
    if (!companyId || !fiscalYearId) return
    setBusy(true); setError(''); setMessage(''); setStageData({}); setOverviewData({})
    try {
      const [modelResponse, planResponse] = await Promise.all([
        api.get<Model[]>('/reference/models', { params: { companyId } }),
        api.get<Plan[]>('/budget/plans', { params: { companyId, fiscalYearId } })
      ])
      const model = modelResponse.data.find(x => x.code.toUpperCase() === 'TRADE') ?? null
      setTradeModel(model)
      if (!model) {
        setPlan(null); setVersionId(''); setDimensions([]); setMeasures([])
        setError('مدل TRADE (واردات، فروش و موجودی) در شرکت یافت نشد. ابتدا داده‌های پایه سیستم را بررسی کنید.')
        return
      }

      const currentPlan = planResponse.data.find(x => x.budgetModelId === model.id) ?? null
      setPlan(currentPlan)
      const latest = [...(currentPlan?.versions ?? [])].sort((a, b) => b.versionNumber - a.versionNumber)[0]
      setVersionId(latest?.id ?? '')

      const [dimensionResponse, measureResponse] = await Promise.all([
        api.get<Dimension[]>('/reference/dimensions', { params: { modelId: model.id } }),
        api.get<Measure[]>('/reference/measures', { params: { modelId: model.id } })
      ])
      setDimensions(dimensionResponse.data)
      setMeasures(measureResponse.data)

      const memberEntries = await Promise.all(dimensionResponse.data.map(async dimension => [
        dimension.id,
        (await api.get<Member[]>('/reference/dimension-members', { params: { dimensionId: dimension.id, companyId } })).data
      ] as const))
      const memberMap = Object.fromEntries(memberEntries) as Record<string, Member[]>
      setMembers(memberMap)
      const productDim = dimensionResponse.data.find(x => x.code.toUpperCase() === 'PRODUCT')
      setProductId(productDim ? memberMap[productDim.id]?.[0]?.id ?? '' : '')
      setSupplierId('')
    } catch (requestError) {
      setError(apiError(requestError, 'دریافت اطلاعات زنجیره خرید و فروش ناموفق بود.'))
    } finally { setBusy(false) }
  }

  useEffect(() => { void initialize() }, [companyId, fiscalYearId])

  const createTradePlan = async () => {
    if (!tradeModel || !canWrite) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<Plan>('/budget/plans', {
        companyId,
        fiscalYearId,
        budgetModelId: tradeModel.id,
        name: 'برنامه خرید، واردات، انبار و فروش'
      })
      setPlan(data)
      const latest = [...data.versions].sort((a, b) => b.versionNumber - a.versionNumber)[0]
      setVersionId(latest?.id ?? '')
      setMessage('برنامه زنجیره تجارت ایجاد شد. اکنون می‌توانید مقادیر ماهانه را ثبت کنید.')
    } catch (requestError) {
      setError(apiError(requestError, 'ایجاد برنامه زنجیره تجارت ناموفق بود.'))
    } finally { setBusy(false) }
  }

  const queryMeasure = async (measure: Measure): Promise<LoadedMeasure> => {
    if (!version || !productDimension || !productId) return { measure, periods: [], cells: [] }
    const { data } = await api.post<Grid>('/budget/grid/query', {
      versionId: version.id,
      rowDimensionId: productDimension.id,
      measureId: measure.id,
      valueKind,
      filters: fixedFilters()
    })
    const row = data.rows.find(x => x.memberId === productId)
    return {
      measure: data.measure,
      periods: data.periods,
      cells: row?.cells ?? data.periods.map(period => ({ periodId: period.id, value: 0 }))
    }
  }

  const loadData = async () => {
    if (!version || !productDimension || !productId) return
    setBusy(true); setError(''); setMessage('')
    try {
      const stageMeasures = activeStage.measures.map(code => measureByCode[code]).filter((x): x is Measure => !!x)
      const overviewCodes = ['IMPORT_QTY', 'TRADE_LANDED_COST_TOTAL', 'CLOSING_QTY', 'CLOSING_VALUE', 'NET_SALES', 'TRADE_GROSS_MARGIN']
      const overviewMeasures = overviewCodes.map(code => measureByCode[code]).filter((x): x is Measure => !!x)
      const [stageResults, overviewResults] = await Promise.all([
        Promise.all(stageMeasures.map(queryMeasure)),
        Promise.all(overviewMeasures.map(queryMeasure))
      ])
      setStageData(Object.fromEntries(stageResults.map(x => [x.measure.code, x])))
      setOverviewData(Object.fromEntries(overviewResults.map(x => [x.measure.code, x])))
    } catch (requestError) {
      setError(apiError(requestError, 'بارگذاری اطلاعات زنجیره تجارت ناموفق بود.'))
    } finally { setBusy(false) }
  }

  useEffect(() => {
    if (version && productDimension && productId && measures.length) void loadData()
  }, [version?.id, productId, supplierId, valueKind, stageCode, measures.length, productDimension?.id])

  const updateLocalCell = (measureCode: string, periodId: string, value: number) => {
    setStageData(current => {
      const item = current[measureCode]
      if (!item) return current
      return {
        ...current,
        [measureCode]: { ...item, cells: item.cells.map(cell => cell.periodId === periodId ? { ...cell, value } : cell) }
      }
    })
  }

  const saveCell = async (loaded: LoadedMeasure, periodId: string, value: number) => {
    if (!version || !productDimension || !productId || !editable || loaded.measure.isCalculated) return
    const key = `${loaded.measure.id}:${periodId}`
    setSavingKey(key); setError(''); setMessage('')
    try {
      const dimensionsForFact = [
        { dimensionId: productDimension.id, memberId: productId },
        ...(supplierDimension && supplierId ? [{ dimensionId: supplierDimension.id, memberId: supplierId }] : [])
      ]
      const { data } = await api.post<{ id: string }>('/budget/facts', {
        versionId: version.id,
        periodId,
        measureId: loaded.measure.id,
        valueKind,
        value,
        currencyCode: loaded.measure.valueType === 0 ? 'IRR' : null,
        dimensions: dimensionsForFact,
        source: 'TradeSupplyChain',
        note: null
      })
      setStageData(current => {
        const item = current[loaded.measure.code]
        if (!item) return current
        return {
          ...current,
          [loaded.measure.code]: {
            ...item,
            cells: item.cells.map(cell => cell.periodId === periodId ? { ...cell, factId: data.id, value } : cell)
          }
        }
      })
    } catch (requestError) {
      setError(apiError(requestError, `ذخیره «${loaded.measure.name}» ناموفق بود.`))
    } finally { setSavingKey('') }
  }

  const recalculate = async () => {
    if (!version || !editable) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<{ coordinatesProcessed: number; factsCreated: number; factsUpdated: number; formulasSkipped: number; errors: string[] }>(`/calculations/versions/${version.id}/recalculate`)
      const changed = data.factsCreated + data.factsUpdated
      setMessage(`محاسبات زنجیره انجام شد؛ ${data.coordinatesProcessed.toLocaleString('fa-IR')} مختصات پردازش و ${changed.toLocaleString('fa-IR')} مقدار محاسباتی ثبت/به‌روزرسانی شد.${data.errors.length ? ` هشدار: ${data.errors.slice(0, 3).join(' | ')}` : ''}`)
      await loadData()
    } catch (requestError) {
      setError(apiError(requestError, 'محاسبه مجدد زنجیره تجارت ناموفق بود.'))
    } finally { setBusy(false) }
  }

  const missingCodes = activeStage.measures.filter(code => !measureByCode[code])
  const selectedProduct = products.find(x => x.id === productId)
  const selectedSupplier = suppliers.find(x => x.id === supplierId)

  const cards = [
    { title: 'خرید سال', value: sum(overviewData.IMPORT_QTY?.cells), unit: 'واحد', icon: <LocalShippingRoundedIcon /> },
    { title: 'بهای خرید تا انبار', value: sum(overviewData.TRADE_LANDED_COST_TOTAL?.cells), unit: 'ریال', icon: <AccountBalanceRoundedIcon /> },
    { title: 'موجودی پایان', value: lastPeriodValue(overviewData.CLOSING_QTY), unit: 'واحد', icon: <Inventory2RoundedIcon /> },
    { title: 'ارزش موجودی پایان', value: lastPeriodValue(overviewData.CLOSING_VALUE), unit: 'ریال', icon: <Inventory2RoundedIcon /> },
    { title: 'فروش خالص سال', value: sum(overviewData.NET_SALES?.cells), unit: 'ریال', icon: <StorefrontRoundedIcon /> },
    { title: 'حاشیه سود سال', value: sum(overviewData.TRADE_GROSS_MARGIN?.cells), unit: 'ریال', icon: <StorefrontRoundedIcon /> }
  ]

  if (busy && !tradeModel) return <Box sx={{ display: 'grid', placeItems: 'center', minHeight: 260 }}><CircularProgress /></Box>

  return <Stack spacing={2.5}>
    <Card variant="outlined" sx={{ overflow: 'visible', background: 'linear-gradient(135deg, #f8fbff 0%, #f7f5ff 100%)' }}>
      <CardContent>
        <Stack direction={{ xs: 'column', lg: 'row' }} justifyContent="space-between" gap={2} alignItems={{ lg: 'center' }}>
          <Box>
            <Typography variant="h5" fontWeight={900}>زنجیره خرید، واردات، انبار و فروش</Typography>
            <Typography color="text.secondary" sx={{ mt: .7, lineHeight: 1.9 }}>
              مدل یکپارچه بودجه و عملکرد از خرید در مبدا تا بهای تمام‌شده، گردش انبار و فروش؛ منطبق با ساختار فایل بودجه مدیریت مالی.
            </Typography>
          </Box>
          <Stack direction="row" gap={1} flexWrap="wrap">
            <Chip label="مدل TRADE" color="primary" variant="outlined" />
            <Chip label={valueKinds.find(x => x.value === valueKind)?.label ?? 'بودجه'} color="info" variant="outlined" />
            {version && <Chip label={`نسخه ${version.versionNumber} - ${version.name}`} color={editable ? 'success' : 'default'} />}
          </Stack>
        </Stack>
      </CardContent>
    </Card>

    {error && <Alert severity="error">{error}</Alert>}
    {message && <Alert severity="success">{message}</Alert>}

    {!plan && tradeModel && <Alert severity="info" action={
      <Button color="inherit" size="small" startIcon={<AddRoundedIcon />} onClick={() => void createTradePlan()} disabled={!canWrite || busy}>ایجاد برنامه</Button>
    }>
      برای این شرکت و سال مالی هنوز برنامه «واردات، فروش و موجودی» ایجاد نشده است.
    </Alert>}

    {tradeModel && dimensions.length > 0 && !productDimension && <Alert severity="warning">بعد PRODUCT در مدل TRADE یافت نشد.</Alert>}
    {productDimension && products.length === 0 && <Alert severity="warning">هیچ کالایی در بعد PRODUCT تعریف نشده است. ابتدا از «تنظیمات و داده‌های پایه» کالاها را ایجاد کنید.</Alert>}

    {plan && <>
      <Card variant="outlined">
        <CardContent>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems={{ md: 'center' }}>
            <FormControl size="small" sx={{ minWidth: 240 }}>
              <InputLabel>کالا</InputLabel>
              <Select label="کالا" value={productId} onChange={e => setProductId(e.target.value)}>
                {products.map(item => <MenuItem key={item.id} value={item.id}>{item.code} — {item.name}</MenuItem>)}
              </Select>
            </FormControl>
            {supplierDimension && <FormControl size="small" sx={{ minWidth: 220 }}>
              <InputLabel>تأمین‌کننده</InputLabel>
              <Select label="تأمین‌کننده" value={supplierId} onChange={e => setSupplierId(e.target.value)}>
                <MenuItem value="">بدون تفکیک تأمین‌کننده</MenuItem>
                {suppliers.map(item => <MenuItem key={item.id} value={item.id}>{item.code} — {item.name}</MenuItem>)}
              </Select>
            </FormControl>}
            <FormControl size="small" sx={{ minWidth: 180 }}>
              <InputLabel>نسخه</InputLabel>
              <Select label="نسخه" value={version?.id ?? ''} onChange={e => setVersionId(e.target.value)}>
                {versions.map(item => <MenuItem key={item.id} value={item.id}>نسخه {item.versionNumber} — {item.name}</MenuItem>)}
              </Select>
            </FormControl>
            <FormControl size="small" sx={{ minWidth: 150 }}>
              <InputLabel>نوع مقدار</InputLabel>
              <Select label="نوع مقدار" value={valueKind} onChange={e => setValueKind(Number(e.target.value))}>
                {valueKinds.map(item => <MenuItem key={item.value} value={item.value}>{item.label}</MenuItem>)}
              </Select>
            </FormControl>
            <Box sx={{ flex: 1 }} />
            <Button variant="outlined" startIcon={<RefreshRoundedIcon />} onClick={() => void loadData()} disabled={busy || !productId}>تازه‌سازی</Button>
            <Button variant="contained" startIcon={<CalculateRoundedIcon />} onClick={() => void recalculate()} disabled={busy || !editable || !productId}>محاسبه مجدد</Button>
          </Stack>
          <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={1} mt={1.5}>
            {!editable && version
              ? <Typography variant="caption" color="text.secondary">این نسخه قفل است یا در وضعیت پیش‌نویس نیست؛ مقادیر فقط قابل مشاهده‌اند.</Typography>
              : <Typography variant="caption" color="text.secondary">نرخ‌های درصدی در PBM به صورت درصد وارد می‌شوند؛ مثال ۵ برای ۵٪.</Typography>}
          </Stack>
        </CardContent>
      </Card>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', xl: 'repeat(6, 1fr)' }, gap: 1.5 }}>
        {cards.map(card => <Card key={card.title} variant="outlined">
          <CardContent sx={{ p: 2, '&:last-child': { pb: 2 } }}>
            <Stack direction="row" justifyContent="space-between" alignItems="center">
              <Box>
                <Typography variant="caption" color="text.secondary">{card.title}</Typography>
                <Typography variant="h6" fontWeight={900} sx={{ mt: .5 }} title={number.format(card.value)}>{compactNumber.format(card.value)}</Typography>
                <Typography variant="caption" color="text.secondary">{card.unit}</Typography>
              </Box>
              <Box sx={{ width: 40, height: 40, display: 'grid', placeItems: 'center', borderRadius: 2.5, bgcolor: 'action.hover', color: 'primary.main' }}>{card.icon}</Box>
            </Stack>
          </CardContent>
        </Card>)}
      </Box>

      <Card variant="outlined">
        <CardContent>
          <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.2} mb={2}>
            {stages.map(stage => <Button
              key={stage.code}
              variant={stage.code === activeStage.code ? 'contained' : 'outlined'}
              color={stage.color}
              onClick={() => setStageCode(stage.code)}
              sx={{ flex: 1, minHeight: 58, justifyContent: 'flex-start', textAlign: 'right' }}
            >
              <Box>
                <Typography fontWeight={900} fontSize={14}>{stage.title}</Typography>
                <Typography component="span" fontSize={11} sx={{ opacity: .8 }}>{stage.subtitle}</Typography>
              </Box>
            </Button>)}
          </Stack>

          <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={1} mb={1.5}>
            <Box>
              <Typography variant="h6" fontWeight={900}>{activeStage.title}</Typography>
              <Typography variant="body2" color="text.secondary">{activeStage.subtitle}</Typography>
            </Box>
            <Typography variant="body2" color="text.secondary">
              {selectedProduct ? `کالا: ${selectedProduct.code} — ${selectedProduct.name}` : 'کالا انتخاب نشده'}
              {selectedSupplier ? ` | تأمین‌کننده: ${selectedSupplier.name}` : ''}
            </Typography>
          </Stack>

          {missingCodes.length > 0 && <Alert severity="warning" sx={{ mb: 2 }}>
            برخی Measureهای این مرحله هنوز روی API جاری بارگذاری نشده‌اند: {missingCodes.join('، ')}. پس از Pull و rebuild سرویس API این موارد خودکار ایجاد می‌شوند.
          </Alert>}

          {busy ? <Box sx={{ display: 'grid', placeItems: 'center', minHeight: 220 }}><CircularProgress /></Box> :
            <TableContainer sx={{ maxHeight: 'calc(100vh - 350px)', border: '1px solid', borderColor: 'divider', borderRadius: 2 }}>
              <Table stickyHeader size="small" sx={{ minWidth: 1250 }}>
                <TableHead>
                  <TableRow>
                    <TableCell sx={{ minWidth: 260, fontWeight: 900, position: 'sticky', right: 0, zIndex: 4, bgcolor: 'background.paper' }}>شاخص / Measure</TableCell>
                    {(Object.values(stageData)[0]?.periods ?? []).slice().sort((a, b) => a.sequence - b.sequence).map(period =>
                      <TableCell key={period.id} align="center" sx={{ minWidth: 130, fontWeight: 900 }}>{period.name}</TableCell>)}
                    <TableCell align="center" sx={{ minWidth: 145, fontWeight: 900 }}>جمع / پایان دوره</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {activeStage.measures.map(code => {
                    const loaded = stageData[code]
                    if (!loaded) return null
                    const orderedPeriods = [...loaded.periods].sort((a, b) => a.sequence - b.sequence)
                    return <TableRow key={code} hover>
                      <TableCell sx={{ position: 'sticky', right: 0, zIndex: 2, bgcolor: loaded.measure.isCalculated ? '#f2f7ff' : 'background.paper', borderLeft: '1px solid', borderColor: 'divider' }}>
                        <Typography fontWeight={800} fontSize={13.5}>{loaded.measure.name}</Typography>
                        <Stack direction="row" gap={.6} alignItems="center" mt={.4} flexWrap="wrap">
                          <Typography variant="caption" color="text.secondary" dir="ltr">{loaded.measure.code}</Typography>
                          {loaded.measure.unit && <Chip size="small" variant="outlined" label={loaded.measure.unit} sx={{ height: 20, fontSize: 10 }} />}
                          {loaded.measure.isCalculated && <Chip size="small" color="info" label="محاسباتی" sx={{ height: 20, fontSize: 10 }} />}
                        </Stack>
                      </TableCell>
                      {orderedPeriods.map(period => {
                        const cell = loaded.cells.find(x => x.periodId === period.id) ?? { periodId: period.id, value: 0 }
                        const saveKey = `${loaded.measure.id}:${period.id}`
                        return <TableCell key={period.id} align="center" sx={{ bgcolor: loaded.measure.isCalculated ? '#f8fbff' : undefined }}>
                          {loaded.measure.isCalculated
                            ? <Typography fontWeight={700} fontSize={13}>{number.format(cell.value)}</Typography>
                            : <TextField
                                size="small"
                                type="number"
                                value={Number.isFinite(cell.value) ? cell.value : 0}
                                disabled={!editable || period.isClosed || savingKey === saveKey}
                                onChange={event => updateLocalCell(code, period.id, Number(event.target.value))}
                                onBlur={event => void saveCell(loaded, period.id, Number(event.target.value))}
                                inputProps={{ step: 'any', style: { textAlign: 'center', minWidth: 90 } }}
                              />}
                        </TableCell>
                      })}
                      <TableCell align="center" sx={{ fontWeight: 900, bgcolor: 'action.hover' }}>{number.format(aggregate(loaded))}</TableCell>
                    </TableRow>
                  })}
                </TableBody>
              </Table>
            </TableContainer>}

          <Typography variant="caption" color="text.secondary" display="block" mt={1.5} sx={{ lineHeight: 1.9 }}>
            فیلدهای آبی/محاسباتی از سایر ورودی‌ها محاسبه می‌شوند. پس از تغییر نرخ ارز، هزینه‌ها، خرید یا فروش، «محاسبه مجدد» را اجرا کنید. مقدارهای ریالی با واحد IRR در BudgetFact ذخیره می‌شوند و Budget / Actual / Commitment / Forecast از یک مدل مشترک استفاده می‌کنند.
          </Typography>
        </CardContent>
      </Card>
    </>}
  </Stack>
}
