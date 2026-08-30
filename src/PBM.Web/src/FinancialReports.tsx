import { useEffect, useMemo, useState } from 'react'
import { Alert, Box, Card, CardContent, Chip, FormControl, InputLabel, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import { Bar, CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from './api'

type Cell = { periodId: string; periodName: string; sequence: number; value: number }
type Row = { code: string; name: string; displayOrder: number; periods: Cell[]; total: number }
type Report = { type: number; companyId: string; fiscalYearId: string; versionId?: string; versionName?: string; valueKind: number; rows: Row[] }

const nf = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 0 })
const pf = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 1 })
const reportNames = ['صورت سود و زیان', 'ترازنامه', 'جریان نقدی']

export default function FinancialReports({ companyId, fiscalYearId }: { companyId: string; fiscalYearId: string }) {
  const [type, setType] = useState(0)
  const [valueKind, setValueKind] = useState(0)
  const [report, setReport] = useState<Report | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!companyId || !fiscalYearId) return
    setError('')
    api.get<Report>('/reports/financial', { params: { companyId, fiscalYearId, type, valueKind } })
      .then(r => setReport(r.data))
      .catch((e: any) => { setReport(null); setError(e?.response?.data?.detail ?? 'دریافت گزارش مالی ناموفق بود.') })
  }, [companyId, fiscalYearId, type, valueKind])

  const chartData = useMemo(() => {
    if (!report?.rows.length) return []
    const keyRows = type === 0
      ? ['GROSS_SALES', 'NET_SALES', 'GROSS_PROFIT', 'OPERATING_PROFIT', 'NET_PROFIT']
      : type === 1 ? ['CURRENT_ASSETS', 'TOTAL_ASSETS', 'CURRENT_LIABILITIES', 'EQUITY']
      : ['CFO', 'CFI', 'CFF', 'ENDING_CASH']
    const map = new Map<string, Record<string, string | number>>()
    for (const row of report.rows.filter(x => keyRows.includes(x.code))) {
      for (const cell of row.periods) {
        const item = map.get(cell.periodId) ?? { period: cell.periodName }
        item[row.name] = cell.value
        map.set(cell.periodId, item)
      }
    }
    return [...map.values()]
  }, [report, type])

  const netSalesTotal = report?.rows.find(x => x.code === 'NET_SALES')?.total ?? 0
  const structuralCodes = new Set(['NET_SALES','TOTAL_COGS','GROSS_PROFIT','ADMIN_EXPENSE','OTHER_OPERATING_NET','OPERATING_PROFIT','FINANCE_COST','OTHER_NON_OPERATING_NET','PROFIT_BEFORE_TAX','TAX','NET_PROFIT'])

  return <Stack spacing={2.5}>
    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} alignItems={{ md: 'center' }}>
        <Box flexGrow={1}><Typography variant="h6" fontWeight={900}>گزارش‌های مالی بودجه‌ای و عملکردی</Typography><Typography color="text.secondary">
          {type === 0 ? 'صورت سود و زیان مطابق ساختار فایل بودجه: فروش و بهای تمام‌شده از TRADE و هزینه‌های عملیاتی، مالی و غیرعملیاتی از EXPENSE تجمیع می‌شوند.' : 'ترازنامه و جریان نقدی از مدل FINSTAT خوانده می‌شوند.'}
        </Typography></Box>
        <FormControl size="small" sx={{ minWidth: 190 }}><InputLabel>گزارش</InputLabel><Select value={type} label="گزارش" onChange={e => setType(Number(e.target.value))}><MenuItem value={0}>صورت سود و زیان</MenuItem><MenuItem value={1}>ترازنامه</MenuItem><MenuItem value={2}>جریان نقدی</MenuItem></Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 150 }}><InputLabel>نوع مقدار</InputLabel><Select value={valueKind} label="نوع مقدار" onChange={e => setValueKind(Number(e.target.value))}><MenuItem value={0}>بودجه</MenuItem><MenuItem value={1}>عملکرد واقعی</MenuItem><MenuItem value={2}>تعهد</MenuItem><MenuItem value={3}>پیش‌بینی</MenuItem></Select></FormControl>
      </Stack>
    </CardContent></Card>
    {error && <Alert severity="error">{error}</Alert>}
    {report && <>
      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap><Chip label={reportNames[type]} /><Chip variant="outlined" label={report.versionName ?? (type === 0 ? 'گزارش عملیاتی تجمیعی' : 'هنوز نسخه FINSTAT ایجاد نشده')} color={report.versionName ? 'success' : 'warning'} />{type === 0 && <Chip variant="outlined" label="ساختار شیت سود(زیان) اکسل" color="primary" />}</Stack>
      <Card elevation={0}><CardContent sx={{ p: 0 }}>
        <TableContainer sx={{ maxHeight: 650 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell sx={{ minWidth: 300, right: 0, zIndex: 4 }}>سرفصل</TableCell>{report.rows[0]?.periods.map(p => <TableCell key={p.periodId} align="center" sx={{ minWidth: 125 }}>{p.periodName}</TableCell>)}<TableCell align="center" sx={{ minWidth: 145 }}>کل سال / پایان دوره</TableCell>{type === 0 && <TableCell align="center" sx={{ minWidth: 105 }}>% فروش خالص</TableCell>}</TableRow></TableHead><TableBody>{report.rows.map(row => {
          const emphasized = type === 0 && structuralCodes.has(row.code)
          const ratio = type === 0 && netSalesTotal !== 0 ? row.total / netSalesTotal * 100 : null
          return <TableRow hover key={row.code} sx={emphasized ? { '& td': { fontWeight: 800, bgcolor: 'rgba(25,118,210,.035)' } } : undefined}><TableCell sx={{ position: 'sticky', right: 0, bgcolor: emphasized ? '#f8fbff' : '#fff', zIndex: 2 }}><Typography fontWeight={emphasized ? 900 : 700} variant="body2">{row.name}</Typography><Typography variant="caption" color="text.secondary">{row.code}</Typography></TableCell>{row.periods.map(p => <TableCell align="center" key={p.periodId}>{nf.format(p.value)}</TableCell>)}<TableCell align="center"><Typography fontWeight={900}>{nf.format(row.total)}</Typography></TableCell>{type === 0 && <TableCell align="center">{ratio === null ? '-' : `${pf.format(ratio)}٪`}</TableCell>}</TableRow>
        })}</TableBody></Table></TableContainer>
      </CardContent></Card>
      {chartData.length > 0 && <Card elevation={0}><CardContent><Typography variant="h6" fontWeight={900} mb={.5}>{type === 0 ? 'روند ماهانه فروش و سودآوری' : `روند اقلام کلیدی — ${reportNames[type]}`}</Typography>{type === 0 && <Typography color="text.secondary" variant="body2" mb={2}>فروش ناخالص و خالص در کنار سود ناخالص، سود عملیاتی و سود خالص؛ همان سرفصل‌های گزارش جدولی بالا.</Typography>}<Box sx={{ height: 410, direction: 'ltr' }}><ResponsiveContainer width="100%" height="100%"><ComposedChart data={chartData}><CartesianGrid strokeDasharray="3 3" vertical={false} /><XAxis dataKey="period" /><YAxis tickFormatter={v => nf.format(Number(v))} /><Tooltip formatter={v => nf.format(Number(v))} /><Legend />{Object.keys(chartData[0] ?? {}).filter(k => k !== 'period').map((key, index) => index < 2 ? <Bar key={key} dataKey={key} fill={index === 0 ? '#1d4ed8' : '#0f766e'} /> : <Line key={key} dataKey={key} stroke={['#7c3aed', '#d97706', '#be123c'][index - 2] ?? '#333'} strokeWidth={2.5} dot={false} />)}</ComposedChart></ResponsiveContainer></Box></CardContent></Card>}
    </>}
  </Stack>
}
