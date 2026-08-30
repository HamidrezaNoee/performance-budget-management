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
import CatalogPlaceholder from './CatalogPlaceholder'

const masterDimensionSections = new Map<string, { title: string; dimension: string }>([
  ['operational/products/items', { title: 'کالاها', dimension: 'PRODUCT' }],
  ['operational/products/brands', { title: 'برندها', dimension: 'BRAND' }],
  ['operational/products/uom', { title: 'واحدهای سنجش', dimension: 'UOM' }],
  ['operational/partners/suppliers', { title: 'تأمین‌کنندگان', dimension: 'SUPPLIER' }],
  ['operational/geography/countries', { title: 'کشورها', dimension: 'COUNTRY' }],
  ['operational/geography/divisions', { title: 'تقسیمات کشوری', dimension: 'GEOGRAPHY' }],
  ['operational/warehouse/warehouses', { title: 'انبارها', dimension: 'WAREHOUSE' }],
  ['operational/customs/customs', { title: 'گمرک‌ها و مبادی گمرکی', dimension: 'CUSTOMS' }]
])

function DimensionWorkspace({ companyId, roles, title, dimension }: { companyId: string; roles: string[]; title: string; dimension: string }) {
  return <Stack spacing={2}>
    <Card elevation={0}><CardContent>
      <Typography variant="h6" fontWeight={900}>{title}</Typography>
      <Typography color="text.secondary" mt={.5}>این موجودیت در موتور اطلاعات پایه فعال است و فرم ثبت/ویرایش آن مستقیماً برای همین نوع باز شده است.</Typography>
    </CardContent></Card>
    {companyId ? <MasterDataAdmin companyId={companyId} roles={roles} initialDimensionCode={dimension} /> : <Alert severity="warning">برای مدیریت این اطلاعات ابتدا باید یک شرکت در دسترس باشد.</Alert>}
  </Stack>
}

