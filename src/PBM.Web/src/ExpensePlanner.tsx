import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, CircularProgress, FormControl, InputLabel, MenuItem, Select,
  Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, ToggleButton,
  ToggleButtonGroup, Typography
} from '@mui/material'
import PaymentsRoundedIcon from '@mui/icons-material/PaymentsRounded'
import AddRoundedIcon from '@mui/icons-material/AddRounded'
import { api } from './api'

type Member = { id: string; dimensionId: string; code: string; name: string }
type Dimension = { id: string; code: string; name: string; sequence: number; isRequired: boolean; members: Member[] }
type Setup = { modelId: string; modelName: string; baseCurrencyCode: string; dimensions: Dimension[]; measureId: string }
type Version = { id: string; versionNumber: number; name: string; status: number; isLocked: boolean }
type Plan = { id: string; budgetModelId: string; name: string; versions: Version[] }
type Period = { id: string; sequence: number; code: string; name: string; isClosed: boolean }
type PeriodValue = { periodId: string; periodName: string; sequence: number; value: number; factId?: string | null }
type Data = { periods: Period[]; values: PeriodValue[] }

const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })
const compact = new Intl.NumberFormat('fa-IR', { notation: 'compact', maximumFractionDigits: 1 })

function message(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error)
    return (error as { response?: { data?: { detail?: string } } }).response?.data?.detail ?? fallback
  return fallback
}

