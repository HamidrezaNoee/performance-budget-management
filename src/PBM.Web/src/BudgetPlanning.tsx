import { useEffect, useMemo, useState } from 'react'
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, FormControl, InputLabel, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography } from '@mui/material'
import { api } from './api'

type Model = { id: string; code: string; name: string; description?: string }
type Version = { id: string; scenarioId: string; versionNumber: number; name: string; status: number; isLocked: boolean }
type Plan = { id: string; budgetModelId: string; name: string; status: number; versions: Version[] }
type Dimension = { id: string; code: string; name: string; sequence: number; isRequired: boolean }
type Member = { id: string; dimensionId: string; code: string; name: string }
type Measure = { id: string; code: string; name: string; unit?: string; valueType: number; aggregation: number; isCalculated: boolean; formulaExpression?: string }
type Period = { id: string; sequence: number; name: string; isClosed?: boolean }
type GridCell = { periodId: string; factId?: string; value: number }
type GridRow = { memberId: string; code: string; name: string; cells: GridCell[] }
type Grid = { periods: Period[]; measure: Measure; rowDimension: Dimension; rows: GridRow[] }
type VersionDetails = Version & { budgetPlanId: string }
type BulkResult = { created: number; updated: number; skipped: number; recalculatedCoordinates: number; warnings: string[] }
type CompareCell = { periodId: string; leftValue: number; rightValue: number; variance: number; variancePercent?: number | null }
type CompareRow = { memberId: string; code: string; name: string; cells: CompareCell[] }
type Comparison = { leftVersionId: string; rightVersionId: string; periods: Period[]; measure: Measure; rowDimension: Dimension; rows: CompareRow[] }

const statusLabels = ['پیش‌نویس', 'ارسال‌شده', 'در حال بررسی', 'برگشت‌شده', 'تأییدشده', 'ردشده', 'اصلاح‌شده', 'بسته‌شده']
const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string } } }).response
    if (response?.data?.detail) return response.data.detail
  }
  return fallback
}

