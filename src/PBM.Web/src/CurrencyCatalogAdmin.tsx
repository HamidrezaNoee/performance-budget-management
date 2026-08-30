import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Button, Card, CardContent, Checkbox, FormControlLabel, Stack, Table, TableBody, TableCell,
  TableContainer, TableHead, TableRow, TextField, Typography
} from '@mui/material'
import EditRoundedIcon from '@mui/icons-material/EditRounded'
import { api } from './api'

type Currency = {
  id: string
  code: string
  name: string
  symbol?: string
  isBaseCurrency: boolean
  isActive: boolean
}

const emptyForm = { id: '', code: '', name: '', symbol: '', isBaseCurrency: false, isActive: true }

export default function CurrencyCatalogAdmin({ roles }: { roles: string[] }) {
  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const canEdit = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || roleSet.has('CFO') || roleSet.has('BUDGET_MANAGER')
  const [items, setItems] = useState<Currency[]>([])
  const [form, setForm] = useState(emptyForm)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const reload = async () => {
    setError('')
    try {
      const { data } = await api.get<Currency[]>('/reference/currency-catalog')
      setItems(data)
    } catch { setError('دریافت فهرست ارزها ناموفق بود.') }
  }

  useEffect(() => { void reload() }, [])

  const save = async () => {
    if (!canEdit || !form.code.trim() || !form.name.trim()) return
    setBusy(true); setError('')
    try {
      await api.post('/reference/currencies', {
        id: form.id || null,
        code: form.code.trim().toUpperCase(),
        name: form.name.trim(),
        symbol: form.symbol.trim() || null,
        isBaseCurrency: form.isBaseCurrency,
        isActive: form.isActive
      })
      setForm(emptyForm)
      await reload()
    } catch (e: any) {
      setError(e?.response?.data?.detail ?? 'ثبت ارز ناموفق بود.')
    } finally { setBusy(false) }
  }

  const edit = (item: Currency) => setForm({
    id: item.id,
    code: item.code,
    name: item.name,
    symbol: item.symbol ?? '',
    isBaseCurrency: item.isBaseCurrency,
    isActive: item.isActive
  })

  return <Stack spacing={2.5}>
    {error && <Alert severity="error">{error}</Alert>}
    <Alert severity="info">در این صفحه خودِ ارزها تعریف می‌شوند؛ مانند ریال ایران (IRR)، دلار آمریکا (USD)، یوان چین (CNY) و یورو (EUR). ثبت نرخ تبدیل در منوی «نرخ ارز» انجام می‌شود.</Alert>

    {canEdit ? <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>{form.id ? 'ویرایش ارز' : 'تعریف ارز جدید'}</Typography>
      <Typography color="text.secondary" mb={2}>کد ارز را به‌صورت ISO سه‌حرفی وارد کنید. فقط یک ارز می‌تواند به‌عنوان ارز پایه فعال باشد.</Typography>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} alignItems={{ md: 'center' }}>
        <TextField size="small" label="کد ISO" placeholder="IRR" value={form.code} inputProps={{ maxLength: 3, dir: 'ltr' }} onChange={e => setForm(x => ({ ...x, code: e.target.value.toUpperCase() }))} />
        <TextField size="small" label="نام ارز" placeholder="ریال ایران" value={form.name} onChange={e => setForm(x => ({ ...x, name: e.target.value }))} sx={{ minWidth: 220 }} />
        <TextField size="small" label="نماد" placeholder="﷼ / $ / ¥" value={form.symbol} onChange={e => setForm(x => ({ ...x, symbol: e.target.value }))} />
        <FormControlLabel control={<Checkbox checked={form.isBaseCurrency} onChange={e => setForm(x => ({ ...x, isBaseCurrency: e.target.checked }))} />} label="ارز پایه" />
        <FormControlLabel control={<Checkbox checked={form.isActive} onChange={e => setForm(x => ({ ...x, isActive: e.target.checked }))} />} label="فعال" />
        <Button variant="contained" disabled={busy || !form.code.trim() || !form.name.trim()} onClick={save}>{form.id ? 'ذخیره تغییرات' : 'ثبت ارز'}</Button>
        {form.id && <Button onClick={() => setForm(emptyForm)}>انصراف</Button>}
      </Stack>
    </CardContent></Card> : <Alert severity="info">تعریف و ویرایش ارز برای مدیر سامانه، مدیر مالی یا مدیر بودجه فعال است.</Alert>}

    <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <Stack p={2.5} spacing={.5}><Typography variant="h6" fontWeight={900}>ارزهای تعریف‌شده</Typography><Typography variant="body2" color="text.secondary">این فهرست مرجع انتخاب ارز در بودجه خرید، فروش و ثبت نرخ ارز است.</Typography></Stack>
      <TableContainer><Table size="small"><TableHead><TableRow><TableCell>کد ISO</TableCell><TableCell>نام ارز</TableCell><TableCell>نماد</TableCell><TableCell>ارز پایه</TableCell><TableCell>وضعیت</TableCell>{canEdit && <TableCell>عملیات</TableCell>}</TableRow></TableHead><TableBody>
        {items.map(item => <TableRow key={item.id}><TableCell dir="ltr">{item.code}</TableCell><TableCell>{item.name}</TableCell><TableCell>{item.symbol || '—'}</TableCell><TableCell>{item.isBaseCurrency ? 'بله' : 'خیر'}</TableCell><TableCell>{item.isActive ? 'فعال' : 'غیرفعال'}</TableCell>{canEdit && <TableCell><Button size="small" startIcon={<EditRoundedIcon />} onClick={() => edit(item)}>ویرایش</Button></TableCell>}</TableRow>)}
        {items.length === 0 && <TableRow><TableCell colSpan={canEdit ? 6 : 5} align="center">هنوز ارزی تعریف نشده است.</TableCell></TableRow>}
      </TableBody></Table></TableContainer>
    </CardContent></Card>
  </Stack>
}
