import { Monitor } from "lucide-react"
import type { Computer, ComputerStatus } from "@/lib/data"
import { STATUS_META } from "@/lib/data"
import { cn } from "@/lib/utils"

const ICON_COLOR: Record<string, string> = {
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  info: "text-info",
  neutral: "text-muted-foreground",
}

const LEGEND: ComputerStatus[] = [
  "error",
  "offline",
  "updating",
  "online",
  "registered",
  "unknown",
]

export function ComputersBoard({ rows }: { rows: Computer[] }) {
  const stations = Array.from(new Set(rows.map((c) => c.station)))

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1.5 px-4">
        {LEGEND.map((s) => (
          <span key={s} className="flex items-center gap-1.5">
            <Monitor className={cn("size-3.5", ICON_COLOR[STATUS_META[s].token])} />
            <span className="text-[12px] text-muted-foreground">
              {STATUS_META[s].label}
            </span>
          </span>
        ))}
      </div>

      <div className="flex flex-col divide-y divide-border border-y border-border">
        {stations.map((station) => {
          const stationRows = rows.filter((c) => c.station === station)
          const bays = Array.from(new Set(stationRows.map((c) => c.bay)))
          return (
            <div key={station} className="px-4 py-3">
              <p className="mb-2 font-mono text-[12px] font-semibold tracking-wide text-foreground">
                {station}
              </p>
              <div className="flex flex-col gap-1.5">
                {bays.map((bay) => {
                  const bayRows = stationRows.filter((c) => c.bay === bay)
                  return (
                    <div key={bay} className="flex items-center gap-3">
                      <span className="w-24 shrink-0 text-[12px] text-muted-foreground">
                        {bay}
                      </span>
                      <div className="flex flex-wrap items-center gap-1.5">
                        {bayRows.map((c) => (
                          <BoardIcon key={c.id} computer={c} />
                        ))}
                      </div>
                    </div>
                  )
                })}
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}

function BoardIcon({ computer }: { computer: Computer }) {
  const meta = STATUS_META[computer.status]
  return (
    <div className="group relative">
      <button
        type="button"
        aria-label={`${computer.name} — ${meta.label}`}
        className="flex size-7 items-center justify-center rounded-md border border-border bg-background transition-colors hover:border-border-strong"
      >
        <Monitor className={cn("size-4", ICON_COLOR[meta.token])} />
      </button>
      <div className="pointer-events-none absolute left-1/2 top-full z-10 mt-2 hidden w-56 -translate-x-1/2 rounded-lg border border-border-strong bg-popover p-3 text-popover-foreground shadow-xl group-hover:block">
        <div className="mb-2 flex items-center gap-2 border-b border-border pb-2">
          <Monitor className={cn("size-4", ICON_COLOR[meta.token])} />
          <span className="text-[13px] font-semibold">{computer.name}</span>
        </div>
        <dl className="flex flex-col gap-1 text-[12px]">
          <Row label="Status" value={meta.label} valueClass={ICON_COLOR[meta.token]} />
          <Row label="IP" value={computer.ip} mono />
          <Row label="MAC" value={computer.mac} mono />
          <Row label="Last seen" value={computer.lastSeen} />
        </dl>
      </div>
    </div>
  )
}

function Row({
  label,
  value,
  mono,
  valueClass,
}: {
  label: string
  value: string
  mono?: boolean
  valueClass?: string
}) {
  return (
    <div className="flex items-center justify-between gap-3">
      <dt className="text-muted-foreground">{label}</dt>
      <dd
        className={cn(
          "font-medium text-foreground",
          mono && "font-mono text-[11px]",
          valueClass,
        )}
      >
        {value}
      </dd>
    </div>
  )
}
