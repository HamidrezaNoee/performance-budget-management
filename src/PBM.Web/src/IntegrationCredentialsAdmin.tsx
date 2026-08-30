import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Chip, FormControl, InputLabel,
  MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead,
  TableRow, TextField, Typography
} from '@mui/material'
import { api } from './api'

type Company = { id: string; code: string; name: string }
type Credential = {
  id: string
  userId: string
  userName: string
  name: string
  clientId: string
  expiresAtUtc?: string | null
  lastUsedAtUtc?: string | null
  revokedAtUtc?: string | null
  revocationReason?: string | null
  isActive: boolean
}
type SecretResult = { credential: Credential; clientSecret: string }

const faDateTime = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { dateStyle: 'short', timeStyle: 'short' })

function date(value?: string | null) {
  return value ? faDateTime.format(new Date(value)) : '-'
}

function errorText(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? fallback
  }
  return fallback
}

export default function IntegrationCredentialsAdmin({ companyId }: { companyId: string }) {
  const [credentials, setCredentials] = useState<Credential[]>([])
  const [companies, setCompanies] = useState<Company[]>([])
  const [selectedCompanies, setSelectedCompanies] = useState<string[]>(companyId ? [companyId] : [])
  const [userName, setUserName] = useState('erp-service')
  const [displayName, setDisplayName] = useState('اتصال ERP / حسابداری')
  const [credentialName, setCredentialName] = useState('ERP Production')
  const [expiryDate, setExpiryDate] = useState('')
  const [secretResult, setSecretResult] = useState<SecretResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (companyId && !selectedCompanies.length) setSelectedCompanies([companyId])
  }, [companyId])

  const companyById = useMemo(() => new Map(companies.map(x => [x.id, x])), [companies])

  const reload = async () => {
    setError('')
    try {
      const [credentialResponse, companyResponse] = await Promise.all([
        api.get<Credential[]>('/security/integration-credentials/'),
        api.get<Company[]>('/companies')
      ])
      setCredentials(credentialResponse.data)
      setCompanies(companyResponse.data)
      if (!selectedCompanies.length && companyId) setSelectedCompanies([companyId])
    } catch (error) {
      setError(errorText(error, 'دریافت حساب‌های Integration ناموفق بود.'))
    }
  }

  useEffect(() => { reload() }, [companyId])

  const createServiceAccount = async () => {
    if (!userName.trim() || !displayName.trim() || !credentialName.trim() || selectedCompanies.length === 0) return
    setBusy(true); setError(''); setSecretResult(null)
    try {
      const expiresAtUtc = expiryDate ? new Date(`${expiryDate}T23:59:59Z`).toISOString() : null
      const { data } = await api.post<SecretResult>('/security/integration-credentials/service-accounts', {
        userName: userName.trim(),
        displayName: displayName.trim(),
        credentialName: credentialName.trim(),
        companyAccess: selectedCompanies.map(id => ({ companyId: id, canRead: true, canWrite: true })),
        expiresAtUtc
      })
      setSecretResult(data)
      setUserName(''); setDisplayName(''); setCredentialName('ERP Production'); setExpiryDate('')
      await reload()
    } catch (error) {
      setError(errorText(error, 'ایجاد Service Account ناموفق بود.'))
    } finally { setBusy(false) }
  }

  const rotate = async (credential: Credential) => {
    if (!window.confirm(`Secret مربوط به ${credential.clientId} تعویض شود؟ Tokenهای قبلی این Service Account نیز باطل می‌شوند.`)) return
    setBusy(true); setError(''); setSecretResult(null)
    try {
      const { data } = await api.post<SecretResult>(`/security/integration-credentials/${credential.id}/rotate`, { expiresAtUtc: null })
      setSecretResult(data)
      await reload()
    } catch (error) {
      setError(errorText(error, 'تعویض Secret ناموفق بود.'))
    } finally { setBusy(false) }
  }

  const revoke = async (credential: Credential) => {
    const reason = window.prompt('دلیل ابطال Credential را وارد کنید (حداقل ۵ کاراکتر):')?.trim()
    if (!reason) return
    setBusy(true); setError('')
    try {
      await api.post(`/security/integration-credentials/${credential.id}/revoke`, { reason })
      await reload()
    } catch (error) {
      setError(errorText(error, 'ابطال Credential ناموفق بود.'))
    } finally { setBusy(false) }
  }

  const copySecret = async () => {
    if (!secretResult) return
    await navigator.clipboard.writeText(secretResult.clientSecret)
  }

  return <Stack spacing={2}>
    {error && <Alert severity="error">{error}</Alert>}
    {secretResult && <Alert severity="warning" action={<Button color="inherit" onClick={copySecret}>کپی Secret</Button>}>
      <Typography fontWeight={900}>این Secret فقط همین یک‌بار نمایش داده می‌شود.</Typography>
      <Typography variant="body2" sx={{ direction: 'ltr', fontFamily: 'monospace', overflowWrap: 'anywhere' }}>
        Client ID: {secretResult.credential.clientId}
      </Typography>
      <Typography variant="body2" sx={{ direction: 'ltr', fontFamily: 'monospace', overflowWrap: 'anywhere' }}>
        Client Secret: {secretResult.clientSecret}
      </Typography>
    </Alert>}

    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>ایجاد Service Account برای ERP / حسابداری</Typography>
      <Typography color="text.secondary" mb={2}>
        حساب Integration رمز عبور قابل استفاده ندارد. دسترسی API فقط با Client ID/Secret و Token کوتاه‌عمر انجام می‌شود و Token آن فقط Actual Ledger را می‌بیند.
      </Typography>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.5} alignItems={{ lg: 'flex-start' }}>
        <TextField size="small" label="نام کاربری سرویس" value={userName} onChange={e => setUserName(e.target.value)} sx={{ minWidth: 180 }} />
        <TextField size="small" label="عنوان حساب" value={displayName} onChange={e => setDisplayName(e.target.value)} sx={{ minWidth: 220 }} />
        <TextField size="small" label="نام Credential" value={credentialName} onChange={e => setCredentialName(e.target.value)} sx={{ minWidth: 180 }} />
        <FormControl size="small" sx={{ minWidth: 260 }}>
          <InputLabel>شرکت‌های مجاز</InputLabel>
          <Select multiple value={selectedCompanies} label="شرکت‌های مجاز" onChange={e => setSelectedCompanies(typeof e.target.value === 'string' ? e.target.value.split(',') : e.target.value)} renderValue={selected => selected.map(id => companyById.get(id)?.name ?? id).join('، ')}>
            {companies.map(company => <MenuItem key={company.id} value={company.id}><Checkbox checked={selectedCompanies.includes(company.id)} /><Typography>{company.name}</Typography></MenuItem>)}
          </Select>
        </FormControl>
        <TextField size="small" type="date" label="انقضای Secret (اختیاری)" InputLabelProps={{ shrink: true }} value={expiryDate} onChange={e => setExpiryDate(e.target.value)} />
        <Button variant="contained" disabled={busy || !userName.trim() || !displayName.trim() || !credentialName.trim() || selectedCompanies.length === 0} onClick={createServiceAccount}>ایجاد و نمایش Secret</Button>
      </Stack>
    </CardContent></Card>

    <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <Box p={2.5}><Typography variant="h6" fontWeight={900}>Credentialهای Integration</Typography><Typography variant="caption" color="text.secondary">Rotate و Revoke باعث باطل‌شدن Tokenهای قبلی همان Service Account می‌شوند.</Typography></Box>
      <TableContainer><Table size="small"><TableHead><TableRow><TableCell>Service Account</TableCell><TableCell>نام Credential</TableCell><TableCell>Client ID</TableCell><TableCell>انقضا</TableCell><TableCell>آخرین استفاده</TableCell><TableCell>وضعیت</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>
        {credentials.map(row => <TableRow key={row.id}>
          <TableCell><Typography fontWeight={800}>{row.userName}</Typography></TableCell>
          <TableCell>{row.name}</TableCell>
          <TableCell sx={{ direction: 'ltr', fontFamily: 'monospace' }}>{row.clientId}</TableCell>
          <TableCell>{date(row.expiresAtUtc)}</TableCell>
          <TableCell>{date(row.lastUsedAtUtc)}</TableCell>
          <TableCell>{row.isActive ? <Chip size="small" color="success" label="فعال" /> : <Chip size="small" label={row.revokedAtUtc ? 'باطل‌شده' : 'منقضی'} />}{row.revocationReason && <Typography variant="caption" display="block" color="text.secondary">{row.revocationReason}</Typography>}</TableCell>
          <TableCell><Stack direction="row" spacing={1}><Button size="small" disabled={busy || !row.isActive} onClick={() => rotate(row)}>Rotate</Button><Button size="small" color="error" disabled={busy || !!row.revokedAtUtc} onClick={() => revoke(row)}>Revoke</Button></Stack></TableCell>
        </TableRow>)}
        {!credentials.length && <TableRow><TableCell colSpan={7} align="center" sx={{ py: 5 }}>هنوز Service Account یا Credentialی ایجاد نشده است.</TableCell></TableRow>}
      </TableBody></Table></TableContainer>
    </CardContent></Card>
  </Stack>
}
