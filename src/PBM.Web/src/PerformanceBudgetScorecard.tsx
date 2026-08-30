import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Box, Button, Card, CardContent, Chip, CircularProgress, FormControl, InputLabel,
  MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead,
  TableRow, Typography
} from '@mui/material'
import { api } from './api'

type Version = { id: string; scenarioId: string; versionNumber: number; name: string; status: number; isLocked: boolean }
type Plan = { id: string; budgetModelId: string; name: string; versions: Version[] }
type CurrencySummary = {
  currencyCode: string
  annualBudget: number
  annualActual: number
  annualCommitment: number
  annualForecast: number
  ytdBudget: number
  ytdActual: number
  ytdCommitment: number
  ytdForecast: number
  ytdUtilizationPercent?: number | null
  ytdExposurePercent?: number | null
  annualForecastPercent?: number | null
}
type KpiComponent = {
  kpiId: string
  code: string
  name: string
  scoreMode: string
  weight: number
  observationCount: number
  averageScore: number
  latestScore: number
  latestIsOnTarget: boolean
}
type ObjectiveComponent = {
  objectiveId: string
  code: string
  name: string
  strategicWeight: number
  linkedKpiCount: number
  observedKpiCount: number
  dataCoveragePercent: number
  score?: number | null
}
type Scorecard = {
  versionId: string
  companyId: string
  fiscalYearId: string
  budgetModelId: string
  versionName: string
  versionNumber: number
  scenarioName: string
  fiscalYearName: string
  selectedMeasureCode: string
  selectedMeasureName: string
  elapsedPeriods: number
  totalPeriods: number
  dataCoveragePercent: number
  weightedKpiScore?: number | null
  strategyCoveragePercent: number
  strategyWeightedScore?: number | null
  recommendationScore?: number | null
  recommendation: number
  reasons: string[]
  currencies: CurrencySummary[]
  kpis: KpiComponent[]
  objectives: ObjectiveComponent[]
}

const number = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })
const amount = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 0 })
const recommendations = [
  { label: 'داده ناکافی', color: 'default' as const },
  { label: 'حفظ سطح تخصیص', color: 'success' as const },
  { label: 'اولویت افزایش تخصیص', color: 'success' as const },
  { label: 'پایش نزدیک', color: 'warning' as const },
  { label: 'بازنگری تأمین بودجه', color: 'warning' as const },
  { label: 'اقدام اصلاحی', color: 'error' as const }
]

function modeLabel(mode: string) {
  if (mode === 'LowerIsBetter') return 'کمتر بهتر'
  if (mode === 'TargetRange') return 'بازه هدف'
  return 'بیشتر بهتر'
}

function percent(value?: number | null) {
  return value == null ? '-' : `${number.format(value)}٪`
}

function scoreColor(value?: number | null): 'success' | 'warning' | 'error' | 'default' {
  if (value == null) return 'default'
  if (value >= 90) return 'success'
  if (value >= 70) return 'warning'
  return 'error'
}

function apiError(error: unknown, fallback: string) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; title?: string } } }).response
    return response?.data?.detail ?? response?.data?.title ?? fallback
  }
  return fallback
}

