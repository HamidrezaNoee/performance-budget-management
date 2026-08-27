import { useEffect, useMemo, useState } from 'react'
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, FormControl, InputLabel, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography } from '@mui/material'
import { api } from './api'

type Model = { id: string; code: string; name: string; description?: string }
type Version = { id: string; scenarioId: string; versionNumber: number; name: string; status: number; isLocked: boolean }
type Plan = { id: string; budgetModelId: string; name: string; status: number; versions: Version[] }
type Dimension = { id: string; code: string; name: string; sequence: number; isRequired: boolean }
type Member = { id: string; dimensionId: string; code: string; name: string }
type Measure = { id: string; code: string; name: string; unit?: string; isCalculated: boolean; formulaExpression?: string }
type Period = { id: string; sequence: number; name: string }
type GridCell = { periodId: string; factId?: string; value: number }
type GridRow = { memberId: string; code: string; name: string; cells: GridCell[] }
type Grid = { periods: Period[]; measure: Measure; rowDimension: Dimension; rows: GridRow[] }

type VersionDetails = Version & { budgetPlanId: string }

const statusLabels = ['پیش‌نویس', 'ارسال‌شده', 'در حال بررسی', 'برگشت‌شده', 'تأییدشده', 'ردشده', 'اصلاح‌شده', 'بسته‌شده']

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

  const selectedPlan = plans.find(x => x.id === planId)
  const sortedVersions = useMemo(() => [...(selectedPlan?.versions ?? [])].sort((a, b) => b.versionNumber - a.versionNumber), [selectedPlan])
  const version = sortedVersions.find(x => x.id === versionId) ?? sortedVersions[0]

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
    setBusy(true); setError(''); setGrid(null)
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
    }).catch(() => setError('دریافت اطلاعات برنامه بودجه ناموفق بود.')).finally(() => setBusy(false))
  }, [companyId, fiscalYearId])

  useEffect(() => {
    if (!modelId) return
    setGrid(null)
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
    }).catch(() => setError('دریافت ابعاد و مژرهای مدل ناموفق بود.'))
  }, [modelId, companyId, plans.length])

  useEffect(() => {
    if (!selectedPlan) return
    const latest = [...selectedPlan.versions].sort((a, b) => b.versionNumber - a.versionNumber)[0]
    if (!selectedPlan.versions.some(x => x.id === versionId)) setVersionId(latest?.id ?? '')
  }, [planId, selectedPlan?.versions.length])

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
        versionId: version.id, rowDimensionId, measureId, valueKind,
        filters: Object.entries(filters).filter(([, memberId]) => memberId).map(([dimensionId, memberId]) => ({ dimensionId, memberId }))
      })
      setGrid(data)
    } catch { setError('بارگذاری جدول بودجه ناموفق بود.') }
    finally { setBusy(false) }
  }

  useEffect(() => { if (version && rowDimensionId && measureId) loadGrid() }, [version?.id, rowDimensionId, measureId, valueKind, JSON.stringify(filters)])

  const createPlan = async () => {
    if (!modelId) return
    setBusy(true)
    try {
      const { data } = await api.post<Plan>('/budget/plans', { companyId, fiscalYearId, budgetModelId: modelId, name: `برنامه بودجه ${models.find(x => x.id === modelId)?.name ?? ''}` })
      setPlans(x => [...x, data]); setPlanId(data.id); setVersionId(data.versions[0]?.id ?? '')
    } catch { setError('ایجاد برنامه بودجه ناموفق بود.') }
    finally { setBusy(false) }
  }

  const saveCell = async (row: GridRow, cell: GridCell, value: number) => {
    if (!version || !grid) return
    const dimensionsForFact = [{ dimensionId: rowDimensionId, memberId: row.memberId }, ...Object.entries(filters).map(([dimensionId, memberId]) => ({ dimensionId, memberId }))]
    try {
      const { data } = await api.post<{ id: string }>('/budget/facts', { versionId: version.id, periodId: cell.periodId, measureId, valueKind, value, currencyCode: 'IRR', dimensions: dimensionsForFact, source: 'PlanningGrid', note: null })
      setGrid(current => current ? { ...current, rows: current.rows.map(r => r.memberId !== row.memberId ? r : { ...r, cells: r.cells.map(c => c.periodId !== cell.periodId ? c : { ...c, factId: data.id, value }) }) } : current)
    } catch { setError('ذخیره مقدار ناموفق بود.') }
  }

  const changeStatus = async (status: number) => {
    if (!version) return
    const comment = status === 3 || status === 5 ? window.prompt('دلیل برگشت/رد را وارد کنید:') : window.prompt('توضیح اختیاری:')
    if ((status === 3 || status === 5) && !comment?.trim()) return
    setBusy(true); setError('')
    try {
      const { data } = await api.post<VersionDetails>(`/budget/versions/${version.id}/status`, { status, comment: comment?.trim() || null })
      await refreshPlans(data.id)
    } catch { setError('تغییر وضعیت نسخه بودجه ناموفق بود.') }
    finally { setBusy(false) }
  }

  const createRevision = async () => {
    if (!version) return
    const name = window.prompt('نام نسخه اصلاحی جدید:', `اصلاحیه ${version.versionNumber + 1}`)
    if (!name?.trim()) return
    setBusy(true); setError('')
    try {
      const { data } = await api.post<VersionDetails>('/budget/versions/revision', { sourceVersionId: version.id, name: name.trim(), scenarioId: null })
      await refreshPlans(data.id)
    } catch { setError('ایجاد نسخه اصلاحی ناموفق بود.') }
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
      </Stack>}
      {filterDimensions.length > 0 && <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} mt={2}>{filterDimensions.map(d => <FormControl size="small" sx={{ minWidth: 220 }} key={d.id}><InputLabel>{d.name}</InputLabel><Select value={filters[d.id] ?? ''} label={d.name} onChange={e => setFilters(x => ({ ...x, [d.id]: e.target.value }))}>{(members[d.id] ?? []).map(m => <MenuItem value={m.id} key={m.id}>{m.name}</MenuItem>)}</Select></FormControl>)}</Stack>}
    </CardContent></Card>
    {error && <Alert severity="error">{error}</Alert>}
    {version?.isLocked && <Alert severity="warning">این نسخه قفل شده و قابل ویرایش نیست. برای تغییر بودجه، نسخه را برگشت دهید یا اصلاحیه جدید بسازید.</Alert>}
    {grid && <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <Box p={2.5}><Typography variant="h6" fontWeight={900}>جدول برنامه‌ریزی — {grid.measure.name}</Typography><Typography variant="body2" color="text.secondary">ردیف: {grid.rowDimension.name} | واحد: {grid.measure.unit ?? '-'} | نسخه {version?.versionNumber}</Typography></Box>
      <TableContainer sx={{ maxHeight: '65vh' }}><Table stickyHeader size="small" className="planning-table"><TableHead><TableRow><TableCell sx={{ minWidth: 220, right: 0, zIndex: 4 }}>کد / شرح</TableCell>{grid.periods.map(p => <TableCell align="center" key={p.id} sx={{ minWidth: 130 }}>{p.name}</TableCell>)}</TableRow></TableHead><TableBody>{grid.rows.map(row => <TableRow hover key={row.memberId}><TableCell sx={{ position: 'sticky', right: 0, bgcolor: '#fff', zIndex: 2 }}><Typography fontWeight={800} variant="body2">{row.name}</Typography><Typography variant="caption" color="text.secondary">{row.code}</Typography></TableCell>{row.cells.map(cell => <TableCell key={cell.periodId} sx={{ p: .5 }}><TextField size="small" type="number" defaultValue={cell.value} disabled={version?.isLocked || grid.measure.isCalculated} inputProps={{ style: { textAlign: 'center', minWidth: 90 } }} onBlur={e => { const value = Number(e.target.value); if (Number.isFinite(value) && value !== cell.value) saveCell(row, cell, value) }} /></TableCell>)}</TableRow>)}</TableBody></Table></TableContainer>
    </CardContent></Card>}
  </Stack>
}
