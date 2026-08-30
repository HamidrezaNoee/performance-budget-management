import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, FormControl, FormControlLabel,
  InputLabel, MenuItem, Select, Stack, Switch, Table, TableBody, TableCell, TableContainer,
  TableHead, TableRow, TextField, Typography
} from '@mui/material'
import AddRoundedIcon from '@mui/icons-material/AddRounded'
import Inventory2RoundedIcon from '@mui/icons-material/Inventory2Rounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import EditRoundedIcon from '@mui/icons-material/EditRounded'
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
  metadataJson?: string | null
  isActive: boolean
}

type Scope = 'company' | 'global'
type Metadata = Record<string, string | number | boolean | null>

const operationalCodes = new Set(['PRODUCT', 'BRAND', 'UOM', 'SUPPLIER', 'COUNTRY', 'GEOGRAPHY', 'WAREHOUSE', 'CUSTOMS'])
const secondaryCodes = new Set(['CUSTOMER', 'CONTRACT', 'REGION', 'COSTCENTER', 'ACCOUNT', 'PROGRAM', 'ACTIVITY', 'PROJECT', 'FUNDINGSOURCE'])

const labels: Record<string, string> = {
  PRODUCT: 'کالا / محصول', BRAND: 'برند', UOM: 'واحد سنجش', SUPPLIER: 'تأمین‌کننده', COUNTRY: 'کشور',
  GEOGRAPHY: 'جغرافیا', WAREHOUSE: 'انبار', CUSTOMS: 'گمرک / مبادی گمرکی', CUSTOMER: 'مشتری',
  CONTRACT: 'قرارداد', REGION: 'منطقه', COSTCENTER: 'مرکز هزینه', ACCOUNT: 'حساب', PROGRAM: 'برنامه',
  ACTIVITY: 'فعالیت', PROJECT: 'پروژه', FUNDINGSOURCE: 'منبع تأمین مالی'
}

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; message?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.message ?? response?.data?.title ?? fallback
  }
  return fallback
}

function parseMetadata(value?: string | null): Metadata {
  if (!value) return {}
  try { return JSON.parse(value) as Metadata }
  catch { return {} }
}

function text(meta: Metadata, key: string) { const value = meta[key]; return value === null || value === undefined ? '' : String(value) }
function bool(meta: Metadata, key: string, fallback = false) { const value = meta[key]; return typeof value === 'boolean' ? value : fallback }
function numberOrNull(value: string): number | null { const parsed = Number(value); return value.trim() && Number.isFinite(parsed) ? parsed : null }

