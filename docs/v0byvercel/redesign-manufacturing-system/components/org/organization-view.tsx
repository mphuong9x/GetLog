"use client"

import { useState } from "react"
import { Plus } from "lucide-react"
import { DepartmentList } from "@/components/org/department-list"
import { DepartmentDetail } from "@/components/org/department-detail"
import { departments } from "@/lib/org-data"

export function OrganizationView() {
  const [selectedId, setSelectedId] = useState(departments[0].id)
  const selected =
    departments.find((d) => d.id === selectedId) ?? departments[0]

  return (
    <section className="flex flex-1 flex-col overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border bg-card px-6 py-3">
        <div>
          <div className="flex items-center gap-2 text-[12px] text-muted-foreground">
            <span>Organization</span>
            <span aria-hidden="true">/</span>
            <span className="font-medium text-foreground">Structure</span>
          </div>
          <p className="mt-0.5 text-[12px] text-muted-foreground">
            Browse departments, their teams, and member rosters in one place.
          </p>
        </div>
        <button
          type="button"
          className="flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-[13px] font-medium text-primary-foreground hover:opacity-90"
        >
          <Plus className="size-4" />
          Add department
        </button>
      </div>

      <div className="grid flex-1 grid-cols-1 overflow-hidden lg:grid-cols-[288px_1fr]">
        <div className="hidden border-r border-border bg-card lg:block">
          <DepartmentList
            departments={departments}
            selectedId={selectedId}
            onSelect={setSelectedId}
          />
        </div>
        <div className="overflow-hidden">
          <DepartmentDetail dept={selected} />
        </div>
      </div>
    </section>
  )
}
