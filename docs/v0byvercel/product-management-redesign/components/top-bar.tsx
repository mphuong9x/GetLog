'use client'

import { ChevronDown, Languages, PanelLeft, Search } from 'lucide-react'

export function TopBar() {
  return (
    <header className="flex h-14 shrink-0 items-center gap-3 border-b border-border bg-background px-4">
      <button
        type="button"
        aria-label="Toggle navigation"
        className="flex size-8 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-secondary hover:text-foreground"
      >
        <PanelLeft className="size-4" />
      </button>

      <div className="relative w-full max-w-md">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <input
          type="search"
          placeholder="Search or type a command"
          className="h-9 w-full rounded-md border border-transparent bg-secondary pl-9 pr-14 text-sm text-foreground placeholder:text-muted-foreground focus:border-ring focus:bg-background focus:outline-none focus:ring-2 focus:ring-ring/25"
        />
        <kbd className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 rounded border border-border bg-background px-1.5 py-0.5 font-mono text-[11px] text-muted-foreground">
          ⌘K
        </kbd>
      </div>

      <div className="ml-auto flex items-center gap-1.5">
        <button
          type="button"
          aria-label="Change language"
          className="flex size-8 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-secondary hover:text-foreground"
        >
          <Languages className="size-4" />
        </button>
        <div className="mx-1 h-6 w-px bg-border" />
        <button
          type="button"
          className="flex items-center gap-2 rounded-md py-1 pl-1 pr-2 transition-colors hover:bg-secondary"
        >
          <span className="flex size-7 items-center justify-center rounded-full bg-primary text-xs font-semibold text-primary-foreground">
            SA
          </span>
          <span className="text-sm font-medium text-foreground">System Admin</span>
          <ChevronDown className="size-3.5 text-muted-foreground" />
        </button>
      </div>
    </header>
  )
}
