import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Chip, Divider, FormControl, InputLabel, LinearProgress,
  MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow,
  TextField, Typography
} from '@mui/material'
import { api } from './api'

type OwnerUnit = { id: string; parentId?: string | null; code: string; name: string; unitType: string }
type Currency = { id: string; code: string; name: string; symbol?: string; isBaseCurrency: boolean }
type Milestone = {
  id: string; projectId: string; code: string; name: string; dueDate: string; weight: number;
  progressPercent: number; isCompleted: boolean; completedAtUtc?: string | null; note?: string | null
}
type Project = {
  id: string; companyId: string; projectDimensionMemberId: string; code: string; name: string;
  description?: string | null; status: number; priority: number; startDate: string; endDate: string;
  requestedBudget?: number | null; approvedBudgetLimit?: number | null; currencyCode: string;
  ownerOrganizationUnitId?: string | null; ownerOrganizationUnitName?: string | null;
  requestedByUserId: string; requestedByDisplayName: string; approvedByUserId?: string | null;
  approvedByDisplayName?: string | null; approvedAtUtc?: string | null; completionPercent: number;
  lastDecisionComment?: string | null; isActive: boolean; milestones: Milestone[]
}
type Monthly = {
  periodId: string; periodName: string; sequence: number; budget: number; actual: number;
  commitment: number; forecast: number; available: number
}
type FinancialSummary = {
  projectId: string; fiscalYearId: string; budget: number; actual: number; commitment: number;
  forecast: number; available: number; requestedBudget?: number | null; approvedBudgetLimit?: number | null;
  budgetVsApprovedLimitVariance: number; monthly: Monthly[]
}

const faNumber = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 0 })
const faDecimal = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })
const faDate = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { year: 'numeric', month: '2-digit', day: '2-digit' })

const statusLabels = ['پیشنهاد', 'ارسال‌شده', 'تأییدشده', 'در حال اجرا', 'متوقف', 'تکمیل‌شده', 'لغوشده']
const priorityLabels = ['کم', 'عادی', 'بالا', 'بحرانی']

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? fallback
  }
  return fallback
}

function inputDate(value?: string | null) {
  return value ? new Date(value).toISOString().slice(0, 10) : ''
}

function formatMoney(value?: number | null) {
  return value == null ? '-' : faNumber.format(value)
}

