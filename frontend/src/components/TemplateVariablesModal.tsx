import { useState } from "react";

export function TemplateVariablesModal({
  variables,
  onSubmit,
  onCancel,
}: {
  variables: string[];
  onSubmit: (values: Record<string, string>) => void;
  onCancel: () => void;
}) {
  const [values, setValues] = useState<Record<string, string>>(() => Object.fromEntries(variables.map((v) => [v, ""])));

  return (
    <div className="modal-overlay" onClick={onCancel}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <h2>Fill in template values</h2>
        <div className="modal-fields">
          {variables.map((v, i) => (
            <label key={v} className="modal-field">
              <span>{v}</span>
              <input
                autoFocus={i === 0}
                value={values[v]}
                onChange={(e) => setValues((prev) => ({ ...prev, [v]: e.target.value }))}
                onKeyDown={(e) => {
                  if (e.key === "Enter") onSubmit(values);
                  if (e.key === "Escape") onCancel();
                }}
              />
            </label>
          ))}
        </div>
        <div className="modal-actions">
          <button className="secondary" onClick={onCancel}>
            Cancel
          </button>
          <button onClick={() => onSubmit(values)}>Create</button>
        </div>
      </div>
    </div>
  );
}
