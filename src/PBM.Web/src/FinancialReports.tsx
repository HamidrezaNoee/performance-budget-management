import { useEffect, useMemo, useState } from 'react'
import { Alert, Box, Card, CardContent, Chip, FormControl, InputLabel, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import { Bar, CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from './api'

type Cell = { periodId: string; periodName: string; sequence: number; value: number }
type Row = { code: string; name: string; displayOrder: number; periods: Cell[]; total: number }
type Report = { type: number; companyId: string; fiscalYearId: string; versionId?: string; versionName?: string; valueKind: number; rows: Row[] }
type Comparison = { budget: Report; actual: Report; forecast: Report }

const nf = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 0 })
const pf = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 1 })
const reportNames = ['صورت سود و زیان', 'ترازنامه', 'جریان نقدی']
const pnlComparisonCodes = ['GROSS_SALES','SALES_DISCOUNT','SALES_RETURN','NET_SALES','COGS','TOTAL_COGS','GROSS_PROFIT','ADMIN_EXPENSE','OTHER_OPERATING_NET','OPERATING_PROFIT','FINANCE_COST','OTHER_NON_OPERATING_NET','PROFIT_BEFORE_TAX','TAX','NET_PROFIT']

function requestError(error: unknown) {
  if (typeof error === 'object' && error !== null && 'response' in error)
    return (error as { response?: { data?: { detail?: string } } }).response?.data?.detail ?? 'دریافت گزارش مالی ناموفق بود.'
  return 'دریافت گزارش مالی ناموفق بود.'
}