export default function CapexProjects({
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
  const [projects, setProjects] = useState<Project[]>([])
  const [owners, setOwners] = useState<OwnerUnit[]>([])
  const [currencies, setCurrencies] = useState<Currency[]>([])
  const [selectedId, setSelectedId] = useState('')
  const [project, setProject] = useState<Project | null>(null)
  const [financial, setFinancial] = useState<FinancialSummary | null>(null)
  const [statusFilter, setStatusFilter] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const isReviewer = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || roleSet.has('CFO') || roleSet.has('BUDGET_MANAGER')

  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [priority, setPriority] = useState(1)
  const [startDate, setStartDate] = useState('')
  const [endDate, setEndDate] = useState('')
  const [requestedBudget, setRequestedBudget] = useState('')
  const [approvedBudgetLimit, setApprovedBudgetLimit] = useState('')
  const [currencyCode, setCurrencyCode] = useState('IRR')
  const [ownerId, setOwnerId] = useState('')
  const [completionPercent, setCompletionPercent] = useState(0)
  const [decisionComment, setDecisionComment] = useState('')

  const [milestoneId, setMilestoneId] = useState<string | null>(null)
  const [milestoneCode, setMilestoneCode] = useState('')
  const [milestoneName, setMilestoneName] = useState('')
  const [milestoneDueDate, setMilestoneDueDate] = useState('')
  const [milestoneWeight, setMilestoneWeight] = useState(0)
  const [milestoneProgress, setMilestoneProgress] = useState(0)
  const [milestoneCompleted, setMilestoneCompleted] = useState(false)
  const [milestoneNote, setMilestoneNote] = useState('')

  const loadList = async () => {
    if (!companyId) return
    const params: Record<string, string | number> = { companyId }
    if (statusFilter !== '') params.status = Number(statusFilter)
    const { data } = await api.get<Project[]>('/capex/projects', { params })
    setProjects(data)
    setSelectedId(current => current && data.some(x => x.id === current) ? current : data[0]?.id ?? '')
  }

  const loadReference = async () => {
    if (!companyId) return
    const [ownerResponse, currencyResponse] = await Promise.all([
      api.get<OwnerUnit[]>('/capex/owner-units', { params: { companyId } }),
      api.get<Currency[]>('/reference/currencies')
    ])
    setOwners(ownerResponse.data)
    setCurrencies(currencyResponse.data)
    const base = currencyResponse.data.find(x => x.isBaseCurrency)
    setCurrencyCode(current => currencyResponse.data.some(x => x.code === current) ? current : base?.code ?? currencyResponse.data[0]?.code ?? 'IRR')
  }

  const loadProject = async (id: string) => {
    if (!id) { setProject(null); setFinancial(null); return }
    const [projectResponse, financialResponse] = await Promise.all([
      api.get<Project>(`/capex/projects/${id}`),
      api.get<FinancialSummary>(`/capex/projects/${id}/financial-summary`, { params: { fiscalYearId } })
    ])
    const value = projectResponse.data
    setProject(value)
    setFinancial(financialResponse.data)
    setName(value.name); setDescription(value.description ?? ''); setPriority(value.priority)
    setStartDate(inputDate(value.startDate)); setEndDate(inputDate(value.endDate))
    setRequestedBudget(value.requestedBudget == null ? '' : String(value.requestedBudget))
    setApprovedBudgetLimit(value.approvedBudgetLimit == null ? '' : String(value.approvedBudgetLimit))
    setCurrencyCode(value.currencyCode); setOwnerId(value.ownerOrganizationUnitId ?? '')
    setCompletionPercent(value.completionPercent); setDecisionComment('')
  }

  const reload = async () => {
    setBusy(true); setError('')
    try { await Promise.all([loadList(), loadReference()]) }
    catch (e) { setError(apiError(e, 'دریافت اطلاعات پروژه‌های سرمایه‌ای ناموفق بود.')) }
    finally { setBusy(false) }
  }

  useEffect(() => { setSelectedId(''); setProject(null); setFinancial(null); reload() }, [companyId, statusFilter])
  useEffect(() => {
    if (!selectedId || !fiscalYearId) { setProject(null); setFinancial(null); return }
    setBusy(true); setError('')
    loadProject(selectedId).catch(e => setError(apiError(e, 'دریافت جزئیات CAPEX ناموفق بود.'))).finally(() => setBusy(false))
  }, [selectedId, fiscalYearId])

  const createProject = async () => {
    if (!canWrite || !code.trim() || !name.trim() || !startDate || !endDate) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.post<Project>('/capex/projects', {
        companyId,
        code: code.trim(),
        name: name.trim(),
        description: description.trim() || null,
        priority,
        startDate,
        endDate,
        requestedBudget: requestedBudget === '' ? null : Number(requestedBudget),
        currencyCode,
        ownerOrganizationUnitId: ownerId || null
      })
      setCode(''); setMessage('پروژه سرمایه‌ای ایجاد و Member متناظر آن در Dimension پروژه ساخته شد.')
      await loadList(); setSelectedId(data.id)
    } catch (e) { setError(apiError(e, 'ایجاد پروژه سرمایه‌ای ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const saveProject = async () => {
    if (!canWrite || !project) return
    setBusy(true); setError(''); setMessage('')
    try {
      const { data } = await api.put<Project>(`/capex/projects/${project.id}`, {
        name: name.trim(), description: description.trim() || null, priority, startDate, endDate,
        requestedBudget: requestedBudget === '' ? null : Number(requestedBudget),
        approvedBudgetLimit: approvedBudgetLimit === '' ? null : Number(approvedBudgetLimit),
        currencyCode, ownerOrganizationUnitId: ownerId || null, completionPercent
      })
      setProject(data); setMessage('اطلاعات پروژه سرمایه‌ای ذخیره شد.'); await loadList(); await loadProject(project.id)
    } catch (e) { setError(apiError(e, 'ذخیره پروژه سرمایه‌ای ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const changeStatus = async (status: number) => {
    if (!canWrite || !project) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.post(`/capex/projects/${project.id}/status`, { status, comment: decisionComment.trim() || null })
      setDecisionComment(''); setMessage('وضعیت پروژه سرمایه‌ای به‌روزرسانی شد.'); await loadList(); await loadProject(project.id)
    } catch (e) { setError(apiError(e, 'تغییر وضعیت پروژه سرمایه‌ای ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const resetMilestone = () => {
    setMilestoneId(null); setMilestoneCode(''); setMilestoneName(''); setMilestoneDueDate('')
    setMilestoneWeight(0); setMilestoneProgress(0); setMilestoneCompleted(false); setMilestoneNote('')
  }

  const editMilestone = (item: Milestone) => {
    setMilestoneId(item.id); setMilestoneCode(item.code); setMilestoneName(item.name); setMilestoneDueDate(inputDate(item.dueDate))
    setMilestoneWeight(item.weight); setMilestoneProgress(item.progressPercent); setMilestoneCompleted(item.isCompleted); setMilestoneNote(item.note ?? '')
  }

  const saveMilestone = async () => {
    if (!canWrite || !project || !milestoneCode.trim() || !milestoneName.trim() || !milestoneDueDate) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.put(`/capex/projects/${project.id}/milestones`, {
        id: milestoneId, code: milestoneCode.trim(), name: milestoneName.trim(), dueDate: milestoneDueDate,
        weight: milestoneWeight, progressPercent: milestoneProgress, isCompleted: milestoneCompleted, note: milestoneNote.trim() || null
      })
      resetMilestone(); setMessage('Milestone پروژه ذخیره شد.'); await loadProject(project.id)
    } catch (e) { setError(apiError(e, 'ذخیره Milestone ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const deleteMilestone = async (item: Milestone) => {
    if (!canWrite || !project || !window.confirm(`Milestone «${item.name}» حذف شود؟`)) return
    setBusy(true); setError(''); setMessage('')
    try { await api.delete(`/capex/projects/${project.id}/milestones/${item.id}`); resetMilestone(); setMessage('Milestone حذف شد.'); await loadProject(project.id) }
    catch (e) { setError(apiError(e, 'حذف Milestone ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const transitionButtons = () => {
    if (!project || !canWrite) return [] as Array<[number, string, boolean]>
    switch (project.status) {
      case 0: return [[1, 'ارسال برای بررسی', true]]
      case 1: return isReviewer ? [[0, 'برگشت برای اصلاح', true], [2, 'تأیید پروژه', true], [6, 'لغو', true]] : []
      case 2: return [[3, 'شروع اجرا', true], ...(isReviewer ? [[6, 'لغو', true] as [number, string, boolean]] : [])]
      case 3: return [[4, 'توقف موقت', true], [5, 'اتمام پروژه', true], ...(isReviewer ? [[6, 'لغو', true] as [number, string, boolean]] : [])]
      case 4: return [[3, 'ازسرگیری اجرا', true], ...(isReviewer ? [[6, 'لغو', true] as [number, string, boolean]] : [])]
      default: return []
    }
  }

  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    {message && <Alert severity="success" onClose={() => setMessage('')}>{message}</Alert>}
    {!canWrite && <Alert severity="info">دسترسی شما برای شرکت انتخاب‌شده فقط خواندنی است؛ پروژه‌ها، Milestoneها و وضعیت مالی قابل مشاهده‌اند.</Alert>}

    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2}>
        <Box><Typography variant="h6" fontWeight={900}>پروژه‌های سرمایه‌ای</Typography><Typography color="text.secondary">هر پروژه به Member مستقل Dimension PROJECT متصل است و ارقام بودجه/عملکرد از Fact چندبعدی PBM خوانده می‌شود.</Typography></Box>
        <FormControl size="small" sx={{ minWidth: 180 }}><InputLabel>وضعیت</InputLabel><Select label="وضعیت" value={statusFilter} onChange={e => setStatusFilter(e.target.value)}><MenuItem value="">همه</MenuItem>{statusLabels.map((label, index) => <MenuItem value={String(index)} key={label}>{label}</MenuItem>)}</Select></FormControl>
      </Stack>
      <TableContainer sx={{ mt: 2 }}><Table size="small"><TableHead><TableRow><TableCell>کد</TableCell><TableCell>پروژه</TableCell><TableCell>وضعیت</TableCell><TableCell>اولویت</TableCell><TableCell>واحد مالک</TableCell><TableCell>بودجه درخواستی</TableCell><TableCell>پیشرفت</TableCell></TableRow></TableHead><TableBody>{projects.map(item => <TableRow key={item.id} hover selected={item.id === selectedId} onClick={() => setSelectedId(item.id)} sx={{ cursor: 'pointer' }}><TableCell sx={{ direction: 'ltr' }}>{item.code}</TableCell><TableCell>{item.name}</TableCell><TableCell><Chip size="small" label={statusLabels[item.status] ?? item.status} /></TableCell><TableCell>{priorityLabels[item.priority] ?? item.priority}</TableCell><TableCell>{item.ownerOrganizationUnitName ?? '-'}</TableCell><TableCell>{formatMoney(item.requestedBudget)} {item.currencyCode}</TableCell><TableCell sx={{ minWidth: 130 }}><Stack direction="row" spacing={1} alignItems="center"><LinearProgress variant="determinate" value={Math.min(100, item.completionPercent)} sx={{ width: 80 }} /><Typography variant="caption">{faDecimal.format(item.completionPercent)}٪</Typography></Stack></TableCell></TableRow>)}</TableBody></Table></TableContainer>
    </CardContent></Card>

    {canWrite && !project && <Card elevation={0}><CardContent><Typography variant="h6" fontWeight={900}>ایجاد پروژه سرمایه‌ای</Typography><ProjectFields code={code} setCode={setCode} name={name} setName={setName} description={description} setDescription={setDescription} priority={priority} setPriority={setPriority} startDate={startDate} setStartDate={setStartDate} endDate={endDate} setEndDate={setEndDate} requestedBudget={requestedBudget} setRequestedBudget={setRequestedBudget} approvedBudgetLimit={approvedBudgetLimit} setApprovedBudgetLimit={setApprovedBudgetLimit} currencyCode={currencyCode} setCurrencyCode={setCurrencyCode} ownerId={ownerId} setOwnerId={setOwnerId} owners={owners} currencies={currencies} isCreate isReviewer={isReviewer} /><Button variant="contained" sx={{ mt: 2 }} onClick={createProject} disabled={busy || !code.trim() || !name.trim() || !startDate || !endDate}>ایجاد پروژه</Button></CardContent></Card>}

    {project && <>
      <Card elevation={0}><CardContent>
        <Stack direction={{ xs: 'column', lg: 'row' }} justifyContent="space-between" spacing={2}><Box><Typography variant="h6" fontWeight={900}>{project.code} — {project.name}</Typography><Typography color="text.secondary">درخواست‌کننده: {project.requestedByDisplayName}{project.approvedByDisplayName ? ` | تأییدکننده: ${project.approvedByDisplayName}` : ''}</Typography></Box><Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap><Chip label={statusLabels[project.status] ?? project.status} color={project.status === 2 || project.status === 3 || project.status === 5 ? 'success' : project.status === 6 ? 'error' : 'default'} /><Chip label={`پیشرفت ${faDecimal.format(project.completionPercent)}٪`} variant="outlined" /></Stack></Stack>
        <Divider sx={{ my: 2 }} />
        <ProjectFields code={project.code} setCode={() => {}} name={name} setName={setName} description={description} setDescription={setDescription} priority={priority} setPriority={setPriority} startDate={startDate} setStartDate={setStartDate} endDate={endDate} setEndDate={setEndDate} requestedBudget={requestedBudget} setRequestedBudget={setRequestedBudget} approvedBudgetLimit={approvedBudgetLimit} setApprovedBudgetLimit={setApprovedBudgetLimit} currencyCode={currencyCode} setCurrencyCode={setCurrencyCode} ownerId={ownerId} setOwnerId={setOwnerId} owners={owners} currencies={currencies} isCreate={false} isReviewer={isReviewer} />
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} mt={2}><TextField size="small" type="number" label="درصد پیشرفت دستی" value={completionPercent} onChange={e => setCompletionPercent(Number(e.target.value))} disabled={!canWrite || project.milestones.length > 0} inputProps={{ min: 0, max: 100 }} /><Button variant="outlined" onClick={saveProject} disabled={!canWrite || busy || project.status === 5 || project.status === 6}>ذخیره اطلاعات</Button></Stack>
        {project.milestones.length > 0 && <Typography variant="caption" color="text.secondary" display="block" mt={1}>در صورت وجود Milestone، درصد پیشرفت از مجموع وزنی Milestoneها محاسبه می‌شود.</Typography>}
      </CardContent></Card>

      {transitionButtons().length > 0 && <Card elevation={0}><CardContent><Typography variant="h6" fontWeight={900}>گردش وضعیت پروژه</Typography><TextField fullWidth multiline minRows={2} label="توضیح تصمیم / بازگشت / لغو" value={decisionComment} onChange={e => setDecisionComment(e.target.value)} sx={{ mt: 1.5 }} /><Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap mt={1.5}>{transitionButtons().map(([status, label]) => <Button key={status} variant={status === 2 || status === 3 || status === 5 ? 'contained' : 'outlined'} color={status === 6 ? 'error' : 'primary'} onClick={() => changeStatus(status)} disabled={busy}>{label}</Button>)}</Stack></CardContent></Card>}

      <Card elevation={0}><CardContent>
        <Typography variant="h6" fontWeight={900}>Milestoneها و پیشرفت فیزیکی</Typography>
        <TableContainer sx={{ mt: 1.5 }}><Table size="small"><TableHead><TableRow><TableCell>کد</TableCell><TableCell>عنوان</TableCell><TableCell>سررسید شمسی</TableCell><TableCell>وزن</TableCell><TableCell>پیشرفت</TableCell><TableCell>وضعیت</TableCell><TableCell /></TableRow></TableHead><TableBody>{project.milestones.map(item => <TableRow key={item.id}><TableCell>{item.code}</TableCell><TableCell>{item.name}</TableCell><TableCell>{faDate.format(new Date(item.dueDate))}</TableCell><TableCell>{faDecimal.format(item.weight)}٪</TableCell><TableCell>{faDecimal.format(item.progressPercent)}٪</TableCell><TableCell>{item.isCompleted ? 'تکمیل' : 'باز'}</TableCell><TableCell><Stack direction="row" spacing={.5}><Button size="small" onClick={() => editMilestone(item)} disabled={!canWrite}>ویرایش</Button><Button size="small" color="error" onClick={() => deleteMilestone(item)} disabled={!canWrite}>حذف</Button></Stack></TableCell></TableRow>)}</TableBody></Table></TableContainer>
        {canWrite && project.status !== 5 && project.status !== 6 && <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1} mt={2} alignItems={{ lg: 'center' }}><TextField size="small" label="کد Milestone" value={milestoneCode} onChange={e => setMilestoneCode(e.target.value)} /><TextField size="small" label="عنوان" value={milestoneName} onChange={e => setMilestoneName(e.target.value)} /><TextField size="small" type="date" label="سررسید" InputLabelProps={{ shrink: true }} value={milestoneDueDate} onChange={e => setMilestoneDueDate(e.target.value)} /><TextField size="small" type="number" label="وزن ٪" value={milestoneWeight} onChange={e => setMilestoneWeight(Number(e.target.value))} inputProps={{ min: 0, max: 100 }} /><TextField size="small" type="number" label="پیشرفت ٪" value={milestoneProgress} onChange={e => setMilestoneProgress(Number(e.target.value))} inputProps={{ min: 0, max: 100 }} /><FormControl size="small" sx={{ minWidth: 120 }}><InputLabel>وضعیت</InputLabel><Select label="وضعیت" value={milestoneCompleted ? '1' : '0'} onChange={e => setMilestoneCompleted(e.target.value === '1')}><MenuItem value="0">باز</MenuItem><MenuItem value="1">تکمیل</MenuItem></Select></FormControl><Button variant="contained" onClick={saveMilestone} disabled={busy || !milestoneCode.trim() || !milestoneName.trim() || !milestoneDueDate}>{milestoneId ? 'ذخیره' : 'افزودن'}</Button>{milestoneId && <Button onClick={resetMilestone}>انصراف</Button>}</Stack>}
      </CardContent></Card>

      {financial && <Card elevation={0}><CardContent>
        <Typography variant="h6" fontWeight={900}>وضعیت مالی پروژه در سال مالی انتخاب‌شده</Typography><Typography color="text.secondary">ارقام از BudgetFact مدل CAPEX و Member همین پروژه خوانده می‌شوند.</Typography>
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap mt={2}><Chip label={`بودجه: ${formatMoney(financial.budget)}`} /><Chip label={`عملکرد: ${formatMoney(financial.actual)}`} /><Chip label={`تعهدات: ${formatMoney(financial.commitment)}`} /><Chip label={`در دسترس: ${formatMoney(financial.available)}`} color={financial.available < 0 ? 'error' : 'success'} variant="outlined" /><Chip label={`سقف مصوب: ${formatMoney(financial.approvedBudgetLimit)}`} variant="outlined" /></Stack>
        <TableContainer sx={{ mt: 2 }}><Table size="small"><TableHead><TableRow><TableCell>ماه</TableCell><TableCell>بودجه</TableCell><TableCell>عملکرد</TableCell><TableCell>تعهد</TableCell><TableCell>Forecast</TableCell><TableCell>در دسترس</TableCell></TableRow></TableHead><TableBody>{financial.monthly.map(item => <TableRow key={item.periodId}><TableCell>{item.periodName}</TableCell><TableCell>{formatMoney(item.budget)}</TableCell><TableCell>{formatMoney(item.actual)}</TableCell><TableCell>{formatMoney(item.commitment)}</TableCell><TableCell>{formatMoney(item.forecast)}</TableCell><TableCell>{formatMoney(item.available)}</TableCell></TableRow>)}</TableBody></Table></TableContainer>
      </CardContent></Card>}
    </>}
  </Stack>
}

function ProjectFields({
  code, setCode, name, setName, description, setDescription, priority, setPriority, startDate, setStartDate,
  endDate, setEndDate, requestedBudget, setRequestedBudget, approvedBudgetLimit, setApprovedBudgetLimit,
  currencyCode, setCurrencyCode, ownerId, setOwnerId, owners, currencies, isCreate, isReviewer
}: {
  code: string; setCode: (value: string) => void; name: string; setName: (value: string) => void;
  description: string; setDescription: (value: string) => void; priority: number; setPriority: (value: number) => void;
  startDate: string; setStartDate: (value: string) => void; endDate: string; setEndDate: (value: string) => void;
  requestedBudget: string; setRequestedBudget: (value: string) => void; approvedBudgetLimit: string; setApprovedBudgetLimit: (value: string) => void;
  currencyCode: string; setCurrencyCode: (value: string) => void; ownerId: string; setOwnerId: (value: string) => void;
  owners: OwnerUnit[]; currencies: Currency[]; isCreate: boolean; isReviewer: boolean
}) {
  return <Stack spacing={1.5} mt={2}>
    <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.5}>
      <TextField size="small" label="کد پروژه" value={code} onChange={e => setCode(e.target.value.toUpperCase())} disabled={!isCreate} placeholder="CAPEX-1405-001" />
      <TextField size="small" label="عنوان پروژه" value={name} onChange={e => setName(e.target.value)} sx={{ minWidth: 280 }} />
      <FormControl size="small" sx={{ minWidth: 140 }}><InputLabel>اولویت</InputLabel><Select label="اولویت" value={priority} onChange={e => setPriority(Number(e.target.value))}>{priorityLabels.map((label, index) => <MenuItem key={label} value={index}>{label}</MenuItem>)}</Select></FormControl>
      <FormControl size="small" sx={{ minWidth: 210 }}><InputLabel>واحد مالک</InputLabel><Select label="واحد مالک" value={ownerId} onChange={e => setOwnerId(e.target.value)}><MenuItem value="">بدون تخصیص</MenuItem>{owners.map(x => <MenuItem key={x.id} value={x.id}>{x.name} — {x.code}</MenuItem>)}</Select></FormControl>
    </Stack>
    <TextField size="small" multiline minRows={2} label="شرح پروژه" value={description} onChange={e => setDescription(e.target.value)} />
    <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.5}>
      <TextField size="small" type="date" label="تاریخ شروع" InputLabelProps={{ shrink: true }} value={startDate} onChange={e => setStartDate(e.target.value)} />
      <TextField size="small" type="date" label="تاریخ پایان" InputLabelProps={{ shrink: true }} value={endDate} onChange={e => setEndDate(e.target.value)} />
      <TextField size="small" type="number" label="بودجه درخواستی" value={requestedBudget} onChange={e => setRequestedBudget(e.target.value)} />
      {!isCreate && <TextField size="small" type="number" label="سقف بودجه مصوب" value={approvedBudgetLimit} onChange={e => setApprovedBudgetLimit(e.target.value)} disabled={!isReviewer} />}
      <FormControl size="small" sx={{ minWidth: 160 }}><InputLabel>ارز</InputLabel><Select label="ارز" value={currencyCode} onChange={e => setCurrencyCode(e.target.value)}>{currencies.map(x => <MenuItem key={x.id} value={x.code}>{x.code} — {x.name}</MenuItem>)}</Select></FormControl>
    </Stack>
  </Stack>
}
