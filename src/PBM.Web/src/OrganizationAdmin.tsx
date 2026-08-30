import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Chip, Divider, FormControl, InputLabel, MenuItem, Select, Stack,
  Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography
} from '@mui/material'
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded'
import BadgeRoundedIcon from '@mui/icons-material/BadgeRounded'
import { api } from './api'

type Company = { id: string; code: string; name: string; industry?: string; isActive: boolean; createdAtUtc: string }
type Unit = { id: string; companyId: string; parentId?: string | null; code: string; name: string; unitType: string; isActive: boolean }
type FlatUnit = Unit & { depth: number }

const notifyWorkspaceChanged = () => window.dispatchEvent(new Event('pbm:workspace-data-changed'))
const typeLabel: Record<string, string> = {
  Holding: 'هلدینگ', Division: 'معاونت', Department: 'دپارتمان / مدیریت', Unit: 'واحد', CostCenter: 'مرکز هزینه', Position: 'سمت'
}

function flattenTree(units: Unit[]): FlatUnit[] {
  const children = new Map<string | null, Unit[]>()
  units.forEach(unit => {
    const key = unit.parentId ?? null
    children.set(key, [...(children.get(key) ?? []), unit])
  })
  children.forEach(items => items.sort((a, b) => a.name.localeCompare(b.name, 'fa')))
  const result: FlatUnit[] = []
  const visit = (parentId: string | null, depth: number, guard: Set<string>) => {
    for (const unit of children.get(parentId) ?? []) {
      if (guard.has(unit.id)) continue
      result.push({ ...unit, depth })
      const next = new Set(guard); next.add(unit.id)
      visit(unit.id, depth + 1, next)
    }
  }
  visit(null, 0, new Set())
  for (const unit of units) if (!result.some(x => x.id === unit.id)) result.push({ ...unit, depth: 0 })
  return result
}

