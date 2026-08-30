import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle,
  Divider, FormControl, FormControlLabel, InputLabel, MenuItem, Select, Stack, Switch, Table, TableBody,
  TableCell, TableContainer, TableHead, TableRow, TextField, Typography
} from '@mui/material'
import { api } from './api'

type Role = { id: string; code: string; name: string }
type Company = { id: string; code: string; name: string; isActive: boolean }
type Unit = { id: string; companyId: string; parentId?: string | null; code: string; name: string; unitType: string; isActive: boolean }
type CompanyAccess = {
  companyId: string; companyCode: string; companyName: string;
  organizationUnitId?: string | null; organizationUnitCode?: string | null; organizationUnitName?: string | null;
  canRead: boolean; canWrite: boolean
}
type User = { id: string; userName: string; displayName: string; email?: string; isActive: boolean; roles: Role[]; companyAccess: CompanyAccess[] }
type LicenseUsage = { maxUsers: number; activeUsers: number; maxCompanies: number; activeCompanies: number; expiresAtUtc: string; isActive: boolean }
type AccessState = Record<string, { organizationUnitId: string; canRead: boolean; canWrite: boolean }>

const faDate = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { year: 'numeric', month: '2-digit', day: '2-digit' })

export default function SecurityAdmin({ showLicense = true }: { showLicense?: boolean }) {
  const [users, setUsers] = useState<User[]>([])
  const [roles, setRoles] = useState<Role[]>([])
  const [companies, setCompanies] = useState<Company[]>([])
  const [positionsByCompany, setPositionsByCompany] = useState<Record<string, Unit[]>>({})
  const [license, setLicense] = useState<LicenseUsage | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editing, setEditing] = useState<User | null>(null)
  const [userName, setUserName] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [isActive, setIsActive] = useState(true)
  const [roleIds, setRoleIds] = useState<string[]>([])
  const [access, setAccess] = useState<AccessState>({})
  const [roleCode, setRoleCode] = useState('')
  const [roleName, setRoleName] = useState('')
  const [newPassword, setNewPassword] = useState('')

  const reload = async () => {
    setError('')
    try {
      const [usersResponse, rolesResponse, companiesResponse, licenseResponse] = await Promise.all([
        api.get<User[]>('/admin/security/users'),
        api.get<Role[]>('/admin/security/roles'),
        api.get<Company[]>('/admin/organization/companies'),
        api.get<LicenseUsage>('/admin/security/license-usage')
      ])
      const activeCompanies = companiesResponse.data.filter(x => x.isActive)
      const unitResponses = await Promise.all(activeCompanies.map(company => api.get<Unit[]>(`/admin/organization/companies/${company.id}/units`)))
      const positions: Record<string, Unit[]> = {}
      activeCompanies.forEach((company, index) => {
        positions[company.id] = unitResponses[index].data.filter(x => x.isActive && x.unitType === 'Position').sort((a, b) => a.name.localeCompare(b.name, 'fa'))
      })
      setUsers(usersResponse.data); setRoles(rolesResponse.data); setCompanies(activeCompanies); setPositionsByCompany(positions); setLicense(licenseResponse.data)
    } catch { setError('دریافت اطلاعات کاربران، دسترسی‌ها و سمت‌های سازمانی ناموفق بود.') }
  }

  useEffect(() => { void reload() }, [])

  const openCreate = () => {
    setEditing(null); setUserName(''); setDisplayName(''); setEmail(''); setPassword(''); setIsActive(true); setRoleIds([]); setNewPassword('')
    const next: AccessState = {}; companies.forEach(c => { next[c.id] = { organizationUnitId: '', canRead: false, canWrite: false } }); setAccess(next); setDialogOpen(true)
  }

  const openEdit = (user: User) => {
    setEditing(user); setUserName(user.userName); setDisplayName(user.displayName); setEmail(user.email ?? ''); setPassword(''); setIsActive(user.isActive); setRoleIds(user.roles.map(x => x.id)); setNewPassword('')
    const next: AccessState = {}; companies.forEach(c => {
      const current = user.companyAccess.find(x => x.companyId === c.id)
      next[c.id] = { organizationUnitId: current?.organizationUnitId ?? '', canRead: current?.canRead ?? false, canWrite: current?.canWrite ?? false }
    }); setAccess(next); setDialogOpen(true)
  }

  const companyAccess = useMemo(() => Object.entries(access)
    .filter(([, value]) => value.canRead || value.canWrite)
    .map(([companyId, value]) => ({
      companyId,
      organizationUnitId: value.organizationUnitId || null,
      canRead: value.canRead || value.canWrite,
      canWrite: value.canWrite
    })), [access])

  const save = async () => {
    setBusy(true); setError('')
    try {
      if (editing) {
        await api.put(`/admin/security/users/${editing.id}`, { displayName, email: email || null, isActive, roleIds, companyAccess })
        if (newPassword) await api.put(`/admin/security/users/${editing.id}/password`, { newPassword })
      } else {
        await api.post('/admin/security/users', { userName, displayName, email: email || null, password, roleIds, companyAccess })
      }
      setDialogOpen(false); await reload()
    } catch (e: any) {
      setError(e?.response?.data?.detail ?? 'ذخیره کاربر ناموفق بود. رمز عبور باید حداقل ۱۰ کاراکتر و شامل حروف بزرگ، کوچک و عدد باشد.')
    } finally { setBusy(false) }
  }

  const createRole = async () => {
    if (!roleCode.trim() || !roleName.trim()) return
    setBusy(true); setError('')
    try { await api.post('/admin/security/roles', { code: roleCode, name: roleName }); setRoleCode(''); setRoleName(''); await reload() }
    catch (e: any) { setError(e?.response?.data?.detail ?? 'ایجاد نقش ناموفق بود.') }
    finally { setBusy(false) }
  }

  return <Stack spacing={2.5}>
    {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
    {showLicense && license && <Box className="kpi-grid">
      <Metric title="کاربران فعال" value={`${license.activeUsers.toLocaleString('fa-IR')} / ${license.maxUsers.toLocaleString('fa-IR')}`} />
      <Metric title="شرکت‌های فعال" value={`${license.activeCompanies.toLocaleString('fa-IR')} / ${license.maxCompanies.toLocaleString('fa-IR')}`} />
      <Metric title="اعتبار لایسنس" value={license.isActive ? 'فعال' : 'غیرفعال'} />
      <Metric title="انقضا" value={faDate.format(new Date(license.expiresAtUtc))} />
    </Box>}

    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2} mb={2}>
        <Box><Typography variant="h6" fontWeight={900}>کاربران، سمت سازمانی و سطح دسترسی</Typography><Typography color="text.secondary">کاربر ابتدا به شرکت دسترسی می‌گیرد و در صورت تعریف ساختار سازمانی می‌تواند به یکی از سمت‌های کددار همان شرکت متصل شود.</Typography></Box>
        <Button variant="contained" onClick={openCreate} disabled={busy || !!(license && license.activeUsers >= license.maxUsers)}>کاربر جدید</Button>
      </Stack>
      <TableContainer><Table size="small"><TableHead><TableRow><TableCell>کاربر</TableCell><TableCell>نام نمایشی</TableCell><TableCell>نقش‌ها</TableCell><TableCell>شرکت / سمت</TableCell><TableCell>وضعیت</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>
        {users.map(user => <TableRow key={user.id} hover><TableCell sx={{ direction: 'ltr' }}>{user.userName}</TableCell><TableCell>{user.displayName}<Typography variant="caption" display="block" color="text.secondary">{user.email ?? '—'}</Typography></TableCell><TableCell><Stack direction="row" spacing={.5} flexWrap="wrap" useFlexGap>{user.roles.map(r => <Chip key={r.id} size="small" label={r.name} />)}</Stack></TableCell><TableCell>{user.companyAccess.length ? user.companyAccess.map(x => <Box key={x.companyId} mb={.5}><Typography variant="body2" fontWeight={700}>{x.companyName}{x.organizationUnitName ? ` — ${x.organizationUnitName}` : ''}</Typography><Typography variant="caption" color="text.secondary">{x.organizationUnitCode ? `${x.organizationUnitCode} · ` : ''}{x.canWrite ? 'خواندن و نوشتن' : 'فقط خواندن'}</Typography></Box>) : '—'}</TableCell><TableCell><Chip size="small" color={user.isActive ? 'success' : 'default'} label={user.isActive ? 'فعال' : 'غیرفعال'} /></TableCell><TableCell><Button size="small" onClick={() => openEdit(user)}>ویرایش</Button></TableCell></TableRow>)}
      </TableBody></Table></TableContainer>
    </CardContent></Card>

    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>تعریف سطح دسترسی / نقش سیستمی</Typography><Typography color="text.secondary" mb={2}>نقش سیستمی با «سمت سازمانی» متفاوت است. سمت جایگاه فرد در ساختار شرکت است؛ نقش مشخص می‌کند در نرم‌افزار چه عملیاتی مجاز است.</Typography>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5}><TextField size="small" label="کد نقش" value={roleCode} onChange={e => setRoleCode(e.target.value.toUpperCase())} placeholder="BUDGET_MANAGER" /><TextField size="small" label="نام نقش" value={roleName} onChange={e => setRoleName(e.target.value)} placeholder="مدیر بودجه" /><Button variant="outlined" onClick={createRole} disabled={busy}>ایجاد نقش</Button></Stack>
      <Stack direction="row" spacing={.7} flexWrap="wrap" useFlexGap mt={2}>{roles.map(role => <Chip key={role.id} label={`${role.name} (${role.code})`} />)}</Stack>
    </CardContent></Card>

    <Dialog open={dialogOpen} onClose={() => !busy && setDialogOpen(false)} fullWidth maxWidth="md">
      <DialogTitle>{editing ? `ویرایش ${editing.displayName}` : 'ایجاد کاربر جدید'}</DialogTitle>
      <DialogContent><Stack spacing={2} mt={1}>
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5}>
          <TextField fullWidth label="نام کاربری" value={userName} onChange={e => setUserName(e.target.value)} disabled={!!editing} />
          <TextField fullWidth label="نام نمایشی" value={displayName} onChange={e => setDisplayName(e.target.value)} />
          <TextField fullWidth label="ایمیل" value={email} onChange={e => setEmail(e.target.value)} />
        </Stack>
        {!editing && <TextField type="password" label="رمز عبور اولیه" value={password} onChange={e => setPassword(e.target.value)} helperText="حداقل ۱۰ کاراکتر شامل حرف بزرگ، حرف کوچک و عدد" />}
        {editing && <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} alignItems={{ md: 'center' }}><TextField fullWidth type="password" label="رمز عبور جدید (اختیاری)" value={newPassword} onChange={e => setNewPassword(e.target.value)} /><FormControlLabel control={<Switch checked={isActive} onChange={e => setIsActive(e.target.checked)} />} label="کاربر فعال" /></Stack>}
        <FormControl fullWidth><InputLabel>نقش‌های سیستمی</InputLabel><Select multiple label="نقش‌های سیستمی" value={roleIds} onChange={e => setRoleIds(typeof e.target.value === 'string' ? e.target.value.split(',') : e.target.value)} renderValue={selected => roles.filter(x => selected.includes(x.id)).map(x => x.name).join('، ')}>{roles.map(role => <MenuItem key={role.id} value={role.id}><Checkbox checked={roleIds.includes(role.id)} />{role.name}</MenuItem>)}</Select></FormControl>
        <Divider />
        <Box><Typography fontWeight={900} mb={.5}>شرکت، سمت و سطح دسترسی</Typography><Typography variant="body2" color="text.secondary" mb={1.5}>سمت اختیاری است؛ ابتدا آن را در «اطلاعات پایه ← شرکت و ساختار سازمانی» تعریف کنید.</Typography>{companies.map(company => {
          const value = access[company.id] ?? { organizationUnitId: '', canRead: false, canWrite: false }
          const enabled = value.canRead || value.canWrite
          return <Stack key={company.id} direction={{ xs: 'column', lg: 'row' }} spacing={1.5} justifyContent="space-between" alignItems={{ lg: 'center' }} sx={{ py: 1.3, borderBottom: '1px solid #eef2f7' }}>
            <Box sx={{ minWidth: 150 }}><Typography fontWeight={800}>{company.name}</Typography><Typography variant="caption" color="text.secondary">{company.code}</Typography></Box>
            <FormControl size="small" sx={{ minWidth: 260 }} disabled={!enabled}><InputLabel>سمت سازمانی</InputLabel><Select value={value.organizationUnitId} label="سمت سازمانی" onChange={e => setAccess(x => ({ ...x, [company.id]: { ...value, organizationUnitId: e.target.value } }))}><MenuItem value="">بدون سمت</MenuItem>{(positionsByCompany[company.id] ?? []).map(position => <MenuItem key={position.id} value={position.id}>{position.name} — {position.code}</MenuItem>)}</Select></FormControl>
            <Stack direction="row"><FormControlLabel control={<Checkbox checked={value.canRead} onChange={e => setAccess(x => ({ ...x, [company.id]: { ...value, canRead: e.target.checked, canWrite: e.target.checked ? value.canWrite : false, organizationUnitId: e.target.checked ? value.organizationUnitId : '' } }))} />} label="خواندن" /><FormControlLabel control={<Checkbox checked={value.canWrite} onChange={e => setAccess(x => ({ ...x, [company.id]: { ...value, canRead: e.target.checked ? true : value.canRead, canWrite: e.target.checked } }))} />} label="نوشتن" /></Stack>
          </Stack>
        })}</Box>
      </Stack></DialogContent>
      <DialogActions><Button onClick={() => setDialogOpen(false)} disabled={busy}>انصراف</Button><Button variant="contained" onClick={save} disabled={busy || !displayName.trim() || (!editing && (!userName.trim() || !password))}>ذخیره</Button></DialogActions>
    </Dialog>
  </Stack>
}

function Metric({ title, value }: { title: string; value: string }) {
  return <Card elevation={0} className="kpi-card"><CardContent><Typography color="text.secondary" fontWeight={700}>{title}</Typography><Typography variant="h6" fontWeight={900} mt={1}>{value}</Typography></CardContent></Card>
}
