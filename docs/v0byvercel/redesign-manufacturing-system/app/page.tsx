import { Sidebar } from "@/components/org/sidebar"
import { Topbar } from "@/components/org/topbar"
import { OrganizationView } from "@/components/org/organization-view"

export default function Page() {
  return (
    <div className="flex h-screen overflow-hidden bg-background">
      <Sidebar />
      <div className="flex min-w-0 flex-1 flex-col overflow-hidden">
        <Topbar />
        <OrganizationView />
      </div>
    </div>
  )
}
