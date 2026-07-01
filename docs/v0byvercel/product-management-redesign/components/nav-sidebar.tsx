'use client'

import {
  Building2,
  ChevronDown,
  ClipboardCheck,
  Code,
  GitBranch,
  Inbox,
  LayoutDashboard,
  type LucideIcon,
  Monitor,
  Package,
  RefreshCcw,
  Send,
  ShieldCheck,
  Users,
} from 'lucide-react'
import { cn } from '@/lib/utils'

type NavItem = {
  label: string
  icon: LucideIcon
  active?: boolean
  hasChildren?: boolean
}

type NavSection = {
  heading: string | null
  items: NavItem[]
}

const sections: NavSection[] = [
  {
    heading: 'Menu',
    items: [
      { label: 'Dashboard', icon: LayoutDashboard },
      { label: 'Users', icon: Users },
      { label: 'Organization', icon: Building2 },
      { label: 'Products', icon: Package, active: true },
      { label: 'Computers', icon: Monitor },
      { label: 'Software', icon: Code, hasChildren: true },
    ],
  },
  {
    heading: 'Workspace',
    items: [
      { label: 'Installation Jobs', icon: ClipboardCheck },
      { label: 'Agent releases', icon: RefreshCcw },
      { label: 'Repositories', icon: GitBranch },
      { label: 'Approval Inbox', icon: Inbox },
      { label: 'My Requests', icon: Send },
      { label: 'Role & Permissions', icon: ShieldCheck },
    ],
  },
]

export function NavSidebar() {
  return (
    <aside className="flex w-60 shrink-0 flex-col border-r border-sidebar-border bg-sidebar">
      <div className="flex h-14 items-center gap-2 px-4">
        <div className="flex size-7 items-center justify-center rounded-md bg-primary text-primary-foreground">
          <svg viewBox="0 0 20 20" className="size-4" aria-hidden="true">
            <rect x="3" y="10" width="3" height="7" rx="1" fill="currentColor" />
            <rect x="8.5" y="6" width="3" height="11" rx="1" fill="currentColor" />
            <rect x="14" y="3" width="3" height="14" rx="1" fill="currentColor" />
          </svg>
        </div>
        <span className="text-sm font-semibold tracking-tight text-sidebar-foreground">
          M-System
        </span>
      </div>

      <nav className="flex-1 overflow-y-auto px-2 py-2">
        {sections.map((section, i) => (
          <div key={section.heading ?? `section-${i}`} className={cn(i > 0 && 'mt-5')}>
            {section.heading && (
              <p className="px-3 pb-1 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground/70">
                {section.heading}
              </p>
            )}
            <ul className="flex flex-col gap-0.5">
              {section.items.map((item) => (
                <li key={item.label}>
                  <button
                    type="button"
                    aria-current={item.active ? 'page' : undefined}
                    className={cn(
                      'group relative flex w-full items-center gap-2.5 rounded-md px-3 py-2 text-sm font-medium transition-colors',
                      item.active
                        ? 'bg-secondary text-foreground'
                        : 'text-sidebar-foreground hover:bg-secondary/60 hover:text-foreground',
                    )}
                  >
                    {item.active && (
                      <span className="absolute inset-y-1.5 left-0 w-0.5 rounded-full bg-primary" />
                    )}
                    <item.icon
                      className={cn(
                        'size-[18px] shrink-0',
                        item.active ? 'text-primary' : 'text-muted-foreground',
                      )}
                    />
                    <span className="truncate">{item.label}</span>
                    {item.hasChildren && (
                      <ChevronDown className="ml-auto size-3.5 text-muted-foreground" />
                    )}
                  </button>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </nav>

      <div className="border-t border-sidebar-border p-3">
        <button
          type="button"
          className="flex w-full items-center gap-2.5 rounded-md p-1.5 text-left transition-colors hover:bg-secondary/60"
        >
          <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-secondary text-xs font-semibold text-foreground">
            SA
          </span>
          <span className="flex min-w-0 flex-col">
            <span className="truncate text-sm font-medium text-sidebar-foreground">
              System Admin
            </span>
            <span className="truncate text-xs text-muted-foreground">admin@system.local</span>
          </span>
        </button>
      </div>
    </aside>
  )
}
