import { cn } from "@/lib/utils"

export function StatusBadge({ status }: { status: "Active" | "Suspended" | "Inactive" }) {
  const styles: Record<string, string> = {
    Active: "text-success",
    Suspended: "text-warning",
    Inactive: "text-muted-foreground",
  }
  const dot: Record<string, string> = {
    Active: "bg-success",
    Suspended: "bg-warning",
    Inactive: "bg-muted-foreground",
  }
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 text-sm font-medium",
        styles[status],
      )}
    >
      <span className={cn("size-1.5 rounded-full", dot[status])} aria-hidden="true" />
      {status}
    </span>
  )
}
