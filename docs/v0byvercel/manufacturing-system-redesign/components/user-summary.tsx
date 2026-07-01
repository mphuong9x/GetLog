import { Pencil, Trash2 } from "lucide-react"
import { Button } from "@/components/ui/button"
import { StatusBadge } from "@/components/status-badge"
import type { UserRecord } from "@/lib/user-data"

export function UserSummary({ user }: { user: UserRecord }) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-4 px-6 py-5">
      <div className="flex items-center gap-4">
        <span className="flex size-14 shrink-0 items-center justify-center rounded-lg bg-primary text-lg font-semibold text-primary-foreground">
          {user.initials}
        </span>
        <div className="space-y-1">
          <div className="flex items-center gap-2.5">
            <h1 className="text-xl font-semibold tracking-tight text-foreground">{user.fullName}</h1>
            <span className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-xs text-muted-foreground">
              {user.ecode}
            </span>
          </div>
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-muted-foreground">
            <span>{user.email}</span>
            <span className="text-border" aria-hidden="true">
              |
            </span>
            <StatusBadge status={user.status} />
          </div>
        </div>
      </div>

      <div className="flex items-center gap-2">
        <Button variant="outline" size="sm">
          <Pencil />
          Edit
        </Button>
        <Button variant="destructive" size="sm">
          <Trash2 />
          Delete
        </Button>
      </div>
    </div>
  )
}
