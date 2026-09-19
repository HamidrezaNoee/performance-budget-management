import { Alert, Card, CardContent, Stack, Typography } from '@mui/material'
import MasterDataWorkspace from './MasterDataWorkspace'
import MasterDataAdmin from './MasterDataAdmin'
import OrganizationAdmin from './OrganizationAdmin'
import SecurityAdmin from './SecurityAdmin'
import CurrencyAdmin from './CurrencyAdmin'
import CurrencyCatalogAdmin from './CurrencyCatalogAdmin'
import FiscalCalendarAdmin from './FiscalCalendarAdmin'
import ScenarioAdmin from './ScenarioAdmin'
import AssumptionsAdmin from './AssumptionsAdmin'
import DriverTemplatesAdmin from './DriverTemplatesAdmin'
import FormulaDesigner from './FormulaDesigner'
import ReservationReconciliationAdmin from './ReservationReconciliationAdmin'
import StrategyAdmin from './StrategyAdmin'

const dimensionSections = new Map<string, { title: string; dimension: string }>([
  ['operational/products/items', { title: 'کالاها', dimension: 'PRODUCT' }],
  ['operational/products/brands', { title: 'برندها', dimension: 'BRAND' }],
  ['operational/products/uom', { title: 'واحدهای سنجش', dimension: 'UOM' }],
  ['operational/partners/suppliers', { title: 'تأمین‌کنندگان', dimension: 'SUPPLIER' }],
  ['operational/geography/countries', { title: 'کشورها', dimension: 'COUNTRY' }],
  ['operational/geography/divisions', { title: 'تقسیمات کشوری', dimension: 'GEOGRAPHY' }],
  ['operational/warehouse/warehouses', { title: 'انبارها', dimension: 'WAREHOUSE' }],
  ['operational/customs/customs', { title: 'گمرک‌ها و مبادی', dimension: 'CUSTOMS' }]
])

function DimensionWorkspace({ companyId, roles, title, dimension }: { companyId: string; roles: string[]; title: string; dimension: string }) {
  return <Stack spacing={2}>
    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>{title}</Typography>
      <Typography color="text.secondary" mt={.5}>ثبت و ویرایش اطلاعات این بخش در موتور یکپارچه اطلاعات پایه انجام می‌شود.</Typography>
    </CardContent></Card>
    {companyId ? <MasterDataAdmin companyId={companyId} roles={roles} initialDimensionCode={dimension} /> : <Alert severity="warning">ابتدا شرکت را انتخاب کنید.</Alert>}
  </Stack>
}

export default function StructuredMasterDataWorkspace({ companyId, roles, section }: { companyId: string; roles: string[]; section: string }) {
  const roleSet = new Set(roles.map(role => role.toUpperCase()))
  const canManage = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || roleSet.has('BUDGET_MANAGER')

  if (!section) return <MasterDataWorkspace companyId={companyId} roles={roles} />
  if (section === 'operational/organization/structure') return <OrganizationAdmin />
  if (section === 'operational/organization/user-positions') return <SecurityAdmin showLicense={false} />

  const dimensionSection = dimensionSections.get(section)
  if (dimensionSection)
    return <DimensionWorkspace companyId={companyId} roles={roles} title={dimensionSection.title} dimension={dimensionSection.dimension} />

  if (section === 'operational/currency/currencies') return <CurrencyCatalogAdmin roles={roles} />
  if (section === 'operational/currency/rates') return <CurrencyAdmin roles={roles} />
  if (section === 'planning/calendar/fiscal-calendar')
    return companyId ? <FiscalCalendarAdmin companyId={companyId} /> : <Alert severity="warning">ابتدا شرکت را انتخاب کنید.</Alert>
  if (section === 'planning/budget/scenarios') return <ScenarioAdmin canManage={canManage} />
  if (section === 'planning/budget/assumptions')
    return companyId ? <AssumptionsAdmin companyId={companyId} canManage={canManage} /> : <Alert severity="warning">ابتدا شرکت را انتخاب کنید.</Alert>
  if (section === 'planning/budget/driver-templates')
    return companyId ? <DriverTemplatesAdmin companyId={companyId} canManage={canManage} /> : <Alert severity="warning">ابتدا شرکت را انتخاب کنید.</Alert>
  if (section === 'planning/budget/formulas')
    return companyId ? <FormulaDesigner companyId={companyId} canManage={canManage} /> : <Alert severity="warning">ابتدا شرکت را انتخاب کنید.</Alert>
  if (section === 'planning/mapping/actual-budget')
    return companyId ? <ReservationReconciliationAdmin companyId={companyId} /> : <Alert severity="warning">ابتدا شرکت را انتخاب کنید.</Alert>
  if (section === 'performance/objectives') return <StrategyAdmin canManage={canManage} />

  return <Alert severity="info">این مسیر دیگر در منوی فعال سامانه وجود ندارد.</Alert>
}
