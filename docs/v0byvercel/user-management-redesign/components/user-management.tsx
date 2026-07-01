"use client"

import { useMemo, useState } from "react"
import {
  Users,
  RotateCcw,
  SlidersHorizontal,
  Calendar,
  ChevronDown,
  MoreHorizontal,
  Plus,
} from "lucide-react"
import { cn } from "@/lib/utils"

type Status = "Active" | "Pending" | "System"

type Role = { team: string; role: string; lead?: boolean }

type User = {
  ecode: string
  name: string
  email: string | null
  initial: string
  roles: Role[]
  status: Status
  joined: string
}

const USERS: User[] = [
  {
    ecode: "V000111003",
    name: "User Test 3",
    email: null,
    initial: "U",
    roles: [
      { team: "SWITCH-CFT", role: "" },
      { team: "Global", role: "Viewer" },
    ],
    status: "Active",
    joined: "27/06/2026",
  },
  {
    ecode: "V3264033",
    name: "Minh Phuong",
    email: null,
    initial: "M",
    roles: [
      { team: "EOT-CFT", role: "TeamLeader", lead: true },
      { team: "CAMERA-CFT", role: "TeamLeader", lead: true },
      { team: "Global", role: "Viewer" },
    ],
    status: "Active",
    joined: "26/06/2026",
  },
  {
    ecode: "V000111002",
    name: "User Test 2",
    email: null,
    initial: "U",
    roles: [{ team: "Global", role: "Viewer" }],
    status: "Pending",
    joined: "26/06/2026",
  },
  {
    ecode: "V000111001",
    name: "User Test 1",
    email: null,
    initial: "U",
    roles: [
      { team: "EOT-CFT", role: "" },
      { team: "Global", role: "Viewer" },
    ],
    status: "Active",
    joined: "26/06/2026",
  },
  {
    ecode: "V111222333",
    name: "Minh Phuong",
    email: null,
    initial: "M",
    roles: [
      { team: "CAMERA-CFT", role: "" },
      { team: "EOT-CFT", role: "" },
      { team: "Global", role: "Viewer" },
    ],
    status: "Active",
    joined: "13/06/2026",
  },
  {
    ecode: "_SYSTEM_",
    name: "System (Self-Announce)",
    email: "system-selfannounce@internal",
    initial: "S",
    roles: [],
    status: "System",
    joined: "10/06/2026",
  },
  {
    ecode: "ADMIN",
    name: "System Admin",
    email: "admin@system.local",
    initial: "S",
    roles: [
      { team: "SWITCH-CFT", role: "" },
      { team: "EOT-ONLINE", role: "TeamLeader", lead: true },
      { team: "EOT-CFT", role: "" },
      { team: "AP-CFT", role: "TeamLeader", lead: true },
      { team: "Global", role: "Admin" },
    ],
    status: "Active",
    joined: "10/06/2026",
  },
]

const statusStyles: Record<Status, string> = {
  Active: "text-success",
  Pending: "text-warning",
  System: "text-muted-foreground",
}

const statusDot: Record<Status, string> = {
  Active: "bg-success",
  Pending: "bg-warning",
  System: "bg-muted-foreground",
}

function FieldLabel({ children }: { children: React.ReactNode }) {
  return (
    <label className="text-[11px] font-medium uppercase tracking-wide text-muted-foreground">
      {children}
    </label>
  )
}

const inputClass =
  "h-8 w-full rounded-md border border-border bg-background px-2.5 text-[13px] text-foreground placeholder:text-muted-foreground/70 focus:border-ring focus:outline-none focus:ring-2 focus:ring-ring/30"

