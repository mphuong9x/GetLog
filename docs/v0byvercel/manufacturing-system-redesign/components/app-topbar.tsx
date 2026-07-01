import { ChevronDown, Languages, PanelLeft, Search } from "lucide-react"

export function AppTopbar() {
  return (
    <header className="sticky top-0 z-30 flex h-12 items-center gap-3 border-b border-border bg-card px-3">
      <button
        type="button"
        aria-label="Toggle navigation"
        className="flex size-8 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
      >
        <PanelLeft className="size-4" />
      </button>

      <div className="relative w-full max-w-md">
        <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <input
          type="search"
          placeholder="Search or type a command"
          aria-label="Search"
          className="h-8 w-full rounded-md border border-border bg-background pl-8 pr-14 text-sm text-foreground outline-none placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/30"
        />
        <kbd className="pointer-events-none absolute right-2 top-1/2 hidden -translate-y-1/2 items-center gap-0.5 rounded border border-border bg-muted px-1.5 font-mono text-[11px] text-muted-foreground sm:flex">
          ⌘K
        </kbd>
      </div>

      <div className="ml-auto flex items-center gap-1">
        <button
          type="button"
          aria-label="Change language"
          className="flex size-8 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
        >
          <Languages className="size-4" />
        </button>
        <button
          type="button"
          className="flex items-center gap-2 rounded-md py-1 pl-1 pr-2 transition-colors hover:bg-muted"
        >
          <span className="flex size-7 items-center justify-center rounded-full bg-primary text-xs font-semibold text-primary-foreground">
            SA
          </span>
          <span className="hidden text-sm font-medium text-foreground sm:inline">System Admin</span>
          <ChevronDown className="size-4 text-muted-foreground" />
        </button>
      </div>
    </header>
  )
}
