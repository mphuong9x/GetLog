import { stats } from "@/lib/data"
import { cn } from "@/lib/utils"

const VALUE_COLOR: Record<string, string> = {
  foreground: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  "muted-foreground": "text-muted-foreground",
}

export function StatStrip() {
  return (
    <div className="grid grid-cols-2 divide-y divide-x divide-border rounded-lg border border-border bg-surface sm:grid-cols-3 sm:divide-y-0 lg:grid-cols-6">
      {stats.map((s) => (
        <div key={s.key} className="flex flex-col gap-1 px-4 py-3">
          <span className="text-[11px] font-medium uppercase tracking-wide text-muted-foreground">
            {s.label}
          </span>
          <span
            className={cn(
              "text-2xl font-semibold tabular-nums leading-none",
              VALUE_COLOR[s.token] ?? "text-foreground",
            )}
          >
            {s.value}
          </span>
        </div>
      ))}
    </div>
  )
}
