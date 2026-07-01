"use client"

import {
  LayoutDashboard,
  Users,
  Building2,
  Package,
  Monitor,
  AppWindow,
  ListChecks,
  Rocket,
  GitBranch,
  Inbox,
  Send,
  ShieldCheck,
  ChevronDown,
  BarChart3,
} from "lucide-react"
import { cn } from "@/lib/utils"

type NavItem = {
  label: string
  icon: React.ComponentType<{ className?: string }>
  active?: boolean
  hasChildren?: boolean
}

const navGroups: { heading: string; items: NavItem[] }[] = [
  {
    heading: "Menu",
    items: [
      { label: "Dashboard", icon: LayoutDashboard },
      { label: "Users", icon: Users, active: true },
      { label: "Organization", icon: Building2 },
      { label: "Products", icon: Package },
      { label: "Computers", icon: Monitor },
      { label: "Software", icon: AppWindow, hasChildren: true },
      { label: "Installation Jobs", icon: ListChecks },
      { label: "Agent releases", icon: Rocket },
      { label: "Repositories", icon: GitBranch },
    ],
  },
  {
    heading: "Workspace",
    items: [
      { label: "Approval Inbox", icon: Inbox },
      { label: "My Requests", icon: Send },
      { label: "Role & Permissions", icon: ShieldCheck },
    ],
  },
]

export function AppSidebar() {
  return (
    <aside className="flex w-60 shrink-0 flex-col border-r border-sidebar-border bg-sidebar">
      <div className="flex h-14 items-center gap-2.5 px-4">
        <div className="flex size-8 items-center justify-center rounded-md bg-primary text-primary-foreground">
          <BarChart3 className="size-4.5" />
        </div>
        <span className="text-[15px] font-semibold tracking-tight text-foreground">
          M-System
        </span>
      </div>

      <nav className="flex flex-1 flex-col gap-5 overflow-y-auto px-3 py-3">
        {navGroups.map((group) => (
          <div key={group.heading} className="flex flex-col gap-0.5">
            <p className="px-2 pb-1.5 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground/70">
              {group.heading}
            </p>
            {group.items.map((item) => {
              const Icon = item.icon
              return (
                <button
                  key={item.label}
                  type="button"
                  aria-current={item.active ? "page" : undefined}
                  className={cn(
                    "group flex h-8 items-center gap-2.5 rounded-md px-2 text-[13px] font-medium transition-colors",
                    item.active
                      ? "bg-sidebar-accent text-sidebar-accent-foreground"
                      : "text-sidebar-foreground hover:bg-sidebar-accent/60 hover:text-sidebar-accent-foreground",
                  )}
                >
                  <Icon
                    className={cn(
                      "size-4 shrink-0",
                      item.active ? "text-primary" : "text-muted-foreground group-hover:text-foreground",
                    )}
                  />
                  <span className="flex-1 text-left">{item.label}</span>
                  {item.hasChildren && (
                    <ChevronDown className="size-3.5 text-muted-foreground" />
                  )}
                </button>
              )
            })}
          </div>
        ))}
      </nav>

      <div className="border-t border-sidebar-border p-3">
        <div className="flex items-center gap-2.5 rounded-md px-2 py-1.5">
          <div className="flex size-8 items-center justify-center rounded-full bg-primary/15 text-xs font-semibold text-primary">
            SA
          </div>
          <div className="min-w-0 flex-1">
            <p className="truncate text-[13px] font-medium text-foreground">System Admin</p>
            <p className="truncate text-[11px] text-muted-foreground">admin@system.local</p>
          </div>
        </div>
      </div>
    </aside>
  )
}
