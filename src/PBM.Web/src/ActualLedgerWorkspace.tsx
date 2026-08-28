import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Chip, Dialog, DialogActions, DialogContent,
  DialogTitle, FormControl, InputLabel, MenuItem, Select, Stack, Table, TableBody,
  TableCell, TableContainer, TableHead, TableRow, TextField, Typography
} from '@mui/material'
import { api } from './api'

type Version = { id: string; versionNumber: number; name: string; status: number; isLocked: boolean }
type Plan = { id: string; budgetModelId: string; name: string; versions: Version[] }
type Period = { id: string; sequence: number; name: string }
type Measure = { id: string; code: string; name: string }
type LedgerEntry = {
  id: string
  entryType: number
  originalEntryId?: string | null
  companyId: string
  versionId: string
  periodId: string
  measureId: string
  sourceSystem: string
  externalDocumentId: string
  externalLineId: string
  postingDate: string
  amount: number
  currencyCode: string
  coordinateHash: string
  note?: string | null
  reversalReason?: string | null
  isReversed: boolean
  createdAtUtc: string
}
type Reconciliation = {
  versionId: string
  periodId: string
  measureId: string
  coordinateHash: string
  currencyCode: string
  ledgerAmount: number
  projectedAmount?: number | null
  projectedCurrencyCode?: string | null
  status: number
  difference: number
}

const faNumber = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })
const faDate = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { dateStyle: 'short' })
const faDateTime = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { dateStyle: 'short', timeStyle: 'short' })
const versionStatus = ['پیش‌نویس', 'ارسال‌شده', 'در بررسی', 'برگشتی', 'مصوب', 'ردشده', 'بازنگری‌شده', 'بسته']
const reconciliationStatus = [
  { label: 'تطبیق', color: 'success' as const },
  { label: 'Projection مفقود', color: 'error' as const },
  { label: 'اختلاف مبلغ', color: 'warning' as const },
  { label: 'اختلاف ارز', color: 'error' as const },
  { label: 'Projection بدون Ledger', color: 'warning' as const }
]

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? fallback
  }
  return fallback
}

