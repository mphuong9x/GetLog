"use client"

import { useState } from "react"
import { ChevronRight, Crown, Pencil, Plus, Trash2, Users, X } from "lucide-react"
import type { LucideIcon } from "lucide-react"
import { cn } from "@/lib/utils"
import { Avatar } from "@/components/org/avatar"
import type { Member, Team } from "@/lib/org-data"

function Roster({
  label,
  icon: Icon,
  members,
  addLabel,
  isLeader,
}: {
  label: string
  icon: LucideIcon
  members: Member[]
  addLabel: string
  isLeader?: boolean
}) {
  return (
    <div className="flex flex-col">
      <div className="flex items-center justify-between px-4 py-2">
        <div className="flex items-center gap-2">
          <Icon className="size-3.5 text-muted-foreground" />
          <span className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
            {label}
          </span>
          <span className="rounded bg-secondary px-1.5 py-0.5 font-mono text-[10px] text-muted-foreground">
            {members.length}
          </span>
        </div>
        <button
          type="button"
          className="flex items-center gap-1 rounded px-1.5 py-1 text-[11px] font-medium text-primary hover:bg-primary/10"
        >
          <Plus className="size-3.5" />
          {addLabel}
        </button>
      </div>

      {members.length > 0 && (
        <div role="table" className="border-t border-border">
          <div
            role="row"
            className="grid grid-cols-[1fr_140px_88px_32px] items-center gap-3 bg-secondary/40 px-4 py-1.5 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground"
          >
            <span role="columnheader">{isLeader ? "Leader" : "Member"}</span>
            <span role="columnheader" className="hidden md:block">
              Title
            </span>
            <span role="columnheader">Status</span>
            <span role="columnheader" className="sr-only">
              Actions
            </span>
          </div>

          {members.map((person, index) => (
            <div
              role="row"
              key={`${person.id}-${index}`}
              className="grid grid-cols-[1fr_140px_88px_32px] items-center gap-3 border-t border-border px-4 py-2 first:border-t-0 hover:bg-secondary/40"
            >
              <div role="cell" className="flex min-w-0 items-center gap-2.5">
                <Avatar name={person.name} />
                <span className="min-w-0">
                  <span className="flex items-center gap-1.5">
                    <span className="truncate text-[13px] font-medium text-foreground">
                      {person.name}
                    </span>
                    {isLeader && (
                      <Crown className="size-3 shrink-0 text-warning" />
                    )}
                  </span>
                  <span className="block truncate font-mono text-[11px] text-muted-foreground">
                    @{person.handle}
                  </span>
                </span>
              </div>
              <span
                role="cell"
                className="hidden truncate text-[12px] text-muted-foreground md:block"
              >
                {person.title}
              </span>
              <span role="cell">
                <span className="inline-flex items-center gap-1.5 text-[11px] font-medium text-muted-foreground">
                  <span
                    className={cn(
                      "size-1.5 rounded-full",
                      person.status === "online" ? "bg-success" : "bg-border",
                    )}
                  />
                  {person.status === "online" ? "Online" : "Offline"}
                </span>
              </span>
              <button
                role="cell"
                type="button"
                aria-label={`Remove ${person.name}`}
                className="flex size-6 items-center justify-center rounded text-muted-foreground hover:bg-secondary hover:text-destructive"
              >
                <X className="size-3.5" />
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

export function TeamSection({ team }: { team: Team }) {
  const [open, setOpen] = useState(true)

  return (
    <section className="overflow-hidden rounded-md border border-border bg-card">
      <div className="flex items-center gap-3 bg-secondary/40 px-3 py-2">
        <button
          type="button"
          onClick={() => setOpen((v) => !v)}
          aria-expanded={open}
          className="flex min-w-0 flex-1 items-center gap-2 text-left"
        >
          <ChevronRight
            className={cn(
              "size-4 shrink-0 text-muted-foreground transition-transform",
              open && "rotate-90",
            )}
          />
          <span className="truncate text-[13px] font-semibold tracking-tight text-foreground">
            {team.name}
          </span>
          <span className="hidden truncate text-[12px] text-muted-foreground sm:block">
            {team.purpose}
          </span>
        </button>
        <span className="shrink-0 font-mono text-[11px] text-muted-foreground">
          {team.leaders.length} lead · {team.members.length} members
        </span>
        <div className="flex shrink-0 items-center gap-0.5">
          <button
            type="button"
            aria-label="Edit team"
            className="flex size-6 items-center justify-center rounded text-muted-foreground hover:bg-secondary hover:text-foreground"
          >
            <Pencil className="size-3.5" />
          </button>
          <button
            type="button"
            aria-label="Delete team"
            className="flex size-6 items-center justify-center rounded text-muted-foreground hover:bg-secondary hover:text-destructive"
          >
            <Trash2 className="size-3.5" />
          </button>
        </div>
      </div>

      {open && (
        <div className="divide-y divide-border">
          <Roster
            label="Leaders"
            icon={Crown}
            members={team.leaders}
            addLabel="Add leader"
            isLeader
          />
          <Roster
            label="Members"
            icon={Users}
            members={team.members}
            addLabel="Add member"
          />
        </div>
      )}
    </section>
  )
}
