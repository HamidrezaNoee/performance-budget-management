import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Button, Card, CardContent, Chip, FormControl, InputLabel, MenuItem, Select, Stack,
  Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography
} from '@mui/material'
import { api } from './api'

type FiscalYear = { id: string; code: string; name: string; jalaliYear: number }
type CurrencySummary = {
  currencyCode: string; coordinateCount: number; reconciledCount: number; openIssueCount: number;
  consumedAmount: number; actualAmount: number; variance: number
}
type Item = {
  companyId: string; fiscalYearId: string; versionId: string; versionNumber: number; periodId: string;
  periodName: string; measureId: string; measureCode: string; measureName: string; coordinateHash: string;
  currencyCode: string; reservationNumbers: string[]; externalReferences: string[]; reservationCount: number;
  consumedAmount: number; actualAmount: number; variance: number; allowedTolerance: number;
  firstConsumedAtUtc: string; lastConsumedAtUtc: string; daysSinceFirstConsumption: number; status: number;
  actualFactId?: string | null; actualSource?: string | null; actualUpdatedAtUtc?: string | null
}
type Summary = {
  companyId: string; fiscalYearId?: string | null; graceDays: number; tolerancePercent: number;
  coordinateCount: number; reconciledCount: number; awaitingCount: number; missingCount: number;
  underPostedCount: number; overPostedCount: number; currencyConflictCount: number;
  currencies: CurrencySummary[]; items: Item[]
}

const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })
const dateTime = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { dateStyle: 'short', timeStyle: 'short' })
const statusMeta = [
  { label: 'در انتظار Actual', color: 'info' as const },
  { label: 'Actual ثبت نشده', color: 'error' as const },
  { label: 'تطبیق‌شده', color: 'success' as const },
  { label: 'Actual کمتر از مصرف', color: 'warning' as const },
  { label: 'Actual بیشتر از مصرف', color: 'warning' as const },
  { label: 'تعارض ارز', color: 'error' as const }
]

function apiError(error: unknown) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? 'دریافت گزارش تطبیق ناموفق بود.'
  }
  return 'دریافت گزارش تطبیق ناموفق بود.'
}