export default function StructuredMasterDataWorkspace({ companyId, roles, section }: { companyId: string; roles: string[]; section: string }) {
  const roleSet = new Set(roles.map(x => x.toUpperCase()))
  const canManage = roleSet.has('SUPERADMIN') || roleSet.has('ADMIN') || roleSet.has('BUDGET_MANAGER')

  if (!section) return <MasterDataWorkspace companyId={companyId} roles={roles} />

  if (['operational/organization/companies', 'operational/organization/structure', 'operational/organization/positions'].includes(section))
    return <OrganizationAdmin />
  if (section === 'operational/organization/user-positions') return <SecurityAdmin showLicense={false} />

  const dimensionSection = masterDimensionSections.get(section)
  if (dimensionSection) return <DimensionWorkspace companyId={companyId} roles={roles} title={dimensionSection.title} dimension={dimensionSection.dimension} />

  if (section === 'operational/products/groups') return <CatalogPlaceholder title="گروه‌های کالا" description="ساختار گروه‌بندی کالا به‌صورت درختی طراحی خواهد شد و هر کالا می‌تواند به یک گروه متصل شود." fields={['کد گروه', 'عنوان فارسی', 'عنوان انگلیسی', 'گروه بالادستی', 'وضعیت']} />
  if (section === 'operational/partners/manufacturers') return <CatalogPlaceholder title="تولیدکنندگان" fields={['کد', 'نام فارسی', 'نام انگلیسی', 'کشور', 'شناسه مالیاتی', 'آدرس']} />
  if (section === 'operational/partners/vendors') return <CatalogPlaceholder title="فروشندگان" fields={['کد', 'نام', 'نوع فروشنده', 'کشور', 'اطلاعات تماس']} />
  if (section === 'operational/partners/others') return <CatalogPlaceholder title="سایر طرف‌های تجاری" fields={['کد', 'نام', 'نوع طرف تجاری', 'کشور', 'اطلاعات تماس']} />

  if (section === 'operational/currency/currencies') return <CurrencyCatalogAdmin roles={roles} />
  if (section === 'operational/currency/rates') return <CurrencyAdmin roles={roles} />
  if (section === 'operational/warehouse/types') return <CatalogPlaceholder title="انواع انبار" fields={['کد نوع انبار', 'عنوان', 'شرح', 'وضعیت']} />

  const customsPlaceholders: Record<string, string> = {
    'operational/customs/entry-points': 'مبادی ورودی',
    'operational/customs/exit-points': 'مبادی خروجی',
    'operational/customs/ports': 'بنادر',
    'operational/customs/airports': 'فرودگاه‌ها',
    'operational/customs/border-terminals': 'پایانه‌های مرزی'
  }
  if (customsPlaceholders[section]) return <CatalogPlaceholder title={customsPlaceholders[section]} fields={['کد', 'نام فارسی', 'نام انگلیسی', 'کشور', 'موقعیت جغرافیایی', 'Latitude', 'Longitude']} />

  if (section.startsWith('planning/calendar/')) return companyId ? <FiscalCalendarAdmin companyId={companyId} /> : <Alert severity="warning">ابتدا شرکت را انتخاب کنید.</Alert>
  if (section === 'planning/budget/scenarios') return <ScenarioAdmin canManage={canManage} />
  if (section === 'planning/budget/assumptions') return companyId ? <AssumptionsAdmin companyId={companyId} canManage={canManage} /> : <Alert severity="warning">ابتدا شرکت را انتخاب کنید.</Alert>
  if (section === 'planning/budget/drivers') return <CatalogPlaceholder title="Driverهای بودجه" fields={['کد Driver', 'عنوان', 'واحد', 'منبع داده', 'فرمول / منطق', 'وضعیت']} />
  if (section === 'planning/budget/driver-templates') return companyId ? <DriverTemplatesAdmin companyId={companyId} canManage={canManage} /> : <Alert severity="warning">ابتدا شرکت را انتخاب کنید.</Alert>
  if (section === 'planning/budget/formulas') return companyId ? <FormulaDesigner companyId={companyId} canManage={canManage} /> : <Alert severity="warning">ابتدا شرکت را انتخاب کنید.</Alert>
  if (section === 'planning/budget/versions') return <CatalogPlaceholder title="نسخه‌های بودجه" description="نسخه بودجه یک Master Data ساده نیست و باید با چرخه Revision/Approval بودجه هماهنگ باشد. صفحه اختصاصی آن در همین مسیر قرار می‌گیرد." fields={['شماره نسخه', 'عنوان نسخه', 'سناریو', 'وضعیت', 'تاریخ ایجاد', 'نسخه مبنا']} />
  if (section === 'planning/budget/periods') return companyId ? <FiscalCalendarAdmin companyId={companyId} /> : <Alert severity="warning">ابتدا شرکت را انتخاب کنید.</Alert>

  if (section === 'planning/mapping/actual-budget' || section === 'planning/mapping/actual-allocation') return companyId ? <ReservationReconciliationAdmin companyId={companyId} /> : <Alert severity="warning">ابتدا شرکت را انتخاب کنید.</Alert>
  const mappingLabels: Record<string, string> = {
    'planning/mapping/accounts': 'Mapping حساب‌ها', 'planning/mapping/cost-centers': 'Mapping مراکز هزینه',
    'planning/mapping/products': 'Mapping کالا', 'planning/mapping/companies': 'Mapping شرکت', 'planning/mapping/departments': 'Mapping دپارتمان'
  }
  if (mappingLabels[section]) return <CatalogPlaceholder title={mappingLabels[section]} description="این Mapping برای تبدیل کدهای سیستم مبدأ به کدهای استاندارد PBM استفاده خواهد شد." fields={['سیستم مبدأ', 'کد مبدأ', 'موجودیت مقصد', 'کد مقصد', 'تاریخ اعتبار', 'وضعیت']} />

  if (section === 'performance/objectives' || section === 'performance/objective-kpi-driver') return <StrategyAdmin canManage={canManage} />
  if (section === 'performance/kpi') return <CatalogPlaceholder title="تعاریف KPI" fields={['کد KPI', 'عنوان', 'فرمول', 'واحد', 'Target', 'Threshold', 'Frequency', 'Data Source']} />

  return <CatalogPlaceholder title="اطلاعات پایه" description="ساختار این صفحه ایجاد شده و جزئیات فرم آن در ادامه تکمیل می‌شود." />
}