export function UserManagement() {
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [ecode, setEcode] = useState("")
  const [name, setName] = useState("")
  const [status, setStatus] = useState("")
  const [date, setDate] = useState("")
  const [menuOpen, setMenuOpen] = useState<string | null>(null)

  const allSelected = selected.size > 0 && selected.size === USERS.length

  function toggleAll() {
    setSelected(allSelected ? new Set() : new Set(USERS.map((u) => u.ecode)))
  }

  function toggleOne(code: string) {
    setSelected((prev) => {
      const next = new Set(prev)
      next.has(code) ? next.delete(code) : next.add(code)
      return next
    })
  }

  function reset() {
    setEcode("")
    setName("")
    setStatus("")
    setDate("")
  }

  const visible = useMemo(() => {
    return USERS.filter((u) => {
      if (ecode && !u.ecode.toLowerCase().includes(ecode.toLowerCase())) return false
      if (name && !u.name.toLowerCase().includes(name.toLowerCase())) return false
      if (status && u.status !== status) return false
      return true
    })
  }, [ecode, name, status])

  return (
    <div className="flex flex-col">
      {/* Page header */}
      <div className="flex flex-wrap items-end justify-between gap-3 px-6 pt-5 pb-4">
        <div className="flex flex-col gap-1">
          <div className="flex items-center gap-2">
            <Users className="size-4 text-primary" />
            <h1 className="text-lg font-semibold tracking-tight text-foreground">
              User Management
            </h1>
          </div>
          <p className="text-[13px] text-muted-foreground">
            Everyone here is a valued UI-TE member and colleague.
          </p>
        </div>
        <button
          type="button"
          className="inline-flex h-8 items-center gap-1.5 rounded-md bg-primary px-3 text-[13px] font-medium text-primary-foreground transition-colors hover:bg-primary/90"
        >
          <Plus className="size-4" />
          Add User
        </button>
      </div>

      {/* Filter toolbar */}
      <div className="border-y border-border bg-surface/50 px-6 py-3">
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex min-w-40 flex-1 flex-col gap-1.5">
            <FieldLabel>ECode</FieldLabel>
            <input
              className={inputClass}
              placeholder="Type ECode…"
              value={ecode}
              onChange={(e) => setEcode(e.target.value)}
            />
          </div>
          <div className="flex min-w-40 flex-1 flex-col gap-1.5">
            <FieldLabel>Name</FieldLabel>
            <input
              className={inputClass}
              placeholder="Type Name…"
              value={name}
              onChange={(e) => setName(e.target.value)}
            />
          </div>
          <div className="flex min-w-40 flex-1 flex-col gap-1.5">
            <FieldLabel>Status</FieldLabel>
            <div className="relative">
              <select
                className={cn(inputClass, "appearance-none pr-8")}
                value={status}
                onChange={(e) => setStatus(e.target.value)}
              >
                <option value="">All statuses</option>
                <option value="Active">Active</option>
                <option value="Pending">Pending</option>
                <option value="System">System</option>
              </select>
              <ChevronDown className="pointer-events-none absolute right-2.5 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
            </div>
          </div>
          <div className="flex min-w-40 flex-1 flex-col gap-1.5">
            <FieldLabel>Date</FieldLabel>
            <div className="relative">
              <input
                className={cn(inputClass, "pr-8")}
                placeholder="Select date"
                value={date}
                onChange={(e) => setDate(e.target.value)}
              />
              <Calendar className="pointer-events-none absolute right-2.5 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={reset}
              className="inline-flex h-8 items-center gap-1.5 rounded-md border border-border bg-background px-3 text-[13px] font-medium text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
            >
              <RotateCcw className="size-3.5" />
              Reset
            </button>
            <button
              type="button"
              className="inline-flex h-8 items-center gap-1.5 rounded-md bg-primary px-3 text-[13px] font-medium text-primary-foreground transition-colors hover:bg-primary/90"
            >
              <SlidersHorizontal className="size-3.5" />
              Apply Filters
            </button>
          </div>
        </div>
      </div>

      {/* Selection bar */}
      {selected.size > 0 && (
        <div className="flex items-center gap-3 border-b border-border bg-primary/10 px-6 py-2 text-[13px]">
          <span className="font-medium text-foreground">{selected.size} selected</span>
          <button className="text-muted-foreground transition-colors hover:text-foreground">
            Export
          </button>
          <button className="text-destructive transition-colors hover:text-destructive/80">
            Deactivate
          </button>
          <button
            onClick={() => setSelected(new Set())}
            className="ml-auto text-muted-foreground transition-colors hover:text-foreground"
          >
            Clear
          </button>
        </div>
      )}

      {/* Table */}
      <div className="overflow-x-auto">
        <table className="w-full min-w-[860px] border-collapse text-left">
          <thead>
            <tr className="border-b border-border text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
              <th className="w-10 py-2.5 pl-6 pr-2">
                <input
                  type="checkbox"
                  aria-label="Select all"
                  checked={allSelected}
                  onChange={toggleAll}
                  className="size-3.5 cursor-pointer rounded border-border accent-primary"
                />
              </th>
              <th className="px-3 py-2.5 font-semibold">ECode</th>
              <th className="px-3 py-2.5 font-semibold">Name</th>
              <th className="px-3 py-2.5 font-semibold">Team &amp; Role</th>
              <th className="px-3 py-2.5 font-semibold">Status</th>
              <th className="px-3 py-2.5 font-semibold">Joined Date</th>
              <th className="w-12 px-3 py-2.5 text-right font-semibold">Actions</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((u) => {
              const isSelected = selected.has(u.ecode)
              return (
                <tr
                  key={u.ecode}
                  className={cn(
                    "border-b border-border/70 transition-colors hover:bg-accent/40",
                    isSelected && "bg-primary/5",
                  )}
                >
                  <td className="py-2.5 pl-6 pr-2 align-middle">
                    <input
                      type="checkbox"
                      aria-label={`Select ${u.name}`}
                      checked={isSelected}
                      onChange={() => toggleOne(u.ecode)}
                      className="size-3.5 cursor-pointer rounded border-border accent-primary"
                    />
                  </td>
                  <td className="px-3 py-2.5 align-middle">
                    <span className="font-mono text-[12.5px] text-primary">{u.ecode}</span>
                  </td>
                  <td className="px-3 py-2.5 align-middle">
                    <div className="flex items-center gap-2.5">
                      <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-muted text-[11px] font-semibold text-muted-foreground">
                        {u.initial}
                      </span>
                      <div className="min-w-0 leading-tight">
                        <p className="truncate text-[13px] font-medium text-foreground">{u.name}</p>
                        <p className="truncate text-[11.5px] text-muted-foreground">
                          {u.email ?? "No email"}
                        </p>
                      </div>
                    </div>
                  </td>
                  <td className="px-3 py-2.5 align-middle">
                    {u.roles.length === 0 ? (
                      <span className="text-[13px] text-muted-foreground">—</span>
                    ) : (
                      <div className="flex flex-wrap items-center gap-1.5">
                        {u.roles.map((r, i) => (
                          <span
                            key={`${u.ecode}-${r.team}-${i}`}
                            className="inline-flex items-center gap-1 rounded border border-border bg-muted/50 px-1.5 py-0.5 text-[11.5px] font-medium text-foreground"
                          >
                            {r.team}
                            {r.role && (
                              <span
                                className={cn(
                                  "font-normal",
                                  r.lead ? "text-primary" : "text-muted-foreground",
                                )}
                              >
                                {r.role}
                              </span>
                            )}
                          </span>
                        ))}
                      </div>
                    )}
                  </td>
                  <td className="px-3 py-2.5 align-middle">
                    <span
                      className={cn(
                        "inline-flex items-center gap-1.5 text-[12.5px] font-medium",
                        statusStyles[u.status],
                      )}
                    >
                      <span className={cn("size-1.5 rounded-full", statusDot[u.status])} />
                      {u.status}
                    </span>
                  </td>
                  <td className="px-3 py-2.5 align-middle">
                    <span className="text-[12.5px] tabular-nums text-muted-foreground">
                      {u.joined}
                    </span>
                  </td>
                  <td className="px-3 py-2.5 text-right align-middle">
                    <div className="relative inline-block">
                      <button
                        type="button"
                        aria-label={`Actions for ${u.name}`}
                        onClick={() => setMenuOpen(menuOpen === u.ecode ? null : u.ecode)}
                        className="flex size-7 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
                      >
                        <MoreHorizontal className="size-4" />
                      </button>
                      {menuOpen === u.ecode && (
                        <>
                          <div
                            className="fixed inset-0 z-10"
                            onClick={() => setMenuOpen(null)}
                          />
                          <div className="absolute right-0 z-20 mt-1 w-36 overflow-hidden rounded-md border border-border bg-popover py-1 text-[13px] shadow-lg">
                            <button className="block w-full px-3 py-1.5 text-left text-popover-foreground transition-colors hover:bg-accent">
                              View profile
                            </button>
                            <button className="block w-full px-3 py-1.5 text-left text-popover-foreground transition-colors hover:bg-accent">
                              Edit roles
                            </button>
                            <button className="block w-full px-3 py-1.5 text-left text-destructive transition-colors hover:bg-destructive/10">
                              Deactivate
                            </button>
                          </div>
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>

      {/* Footer / pagination */}
      <div className="flex items-center justify-between px-6 py-3 text-[12.5px] text-muted-foreground">
        <span>
          Showing <span className="font-medium text-foreground">{visible.length}</span> of{" "}
          <span className="font-medium text-foreground">{USERS.length}</span> users
        </span>
        <div className="flex items-center gap-1">
          <button className="h-7 rounded-md border border-border bg-background px-2.5 text-foreground transition-colors hover:bg-accent disabled:opacity-40" disabled>
            Previous
          </button>
          <button className="h-7 rounded-md border border-border bg-background px-2.5 text-foreground transition-colors hover:bg-accent disabled:opacity-40" disabled>
            Next
          </button>
        </div>
      </div>
    </div>
  )
}
