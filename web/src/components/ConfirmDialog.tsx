import { useEffect, useId, useRef, type ReactNode } from "react";
import { TriangleAlert, type LucideIcon } from "lucide-react";

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  children: ReactNode;
  confirmLabel: string;
  /** Label while the action runs; the dialog stays open until the caller closes it. */
  busyLabel?: string;
  cancelLabel?: string;
  busy?: boolean;
  icon?: LucideIcon;
  /** Shown inside the dialog, e.g. when the action failed. */
  error?: ReactNode;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * Modal confirmation for destructive actions, built on the native <dialog> element: focus is trapped
 * and restored, Esc cancels, and it's announced as a dialog. A click on the backdrop also cancels.
 */
export function ConfirmDialog({
  open, title, children, confirmLabel, busyLabel, cancelLabel = "Cancel", busy = false,
  icon: Icon = TriangleAlert, error, onConfirm, onCancel,
}: ConfirmDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const descriptionId = useId();

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      if (typeof dialog.showModal === "function") dialog.showModal();
      else dialog.setAttribute("open", ""); // environments without <dialog> support (e.g. jsdom)
    } else if (!open && dialog.open) {
      if (typeof dialog.close === "function") dialog.close();
      else dialog.removeAttribute("open");
    }
  }, [open]);

  return (
    <dialog ref={dialogRef} className="dialog" aria-labelledby={titleId} aria-describedby={descriptionId}
      onCancel={(event) => {
        event.preventDefault(); // Esc: let React state drive closing
        if (!busy) onCancel();
      }}
      onClick={(event) => {
        if (event.target === event.currentTarget && !busy) onCancel(); // backdrop click
      }}>
      <div className="dialog-panel">
        <div className="dialog-icon" aria-hidden="true"><Icon size={22} /></div>
        <div className="dialog-body">
          <h2 id={titleId} className="dialog-title">{title}</h2>
          <div id={descriptionId} className="dialog-text">{children}</div>
          {error && <div className="dialog-error" role="alert">{error}</div>}
        </div>
        <div className="dialog-actions">
          <button type="button" className="button button-secondary" onClick={onCancel} disabled={busy} autoFocus>
            {cancelLabel}
          </button>
          <button type="button" className="button button-destructive" onClick={onConfirm} disabled={busy}>
            {busy ? (busyLabel ?? confirmLabel) : confirmLabel}
          </button>
        </div>
      </div>
    </dialog>
  );
}
