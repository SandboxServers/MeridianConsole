import { clsx } from "clsx";
import { useId, type ChangeEventHandler, type ReactNode } from "react";

interface TextFieldProps {
  value: string;
  onChange: ChangeEventHandler<HTMLInputElement>;
  placeholder?: string;
  label?: string;
  clearable?: boolean;
  onClear?: () => void;
  className?: string;
  icon?: ReactNode;
  id?: string;
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
  id: providedId,
}: TextFieldProps) {
  const generatedId = useId();
  const inputId = providedId ?? generatedId;

  return (
    <div className={clsx("relative", className)}>
      {label && (
        <label htmlFor={inputId} className="block text-sm text-white/60 mb-1">
          {label}
        </label>
      )}
      <div className="relative">
        {icon && (
          <div className="absolute left-3 top-1/2 -translate-y-1/2 text-indigo-400" aria-hidden="true">
            {icon}
          </div>
        )}
        <input
          id={inputId}
          type="text"
          value={value}
          onChange={onChange}
          placeholder={placeholder}
          aria-label={!label ? placeholder : undefined}
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
            aria-label="Clear input"
            className="absolute right-3 top-1/2 -translate-y-1/2 text-white/50 hover:text-white transition-colors"
          >
            <svg
              className="w-4 h-4"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              aria-hidden="true"
            >
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
