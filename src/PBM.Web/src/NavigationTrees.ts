import type { SidebarTreeNode } from './SidebarTree'

export const masterDataTree: SidebarTreeNode[] = [
  {
    label: '1. اطلاعات پایه عملیاتی', path: 'operational', children: [
      { label: '1.1 سازمان و ساختار سازمانی', path: 'operational/organization', children: [
        { label: 'ساختار سازمانی', path: 'operational/organization/structure' },
        { label: 'تخصیص کاربران به سمت‌ها', path: 'operational/organization/user-positions' }
      ]},
      { label: '1.2 کالا و خدمات', path: 'operational/products', children: [
        { label: 'کالاها', path: 'operational/products/items' },
        { label: 'برندها', path: 'operational/products/brands' },
        { label: 'گروه‌های کالا', path: 'operational/products/groups' },
        { label: 'واحدهای سنجش', path: 'operational/products/uom' }
      ]},
      { label: '1.3 شرکای تجاری', path: 'operational/partners', children: [
        { label: 'تأمین‌کنندگان', path: 'operational/partners/suppliers' },
        { label: 'تولیدکنندگان', path: 'operational/partners/manufacturers' },
        { label: 'فروشندگان', path: 'operational/partners/vendors' },
        { label: 'سایر طرف‌های تجاری', path: 'operational/partners/others' }
      ]},
      { label: '1.4 جغرافیا', path: 'operational/geography', children: [
        { label: 'کشورها', path: 'operational/geography/countries' },
        { label: 'تقسیمات کشوری', path: 'operational/geography/divisions' }
      ]},
      { label: '1.5 ارز', path: 'operational/currency', children: [
        { label: 'ارزها', path: 'operational/currency/currencies' }
      ]},
      { label: '1.6 انبار و لجستیک', path: 'operational/warehouse', children: [
        { label: 'انبارها', path: 'operational/warehouse/warehouses' },
        { label: 'انواع انبار', path: 'operational/warehouse/types' }
      ]},
      { label: '1.7 گمرک و مبادی', path: 'operational/customs', children: [
        { label: 'گمرک‌ها', path: 'operational/customs/customs' },
        { label: 'مبادی ورودی', path: 'operational/customs/entry-points' },
        { label: 'مبادی خروجی', path: 'operational/customs/exit-points' },
        { label: 'بنادر', path: 'operational/customs/ports' },
        { label: 'فرودگاه‌ها', path: 'operational/customs/airports' },
        { label: 'پایانه‌های مرزی', path: 'operational/customs/border-terminals' }
      ]}
    ]
  },
  {
    label: '2. اطلاعات پایه برنامه‌ریزی و مالی', path: 'planning', children: [
      { label: '2.1 تقویم', path: 'planning/calendar', children: [
        { label: 'تقویم مالی', path: 'planning/calendar/fiscal-calendar' },
        { label: 'سال مالی', path: 'planning/calendar/fiscal-years' },
        { label: 'دوره مالی', path: 'planning/calendar/periods' },
        { label: 'فصل', path: 'planning/calendar/quarters' },
        { label: 'ماه', path: 'planning/calendar/months' }
      ]},
      { label: '2.2 بودجه', path: 'planning/budget', children: [
        { label: 'سناریوهای بودجه', path: 'planning/budget/scenarios' },
        { label: 'فرضیات بودجه', path: 'planning/budget/assumptions' },
        { label: 'Driverها', path: 'planning/budget/drivers' },
        { label: 'Driver-Based Budgeting Templates', path: 'planning/budget/driver-templates' },
        { label: 'فرمول‌ها', path: 'planning/budget/formulas' },
        { label: 'نسخه‌های بودجه', path: 'planning/budget/versions' },
        { label: 'دوره‌های بودجه', path: 'planning/budget/periods' }
      ]},
      { label: '2.3 Actual / Budget Mapping', path: 'planning/mapping', children: [
        { label: 'تطبیق Actual با Budget', path: 'planning/mapping/actual-budget' },
        { label: 'Mapping حساب‌ها', path: 'planning/mapping/accounts' },
        { label: 'Mapping مراکز هزینه', path: 'planning/mapping/cost-centers' },
        { label: 'Mapping کالا', path: 'planning/mapping/products' },
        { label: 'Mapping شرکت', path: 'planning/mapping/companies' },
        { label: 'Mapping دپارتمان', path: 'planning/mapping/departments' },
        { label: 'رزرو / تخصیص Actual', path: 'planning/mapping/actual-allocation' }
      ]}
    ]
  },
  {
    label: '3. اطلاعات پایه مدیریت عملکرد', path: 'performance', children: [
      { label: 'اهداف راهبردی', path: 'performance/objectives' },
      { label: 'KPI', path: 'performance/kpi' },
      { label: 'ارتباط هدف ← KPI ← Budget Driver', path: 'performance/objective-kpi-driver' }
    ]
  }
]

