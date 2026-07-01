import { Search, Bell, ChevronDown, PanelLeft } from "lucide-react"

export function Topbar() {
  return (
    <header className="flex h-14 shrink-0 items-center gap-4 border-b border-border bg-card px-4">
      <button
        type="button"
        aria-label="Toggle navigation"
        className="flex size-8 items-center justify-center rounded-md text-muted-foreground hover:bg-secondary hover:text-foreground lg:hidden"
      >
        <PanelLeft className="size-4" />
      </button>

      <div className="relative w-full max-w-md">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <input
          type="search"
          placeholder="Search or type a command"
          className="h-9 w-full rounded-md border border-input bg-background pl-9 pr-14 text-[13px] text-foreground placeholder:text-muted-foreground focus:border-ring focus:outline-none focus:ring-2 focus:ring-ring/25"
        />
        <kbd className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 rounded border border-border bg-secondary px-1.5 py-0.5 font-mono text-[10px] text-muted-foreground">
          ⌘K
        </kbd>
      </div>

      <div className="ml-auto flex items-center gap-2">
        <button
          type="button"
          aria-label="Notifications"
          className="relative flex size-8 items-center justify-center rounded-md text-muted-foreground hover:bg-secondary hover:text-foreground"
        >
          <Bell className="size-4" />
          <span className="absolute right-1.5 top-1.5 size-1.5 rounded-full bg-primary" />
        </button>

        <div className="h-6 w-px bg-border" />

        <button
          type="button"
          className="flex items-center gap-2 rounded-md py-1 pl-1 pr-2 hover:bg-secondary"
        >
          <span className="flex size-7 items-center justify-center rounded-md bg-primary text-[11px] font-semibold text-primary-foreground">
            SA
          </span>
          <span className="hidden text-left leading-tight sm:block">
            <span className="block text-[13px] font-medium text-foreground">
              System Admin
            </span>
            <span className="block text-[11px] text-muted-foreground">
              Administrator
            </span>
          </span>
          <ChevronDown className="size-4 text-muted-foreground" />
        </button>
      </div>
    </header>
  )
}
