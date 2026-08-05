import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { completeLogin } from "../auth/oidc";

/** Target of the OIDC provider's redirect back after login - completes the code+PKCE exchange
 * then returns to the app. A full-page redirect landing here (rather than Keycloak's old iframe
 * silent-check) so this works the same way across arbitrary OIDC providers, some of which block
 * being framed. */
export function AuthCallback() {
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    completeLogin()
      .then(() => navigate("/", { replace: true }))
      .catch((err) => setError(err instanceof Error ? err.message : String(err)));
  }, [navigate]);

  return (
    <div className="splash">
      {error ? (
        <>
          <h1>Sign-in failed</h1>
          <p style={{ color: "crimson" }}>{error}</p>
          <button className="primary" onClick={() => navigate("/", { replace: true })}>
            Back to Markdown Hub
          </button>
        </>
      ) : (
        <p>Completing sign-in…</p>
      )}
    </div>
  );
}
