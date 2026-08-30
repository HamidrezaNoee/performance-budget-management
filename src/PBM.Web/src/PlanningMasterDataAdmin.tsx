import { useMemo, useState } from 'react'
import { Alert, Card, Stack, Tab, Tabs } from '@mui/material'
import FiscalCalendarAdmin from './FiscalCalendarAdmin'
import ScenarioAdmin from './ScenarioAdmin'
import AssumptionsAdmin from './AssumptionsAdmin'
import DriverTemplatesAdmin from './DriverTemplatesAdmin'
import FormulaDesigner from './FormulaDesigner'
import StrategyAdmin from './StrategyAdmin'
import ReservationReconciliationAdmin from './ReservationReconciliationAdmin'

export default function PlanningMasterDataAdmin({ companyId, roles }: { companyId: string; roles: string[] }) {
  const [tab, setTab] = useState(0)
  const roleSet = useMemo(() => new Set(roles.map(x => x.toUpperCase())), [roles])
  const canManage = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || roleSet.has('CFO') || roleSet.has('BUDGET_MANAGER')
  const canManageStrategy = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || roleSet.has('BUDGET_MANAGER')
  const canViewReconciliation = canManage || roleSet.has('AUDITOR')

  return <Stack spacing={2.5}>
    <Alert severity="info">
      این بخش برای اطلاعات پایه بودجه‌ای و مدیریتی است: تقویم مالی، سناریوها، فرضیات، Driver-Based Budgeting، فرمول‌ها، اهداف راهبردی و KPI و قواعد تطبیق بودجه با Actual.
    </Alert>
    <Card elevation={0}>
      <Tabs value={tab} onChange={(_, value) => setTab(value)} variant="scrollable" scrollButtons="auto">
        <Tab label="تقویم مالی" />
        <Tab label="سناریوهای بودجه" />
        <Tab label="فرضیات بودجه" />
        <Tab label="Templateهای Driver-Based" />
        <Tab label="فرمول‌ها" />
        <Tab label="اهداف راهبردی و KPI" />
        <Tab label="تطبیق رزرو و Actual" disabled={!canViewReconciliation} />
      </Tabs>
    </Card>
    {tab === 0 && companyId && <FiscalCalendarAdmin companyId={companyId} />}
    {tab === 0 && !companyId && <Alert severity="warning">برای تعریف تقویم مالی ابتدا شرکت را انتخاب یا تعریف کنید.</Alert>}
    {tab === 1 && <ScenarioAdmin canManage={canManage} />}
    {tab === 2 && companyId && <AssumptionsAdmin companyId={companyId} canManage={canManage} />}
    {tab === 3 && companyId && <DriverTemplatesAdmin companyId={companyId} canManage={canManage} />}
    {tab === 4 && companyId && <FormulaDesigner companyId={companyId} canManage={canManage} />}
    {tab === 5 && <StrategyAdmin canManage={canManageStrategy} />}
    {tab === 6 && companyId && canViewReconciliation && <ReservationReconciliationAdmin companyId={companyId} />}
  </Stack>
}
