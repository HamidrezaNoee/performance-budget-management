import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Button, Card, CardContent, Chip, Divider, FormControl, InputLabel, LinearProgress,
  MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow,
  TextField, Typography
} from '@mui/material'
import { api } from './api'

type Version = { id: string; scenarioId: string; versionNumber: number; name: string; status: number; isLocked: boolean }
type Item = { id: string; code: string; name: string }
type Setup = {
  budgetModelId: string; cashFlowItemDimensionId: string; openingCashMeasureId: string; cashInflowMeasureId: string;
  cashOutflowMeasureId: string; minimumCashBufferMeasureId: string; budgetPlanId?: string | null; versions: Version[]; items: Item[]
}
type Period = { id: string; sequence: number; code: string; name: string; jalaliMonth: number; startDate: string; endDate: string; isClosed: boolean }
type Currency = { id: string; code: string; name: string; symbol?: string; isBaseCurrency: boolean }
type Monthly = {
  periodId: string; periodName: string; sequence: number; budgetOpening: number; budgetInflow: number; budgetOutflow: number; budgetClosing: number;
  actualOpening: number; actualInflow: number; actualOutflow: number; actualClosing: number; forecastOpening: number; forecastInflow: number;
  forecastOutflow: number; forecastClosing: number; commitmentOutflow: number; projectedAvailable: number; minimumCashBuffer: number; liquidityGap: number
}
type CurrencySummary = {
  currencyCode: string; budgetInflow: number; budgetOutflow: number; actualInflow: number; actualOutflow: number; forecastInflow: number;
  forecastOutflow: number; commitmentOutflow: number; budgetEndingCash: number; actualEndingCash: number; forecastEndingCash: number;
  projectedAvailableEndingCash: number; minimumProjectedAvailableCash: number; maximumLiquidityShortfall: number; monthsBelowBuffer: number; monthly: Monthly[]
}
type Summary = { versionId: string; companyId: string; fiscalYearId: string; currencies: CurrencySummary[] }
type Entry = {
  factId: string; versionId: string; periodId: string; periodName: string; periodSequence: number; itemMemberId: string; itemCode: string; itemName: string;
  measureCode: string; measureName: string; valueKind: number; value: number; currencyCode: string; note?: string | null; updatedAtUtc: string
}

const faNumber = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 0 })
const statusLabels = ['پیش‌نویس', 'ارسال‌شده', 'در حال بررسی', 'برگشت برای اصلاح', 'تأییدشده', 'ردشده', 'بازنگری‌شده', 'بسته']
const kindLabels = ['بودجه', 'عملکرد واقعی', 'تعهد', 'پیش‌بینی']
const measureLabels: Record<string, string> = {
  OPENING_CASH: 'مانده نقد ابتدای دوره',
  CASH_INFLOW: 'دریافت نقدی',
  CASH_OUTFLOW: 'پرداخت نقدی',
  MINIMUM_CASH_BUFFER: 'حداقل ذخیره نقدینگی'
}
const inflowItems = new Set(['CUSTOMER_COLLECTIONS', 'OTHER_OPERATING_INFLOW', 'LOAN_DRAWDOWN', 'OTHER_FINANCING'])
const outflowItems = new Set(['SUPPLIER_PAYMENTS', 'PAYROLL', 'TAX_AND_DUTY', 'OTHER_OPERATING_OUTFLOW', 'CAPEX_PAYMENTS', 'LOAN_REPAYMENT', 'FINANCE_COST', 'OTHER_FINANCING'])

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? fallback
  }
  return fallback
}

function money(value: number) { return faNumber.format(value) }

