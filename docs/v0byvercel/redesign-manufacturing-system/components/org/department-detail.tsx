import { Pencil, Plus, Trash2 } from "lucide-react"
import { TeamSection } from "@/components/org/team-section"
import { departmentStats, type Department } from "@/lib/org-data"

function Stat({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="font-mono text-[15px] font-semibold leading-none text-foreground">
        {value}
      </span>
      <span className="text-[11px] uppercase tracking-wider text-muted-foreground">
        {label}
      </span>
    </div>
  )
}

export function DepartmentDetail({ dept }: { dept: Department }) {
  const { teamCount, userCount } = departmentStats(dept)

  return (
    <div className="flex h-full flex-col">
      <div className="flex flex-wrap items-start justify-between gap-4 border-b border-border px-6 py-4">
        <div className="flex items-start gap-3">
          <span className="flex size-10 items-center justify-center rounded-md bg-primary/10 font-mono text-[13px] font-semibold text-primary ring-1 ring-inset ring-primary/20">
            {dept.code.slice(0, 3)}
          </span>
          <div>
            <div className="flex items-center gap-2">
              <h1 className="text-base font-semibold tracking-tight text-foreground">
                {dept.code}
              </h1>
              <span className="text-[13px] text-muted-foreground">
                {dept.name}
              </span>
            </div>
            <div className="mt-2 flex items-center gap-6">
              <Stat label="Teams" value={teamCount} />
              <div className="h-6 w-px bg-border" />
              <Stat label="Users" value={userCount} />
              <div className="h-6 w-px bg-border" />
              <Stat label="Prod. groups" value={dept.productionGroups} />
            </div>
          </div>
        </div>

        <div className="flex items-center gap-1.5">
          <button
            type="button"
            className="flex items-center gap-1.5 rounded-md border border-border bg-card px-2.5 py-1.5 text-[12px] font-medium text-foreground hover:bg-secondary"
          >
            <Pencil className="size-3.5" />
            Edit
          </button>
          <button
            type="button"
            aria-label="Delete department"
            className="flex size-8 items-center justify-center rounded-md border border-border bg-card text-muted-foreground hover:bg-secondary hover:text-destructive"
          >
            <Trash2 className="size-3.5" />
          </button>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-6 py-4">
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
            Teams
            <span className="ml-2 rounded bg-secondary px-1.5 py-0.5 font-mono text-[10px] text-muted-foreground">
              {teamCount}
            </span>
          </h2>
          <button
            type="button"
            className="flex items-center gap-1.5 rounded-md bg-primary px-2.5 py-1.5 text-[12px] font-medium text-primary-foreground hover:opacity-90"
          >
            <Plus className="size-3.5" />
            Add team
          </button>
        </div>

        <div className="flex flex-col gap-3 pb-4">
          {dept.teams.map((team) => (
            <TeamSection key={team.id} team={team} />
          ))}
        </div>
      </div>
    </div>
  )
}
