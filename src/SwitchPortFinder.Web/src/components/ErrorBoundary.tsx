import { Component, type ErrorInfo, type ReactNode } from 'react';

/** A render crash shows a message and a way out instead of a blank page. The details go to the browser console. */
export default class ErrorBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  state = { error: null as Error | null };
  static getDerivedStateFromError(error: Error) { return { error }; }
  componentDidCatch(error: Error, info: ErrorInfo) { console.error('UI error:', error, info.componentStack); }
  render() {
    if (!this.state.error) return this.props.children;
    return (
      <div className="alert error" role="alert" style={{ margin: 24 }}>
        <strong>Something went wrong showing this page.</strong>
        <div className="small mono" style={{ margin: '8px 0' }}>{this.state.error.message}</div>
        <a className="btn" href="/" onClick={() => this.setState({ error: null })}>Back to dashboard</a>{' '}
        <button className="btn" onClick={() => location.reload()}>Reload</button>
      </div>
    );
  }
}
