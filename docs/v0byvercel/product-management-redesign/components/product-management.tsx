'use client'

import { useMemo, useState } from 'react'
import {
  Boxes,
  ChevronRight,
  Clock,
  Layers,
  MapPin,
  Package,
  Plus,
  RefreshCw,
  Search,
} from 'lucide-react'
import {
  type Model,
  type ProductionGroup,
  type Station,
  productionGroups,
  workspaceStats,
} from '@/lib/product-data'
import { cn } from '@/lib/utils'

export function ProductManagement() {
  const [selectedGroupId, setSelectedGroupId] = useState('eot')
  const [selectedModelId, setSelectedModelId] = useState<string | null>('utpg3tm0t01')
  const [groupQuery, setGroupQuery] = useState('')
  const [modelQuery, setModelQuery] = useState('')
  const [stationQuery, setStationQuery] = useState('')

  const selectedGroup = useMemo(
    () => productionGroups.find((g) => g.id === selectedGroupId) ?? null,
    [selectedGroupId],
  )
  const selectedModel = useMemo(
    () => selectedGroup?.models.find((m) => m.id === selectedModelId) ?? null,
    [selectedGroup, selectedModelId],
  )

  const filteredGroups = productionGroups.filter((g) =>
    g.name.toLowerCase().includes(groupQuery.toLowerCase()),
  )
  const filteredModels = (selectedGroup?.models ?? []).filter((m) =>
    m.code.toLowerCase().includes(modelQuery.toLowerCase()),
  )
  const filteredStations = (selectedModel?.stations ?? []).filter((s) =>
    s.name.toLowerCase().includes(stationQuery.toLowerCase()),
  )

  function handleSelectGroup(group: ProductionGroup) {
    setSelectedGroupId(group.id)
    setSelectedModelId(group.models[0]?.id ?? null)
    setModelQuery('')
    setStationQuery('')
  }

  function handleSelectModel(model: Model) {
    setSelectedModelId(model.id)
    setStationQuery('')
  }

  return (
    <div className="flex h-full flex-col gap-6 px-6 py-5">
      <PageHeader />

      <div className="-mx-6 flex min-h-0 flex-1 overflow-hidden border-y border-border bg-card">
        {/* Production Groups */}
        <Column
          className="w-[clamp(240px,26%,320px)] shrink-0"
          title="Production Groups"
          count={productionGroups.length}
          query={groupQuery}
          onQuery={setGroupQuery}
          placeholder="Search groups"
        >
          {filteredGroups.length === 0 ? (
            <EmptyState label="No groups match your search" />
          ) : (
            filteredGroups.map((group) => (
              <RowButton
                key={group.id}
                selected={group.id === selectedGroupId}
                onClick={() => handleSelectGroup(group)}
              >
                <Package className="size-4 shrink-0 text-muted-foreground" />
                <span className="flex-1 truncate text-sm font-medium text-foreground">
                  {group.name}
                </span>
                <CountBadge value={group.models.length} muted={group.models.length === 0} />
                <ChevronRight className="size-4 shrink-0 text-muted-foreground/70" />
              </RowButton>
            ))
          )}
        </Column>

        {/* Models */}
        <Column
          className="flex-1"
          title="Models"
          count={selectedGroup?.models.length ?? 0}
          query={modelQuery}
          onQuery={setModelQuery}
          placeholder="Search models"
        >
          {!selectedGroup ? (
            <EmptyState label="Select a production group" />
          ) : filteredModels.length === 0 ? (
            <EmptyState
              label={
                selectedGroup.models.length === 0
                  ? `No models in ${selectedGroup.name}`
                  : 'No models match your search'
              }
            />
          ) : (
            filteredModels.map((model) => (
              <RowButton
                key={model.id}
                selected={model.id === selectedModelId}
                onClick={() => handleSelectModel(model)}
                className="items-start py-2.5"
              >
                <Layers className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
                <div className="flex min-w-0 flex-1 flex-col">
                  <span className="truncate font-mono text-sm font-medium text-foreground">
                    {model.code}
                  </span>
                  <span className="mt-0.5 truncate text-xs text-muted-foreground">
                    Owner:{' '}
                    <span
                      className={cn(
                        model.owner ? 'text-foreground/80' : 'italic text-muted-foreground',
                      )}
                    >
                      {model.owner ?? 'Unassigned'}
                    </span>
                  </span>
                </div>
                <span className="mt-0.5 rounded-full border border-border bg-secondary px-2 py-0.5 text-[11px] font-medium tabular-nums text-muted-foreground">
                  {model.stations.length} st.
                </span>
                <ChevronRight className="mt-0.5 size-4 shrink-0 text-muted-foreground/70" />
              </RowButton>
            ))
          )}
        </Column>

        {/* Stations */}
        <Column
          className="w-[clamp(280px,32%,400px)] shrink-0"
          title="Stations"
          count={selectedModel?.stations.length ?? 0}
          query={stationQuery}
          onQuery={setStationQuery}
          placeholder="Search stations"
        >
          {!selectedModel ? (
            <EmptyState label="Select a model" />
          ) : filteredStations.length === 0 ? (
            <EmptyState label="No stations match your search" />
          ) : (
            filteredStations.map((station) => (
              <StationRow key={station.id} station={station} />
            ))
          )}
        </Column>
      </div>
    </div>
  )
}