export default function FinancialReports({ companyId, fiscalYearId }: { companyId: string; fiscalYearId: string }) {
  const [type, setType] = useState(0)
  const [valueKind, setValueKind] = useState(0)
  const [report, setReport] = useState<Report | null>(null)
  const [comparison, setComparison] = useState<Comparison | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!companyId || !fiscalYearId) return
    setError('')
    api.get<Report>('/reports/financial', { params: { companyId, fiscalYearId, type, valueKind } })
      .then(r => setReport(r.data))
      .catch(e => { setReport(null); setError(requestError(e)) })
  }, [companyId, fiscalYearId, type, valueKind])

  useEffect(() => {
    let active = true
    if (!companyId || !fiscalYearId || type !== 0) { setComparison(null); return () => { active = false } }
    Promise.all([
      api.get<Report>('/reports/financial', { params: { companyId, fiscalYearId, type: 0, valueKind: 0 } }),
      api.get<Report>('/reports/financial', { params: { companyId, fiscalYearId, type: 0, valueKind: 1 } }),
      api.get<Report>('/reports/financial', { params: { companyId, fiscalYearId, type: 0, valueKind: 3 } })
    ]).then(([budget, actual, forecast]) => {
      if (active) setComparison({ budget: budget.data, actual: actual.data, forecast: forecast.data })
    }).catch(e => { if (active) { setComparison(null); setError(requestError(e)) } })
    return () => { active = false }
  }, [companyId, fiscalYearId, type])

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

  const comparisonRows = useMemo(() => {
    if (!comparison) return []
    const actualByCode = new Map(comparison.actual.rows.map(x => [x.code, x]))
    const forecastByCode = new Map(comparison.forecast.rows.map(x => [x.code, x]))
    return comparison.budget.rows.filter(x => pnlComparisonCodes.includes(x.code)).map(budget => {
      const actual = actualByCode.get(budget.code)?.total ?? 0
      const forecast = forecastByCode.get(budget.code)?.total ?? 0
      return {
        code: budget.code,
        name: budget.name,
        budget: budget.total,
        actual,
        forecast,
        actualVariance: actual - budget.total,
        actualVariancePercent: budget.total === 0 ? null : (actual - budget.total) / Math.abs(budget.total) * 100,
        forecastVariance: forecast - budget.total
      }
    })
  }, [comparison])

  const comparisonChart = useMemo(() => {
    if (!comparison?.budget.rows.length) return []
    const row = (reportItem: Report, code: string) => reportItem.rows.find(x => x.code === code)
    const budgetSales = row(comparison.budget, 'NET_SALES')
    const actualSales = row(comparison.actual, 'NET_SALES')
    const forecastSales = row(comparison.forecast, 'NET_SALES')
    const budgetProfit = row(comparison.budget, 'NET_PROFIT')
    const actualProfit = row(comparison.actual, 'NET_PROFIT')
    if (!budgetSales) return []
    return budgetSales.periods.map(period => ({
      period: period.periodName,
      budgetSales: period.value,
      actualSales: actualSales?.periods.find(x => x.periodId === period.periodId)?.value ?? 0,
      forecastSales: forecastSales?.periods.find(x => x.periodId === period.periodId)?.value ?? 0,
      budgetNetProfit: budgetProfit?.periods.find(x => x.periodId === period.periodId)?.value ?? 0,
      actualNetProfit: actualProfit?.periods.find(x => x.periodId === period.periodId)?.value ?? 0
    }))
  }, [comparison])

  const netSalesTotal = report?.rows.find(x => x.code === 'NET_SALES')?.total ?? 0
  const structuralCodes = new Set(['NET_SALES','TOTAL_COGS','GROSS_PROFIT','ADMIN_EXPENSE','OTHER_OPERATING_NET','OPERATING_PROFIT','FINANCE_COST','OTHER_NON_OPERATING_NET','PROFIT_BEFORE_TAX','TAX','NET_PROFIT'])

  return <Stack spacing={2.5}>
    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} alignItems={{ md: 'center' }}>
        <Box flexGrow={1}><Typography variant="h6" fontWeight={900}>گزارش‌های مالی بودجه‌ای و عملکردی</Typography><Typography color="text.secondary">
          {type === 0 ? 'صورت سود و زیان از TRADE + EXPENSE ساخته می‌شود و نمای مقایسه Budget / Actual / Forecast نیز به‌صورت هم‌زمان محاسبه می‌شود.' : 'ترازنامه و جریان نقدی از مدل FINSTAT خوانده می‌شوند.'}
        </Typography></Box>
        <FormControl size="small" sx={{ minWidth: 190 }}><InputLabel>گزارش</InputLabel><Select value={type} label="گزارش" onChange={e => setType(Number(e.target.value))}><MenuItem value={0}>صورت سود و زیان</MenuItem><MenuItem value={1}>ترازنامه</MenuItem><MenuItem value={2}>جریان نقدی</MenuItem></Select></FormControl>
        <FormControl size="small" sx={{ minWidth: 150 }}><InputLabel>نوع مقدار</InputLabel><Select value={valueKind} label="نوع مقدار" onChange={e => setValueKind(Number(e.target.value))}><MenuItem value={0}>بودجه</MenuItem><MenuItem value={1}>عملکرد واقعی</MenuItem><MenuItem value={2}>تعهد</MenuItem><MenuItem value={3}>پیش‌بینی</MenuItem></Select></FormControl>
      </Stack>
    </CardContent></Card>
    {error && <Alert severity="error">{error}</Alert>}

    {type === 0 && comparison && <>
      <Card elevation={0}><CardContent>
        <Typography variant="h6" fontWeight={900} mb={.5}>مقایسه سالانه Budget / Actual / Forecast</Typography>
        <Typography color="text.secondary" variant="body2" mb={2}>انحراف Actual نسبت به بودجه برای کل زنجیره سود و زیان؛ از فروش تا سود خالص.</Typography>
        <TableContainer sx={{ maxHeight: 560 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>سرفصل</TableCell><TableCell align="left">Budget</TableCell><TableCell align="left">Actual</TableCell><TableCell align="left">Forecast</TableCell><TableCell align="left">انحراف Actual</TableCell><TableCell align="left">٪ انحراف</TableCell><TableCell align="left">انحراف Forecast</TableCell></TableRow></TableHead><TableBody>{comparisonRows.map(row => <TableRow hover key={row.code}><TableCell><Typography fontWeight={800}>{row.name}</Typography><Typography variant="caption" color="text.secondary">{row.code}</Typography></TableCell><TableCell align="left">{nf.format(row.budget)}</TableCell><TableCell align="left">{nf.format(row.actual)}</TableCell><TableCell align="left">{nf.format(row.forecast)}</TableCell><TableCell align="left"><Typography fontWeight={800}>{nf.format(row.actualVariance)}</Typography></TableCell><TableCell align="left">{row.actualVariancePercent === null ? '—' : `${pf.format(row.actualVariancePercent)}٪`}</TableCell><TableCell align="left">{nf.format(row.forecastVariance)}</TableCell></TableRow>)}</TableBody></Table></TableContainer>
      </CardContent></Card>
      {comparisonChart.length > 0 && <Card elevation={0}><CardContent><Typography variant="h6" fontWeight={900} mb={.5}>روند مقایسه‌ای فروش خالص و سود خالص</Typography><Typography color="text.secondary" variant="body2" mb={2}>فروش خالص Budget / Actual / Forecast با روند سود خالص Budget و Actual.</Typography><Box sx={{ height: 410, direction: 'ltr' }}><ResponsiveContainer width="100%" height="100%"><ComposedChart data={comparisonChart}><CartesianGrid strokeDasharray="3 3" vertical={false}/><XAxis dataKey="period"/><YAxis tickFormatter={v => nf.format(Number(v))}/><Tooltip formatter={(v: unknown) => nf.format(Number(v ?? 0))}/><Legend/><Bar dataKey="budgetSales" name="Budget فروش خالص" fill="#2563eb"/><Bar dataKey="actualSales" name="Actual فروش خالص" fill="#0f766e"/><Bar dataKey="forecastSales" name="Forecast فروش خالص" fill="#7c3aed"/><Line dataKey="budgetNetProfit" name="Budget سود خالص" stroke="#d97706" strokeWidth={2.4} dot={false}/><Line dataKey="actualNetProfit" name="Actual سود خالص" stroke="#be123c" strokeWidth={2.4} dot={false}/></ComposedChart></ResponsiveContainer></Box></CardContent></Card>}
    </>}

    {report && <>
      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap><Chip label={reportNames[type]} /><Chip variant="outlined" label={report.versionName ?? (type === 0 ? 'گزارش عملیاتی تجمیعی' : 'هنوز نسخه FINSTAT ایجاد نشده')} color={report.versionName ? 'success' : 'warning'} />{type === 0 && <Chip variant="outlined" label="ساختار شیت سود(زیان) اکسل" color="primary" />}</Stack>
      <Card elevation={0}><CardContent sx={{ p: 0 }}>
        <TableContainer sx={{ maxHeight: 650 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell sx={{ minWidth: 300, right: 0, zIndex: 4 }}>سرفصل</TableCell>{report.rows[0]?.periods.map(p => <TableCell key={p.periodId} align="center" sx={{ minWidth: 125 }}>{p.periodName}</TableCell>)}<TableCell align="center" sx={{ minWidth: 145 }}>کل سال / پایان دوره</TableCell>{type === 0 && <TableCell align="center" sx={{ minWidth: 105 }}>% فروش خالص</TableCell>}</TableRow></TableHead><TableBody>{report.rows.map(row => {
          const emphasized = type === 0 && structuralCodes.has(row.code)
          const ratio = type === 0 && netSalesTotal !== 0 ? row.total / netSalesTotal * 100 : null
          return <TableRow hover key={row.code} sx={emphasized ? { '& td': { fontWeight: 800, bgcolor: 'rgba(25,118,210,.035)' } } : undefined}><TableCell sx={{ position: 'sticky', right: 0, bgcolor: emphasized ? '#f8fbff' : '#fff', zIndex: 2 }}><Typography fontWeight={emphasized ? 900 : 700} variant="body2">{row.name}</Typography><Typography variant="caption" color="text.secondary">{row.code}</Typography></TableCell>{row.periods.map(p => <TableCell align="center" key={p.periodId}>{nf.format(p.value)}</TableCell>)}<TableCell align="center"><Typography fontWeight={900}>{nf.format(row.total)}</Typography></TableCell>{type === 0 && <TableCell align="center">{ratio === null ? '-' : `${pf.format(ratio)}٪`}</TableCell>}</TableRow>
        })}</TableBody></Table></TableContainer>
      </CardContent></Card>
      {chartData.length > 0 && <Card elevation={0}><CardContent><Typography variant="h6" fontWeight={900} mb={.5}>{type === 0 ? 'روند ماهانه گزارش انتخاب‌شده' : `روند اقلام کلیدی — ${reportNames[type]}`}</Typography>{type === 0 && <Typography color="text.secondary" variant="body2" mb={2}>نمای جزئی برای نوع مقدار انتخاب‌شده در بالای صفحه.</Typography>}<Box sx={{ height: 410, direction: 'ltr' }}><ResponsiveContainer width="100%" height="100%"><ComposedChart data={chartData}><CartesianGrid strokeDasharray="3 3" vertical={false} /><XAxis dataKey="period" /><YAxis tickFormatter={v => nf.format(Number(v))} /><Tooltip formatter={(v: unknown) => nf.format(Number(v ?? 0))} /><Legend />{Object.keys(chartData[0] ?? {}).filter(k => k !== 'period').map((key, index) => index < 2 ? <Bar key={key} dataKey={key} fill={index === 0 ? '#1d4ed8' : '#0f766e'} /> : <Line key={key} dataKey={key} stroke={['#7c3aed', '#d97706', '#be123c'][index - 2] ?? '#333'} strokeWidth={2.5} dot={false} />)}</ComposedChart></ResponsiveContainer></Box></CardContent></Card>}
    </>}
  </Stack>
}