export default function ActualLedgerWorkspace({
  companyId,
  fiscalYearId,
  roles,
  canWrite
}: {
  companyId: string
  fiscalYearId: string
  roles: string[]
  canWrite: boolean
}) {
  const [plans, setPlans] = useState<Plan[]>([])
  const [versionId, setVersionId] = useState('')
  const [periods, setPeriods] = useState<Period[]>([])
  const [measures, setMeasures] = useState<Measure[]>([])
  const [entries, setEntries] = useState<LedgerEntry[]>([])
  const [reconciliation, setReconciliation] = useState<Reconciliation[]>([])
  const [sourceSystem, setSourceSystem] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [reverseEntry, setReverseEntry] = useState<LedgerEntry | null>(null)
  const [reversalReason, setReversalReason] = useState('')

  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const canRebuild = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || roleSet.has('CFO') || roleSet.has('BUDGET_MANAGER')
  const versions = useMemo(() => plans.flatMap(plan => plan.versions.map(version => ({ plan, version })))
    .filter(x => x.version.status !== 5)
    .sort((a, b) => {
      const aOperational = a.version.status === 4 ? 1 : 0
      const bOperational = b.version.status === 4 ? 1 : 0
      return bOperational - aOperational || b.version.versionNumber - a.version.versionNumber
    }), [plans])
  const selected = useMemo(() => versions.find(x => x.version.id === versionId), [versions, versionId])
  const periodById = useMemo(() => new Map(periods.map(x => [x.id, x])), [periods])
  const measureById = useMemo(() => new Map(measures.map(x => [x.id, x])), [measures])

  useEffect(() => {
    setPlans([]); setVersionId(''); setEntries([]); setReconciliation([]); setError('')
    if (!companyId || !fiscalYearId) return
    setBusy(true)
    api.get<Plan[]>('/budget/plans', { params: { companyId, fiscalYearId } })
      .then(response => {
        setPlans(response.data)
        const candidates = response.data.flatMap(plan => plan.versions.map(version => ({ plan, version })))
          .filter(x => x.version.status !== 5)
          .sort((a, b) => (b.version.status === 4 ? 1 : 0) - (a.version.status === 4 ? 1 : 0) || b.version.versionNumber - a.version.versionNumber)
        setVersionId(candidates[0]?.version.id ?? '')
      })
      .catch(error => setError(apiError(error, 'دریافت نسخه‌های بودجه ناموفق بود.')))
      .finally(() => setBusy(false))
  }, [companyId, fiscalYearId])

  useEffect(() => {
    if (!selected) { setMeasures([]); return }
    api.get<Measure[]>('/reference/measures', { params: { modelId: selected.plan.budgetModelId } })
      .then(response => setMeasures(response.data))
      .catch(error => setError(apiError(error, 'دریافت Measureها ناموفق بود.')))
  }, [selected?.plan.budgetModelId])

  useEffect(() => {
    if (!fiscalYearId) { setPeriods([]); return }
    api.get<Period[]>('/reference/periods', { params: { fiscalYearId } })
      .then(response => setPeriods(response.data))
      .catch(error => setError(apiError(error, 'دریافت دوره‌های مالی ناموفق بود.')))
  }, [fiscalYearId])

  const reload = async () => {
    if (!versionId) return
    setBusy(true); setError(''); setMessage('')
    try {
      const [entriesResponse, reconciliationResponse] = await Promise.all([
        api.get<LedgerEntry[]>('/actual-ledger/entries', {
          params: { companyId, fiscalYearId, versionId, sourceSystem: sourceSystem.trim() || undefined, take: 1000 }
        }),
        api.get<Reconciliation[]>('/actual-ledger/reconciliation', { params: { versionId, tolerance: 0.01 } })
      ])
      setEntries(entriesResponse.data)
      setReconciliation(reconciliationResponse.data)
    } catch (error) { setError(apiError(error, 'دریافت Actual Ledger یا Reconciliation ناموفق بود.')) }
    finally { setBusy(false) }
  }

  useEffect(() => { if (versionId) reload() }, [versionId])

  const reverse = async () => {
    if (!reverseEntry || !canWrite || reversalReason.trim().length < 5) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.post(`/actual-ledger/${reverseEntry.id}/reverse`, { reason: reversalReason.trim() })
      setReverseEntry(null); setReversalReason('')
      setMessage('Reversal ثبت و Projection Actual همان Coordinate دوباره محاسبه شد.')
      await reload()
    } catch (error) { setError(apiError(error, 'ثبت Reversal ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const rebuild = async () => {
    if (!canRebuild || !versionId || !window.confirm('Projection تمام Coordinateهای Actual Ledger این نسخه از نو ساخته شود؟ Ledger immutable باقی می‌ماند.')) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<{ rebuilt: number }>('/actual-ledger/rebuild-projection', null, { params: { versionId } })
      setMessage(`${data.rebuilt.toLocaleString('fa-IR')} Coordinate از Actual Ledger بازسازی شد.`)
      await reload()
    } catch (error) { setError(apiError(error, 'بازسازی Projection ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const mismatches = reconciliation.filter(x => x.status !== 0).length

  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    {message && <Alert severity="success" onClose={() => setMessage('')}>{message}</Alert>}
    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>Actual Ledger — اتصال ERP / حسابداری</Typography>
      <Typography color="text.secondary" mt={.5}>هر ردیف منبع با شناسه خارجی immutable ثبت می‌شود؛ اصلاح با Reversal است و BudgetFact.Actual فقط Projection تجمیعی Ledger است. Retry تکراری با همان Business Key دوباره سند مالی ایجاد نمی‌کند.</Typography>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.5} mt={2} alignItems={{ lg: 'center' }}>
        <FormControl size="small" sx={{ minWidth: 360 }}><InputLabel>نسخه بودجه</InputLabel><Select label="نسخه بودجه" value={versionId} onChange={e => setVersionId(e.target.value)}>{versions.map(({ plan, version }) => <MenuItem key={version.id} value={version.id}>{plan.name} — {version.name} — v{version.versionNumber} — {versionStatus[version.status] ?? version.status}</MenuItem>)}</Select></FormControl>
        <TextField size="small" label="فیلتر Source System" value={sourceSystem} onChange={e => setSourceSystem(e.target.value)} placeholder="ERP / SAP / ACCOUNTING" />
        <Button variant="outlined" onClick={reload} disabled={busy || !versionId}>به‌روزرسانی</Button>
        {canRebuild && <Button color="warning" variant="outlined" onClick={rebuild} disabled={busy || !versionId}>Rebuild Projection</Button>}
        <Chip label={`اختلاف: ${mismatches.toLocaleString('fa-IR')}`} color={mismatches ? 'warning' : 'success'} variant="outlined" />
      </Stack>
    </CardContent></Card>

    {!versions.length && !busy && <Alert severity="info">برای سال مالی انتخاب‌شده نسخه بودجه‌ای جهت اتصال Actual وجود ندارد.</Alert>}

    <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <Box p={2.5}><Typography variant="h6" fontWeight={900}>ردیف‌های Ledger</Typography><Typography variant="caption" color="text.secondary">ورودی اصلی این جدول Endpoint یکپارچه‌سازی `POST /api/v1/actual-ledger/post` است.</Typography></Box>
      <TableContainer sx={{ maxHeight: 430 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>منبع / سند</TableCell><TableCell>دوره / Measure</TableCell><TableCell>تاریخ سند</TableCell><TableCell align="left">مبلغ</TableCell><TableCell>نوع</TableCell><TableCell>Coordinate</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>
        {entries.map(entry => <TableRow key={entry.id} hover>
          <TableCell><Typography fontWeight={800}>{entry.sourceSystem}</Typography><Typography variant="caption" color="text.secondary" sx={{ direction: 'ltr', display: 'block' }}>{entry.externalDocumentId} / {entry.externalLineId}</Typography></TableCell>
          <TableCell><Typography variant="body2">{periodById.get(entry.periodId)?.name ?? entry.periodId}</Typography><Typography variant="caption" color="text.secondary">{measureById.get(entry.measureId)?.name ?? entry.measureId}</Typography></TableCell>
          <TableCell sx={{ whiteSpace: 'nowrap' }}>{faDate.format(new Date(entry.postingDate))}<Typography variant="caption" color="text.secondary" display="block">ثبت: {faDateTime.format(new Date(entry.createdAtUtc))}</Typography></TableCell>
          <TableCell align="left" sx={{ direction: 'ltr', fontWeight: 800 }}>{faNumber.format(entry.amount)} {entry.currencyCode}</TableCell>
          <TableCell>{entry.entryType === 1 ? <Chip size="small" label="Reversal" color="warning" /> : <Chip size="small" label={entry.isReversed ? 'Posting — برگشت‌شده' : 'Posting'} color={entry.isReversed ? 'default' : 'success'} variant="outlined" />}{entry.reversalReason && <Typography variant="caption" color="text.secondary" display="block" mt={.5}>{entry.reversalReason}</Typography>}</TableCell>
          <TableCell sx={{ direction: 'ltr', fontFamily: 'monospace', fontSize: 11, maxWidth: 180, overflow: 'hidden', textOverflow: 'ellipsis' }}>{entry.coordinateHash}</TableCell>
          <TableCell>{entry.entryType === 0 && !entry.isReversed && canWrite ? <Button size="small" color="warning" onClick={() => { setReverseEntry(entry); setReversalReason('') }}>Reversal</Button> : '-'}</TableCell>
        </TableRow>)}
        {!entries.length && !busy && <TableRow><TableCell colSpan={7} align="center" sx={{ py: 5 }}>برای فیلتر انتخاب‌شده ردیف Ledger وجود ندارد.</TableCell></TableRow>}
      </TableBody></Table></TableContainer>
    </Card>

    <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <Box p={2.5}><Typography variant="h6" fontWeight={900}>Reconciliation Ledger ↔ BudgetFact.Actual</Typography><Typography variant="caption" color="text.secondary">مقدار Ledger از Posting + Reversal محاسبه و با Projection عملیاتی مقایسه می‌شود.</Typography></Box>
      <TableContainer sx={{ maxHeight: 360 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>دوره</TableCell><TableCell>Measure</TableCell><TableCell>ارز</TableCell><TableCell align="left">Ledger</TableCell><TableCell align="left">Projection</TableCell><TableCell align="left">اختلاف</TableCell><TableCell>وضعیت</TableCell></TableRow></TableHead><TableBody>
        {reconciliation.map((row, index) => {
          const meta = reconciliationStatus[row.status] ?? reconciliationStatus[2]
          return <TableRow key={`${row.periodId}-${row.measureId}-${row.coordinateHash}-${index}`} hover><TableCell>{periodById.get(row.periodId)?.name ?? row.periodId}</TableCell><TableCell>{measureById.get(row.measureId)?.name ?? row.measureId}</TableCell><TableCell sx={{ direction: 'ltr' }}>{row.currencyCode || row.projectedCurrencyCode || '-'}</TableCell><TableCell align="left">{faNumber.format(row.ledgerAmount)}</TableCell><TableCell align="left">{row.projectedAmount == null ? '-' : faNumber.format(row.projectedAmount)}</TableCell><TableCell align="left">{faNumber.format(row.difference)}</TableCell><TableCell><Chip size="small" label={meta.label} color={meta.color} variant="outlined" /></TableCell></TableRow>
        })}
        {!reconciliation.length && !busy && <TableRow><TableCell colSpan={7} align="center" sx={{ py: 5 }}>Coordinate مدیریت‌شده توسط Actual Ledger وجود ندارد.</TableCell></TableRow>}
      </TableBody></Table></TableContainer>
    </Card>

    <Dialog open={!!reverseEntry} onClose={() => !busy && setReverseEntry(null)} fullWidth maxWidth="sm">
      <DialogTitle>ثبت Reversal سند Actual</DialogTitle><DialogContent><Stack spacing={1.5} mt={1}>{reverseEntry && <Alert severity="warning">این عملیات ردیف اصلی را حذف یا ویرایش نمی‌کند؛ یک ردیف Reversal با مبلغ معکوس ایجاد می‌شود. Reversal روی دوره مالی بسته مجاز نیست.</Alert>}<TextField multiline minRows={4} label="علت Reversal *" value={reversalReason} onChange={e => setReversalReason(e.target.value)} helperText="حداقل ۵ کاراکتر؛ در Audit Trail ثبت می‌شود." /></Stack></DialogContent>
      <DialogActions><Button onClick={() => setReverseEntry(null)} disabled={busy}>انصراف</Button><Button variant="contained" color="warning" onClick={reverse} disabled={busy || reversalReason.trim().length < 5}>ثبت Reversal</Button></DialogActions>
    </Dialog>
  </Stack>
}
