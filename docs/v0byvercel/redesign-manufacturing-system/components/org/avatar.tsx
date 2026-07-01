import { cn } from "@/lib/utils"

function initials(name: string) {
  return name
    .split(" ")
    .map((part) => part[0])
    .slice(0, 2)
    .join("")
    .toUpperCase()
}

export function Avatar({
  name,
  className,
}: {
  name: string
  className?: string
}) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        "inline-flex size-6 shrink-0 items-center justify-center rounded-md bg-secondary text-[10px] font-semibold tracking-wide text-secondary-foreground ring-1 ring-inset ring-border",
        className,
      )}
    >
      {initials(name)}
    </span>
  )
}
