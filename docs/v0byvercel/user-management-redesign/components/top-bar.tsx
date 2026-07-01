"use client"

import { Search, Languages, PanelLeft, ChevronDown } from "lucide-react"

export function TopBar() {
  return (
    <header className="flex h-14 shrink-0 items-center gap-4 border-b border-border bg-surface px-4">
      <button
        type="button"
        aria-label="Toggle sidebar"
        className="flex size-8 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
      >
        <PanelLeft className="size-4.5" />
      </button>

      <div className="relative max-w-md flex-1">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <input
          type="text"
          placeholder="Search or type a command"
          className="h-9 w-full rounded-md border border-border bg-background pl-9 pr-16 text-[13px] text-foreground placeholder:text-muted-foreground focus:border-ring focus:outline-none focus:ring-2 focus:ring-ring/30"
        />
        <kbd className="absolute right-2.5 top-1/2 -translate-y-1/2 rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[10px] font-medium text-muted-foreground">
          ⌘K
        </kbd>
      </div>

      <div className="ml-auto flex items-center gap-1.5">
        <button
          type="button"
          aria-label="Change language"
          className="flex size-8 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
        >
          <Languages className="size-4.5" />
        </button>

        <div className="mx-1 h-6 w-px bg-border" />

        <button
          type="button"
          className="flex items-center gap-2 rounded-md py-1 pl-1 pr-2 transition-colors hover:bg-accent"
        >
          <span className="flex size-7 items-center justify-center rounded-full bg-primary/15 text-xs font-semibold text-primary">
            SA
          </span>
          <span className="text-[13px] font-medium text-foreground">System Admin</span>
          <ChevronDown className="size-3.5 text-muted-foreground" />
        </button>
      </div>
    </header>
  )
}
