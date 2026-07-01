import { cn } from "@/lib/utils"

const TOKEN_TEXT: Record<string, string> = {
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  info: "text-info",
  neutral: "text-muted-foreground",
}

const TOKEN_DOT: Record<string, string> = {
  success: "bg-success",
  warning: "bg-warning",
  danger: "bg-danger",
  info: "bg-info",
  neutral: "bg-muted-foreground/60",
}

const TOKEN_SOFT: Record<string, string> = {
  success: "bg-success/12 text-success",
  warning: "bg-warning/15 text-warning",
  danger: "bg-danger/15 text-danger",
  info: "bg-info/15 text-info",
  neutral: "bg-muted text-muted-foreground",
}

export function StatusText({
  label,
  token,
  className,
}: {
  label: string
  token: string
  className?: string
}) {
  return (
    <span className={cn("inline-flex items-center gap-2", className)}>
      <span
        className={cn(
          "size-1.5 rounded-full",
          TOKEN_DOT[token] ?? TOKEN_DOT.neutral,
        )}
      />
      <span
        className={cn(
          "text-[13px] font-medium",
          TOKEN_TEXT[token] ?? TOKEN_TEXT.neutral,
        )}
      >
        {label}
      </span>
    </span>
  )
}

export function Tag({
  label,
  token,
  className,
}: {
  label: string
  token: string
  className?: string
}) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-md px-1.5 py-0.5 text-[11px] font-medium tabular-nums",
        TOKEN_SOFT[token] ?? TOKEN_SOFT.neutral,
        className,
      )}
    >
      {label}
    </span>
  )
}

function usageToken(value: number) {
  if (value >= 90) return "danger"
  if (value >= 70) return "warning"
  return "success"
}

const TOKEN_BAR: Record<string, string> = {
  success: "bg-success",
  warning: "bg-warning",
  danger: "bg-danger",
}

export function Utilization({ value }: { value: number | null }) {
  if (value === null) {
    return <span className="text-[13px] tabular-nums text-muted-foreground/50">—</span>
  }
  const token = usageToken(value)
  return (
    <div className="flex items-center gap-2">
      <div className="h-1 w-16 overflow-hidden rounded-full bg-border-strong">
        <div
          className={cn("h-full rounded-full", TOKEN_BAR[token])}
          style={{ width: `${value}%` }}
        />
      </div>
      <span className="w-8 text-right text-[13px] tabular-nums text-foreground/80">
        {value}%
      </span>
    </div>
  )
}
