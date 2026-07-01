import { Search, Languages, ChevronDown } from "lucide-react"

export function TopBar() {
  return (
    <header className="flex h-14 shrink-0 items-center gap-4 border-b border-border bg-surface px-4">
      <div className="relative w-full max-w-md">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <input
          type="text"
          placeholder="Search or type a command"
          className="h-9 w-full rounded-md border border-border bg-background pl-9 pr-14 text-[13px] text-foreground placeholder:text-muted-foreground focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
        />
        <kbd className="absolute right-2.5 top-1/2 -translate-y-1/2 rounded border border-border bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
          ⌘K
        </kbd>
      </div>

      <div className="ml-auto flex items-center gap-3">
        <button
          type="button"
          aria-label="Change language"
          className="flex size-8 items-center justify-center rounded-md text-muted-foreground hover:bg-accent hover:text-foreground"
        >
          <Languages className="size-4" />
        </button>
        <div className="h-6 w-px bg-border" />
        <button
          type="button"
          className="flex items-center gap-2 rounded-md py-1 pl-1 pr-2 hover:bg-accent"
        >
          <span className="flex size-7 items-center justify-center rounded-full bg-primary text-[11px] font-semibold text-primary-foreground">
            SA
          </span>
          <span className="text-[13px] font-medium">System Admin</span>
          <ChevronDown className="size-4 text-muted-foreground" />
        </button>
      </div>
    </header>
  )
}
