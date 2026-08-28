import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Chip, Divider, FormControl, InputLabel, MenuItem, Select, Stack,
  Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography
} from '@mui/material'
import { api } from './api'

type Company = { id: string; code: string; name: string; industry?: string; isActive: boolean; createdAtUtc: string }
type Unit = { id: string; companyId: string; parentId?: string; code: string; name: string; unitType: string; isActive: boolean }
type LicenseUsage = { maxUsers: number; activeUsers: number; maxCompanies: number; activeCompanies: number; expiresAtUtc: string; isActive: boolean }

export default function OrganizationAdmin() {
  const [companies, setCompanies] = useState<Company[]>([])
  const [companyId, setCompanyId] = useState('')
  const [units, setUnits] = useState<Unit[]>([])
  const [license, setLicense] = useState<LicenseUsage | null>(null)
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
      const [companyResponse, licenseResponse] = await Promise.all([
        api.get<Company[]>('/admin/organization/companies'),
        api.get<LicenseUsage>('/admin/security/license-usage')
      ])
      setCompanies(companyResponse.data); setLicense(licenseResponse.data)
      setCompanyId(current => current && companyResponse.data.some(x => x.id === current) ? current : companyResponse.data.find(x => x.isActive)?.id ?? companyResponse.data[0]?.id ?? '')
    } catch { setError('دریافت شرکت‌ها و وضعیت لایسنس ناموفق بود.') }
  }

  const reloadUnits = async () => {
    if (!companyId) { setUnits([]); return }
    try { const { data } = await api.get<Unit[]>(`/admin/organization/companies/${companyId}/units`); setUnits(data) }
    catch { setError('دریافت ساختار سازمانی ناموفق بود.') }
  }

  useEffect(() => { reloadCompanies() }, [])
  useEffect(() => { reloadUnits() }, [companyId])

  const createCompany = async () => {
    if (!code.trim() || !name.trim()) return
    setBusy(true); setError('')
    try {
      const { data } = await api.post<Company>('/admin/organization/companies', { code, name, industry: industry || null })
      setCode(''); setName(''); setIndustry(''); await reloadCompanies(); setCompanyId(data.id)
    } catch (e: any) { setError(e?.response?.data?.detail ?? 'ایجاد شرکت ناموفق بود.') }
    finally { setBusy(false) }
  }

  const toggleCompany = async (company: Company) => {
    setBusy(true); setError('')
    try { await api.put(`/admin/organization/companies/${company.id}`, { name: company.name, industry: company.industry ?? null, isActive: !company.isActive }); await reloadCompanies() }
    catch (e: any) { setError(e?.response?.data?.detail ?? 'تغییر وضعیت شرکت ناموفق بود.') }
    finally { setBusy(false) }
  }

  const createUnit = async () => {
    if (!companyId || !unitCode.trim() || !unitName.trim()) return
    setBusy(true); setError('')
    try {
      await api.post('/admin/organization/units', { companyId, parentId: parentId || null, code: unitCode, name: unitName, unitType })
      setUnitCode(''); setUnitName(''); setParentId(''); await reloadUnits()
    } catch (e: any) { setError(e?.response?.data?.detail ?? 'ایجاد واحد سازمانی ناموفق بود.') }
    finally { setBusy(false) }
  }

  const toggleUnit = async (unit: Unit) => {
    setBusy(true); setError('')
    try { await api.put(`/admin/organization/units/${unit.id}`, { parentId: unit.parentId ?? null, name: unit.name, unitType: unit.unitType, isActive: !unit.isActive }); await reloadUnits() }
    catch (e: any) { setError(e?.response?.data?.detail ?? 'تغییر وضعیت واحد سازمانی ناموفق بود.') }
    finally { setBusy(false) }
  }

  const selectedCompany = useMemo(() => companies.find(x => x.id === companyId), [companies, companyId])
  const parentName = (id?: string) => id ? units.find(x => x.id === id)?.name ?? '-' : '-'

  return <Stack spacing={2.5}>
    {error && <Alert severity="error">{error}</Alert>}
    {license && <Alert severity={license.isActive ? 'info' : 'error'}>لایسنس: {license.activeCompanies.toLocaleString('fa-IR')} شرکت فعال از سقف {license.maxCompanies.toLocaleString('fa-IR')} شرکت.</Alert>}

    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>تعریف شرکت</Typography>
      <Typography color="text.secondary" mb={2}>ایجاد شرکت جدید تحت Tenant جاری با کنترل سقف لایسنس.</Typography>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.5}>
        <TextField size="small" label="کد شرکت" value={code} onChange={e => setCode(e.target.value)} placeholder="COMP-02" />
        <TextField size="small" label="نام شرکت" value={name} onChange={e => setName(e.target.value)} />
        <TextField size="small" label="صنعت / حوزه فعالیت" value={industry} onChange={e => setIndustry(e.target.value)} />
        <Button variant="contained" onClick={createCompany} disabled={busy || !code.trim() || !name.trim() || !!(license && license.activeCompanies >= license.maxCompanies)}>ایجاد شرکت</Button>
      </Stack>
    </CardContent></Card>

    <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <Box p={2.5}><Typography variant="h6" fontWeight={900}>شرکت‌ها</Typography></Box><Divider />
      <TableContainer><Table size="small"><TableHead><TableRow><TableCell>کد</TableCell><TableCell>نام</TableCell><TableCell>حوزه</TableCell><TableCell>وضعیت</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>
        {companies.map(company => <TableRow key={company.id} hover selected={company.id === companyId} onClick={() => setCompanyId(company.id)} sx={{ cursor: 'pointer' }}><TableCell>{company.code}</TableCell><TableCell><Typography fontWeight={800}>{company.name}</Typography></TableCell><TableCell>{company.industry ?? '-'}</TableCell><TableCell><Chip size="small" color={company.isActive ? 'success' : 'default'} label={company.isActive ? 'فعال' : 'غیرفعال'} /></TableCell><TableCell><Button size="small" color={company.isActive ? 'warning' : 'primary'} onClick={e => { e.stopPropagation(); toggleCompany(company) }}>{company.isActive ? 'غیرفعال‌سازی' : 'فعال‌سازی'}</Button></TableCell></TableRow>)}
      </TableBody></Table></TableContainer>
    </CardContent></Card>

    {selectedCompany && <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>ساختار سازمانی — {selectedCompany.name}</Typography>
      <Typography color="text.secondary" mb={2}>واحدهای ایجادشده همزمان با Dimension «واحد سازمانی» همگام می‌شوند تا مستقیماً در بودجه قابل استفاده باشند.</Typography>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.5} mb={2}>
        <TextField size="small" label="کد واحد" value={unitCode} onChange={e => setUnitCode(e.target.value)} placeholder="FIN" />
        <TextField size="small" label="نام واحد" value={unitName} onChange={e => setUnitName(e.target.value)} />
        <FormControl size="small" sx={{ minWidth: 160 }}><InputLabel>نوع</InputLabel><Select value={unitType} label="نوع" onChange={e => setUnitType(e.target.value)}><MenuItem value="Holding">هلدینگ</MenuItem><MenuItem value="Division">معاونت</MenuItem><MenuItem value="Department">مدیریت / دپارتمان</MenuItem><MenuItem value="Unit">واحد</MenuItem><MenuItem value="CostCenter">مرکز هزینه</MenuItem></Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 220 }}><InputLabel>واحد بالادست</InputLabel><Select value={parentId} label="واحد بالادست" onChange={e => setParentId(e.target.value)}><MenuItem value="">بدون بالادست</MenuItem>{units.filter(x => x.isActive).map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl>
        <Button variant="contained" onClick={createUnit} disabled={busy || !unitCode.trim() || !unitName.trim()}>افزودن واحد</Button>
      </Stack>
      <TableContainer sx={{ border: '1px solid #e8eef5', borderRadius: 2 }}><Table size="small"><TableHead><TableRow><TableCell>کد</TableCell><TableCell>نام</TableCell><TableCell>نوع</TableCell><TableCell>بالادست</TableCell><TableCell>وضعیت</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>{units.map(unit => <TableRow key={unit.id}><TableCell>{unit.code}</TableCell><TableCell><Typography fontWeight={800}>{unit.name}</Typography></TableCell><TableCell>{unit.unitType}</TableCell><TableCell>{parentName(unit.parentId)}</TableCell><TableCell><Chip size="small" color={unit.isActive ? 'success' : 'default'} label={unit.isActive ? 'فعال' : 'غیرفعال'} /></TableCell><TableCell><Button size="small" onClick={() => toggleUnit(unit)}>{unit.isActive ? 'غیرفعال' : 'فعال'}</Button></TableCell></TableRow>)}</TableBody></Table></TableContainer>
    </CardContent></Card>}
  </Stack>
}
