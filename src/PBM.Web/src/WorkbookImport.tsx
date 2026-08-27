import { useState } from 'react'
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Divider, List, ListItemButton, ListItemText, Stack, Table, TableBody, TableCell, TableContainer, TableRow, Typography } from '@mui/material'
import UploadFileRoundedIcon from '@mui/icons-material/UploadFileRounded'
import { api } from './api'

type SheetPreview = { name: string; rowCount: number; columnCount: number; previewRows: (string | null)[][] }
type Inspection = { fileName: string; fileSize: number; sheets: SheetPreview[] }

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
  return <Stack spacing={2.5}>
    <Card elevation={0}><CardContent sx={{ p: 3 }}>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={2}>
        <Box><Typography variant="h6" fontWeight={900}>ورود و نگاشت فایل اکسل</Typography><Typography color="text.secondary" mt={.5}>در مرحله اول ساختار Workbook، شیت‌ها و چند ردیف نمونه خوانده می‌شود؛ سپس پروفایل نگاشت به Dimension و Measure ساخته خواهد شد.</Typography></Box>
        <Button component="label" variant="contained" startIcon={<UploadFileRoundedIcon />} disabled={busy}>انتخاب فایل XLSX<input hidden type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" onChange={e => inspect(e.target.files?.[0])} /></Button>
      </Stack>
    </CardContent></Card>
    {busy && <Box py={6} textAlign="center"><CircularProgress /></Box>}
    {error && <Alert severity="error">{error}</Alert>}
    {inspection && <Card elevation={0}><CardContent sx={{ p: 0 }}>
      <Stack direction="row" spacing={1} alignItems="center" p={2.5}><Typography fontWeight={900}>{inspection.fileName}</Typography><Chip size="small" label={`${inspection.sheets.length} شیت`} /><Chip size="small" variant="outlined" label={`${(inspection.fileSize / 1024 / 1024).toFixed(2)} MB`} /></Stack><Divider />
      <Box sx={{ display: 'grid', gridTemplateColumns: '280px minmax(0, 1fr)', minHeight: 520 }}>
        <List sx={{ borderLeft: '1px solid #e7edf5', overflow: 'auto', maxHeight: 620 }}>{inspection.sheets.map((s, i) => <ListItemButton selected={selected === i} onClick={() => setSelected(i)} key={`${s.name}-${i}`}><ListItemText primary={s.name} secondary={`${s.rowCount} ردیف × ${s.columnCount} ستون`} /></ListItemButton>)}</List>
        <Box sx={{ minWidth: 0, p: 2.5 }}><Typography variant="h6" fontWeight={800}>{sheet?.name}</Typography><Typography variant="body2" color="text.secondary" mb={2}>پیش‌نمایش حداکثر ۸ ردیف و ۲۰ ستون اول؛ مقادیر خام برای طراحی Mapping نمایش داده می‌شوند.</Typography>
          <TableContainer sx={{ border: '1px solid #e8eef5', borderRadius: 2, maxHeight: 500 }}><Table size="small" stickyHeader><TableBody>{sheet?.previewRows.map((row, r) => <TableRow key={r}>{row.map((cell, c) => <TableCell key={c} sx={{ minWidth: 110, whiteSpace: 'nowrap' }}>{cell ?? ''}</TableCell>)}</TableRow>)}</TableBody></Table></TableContainer>
        </Box>
      </Box>
    </CardContent></Card>}
  </Stack>
}