export default function CashPlanning({
  companyId, fiscalYearId, canWrite, roles
}: {
  companyId: string; fiscalYearId: string; canWrite: boolean; roles: string[]
}) {
  const [setup, setSetup] = useState<Setup | null>(null)
  const [periods, setPeriods] = useState<Period[]>([])
  const [currencies, setCurrencies] = useState<Currency[]>([])
  const [versionId, setVersionId] = useState('')
  const [currencyCode, setCurrencyCode] = useState('')
  const [summary, setSummary] = useState<CurrencySummary | null>(null)
  const [entries, setEntries] = useState<Entry[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [workflowComment, setWorkflowComment] = useState('')

  const [periodId, setPeriodId] = useState('')
  const [itemMemberId, setItemMemberId] = useState('')
  const [measureCode, setMeasureCode] = useState('CASH_INFLOW')
  const [valueKind, setValueKind] = useState(0)
  const [value, setValue] = useState('')
  const [note, setNote] = useState('')

  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const selectedVersion = useMemo(() => setup?.versions.find(x => x.id === versionId), [setup, versionId])
  const canPlanEdit = canWrite && !!selectedVersion && selectedVersion.status === 0 && !selectedVersion.isLocked
  const canExecutionEdit = canWrite && !!selectedVersion && selectedVersion.status === 4
  const canEditKind = (kind: number) => canPlanEdit || (canExecutionEdit && (kind === 1 || kind === 2))
  const canEditEntry = canEditKind(valueKind)
  const canReview = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || roleSet.has('BUDGET_MANAGER') || roleSet.has('CFO')
  const canApprove = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || roleSet.has('CFO') || roleSet.has('CEO')

  const availableItems = useMemo(() => {
    const items = setup?.items ?? []
    if (measureCode === 'OPENING_CASH') return items.filter(x => x.code === 'OPENING_BALANCE')
    if (measureCode === 'MINIMUM_CASH_BUFFER') return items.filter(x => x.code === 'LIQUIDITY_BUFFER')
    if (measureCode === 'CASH_INFLOW') return items.filter(x => inflowItems.has(x.code))
    if (measureCode === 'CASH_OUTFLOW') return items.filter(x => outflowItems.has(x.code))
    return items
  }, [setup, measureCode])

  useEffect(() => {
    if (availableItems.length === 0) { setItemMemberId(''); return }
    if (!availableItems.some(x => x.id === itemMemberId)) setItemMemberId(availableItems[0].id)
  }, [availableItems, itemMemberId])

  useEffect(() => {
    if (!selectedVersion) return
    if (selectedVersion.status === 4 && valueKind !== 1 && valueKind !== 2) setValueKind(1)
  }, [selectedVersion?.id, selectedVersion?.status])

  const workflowTransitions = useMemo((): Array<[number, string]> => {
    if (!selectedVersion || !canWrite) return []
    switch (selectedVersion.status) {
      case 0: return [[1, 'ارسال برای بررسی']]
      case 1: return canReview ? [[2, 'شروع بررسی'], [3, 'برگشت برای اصلاح'], [5, 'رد برنامه']] : []
      case 2: return [
        ...(canReview ? [[3, 'برگشت برای اصلاح'] as [number, string], [5, 'رد برنامه'] as [number, string]] : []),
        ...(canApprove ? [[4, 'تأیید برنامه'] as [number, string]] : [])
      ]
      case 3: return [[0, 'بازگشت به پیش‌نویس']]
      case 4: return canApprove ? [[7, 'بستن برنامه']] : []
      default: return []
    }
  }, [selectedVersion, canWrite, canReview, canApprove])

  const loadSetup = async () => {
    const [setupResponse, periodResponse, currencyResponse] = await Promise.all([
      api.get<Setup>('/cash-planning/setup', { params: { companyId, fiscalYearId } }),
      api.get<Period[]>('/reference/periods', { params: { fiscalYearId } }),
      api.get<Currency[]>('/reference/currencies')
    ])
    setSetup(setupResponse.data); setPeriods(periodResponse.data); setCurrencies(currencyResponse.data)
    setVersionId(current => current && setupResponse.data.versions.some(x => x.id === current)
      ? current : [...setupResponse.data.versions].sort((a, b) => b.versionNumber - a.versionNumber)[0]?.id ?? '')
    setPeriodId(current => current && periodResponse.data.some(x => x.id === current) ? current : periodResponse.data.find(x => !x.isClosed)?.id ?? periodResponse.data[0]?.id ?? '')
    const base = currencyResponse.data.find(x => x.isBaseCurrency)
    setCurrencyCode(current => current && currencyResponse.data.some(x => x.code === current) ? current : base?.code ?? currencyResponse.data[0]?.code ?? 'IRR')
  }

  const loadVersionData = async () => {
    if (!versionId || !currencyCode) { setSummary(null); setEntries([]); return }
    const [summaryResponse, entryResponse] = await Promise.all([
      api.get<Summary>('/cash-planning/summary', { params: { versionId, currencyCode } }),
      api.get<Entry[]>('/cash-planning/entries', { params: { versionId, currencyCode } })
    ])
    setSummary(summaryResponse.data.currencies.find(x => x.currencyCode === currencyCode) ?? summaryResponse.data.currencies[0] ?? null)
    setEntries(entryResponse.data)
  }

  useEffect(() => {
    if (!companyId || !fiscalYearId) return
    setBusy(true); setError(''); setMessage(''); setSetup(null); setVersionId(''); setSummary(null); setEntries([])
    loadSetup().catch(e => setError(apiError(e, 'بارگذاری ماژول نقدینگی ناموفق بود.'))).finally(() => setBusy(false))
  }, [companyId, fiscalYearId])

  useEffect(() => {
    if (!versionId || !currencyCode) return
    setBusy(true); setError('')
    loadVersionData().catch(e => setError(apiError(e, 'دریافت برنامه نقدینگی ناموفق بود.'))).finally(() => setBusy(false))
  }, [versionId, currencyCode])

  const ensurePlan = async () => {
    if (!canWrite) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<Setup>('/cash-planning/ensure-plan', { companyId, fiscalYearId })
      setSetup(data); setVersionId([...data.versions].sort((a, b) => b.versionNumber - a.versionNumber)[0]?.id ?? '')
      setMessage('برنامه نقدینگی ایجاد و برای ورود اطلاعات آماده شد.')
    } catch (e) { setError(apiError(e, 'ایجاد برنامه نقدینگی ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const saveEntry = async () => {
    if (!canEditEntry || !versionId || !periodId || !itemMemberId || !measureCode || value === '' || !currencyCode) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.put('/cash-planning/entries', {
        versionId, periodId, itemMemberId, measureCode, valueKind, value: Number(value), currencyCode, note: note.trim() || null
      })
      setValue(''); setNote('');
      setMessage(selectedVersion?.status === 4 ? 'داده اجرایی روی نسخه مصوب ثبت شد؛ Budget مصوب بدون تغییر باقی ماند.' : 'آیتم برنامه نقدینگی ذخیره شد.')
      await loadVersionData()
    } catch (e) { setError(apiError(e, 'ثبت آیتم نقدینگی ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const editEntry = (entry: Entry) => {
    if (!canEditKind(entry.valueKind)) return
    setPeriodId(entry.periodId); setMeasureCode(entry.measureCode); setItemMemberId(entry.itemMemberId)
    setValueKind(entry.valueKind); setValue(String(entry.value)); setCurrencyCode(entry.currencyCode); setNote(entry.note ?? '')
  }

  const changeWorkflowStatus = async (status: number) => {
    if (!versionId || !canWrite) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.post(`/budget/versions/${versionId}/status`, { status, comment: workflowComment.trim() || null })
      setWorkflowComment(''); setMessage('وضعیت برنامه نقدینگی به‌روزرسانی شد.'); await loadSetup(); await loadVersionData()
    } catch (e) { setError(apiError(e, 'تغییر وضعیت برنامه نقدینگی ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const baseAllowedKinds = measureCode === 'OPENING_CASH' || measureCode === 'MINIMUM_CASH_BUFFER' ? [0, 1, 3] : [0, 1, 2, 3]
  const allowedKinds = selectedVersion?.status === 4
    ? baseAllowedKinds.filter(kind => kind === 1 || kind === 2)
    : baseAllowedKinds

  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    {message && <Alert severity="success" onClose={() => setMessage('')}>{message}</Alert>}
    {!canWrite && <Alert severity="info">دسترسی شما به برنامه نقدینگی فقط خواندنی است.</Alert>}

    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', lg: 'row' }} justifyContent="space-between" alignItems={{ lg: 'center' }} spacing={2}>
        <div><Typography variant="h6" fontWeight={900}>برنامه نقدینگی و خزانه‌داری</Typography><Typography color="text.secondary">Rolling cash balance بر اساس دریافت، پرداخت، Forecast و تعهدات؛ ارزها به‌صورت Dimension مستقل نگهداری و تحلیل می‌شوند.</Typography></div>
        {!setup?.budgetPlanId && canWrite && <Button variant="contained" onClick={ensurePlan} disabled={busy}>ایجاد Cash Plan</Button>}
      </Stack>
      {setup?.budgetPlanId && <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} mt={2}>
        <FormControl size="small" sx={{ minWidth: 260 }}><InputLabel>نسخه برنامه</InputLabel><Select label="نسخه برنامه" value={versionId} onChange={e => setVersionId(e.target.value)}>{setup.versions.map(x => <MenuItem key={x.id} value={x.id}>نسخه {x.versionNumber.toLocaleString('fa-IR')} — {x.name} — {statusLabels[x.status] ?? x.status}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 170 }}><InputLabel>ارز</InputLabel><Select label="ارز" value={currencyCode} onChange={e => setCurrencyCode(e.target.value)}>{currencies.map(x => <MenuItem key={x.id} value={x.code}>{x.code} — {x.name}</MenuItem>)}</Select></FormControl>
        {selectedVersion && <Chip label={selectedVersion.isLocked ? `${statusLabels[selectedVersion.status] ?? selectedVersion.status} — قفل برنامه` : statusLabels[selectedVersion.status] ?? selectedVersion.status} color={canPlanEdit || canExecutionEdit ? 'success' : 'default'} variant="outlined" />}
      </Stack>}
    </CardContent></Card>

    {workflowTransitions.length > 0 && <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>گردش تأیید Cash Plan</Typography>
      <Typography variant="body2" color="text.secondary">همان State Machine و RBAC بودجه اصلی برای نسخه نقدینگی استفاده می‌شود.</Typography>
      <TextField fullWidth multiline minRows={2} label="توضیح گردش / تصمیم" value={workflowComment} onChange={e => setWorkflowComment(e.target.value)} sx={{ mt: 1.5 }} />
      <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap mt={1.5}>{workflowTransitions.map(([status, label]) => <Button key={status} variant={status === 1 || status === 2 || status === 4 || status === 7 ? 'contained' : 'outlined'} color={status === 5 ? 'error' : 'primary'} onClick={() => changeWorkflowStatus(status)} disabled={busy}>{label}</Button>)}</Stack>
    </CardContent></Card>}

    {summary && <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>شاخص‌های نقدینگی — {summary.currencyCode}</Typography>
      <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap mt={2}>
        <Chip label={`پایان Budget: ${money(summary.budgetEndingCash)}`} />
        <Chip label={`پایان Forecast: ${money(summary.forecastEndingCash)}`} />
        <Chip label={`پس از تعهدات: ${money(summary.projectedAvailableEndingCash)}`} color={summary.projectedAvailableEndingCash < 0 ? 'error' : 'success'} variant="outlined" />
        <Chip label={`کمینه نقد قابل دسترس: ${money(summary.minimumProjectedAvailableCash)}`} variant="outlined" />
        <Chip label={`حداکثر کسری: ${money(summary.maximumLiquidityShortfall)}`} color={summary.maximumLiquidityShortfall > 0 ? 'error' : 'default'} variant="outlined" />
        <Chip label={`ماه زیر Buffer: ${faNumber.format(summary.monthsBelowBuffer)}`} color={summary.monthsBelowBuffer > 0 ? 'warning' : 'default'} variant="outlined" />
      </Stack>
      <TableContainer sx={{ mt: 2 }}><Table size="small"><TableHead><TableRow><TableCell>ماه</TableCell><TableCell>افتتاح Budget</TableCell><TableCell>دریافت Budget</TableCell><TableCell>پرداخت Budget</TableCell><TableCell>اختتام Budget</TableCell><TableCell>اختتام Actual</TableCell><TableCell>اختتام Forecast</TableCell><TableCell>تعهد پرداخت</TableCell><TableCell>قابل دسترس</TableCell><TableCell>Buffer</TableCell><TableCell>Gap</TableCell></TableRow></TableHead><TableBody>{summary.monthly.map(row => <TableRow key={row.periodId} sx={row.liquidityGap < 0 ? { bgcolor: 'rgba(211,47,47,.06)' } : undefined}><TableCell>{row.periodName}</TableCell><TableCell>{money(row.budgetOpening)}</TableCell><TableCell>{money(row.budgetInflow)}</TableCell><TableCell>{money(row.budgetOutflow)}</TableCell><TableCell>{money(row.budgetClosing)}</TableCell><TableCell>{money(row.actualClosing)}</TableCell><TableCell>{money(row.forecastClosing)}</TableCell><TableCell>{money(row.commitmentOutflow)}</TableCell><TableCell>{money(row.projectedAvailable)}</TableCell><TableCell>{money(row.minimumCashBuffer)}</TableCell><TableCell><Chip size="small" label={money(row.liquidityGap)} color={row.liquidityGap < 0 ? 'error' : 'success'} variant="outlined" /></TableCell></TableRow>)}</TableBody></Table></TableContainer>
      {summary.monthly.length > 0 && <LinearProgress variant="determinate" value={Math.max(0, Math.min(100, 100 - (summary.monthsBelowBuffer / summary.monthly.length) * 100))} sx={{ mt: 2 }} />}
    </CardContent></Card>}

    {setup?.budgetPlanId && <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>ثبت دریافت / پرداخت / مانده</Typography>
      {selectedVersion?.status === 4 && canWrite && <Alert severity="info" sx={{ mt: 1.5 }}>نسخه مصوب است: فقط «عملکرد واقعی» و «تعهد» قابل ثبت‌اند. Budget و Forecast مصوب فقط از مسیر Revision تغییر می‌کنند.</Alert>}
      {!canPlanEdit && !canExecutionEdit && <Alert severity="info" sx={{ mt: 1.5 }}>نسخه فعلی در وضعیت قابل ثبت نیست. برای برنامه‌ریزی به Draft و برای ثبت اجرای واقعی به Approved نیاز است.</Alert>}
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.2} mt={2}>
        <FormControl size="small" sx={{ minWidth: 150 }}><InputLabel>ماه</InputLabel><Select label="ماه" value={periodId} onChange={e => setPeriodId(e.target.value)}>{periods.map(x => <MenuItem key={x.id} value={x.id} disabled={x.isClosed}>{x.name}{x.isClosed ? ' — بسته' : ''}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 230 }}><InputLabel>آیتم جریان نقدی</InputLabel><Select label="آیتم جریان نقدی" value={itemMemberId} onChange={e => setItemMemberId(e.target.value)}>{availableItems.map(x => <MenuItem key={x.id} value={x.id}>{x.name} — {x.code}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 190 }}><InputLabel>نوع مبلغ</InputLabel><Select label="نوع مبلغ" value={measureCode} onChange={e => { const next = e.target.value; setMeasureCode(next); if ((next === 'OPENING_CASH' || next === 'MINIMUM_CASH_BUFFER') && valueKind === 2) setValueKind(selectedVersion?.status === 4 ? 1 : 0) }}>{Object.entries(measureLabels).map(([code, label]) => <MenuItem key={code} value={code}>{label}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 150 }}><InputLabel>نوع مقدار</InputLabel><Select label="نوع مقدار" value={valueKind} onChange={e => setValueKind(Number(e.target.value))}>{allowedKinds.map(kind => <MenuItem key={kind} value={kind}>{kindLabels[kind]}</MenuItem>)}</Select></FormControl>
        <TextField size="small" type="number" label={`مبلغ ${currencyCode}`} value={value} onChange={e => setValue(e.target.value)} inputProps={{ min: 0 }} />
        <Button variant="contained" onClick={saveEntry} disabled={!canEditEntry || busy || !periodId || !itemMemberId || value === ''}>ذخیره</Button>
      </Stack>
      <TextField size="small" fullWidth label="یادداشت / منبع" value={note} onChange={e => setNote(e.target.value)} sx={{ mt: 1.5 }} />
      <Divider sx={{ my: 2 }} />
      <Typography fontWeight={800}>ورودی‌های ثبت‌شده</Typography><Typography variant="body2" color="text.secondary">برای ویرایش سطرهای مجاز روی آن کلیک کنید؛ در نسخه مصوب فقط Actual/Commitment قابل ویرایش‌اند و Currency Dimension مانع overwrite ارزهای دیگر است.</Typography>
      <TableContainer sx={{ mt: 1 }}><Table size="small"><TableHead><TableRow><TableCell>ماه</TableCell><TableCell>آیتم</TableCell><TableCell>نوع مبلغ</TableCell><TableCell>نوع مقدار</TableCell><TableCell>مبلغ</TableCell><TableCell>ارز</TableCell><TableCell>یادداشت</TableCell></TableRow></TableHead><TableBody>{entries.map(entry => <TableRow key={entry.factId} hover={canEditKind(entry.valueKind)} onClick={() => editEntry(entry)} sx={{ cursor: canEditKind(entry.valueKind) ? 'pointer' : 'default' }}><TableCell>{entry.periodName}</TableCell><TableCell>{entry.itemName}</TableCell><TableCell>{measureLabels[entry.measureCode] ?? entry.measureName}</TableCell><TableCell>{kindLabels[entry.valueKind] ?? entry.valueKind}</TableCell><TableCell>{money(entry.value)}</TableCell><TableCell>{entry.currencyCode}</TableCell><TableCell>{entry.note ?? '-'}</TableCell></TableRow>)}</TableBody></Table></TableContainer>
    </CardContent></Card>}
  </Stack>
}