export default function MasterDataAdmin({ companyId, roles, initialDimensionCode }: { companyId: string; roles: string[]; initialDimensionCode?: string }) {
  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const canManage = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || roleSet.has('BUDGET_MANAGER')
  const canGlobal = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN')
  const lockedDimensionCode = initialDimensionCode?.trim().toUpperCase() ?? ''

  const [dimensions, setDimensions] = useState<Dimension[]>([])
  const [dimensionId, setDimensionId] = useState('')
  const [members, setMembers] = useState<Member[]>([])
  const [relatedMembers, setRelatedMembers] = useState<Record<string, Member[]>>({})
  const [showSecondary, setShowSecondary] = useState(false)
  const [scope, setScope] = useState<Scope>('company')
  const [editingId, setEditingId] = useState('')
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [nameEn, setNameEn] = useState('')
  const [externalKey, setExternalKey] = useState('')
  const [parentId, setParentId] = useState('')
  const [brandMemberId, setBrandMemberId] = useState('')
  const [uomMemberId, setUomMemberId] = useState('')
  const [countryMemberId, setCountryMemberId] = useState('')
  const [geographyMemberId, setGeographyMemberId] = useState('')
  const [specifications, setSpecifications] = useState('')
  const [symbol, setSymbol] = useState('')
  const [taxId, setTaxId] = useState('')
  const [address, setAddress] = useState('')
  const [iso2, setIso2] = useState('')
  const [iso3, setIso3] = useState('')
  const [isDomestic, setIsDomestic] = useState(false)
  const [locationType, setLocationType] = useState('Province')
  const [latitude, setLatitude] = useState('')
  const [longitude, setLongitude] = useState('')
  const [mapAddress, setMapAddress] = useState('')
  const [busy, setBusy] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  const selectedDimension = dimensions.find(x => x.id === dimensionId)
  const selectedCode = selectedDimension?.code.toUpperCase() ?? ''
  const visibleDimensions = useMemo(() => dimensions.filter(d => operationalCodes.has(d.code.toUpperCase()) || (showSecondary && secondaryCodes.has(d.code.toUpperCase()))), [dimensions, showSecondary])
  const parents = useMemo(() => members.filter(x => x.isActive && x.id !== editingId), [members, editingId])
  const brands = relatedMembers.BRAND ?? []
  const uoms = relatedMembers.UOM ?? []
  const countries = relatedMembers.COUNTRY ?? []
  const geographies = relatedMembers.GEOGRAPHY ?? []
  const memberById = useMemo(() => new Map(Object.values(relatedMembers).flat().map(x => [x.id, x])), [relatedMembers])

  const resetForm = () => {
    setEditingId(''); setCode(''); setName(''); setNameEn(''); setExternalKey(''); setParentId(''); setBrandMemberId(''); setUomMemberId('')
    setCountryMemberId(''); setGeographyMemberId(''); setSpecifications(''); setSymbol(''); setTaxId(''); setAddress(''); setIso2(''); setIso3('')
    setIsDomestic(false); setLocationType('Province'); setLatitude(''); setLongitude(''); setMapAddress(''); setScope('company')
  }

  const loadMembers = async (targetDimensionId = dimensionId) => {
    if (!targetDimensionId || !companyId) { setMembers([]); return }
    setBusy(true); setError('')
    try {
      const response = await api.get<Member[]>('/master-data/members', { params: { dimensionId: targetDimensionId, companyId, includeInactive: true } })
      setMembers(response.data)
    } catch (requestError) { setError(apiError(requestError, 'دریافت داده‌های پایه ناموفق بود.')) }
    finally { setBusy(false) }
  }

  const loadRelated = async (allDimensions: Dimension[]) => {
    if (!companyId) return
    const wanted = ['BRAND', 'UOM', 'COUNTRY', 'GEOGRAPHY']
    const pairs = await Promise.all(wanted.map(async dimensionCode => {
      const dimension = allDimensions.find(x => x.code.toUpperCase() === dimensionCode)
      if (!dimension) return [dimensionCode, []] as const
      const response = await api.get<Member[]>('/master-data/members', { params: { dimensionId: dimension.id, companyId, includeInactive: false } })
      return [dimensionCode, response.data] as const
    }))
    setRelatedMembers(Object.fromEntries(pairs))
  }

  const loadDimensions = async () => {
    if (!companyId) return
    setBusy(true); setError(''); setMessage('')
    try {
      const response = await api.get<Dimension[]>('/master-data/dimensions')
      setDimensions(response.data)
      await loadRelated(response.data)
      const requested = lockedDimensionCode ? response.data.find(x => x.code.toUpperCase() === lockedDimensionCode) : undefined
      const product = response.data.find(x => x.code.toUpperCase() === 'PRODUCT')
      const nextId = requested?.id ?? product?.id ?? response.data.find(x => operationalCodes.has(x.code.toUpperCase()))?.id ?? ''
      setDimensionId(nextId)
      if (nextId) await loadMembers(nextId); else setMembers([])
    } catch (requestError) { setError(apiError(requestError, 'دریافت فهرست اطلاعات پایه ناموفق بود.')) }
    finally { setBusy(false) }
  }

  useEffect(() => { void loadDimensions() }, [companyId, lockedDimensionCode])
  useEffect(() => { resetForm(); if (dimensionId) void loadMembers(dimensionId) }, [dimensionId])

  const buildMetadata = (): Metadata => {
    const common: Metadata = { nameEn: nameEn.trim() || null }
    if (selectedCode === 'PRODUCT') return { ...common, brandMemberId: brandMemberId || null, uomMemberId: uomMemberId || null, specifications: specifications.trim() || null }
    if (selectedCode === 'BRAND') return common
    if (selectedCode === 'UOM') return { ...common, symbol: symbol.trim() || null }
    if (selectedCode === 'SUPPLIER') return { ...common, countryMemberId: countryMemberId || null, address: address.trim() || null, taxId: taxId.trim() || null }
    if (selectedCode === 'COUNTRY') return { ...common, iso2: iso2.trim().toUpperCase() || null, iso3: iso3.trim().toUpperCase() || null, isDomestic, latitude: numberOrNull(latitude), longitude: numberOrNull(longitude), mapAddress: mapAddress.trim() || null }
    if (selectedCode === 'GEOGRAPHY') return { ...common, locationType, countryMemberId: countryMemberId || null, latitude: numberOrNull(latitude), longitude: numberOrNull(longitude), mapAddress: mapAddress.trim() || null }
    if (selectedCode === 'WAREHOUSE') return { ...common, geographyMemberId: geographyMemberId || null, address: address.trim() || null, latitude: numberOrNull(latitude), longitude: numberOrNull(longitude), mapAddress: mapAddress.trim() || null }
    if (selectedCode === 'CUSTOMS') return { ...common, countryMemberId: countryMemberId || null, geographyMemberId: geographyMemberId || null, address: address.trim() || null, latitude: numberOrNull(latitude), longitude: numberOrNull(longitude), mapAddress: mapAddress.trim() || null }
    return common
  }

  const saveMember = async () => {
    if (!canManage || !dimensionId || !companyId || !code.trim() || !name.trim()) return
    if (selectedCode === 'GEOGRAPHY' && locationType !== 'Province' && !parentId) { setError('برای شهر، روستا و سایر سطوح جغرافیایی انتخاب موقعیت بالادست الزامی است.'); return }
    setSaving(true); setError(''); setMessage('')
    const metadataJson = JSON.stringify(buildMetadata())
    try {
      if (editingId) {
        const current = members.find(x => x.id === editingId)
        await api.put(`/master-data/members/${editingId}`, { parentId: parentId || null, name: name.trim(), externalKey: externalKey.trim() || null, metadataJson, isActive: current?.isActive ?? true })
      } else {
        await api.post('/master-data/members', { dimensionId, parentId: parentId || null, companyId: scope === 'global' ? null : companyId, code: code.trim().toUpperCase(), name: name.trim(), externalKey: externalKey.trim() || null, metadataJson })
      }
      const verb = editingId ? 'ویرایش' : 'ثبت'
      resetForm(); await loadMembers(dimensionId); await loadRelated(dimensions)
      setMessage(`${labels[selectedCode] ?? selectedDimension?.name ?? 'داده پایه'} با موفقیت ${verb} شد.`)
    } catch (requestError) { setError(apiError(requestError, 'ذخیره اطلاعات پایه ناموفق بود.')) }
    finally { setSaving(false) }
  }

  const startEdit = (member: Member) => {
    const meta = parseMetadata(member.metadataJson)
    setEditingId(member.id); setCode(member.code); setName(member.name); setExternalKey(member.externalKey ?? ''); setParentId(member.parentId ?? ''); setScope(member.companyId ? 'company' : 'global')
    setNameEn(text(meta, 'nameEn')); setBrandMemberId(text(meta, 'brandMemberId')); setUomMemberId(text(meta, 'uomMemberId')); setCountryMemberId(text(meta, 'countryMemberId')); setGeographyMemberId(text(meta, 'geographyMemberId'))
    setSpecifications(text(meta, 'specifications')); setSymbol(text(meta, 'symbol')); setTaxId(text(meta, 'taxId')); setAddress(text(meta, 'address')); setIso2(text(meta, 'iso2')); setIso3(text(meta, 'iso3')); setIsDomestic(bool(meta, 'isDomestic'))
    setLocationType(text(meta, 'locationType') || 'Province'); setLatitude(text(meta, 'latitude')); setLongitude(text(meta, 'longitude')); setMapAddress(text(meta, 'mapAddress'))
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }

  const toggleActive = async (member: Member) => {
    if (!canManage) return
    setSaving(true); setError(''); setMessage('')
    try {
      await api.put(`/master-data/members/${member.id}`, { parentId: member.parentId ?? null, name: member.name, externalKey: member.externalKey ?? null, metadataJson: member.metadataJson ?? null, isActive: !member.isActive })
      await loadMembers(dimensionId); await loadRelated(dimensions)
      setMessage(member.isActive ? 'عضو غیرفعال شد.' : 'عضو دوباره فعال شد.')
    } catch (requestError) { setError(apiError(requestError, 'تغییر وضعیت اطلاعات پایه ناموفق بود.')) }
    finally { setSaving(false) }
  }

  const metadataSummary = (member: Member) => {
    const meta = parseMetadata(member.metadataJson)
    const parts: string[] = []
    if (text(meta, 'nameEn')) parts.push(text(meta, 'nameEn'))
    if (selectedCode === 'PRODUCT') {
      const brand = memberById.get(text(meta, 'brandMemberId')); const uom = memberById.get(text(meta, 'uomMemberId'))
      if (brand) parts.push(`برند: ${brand.name}`); if (uom) parts.push(`واحد: ${uom.name}`)
    }
    if (selectedCode === 'COUNTRY') { if (text(meta, 'iso2')) parts.push(`ISO: ${text(meta, 'iso2')}`); parts.push(bool(meta, 'isDomestic') ? 'داخلی' : 'خارجی') }
    if (selectedCode === 'GEOGRAPHY' && text(meta, 'locationType')) parts.push(text(meta, 'locationType'))
    if ((selectedCode === 'WAREHOUSE' || selectedCode === 'CUSTOMS') && text(meta, 'address')) parts.push(text(meta, 'address'))
    return parts.join(' • ') || '—'
  }

  const parentName = (member: Member) => member.parentId ? members.find(x => x.id === member.parentId)?.name ?? '—' : '—'
  const codePlaceholder = selectedCode === 'PRODUCT' ? 'ITEM-001' : selectedCode === 'BRAND' ? 'BRAND-001' : selectedCode === 'UOM' ? 'PCS' : selectedCode === 'COUNTRY' ? 'IR' : selectedCode === 'WAREHOUSE' ? 'WH-01' : selectedCode === 'CUSTOMS' ? 'CUS-01' : 'CODE-001'

  return <Card elevation={0}>
    <CardContent>
      <Stack direction={{ xs: 'column', lg: 'row' }} justifyContent="space-between" spacing={2} alignItems={{ lg: 'center' }} mb={2}>
        <Box><Stack direction="row" spacing={1} alignItems="center"><Inventory2RoundedIcon color="primary" /><Typography variant="h6" fontWeight={900}>کدینگ و موجودیت‌های عملیاتی</Typography></Stack><Typography color="text.secondary" mt={0.7}>کالا، برند، واحد سنجش، تأمین‌کننده، کشور و جغرافیا، انبار و گمرک با مشخصات ساخت‌یافته و قابل اتصال به ERP مدیریت می‌شوند.</Typography></Box>
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap><Button variant={showSecondary ? 'contained' : 'outlined'} onClick={() => setShowSecondary(x => !x)}>{showSecondary ? 'فقط اطلاعات عملیاتی اصلی' : 'نمایش کدینگ‌های تکمیلی'}</Button><Button startIcon={<RefreshRoundedIcon />} onClick={() => void loadDimensions()} disabled={busy || saving}>بازخوانی</Button></Stack>
      </Stack>

      {error && <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError('')}>{error}</Alert>}
      {message && <Alert severity="success" sx={{ mb: 2 }} onClose={() => setMessage('')}>{message}</Alert>}

      {!lockedDimensionCode && <FormControl size="small" sx={{ minWidth: 320, mb: 2 }}><InputLabel>نوع اطلاعات پایه</InputLabel><Select label="نوع اطلاعات پایه" value={dimensionId} onChange={e => setDimensionId(e.target.value)}>{visibleDimensions.map(item => <MenuItem key={item.id} value={item.id}>{labels[item.code.toUpperCase()] ?? item.name}</MenuItem>)}</Select></FormControl>}

      {canManage && selectedDimension && <Card variant="outlined" sx={{ mb: 2 }}><CardContent>
        <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={1} mb={1.5}><Typography fontWeight={900}>{editingId ? `ویرایش ${labels[selectedCode] ?? selectedDimension.name}` : `افزودن ${labels[selectedCode] ?? selectedDimension.name}`}</Typography>{editingId && <Button size="small" onClick={resetForm}>انصراف از ویرایش</Button>}</Stack>
        <Stack spacing={1.5}>
          <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.2}>
            <TextField size="small" label="کد *" value={code} onChange={e => setCode(e.target.value.toUpperCase())} placeholder={codePlaceholder} disabled={!!editingId} sx={{ minWidth: 180, direction: 'ltr' }} />
            <TextField size="small" label="نام فارسی *" value={name} onChange={e => setName(e.target.value)} sx={{ minWidth: 240 }} />
            <TextField size="small" label="نام انگلیسی" value={nameEn} onChange={e => setNameEn(e.target.value)} sx={{ minWidth: 220, direction: 'ltr' }} />
            <TextField size="small" label="کد / کلید ERP" value={externalKey} onChange={e => setExternalKey(e.target.value)} sx={{ minWidth: 190, direction: 'ltr' }} />
            {!editingId && canGlobal && <FormControl size="small" sx={{ minWidth: 170 }}><InputLabel>دامنه</InputLabel><Select label="دامنه" value={scope} onChange={e => setScope(e.target.value as Scope)}><MenuItem value="company">شرکت جاری</MenuItem><MenuItem value="global">سراسری Tenant</MenuItem></Select></FormControl>}
          </Stack>

          {selectedCode === 'PRODUCT' && <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.2}><FormControl size="small" sx={{ minWidth: 230 }}><InputLabel>برند</InputLabel><Select value={brandMemberId} label="برند" onChange={e => setBrandMemberId(e.target.value)}><MenuItem value="">انتخاب کنید</MenuItem>{brands.map(x => <MenuItem key={x.id} value={x.id}>{x.name} — {x.code}</MenuItem>)}</Select></FormControl><FormControl size="small" sx={{ minWidth: 230 }}><InputLabel>واحد سنجش</InputLabel><Select value={uomMemberId} label="واحد سنجش" onChange={e => setUomMemberId(e.target.value)}><MenuItem value="">انتخاب کنید</MenuItem>{uoms.map(x => <MenuItem key={x.id} value={x.id}>{x.name} — {x.code}</MenuItem>)}</Select></FormControl><TextField size="small" fullWidth label="مشخصات / توضیحات کالا" value={specifications} onChange={e => setSpecifications(e.target.value)} /></Stack>}
          {selectedCode === 'UOM' && <TextField size="small" label="نماد واحد" value={symbol} onChange={e => setSymbol(e.target.value)} placeholder="pcs / kg / pair" sx={{ maxWidth: 260, direction: 'ltr' }} />}
          {selectedCode === 'SUPPLIER' && <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.2}><FormControl size="small" sx={{ minWidth: 240 }}><InputLabel>کشور</InputLabel><Select value={countryMemberId} label="کشور" onChange={e => setCountryMemberId(e.target.value)}><MenuItem value="">انتخاب کنید</MenuItem>{countries.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl><TextField size="small" label="شناسه مالیاتی / ثبتی" value={taxId} onChange={e => setTaxId(e.target.value)} sx={{ minWidth: 220 }} /><TextField size="small" label="آدرس" value={address} onChange={e => setAddress(e.target.value)} fullWidth /></Stack>}
          {selectedCode === 'COUNTRY' && <><Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.2}><TextField size="small" label="کد ISO2" value={iso2} onChange={e => setIso2(e.target.value.toUpperCase())} inputProps={{ maxLength: 2 }} sx={{ direction: 'ltr' }} /><TextField size="small" label="کد ISO3" value={iso3} onChange={e => setIso3(e.target.value.toUpperCase())} inputProps={{ maxLength: 3 }} sx={{ direction: 'ltr' }} /><FormControlLabel control={<Switch checked={isDomestic} onChange={e => setIsDomestic(e.target.checked)} />} label="کشور داخلی" /><TextField size="small" label="Latitude" value={latitude} onChange={e => setLatitude(e.target.value)} sx={{ direction: 'ltr' }} /><TextField size="small" label="Longitude" value={longitude} onChange={e => setLongitude(e.target.value)} sx={{ direction: 'ltr' }} /></Stack><TextField size="small" label="نام/آدرس قابل جستجو روی نقشه" value={mapAddress} onChange={e => setMapAddress(e.target.value)} placeholder="Iran" fullWidth sx={{ direction: 'ltr' }} /></>}
          {selectedCode === 'GEOGRAPHY' && <><Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.2}><FormControl size="small" sx={{ minWidth: 190 }}><InputLabel>نوع موقعیت</InputLabel><Select value={locationType} label="نوع موقعیت" onChange={e => { setLocationType(e.target.value); if (e.target.value === 'Province') setParentId('') }}><MenuItem value="Province">استان / ایالت</MenuItem><MenuItem value="County">شهرستان</MenuItem><MenuItem value="City">شهر</MenuItem><MenuItem value="District">بخش / ناحیه</MenuItem><MenuItem value="RuralDistrict">دهستان</MenuItem><MenuItem value="Village">روستا</MenuItem><MenuItem value="Other">سایر</MenuItem></Select></FormControl><FormControl size="small" sx={{ minWidth: 230 }}><InputLabel>کشور</InputLabel><Select value={countryMemberId} label="کشور" onChange={e => setCountryMemberId(e.target.value)}><MenuItem value="">انتخاب کنید</MenuItem>{countries.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl><FormControl size="small" sx={{ minWidth: 250 }} disabled={locationType === 'Province'}><InputLabel>موقعیت بالادست</InputLabel><Select value={parentId} label="موقعیت بالادست" onChange={e => setParentId(e.target.value)}><MenuItem value="">انتخاب کنید</MenuItem>{parents.map(x => <MenuItem key={x.id} value={x.id}>{x.name} — {x.code}</MenuItem>)}</Select></FormControl></Stack><Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.2}><TextField size="small" label="Latitude" value={latitude} onChange={e => setLatitude(e.target.value)} sx={{ direction: 'ltr' }} /><TextField size="small" label="Longitude" value={longitude} onChange={e => setLongitude(e.target.value)} sx={{ direction: 'ltr' }} /><TextField size="small" label="نام/آدرس انگلیسی قابل جستجو روی نقشه" value={mapAddress} onChange={e => setMapAddress(e.target.value)} fullWidth sx={{ direction: 'ltr' }} /></Stack></>}
          {(selectedCode === 'WAREHOUSE' || selectedCode === 'CUSTOMS') && <><Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.2}>{selectedCode === 'CUSTOMS' && <FormControl size="small" sx={{ minWidth: 220 }}><InputLabel>کشور</InputLabel><Select value={countryMemberId} label="کشور" onChange={e => setCountryMemberId(e.target.value)}><MenuItem value="">انتخاب کنید</MenuItem>{countries.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl>}<FormControl size="small" sx={{ minWidth: 260 }}><InputLabel>موقعیت جغرافیایی</InputLabel><Select value={geographyMemberId} label="موقعیت جغرافیایی" onChange={e => setGeographyMemberId(e.target.value)}><MenuItem value="">انتخاب کنید</MenuItem>{geographies.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl><TextField size="small" label="آدرس" value={address} onChange={e => setAddress(e.target.value)} fullWidth /></Stack><Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.2}><TextField size="small" label="Latitude" value={latitude} onChange={e => setLatitude(e.target.value)} sx={{ direction: 'ltr' }} /><TextField size="small" label="Longitude" value={longitude} onChange={e => setLongitude(e.target.value)} sx={{ direction: 'ltr' }} /><TextField size="small" label="آدرس انگلیسی قابل جستجو روی نقشه" value={mapAddress} onChange={e => setMapAddress(e.target.value)} fullWidth sx={{ direction: 'ltr' }} /></Stack></>}
          {selectedDimension.isHierarchical && selectedCode !== 'GEOGRAPHY' && !['PRODUCT', 'BRAND', 'SUPPLIER', 'WAREHOUSE', 'CUSTOMS'].includes(selectedCode) && <FormControl size="small" sx={{ maxWidth: 320 }}><InputLabel>عضو بالادست</InputLabel><Select value={parentId} label="عضو بالادست" onChange={e => setParentId(e.target.value)}><MenuItem value="">بدون بالادست</MenuItem>{parents.map(x => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}</Select></FormControl>}

          <Stack direction="row" spacing={1}><Button variant="contained" startIcon={editingId ? <EditRoundedIcon /> : <AddRoundedIcon />} onClick={() => void saveMember()} disabled={saving || busy || !code.trim() || !name.trim()}>{editingId ? 'ذخیره تغییرات' : 'ثبت'}</Button>{saving && <CircularProgress size={24} />}</Stack>
        </Stack>
      </CardContent></Card>}

      {!canManage && <Alert severity="info" sx={{ mb: 2 }}>ثبت و تغییر اطلاعات پایه برای مدیر سامانه یا مدیر بودجه فعال است.</Alert>}
      {selectedCode === 'GEOGRAPHY' && <Alert severity="info" sx={{ mb: 2 }}>برای ایران می‌توانید ساختار «استان ← شهر ← روستا» را با نام فارسی و انگلیسی ایجاد کنید. کاتالوگ کامل استان‌ها و شهرهای ایران در مرحله داده‌گذاری مرجع اضافه خواهد شد.</Alert>}

      {busy && !members.length ? <Box py={4} textAlign="center"><CircularProgress size={28} /></Box> : <TableContainer sx={{ maxHeight: 520 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>کد</TableCell><TableCell>نام</TableCell>{selectedDimension?.isHierarchical && <TableCell>بالادست</TableCell>}<TableCell>مشخصات</TableCell><TableCell>کلید ERP</TableCell><TableCell>دامنه</TableCell><TableCell>وضعیت</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>
        {members.map(member => <TableRow key={member.id} hover sx={{ opacity: member.isActive ? 1 : .55 }}><TableCell sx={{ direction: 'ltr', fontFamily: 'monospace' }}>{member.code}</TableCell><TableCell><Typography fontWeight={800}>{member.name}</Typography></TableCell>{selectedDimension?.isHierarchical && <TableCell>{parentName(member)}</TableCell>}<TableCell>{metadataSummary(member)}</TableCell><TableCell sx={{ direction: 'ltr' }}>{member.externalKey || '—'}</TableCell><TableCell>{member.companyId ? 'شرکت جاری' : 'سراسری'}</TableCell><TableCell><Chip size="small" label={member.isActive ? 'فعال' : 'غیرفعال'} color={member.isActive ? 'success' : 'default'} variant="outlined" /></TableCell><TableCell><Stack direction="row" spacing={.5}><Button size="small" startIcon={<EditRoundedIcon />} onClick={() => startEdit(member)} disabled={!canManage || saving}>ویرایش</Button><Button size="small" onClick={() => void toggleActive(member)} disabled={!canManage || saving}>{member.isActive ? 'غیرفعال' : 'فعال'}</Button></Stack></TableCell></TableRow>)}
        {!members.length && !busy && <TableRow><TableCell colSpan={selectedDimension?.isHierarchical ? 8 : 7} align="center" sx={{ py: 4, color: 'text.secondary' }}>هنوز موردی برای {labels[selectedCode] ?? selectedDimension?.name ?? 'این بخش'} ثبت نشده است.</TableCell></TableRow>}
      </TableBody></Table></TableContainer>}
    </CardContent>
  </Card>
}
