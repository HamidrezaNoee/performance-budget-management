import { useMemo, useState } from 'react'
import { Alert, Box, Card, CardContent, Stack, Tab, Tabs, Typography } from '@mui/material'
import Inventory2RoundedIcon from '@mui/icons-material/Inventory2Rounded'
import BusinessRoundedIcon from '@mui/icons-material/BusinessRounded'
import MasterDataAdmin from './MasterDataAdmin'
import OrganizationAdmin from './OrganizationAdmin'
import CurrencyAdmin from './CurrencyAdmin'
import PlanningMasterDataAdmin from './PlanningMasterDataAdmin'

export default function MasterDataWorkspace({ companyId, roles }: { companyId: string; roles: string[] }) {
  const [tab, setTab] = useState(0)
  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const canManageOrganization = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN')

  return <Stack spacing={2.5}>
    <Card elevation={0}>
      <Tabs value={tab} onChange={(_, value) => setTab(value)} variant="fullWidth">
        <Tab label="اطلاعات پایه عملیاتی" />
        <Tab label="اطلاعات پایه بودجه‌ای / غیرعملیاتی" />
      </Tabs>
    </Card>

    {tab === 0 && <Stack spacing={2.5}>
      <Card elevation={0}>
        <CardContent>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems={{ md: 'center' }}>
            <Inventory2RoundedIcon color="primary" sx={{ fontSize: 36 }} />
            <Box>
              <Typography variant="h6" fontWeight={900}>اطلاعات پایه عملیاتی</Typography>
              <Typography color="text.secondary" mt={0.5}>
                شرکت و ساختار سازمانی، کالا و محصول، برند، واحد سنجش، تأمین‌کننده، کشور و جغرافیا، ارز، انبار، گمرک و سایر کدهای عملیاتی در این شاخه مدیریت می‌شوند.
              </Typography>
            </Box>
          </Stack>
        </CardContent>
      </Card>

      {canManageOrganization ? <>
        <Stack direction="row" spacing={1} alignItems="center">
          <BusinessRoundedIcon color="primary" />
          <Typography variant="h6" fontWeight={900}>شرکت و ساختار سازمانی</Typography>
        </Stack>
        <OrganizationAdmin />
      </> : <Alert severity="info">
        تعریف شرکت و تغییر ساختار سازمانی فقط برای مدیر سامانه فعال است. اطلاعات پایه شرکت جاری را می‌توانید در ادامه مشاهده یا در صورت داشتن مجوز مدیریت کنید.
      </Alert>}

      {companyId ? <MasterDataAdmin companyId={companyId} roles={roles} /> : <Alert severity="warning">
        برای مشاهده اطلاعات پایه عملیاتی ابتدا باید یک شرکت در دسترس باشد.
      </Alert>}

      <CurrencyAdmin roles={roles} />
    </Stack>}

    {tab === 1 && <PlanningMasterDataAdmin companyId={companyId} roles={roles} />}
  </Stack>
}
