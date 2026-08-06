import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { setToken } from "../auth/auth";

/** Target of both the local-login flow's provider buttons and the API's external-provider
 * callback redirect (Controllers/AuthController.cs) - the token (or an error message) arrives in
 * the URL fragment rather than a query string so it never lands in server access logs or the
 * Referer header of any request this page happens to make. */
export function AuthCallback() {
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const params = new URLSearchParams(window.location.hash.replace(/^#/, ""));
    const token = params.get("token");
    const errorMessage = params.get("error");

    if (token) {
      setToken(token);
      navigate("/", { replace: true });
    } else {
      setError(errorMessage ?? "Sign-in did not complete.");
    }
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
