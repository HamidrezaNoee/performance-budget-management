import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Button, Card, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControl, InputLabel, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer,
  TableHead, TableRow, TextField, Typography
} from '@mui/material'
import { api } from './api'

type Objective = {
  id: string
  parentId?: string | null
  code: string
  name: string
  description?: string | null
  weight: number
  isActive: boolean
}
type Kpi = { id: string; code: string; name: string }
type Link = {
  kpiId: string
  kpiCode: string
  kpiName: string
  objectiveId: string
  objectiveCode: string
  objectiveName: string
  weight: number
}
type ObjectiveDraft = {
  id?: string
  parentId: string
  code: string
  name: string
  description: string
  weight: number
  isActive: boolean
}

const emptyDraft: ObjectiveDraft = { parentId: '', code: '', name: '', description: '', weight: 0, isActive: true }
const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? fallback
  }
  return fallback
}

export default function StrategyAdmin({ canManage }: { canManage: boolean }) {
  const [objectives, setObjectives] = useState<Objective[]>([])
  const [kpis, setKpis] = useState<Kpi[]>([])
  const [links, setLinks] = useState<Link[]>([])
  const [includeInactive, setIncludeInactive] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [dialogOpen, setDialogOpen] = useState(false)
  const [draft, setDraft] = useState<ObjectiveDraft>(emptyDraft)
  const [linkKpiId, setLinkKpiId] = useState('')
  const [linkObjectiveId, setLinkObjectiveId] = useState('')
  const [linkWeight, setLinkWeight] = useState(1)

  const activeObjectives = useMemo(() => objectives.filter(x => x.isActive), [objectives])
  const objectiveById = useMemo(() => new Map(objectives.map(x => [x.id, x])), [objectives])

  const reload = async () => {
    setBusy(true); setError('')
    try {
      const [objectiveResponse, kpiResponse, linkResponse] = await Promise.all([
        api.get<Objective[]>('/strategy/objectives', { params: { includeInactive } }),
        api.get<Kpi[]>('/performance/kpis'),
        api.get<Link[]>('/strategy/kpi-objective-links')
      ])
      setObjectives(objectiveResponse.data)
      setKpis(kpiResponse.data)
      setLinks(linkResponse.data)
      setLinkKpiId(current => current || kpiResponse.data[0]?.id || '')
      setLinkObjectiveId(current => current || objectiveResponse.data.find(x => x.isActive)?.id || '')
    } catch (error) { setError(apiError(error, 'دریافت اهداف راهبردی و Mapping شاخص‌ها ناموفق بود.')) }
    finally { setBusy(false) }
  }

  useEffect(() => { reload() }, [includeInactive])

  const openCreate = () => { setDraft(emptyDraft); setDialogOpen(true); setError(''); setMessage('') }
  const openEdit = (objective: Objective) => {
    setDraft({
      id: objective.id,
      parentId: objective.parentId ?? '',
      code: objective.code,
      name: objective.name,
      description: objective.description ?? '',
      weight: objective.weight,
      isActive: objective.isActive
    })
    setDialogOpen(true); setError(''); setMessage('')
  }

  const saveObjective = async () => {
    if (!canManage) return
    if (!draft.code.trim() || !draft.name.trim()) { setError('کد و نام هدف راهبردی الزامی است.'); return }
    if (!Number.isFinite(draft.weight) || draft.weight < 0 || draft.weight > 100) { setError('وزن هدف باید بین صفر تا صد باشد.'); return }
    setBusy(true); setError(''); setMessage('')
    try {
      if (draft.id) {
        await api.put(`/strategy/objectives/${draft.id}`, {
          parentId: draft.parentId || null,
          name: draft.name.trim(),
          description: draft.description.trim() || null,
          weight: draft.weight,
          isActive: draft.isActive
        })
        setMessage('هدف راهبردی به‌روزرسانی شد.')
      } else {
        await api.post('/strategy/objectives', {
          parentId: draft.parentId || null,
          code: draft.code.trim(),
          name: draft.name.trim(),
          description: draft.description.trim() || null,
          weight: draft.weight
        })
        setMessage('هدف راهبردی ایجاد شد.')
      }
      setDialogOpen(false)
      await reload()
    } catch (error) { setError(apiError(error, 'ذخیره هدف راهبردی ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const saveLink = async () => {
    if (!canManage) return
    if (!linkKpiId || !linkObjectiveId) { setError('KPI و هدف راهبردی را انتخاب کنید.'); return }
    if (!Number.isFinite(linkWeight) || linkWeight <= 0 || linkWeight > 100) { setError('وزن ارتباط باید بزرگ‌تر از صفر و حداکثر صد باشد.'); return }
    setBusy(true); setError(''); setMessage('')
    try {
      await api.put('/strategy/kpi-objective-links', { kpiId: linkKpiId, objectiveId: linkObjectiveId, weight: linkWeight })
      setMessage('ارتباط KPI با هدف راهبردی ذخیره شد.')
      await reload()
    } catch (error) { setError(apiError(error, 'ذخیره Mapping KPI و هدف ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const deleteLink = async (link: Link) => {
    if (!canManage || !window.confirm(`ارتباط ${link.kpiName} با ${link.objectiveName} حذف شود؟`)) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.delete(`/strategy/kpi-objective-links/${link.kpiId}/${link.objectiveId}`)
      setMessage('ارتباط KPI و هدف حذف شد.')
      await reload()
    } catch (error) { setError(apiError(error, 'حذف Mapping ناموفق بود.')) }
    finally { setBusy(false) }
  }

  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    {message && <Alert severity="success" onClose={() => setMessage('')}>{message}</Alert>}
    {!canManage && <Alert severity="info">اهداف و Mapping شاخص‌ها قابل مشاهده‌اند؛ ویرایش برای مدیر بودجه یا مدیر سامانه فعال است.</Alert>}

    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2}>
        <Stack spacing={.5}><Typography variant="h6" fontWeight={900}>اهداف راهبردی</Typography><Typography color="text.secondary">سلسله‌مراتب هدف‌ها و وزن آن‌ها، لایه Strategy را به KPI و تصمیم بودجه متصل می‌کند.</Typography></Stack>
        <Stack direction="row" spacing={1}><Button variant="outlined" onClick={() => setIncludeInactive(value => !value)}>{includeInactive ? 'فقط فعال‌ها' : 'نمایش غیرفعال‌ها'}</Button>{canManage && <Button variant="contained" onClick={openCreate}>هدف جدید</Button>}</Stack>
      </Stack>
    </CardContent></Card>

    <Card elevation={0}><CardContent sx={{ p: 0 }}><TableContainer sx={{ maxHeight: 360 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>کد / هدف</TableCell><TableCell>هدف والد</TableCell><TableCell align="left">وزن</TableCell><TableCell>وضعیت</TableCell><TableCell>شرح</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>{objectives.map(objective => <TableRow key={objective.id}><TableCell><Typography fontWeight={900}>{objective.name}</Typography><Typography variant="caption" color="text.secondary" sx={{ direction: 'ltr' }}>{objective.code}</Typography></TableCell><TableCell>{objective.parentId ? objectiveById.get(objective.parentId)?.name ?? 'والد خارج از فیلتر' : 'ریشه'}</TableCell><TableCell align="left">{number.format(objective.weight)}٪</TableCell><TableCell><Chip size="small" label={objective.isActive ? 'فعال' : 'غیرفعال'} color={objective.isActive ? 'success' : 'default'} variant="outlined" /></TableCell><TableCell sx={{ maxWidth: 360 }}>{objective.description ?? '-'}</TableCell><TableCell>{canManage ? <Button size="small" onClick={() => openEdit(objective)}>ویرایش</Button> : '-'}</TableCell></TableRow>)}{!objectives.length && !busy && <TableRow><TableCell colSpan={6} align="center" sx={{ py: 5 }}>هدف راهبردی تعریف نشده است.</TableCell></TableRow>}</TableBody></Table></TableContainer></CardContent></Card>

    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>Mapping KPI به اهداف</Typography>
      <Typography color="text.secondary" mb={2}>یک KPI می‌تواند به چند هدف متصل شود. وزن ارتباط هنگام محاسبه سهم KPI در هر Objective نرمال‌سازی می‌شود.</Typography>
      {canManage && <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} mb={2}>
        <FormControl size="small" sx={{ minWidth: 260, flex: 1 }}><InputLabel>KPI</InputLabel><Select value={linkKpiId} label="KPI" onChange={e => setLinkKpiId(e.target.value)}>{kpis.map(kpi => <MenuItem key={kpi.id} value={kpi.id}>{kpi.name} ({kpi.code})</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 260, flex: 1 }}><InputLabel>هدف راهبردی</InputLabel><Select value={linkObjectiveId} label="هدف راهبردی" onChange={e => setLinkObjectiveId(e.target.value)}>{activeObjectives.map(objective => <MenuItem key={objective.id} value={objective.id}>{objective.name} ({objective.code})</MenuItem>)}</Select></FormControl>
        <TextField size="small" type="number" label="وزن ارتباط" value={linkWeight} onChange={e => setLinkWeight(Number(e.target.value))} sx={{ width: 150 }} />
        <Button variant="contained" onClick={saveLink} disabled={busy || !linkKpiId || !linkObjectiveId}>ثبت Mapping</Button>
      </Stack>}
      <TableContainer sx={{ maxHeight: 360 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>هدف راهبردی</TableCell><TableCell>KPI</TableCell><TableCell align="left">وزن ارتباط</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>{links.map(link => <TableRow key={`${link.kpiId}-${link.objectiveId}`}><TableCell>{link.objectiveName}<Typography variant="caption" color="text.secondary" display="block">{link.objectiveCode}</Typography></TableCell><TableCell>{link.kpiName}<Typography variant="caption" color="text.secondary" display="block">{link.kpiCode}</Typography></TableCell><TableCell align="left">{number.format(link.weight)}</TableCell><TableCell>{canManage ? <Button size="small" color="error" onClick={() => deleteLink(link)}>حذف</Button> : '-'}</TableCell></TableRow>)}{!links.length && <TableRow><TableCell colSpan={4} align="center" sx={{ py: 4 }}>هنوز KPI به هدف راهبردی متصل نشده است.</TableCell></TableRow>}</TableBody></Table></TableContainer>
    </CardContent></Card>

    <Dialog open={dialogOpen} onClose={() => !busy && setDialogOpen(false)} fullWidth maxWidth="sm"><DialogTitle>{draft.id ? 'ویرایش هدف راهبردی' : 'تعریف هدف راهبردی'}</DialogTitle><DialogContent><Stack spacing={2} mt={1}>
      <TextField label="کد هدف" value={draft.code} disabled={!!draft.id} onChange={e => setDraft(current => ({ ...current, code: e.target.value }))} />
      <TextField label="نام هدف" value={draft.name} onChange={e => setDraft(current => ({ ...current, name: e.target.value }))} />
      <FormControl><InputLabel>هدف والد</InputLabel><Select value={draft.parentId} label="هدف والد" onChange={e => setDraft(current => ({ ...current, parentId: e.target.value }))}><MenuItem value="">ریشه</MenuItem>{objectives.filter(x => x.isActive && x.id !== draft.id).map(objective => <MenuItem key={objective.id} value={objective.id}>{objective.name} ({objective.code})</MenuItem>)}</Select></FormControl>
      <TextField type="number" label="وزن راهبردی (%)" value={draft.weight} onChange={e => setDraft(current => ({ ...current, weight: Number(e.target.value) }))} />
      <TextField label="شرح" multiline minRows={3} value={draft.description} onChange={e => setDraft(current => ({ ...current, description: e.target.value }))} />
      {draft.id && <FormControl><InputLabel>وضعیت</InputLabel><Select value={draft.isActive ? 'active' : 'inactive'} label="وضعیت" onChange={e => setDraft(current => ({ ...current, isActive: e.target.value === 'active' }))}><MenuItem value="active">فعال</MenuItem><MenuItem value="inactive">غیرفعال</MenuItem></Select></FormControl>}
    </Stack></DialogContent><DialogActions><Button onClick={() => setDialogOpen(false)} disabled={busy}>انصراف</Button><Button variant="contained" onClick={saveObjective} disabled={busy || !draft.name.trim() || (!draft.id && !draft.code.trim())}>ذخیره</Button></DialogActions></Dialog>
  </Stack>
}
