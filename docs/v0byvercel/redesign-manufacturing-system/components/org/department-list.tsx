"use client"

import { Eye, Pencil, Trash2, Plus } from "lucide-react"
import { cn } from "@/lib/utils"
import { departmentStats, type Department } from "@/lib/org-data"

function RowAction({
  label,
  icon: Icon,
  danger,
}: {
  label: string
  icon: React.ComponentType<{ className?: string }>
  danger?: boolean
}) {
  return (
    <button
      type="button"
      aria-label={label}
      onClick={(e) => e.stopPropagation()}
      className={cn(
        "flex size-6 items-center justify-center rounded text-muted-foreground transition-colors hover:bg-secondary",
        danger ? "hover:text-destructive" : "hover:text-foreground",
      )}
    >
      <Icon className="size-3.5" />
    </button>
  )
}

export function DepartmentList({
  departments,
  selectedId,
  onSelect,
}: {
  departments: Department[]
  selectedId: string
  onSelect: (id: string) => void
}) {
  return (
    <div className="flex h-full flex-col">
      <div className="flex items-center justify-between px-3 py-2.5">
        <div className="flex items-center gap-2">
          <h2 className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
            Departments
          </h2>
          <span className="rounded bg-secondary px-1.5 py-0.5 font-mono text-[10px] text-muted-foreground">
            {departments.length}
          </span>
        </div>
        <button
          type="button"
          aria-label="Add department"
          className="flex size-6 items-center justify-center rounded text-muted-foreground hover:bg-secondary hover:text-foreground"
        >
          <Plus className="size-4" />
        </button>
      </div>

      <ul className="flex-1 overflow-y-auto border-t border-border">
        {departments.map((dept) => {
          const { teamCount, userCount } = departmentStats(dept)
          const active = dept.id === selectedId
          return (
            <li
              key={dept.id}
              className={cn(
                "group relative flex items-center border-b border-border transition-colors",
                active ? "bg-accent" : "hover:bg-secondary/60",
              )}
            >
              <span
                className={cn(
                  "absolute inset-y-0 left-0 w-0.5",
                  active ? "bg-primary" : "bg-transparent",
                )}
              />
              <button
                type="button"
                onClick={() => onSelect(dept.id)}
                aria-current={active ? "true" : undefined}
                className="flex min-w-0 flex-1 items-center gap-3 px-3 py-2.5 text-left focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
              >
                <span
                  className={cn(
                    "flex size-8 shrink-0 items-center justify-center rounded-md font-mono text-[11px] font-semibold ring-1 ring-inset",
                    active
                      ? "bg-primary/10 text-primary ring-primary/20"
                      : "bg-secondary text-muted-foreground ring-border",
                  )}
                >
                  {dept.code.slice(0, 3)}
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-[13px] font-semibold text-foreground">
                    {dept.code}
                  </span>
                  <span className="block truncate text-[11px] text-muted-foreground">
                    {teamCount} teams · {userCount} users
                  </span>
                </span>
              </button>
              <span className="flex items-center gap-0.5 pr-2 opacity-0 transition-opacity focus-within:opacity-100 group-hover:opacity-100">
                <RowAction label="View department" icon={Eye} />
                <RowAction label="Edit department" icon={Pencil} />
                <RowAction label="Delete department" icon={Trash2} danger />
              </span>
            </li>
          )
        })}
      </ul>
    </div>
  )
}
