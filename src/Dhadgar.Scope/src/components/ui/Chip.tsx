import { clsx } from "clsx";
import type { ReactNode } from "react";

interface ChipProps {
  size?: "small" | "medium";
  className?: string;
  children: ReactNode;
}

export function Chip({ size = "small", className, children }: ChipProps) {
  return (
    <span
      className={clsx(
        "inline-flex items-center rounded-lg bg-white/5 text-white/70",
        size === "small" ? "px-2 py-1 text-xs" : "px-3 py-1.5 text-sm",
        className
      )}
    >
      {children}
    </span>
  );
}
