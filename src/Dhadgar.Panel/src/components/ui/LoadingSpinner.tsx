import { clsx } from 'clsx';

interface LoadingSpinnerProps {
  size?: 'sm' | 'md' | 'lg';
  className?: string;
}

const sizeStyles = {
  sm: 'h-4 w-4',
  md: 'h-8 w-8',
  lg: 'h-12 w-12',
};

export default function LoadingSpinner({ size = 'md', className }: LoadingSpinnerProps) {
  return (
    <div className={clsx('relative', sizeStyles[size], className)}>
      <div
        className={clsx(
          'absolute inset-0 rounded-full border-2 border-cyber-cyan/20',
          sizeStyles[size]
        )}
      />
      <div
        className={clsx(
          'absolute inset-0 rounded-full border-2 border-transparent border-t-cyber-cyan animate-spin',
          sizeStyles[size]
        )}
      />
    </div>
  );
}
