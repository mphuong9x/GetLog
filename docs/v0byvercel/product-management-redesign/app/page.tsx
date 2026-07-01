import { NavSidebar } from '@/components/nav-sidebar'
import { ProductManagement } from '@/components/product-management'
import { TopBar } from '@/components/top-bar'

export default function Page() {
  return (
    <div className="flex h-screen w-full overflow-hidden bg-background text-foreground">
      <NavSidebar />
      <div className="flex min-w-0 flex-1 flex-col">
        <TopBar />
        <main className="min-h-0 flex-1 overflow-hidden">
          <ProductManagement />
        </main>
      </div>
    </div>
  )
}
