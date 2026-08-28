import { useEffect, useState } from 'react'
import { Alert, Button, Card, CardContent, Chip, Stack, Switch, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography } from '@mui/material'
import { api } from './api'

type Scenario = { id: string; code: string; name: string; isActive: boolean }

function apiError(error: unknown) {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string } } }).response
    if (response?.data?.detail) return response.data.detail
  }
  return 'عملیات سناریو ناموفق بود.'
}

export default function ScenarioAdmin({ canManage }: { canManage: boolean }) {
  const [items, setItems] = useState<Scenario[]>([])
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  const reload = async () => {
    setBusy(true); setError('')
    try { const { data } = await api.get<Scenario[]>('/scenarios/'); setItems(data) }
    catch (error) { setError(apiError(error)) }
    finally { setBusy(false) }
  }

  useEffect(() => { reload() }, [])

  const createScenario = async () => {
    if (!canManage || !code.trim() || !name.trim()) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.post('/scenarios/', { code: code.trim(), name: name.trim() })
      setCode(''); setName(''); setMessage('سناریوی بودجه ایجاد شد.'); await reload()
    } catch (error) { setError(apiError(error)) }
    finally { setBusy(false) }
  }

  const updateScenario = async (scenario: Scenario, patch: Partial<Pick<Scenario, 'name' | 'isActive'>>) => {
    if (!canManage) return
    setBusy(true); setError(''); setMessage('')
    try {
      await api.put(`/scenarios/${scenario.id}`, { name: patch.name ?? scenario.name, isActive: patch.isActive ?? scenario.isActive })
      setMessage('سناریو به‌روزرسانی شد.'); await reload()
    } catch (error) { setError(apiError(error)) }
    finally { setBusy(false) }
  }

  return <Stack spacing={2.5}>
    {error && <Alert severity="error">{error}</Alert>}
    {message && <Alert severity="success">{message}</Alert>}
    {canManage && <Card elevation={0}><CardContent><Typography variant="h6" fontWeight={900}>تعریف سناریوی بودجه</Typography><Typography color="text.secondary" mb={2}>برای سناریوهای پایه، خوش‌بینانه، بدبینانه، تنش و Forecast نسخه‌های مستقل بودجه قابل ایجاد است.</Typography><Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5}><TextField size="small" label="کد سناریو" value={code} onChange={e => setCode(e.target.value)} placeholder="BEST_CASE" /><TextField size="small" label="نام سناریو" value={name} onChange={e => setName(e.target.value)} placeholder="سناریوی بهترین حالت" sx={{ minWidth: 260 }} /><Button variant="contained" onClick={createScenario} disabled={busy || !code.trim() || !name.trim()}>ایجاد سناریو</Button></Stack></CardContent></Card>}
    {!canManage && <Alert severity="info">سناریوها فقط قابل مشاهده هستند. ایجاد و تغییر سناریو نیازمند نقش مدیر بودجه، مدیر مالی یا مدیر سامانه است.</Alert>}
    <Card elevation={0}><CardContent sx={{ p: 0 }}><TableContainer><Table size="small"><TableHead><TableRow><TableCell>کد</TableCell><TableCell>نام</TableCell><TableCell>وضعیت</TableCell><TableCell>فعال/غیرفعال</TableCell></TableRow></TableHead><TableBody>{items.map(item => <TableRow key={item.id} hover><TableCell sx={{ direction: 'ltr' }}>{item.code}</TableCell><TableCell>{canManage ? <TextField size="small" defaultValue={item.name} onBlur={e => { const value = e.target.value.trim(); if (value && value !== item.name) updateScenario(item, { name: value }) }} /> : item.name}</TableCell><TableCell><Chip size="small" color={item.isActive ? 'success' : 'default'} label={item.isActive ? 'فعال' : 'غیرفعال'} /></TableCell><TableCell><Switch checked={item.isActive} disabled={!canManage || item.code === 'BASE' || busy} onChange={e => updateScenario(item, { isActive: e.target.checked })} /></TableCell></TableRow>)}</TableBody></Table></TableContainer></CardContent></Card>
  </Stack>
}
