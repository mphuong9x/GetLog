import {
  LayoutDashboard,
  Users,
  Network,
  Package,
  MonitorSmartphone,
  Boxes,
  ClipboardCheck,
  RefreshCw,
  GitBranch,
  Inbox,
  Send,
  ShieldCheck,
  BarChart3,
} from "lucide-react"
import { cn } from "@/lib/utils"

type NavItem = {
  label: string
  icon: React.ComponentType<{ className?: string }>
  active?: boolean
}

const primary: NavItem[] = [
  { label: "Dashboard", icon: LayoutDashboard },
  { label: "Users", icon: Users },
  { label: "Organization", icon: Network, active: true },
  { label: "Products", icon: Package },
  { label: "Computers", icon: MonitorSmartphone },
  { label: "Software", icon: Boxes },
]

const operations: NavItem[] = [
  { label: "Installation Jobs", icon: ClipboardCheck },
  { label: "Agent Releases", icon: RefreshCw },
  { label: "Repositories", icon: GitBranch },
]

const requests: NavItem[] = [
  { label: "Approval Inbox", icon: Inbox },
  { label: "My Requests", icon: Send },
  { label: "Role & Permissions", icon: ShieldCheck },
]

function NavGroup({ label, items }: { label: string; items: NavItem[] }) {
  return (
    <div className="flex flex-col gap-1">
      <p className="px-3 pb-1 text-[10px] font-semibold uppercase tracking-wider text-sidebar-foreground/45">
        {label}
      </p>
      {items.map((item) => (
        <a
          key={item.label}
          href="#"
          aria-current={item.active ? "page" : undefined}
          className={cn(
            "flex items-center gap-2.5 rounded-md px-3 py-1.5 text-[13px] font-medium transition-colors",
            item.active
              ? "bg-sidebar-primary text-sidebar-primary-foreground"
              : "text-sidebar-foreground/80 hover:bg-sidebar-accent hover:text-sidebar-accent-foreground",
          )}
        >
          <item.icon className="size-4 shrink-0" />
          {item.label}
        </a>
      ))}
    </div>
  )
}

export function Sidebar() {
  return (
    <aside className="hidden w-56 shrink-0 flex-col gap-6 border-r border-sidebar-border bg-sidebar px-3 py-4 lg:flex">
      <div className="flex items-center gap-2 px-2">
        <span className="flex size-8 items-center justify-center rounded-md bg-sidebar-primary text-sidebar-primary-foreground">
          <BarChart3 className="size-4" />
        </span>
        <span className="text-sm font-semibold tracking-tight text-sidebar-foreground">
          M-System
        </span>
      </div>

      <nav className="flex flex-1 flex-col gap-5 overflow-y-auto">
        <NavGroup label="Menu" items={primary} />
        <NavGroup label="Operations" items={operations} />
        <NavGroup label="Governance" items={requests} />
      </nav>

      <div className="rounded-md bg-sidebar-accent px-3 py-2.5">
        <p className="text-[11px] font-medium text-sidebar-accent-foreground">
          Production Line 4
        </p>
        <p className="text-[10px] text-sidebar-foreground/50">
          Sync active · 2m ago
        </p>
      </div>
    </aside>
  )
}
