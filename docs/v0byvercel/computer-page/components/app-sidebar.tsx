import {
  LayoutDashboard,
  Users,
  Building2,
  Package,
  Monitor,
  Boxes,
  ListChecks,
  RefreshCw,
  GitBranch,
  Inbox,
  Send,
  ShieldCheck,
  BarChart3,
} from "lucide-react"
import { cn } from "@/lib/utils"

const NAV = [
  { section: null, items: [{ label: "Dashboard", icon: LayoutDashboard }] },
  {
    section: "Manage",
    items: [
      { label: "Users", icon: Users },
      { label: "Organization", icon: Building2 },
      { label: "Products", icon: Package },
      { label: "Computers", icon: Monitor, active: true },
      { label: "Software", icon: Boxes },
    ],
  },
  {
    section: "Operations",
    items: [
      { label: "Installation Jobs", icon: ListChecks },
      { label: "Agent releases", icon: RefreshCw },
      { label: "Repositories", icon: GitBranch },
      { label: "Approval Inbox", icon: Inbox },
      { label: "My Requests", icon: Send },
      { label: "Role & Permissions", icon: ShieldCheck },
    ],
  },
]

export function AppSidebar() {
  return (
    <aside className="flex w-56 shrink-0 flex-col border-r border-border bg-surface">
      <div className="flex h-14 items-center gap-2.5 border-b border-border px-4">
        <div className="flex size-7 items-center justify-center rounded-md bg-primary">
          <BarChart3 className="size-4 text-primary-foreground" />
        </div>
        <span className="text-sm font-semibold tracking-tight">M-System</span>
      </div>

      <nav className="flex flex-1 flex-col gap-4 overflow-y-auto px-3 py-4">
        {NAV.map((group, i) => (
          <div key={i} className="flex flex-col gap-0.5">
            {group.section && (
              <p className="px-2 pb-1 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground/70">
                {group.section}
              </p>
            )}
            {group.items.map((item) => (
              <a
                key={item.label}
                href="#"
                aria-current={"active" in item && item.active ? "page" : undefined}
                className={cn(
                  "flex items-center gap-2.5 rounded-md px-2 py-1.5 text-[13px] font-medium transition-colors",
                  "active" in item && item.active
                    ? "bg-primary/15 text-foreground"
                    : "text-muted-foreground hover:bg-accent hover:text-foreground",
                )}
              >
                <item.icon
                  className={cn(
                    "size-4 shrink-0",
                    "active" in item && item.active
                      ? "text-primary"
                      : "text-muted-foreground",
                  )}
                />
                {item.label}
              </a>
            ))}
          </div>
        ))}
      </nav>
    </aside>
  )
}
