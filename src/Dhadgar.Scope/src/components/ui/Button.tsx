import { clsx } from "clsx";
import type { ReactNode, MouseEventHandler } from "react";

interface ButtonProps {
  variant?: "filled" | "outlined" | "text";
  size?: "small" | "medium";
  color?: "primary" | "default";
  disabled?: boolean;
  href?: string;
  onClick?: MouseEventHandler<HTMLButtonElement>;
  className?: string;
  children: ReactNode;
}

export function Button({
  variant = "filled",
  size = "medium",
  color = "default",
  disabled = false,
  href,
  onClick,
  className,
  children,
}: ButtonProps) {
  const baseClasses =
    "inline-flex items-center justify-center font-semibold transition-colors rounded-xl";

  const sizeClasses = {
    small: "px-3 py-1.5 text-sm",
    medium: "px-4 py-2 text-sm",
  };

  const variantClasses = {
    filled:
      color === "primary"
        ? "bg-indigo-500 hover:bg-indigo-600 text-white"
        : "bg-white/10 hover:bg-white/15 text-white",
    outlined: "border border-white/15 bg-transparent hover:bg-white/5 text-white",
    text: "bg-transparent hover:bg-white/5 text-white/80 hover:text-white",
  };

  const classes = clsx(
    baseClasses,
    sizeClasses[size],
    variantClasses[variant],
    disabled && "opacity-50 cursor-not-allowed",
    className
  );

  if (href) {
    return (
      <a
        href={disabled ? undefined : href}
        className={classes}
        aria-disabled={disabled}
        tabIndex={disabled ? -1 : undefined}
        onClick={disabled ? (e) => e.preventDefault() : undefined}
      >
        {children}
      </a>
    );
  }

  return (
    <button type="button" onClick={onClick} disabled={disabled} className={classes}>
      {children}
    </button>
  );
}
