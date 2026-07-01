import { AppSidebar } from "@/components/app-sidebar"
import { TopBar } from "@/components/top-bar"
import { ComputerWorkspace } from "@/components/computer-workspace"

export default function Page() {
  return (
    <div className="flex h-screen overflow-hidden bg-background">
      <AppSidebar />
      <div className="flex flex-1 flex-col overflow-hidden">
        <TopBar />
        <main className="flex-1 overflow-y-auto px-6 py-6">
          <div className="mx-auto max-w-[1400px]">
            <ComputerWorkspace />
          </div>
        </main>
      </div>
    </div>
  )
}
