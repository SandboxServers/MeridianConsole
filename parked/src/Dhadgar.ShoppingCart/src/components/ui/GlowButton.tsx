import { clsx } from 'clsx';
import type { ButtonHTMLAttributes, ReactNode } from 'react';

export type GlowButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost';
export type GlowButtonSize = 'sm' | 'md' | 'lg';

interface GlowButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: GlowButtonVariant;
  size?: GlowButtonSize;
  isLoading?: boolean;
  icon?: ReactNode;
  fullWidth?: boolean;
  children: ReactNode;
}

const variantStyles: Record<GlowButtonVariant, string> = {
  primary: clsx(
    'bg-cyber-cyan/20 text-cyber-cyan border-cyber-cyan/50',
    'hover:bg-cyber-cyan/30 hover:border-cyber-cyan hover:shadow-glow-cyan',
    'focus:ring-cyber-cyan/50',
    'disabled:bg-cyber-cyan/10 disabled:text-cyber-cyan/50 disabled:border-cyber-cyan/30'
  ),
  secondary: clsx(
    'bg-panel-dark text-text-primary border-glow-line',
    'hover:bg-glow-line/30 hover:border-text-secondary',
    'focus:ring-glow-line',
    'disabled:bg-panel-darker disabled:text-text-muted disabled:border-glow-line/50'
  ),
  danger: clsx(
    'bg-cyber-magenta/20 text-cyber-magenta border-cyber-magenta/50',
    'hover:bg-cyber-magenta/30 hover:border-cyber-magenta hover:shadow-glow-magenta',
    'focus:ring-cyber-magenta/50',
    'disabled:bg-cyber-magenta/10 disabled:text-cyber-magenta/50 disabled:border-cyber-magenta/30'
  ),
  ghost: clsx(
    'bg-transparent text-text-primary border-transparent',
    'hover:bg-glow-line/20 hover:border-glow-line',
    'focus:ring-glow-line',
    'disabled:text-text-muted'
  ),
};

const sizeStyles: Record<GlowButtonSize, string> = {
  sm: 'px-3 py-1.5 text-sm gap-1.5',
  md: 'px-4 py-2.5 text-base gap-2',
  lg: 'px-6 py-3 text-lg gap-3',
};

export default function GlowButton({
  variant = 'primary',
  size = 'md',
  isLoading = false,
  icon,
  fullWidth = false,
  disabled,
  className,
  children,
  ...props
}: GlowButtonProps) {
  return (
    <button
      className={clsx(
        'relative inline-flex items-center justify-center font-body font-medium',
        'rounded-full border transition-all duration-200',
        'focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-offset-space-dark',
        variantStyles[variant],
        sizeStyles[size],
        fullWidth && 'w-full',
        isLoading && 'cursor-wait',
        className
      )}
      disabled={disabled || isLoading}
      {...props}
    >
      {isLoading && (
        <svg
          className="animate-spin -ml-1 mr-2 h-4 w-4"
          xmlns="http://www.w3.org/2000/svg"
          fill="none"
          viewBox="0 0 24 24"
        >
          <circle
            className="opacity-25"
            cx="12"
            cy="12"
            r="10"
            stroke="currentColor"
            strokeWidth="4"
          />
          <path
            className="opacity-75"
            fill="currentColor"
            d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
          />
        </svg>
      )}
      {!isLoading && icon && <span className="flex-shrink-0">{icon}</span>}
      {children}
    </button>
  );
}
