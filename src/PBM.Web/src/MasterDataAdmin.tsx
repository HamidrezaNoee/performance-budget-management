import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, FormControl, InputLabel,
  MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow,
  TextField, Typography
} from '@mui/material'
import AddRoundedIcon from '@mui/icons-material/AddRounded'
import Inventory2RoundedIcon from '@mui/icons-material/Inventory2Rounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import TuneRoundedIcon from '@mui/icons-material/TuneRounded'
import { api } from './api'

type Dimension = {
  id: string
  code: string
  name: string
  isHierarchical: boolean
  isSystem: boolean
  isActive: boolean
}

type Member = {
  id: string
  dimensionId: string
  parentId?: string | null
  companyId?: string | null
  code: string
  name: string
  externalKey?: string | null
  isActive: boolean
}

type Scope = 'company' | 'global'

const primaryDimensionCodes = new Set(['PRODUCT', 'SUPPLIER', 'BRAND', 'CURRENCY'])
const advancedDimensionCodes = new Set([
  'CUSTOMER', 'CONTRACT', 'REGION', 'DEPARTMENT', 'COSTCENTER', 'ACCOUNT',
  'PROGRAM', 'ACTIVITY', 'PROJECT', 'FUNDINGSOURCE', 'EXPENSECLASS', 'EXPENSEITEM', 'PURCHASECOST'
])

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; message?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.message ?? response?.data?.title ?? fallback
  }
  return fallback
}

