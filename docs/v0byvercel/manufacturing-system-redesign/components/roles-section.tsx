import { History, Plus } from "lucide-react"
import { Button } from "@/components/ui/button"
import type { RoleAssignment, UserRecord } from "@/lib/user-data"

function ScopeCell({ role }: { role: RoleAssignment }) {
  if (role.scopeType === "Global") {
    return <span className="text-sm text-muted-foreground">Global</span>
  }
  return (
    <span className="inline-flex items-center gap-1.5 text-sm text-foreground">
      <span className="text-xs uppercase tracking-wide text-muted-foreground">Team</span>
      <span className="font-mono">{role.scopeName}</span>
    </span>
  )
}

export function RolesSection({ user }: { user: UserRecord }) {
  return (
    <section className="px-6 py-5">
      <div className="mb-3 flex items-center justify-between">
        <h2 className="text-sm font-semibold text-foreground">
          Roles &amp; access <span className="font-normal text-muted-foreground">({user.roles.length})</span>
        </h2>
        <Button size="sm">
          <Plus />
          Assign role
        </Button>
      </div>

      <div className="overflow-x-auto rounded-md border border-border">
        <table className="w-full border-collapse text-sm">
          <thead>
            <tr className="border-b border-border bg-muted/60 text-left">
              <th className="px-3 py-2 font-medium text-muted-foreground">Role</th>
              <th className="px-3 py-2 font-medium text-muted-foreground">Scope</th>
              <th className="px-3 py-2 font-medium text-muted-foreground">Effective</th>
              <th className="px-3 py-2 font-medium text-muted-foreground">Assigned</th>
              <th className="px-3 py-2 font-medium text-muted-foreground">Assigned by</th>
              <th className="px-3 py-2 text-right font-medium text-muted-foreground">Actions</th>
            </tr>
          </thead>
          <tbody>
            {user.roles.map((role) => (
              <tr
                key={role.id}
                className="border-b border-border last:border-0 transition-colors hover:bg-muted/40"
              >
                <td className="px-3 py-2.5">
                  <span className="inline-flex items-center rounded border border-accent-foreground/20 bg-accent px-1.5 py-0.5 text-xs font-medium text-accent-foreground">
                    {role.role}
                  </span>
                </td>
                <td className="px-3 py-2.5">
                  <ScopeCell role={role} />
                </td>
                <td className="px-3 py-2.5 font-mono text-xs text-muted-foreground">
                  {role.effectiveFrom} → {role.effectiveTo}
                </td>
                <td className="px-3 py-2.5 text-foreground">{role.assignedAt}</td>
                <td className="px-3 py-2.5 text-muted-foreground">{role.assignedBy}</td>
                <td className="px-3 py-2.5">
                  <div className="flex items-center justify-end gap-1">
                    <Button variant="ghost" size="xs">
                      <History />
                      History
                    </Button>
                    <Button variant="ghost" size="xs" className="text-destructive hover:bg-destructive/10">
                      Revoke
                    </Button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}
