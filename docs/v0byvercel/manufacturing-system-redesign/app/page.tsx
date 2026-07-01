import { ChevronRight } from "lucide-react"
import { AppTopbar } from "@/components/app-topbar"
import { UserSummary } from "@/components/user-summary"
import { InformationSection } from "@/components/information-section"
import { RolesSection } from "@/components/roles-section"
import { user } from "@/lib/user-data"

export default function Page() {
  return (
    <main className="min-h-screen bg-background">
      <AppTopbar />

      <div className="mx-auto max-w-6xl px-6 py-6">
        <nav aria-label="Breadcrumb" className="mb-4 flex items-center gap-1 text-sm text-muted-foreground">
          <a href="#" className="transition-colors hover:text-foreground">
            Users
          </a>
          <ChevronRight className="size-3.5" />
          <span className="font-medium text-foreground">{user.fullName}</span>
        </nav>

        <div className="divide-y divide-border rounded-lg border border-border bg-card shadow-sm">
          <UserSummary user={user} />
          <InformationSection user={user} />
          <RolesSection user={user} />
        </div>
      </div>
    </main>
  )
}
