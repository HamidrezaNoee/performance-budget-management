import type { SidebarTreeNode } from './SidebarTree'

export const masterDataTree: SidebarTreeNode[] = [
  {
    label: 'اطلاعات پایه عملیاتی', path: 'operational', children: [
      { label: 'سازمان و کاربران', path: 'operational/organization', children: [
        { label: 'ساختار سازمانی', path: 'operational/organization/structure' },
        { label: 'تخصیص کاربران به سمت‌ها', path: 'operational/organization/user-positions' }
      ]},
      { label: 'کالا و خدمات', path: 'operational/products', children: [
        { label: 'کالاها', path: 'operational/products/items' },
        { label: 'برندها', path: 'operational/products/brands' },
        { label: 'واحدهای سنجش', path: 'operational/products/uom' }
      ]},
      { label: 'تأمین‌کنندگان', path: 'operational/partners/suppliers' },
      { label: 'جغرافیا', path: 'operational/geography', children: [
        { label: 'کشورها', path: 'operational/geography/countries' },
        { label: 'تقسیمات کشوری', path: 'operational/geography/divisions' }
      ]},
      { label: 'ارز', path: 'operational/currency', children: [
        { label: 'ارزها', path: 'operational/currency/currencies' },
        { label: 'نرخ ارز', path: 'operational/currency/rates' }
      ]},
      { label: 'انبارها', path: 'operational/warehouse/warehouses' },
      { label: 'گمرک‌ها و مبادی', path: 'operational/customs/customs' }
    ]
  },
  {
    label: 'برنامه‌ریزی و مالی', path: 'planning', children: [
      { label: 'تقویم و دوره‌های مالی', path: 'planning/calendar/fiscal-calendar' },
      { label: 'بودجه', path: 'planning/budget', children: [
        { label: 'سناریوها', path: 'planning/budget/scenarios' },
        { label: 'فرضیات', path: 'planning/budget/assumptions' },
        { label: 'قالب‌های Driver-Based', path: 'planning/budget/driver-templates' },
        { label: 'فرمول‌ها', path: 'planning/budget/formulas' }
      ]},
      { label: 'تطبیق و تخصیص Actual', path: 'planning/mapping/actual-budget' }
    ]
  },
  {
    label: 'مدیریت عملکرد', path: 'performance', children: [
      { label: 'اهداف و ارتباط KPI', path: 'performance/objectives' }
    ]
  }
]

export const settingsTree: SidebarTreeNode[] = [
  { label: 'کاربران، نقش‌ها و دسترسی', path: 'security/users' },
  { label: 'ساختار دسترسی سازمانی', path: 'access-hierarchy/organization' },
  { label: 'لایسنس و ظرفیت استفاده', path: 'license/overview' },
  { label: 'Service Accountها', path: 'service-accounts/manage' },
  { label: 'Idempotency و Retry', path: 'integration/idempotency' },
  { label: 'Outbox و Dead Letter', path: 'messaging/outbox' },
  { label: 'تاریخچه و Audit', path: 'audit/audit-log' }
]

export function findTreeLabel(nodes: SidebarTreeNode[], path: string): string | undefined {
  for (const node of nodes) {
    if (node.path === path) return node.label
    const child = node.children ? findTreeLabel(node.children, path) : undefined
    if (child) return child
  }
  return undefined
}
