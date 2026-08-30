import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Divider, FormControl, InputLabel, MenuItem, Select,
  Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography
} from '@mui/material'
import { api } from './api'

type Currency = { id: string; code: string; name: string; symbol?: string; isBaseCurrency: boolean }
type Source = { id: string; code: string; name: string }
type FxRate = { id: string; sourceId: string; sourceName: string; fromCurrencyId: string; fromCurrencyCode: string; toCurrencyId: string; toCurrencyCode: string; rateDate: string; rate: number; note?: string }

const faNumber = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 6 })

export default function CurrencyAdmin({ roles }: { roles: string[] }) {
  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const canEdit = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || roleSet.has('CFO') || roleSet.has('BUDGET_MANAGER')
  const [currencies, setCurrencies] = useState<Currency[]>([])
  const [sources, setSources] = useState<Source[]>([])
  const [rates, setRates] = useState<FxRate[]>([])
  const [sourceId, setSourceId] = useState('')
  const [fromCurrencyId, setFromCurrencyId] = useState('')
  const [toCurrencyId, setToCurrencyId] = useState('')
  const [rate, setRate] = useState(0)
  const [rateDate, setRateDate] = useState(new Date().toISOString().slice(0, 10))
  const [error, setError] = useState('')

  const reload = async () => {
    setError('')
    try {
      const [currencyResponse, sourceResponse, rateResponse] = await Promise.all([
        api.get<Currency[]>('/reference/currencies'),
        api.get<Source[]>('/reference/fx-rate-sources'),
        api.get<FxRate[]>('/reference/fx-rates')
      ])
      setCurrencies(currencyResponse.data)
      setSources(sourceResponse.data)
      setRates(rateResponse.data)
      const base = currencyResponse.data.find(x => x.isBaseCurrency)
      setSourceId(x => x || sourceResponse.data[0]?.id || '')
      setFromCurrencyId(x => x || currencyResponse.data.find(c => !c.isBaseCurrency)?.id || '')
      setToCurrencyId(x => x || base?.id || '')
    } catch { setError('دریافت ارزها و نرخ‌های ارز ناموفق بود.') }
  }

  useEffect(() => { void reload() }, [])

  const saveRate = async () => {
    if (!canEdit || !sourceId || !fromCurrencyId || !toCurrencyId || rate <= 0) return
    setError('')
    try {
      await api.post('/reference/fx-rates', { id: null, sourceId, fromCurrencyId, toCurrencyId, rateDate, rate, note: null })
      setRate(0)
      await reload()
    } catch (e: any) { setError(e?.response?.data?.detail ?? 'ثبت نرخ ارز ناموفق بود.') }
  }

  const selectedFrom = useMemo(() => currencies.find(x => x.id === fromCurrencyId), [currencies, fromCurrencyId])
  const selectedTo = useMemo(() => currencies.find(x => x.id === toCurrencyId), [currencies, toCurrencyId])

  return <Stack spacing={2.5}>
    {error && <Alert severity="error">{error}</Alert>}
    <Alert severity="info">تعریف کد ارز جزو اطلاعات پایه عملیاتی است. نرخ تبدیل نیز در همین بخش نگهداری می‌شود تا خرید، فروش و گزارش‌های مالی از یک مرجع واحد استفاده کنند.</Alert>
    {canEdit ? <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>ثبت نرخ ارز</Typography>
      <Typography color="text.secondary" mb={2}>چند منبع نرخ مستقل قابل نگهداری است؛ تاریخ در دیتابیس میلادی ذخیره و در نماهای کاربری به تقویم فارسی نمایش داده می‌شود.</Typography>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.5}>
        <FormControl size="small" sx={{ minWidth: 190 }}><InputLabel>منبع نرخ</InputLabel><Select label="منبع نرخ" value={sourceId} onChange={e => setSourceId(e.target.value)}>{sources.map(x => <MenuItem value={x.id} key={x.id}>{x.name}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 160 }}><InputLabel>از ارز</InputLabel><Select label="از ارز" value={fromCurrencyId} onChange={e => setFromCurrencyId(e.target.value)}>{currencies.map(x => <MenuItem value={x.id} key={x.id}>{x.code} — {x.name}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 160 }}><InputLabel>به ارز</InputLabel><Select label="به ارز" value={toCurrencyId} onChange={e => setToCurrencyId(e.target.value)}>{currencies.map(x => <MenuItem value={x.id} key={x.id}>{x.code} — {x.name}</MenuItem>)}</Select></FormControl>
        <TextField size="small" type="date" label="تاریخ" InputLabelProps={{ shrink: true }} value={rateDate} onChange={e => setRateDate(e.target.value)} />
        <TextField size="small" type="number" label={`نرخ ${selectedFrom?.code ?? ''}/${selectedTo?.code ?? ''}`} value={rate} onChange={e => setRate(Number(e.target.value))} />
        <Button variant="contained" onClick={saveRate} disabled={!sourceId || !fromCurrencyId || !toCurrencyId || rate <= 0}>ثبت نرخ</Button>
      </Stack>
    </CardContent></Card> : <Alert severity="info">ثبت و تغییر نرخ ارز فقط برای مدیر سامانه، مدیر مالی یا مدیر بودجه فعال است.</Alert>}

    <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <Box p={2.5}><Typography variant="h6" fontWeight={900}>نرخ‌های ثبت‌شده</Typography></Box><Divider />
      <TableContainer><Table size="small"><TableHead><TableRow><TableCell>تاریخ</TableCell><TableCell>منبع</TableCell><TableCell>از</TableCell><TableCell>به</TableCell><TableCell align="left">نرخ</TableCell></TableRow></TableHead><TableBody>
        {rates.map(x => <TableRow key={x.id}><TableCell>{new Intl.DateTimeFormat('fa-IR-u-ca-persian').format(new Date(x.rateDate))}</TableCell><TableCell>{x.sourceName}</TableCell><TableCell>{x.fromCurrencyCode}</TableCell><TableCell>{x.toCurrencyCode}</TableCell><TableCell align="left">{faNumber.format(x.rate)}</TableCell></TableRow>)}
      </TableBody></Table></TableContainer>
    </CardContent></Card>
  </Stack>
}
