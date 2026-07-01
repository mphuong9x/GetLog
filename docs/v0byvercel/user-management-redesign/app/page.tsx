import { AppSidebar } from "@/components/app-sidebar"
import { TopBar } from "@/components/top-bar"
import { UserManagement } from "@/components/user-management"

export default function Page() {
  return (
    <div className="flex h-screen overflow-hidden bg-background">
      <AppSidebar />
      <div className="flex min-w-0 flex-1 flex-col">
        <TopBar />
        <main className="flex-1 overflow-y-auto">
          <UserManagement />
        </main>
      </div>
    </div>
  )
}