function PageHeader() {
  return (
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div className="flex flex-col gap-1.5">
        <h1 className="flex items-center gap-2 text-xl font-semibold tracking-tight text-foreground">
          <Package className="size-5 text-primary" />
          Product Management
        </h1>
        <div className="flex items-center gap-3 text-sm text-muted-foreground">
          <span>Manage your models and stations across production lines.</span>
          <span className="hidden h-3 w-px bg-border sm:block" />
          <div className="hidden items-center gap-3 sm:flex">
            <Stat icon={Boxes} value={workspaceStats.groups} label="groups" />
            <Stat icon={Layers} value={workspaceStats.models} label="models" />
            <Stat icon={MapPin} value={workspaceStats.stations} label="stations" />
          </div>
        </div>
      </div>
      <button
        type="button"
        className="flex h-9 items-center gap-2 rounded-md border border-border bg-surface px-3 text-sm font-medium text-foreground transition-colors hover:bg-secondary"
      >
        <RefreshCw className="size-4 text-muted-foreground" />
        Refresh
      </button>
    </div>
  )
}

function Stat({
  icon: Icon,
  value,
  label,
}: {
  icon: typeof Boxes
  value: number
  label: string
}) {
  return (
    <span className="flex items-center gap-1.5">
      <Icon className="size-3.5 text-muted-foreground/70" />
      <span className="font-semibold tabular-nums text-foreground">{value}</span>
      <span>{label}</span>
    </span>
  )
}

function Column({
  title,
  count,
  query,
  onQuery,
  placeholder,
  className,
  children,
}: {
  title: string
  count: number
  query: string
  onQuery: (v: string) => void
  placeholder: string
  className?: string
  children: React.ReactNode
}) {
  return (
    <section
      className={cn(
        'flex min-w-0 flex-col border-r border-border last:border-r-0',
        className,
      )}
    >
      <div className="flex h-12 shrink-0 items-center gap-2 border-b border-border px-4">
        <h2 className="text-[13px] font-semibold uppercase tracking-wide text-foreground">
          {title}
        </h2>
        <span className="flex h-5 min-w-5 items-center justify-center rounded-full bg-secondary px-1.5 text-[11px] font-semibold tabular-nums text-muted-foreground">
          {count}
        </span>
        <button
          type="button"
          aria-label={`Add ${title}`}
          className="ml-auto flex size-7 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-secondary hover:text-foreground"
        >
          <Plus className="size-4" />
        </button>
      </div>

      <div className="shrink-0 px-3 pb-2 pt-3">
        <div className="relative">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
          <input
            value={query}
            onChange={(e) => onQuery(e.target.value)}
            placeholder={placeholder}
            className="h-8 w-full rounded-md border border-input bg-background pl-8 pr-3 text-[13px] text-foreground placeholder:text-muted-foreground focus:border-ring focus:outline-none focus:ring-2 focus:ring-ring/25"
          />
        </div>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto px-2 pb-2">{children}</div>
    </section>
  )
}

function RowButton({
  selected,
  onClick,
  className,
  children,
}: {
  selected: boolean
  onClick: () => void
  className?: string
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={selected}
      className={cn(
        'relative flex w-full items-center gap-2.5 rounded-md px-3 py-2 text-left transition-colors',
        selected
          ? 'bg-accent text-foreground'
          : 'hover:bg-secondary/70',
        className,
      )}
    >
      {selected && (
        <span className="absolute inset-y-1.5 left-0 w-0.5 rounded-full bg-primary" />
      )}
      {children}
    </button>
  )
}

function StationRow({ station }: { station: Station }) {
  return (
    <div className="flex items-center gap-3 rounded-md px-3 py-2 transition-colors hover:bg-secondary/60">
      <span
        className={cn(
          'size-2 shrink-0 rounded-full',
          station.online ? 'bg-success' : 'bg-muted-foreground/40',
        )}
        aria-hidden="true"
      />
      <div className="flex min-w-0 flex-1 flex-col">
        <span className="truncate text-sm font-medium text-foreground">{station.name}</span>
        <div className="mt-0.5 flex items-center gap-2 text-xs text-muted-foreground">
          <span className="tabular-nums">
            {station.pcCount} PC{station.pcCount === 1 ? '' : 's'} connected
          </span>
          <span className="sr-only">·</span>
          <span
            className={cn(
              'inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-[11px] font-medium',
              station.schedule
                ? 'bg-warning/15 text-warning'
                : 'text-muted-foreground/80',
            )}
          >
            <Clock className="size-3" />
            {station.schedule ?? 'Anytime'}
          </span>
        </div>
      </div>
    </div>
  )
}

function CountBadge({ value, muted }: { value: number; muted?: boolean }) {
  return (
    <span
      className={cn(
        'flex h-5 min-w-5 items-center justify-center rounded-full px-1.5 text-[11px] font-semibold tabular-nums',
        muted ? 'text-muted-foreground/60' : 'bg-secondary text-foreground',
      )}
    >
      {value}
    </span>
  )
}

function EmptyState({ label }: { label: string }) {
  return (
    <div className="flex h-full min-h-32 items-center justify-center px-4 text-center text-[13px] text-muted-foreground">
      {label}
    </div>
  )
}