export default function ExpensePlanner({ companyId, fiscalYearId, canWrite }: { companyId: string; fiscalYearId: string; canWrite: boolean }) {
  const [setup, setSetup] = useState<Setup | null>(null)
  const [plan, setPlan] = useState<Plan | null>(null)
  const [versionId, setVersionId] = useState('')
  const [selections, setSelections] = useState<Record<string, string>>({})
  const [valueKind, setValueKind] = useState<0 | 3>(0)
  const [data, setData] = useState<Data | null>(null)
  const [newCode, setNewCode] = useState('')
  const [newName, setNewName] = useState('')
  const [busy, setBusy] = useState(false)
  const [saving, setSaving] = useState('')
  const [error, setError] = useState('')

  const versions = useMemo(() => [...(plan?.versions ?? [])].sort((a, b) => b.versionNumber - a.versionNumber), [plan])
  const version = versions.find(x => x.id === versionId) ?? versions[0]
  const editable = canWrite && !!version && version.status === 0 && !version.isLocked
  const selectedDimensions = useMemo(() => !setup ? [] : setup.dimensions.filter(d => !!selections[d.id]).map(d => ({ dimensionId: d.id, memberId: selections[d.id] })), [setup, selections])
  const ready = useMemo(() => !!setup && setup.dimensions.filter(d => d.isRequired).every(d => !!selections[d.id]), [setup, selections])

  const loadSetup = async () => {
    if (!companyId || !fiscalYearId) return
    setBusy(true); setError(''); setData(null)
    try {
      const [s, p] = await Promise.all([
        api.get<Setup>('/expense-planning/setup', { params: { companyId } }),
        api.get<Plan[]>('/budget/plans', { params: { companyId, fiscalYearId } })
      ])
      setSetup(s.data)
      const current = p.data.find(x => x.budgetModelId === s.data.modelId) ?? null
      setPlan(current)
      const latest = [...(current?.versions ?? [])].sort((a, b) => b.versionNumber - a.versionNumber)[0]
      setVersionId(latest?.id ?? '')
      const defaults: Record<string, string> = {}
      s.data.dimensions.forEach(d => {
        if (d.isRequired && d.members.length) {
          const preferred = d.code === 'ACCOUNT' ? d.members.find(m => m.code === 'EXPENSE_BUDGET') : undefined
          defaults[d.id] = preferred?.id ?? d.members[0].id
        }
      })
      setSelections(defaults)
    } catch (e) { setError(message(e, 'دریافت تنظیمات هزینه ناموفق بود.')) }
    finally { setBusy(false) }
  }

  useEffect(() => { void loadSetup() }, [companyId, fiscalYearId])

  const query = async () => {
    if (!version || !ready) { setData(null); return }
    setBusy(true); setError('')
    try {
      const response = await api.post<Data>('/expense-planning/query', { versionId: version.id, dimensions: selectedDimensions, valueKind })
      setData(response.data)
    } catch (e) { setError(message(e, 'بارگذاری هزینه‌های ماهانه ناموفق بود.')) }
    finally { setBusy(false) }
  }
  useEffect(() => { if (version && ready) void query(); else setData(null) }, [version?.id, valueKind, ready, JSON.stringify(selectedDimensions)])

  const createPlan = async () => {
    if (!setup || !canWrite) return
    setBusy(true); setError('')
    try {
      const response = await api.post<Plan>('/budget/plans', { companyId, fiscalYearId, budgetModelId: setup.modelId, name: 'بودجه هزینه‌های مراکز هزینه' })
      setPlan(response.data); setVersionId(response.data.versions[0]?.id ?? '')
    } catch (e) { setError(message(e, 'ایجاد برنامه هزینه ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const save = async (periodId: string, raw: string) => {
    if (!editable || !version || !ready) return
    const value = Number(raw)
    if (!Number.isFinite(value) || value < 0) { setError('مبلغ باید نامنفی باشد؛ ماهیت درآمد/هزینه از طبقه انتخاب‌شده تعیین می‌شود.'); return }
    setSaving(periodId); setError('')
    try {
      await api.post('/expense-planning/cell', { versionId: version.id, periodId, value, dimensions: selectedDimensions, valueKind, note: null })
      await query()
    } catch (e) { setError(message(e, 'ذخیره هزینه ناموفق بود.')) }
    finally { setSaving('') }
  }

  const createItem = async () => {
    if (!canWrite || !newCode.trim() || !newName.trim()) return
    setBusy(true); setError('')
    try {
      await api.post('/expense-planning/items', { companyId, code: newCode.trim().toUpperCase(), name: newName.trim() })
      setNewCode(''); setNewName(''); await loadSetup()
    } catch (e) { setError(message(e, 'ایجاد ردیف هزینه ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const annual = data?.values.reduce((sum, x) => sum + Number(x.value || 0), 0) ?? 0
  const selectedClass = setup?.dimensions.find(d => d.code === 'EXPENSECLASS')?.members.find(m => m.id === selections[setup?.dimensions.find(d => d.code === 'EXPENSECLASS')?.id ?? ''])
  const selectedItem = setup?.dimensions.find(d => d.code === 'EXPENSEITEM')?.members.find(m => m.id === selections[setup?.dimensions.find(d => d.code === 'EXPENSEITEM')?.id ?? ''])

  if (busy && !setup) return <Box py={8} textAlign="center"><CircularProgress /></Box>
  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    <Card elevation={0} sx={{ background: 'linear-gradient(135deg, rgba(124,58,237,.07), rgba(234,88,12,.06))' }}><CardContent>
      <Stack direction="row" spacing={1} alignItems="center"><PaymentsRoundedIcon color="primary" /><Typography variant="h5" fontWeight={900}>بودجه هزینه‌ها، حقوق و مراکز هزینه</Typography></Stack>
      <Typography color="text.secondary" mt={1}>ورود Budget/Forecast ماهانه برای حقوق و مزایا، اداری، بازاریابی، فروش، سایر عملیاتی، مالی و غیرعملیاتی به تفکیک مرکز هزینه و سایر Dimensionها.</Typography>
    </CardContent></Card>

    {!plan && <Alert severity="info" action={canWrite ? <Button color="inherit" onClick={createPlan}>ایجاد برنامه</Button> : undefined}>برای سال مالی انتخاب‌شده برنامه EXPENSE وجود ندارد.</Alert>}

    {setup && <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.25} flexWrap="wrap" useFlexGap>
        <ToggleButtonGroup exclusive size="small" value={valueKind} onChange={(_, v) => v !== null && setValueKind(v)}><ToggleButton value={0}>بودجه</ToggleButton><ToggleButton value={3}>Forecast</ToggleButton></ToggleButtonGroup>
        {setup.dimensions.map(d => <FormControl size="small" key={d.id} sx={{ minWidth: 205 }}><InputLabel>{d.name}{d.isRequired ? ' *' : ''}</InputLabel><Select value={selections[d.id] ?? ''} label={`${d.name}${d.isRequired ? ' *' : ''}`} onChange={e => setSelections(x => ({ ...x, [d.id]: e.target.value }))}>
          {!d.isRequired && <MenuItem value=""><em>بدون تفکیک</em></MenuItem>}{d.members.map(m => <MenuItem key={m.id} value={m.id}>{m.name} — {m.code}</MenuItem>)}
        </Select></FormControl>)}
        {plan && <FormControl size="small" sx={{ minWidth: 220 }}><InputLabel>نسخه</InputLabel><Select value={version?.id ?? ''} label="نسخه" onChange={e => setVersionId(e.target.value)}>{versions.map(v => <MenuItem key={v.id} value={v.id}>{v.name} — {v.versionNumber.toLocaleString('fa-IR')}</MenuItem>)}</Select></FormControl>}
      </Stack>
      {!ready && <Alert severity="warning" sx={{ mt: 2 }}>واحد/حساب و طبقه و ردیف هزینه را انتخاب کنید.</Alert>}
      {version && !editable && <Alert severity="warning" sx={{ mt: 2 }}>نسخه انتخابی قابل ویرایش نیست.</Alert>}
      {canWrite && <Stack direction={{ xs: 'column', md: 'row' }} spacing={1} mt={2}><TextField size="small" label="کد ردیف جدید" value={newCode} onChange={e => setNewCode(e.target.value.toUpperCase())} /><TextField size="small" label="نام ردیف هزینه/درآمد" value={newName} onChange={e => setNewName(e.target.value)} sx={{ minWidth: 260 }} /><Button variant="outlined" startIcon={<AddRoundedIcon />} onClick={createItem}>افزودن ردیف</Button></Stack>}
    </CardContent></Card>}

    {data && <>
      <Card elevation={0}><CardContent><Typography color="text.secondary">{selectedClass?.name ?? 'طبقه'} / {selectedItem?.name ?? 'ردیف'}</Typography><Typography variant="h4" fontWeight={900} mt={1}>{compact.format(annual)} <Typography component="span" variant="caption">{setup?.baseCurrencyCode}</Typography></Typography><Typography variant="caption" color="text.secondary">جمع سال مالی</Typography></CardContent></Card>
      <Card elevation={0}><CardContent sx={{ p: 0 }}><Box p={2.5}><Typography variant="h6" fontWeight={900}>جزئیات ماهانه</Typography></Box><TableContainer><Table size="small"><TableHead><TableRow><TableCell>ماه</TableCell><TableCell>مبلغ</TableCell><TableCell>سهم از سال</TableCell></TableRow></TableHead><TableBody>{data.periods.map(p => {
        const current = data.values.find(x => x.periodId === p.id)?.value ?? 0
        return <TableRow key={p.id} hover><TableCell><Typography fontWeight={800}>{p.name}</Typography></TableCell><TableCell sx={{ width: 300 }}><TextField size="small" type="number" value={current} disabled={!editable || p.isClosed || saving === p.id} onChange={e => setData(x => x ? { ...x, values: x.values.map(v => v.periodId === p.id ? { ...v, value: Number(e.target.value) } : v) } : x)} onBlur={e => void save(p.id, e.target.value)} inputProps={{ min: 0, step: 'any' }} /></TableCell><TableCell>{annual === 0 ? '۰٪' : `${number.format(Number(current) / annual * 100)}٪`}</TableCell></TableRow>
      })}</TableBody></Table></TableContainer></CardContent></Card>
    </>}
  </Stack>
}