export default function MasterDataAdmin({ companyId, roles }: { companyId: string; roles: string[] }) {
  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const canManage = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || roleSet.has('BUDGET_MANAGER')
  const canGlobal = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN')

  const [dimensions, setDimensions] = useState<Dimension[]>([])
  const [dimensionId, setDimensionId] = useState('')
  const [members, setMembers] = useState<Member[]>([])
  const [scope, setScope] = useState<Scope>('company')
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [externalKey, setExternalKey] = useState('')
  const [showAdvanced, setShowAdvanced] = useState(false)
  const [busy, setBusy] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  const selectedDimension = dimensions.find(x => x.id === dimensionId)
  const selectedCode = selectedDimension?.code.toUpperCase() ?? ''
  const isProduct = selectedCode === 'PRODUCT'
  const isSupplier = selectedCode === 'SUPPLIER'
  const isBrand = selectedCode === 'BRAND'
  const isCurrency = selectedCode === 'CURRENCY'
  const visibleDimensions = useMemo(() => dimensions.filter(item => {
    const itemCode = item.code.toUpperCase()
    return primaryDimensionCodes.has(itemCode) || (showAdvanced && advancedDimensionCodes.has(itemCode))
  }), [dimensions, showAdvanced])

  const loadMembers = async (targetDimensionId = dimensionId) => {
    if (!targetDimensionId || !companyId) { setMembers([]); return }
    setBusy(true); setError('')
    try {
      const response = await api.get<Member[]>('/master-data/members', {
        params: { dimensionId: targetDimensionId, companyId, includeInactive: true }
      })
      setMembers(response.data)
    } catch (requestError) {
      setError(apiError(requestError, 'دریافت اعضای داده پایه ناموفق بود.'))
    } finally { setBusy(false) }
  }

  const loadDimensions = async () => {
    if (!companyId) return
    setBusy(true); setError(''); setMessage('')
    try {
      const response = await api.get<Dimension[]>('/master-data/dimensions')
      setDimensions(response.data)
      const product = response.data.find(x => x.code.toUpperCase() === 'PRODUCT')
      const nextId = product?.id ?? response.data.find(x => primaryDimensionCodes.has(x.code.toUpperCase()))?.id ?? ''
      setDimensionId(nextId)
      if (nextId) await loadMembers(nextId)
      else setMembers([])
    } catch (requestError) {
      setError(apiError(requestError, 'دریافت فهرست داده‌های پایه ناموفق بود.'))
    } finally { setBusy(false) }
  }

  useEffect(() => { void loadDimensions() }, [companyId])
  useEffect(() => { if (dimensionId) void loadMembers(dimensionId) }, [dimensionId])
  useEffect(() => {
    if (showAdvanced || !selectedDimension || primaryDimensionCodes.has(selectedDimension.code.toUpperCase())) return
    const product = dimensions.find(x => x.code.toUpperCase() === 'PRODUCT')
    setDimensionId(product?.id ?? dimensions.find(x => primaryDimensionCodes.has(x.code.toUpperCase()))?.id ?? '')
  }, [showAdvanced])

  const createMember = async () => {
    if (!canManage || !dimensionId || !companyId || !code.trim() || !name.trim()) return
    setSaving(true); setError(''); setMessage('')
    try {
      await api.post('/master-data/members', {
        dimensionId,
        companyId: scope === 'global' ? null : companyId,
        code: code.trim().toUpperCase(),
        name: name.trim(),
        externalKey: externalKey.trim() || null
      })
      setCode(''); setName(''); setExternalKey('')
      await loadMembers(dimensionId)
      setMessage(`${selectedDimension?.name ?? 'داده پایه'} با موفقیت ثبت شد.`)
    } catch (requestError) {
      setError(apiError(requestError, 'ثبت داده پایه ناموفق بود.'))
    } finally { setSaving(false) }
  }

  const toggleActive = async (member: Member) => {
    if (!canManage) return
    setSaving(true); setError(''); setMessage('')
    try {
      await api.put(`/master-data/members/${member.id}`, {
        name: member.name,
        externalKey: member.externalKey ?? null,
        isActive: !member.isActive
      })
      await loadMembers(dimensionId)
      setMessage(member.isActive ? 'عضو غیرفعال شد.' : 'عضو دوباره فعال شد.')
    } catch (requestError) {
      setError(apiError(requestError, 'تغییر وضعیت داده پایه ناموفق بود.'))
    } finally { setSaving(false) }
  }

  const codePlaceholder = isProduct ? 'ITEM-001' : isSupplier ? 'SUPPLIER-001' : isBrand ? 'BRAND-001' : isCurrency ? 'USD' : 'CODE-001'
  const namePlaceholder = isProduct ? 'نام کالا / محصول' : isSupplier ? 'نام تأمین‌کننده' : isBrand ? 'نام برند' : isCurrency ? 'نام ارز' : 'نام داده پایه'

  return <Card elevation={0}>
    <CardContent>
      <Stack direction={{ xs: 'column', lg: 'row' }} justifyContent="space-between" spacing={2} alignItems={{ lg: 'center' }} mb={2}>
        <Box>
          <Stack direction="row" spacing={1} alignItems="center">
            <Inventory2RoundedIcon color="primary" />
            <Typography variant="h6" fontWeight={900}>اطلاعات پایه بودجه</Typography>
          </Stack>
          <Typography color="text.secondary" mt={0.7}>
            این بخش مستقل از صنعت است. هر شرکت می‌تواند کالاها، تأمین‌کنندگان، برندها و ارزهای متناسب با فعالیت خودش را تعریف کند.
          </Typography>
        </Box>
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
          <Button startIcon={<TuneRoundedIcon />} variant={showAdvanced ? 'contained' : 'outlined'} onClick={() => setShowAdvanced(x => !x)}>
            {showAdvanced ? 'فقط اطلاعات اصلی' : 'نمایش اطلاعات تکمیلی'}
          </Button>
          <Button startIcon={<RefreshRoundedIcon />} onClick={() => void loadDimensions()} disabled={busy || saving}>بازخوانی</Button>
        </Stack>
      </Stack>

      <Alert severity="info" sx={{ mb: 2 }}>
        در حالت اصلی فقط کالا، تأمین‌کننده، برند و ارز نمایش داده می‌شوند. برای مشتری، قرارداد، منطقه، واحد سازمانی، مرکز هزینه، حساب، برنامه، فعالیت، پروژه و منبع تأمین مالی «نمایش اطلاعات تکمیلی» را بزنید.
      </Alert>

      {error && <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError('')}>{error}</Alert>}
      {message && <Alert severity="success" sx={{ mb: 2 }} onClose={() => setMessage('')}>{message}</Alert>}

      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} alignItems={{ md: 'center' }} mb={2}>
        <FormControl size="small" sx={{ minWidth: 290 }}>
          <InputLabel>نوع داده پایه</InputLabel>
          <Select label="نوع داده پایه" value={dimensionId} onChange={e => setDimensionId(e.target.value)}>
            {visibleDimensions.map(item => <MenuItem key={item.id} value={item.id}>{item.name} — {item.code}</MenuItem>)}
          </Select>
        </FormControl>
        {isProduct && <Alert severity="info" sx={{ py: 0 }}>برای ثبت بودجه خرید یا فروش، حداقل یک کالا / محصول تعریف کنید.</Alert>}
        {isCurrency && <Alert severity="info" sx={{ py: 0 }}>کدهای ارز بودجه را اینجا تعریف کنید؛ نرخ تبدیل ارز در تب «ارز و نرخ ارز» نگهداری می‌شود.</Alert>}
      </Stack>

      {showAdvanced && <Alert severity="warning" sx={{ mb: 2 }}>
        حالت تکمیلی فعال است. این Dimensionها فعلاً در فرم ساده خرید و فروش نمایش داده نمی‌شوند و برای توسعه‌های بعدی و تحلیل‌های پیشرفته نگهداری می‌شوند.
      </Alert>}

      {canManage ? <Card variant="outlined" sx={{ mb: 2 }}><CardContent>
        <Typography fontWeight={900} mb={1.5}>افزودن {selectedDimension?.name ?? 'عضو جدید'}</Typography>
        <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.2}>
          <TextField size="small" label="کد *" value={code} onChange={e => setCode(e.target.value.toUpperCase())} placeholder={codePlaceholder} sx={{ minWidth: 190, direction: 'ltr' }} />
          <TextField size="small" label="نام *" value={name} onChange={e => setName(e.target.value)} placeholder={namePlaceholder} sx={{ minWidth: 270 }} />
          <TextField size="small" label="کد/کلید ERP (اختیاری)" value={externalKey} onChange={e => setExternalKey(e.target.value)} placeholder="ERP-KEY-001" sx={{ minWidth: 210, direction: 'ltr' }} />
          {canGlobal && <FormControl size="small" sx={{ minWidth: 180 }}>
            <InputLabel>دامنه</InputLabel>
            <Select label="دامنه" value={scope} onChange={e => setScope(e.target.value as Scope)}>
              <MenuItem value="company">فقط شرکت جاری</MenuItem>
              <MenuItem value="global">سراسری Tenant</MenuItem>
            </Select>
          </FormControl>}
          <Button variant="contained" startIcon={<AddRoundedIcon />} onClick={() => void createMember()} disabled={saving || busy || !dimensionId || !code.trim() || !name.trim()}>ثبت</Button>
        </Stack>
        <Typography variant="caption" color="text.secondary" display="block" mt={1.2}>
          برای اتصال آینده به ERP می‌توانید شناسه سیستم مبدأ را در «کلید ERP» نگهداری کنید. حذف فیزیکی انجام نمی‌شود؛ اعضای استفاده‌شده را غیرفعال کنید.
        </Typography>
      </CardContent></Card> : <Alert severity="info" sx={{ mb: 2 }}>ثبت داده پایه برای مدیر سامانه یا مدیر بودجه فعال است.</Alert>}

      {busy && !members.length ? <Box py={4} textAlign="center"><CircularProgress size={28} /></Box> :
        <TableContainer sx={{ maxHeight: 430 }}>
          <Table stickyHeader size="small">
            <TableHead><TableRow><TableCell>کد</TableCell><TableCell>نام</TableCell><TableCell>کلید ERP</TableCell><TableCell>دامنه</TableCell><TableCell>وضعیت</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead>
            <TableBody>
              {members.map(member => <TableRow key={member.id} hover>
                <TableCell sx={{ direction: 'ltr', fontFamily: 'monospace' }}>{member.code}</TableCell>
                <TableCell>{member.name}</TableCell>
                <TableCell sx={{ direction: 'ltr' }}>{member.externalKey || '—'}</TableCell>
                <TableCell>{member.companyId ? 'شرکت جاری' : 'سراسری'}</TableCell>
                <TableCell><Chip size="small" label={member.isActive ? 'فعال' : 'غیرفعال'} color={member.isActive ? 'success' : 'default'} variant="outlined" /></TableCell>
                <TableCell><Button size="small" onClick={() => void toggleActive(member)} disabled={!canManage || saving}>{member.isActive ? 'غیرفعال‌سازی' : 'فعال‌سازی'}</Button></TableCell>
              </TableRow>)}
              {!members.length && !busy && <TableRow><TableCell colSpan={6} align="center" sx={{ py: 4, color: 'text.secondary' }}>هنوز عضوی برای {selectedDimension?.name ?? 'این Dimension'} ثبت نشده است.</TableCell></TableRow>}
            </TableBody>
          </Table>
        </TableContainer>}
    </CardContent>
  </Card>
}
