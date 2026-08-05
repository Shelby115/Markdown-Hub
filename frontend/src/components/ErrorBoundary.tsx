import { Component, ReactNode } from "react";

export class ErrorBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  state: { error: Error | null } = { error: null };

  static getDerivedStateFromError(error: Error) {
    return { error };
  }

  render() {
    if (this.state.error) {
      return (
        <div className="banner banner-error" style={{ margin: "2rem", whiteSpace: "pre-wrap" }}>
          <strong>Something crashed while rendering this page.</strong>
          {"\n\n"}
          {this.state.error.message}
          {this.state.error.stack ? `\n\n${this.state.error.stack}` : ""}
        </div>
      );
    }
    return this.props.children;
  }
}