export const settingsTree: SidebarTreeNode[] = [
  { label: '4.1 کاربران و امنیت', path: 'security', children: [
    { label: 'کاربران', path: 'security/users' }, { label: 'نقش‌ها', path: 'security/roles' },
    { label: 'گروه‌های کاربری', path: 'security/groups' }, { label: 'سطوح دسترسی', path: 'security/access-levels' },
    { label: 'Permission', path: 'security/permissions' }, { label: 'Role', path: 'security/role-model' },
    { label: 'Data Scope', path: 'security/data-scope' }
  ]},
  { label: '4.2 سلسله‌مراتب دسترسی', path: 'access-hierarchy', children: [
    { label: 'هلدینگ', path: 'access-hierarchy/holding' }, { label: 'شرکت', path: 'access-hierarchy/company' },
    { label: 'دپارتمان', path: 'access-hierarchy/department' }, { label: 'سمت', path: 'access-hierarchy/position' },
    { label: 'کاربر', path: 'access-hierarchy/user' }, { label: 'سطح داده', path: 'access-hierarchy/data-level' }
  ]},
  { label: '4.3 لایسنس و اشتراک', path: 'license', children: [
    { label: 'License', path: 'license/license' }, { label: 'Plan', path: 'license/plan' },
    { label: 'Expiration Date', path: 'license/expiration' }, { label: 'تعداد کاربران مجاز', path: 'license/users' },
    { label: 'Feature Entitlement', path: 'license/features' }, { label: 'میزان اعتبار / Credit', path: 'license/credit' }
  ]},
  { label: '4.4 اتصال به سیستم‌های خارجی', path: 'external-systems', children: [
    { label: 'ERP', path: 'external-systems/erp' }, { label: 'سیستم حسابداری', path: 'external-systems/accounting' },
    { label: 'CRM', path: 'external-systems/crm' }, { label: 'BPMS', path: 'external-systems/bpms' },
    { label: 'BI', path: 'external-systems/bi' }, { label: 'سایر APIها', path: 'external-systems/apis' }
  ]},
  { label: '4.5 Service Account', path: 'service-accounts', children: [
    { label: 'ERP Service Account', path: 'service-accounts/erp' },
    { label: 'Accounting Service Account', path: 'service-accounts/accounting' },
    { label: 'BI Service Account', path: 'service-accounts/bi' },
    { label: 'API Client', path: 'service-accounts/api-client' },
    { label: 'Integration User', path: 'service-accounts/integration-user' }
  ]},
  { label: '4.6 تنظیمات Integration', path: 'integration', children: [
    { label: 'Endpoint', path: 'integration/endpoint' }, { label: 'Authentication', path: 'integration/authentication' },
    { label: 'Timeout', path: 'integration/timeout' }, { label: 'Retry Policy', path: 'integration/retry-policy' },
    { label: 'Idempotency', path: 'integration/idempotency' }, { label: 'Mapping', path: 'integration/mapping' }
  ]},
  { label: '4.7 مدیریت تراکنش و پیام', path: 'messaging', children: [
    { label: 'Idempotency', path: 'messaging/idempotency' }, { label: 'Retry', path: 'messaging/retry' },
    { label: 'Outbox', path: 'messaging/outbox' }, { label: 'Inbox', path: 'messaging/inbox' },
    { label: 'Dead-Letter Queue', path: 'messaging/dead-letter' }, { label: 'Reprocessing', path: 'messaging/reprocessing' }
  ]},
  { label: '4.8 تاریخچه و Audit', path: 'audit', children: [
    { label: 'تاریخچه تغییرات', path: 'audit/change-history' }, { label: 'Change Log', path: 'audit/change-log' },
    { label: 'Audit Log', path: 'audit/audit-log' }, { label: 'Login History', path: 'audit/login-history' },
    { label: 'Integration Log', path: 'audit/integration-log' }, { label: 'Error Log', path: 'audit/error-log' },
    { label: 'تغییرات اطلاعات پایه', path: 'audit/master-data-changes' }
  ]}
]

export function findTreeLabel(nodes: SidebarTreeNode[], path: string): string | undefined {
  for (const node of nodes) {
    if (node.path === path) return node.label
    const child = node.children ? findTreeLabel(node.children, path) : undefined
    if (child) return child
  }
  return undefined
}