export default function ReservationReconciliationAdmin({ companyId }: { companyId: string }) {
  const [years, setYears] = useState<FiscalYear[]>([])
  const [fiscalYearId, setFiscalYearId] = useState('')
  const [graceDays, setGraceDays] = useState(2)
  const [tolerancePercent, setTolerancePercent] = useState(0.1)
  const [summary, setSummary] = useState<Summary | null>(null)
  const [statusFilter, setStatusFilter] = useState<number | ''>('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!companyId) return
    setBusy(true); setError(''); setSummary(null); setFiscalYearId('')
    api.get<FiscalYear[]>('/reference/fiscal-years', { params: { companyId } })
      .then(response => {
        setYears(response.data)
        setFiscalYearId(response.data[0]?.id ?? '')
      })
      .catch(error => setError(apiError(error)))
      .finally(() => setBusy(false))
  }, [companyId])

  const reload = async () => {
    if (!companyId) return
    setBusy(true); setError('')
    try {
      const { data } = await api.get<Summary>('/budget/reservations/reconciliation', {
        params: {
          companyId,
          fiscalYearId: fiscalYearId || undefined,
          graceDays,
          tolerancePercent
        }
      })
      setSummary(data)
    } catch (error) { setError(apiError(error)) }
    finally { setBusy(false) }
  }

  useEffect(() => { if (companyId && fiscalYearId) reload() }, [companyId, fiscalYearId])

  const items = useMemo(() => summary?.items.filter(x => statusFilter === '' || x.status === statusFilter) ?? [], [summary, statusFilter])
  const openIssues = summary ? summary.coordinateCount - summary.reconciledCount : 0

  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>تطبیق رزرو مصرف‌شده با Actual</Typography>
      <Typography color="text.secondary" mt={.5}>رزروهای Consumed در سطح Version / Period / Measure / Coordinate تجمیع و با Actual همان مختصات مقایسه می‌شوند. این کنترل Actual تولید نمی‌کند و فقط مغایرت را آشکار می‌کند.</Typography>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} mt={2} alignItems={{ md: 'center' }}>
        <FormControl size="small" sx={{ minWidth: 220 }}><InputLabel>سال مالی</InputLabel><Select label="سال مالی" value={fiscalYearId} onChange={e => setFiscalYearId(e.target.value)}>{years.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl>
        <TextField size="small" type="number" label="مهلت ثبت Actual (روز)" value={graceDays} onChange={e => setGraceDays(Math.max(0, Math.min(365, Number(e.target.value))))} inputProps={{ min: 0, max: 365 }} />
        <TextField size="small" type="number" label="Tolerance %" value={tolerancePercent} onChange={e => setTolerancePercent(Math.max(0, Math.min(100, Number(e.target.value))))} inputProps={{ min: 0, max: 100, step: .1 }} />
        <FormControl size="small" sx={{ minWidth: 190 }}><InputLabel>وضعیت تطبیق</InputLabel><Select label="وضعیت تطبیق" value={statusFilter} onChange={e => setStatusFilter(String(e.target.value) === '' ? '' : Number(e.target.value))}><MenuItem value="">همه</MenuItem>{statusMeta.map((x, index) => <MenuItem key={x.label} value={index}>{x.label}</MenuItem>)}</Select></FormControl>
        <Button variant="outlined" onClick={reload} disabled={busy}>بازمحاسبه کنترل</Button>
      </Stack>
    </CardContent></Card>

    {summary && <>
      <Card elevation={0}><CardContent>
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
          <Chip label={`مختصات مصرف‌شده: ${number.format(summary.coordinateCount)}`} />
          <Chip label={`تطبیق‌شده: ${number.format(summary.reconciledCount)}`} color="success" variant="outlined" />
          <Chip label={`موارد باز: ${number.format(openIssues)}`} color={openIssues > 0 ? 'warning' : 'success'} variant="outlined" />
          <Chip label={`Actual مفقود: ${number.format(summary.missingCount)}`} color={summary.missingCount > 0 ? 'error' : 'default'} variant="outlined" />
          <Chip label={`Under: ${number.format(summary.underPostedCount)}`} color={summary.underPostedCount > 0 ? 'warning' : 'default'} variant="outlined" />
          <Chip label={`Over: ${number.format(summary.overPostedCount)}`} color={summary.overPostedCount > 0 ? 'warning' : 'default'} variant="outlined" />
          <Chip label={`تعارض ارز: ${number.format(summary.currencyConflictCount)}`} color={summary.currencyConflictCount > 0 ? 'error' : 'default'} variant="outlined" />
        </Stack>
      </CardContent></Card>

      <Card elevation={0}><CardContent sx={{ p: 0 }}>
        <TableContainer><Table size="small"><TableHead><TableRow><TableCell>ارز</TableCell><TableCell>مختصات</TableCell><TableCell>تطبیق‌شده</TableCell><TableCell>باز</TableCell><TableCell align="left">Consumed</TableCell><TableCell align="left">Actual</TableCell><TableCell align="left">Variance</TableCell></TableRow></TableHead><TableBody>{summary.currencies.map(x => <TableRow key={x.currencyCode}><TableCell>{x.currencyCode}</TableCell><TableCell>{number.format(x.coordinateCount)}</TableCell><TableCell>{number.format(x.reconciledCount)}</TableCell><TableCell>{number.format(x.openIssueCount)}</TableCell><TableCell align="left">{number.format(x.consumedAmount)}</TableCell><TableCell align="left">{number.format(x.actualAmount)}</TableCell><TableCell align="left">{number.format(x.variance)}</TableCell></TableRow>)}</TableBody></Table></TableContainer>
      </CardContent></Card>

      <Card elevation={0}><CardContent sx={{ p: 0 }}>
        <TableContainer sx={{ maxHeight: '62vh' }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>دوره / Measure</TableCell><TableCell>رزروها / مرجع</TableCell><TableCell>وضعیت</TableCell><TableCell align="left">Consumed</TableCell><TableCell align="left">Actual</TableCell><TableCell align="left">Variance</TableCell><TableCell>سن مصرف</TableCell><TableCell>منبع Actual</TableCell></TableRow></TableHead><TableBody>
          {items.map(item => {
            const meta = statusMeta[item.status] ?? statusMeta[0]
            return <TableRow key={`${item.versionId}-${item.periodId}-${item.measureId}-${item.coordinateHash}`} hover>
              <TableCell><Typography fontWeight={800}>{item.periodName} — {item.measureName}</Typography><Typography variant="caption" color="text.secondary">{item.measureCode} | نسخه {item.versionNumber.toLocaleString('fa-IR')} | {item.currencyCode}</Typography></TableCell>
              <TableCell><Typography variant="body2" sx={{ direction: 'ltr', textAlign: 'right' }}>{item.reservationNumbers.join(', ')}</Typography><Typography variant="caption" color="text.secondary">{item.externalReferences.length ? `مرجع: ${item.externalReferences.join(', ')}` : 'بدون مرجع خارجی'}</Typography></TableCell>
              <TableCell><Chip size="small" label={meta.label} color={meta.color} /></TableCell>
              <TableCell align="left">{number.format(item.consumedAmount)}</TableCell>
              <TableCell align="left">{number.format(item.actualAmount)}</TableCell>
              <TableCell align="left">{number.format(item.variance)}</TableCell>
              <TableCell>{number.format(item.daysSinceFirstConsumption)} روز<br/><Typography variant="caption" color="text.secondary">{dateTime.format(new Date(item.firstConsumedAtUtc))}</Typography></TableCell>
              <TableCell>{item.actualSource ?? '-'}{item.actualUpdatedAtUtc && <><br/><Typography variant="caption" color="text.secondary">{dateTime.format(new Date(item.actualUpdatedAtUtc))}</Typography></>}</TableCell>
            </TableRow>
          })}
          {!items.length && <TableRow><TableCell colSpan={8} align="center" sx={{ py: 6 }}>موردی با فیلتر انتخاب‌شده وجود ندارد.</TableCell></TableRow>}
        </TableBody></Table></TableContainer>
      </CardContent></Card>
    </>}
  </Stack>
}