export default function PerformanceBudgetScorecard({
  companyId,
  fiscalYearId,
  refreshToken = 0
}: {
  companyId: string
  fiscalYearId: string
  refreshToken?: number
}) {
  const [plans, setPlans] = useState<Plan[]>([])
  const [versionId, setVersionId] = useState('')
  const [scorecard, setScorecard] = useState<Scorecard | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [manualRefresh, setManualRefresh] = useState(0)

  const versions = useMemo(() => plans.flatMap(plan => plan.versions.map(version => ({ plan, version })))
    .filter(x => x.version.status !== 5)
    .sort((a, b) => {
      const aOperational = a.version.status === 4 || a.version.status === 7 ? 1 : 0
      const bOperational = b.version.status === 4 || b.version.status === 7 ? 1 : 0
      return bOperational - aOperational || b.version.versionNumber - a.version.versionNumber
    }), [plans])

  useEffect(() => {
    if (!companyId || !fiscalYearId) return
    setPlans([]); setVersionId(''); setScorecard(null); setError(''); setLoading(true)
    api.get<Plan[]>('/budget/plans', { params: { companyId, fiscalYearId } })
      .then(response => {
        setPlans(response.data)
        const candidates = response.data.flatMap(plan => plan.versions.map(version => ({ plan, version })))
          .filter(x => x.version.status !== 5)
          .sort((a, b) => {
            const aOperational = a.version.status === 4 || a.version.status === 7 ? 1 : 0
            const bOperational = b.version.status === 4 || b.version.status === 7 ? 1 : 0
            return bOperational - aOperational || b.version.versionNumber - a.version.versionNumber
          })
        setVersionId(candidates[0]?.version.id ?? '')
      })
      .catch(error => setError(apiError(error, 'دریافت نسخه‌های بودجه برای Scorecard ناموفق بود.')))
      .finally(() => setLoading(false))
  }, [companyId, fiscalYearId])

  useEffect(() => {
    if (!versionId) { setScorecard(null); return }
    setLoading(true); setError('')
    api.get<Scorecard>('/performance-budgeting/scorecard', { params: { versionId } })
      .then(response => setScorecard(response.data))
      .catch(error => { setScorecard(null); setError(apiError(error, 'محاسبه Scorecard بودجه مبتنی بر عملکرد ناموفق بود.')) })
      .finally(() => setLoading(false))
  }, [versionId, refreshToken, manualRefresh])

  const recommendation = scorecard ? (recommendations[scorecard.recommendation] ?? recommendations[0]) : recommendations[0]

  return <Stack spacing={2}>
    <Card elevation={0}><CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2}>
        <Box><Typography variant="h6" fontWeight={900}>Scorecard بودجه مبتنی بر عملکرد</Typography><Typography color="text.secondary">زنجیره تصمیم: KPI → هدف راهبردی → کنترل بودجه و تعهدات → Recommendation تخصیص.</Typography></Box>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
          <FormControl size="small" sx={{ minWidth: 320 }}><InputLabel>نسخه بودجه</InputLabel><Select value={versionId} label="نسخه بودجه" onChange={e => setVersionId(e.target.value)}>{versions.map(({ plan, version }) => <MenuItem key={version.id} value={version.id}>{plan.name} — {version.name} — نسخه {version.versionNumber.toLocaleString('fa-IR')}</MenuItem>)}</Select></FormControl>
          <Button variant="outlined" disabled={loading || !versionId} onClick={() => setManualRefresh(value => value + 1)}>به‌روزرسانی Scorecard</Button>
        </Stack>
      </Stack>
    </CardContent></Card>

    {error && <Alert severity="error">{error}</Alert>}
    {loading && <Box textAlign="center" py={2}><CircularProgress size={28} /></Box>}
    {!loading && !versions.length && <Alert severity="info">برای سال مالی انتخاب‌شده هنوز نسخه بودجه‌ای جهت Scorecard وجود ندارد.</Alert>}

    {scorecard && <>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
        <Card elevation={0} sx={{ flex: 1 }}><CardContent><Typography color="text.secondary" variant="body2">امتیاز KPI وزن‌دار</Typography><Typography variant="h4" fontWeight={900} mt={1}>{percent(scorecard.weightedKpiScore)}</Typography><Typography variant="caption" color="text.secondary">قبل از اعمال اولویت اهداف راهبردی</Typography></CardContent></Card>
        <Card elevation={0} sx={{ flex: 1 }}><CardContent><Typography color="text.secondary" variant="body2">امتیاز راهبردی</Typography><Typography variant="h4" fontWeight={900} mt={1}>{percent(scorecard.strategyWeightedScore)}</Typography><Typography variant="caption" color="text.secondary">پوشش Strategy: {percent(scorecard.strategyCoveragePercent)}</Typography></CardContent></Card>
        <Card elevation={0} sx={{ flex: 1 }}><CardContent><Typography color="text.secondary" variant="body2">امتیاز مبنای تصمیم</Typography><Typography variant="h4" fontWeight={900} mt={1}>{percent(scorecard.recommendationScore)}</Typography><Typography variant="caption" color="text.secondary">پوشش مؤثر: {percent(scorecard.dataCoveragePercent)}</Typography></CardContent></Card>
        <Card elevation={0} sx={{ flex: 1 }}><CardContent><Typography color="text.secondary" variant="body2">توصیه تخصیص</Typography><Box mt={1.5}><Chip label={recommendation.label} color={recommendation.color} /></Box><Typography variant="caption" color="text.secondary" display="block" mt={1}>همراه با کنترل Actual، Commitment و Forecast</Typography></CardContent></Card>
      </Stack>

      <Card elevation={0}><CardContent><Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={1}><Box><Typography fontWeight={900}>نسخه و پوشش زمانی</Typography><Typography variant="body2" color="text.secondary">{scorecard.versionName} — سناریو {scorecard.scenarioName} — {scorecard.fiscalYearName}</Typography></Box><Typography variant="body2">{scorecard.elapsedPeriods.toLocaleString('fa-IR')} از {scorecard.totalPeriods.toLocaleString('fa-IR')} دوره | Measure: {scorecard.selectedMeasureName} ({scorecard.selectedMeasureCode})</Typography></Stack></CardContent></Card>

      {scorecard.reasons.length > 0 && <Alert severity={scorecard.recommendation === 5 ? 'error' : scorecard.recommendation === 4 || scorecard.recommendation === 3 ? 'warning' : 'info'}><Stack spacing={.5}>{scorecard.reasons.map((reason, index) => <Typography key={`${reason}-${index}`} variant="body2">• {reason}</Typography>)}</Stack></Alert>}

      {scorecard.objectives.length > 0 && <Card elevation={0}><CardContent sx={{ p: 0 }}><Box p={2.5}><Typography variant="h6" fontWeight={900}>تحقق اهداف راهبردی</Typography><Typography variant="caption" color="text.secondary">امتیاز هر هدف از KPIهای متصل و وزن نسبی Mapping محاسبه می‌شود.</Typography></Box><TableContainer sx={{ maxHeight: 360 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>هدف راهبردی</TableCell><TableCell align="left">وزن راهبردی</TableCell><TableCell align="left">KPI متصل</TableCell><TableCell align="left">KPI دارای داده</TableCell><TableCell align="left">پوشش</TableCell><TableCell align="left">امتیاز</TableCell><TableCell>وضعیت</TableCell></TableRow></TableHead><TableBody>{scorecard.objectives.map(objective => <TableRow key={objective.objectiveId}><TableCell><Typography fontWeight={900}>{objective.name}</Typography><Typography variant="caption" color="text.secondary">{objective.code}</Typography></TableCell><TableCell align="left">{number.format(objective.strategicWeight)}٪</TableCell><TableCell align="left">{objective.linkedKpiCount.toLocaleString('fa-IR')}</TableCell><TableCell align="left">{objective.observedKpiCount.toLocaleString('fa-IR')}</TableCell><TableCell align="left">{percent(objective.dataCoveragePercent)}</TableCell><TableCell align="left">{percent(objective.score)}</TableCell><TableCell><Chip size="small" color={scoreColor(objective.score)} variant="outlined" label={objective.score == null ? 'فاقد داده' : objective.score >= 90 ? 'مطلوب' : objective.score >= 70 ? 'نیازمند پایش' : 'نیازمند اقدام'} /></TableCell></TableRow>)}</TableBody></Table></TableContainer></CardContent></Card>}

      <Card elevation={0}><CardContent sx={{ p: 0 }}><Box p={2.5}><Typography variant="h6" fontWeight={900}>وضعیت مالی به تفکیک ارز</Typography><Typography variant="caption" color="text.secondary">ارزهای متفاوت عمداً با هم جمع نمی‌شوند.</Typography></Box><TableContainer><Table size="small"><TableHead><TableRow><TableCell>ارز</TableCell><TableCell align="left">بودجه YTD</TableCell><TableCell align="left">Actual YTD</TableCell><TableCell align="left">Commitment YTD</TableCell><TableCell align="left">مصرف</TableCell><TableCell align="left">Exposure</TableCell><TableCell align="left">Forecast سالانه</TableCell></TableRow></TableHead><TableBody>{scorecard.currencies.map(row => <TableRow key={row.currencyCode}><TableCell sx={{ direction: 'ltr', fontWeight: 900 }}>{row.currencyCode}</TableCell><TableCell align="left">{amount.format(row.ytdBudget)}</TableCell><TableCell align="left">{amount.format(row.ytdActual)}</TableCell><TableCell align="left">{amount.format(row.ytdCommitment)}</TableCell><TableCell align="left">{percent(row.ytdUtilizationPercent)}</TableCell><TableCell align="left">{percent(row.ytdExposurePercent)}</TableCell><TableCell align="left">{amount.format(row.annualForecast)} <Typography component="span" variant="caption" color="text.secondary">({percent(row.annualForecastPercent)})</Typography></TableCell></TableRow>)}{!scorecard.currencies.length && <TableRow><TableCell colSpan={7} align="center" sx={{ py: 4 }}>برای Measure منتخب هنوز Fact مالی ثبت نشده است.</TableCell></TableRow>}</TableBody></Table></TableContainer></CardContent></Card>

      <Card elevation={0}><CardContent sx={{ p: 0 }}><Box p={2.5}><Typography variant="h6" fontWeight={900}>مؤلفه‌های امتیاز KPI</Typography></Box><TableContainer sx={{ maxHeight: 360 }}><Table stickyHeader size="small"><TableHead><TableRow><TableCell>KPI</TableCell><TableCell>منطق</TableCell><TableCell align="left">وزن</TableCell><TableCell align="left">تعداد مشاهده</TableCell><TableCell align="left">میانگین امتیاز</TableCell><TableCell align="left">آخرین امتیاز</TableCell><TableCell>وضعیت آخر</TableCell></TableRow></TableHead><TableBody>{scorecard.kpis.map(kpi => <TableRow key={kpi.kpiId}><TableCell><Typography fontWeight={800}>{kpi.name}</Typography><Typography variant="caption" color="text.secondary">{kpi.code}</Typography></TableCell><TableCell>{modeLabel(kpi.scoreMode)}</TableCell><TableCell align="left">{number.format(kpi.weight)}٪</TableCell><TableCell align="left">{kpi.observationCount.toLocaleString('fa-IR')}</TableCell><TableCell align="left">{number.format(kpi.averageScore)}٪</TableCell><TableCell align="left">{number.format(kpi.latestScore)}٪</TableCell><TableCell><Chip size="small" label={kpi.latestIsOnTarget ? 'روی هدف' : 'خارج از هدف'} color={kpi.latestIsOnTarget ? 'success' : 'warning'} variant="outlined" /></TableCell></TableRow>)}{!scorecard.kpis.length && <TableRow><TableCell colSpan={7} align="center" sx={{ py: 4 }}>KPI دارای مشاهده YTD وجود ندارد.</TableCell></TableRow>}</TableBody></Table></TableContainer></CardContent></Card>
    </>}
  </Stack>
}