export default function BudgetPlanning({ companyId, fiscalYearId }: { companyId: string; fiscalYearId: string }) {
  const [models, setModels] = useState<Model[]>([])
  const [plans, setPlans] = useState<Plan[]>([])
  const [modelId, setModelId] = useState('')
  const [planId, setPlanId] = useState('')
  const [versionId, setVersionId] = useState('')
  const [dimensions, setDimensions] = useState<Dimension[]>([])
  const [members, setMembers] = useState<Record<string, Member[]>>({})
  const [measures, setMeasures] = useState<Measure[]>([])
  const [rowDimensionId, setRowDimensionId] = useState('')
  const [measureId, setMeasureId] = useState('')
  const [filters, setFilters] = useState<Record<string, string>>({})
  const [valueKind, setValueKind] = useState(0)
  const [grid, setGrid] = useState<Grid | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [compareVersionId, setCompareVersionId] = useState('')
  const [comparison, setComparison] = useState<Comparison | null>(null)

  const selectedPlan = plans.find(x => x.id === planId)
  const sortedVersions = useMemo(() => [...(selectedPlan?.versions ?? [])].sort((a, b) => b.versionNumber - a.versionNumber), [selectedPlan])
  const version = sortedVersions.find(x => x.id === versionId) ?? sortedVersions[0]
  const editable = !!version && version.status === 0 && !version.isLocked
  const selectedMeasure = measures.find(x => x.id === measureId)

  const fixedFilters = () => Object.entries(filters)
    .filter(([, memberId]) => memberId)
    .map(([dimensionId, memberId]) => ({ dimensionId, memberId }))

  const showResult = (title: string, result: BulkResult) => {
    const detail = `${title}: ${result.created.toLocaleString('fa-IR')} ایجاد، ${result.updated.toLocaleString('fa-IR')} به‌روزرسانی، ${result.skipped.toLocaleString('fa-IR')} رد شد.`
    setMessage(result.warnings.length ? `${detail} هشدار: ${result.warnings.slice(0, 3).join(' | ')}` : detail)
  }

  const refreshPlans = async (preferredVersionId?: string) => {
    const { data } = await api.get<Plan[]>('/budget/plans', { params: { companyId, fiscalYearId } })
    setPlans(data)
    const currentPlan = data.find(x => x.id === planId) ?? data.find(x => x.budgetModelId === modelId) ?? data[0]
    if (currentPlan) {
      setPlanId(currentPlan.id)
      const versions = [...currentPlan.versions].sort((a, b) => b.versionNumber - a.versionNumber)
      setVersionId(preferredVersionId && versions.some(x => x.id === preferredVersionId) ? preferredVersionId : versions[0]?.id ?? '')
    }
  }

  useEffect(() => {
    if (!companyId || !fiscalYearId) return
    setBusy(true); setError(''); setMessage(''); setGrid(null); setComparison(null)
    Promise.all([
      api.get<Model[]>('/reference/models', { params: { companyId } }),
      api.get<Plan[]>('/budget/plans', { params: { companyId, fiscalYearId } })
    ]).then(([modelResponse, planResponse]) => {
      setModels(modelResponse.data); setPlans(planResponse.data)
      const firstModel = planResponse.data[0]?.budgetModelId ?? modelResponse.data[0]?.id ?? ''
      setModelId(firstModel)
      const firstPlan = planResponse.data.find(x => x.budgetModelId === firstModel) ?? planResponse.data[0]
      setPlanId(firstPlan?.id ?? '')
      const latest = [...(firstPlan?.versions ?? [])].sort((a, b) => b.versionNumber - a.versionNumber)[0]
      setVersionId(latest?.id ?? '')
    }).catch(error => setError(apiError(error, 'دریافت اطلاعات برنامه بودجه ناموفق بود.'))).finally(() => setBusy(false))
  }, [companyId, fiscalYearId])

  useEffect(() => {
    if (!modelId) return
    setGrid(null); setComparison(null)
    Promise.all([
      api.get<Dimension[]>('/reference/dimensions', { params: { modelId } }),
      api.get<Measure[]>('/reference/measures', { params: { modelId } })
    ]).then(async ([dimensionResponse, measureResponse]) => {
      const dims = dimensionResponse.data
      setDimensions(dims); setMeasures(measureResponse.data)
      const firstRow = dims[0]?.id ?? ''; setRowDimensionId(firstRow)
      setMeasureId(measureResponse.data.find(x => !x.isCalculated)?.id ?? measureResponse.data[0]?.id ?? '')
      const memberEntries = await Promise.all(dims.map(async d => [d.id, (await api.get<Member[]>('/reference/dimension-members', { params: { dimensionId: d.id, companyId } })).data] as const))
      const map = Object.fromEntries(memberEntries); setMembers(map)
      const defaults: Record<string, string> = {}
      dims.slice(1).forEach(d => { if (map[d.id]?.length) defaults[d.id] = map[d.id][0].id })
      setFilters(defaults)
      const matchingPlan = plans.find(x => x.budgetModelId === modelId)
      if (matchingPlan) {
        setPlanId(matchingPlan.id)
        const latest = [...matchingPlan.versions].sort((a, b) => b.versionNumber - a.versionNumber)[0]
        setVersionId(latest?.id ?? '')
      }
    }).catch(error => setError(apiError(error, 'دریافت ابعاد و مژرهای مدل ناموفق بود.')))
  }, [modelId, companyId, plans.length])

  useEffect(() => {
    if (!selectedPlan) return
    const latest = [...selectedPlan.versions].sort((a, b) => b.versionNumber - a.versionNumber)[0]
    if (!selectedPlan.versions.some(x => x.id === versionId)) setVersionId(latest?.id ?? '')
  }, [planId, selectedPlan?.versions.length])

  useEffect(() => {
    const candidate = sortedVersions.find(x => x.id !== version?.id)
    setCompareVersionId(candidate?.id ?? '')
    setComparison(null)
  }, [version?.id, planId, sortedVersions.length])

  const filterDimensions = useMemo(() => dimensions.filter(x => x.id !== rowDimensionId), [dimensions, rowDimensionId])

  useEffect(() => {
    const next = { ...filters }
    filterDimensions.forEach(d => { if (!next[d.id] && members[d.id]?.length) next[d.id] = members[d.id][0].id })
    Object.keys(next).forEach(id => { if (!filterDimensions.some(d => d.id === id)) delete next[id] })
    setFilters(next)
  }, [rowDimensionId, dimensions, members])

  const loadGrid = async () => {
    if (!version || !rowDimensionId || !measureId) return
    setBusy(true); setError('')
    try {
      const { data } = await api.post<Grid>('/budget/grid/query', {
        versionId: version.id, rowDimensionId, measureId, valueKind, filters: fixedFilters()
      })
      setGrid(data)
    } catch (error) { setError(apiError(error, 'بارگذاری جدول بودجه ناموفق بود.')) }
    finally { setBusy(false) }
  }

  useEffect(() => { if (version && rowDimensionId && measureId) loadGrid() }, [version?.id, rowDimensionId, measureId, valueKind, JSON.stringify(filters)])

  const createPlan = async () => {
    if (!modelId) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<Plan>('/budget/plans', { companyId, fiscalYearId, budgetModelId: modelId, name: `برنامه بودجه ${models.find(x => x.id === modelId)?.name ?? ''}` })
      setPlans(x => [...x, data]); setPlanId(data.id); setVersionId(data.versions[0]?.id ?? '')
      setMessage('برنامه بودجه ایجاد شد.')
    } catch (error) { setError(apiError(error, 'ایجاد برنامه بودجه ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const saveCell = async (row: GridRow, cell: GridCell, value: number) => {
    if (!version || !grid || !editable || grid.measure.isCalculated) return
    const dimensionsForFact = [{ dimensionId: rowDimensionId, memberId: row.memberId }, ...fixedFilters()]
    try {
      const { data } = await api.post<{ id: string }>('/budget/facts', {
        versionId: version.id, periodId: cell.periodId, measureId, valueKind, value,
        currencyCode: grid.measure.valueType === 0 ? 'IRR' : null,
        dimensions: dimensionsForFact, source: 'PlanningGrid', note: null
      })
      setGrid(current => current ? { ...current, rows: current.rows.map(r => r.memberId !== row.memberId ? r : { ...r, cells: r.cells.map(c => c.periodId !== cell.periodId ? c : { ...c, factId: data.id, value }) }) } : current)
    } catch (error) { setError(apiError(error, 'ذخیره مقدار ناموفق بود.')) }
  }

  const copyPriorYearActual = async () => {
    if (!version || !editable) return
    const raw = window.prompt('درصد رشد/کاهش نسبت به عملکرد سال قبل را وارد کنید. مثال: 10 یا -5', '0')
    if (raw === null) return
    const growthPercent = Number(raw.replace('٪', '').trim())
    if (!Number.isFinite(growthPercent)) { setError('درصد رشد معتبر نیست.'); return }
    const replaceExisting = window.confirm('آیا مقادیر بودجه موجود نیز بازنویسی شوند؟\nOK = بازنویسی، Cancel = فقط خانه‌های خالی')
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<BulkResult>('/budget/operations/copy-prior-year-actual', { targetVersionId: version.id, growthPercent, replaceExisting })
      showResult('خط پایه سال قبل اعمال شد', data); await loadGrid()
    } catch (error) { setError(apiError(error, 'کپی عملکرد سال قبل ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const recalculate = async () => {
    if (!version || !editable) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<{ coordinatesProcessed: number; factsCreated: number; factsUpdated: number; formulasSkipped: number; errors: string[] }>(`/calculations/versions/${version.id}/recalculate`)
      setMessage(`محاسبات انجام شد؛ ${data.coordinatesProcessed.toLocaleString('fa-IR')} مختصات پردازش و ${(data.factsCreated + data.factsUpdated).toLocaleString('fa-IR')} مقدار محاسباتی ثبت/به‌روزرسانی شد.${data.errors.length ? ` هشدار: ${data.errors.slice(0, 3).join(' | ')}` : ''}`)
      await loadGrid()
    } catch (error) { setError(apiError(error, 'محاسبه مجدد فرمول‌ها ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const spreadRow = async (row: GridRow) => {
    if (!version || !grid || !editable || grid.measure.isCalculated) return
    const currentTotal = row.cells.reduce((sum, cell) => sum + Number(cell.value || 0), 0)
    const raw = window.prompt(`مبلغ/مقدار کل برای «${row.name}» را وارد کنید. مقدار بین دوره‌های باز به‌طور مساوی توزیع می‌شود.`, String(currentTotal))
    if (raw === null) return
    const total = Number(raw.replace(/,/g, '').trim())
    if (!Number.isFinite(total)) { setError('مقدار کل معتبر نیست.'); return }
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<BulkResult>('/budget/operations/spread', {
        versionId: version.id, measureId, valueKind, rowDimensionId, rowMemberId: row.memberId,
        filters: fixedFilters(), total, method: 0, weights: null,
        currencyCode: grid.measure.valueType === 0 ? 'IRR' : null, note: 'توزیع مساوی از جدول برنامه‌ریزی'
      })
      showResult(`توزیع «${row.name}» انجام شد`, data); await loadGrid()
    } catch (error) { setError(apiError(error, 'توزیع مقدار روی دوره‌ها ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const bulkPaste = async () => {
    if (!version || !grid || !editable || grid.measure.isCalculated) return
    const sample = `${grid.rows[0]?.code ?? 'ROW-CODE'}\t${grid.periods.map(() => '0').join('\t')}`
    const raw = window.prompt(`داده چندردیفی را با Tab وارد کنید. ستون اول کد ردیف و سپس ${grid.periods.length.toLocaleString('fa-IR')} مقدار دوره‌هاست.\nنمونه:\n${sample}`)
    if (!raw?.trim()) return
    try {
      const rows = raw.split(/\r?\n/).filter(Boolean).map(line => {
        const parts = line.split('\t')
        const code = parts.shift()?.trim() ?? ''
        const row = grid.rows.find(x => x.code.toLowerCase() === code.toLowerCase())
        if (!row) throw new Error(`کد ردیف «${code}» پیدا نشد.`)
        if (parts.length > grid.periods.length) throw new Error(`تعداد ستون‌های ردیف «${code}» بیشتر از دوره‌هاست.`)
        const cells = parts.map((value, index) => {
          const numeric = Number(value.replace(/,/g, '').trim())
          if (!Number.isFinite(numeric)) throw new Error(`مقدار «${value}» در ردیف «${code}» عدد نیست.`)
          return { periodId: grid.periods[index].id, value: numeric }
        })
        return { rowMemberId: row.memberId, cells }
      })
      setBusy(true); setError(''); setMessage('')
      const { data } = await api.post<BulkResult>('/budget/operations/bulk-paste', {
        versionId: version.id, measureId, valueKind, rowDimensionId, filters: fixedFilters(), rows,
        currencyCode: grid.measure.valueType === 0 ? 'IRR' : null, note: 'Bulk paste from planning grid'
      })
      showResult('چسباندن گروهی انجام شد', data); await loadGrid()
    } catch (error) {
      setError(error instanceof Error ? error.message : apiError(error, 'ورود گروهی ناموفق بود.'))
    } finally { setBusy(false) }
  }

  const compareVersions = async () => {
    if (!version || !compareVersionId || !measureId || !rowDimensionId) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<Comparison>('/budget/operations/compare', {
        leftVersionId: compareVersionId, rightVersionId: version.id, measureId, valueKind, rowDimensionId, filters: fixedFilters()
      })
      setComparison(data)
    } catch (error) { setError(apiError(error, 'مقایسه نسخه‌ها ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const changeStatus = async (status: number) => {
    if (!version) return
    const comment = status === 3 || status === 5 ? window.prompt('دلیل برگشت/رد را وارد کنید:') : window.prompt('توضیح اختیاری:')
    if ((status === 3 || status === 5) && !comment?.trim()) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<VersionDetails>(`/budget/versions/${version.id}/status`, { status, comment: comment?.trim() || null })
      await refreshPlans(data.id); setMessage('وضعیت نسخه بودجه تغییر کرد.')
    } catch (error) { setError(apiError(error, 'تغییر وضعیت نسخه بودجه ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const createRevision = async () => {
    if (!version) return
    const name = window.prompt('نام نسخه اصلاحی جدید:', `اصلاحیه ${version.versionNumber + 1}`)
    if (!name?.trim()) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<VersionDetails>('/budget/versions/revision', { sourceVersionId: version.id, name: name.trim(), scenarioId: null })
      await refreshPlans(data.id); setMessage('نسخه اصلاحی ایجاد شد.')
    } catch (error) { setError(apiError(error, 'ایجاد نسخه اصلاحی ناموفق بود.')) }
    finally { setBusy(false) }
  }

  if (busy && !models.length) return <Box py={8} textAlign="center"><CircularProgress /></Box>

  return <Stack spacing={2.5}>
    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.5} alignItems={{ lg: 'center' }}>
        <FormControl size="small" sx={{ minWidth: 220 }}><InputLabel>مدل بودجه</InputLabel><Select value={modelId} label="مدل بودجه" onChange={e => setModelId(e.target.value)}>{models.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 220 }}><InputLabel>برنامه</InputLabel><Select value={planId} label="برنامه" onChange={e => setPlanId(e.target.value)}>{plans.filter(x => x.budgetModelId === modelId).map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl>
        {!plans.some(x => x.budgetModelId === modelId) && <Button variant="contained" onClick={createPlan}>ایجاد برنامه</Button>}
        <FormControl size="small" sx={{ minWidth: 190 }}><InputLabel>نسخه</InputLabel><Select value={version?.id ?? ''} label="نسخه" onChange={e => setVersionId(e.target.value)}>{sortedVersions.map(x => <MenuItem key={x.id} value={x.id}>نسخه {x.versionNumber} — {x.name}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 180 }}><InputLabel>ردیف‌ها</InputLabel><Select value={rowDimensionId} label="ردیف‌ها" onChange={e => setRowDimensionId(e.target.value)}>{dimensions.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 220 }}><InputLabel>مژر</InputLabel><Select value={measureId} label="مژر" onChange={e => setMeasureId(e.target.value)}>{measures.map(x => <MenuItem key={x.id} value={x.id}>{x.name}{x.isCalculated ? ' (محاسباتی)' : ''}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 150 }}><InputLabel>نوع مقدار</InputLabel><Select value={valueKind} label="نوع مقدار" onChange={e => setValueKind(Number(e.target.value))}><MenuItem value={0}>بودجه</MenuItem><MenuItem value={1}>عملکرد</MenuItem><MenuItem value={2}>تعهد</MenuItem><MenuItem value={3}>پیش‌بینی</MenuItem></Select></FormControl>
      </Stack>

      {version && <Stack direction="row" spacing={1} mt={2} alignItems="center" flexWrap="wrap" useFlexGap>
        <Chip label={statusLabels[version.status] ?? 'نامشخص'} color={version.status === 4 ? 'success' : version.status === 5 ? 'error' : version.status === 3 ? 'warning' : 'default'} />
        {version.status === 0 && <Button size="small" variant="contained" onClick={() => changeStatus(1)}>ارسال برای بررسی</Button>}
        {version.status === 1 && <><Button size="small" onClick={() => changeStatus(2)}>شروع بررسی</Button><Button size="small" color="warning" onClick={() => changeStatus(3)}>برگشت</Button><Button size="small" color="error" onClick={() => changeStatus(5)}>رد</Button></>}
        {version.status === 2 && <><Button size="small" color="success" variant="contained" onClick={() => changeStatus(4)}>تأیید</Button><Button size="small" color="warning" onClick={() => changeStatus(3)}>برگشت</Button><Button size="small" color="error" onClick={() => changeStatus(5)}>رد</Button></>}
        {version.status === 3 && <Button size="small" onClick={() => changeStatus(0)}>بازگشت به پیش‌نویس</Button>}
        {version.status === 4 && <><Button size="small" variant="outlined" onClick={createRevision}>ایجاد اصلاحیه</Button><Button size="small" onClick={() => changeStatus(7)}>بستن نسخه</Button></>}
        {version.status === 5 && <Button size="small" variant="outlined" onClick={createRevision}>ایجاد نسخه جدید از ردشده</Button>}
        {editable && <Button size="small" variant="outlined" onClick={copyPriorYearActual}>خط پایه از عملکرد سال قبل</Button>}
        {editable && <Button size="small" variant="outlined" onClick={recalculate}>محاسبه مجدد فرمول‌ها</Button>}
        {editable && grid && !grid.measure.isCalculated && <Button size="small" variant="outlined" onClick={bulkPaste}>ورود گروهی</Button>}
      </Stack>}

      {filterDimensions.length > 0 && <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} mt={2}>{filterDimensions.map(d => <FormControl size="small" sx={{ minWidth: 220 }} key={d.id}><InputLabel>{d.name}</InputLabel><Select value={filters[d.id] ?? ''} label={d.name} onChange={e => setFilters(x => ({ ...x, [d.id]: e.target.value }))}>{(members[d.id] ?? []).map(m => <MenuItem value={m.id} key={m.id}>{m.name}</MenuItem>)}</Select></FormControl>)}</Stack>}

      {sortedVersions.length > 1 && version && <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} mt={2} alignItems={{ md: 'center' }}>
        <FormControl size="small" sx={{ minWidth: 260 }}><InputLabel>مقایسه با نسخه</InputLabel><Select value={compareVersionId} label="مقایسه با نسخه" onChange={e => setCompareVersionId(e.target.value)}>{sortedVersions.filter(x => x.id !== version.id).map(x => <MenuItem key={x.id} value={x.id}>نسخه {x.versionNumber} — {x.name}</MenuItem>)}</Select></FormControl>
        <Button variant="outlined" onClick={compareVersions} disabled={!compareVersionId || busy}>مقایسه نسخه‌ها</Button>
        {comparison && <Button onClick={() => setComparison(null)}>بستن مقایسه</Button>}
      </Stack>}
    </CardContent></Card>

    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    {message && <Alert severity="success" onClose={() => setMessage('')}>{message}</Alert>}
    {version && !editable && <Alert severity="warning">این نسخه برای ورود مستقیم داده قابل ویرایش نیست. فقط نسخه پیش‌نویسِ باز امکان تغییر دارد؛ در صورت نیاز اصلاحیه جدید بسازید.</Alert>}
    {selectedMeasure?.isCalculated && <Alert severity="info">این مژر محاسباتی است و مقادیر آن از فرمول وابستگی‌ها تولید می‌شود؛ ویرایش مستقیم غیرفعال است.</Alert>}

    {grid && <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <Box p={2.5}><Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={1}><Box><Typography variant="h6" fontWeight={900}>جدول برنامه‌ریزی — {grid.measure.name}</Typography><Typography variant="body2" color="text.secondary">ردیف: {grid.rowDimension.name} | واحد: {grid.measure.unit ?? '-'} | نسخه {version?.versionNumber}</Typography></Box>{busy && <CircularProgress size={24} />}</Stack></Box>
      <TableContainer sx={{ maxHeight: '65vh' }}><Table stickyHeader size="small" className="planning-table"><TableHead><TableRow><TableCell sx={{ minWidth: 270, right: 0, zIndex: 4 }}>کد / شرح</TableCell>{grid.periods.map(p => <TableCell align="center" key={p.id} sx={{ minWidth: 130 }}>{p.name}</TableCell>)}</TableRow></TableHead><TableBody>{grid.rows.map(row => <TableRow hover key={row.memberId}><TableCell sx={{ position: 'sticky', right: 0, bgcolor: '#fff', zIndex: 2 }}><Stack direction="row" justifyContent="space-between" alignItems="center" spacing={1}><Box><Typography fontWeight={800} variant="body2">{row.name}</Typography><Typography variant="caption" color="text.secondary">{row.code}</Typography></Box>{editable && !grid.measure.isCalculated && <Button size="small" onClick={() => spreadRow(row)}>توزیع</Button>}</Stack></TableCell>{row.cells.map(cell => <TableCell key={cell.periodId} sx={{ p: .5 }}><TextField size="small" type="number" defaultValue={cell.value} disabled={!editable || grid.measure.isCalculated} inputProps={{ style: { textAlign: 'center', minWidth: 90 } }} onBlur={e => { const value = Number(e.target.value); if (Number.isFinite(value) && value !== cell.value) saveCell(row, cell, value) }} /></TableCell>)}</TableRow>)}</TableBody></Table></TableContainer>
    </CardContent></Card>}

    {comparison && <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <Box p={2.5}><Typography variant="h6" fontWeight={900}>مقایسه نسخه‌ها — {comparison.measure.name}</Typography><Typography color="text.secondary" variant="body2">مقدار نسخه فعلی در برابر نسخه مبنا؛ انحراف = نسخه فعلی منهای نسخه مبنا.</Typography></Box>
      <TableContainer sx={{ maxHeight: '65vh' }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell sx={{ minWidth: 220 }}>ردیف</TableCell>{comparison.periods.map(period => <TableCell key={period.id} align="center" sx={{ minWidth: 185 }}>{period.name}</TableCell>)}</TableRow></TableHead><TableBody>{comparison.rows.map(row => <TableRow key={row.memberId} hover><TableCell><Typography fontWeight={800}>{row.name}</Typography><Typography variant="caption" color="text.secondary">{row.code}</Typography></TableCell>{row.cells.map(cell => <TableCell key={cell.periodId} align="center"><Typography variant="body2" fontWeight={800}>{number.format(cell.rightValue)}</Typography><Typography variant="caption" color={cell.variance > 0 ? 'error.main' : cell.variance < 0 ? 'success.main' : 'text.secondary'}>{cell.variance >= 0 ? '+' : ''}{number.format(cell.variance)}{cell.variancePercent == null ? '' : ` (${cell.variancePercent >= 0 ? '+' : ''}${number.format(cell.variancePercent)}٪)`}</Typography><Typography variant="caption" display="block" color="text.secondary">مبنا: {number.format(cell.leftValue)}</Typography></TableCell>)}</TableRow>)}</TableBody></Table></TableContainer>
    </CardContent></Card>}
  </Stack>
}