export default function OrganizationAdmin() {
  const [companies, setCompanies] = useState<Company[]>([])
  const [companyId, setCompanyId] = useState('')
  const [units, setUnits] = useState<Unit[]>([])
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [industry, setIndustry] = useState('')
  const [unitCode, setUnitCode] = useState('')
  const [unitName, setUnitName] = useState('')
  const [unitType, setUnitType] = useState('Department')
  const [parentId, setParentId] = useState('')

  const reloadCompanies = async () => {
    setError('')
    try {
      const { data } = await api.get<Company[]>('/admin/organization/companies')
      setCompanies(data)
      setCompanyId(current => current && data.some(x => x.id === current) ? current : data.find(x => x.isActive)?.id ?? data[0]?.id ?? '')
    } catch { setError('دریافت شرکت‌ها ناموفق بود.') }
  }

  const reloadUnits = async () => {
    if (!companyId) { setUnits([]); return }
    try { const { data } = await api.get<Unit[]>(`/admin/organization/companies/${companyId}/units`); setUnits(data) }
    catch { setError('دریافت ساختار سازمانی ناموفق بود.') }
  }

  useEffect(() => { void reloadCompanies() }, [])
  useEffect(() => { void reloadUnits() }, [companyId])
  useEffect(() => {
    if (unitType === 'Position' && parentId && units.find(x => x.id === parentId)?.unitType === 'Position') setParentId('')
  }, [unitType, units])

  const createCompany = async () => {
    if (!code.trim() || !name.trim()) return
    setBusy(true); setError('')
    try {
      const { data } = await api.post<Company>('/admin/organization/companies', { code, name, industry: industry || null })
      setCode(''); setName(''); setIndustry(''); await reloadCompanies(); setCompanyId(data.id); notifyWorkspaceChanged()
    } catch (e: any) { setError(e?.response?.data?.detail ?? 'ایجاد شرکت ناموفق بود.') }
    finally { setBusy(false) }
  }

  const editCompany = async (company: Company) => {
    const nextName = window.prompt('نام شرکت:', company.name)
    if (nextName === null || !nextName.trim()) return
    const nextIndustry = window.prompt('صنعت / حوزه فعالیت (اختیاری):', company.industry ?? '')
    if (nextIndustry === null) return
    setBusy(true); setError('')
    try {
      await api.put(`/admin/organization/companies/${company.id}`, { name: nextName.trim(), industry: nextIndustry.trim() || null, isActive: company.isActive })
      await reloadCompanies(); notifyWorkspaceChanged()
    } catch (e: any) { setError(e?.response?.data?.detail ?? 'ویرایش شرکت ناموفق بود.') }
    finally { setBusy(false) }
  }

  const toggleCompany = async (company: Company) => {
    setBusy(true); setError('')
    try {
      await api.put(`/admin/organization/companies/${company.id}`, { name: company.name, industry: company.industry ?? null, isActive: !company.isActive })
      await reloadCompanies(); notifyWorkspaceChanged()
    } catch (e: any) { setError(e?.response?.data?.detail ?? 'تغییر وضعیت شرکت ناموفق بود.') }
    finally { setBusy(false) }
  }

  const createUnit = async () => {
    if (!companyId || !unitCode.trim() || !unitName.trim()) return
    if (unitType === 'Position' && !parentId) { setError('برای تعریف سمت، ابتدا دپارتمان یا واحد بالادست را انتخاب کنید.'); return }
    setBusy(true); setError('')
    try {
      await api.post('/admin/organization/units', { companyId, parentId: parentId || null, code: unitCode, name: unitName, unitType })
      setUnitCode(''); setUnitName(''); if (unitType !== 'Position') setParentId(''); await reloadUnits()
    } catch (e: any) { setError(e?.response?.data?.detail ?? 'ایجاد ساختار سازمانی ناموفق بود.') }
    finally { setBusy(false) }
  }

  const toggleUnit = async (unit: Unit) => {
    setBusy(true); setError('')
    try { await api.put(`/admin/organization/units/${unit.id}`, { parentId: unit.parentId ?? null, name: unit.name, unitType: unit.unitType, isActive: !unit.isActive }); await reloadUnits() }
    catch (e: any) { setError(e?.response?.data?.detail ?? 'تغییر وضعیت ساختار سازمانی ناموفق بود.') }
    finally { setBusy(false) }
  }

  const selectedCompany = useMemo(() => companies.find(x => x.id === companyId), [companies, companyId])
  const tree = useMemo(() => flattenTree(units), [units])
  const parentOptions = useMemo(() => units.filter(x => x.isActive && x.unitType !== 'Position' && (unitType !== 'Position' || x.unitType !== 'CostCenter')), [units, unitType])

  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}

    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>شرکت‌ها</Typography>
      <Typography color="text.secondary" mb={2}>هر شرکت یک موجودیت مستقل است و ساختار سازمانی، سال مالی، کاربران و داده‌های بودجه‌ای خودش را دارد.</Typography>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.5}>
        <TextField size="small" label="کد شرکت" value={code} onChange={e => setCode(e.target.value.toUpperCase())} placeholder="COMP-01" />
        <TextField size="small" label="نام شرکت" value={name} onChange={e => setName(e.target.value)} />
        <TextField size="small" label="صنعت / حوزه فعالیت" value={industry} onChange={e => setIndustry(e.target.value)} placeholder="اختیاری" />
        <Button variant="contained" onClick={createCompany} disabled={busy || !code.trim() || !name.trim()}>ایجاد شرکت</Button>
      </Stack>
    </CardContent></Card>

    <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <Box p={2.5}><Typography variant="h6" fontWeight={900}>فهرست شرکت‌ها</Typography></Box><Divider />
      <TableContainer><Table size="small"><TableHead><TableRow><TableCell>کد</TableCell><TableCell>نام</TableCell><TableCell>حوزه</TableCell><TableCell>وضعیت</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>
        {companies.map(company => <TableRow key={company.id} hover selected={company.id === companyId} onClick={() => setCompanyId(company.id)} sx={{ cursor: 'pointer' }}><TableCell sx={{ direction: 'ltr' }}>{company.code}</TableCell><TableCell><Typography fontWeight={800}>{company.name}</Typography></TableCell><TableCell>{company.industry ?? '—'}</TableCell><TableCell><Chip size="small" color={company.isActive ? 'success' : 'default'} label={company.isActive ? 'فعال' : 'غیرفعال'} /></TableCell><TableCell><Stack direction="row" spacing={.5}><Button size="small" onClick={e => { e.stopPropagation(); void editCompany(company) }}>ویرایش</Button><Button size="small" color={company.isActive ? 'warning' : 'primary'} onClick={e => { e.stopPropagation(); void toggleCompany(company) }}>{company.isActive ? 'غیرفعال‌سازی' : 'فعال‌سازی'}</Button></Stack></TableCell></TableRow>)}
      </TableBody></Table></TableContainer>
    </CardContent></Card>

    {selectedCompany && <Card elevation={0}><CardContent>
      <Stack direction="row" spacing={1} alignItems="center"><AccountTreeRoundedIcon color="primary" /><Typography variant="h6" fontWeight={900}>ساختار سازمانی — {selectedCompany.name}</Typography></Stack>
      <Typography color="text.secondary" mb={2}>دپارتمان‌ها و واحدها به‌صورت درختی تعریف می‌شوند. «سمت» دارای کد مستقل است و باید زیر یک دپارتمان یا واحد سازمانی قرار بگیرد؛ سپس در تنظیمات کاربران، کاربر به همان سمت متصل می‌شود.</Typography>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.5} mb={2}>
        <TextField size="small" label={unitType === 'Position' ? 'کد سمت' : 'کد واحد'} value={unitCode} onChange={e => setUnitCode(e.target.value.toUpperCase())} placeholder={unitType === 'Position' ? 'POS-001' : 'DEPT-001'} />
        <TextField size="small" label={unitType === 'Position' ? 'عنوان سمت' : 'نام واحد'} value={unitName} onChange={e => setUnitName(e.target.value)} />
        <FormControl size="small" sx={{ minWidth: 185 }}><InputLabel>نوع</InputLabel><Select value={unitType} label="نوع" onChange={e => { setUnitType(e.target.value); setParentId('') }}><MenuItem value="Holding">هلدینگ</MenuItem><MenuItem value="Division">معاونت</MenuItem><MenuItem value="Department">دپارتمان / مدیریت</MenuItem><MenuItem value="Unit">واحد</MenuItem><MenuItem value="CostCenter">مرکز هزینه</MenuItem><MenuItem value="Position"><Stack direction="row" spacing={1} alignItems="center"><BadgeRoundedIcon fontSize="small" />سمت</Stack></MenuItem></Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 250 }}><InputLabel>{unitType === 'Position' ? 'دپارتمان / واحد بالادست *' : 'واحد بالادست'}</InputLabel><Select value={parentId} label={unitType === 'Position' ? 'دپارتمان / واحد بالادست *' : 'واحد بالادست'} onChange={e => setParentId(e.target.value)}><MenuItem value="">{unitType === 'Position' ? 'انتخاب کنید' : 'بدون بالادست'}</MenuItem>{parentOptions.map(x => <MenuItem key={x.id} value={x.id}>{x.name} — {x.code}</MenuItem>)}</Select></FormControl>
        <Button variant="contained" onClick={createUnit} disabled={busy || !unitCode.trim() || !unitName.trim() || (unitType === 'Position' && !parentId)}>افزودن</Button>
      </Stack>

      <TableContainer sx={{ border: '1px solid #e8eef5', borderRadius: 2 }}><Table size="small"><TableHead><TableRow><TableCell>ساختار</TableCell><TableCell>کد</TableCell><TableCell>نوع</TableCell><TableCell>وضعیت</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>
        {tree.map(unit => <TableRow key={unit.id} sx={{ opacity: unit.isActive ? 1 : .55 }}><TableCell><Box sx={{ pr: `${unit.depth * 22}px` }}><Typography fontWeight={unit.unitType === 'Position' ? 650 : 900}>{unit.depth > 0 ? '↳ ' : ''}{unit.name}</Typography></Box></TableCell><TableCell sx={{ direction: 'ltr', fontFamily: 'monospace' }}>{unit.code}</TableCell><TableCell><Chip size="small" variant="outlined" label={typeLabel[unit.unitType] ?? unit.unitType} /></TableCell><TableCell><Chip size="small" color={unit.isActive ? 'success' : 'default'} label={unit.isActive ? 'فعال' : 'غیرفعال'} /></TableCell><TableCell><Button size="small" onClick={() => void toggleUnit(unit)}>{unit.isActive ? 'غیرفعال' : 'فعال'}</Button></TableCell></TableRow>)}
        {!tree.length && <TableRow><TableCell colSpan={5} align="center" sx={{ py: 4, color: 'text.secondary' }}>هنوز ساختار سازمانی تعریف نشده است.</TableCell></TableRow>}
      </TableBody></Table></TableContainer>
    </CardContent></Card>}
  </Stack>
}
