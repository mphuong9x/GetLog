import type { UserRecord } from "@/lib/user-data"

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1">
      <dt className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</dt>
      <dd className="text-sm text-foreground">{children}</dd>
    </div>
  )
}

export function InformationSection({ user }: { user: UserRecord }) {
  return (
    <section className="px-6 py-5">
      <h2 className="mb-4 text-sm font-semibold text-foreground">Information</h2>
      <dl className="grid grid-cols-2 gap-x-6 gap-y-5 md:grid-cols-4">
        <Field label="Full name">{user.fullName}</Field>
        <Field label="Ecode">
          <span className="font-mono">{user.ecode}</span>
        </Field>
        <Field label="Email">{user.email}</Field>
        <Field label="Status">{user.status}</Field>
        <Field label="Created">{user.createdAt}</Field>
        <Field label="Last active">{user.lastActive}</Field>
        <div className="col-span-2 space-y-1 md:col-span-4">
          <dt className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
            Teams ({user.teams.length})
          </dt>
          <dd className="flex flex-wrap gap-1.5 pt-0.5">
            {user.teams.map((team) => (
              <span
                key={team}
                className="rounded border border-border bg-muted px-2 py-0.5 font-mono text-xs text-foreground"
              >
                {team}
              </span>
            ))}
          </dd>
        </div>
      </dl>
    </section>
  )
}
