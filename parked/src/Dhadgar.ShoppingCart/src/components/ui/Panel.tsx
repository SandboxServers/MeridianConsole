import { clsx } from 'clsx';
import type { HTMLAttributes, ReactNode } from 'react';

export type PanelVariant = 'default' | 'elevated' | 'bordered';

interface PanelProps extends HTMLAttributes<HTMLDivElement> {
  variant?: PanelVariant;
  header?: ReactNode;
  footer?: ReactNode;
  noPadding?: boolean;
  children: ReactNode;
}

const variantStyles: Record<PanelVariant, string> = {
  default: 'bg-panel-dark border-glow-line/50',
  elevated: 'bg-panel-dark border-glow-line shadow-inner-glow',
  bordered: 'bg-panel-darker border-cyber-cyan/30 shadow-glow-cyan',
};

export default function Panel({
  variant = 'default',
  header,
  footer,
  noPadding = false,
  className,
  children,
  ...props
}: PanelProps) {
  return (
    <div
      className={clsx(
        'rounded-lg border backdrop-blur-sm',
        'transition-all duration-200',
        variantStyles[variant],
        className
      )}
      {...props}
    >
      {header && (
        <div className="px-6 py-4 border-b border-glow-line/50">
          {typeof header === 'string' ? (
            <h3 className="font-display text-lg text-text-primary tracking-wide">
              {header}
            </h3>
          ) : (
            header
          )}
        </div>
      )}
      <div className={clsx(!noPadding && 'p-6')}>{children}</div>
      {footer && (
        <div className="px-6 py-4 border-t border-glow-line/50 bg-panel-darker/50">
          {footer}
        </div>
      )}
    </div>
  );
}
