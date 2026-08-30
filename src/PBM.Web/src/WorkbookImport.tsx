import { useEffect, useState } from 'react'
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Divider, FormControl, InputLabel, LinearProgress, List, ListItemButton, ListItemText, MenuItem, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import UploadFileRoundedIcon from '@mui/icons-material/UploadFileRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import DataObjectRoundedIcon from '@mui/icons-material/DataObjectRounded'
import { api } from './api'

type SheetPreview = { name: string; rowCount: number; columnCount: number; previewRows: (string | null)[][]; suggestedProfile: number; suggestedModelCode?: string; confidencePercent: number; tags: string[] }
type Inspection = { fileName: string; fileSize: number; sheets: SheetPreview[] }
type NormalizedFact = { sourceRow: number; measureCode: string; valueKind: number; periodName?: string; value: number; unit: string; scaleApplied: number; dimensionMembers: Record<string, string>; sourceLabel?: string }
type Normalization = { sheetName: string; profile: number; modelCode?: string; sourceRows: number; facts: NormalizedFact[]; warnings: string[] }
type Execution = { sheetName: string; modelCode: string; budgetPlanId: string; versionId: string; importedFacts: number; updatedFacts: number; createdDimensionMembers: number; skippedFacts: number; warnings: string[] }

const profileLabels = ['نامشخص', 'داده‌های پایه', 'واردات و فروش', 'قیمت کالا', 'گردش ماهانه', 'نیروی انسانی', 'هزینه واحدها', 'ریز خرید', 'گردش موجودی', 'تسهیلات و مالی', 'سود و زیان', 'ترازنامه', 'جریان نقد', 'مطالبات/بدهی', 'نسبت‌ها', 'جلد']
const nf = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 2 })

