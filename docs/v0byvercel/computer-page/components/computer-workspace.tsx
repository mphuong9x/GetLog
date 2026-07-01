"use client"

import { useMemo, useState } from "react"
import {
  Monitor,
  AlertCircle,
  Inbox,
  TriangleAlert,
  Table2,
  LayoutGrid,
  ChevronDown,
} from "lucide-react"
import { computers, needsAttention, STATUS_META } from "@/lib/data"
import type { ComputerStatus } from "@/lib/data"
import { cn } from "@/lib/utils"
import { StatStrip } from "@/components/stat-strip"
import { ComputersTable } from "@/components/computers-table"
import { ComputersBoard } from "@/components/computers-board"
import { NeedsAttentionTable } from "@/components/needs-attention-table"

type Tab = "all" | "attention" | "unassigned" | "drift"
type View = "table" | "board"

const TABS: { key: Tab; label: string; icon: typeof Monitor; count: number }[] = [
  { key: "all", label: "All computers", icon: Monitor, count: 10 },
  { key: "attention", label: "Needs attention", icon: AlertCircle, count: 7 },
  { key: "unassigned", label: "Unassigned", icon: Inbox, count: 0 },
  { key: "drift", label: "Drift", icon: TriangleAlert, count: 0 },
]

export function ComputerWorkspace() {
  const [tab, setTab] = useState<Tab>("all")
  const [view, setView] = useState<View>("table")
  const [model, setModel] = useState("all")
  const [station, setStation] = useState("all")
  const [status, setStatus] = useState("all")

  const models = useMemo(
    () => Array.from(new Set(computers.map((c) => c.model))),
    [],
  )
  const stations = useMemo(
    () => Array.from(new Set(computers.map((c) => c.station))),
    [],
  )

  const filtered = useMemo(() => {
    return computers.filter((c) => {
      if (model !== "all" && c.model !== model) return false
      if (station !== "all" && c.station !== station) return false
      if (status !== "all" && c.status !== status) return false
      return true
    })
  }, [model, station, status])

  const attentionRows = needsAttention(computers)

  return (
    <div className="flex flex-col gap-6">
      <div>
        <div className="flex items-center gap-2">
          <Monitor className="size-4 text-primary" />
          <h1 className="text-sm font-semibold uppercase tracking-wide text-foreground">
            Computer &amp; Agent Management
          </h1>
        </div>
        <p className="mt-1 max-w-2xl text-[13px] leading-relaxed text-muted-foreground">
          Monitor agent status and heartbeat, generate enrollment tokens, and
          revoke agents when a computer needs re-enrollment.
        </p>
      </div>

      <StatStrip />

      {/* Tabs */}
      <div className="border-b border-border">
        <div className="flex flex-wrap items-center gap-1">
          {TABS.map((t) => {
            const active = tab === t.key
            return (
              <button
                key={t.key}
                type="button"
                onClick={() => setTab(t.key)}
                className={cn(
                  "-mb-px flex items-center gap-2 border-b-2 px-3 py-2.5 text-[13px] font-medium transition-colors",
                  active
                    ? "border-primary text-foreground"
                    : "border-transparent text-muted-foreground hover:text-foreground",
                )}
              >
                <t.icon className="size-4" />
                {t.label}
                <span
                  className={cn(
                    "rounded px-1.5 py-0.5 text-[11px] tabular-nums",
                    active
                      ? "bg-primary/20 text-primary"
                      : "bg-muted text-muted-foreground",
                  )}
                >
                  {t.count}
                </span>
              </button>
            )
          })}
        </div>
      </div>

      {tab === "attention" ? (
        <section className="flex flex-col gap-3">
          <div>
            <h2 className="text-[15px] font-semibold">Needs attention</h2>
            <p className="mt-0.5 text-[13px] text-muted-foreground">
              Computers flagged as offline, in error/crash-loop, low on disk
              (≥90% used), or running drifted software.
            </p>
          </div>
          <div className="rounded-lg border border-border bg-surface">
            <NeedsAttentionTable rows={attentionRows} />
          </div>
        </section>
      ) : (
        <section className="flex flex-col gap-4">
          {/* Filter bar */}
          <div className="flex flex-wrap items-end justify-between gap-3">
            <div className="flex flex-wrap items-end gap-3">
              <Select
                label="Model"
                value={model}
                onChange={setModel}
                options={[{ value: "all", label: "All models" }, ...models.map((m) => ({ value: m, label: m }))]}
              />
              <Select
                label="Station"
                value={station}
                onChange={setStation}
                options={[{ value: "all", label: "All stations" }, ...stations.map((s) => ({ value: s, label: s }))]}
              />
              <Select
                label="Status"
                value={status}
                onChange={setStatus}
                options={[
                  { value: "all", label: "All statuses" },
                  ...(Object.keys(STATUS_META) as ComputerStatus[]).map((s) => ({
                    value: s,
                    label: STATUS_META[s].label,
                  })),
                ]}
              />
            </div>
            <div className="flex items-center gap-3">
              <span className="text-[12px] tabular-nums text-muted-foreground">
                {filtered.length} of {computers.length}
              </span>
              <div className="flex items-center rounded-md border border-border p-0.5">
                <ViewButton
                  active={view === "table"}
                  onClick={() => setView("table")}
                  icon={Table2}
                  label="Table"
                />
                <ViewButton
                  active={view === "board"}
                  onClick={() => setView("board")}
                  icon={LayoutGrid}
                  label="Board"
                />
              </div>
            </div>
          </div>

          <div>
            <h2 className="text-[15px] font-semibold">Agent inventory</h2>
            <p className="mt-0.5 text-[13px] text-muted-foreground">
              Click Detail for agent, heartbeat and runtime information.
              Heartbeat is considered stale after 15 minutes.
            </p>
          </div>

          <div className="rounded-lg border border-border bg-surface py-1">
            {view === "table" ? (
              <ComputersTable rows={filtered} />
            ) : (
              <ComputersBoard rows={filtered} />
            )}
          </div>
        </section>
      )}
    </div>
  )
}

function Select({
  label,
  value,
  onChange,
  options,
}: {
  label: string
  value: string
  onChange: (v: string) => void
  options: { value: string; label: string }[]
}) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
        {label}
      </span>
      <div className="relative">
        <select
          value={value}
          onChange={(e) => onChange(e.target.value)}
          className="h-9 w-44 appearance-none rounded-md border border-border bg-background pl-3 pr-8 text-[13px] text-foreground focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
        >
          {options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        <ChevronDown className="pointer-events-none absolute right-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
      </div>
    </label>
  )
}

function ViewButton({
  active,
  onClick,
  icon: Icon,
  label,
}: {
  active: boolean
  onClick: () => void
  icon: typeof Table2
  label: string
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "flex items-center gap-1.5 rounded px-2.5 py-1 text-[12px] font-medium transition-colors",
        active
          ? "bg-primary/15 text-primary"
          : "text-muted-foreground hover:text-foreground",
      )}
    >
      <Icon className="size-3.5" />
      {label}
    </button>
  )
}
