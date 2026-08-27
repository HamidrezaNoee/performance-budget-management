import { useState } from 'react'
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Divider, LinearProgress, List, ListItemButton, ListItemText, Stack, Table, TableBody, TableCell, TableContainer, TableRow, Typography } from '@mui/material'
import UploadFileRoundedIcon from '@mui/icons-material/UploadFileRounded'
import { api } from './api'

type SheetPreview = {
  name: string
  rowCount: number
  columnCount: number
  previewRows: (string | null)[][]
  suggestedProfile: number
  suggestedModelCode?: string
  confidencePercent: number
  tags: string[]
}
type Inspection = { fileName: string; fileSize: number; sheets: SheetPreview[] }

const profileLabels = [
  'نامشخص', 'داده‌های پایه', 'واردات و فروش', 'قیمت کالا', 'گردش ماهانه', 'نیروی انسانی', 'هزینه واحدها',
  'ریز خرید', 'گردش موجودی', 'تسهیلات و مالی', 'سود و زیان', 'ترازنامه', 'جریان نقد', 'مطالبات/بدهی', 'نسبت‌ها', 'جلد'
]

export default function WorkbookImport() {
  const [inspection, setInspection] = useState<Inspection | null>(null)
  const [selected, setSelected] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const inspect = async (file?: File) => {
    if (!file) return
    setBusy(true); setError(''); setInspection(null)
    const form = new FormData(); form.append('file', file)
    try {
      const { data } = await api.post<Inspection>('/imports/workbook/inspect', form, { timeout: 60000 })
      setInspection(data); setSelected(0)
    } catch { setError('خواندن فایل اکسل ناموفق بود. فایل باید XLSX معتبر باشد.') }
    finally { setBusy(false) }
  }

  const sheet = inspection?.sheets[selected]
  const recognized = inspection?.sheets.filter(x => x.suggestedProfile !== 0).length ?? 0
  const modelCounts = inspection?.sheets.reduce<Record<string, number>>((acc, x) => {
    const key = x.suggestedModelCode ?? 'MANUAL'; acc[key] = (acc[key] ?? 0) + 1; return acc
  }, {}) ?? {}

  return <Stack spacing={2.5}>
    <Card elevation={0}><CardContent sx={{ p: 3 }}>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2}>
        <Box><Typography variant="h6" fontWeight={900}>ورود و نگاشت فایل اکسل</Typography><Typography color="text.secondary" mt={.5}>ساختار Workbook خوانده می‌شود و هر شیت به‌صورت خودکار برای مدل‌های تجارت، هزینه، مالی، منابع انسانی و صورت‌های مالی طبقه‌بندی می‌شود.</Typography></Box>
        <Button component="label" variant="contained" startIcon={<UploadFileRoundedIcon />} disabled={busy}>انتخاب فایل XLSX<input hidden type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" onChange={e => inspect(e.target.files?.[0])} /></Button>
      </Stack>
    </CardContent></Card>
    {busy && <Box py={6} textAlign="center"><CircularProgress /></Box>}
    {error && <Alert severity="error">{error}</Alert>}
    {inspection && <>
      <Box className="kpi-grid">
        <Summary title="شیت‌ها" value={inspection.sheets.length.toLocaleString('fa-IR')} />
        <Summary title="تشخیص خودکار" value={`${recognized.toLocaleString('fa-IR')} شیت`} />
        <Summary title="مدل‌های تشخیص‌داده‌شده" value={Object.keys(modelCounts).length.toLocaleString('fa-IR')} />
        <Summary title="حجم فایل" value={`${(inspection.fileSize / 1024 / 1024).toFixed(2)} MB`} />
      </Box>
      <Card elevation={0}><CardContent sx={{ p: 0 }}>
        <Stack direction="row" spacing={1} alignItems="center" p={2.5}><Typography fontWeight={900}>{inspection.fileName}</Typography><Chip size="small" label={`${inspection.sheets.length} شیت`} /><Chip size="small" variant="outlined" label={`${recognized} شیت تشخیص داده شد`} /></Stack><Divider />
        <Box sx={{ display: 'grid', gridTemplateColumns: '310px minmax(0, 1fr)', minHeight: 560 }}>
          <List sx={{ borderLeft: '1px solid #e7edf5', overflow: 'auto', maxHeight: 680 }}>{inspection.sheets.map((s, i) => <ListItemButton selected={selected === i} onClick={() => setSelected(i)} key={`${s.name}-${i}`} alignItems="flex-start"><ListItemText primary={s.name} secondary={<><span>{s.rowCount} ردیف × {s.columnCount} ستون</span><br /><span>{profileLabels[s.suggestedProfile] ?? 'نامشخص'} — {s.confidencePercent}٪</span></>} /></ListItemButton>)}</List>
          <Box sx={{ minWidth: 0, p: 2.5 }}>
            <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} mb={2}>
              <Box><Typography variant="h6" fontWeight={800}>{sheet?.name}</Typography><Typography variant="body2" color="text.secondary">پیش‌نمایش حداکثر ۸ ردیف و ۲۰ ستون اول؛ مرحله بعد داده‌های این پروفایل را به Fact/Dimension تبدیل می‌کند.</Typography></Box>
              {sheet && <Stack alignItems={{ md: 'flex-end' }} spacing={.7}><Chip color={sheet.confidencePercent >= 90 ? 'success' : sheet.confidencePercent >= 70 ? 'warning' : 'default'} label={profileLabels[sheet.suggestedProfile] ?? 'نامشخص'} /><Typography variant="caption" color="text.secondary">Model: {sheet.suggestedModelCode ?? 'Manual Mapping'}</Typography></Stack>}
            </Stack>
            {sheet && <><Stack direction="row" spacing={.7} flexWrap="wrap" useFlexGap mb={1.2}>{sheet.tags.map(tag => <Chip key={tag} size="small" variant="outlined" label={tag} />)}</Stack><LinearProgress variant="determinate" value={sheet.confidencePercent} sx={{ mb: 2, height: 7, borderRadius: 5 }} /></>}
            <TableContainer sx={{ border: '1px solid #e8eef5', borderRadius: 2, maxHeight: 520 }}><Table size="small" stickyHeader><TableBody>{sheet?.previewRows.map((row, r) => <TableRow key={r}>{row.map((cell, c) => <TableCell key={c} sx={{ minWidth: 110, whiteSpace: 'nowrap' }}>{cell ?? ''}</TableCell>)}</TableRow>)}</TableBody></Table></TableContainer>
          </Box>
        </Box>
      </CardContent></Card>
    </>}
  </Stack>
}

function Summary({ title, value }: { title: string; value: string }) {
  return <Card elevation={0} className="kpi-card"><CardContent><Typography color="text.secondary" fontWeight={700}>{title}</Typography><Typography variant="h6" fontWeight={900} mt={1}>{value}</Typography></CardContent></Card>
}