export default function WorkbookImport({ companyId, fiscalYearId }: { companyId: string; fiscalYearId: string }) {
  const [file, setFile] = useState<File | null>(null)
  const [inspection, setInspection] = useState<Inspection | null>(null)
  const [selected, setSelected] = useState(0)
  const [normalization, setNormalization] = useState<Normalization | null>(null)
  const [execution, setExecution] = useState<Execution | null>(null)
  const [valueKind, setValueKind] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const sheet = inspection?.sheets[selected]
  useEffect(() => { setNormalization(null); setExecution(null) }, [selected])

  const inspect = async (selectedFile?: File) => {
    if (!selectedFile) return
    setFile(selectedFile); setBusy(true); setError(''); setInspection(null); setNormalization(null); setExecution(null)
    const form = new FormData(); form.append('file', selectedFile)
    try { const { data } = await api.post<Inspection>('/imports/workbook/inspect', form, { timeout: 60000 }); setInspection(data); setSelected(0) }
    catch { setError('خواندن فایل اکسل ناموفق بود. فایل باید XLSX معتبر باشد.') }
    finally { setBusy(false) }
  }

  const normalize = async () => {
    if (!file || !sheet || sheet.suggestedProfile === 0) return
    setBusy(true); setError(''); setExecution(null)
    const form = new FormData(); form.append('file', file); form.append('sheetName', sheet.name); form.append('profile', String(sheet.suggestedProfile))
    try { const { data } = await api.post<Normalization>('/imports/workbook/normalize', form, { timeout: 120000 }); setNormalization(data) }
    catch { setError('تبدیل شیت به Fact و Dimension ناموفق بود.') }
    finally { setBusy(false) }
  }

  const execute = async () => {
    if (!file || !sheet || !normalization?.facts.length || !companyId || !fiscalYearId) return
    if (!window.confirm(`تعداد ${normalization.facts.length.toLocaleString('fa-IR')} Fact از شیت «${sheet.name}» وارد نسخه پیش‌نویس شود؟`)) return
    setBusy(true); setError('')
    const form = new FormData(); form.append('file', file); form.append('companyId', companyId); form.append('fiscalYearId', fiscalYearId); form.append('sheetName', sheet.name); form.append('profile', String(sheet.suggestedProfile)); form.append('valueKind', String(valueKind))
    try { const { data } = await api.post<Execution>('/imports/workbook/execute', form, { timeout: 180000 }); setExecution(data) }
    catch { setError('ثبت اطلاعات نرمال‌شده در مدل بودجه ناموفق بود. نسخه هدف باید Draft و قابل ویرایش باشد.') }
    finally { setBusy(false) }
  }

  const recognized = inspection?.sheets.filter(x => x.suggestedProfile !== 0).length ?? 0
  const modelCounts = inspection?.sheets.reduce<Record<string, number>>((acc, x) => { const key = x.suggestedModelCode ?? 'MANUAL'; acc[key] = (acc[key] ?? 0) + 1; return acc }, {}) ?? {}

  return <Stack spacing={2.5}>
    <Card elevation={0}><CardContent sx={{ p: 3 }}><Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2}>
      <Box><Typography variant="h6" fontWeight={900}>ورود، نرمال‌سازی و ثبت فایل اکسل</Typography><Typography color="text.secondary" mt={.5}>شیت‌ها تشخیص داده می‌شوند، به Fact/Dimension استاندارد تبدیل می‌شوند و پس از بازبینی شما داخل نسخه Draft مدل بودجه ثبت می‌شوند.</Typography></Box>
      <Button component="label" variant="contained" startIcon={<UploadFileRoundedIcon />} disabled={busy}>انتخاب فایل XLSX<input hidden type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" onChange={e => inspect(e.target.files?.[0])} /></Button>
    </Stack></CardContent></Card>
    {busy && <Box py={2}><LinearProgress /></Box>}
    {error && <Alert severity="error">{error}</Alert>}
    {inspection && <>
      <Box className="kpi-grid"><Summary title="شیت‌ها" value={inspection.sheets.length.toLocaleString('fa-IR')} /><Summary title="تشخیص خودکار" value={`${recognized.toLocaleString('fa-IR')} شیت`} /><Summary title="مدل‌ها" value={Object.keys(modelCounts).length.toLocaleString('fa-IR')} /><Summary title="حجم فایل" value={`${(inspection.fileSize / 1024 / 1024).toFixed(2)} MB`} /></Box>
      <Card elevation={0}><CardContent sx={{ p: 0 }}><Stack direction="row" spacing={1} alignItems="center" p={2.5}><Typography fontWeight={900}>{inspection.fileName}</Typography><Chip size="small" label={`${inspection.sheets.length} شیت`} /><Chip size="small" variant="outlined" label={`${recognized} شیت تشخیص داده شد`} /></Stack><Divider />
        <Box sx={{ display: 'grid', gridTemplateColumns: '310px minmax(0, 1fr)', minHeight: 560 }}>
          <List sx={{ borderLeft: '1px solid #e7edf5', overflow: 'auto', maxHeight: 680 }}>{inspection.sheets.map((s, i) => <ListItemButton selected={selected === i} onClick={() => setSelected(i)} key={`${s.name}-${i}`} alignItems="flex-start"><ListItemText primary={s.name} secondary={<><span>{s.rowCount} ردیف × {s.columnCount} ستون</span><br /><span>{profileLabels[s.suggestedProfile] ?? 'نامشخص'} — {s.confidencePercent}٪</span></>} /></ListItemButton>)}</List>
          <Box sx={{ minWidth: 0, p: 2.5 }}>
            <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} mb={2}><Box><Typography variant="h6" fontWeight={800}>{sheet?.name}</Typography><Typography variant="body2" color="text.secondary">ابتدا نمونه خام را ببینید؛ سپس «ساخت پیش‌نمایش نرمال‌شده» را بزنید تا قبل از ثبت، Factهای واقعی قابل بررسی باشند.</Typography></Box>{sheet && <Stack alignItems={{ md: 'flex-end' }} spacing={.7}><Chip color={sheet.confidencePercent >= 90 ? 'success' : sheet.confidencePercent >= 70 ? 'warning' : 'default'} label={profileLabels[sheet.suggestedProfile] ?? 'نامشخص'} /><Typography variant="caption" color="text.secondary">Model: {sheet.suggestedModelCode ?? 'Manual Mapping'}</Typography></Stack>}</Stack>
            {sheet && <><Stack direction="row" spacing={.7} flexWrap="wrap" useFlexGap mb={1.2}>{sheet.tags.map(tag => <Chip key={tag} size="small" variant="outlined" label={tag} />)}</Stack><LinearProgress variant="determinate" value={sheet.confidencePercent} sx={{ mb: 2, height: 7, borderRadius: 5 }} /></>}
            <TableContainer sx={{ border: '1px solid #e8eef5', borderRadius: 2, maxHeight: 360 }}><Table size="small" stickyHeader><TableBody>{sheet?.previewRows.map((row, r) => <TableRow key={r}>{row.map((cell, c) => <TableCell key={c} sx={{ minWidth: 110, whiteSpace: 'nowrap' }}>{cell ?? ''}</TableCell>)}</TableRow>)}</TableBody></Table></TableContainer>
            <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.2} mt={2}><Button variant="outlined" startIcon={<DataObjectRoundedIcon />} onClick={normalize} disabled={busy || !sheet || sheet.suggestedProfile === 0}>ساخت پیش‌نمایش نرمال‌شده</Button><FormControl size="small" sx={{ minWidth: 155 }}><InputLabel>ثبت به عنوان</InputLabel><Select value={valueKind} label="ثبت به عنوان" onChange={e => setValueKind(Number(e.target.value))}><MenuItem value={0}>بودجه</MenuItem><MenuItem value={1}>عملکرد واقعی</MenuItem><MenuItem value={2}>تعهد</MenuItem><MenuItem value={3}>پیش‌بینی</MenuItem></Select></FormControl>{normalization?.facts.length ? <Button variant="contained" color="success" startIcon={<PlayArrowRoundedIcon />} onClick={execute} disabled={busy}>ثبت در سیستم</Button> : null}</Stack>
          </Box>
        </Box>
      </CardContent></Card>
    </>}
    {normalization && <Card elevation={0}><CardContent><Stack direction="row" spacing={1} alignItems="center" mb={2}><Typography variant="h6" fontWeight={900}>Factهای نرمال‌شده</Typography><Chip label={`${normalization.facts.length.toLocaleString('fa-IR')} Fact`} color="primary" /><Chip variant="outlined" label={normalization.modelCode ?? '-'} /></Stack>{normalization.warnings.map((w, i) => <Alert key={i} severity="warning" sx={{ mb: 1 }}>{w}</Alert>)}<TableContainer sx={{ maxHeight: 520, border: '1px solid #e8eef5', borderRadius: 2 }}><Table size="small" stickyHeader><TableHead><TableRow><TableCell>ردیف</TableCell><TableCell>دوره</TableCell><TableCell>مژر</TableCell><TableCell>ابعاد</TableCell><TableCell align="left">مقدار</TableCell><TableCell>واحد</TableCell></TableRow></TableHead><TableBody>{normalization.facts.slice(0, 300).map((f, i) => <TableRow key={`${f.sourceRow}-${f.measureCode}-${f.periodName}-${i}`}><TableCell>{f.sourceRow.toLocaleString('fa-IR')}</TableCell><TableCell>{f.periodName ?? '-'}</TableCell><TableCell><code>{f.measureCode}</code></TableCell><TableCell>{Object.entries(f.dimensionMembers).map(([k, v]) => `${k}=${v}`).join(' | ')}</TableCell><TableCell align="left">{nf.format(f.value)}</TableCell><TableCell>{f.unit}</TableCell></TableRow>)}</TableBody></Table></TableContainer>{normalization.facts.length > 300 && <Typography variant="caption" color="text.secondary" display="block" mt={1}>برای سرعت رابط کاربری، ۳۰۰ Fact اول نمایش داده شده است؛ همه Factها هنگام Import پردازش می‌شوند.</Typography>}</CardContent></Card>}
    {execution && <Alert severity="success"><Typography fontWeight={900}>ورود اطلاعات انجام شد.</Typography>مدل {execution.modelCode}: {execution.importedFacts.toLocaleString('fa-IR')} Fact جدید، {execution.updatedFacts.toLocaleString('fa-IR')} Fact به‌روزرسانی، {execution.createdDimensionMembers.toLocaleString('fa-IR')} عضو Dimension جدید و {execution.skippedFacts.toLocaleString('fa-IR')} مورد ردشده.</Alert>}
  </Stack>
}

function Summary({ title, value }: { title: string; value: string }) { return <Card elevation={0} className="kpi-card"><CardContent><Typography color="text.secondary" fontWeight={700}>{title}</Typography><Typography variant="h6" fontWeight={900} mt={1}>{value}</Typography></CardContent></Card> }
