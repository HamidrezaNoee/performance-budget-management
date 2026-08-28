import { useEffect, useState } from 'react'
import { Alert, Box, Button, Card, CardContent, Chip, FormControl, InputLabel, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography } from '@mui/material'
import { api } from './api'

type Period = { id: string; fiscalYearId: string; sequence: number; code: string; name: string; jalaliMonth: number; startDate: string; endDate: string; isClosed: boolean }
type FiscalYear = { id: string; companyId: string; code: string; name: string; jalaliYear: number; startDate: string; endDate: string; isClosed: boolean; periods: Period[] }

const months = ['فروردین', 'اردیبهشت', 'خرداد', 'تیر', 'مرداد', 'شهریور', 'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند']
const faDate = new Intl.DateTimeFormat('fa-IR-u-ca-persian', { year: 'numeric', month: '2-digit', day: '2-digit' })
const currentJalaliYear = () => new Intl.DateTimeFormat('fa-IR-u-ca-persian-nu-latn', { year: 'numeric' }).format(new Date())

export default function FiscalCalendarAdmin({ companyId }: { companyId: string }) {
  const [years, setYears] = useState<FiscalYear[]>([])
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [jalaliYear, setJalaliYear] = useState(currentJalaliYear())
  const [startMonth, setStartMonth] = useState(1)
  const [monthCount, setMonthCount] = useState(12)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const reload = async () => {
    if (!companyId) return
    setError('')
    try { const { data } = await api.get<FiscalYear[]>('/admin/fiscal-calendar/years', { params: { companyId } }); setYears(data) }
    catch { setError('دریافت تقویم مالی ناموفق بود.') }
  }
  useEffect(() => { reload() }, [companyId])

  const createYear = async () => {
    const year = Number(jalaliYear)
    if (!code.trim() || !name.trim() || !Number.isInteger(year)) { setError('کد، نام و سال شمسی الزامی است.'); return }
    setBusy(true); setError('')
    try {
      await api.post('/admin/fiscal-calendar/years', { companyId, code: code.trim(), name: name.trim(), jalaliYear: year, startJalaliMonth: startMonth, monthCount })
      setCode(''); setName(''); await reload()
    } catch { setError('ایجاد سال مالی ناموفق بود. کد تکراری یا بازه نامعتبر را بررسی کنید.') }
    finally { setBusy(false) }
  }

  const setYearClosed = async (year: FiscalYear, isClosed: boolean) => {
    if (!window.confirm(isClosed ? `سال مالی «${year.name}» و تمام دوره‌های آن بسته شود؟` : `سال مالی «${year.name}» دوباره باز شود؟`)) return
    setBusy(true); setError('')
    try { await api.put(`/admin/fiscal-calendar/years/${year.id}/closed`, { isClosed }); await reload() }
    catch { setError('تغییر وضعیت سال مالی ناموفق بود.') }
    finally { setBusy(false) }
  }

  const setPeriodClosed = async (period: Period, isClosed: boolean) => {
    setBusy(true); setError('')
    try { await api.put(`/admin/fiscal-calendar/periods/${period.id}/closed`, { isClosed }); await reload() }
    catch { setError('تغییر وضعیت دوره مالی ناموفق بود.') }
    finally { setBusy(false) }
  }

  return <Stack spacing={2.5}>
    {error && <Alert severity="error">{error}</Alert>}
    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>تعریف سال مالی و دوره‌های شمسی</Typography>
      <Typography color="text.secondary" mb={2}>شروع سال مالی الزاماً فروردین نیست. برای نمونه می‌توانید یک سال ۱۲ماهه از دی تا آذر بسازید؛ تاریخ‌ها برای ذخیره‌سازی به میلادی تبدیل می‌شوند.</Typography>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.5}>
        <TextField size="small" label="کد سال مالی" value={code} onChange={e => setCode(e.target.value)} placeholder="FY1405-DAEY" />
        <TextField size="small" label="نام سال مالی" value={name} onChange={e => setName(e.target.value)} placeholder="سال مالی ۱۴۰۵" />
        <TextField size="small" type="number" label="سال شمسی شروع" value={jalaliYear} onChange={e => setJalaliYear(e.target.value)} />
        <FormControl size="small" sx={{ minWidth: 155 }}><InputLabel>ماه شروع</InputLabel><Select value={startMonth} label="ماه شروع" onChange={e => setStartMonth(Number(e.target.value))}>{months.map((m, i) => <MenuItem key={m} value={i + 1}>{m}</MenuItem>)}</Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 145 }}><InputLabel>تعداد ماه</InputLabel><Select value={monthCount} label="تعداد ماه" onChange={e => setMonthCount(Number(e.target.value))}><MenuItem value={3}>۳ ماه</MenuItem><MenuItem value={6}>۶ ماه</MenuItem><MenuItem value={9}>۹ ماه</MenuItem><MenuItem value={10}>۱۰ ماه</MenuItem><MenuItem value={12}>۱۲ ماه</MenuItem><MenuItem value={18}>۱۸ ماه</MenuItem><MenuItem value={24}>۲۴ ماه</MenuItem></Select></FormControl>
        <Button variant="contained" onClick={createYear} disabled={busy}>ایجاد تقویم مالی</Button>
      </Stack>
    </CardContent></Card>

    {years.map(year => <Card key={year.id} elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={1.5} mb={2}>
        <Box><Stack direction="row" spacing={1} alignItems="center"><Typography variant="h6" fontWeight={900}>{year.name}</Typography><Chip size="small" label={year.code} variant="outlined" /><Chip size="small" label={year.isClosed ? 'بسته' : 'باز'} color={year.isClosed ? 'default' : 'success'} /></Stack><Typography variant="body2" color="text.secondary" mt={.5}>{faDate.format(new Date(year.startDate))} تا {faDate.format(new Date(year.endDate))} — {year.periods.length.toLocaleString('fa-IR')} دوره</Typography></Box>
        <Button variant="outlined" color={year.isClosed ? 'primary' : 'warning'} onClick={() => setYearClosed(year, !year.isClosed)} disabled={busy}>{year.isClosed ? 'بازگشایی سال' : 'بستن سال'}</Button>
      </Stack>
      <TableContainer sx={{ border: '1px solid #e8eef5', borderRadius: 2 }}><Table size="small"><TableHead><TableRow><TableCell>ترتیب</TableCell><TableCell>کد</TableCell><TableCell>دوره</TableCell><TableCell>ماه شمسی</TableCell><TableCell>شروع</TableCell><TableCell>پایان</TableCell><TableCell>وضعیت</TableCell><TableCell>عملیات</TableCell></TableRow></TableHead><TableBody>{year.periods.map(period => <TableRow key={period.id}><TableCell>{period.sequence.toLocaleString('fa-IR')}</TableCell><TableCell>{period.code}</TableCell><TableCell><Typography fontWeight={800}>{period.name}</Typography></TableCell><TableCell>{months[period.jalaliMonth - 1] ?? period.jalaliMonth}</TableCell><TableCell>{faDate.format(new Date(period.startDate))}</TableCell><TableCell>{faDate.format(new Date(period.endDate))}</TableCell><TableCell><Chip size="small" label={period.isClosed ? 'بسته' : 'باز'} color={period.isClosed ? 'default' : 'success'} /></TableCell><TableCell><Button size="small" disabled={busy || year.isClosed} onClick={() => setPeriodClosed(period, !period.isClosed)}>{period.isClosed ? 'بازگشایی' : 'بستن'}</Button></TableCell></TableRow>)}</TableBody></Table></TableContainer>
    </CardContent></Card>)}

    {!years.length && <Card elevation={0}><CardContent sx={{ py: 6, textAlign: 'center' }}><Typography fontWeight={900}>برای این شرکت هنوز سال مالی تعریف نشده است.</Typography><Typography color="text.secondary" mt={1}>سال و ماه شروع را مشخص کنید تا دوره‌های ماهانه به‌صورت خودکار ساخته شوند.</Typography></CardContent></Card>}
  </Stack>
}
