import { useEffect } from 'react';

interface UndoToastProps {
  message: string;
  onUndo: () => void;
  onDismiss: () => void;
  durationMs?: number;
}

export function UndoToast({ message, onUndo, onDismiss, durationMs = 5000 }: UndoToastProps) {
  useEffect(() => {
    const timer = setTimeout(onDismiss, durationMs);
    return () => clearTimeout(timer);
  }, [onDismiss, durationMs]);

  return (
    <div className="undo-toast" role="alert" aria-live="assertive">
      <span>{message}</span>
      <button className="undo-btn" onClick={onUndo} aria-label="Undo action">
        Undo
      </button>
    </div>
  );
}
