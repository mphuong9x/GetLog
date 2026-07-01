import { Monitor, Eye } from "lucide-react"
import type { Computer } from "@/lib/data"
import { STATUS_META } from "@/lib/data"
import { StatusText, Utilization } from "@/components/status-indicators"

export function ComputersTable({ rows }: { rows: Computer[] }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse text-left">
        <thead>
          <tr className="border-b border-border-strong">
            <Th className="pl-4">Computer</Th>
            <Th>Location</Th>
            <Th>Status</Th>
            <Th>CPU</Th>
            <Th>RAM</Th>
            <Th>Disk</Th>
            <Th className="pr-4 text-right">Actions</Th>
          </tr>
        </thead>
        <tbody>
          {rows.map((c) => {
            const meta = STATUS_META[c.status]
            return (
              <tr
                key={c.id}
                className="border-b border-border transition-colors last:border-0 hover:bg-accent/40"
              >
                <td className="py-2.5 pl-4">
                  <div className="flex items-center gap-2.5">
                    <Monitor className="size-4 shrink-0 text-muted-foreground" />
                    <span className="text-[13px] font-medium">{c.name}</span>
                  </div>
                </td>
                <td className="py-2.5">
                  <span className="font-mono text-[12px] text-muted-foreground">
                    {c.station} · {c.bay}
                  </span>
                </td>
                <td className="py-2.5">
                  <StatusText label={meta.label} token={meta.token} />
                </td>
                <td className="py-2.5">
                  <Utilization value={c.cpu} />
                </td>
                <td className="py-2.5">
                  <Utilization value={c.ram} />
                </td>
                <td className="py-2.5">
                  <Utilization value={c.disk} />
                </td>
                <td className="py-2.5 pr-4 text-right">
                  <button
                    type="button"
                    className="inline-flex items-center gap-1.5 rounded-md border border-border px-2.5 py-1 text-[12px] font-medium text-muted-foreground transition-colors hover:border-border-strong hover:bg-accent hover:text-foreground"
                  >
                    <Eye className="size-3.5" />
                    Detail
                  </button>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

function Th({
  children,
  className = "",
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <th
      className={`py-2 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground ${className}`}
    >
      {children}
    </th>
  )
}
