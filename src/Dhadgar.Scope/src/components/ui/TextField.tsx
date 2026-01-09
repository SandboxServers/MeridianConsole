import { clsx } from "clsx";
import type { ChangeEventHandler, ReactNode } from "react";

interface TextFieldProps {
  value: string;
  onChange: ChangeEventHandler<HTMLInputElement>;
  placeholder?: string;
  label?: string;
  clearable?: boolean;
  onClear?: () => void;
  className?: string;
  icon?: ReactNode;
}

export function TextField({
  value,
  onChange,
  placeholder,
  label,
  clearable = false,
  onClear,
  className,
  icon,
}: TextFieldProps) {
  return (
    <div className={clsx("relative", className)}>
      {label && <label className="block text-sm text-white/60 mb-1">{label}</label>}
      <div className="relative">
        {icon && (
          <div className="absolute left-3 top-1/2 -translate-y-1/2 text-indigo-400">{icon}</div>
        )}
        <input
          type="text"
          value={value}
          onChange={onChange}
          placeholder={placeholder}
          className={clsx(
            "w-full rounded-xl border border-white/15 bg-black/30 px-4 py-2.5 text-sm text-white placeholder-white/50",
            "focus:outline-none focus:ring-2 focus:ring-indigo-500/50 focus:border-indigo-500/50",
            "transition-colors",
            icon && "pl-10",
            clearable && value && "pr-10"
          )}
        />
        {clearable && value && (
          <button
            type="button"
            onClick={onClear}
            className="absolute right-3 top-1/2 -translate-y-1/2 text-white/50 hover:text-white transition-colors"
          >
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M6 18L18 6M6 6l12 12"
              />
            </svg>
          </button>
        )}
      </div>
    </div>
  );
}
