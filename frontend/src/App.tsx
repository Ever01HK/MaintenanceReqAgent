import { useCallback, useEffect, useState, type FormEvent } from 'react';
import {
  approveMaintenance,
  getApproval,
  listApprovals,
  type ApprovalFilter,
  processMaintenance,
  rejectMaintenance,
  type AgentResponse,
  type Approval,
} from './api/maintenanceApi';
import './App.css';

type View = 'tenant' | 'manager';

function StatusBadge({ status }: { status: string | null | undefined }) {
  const value = status || 'No action';
  const tone = value.toLowerCase() === 'approved' ? 'approved' : value.toLowerCase() === 'rejected' ? 'rejected' : 'pending';
  return <span className={`status-pill ${tone}`}>{value}</span>;
}

function DetailGrid({ propertyId, description, category, priority, requestId }: {
  propertyId?: number;
  description?: string;
  category?: string;
  priority?: string;
  requestId?: number | null;
}) {
  return <div className="details-grid">
    <div><small>Property ID</small><strong>{propertyId ?? '—'}</strong></div>
    <div><small>Category</small><strong>{category || '—'}</strong></div>
    <div><small>Priority</small><strong>{priority || '—'}</strong></div>
    <div><small>Maintenance request ID</small><strong>{requestId ?? 'Not created yet'}</strong></div>
    <div className="detail-wide"><small>AI-prepared description</small><p>{description || '—'}</p></div>
  </div>;
}

export default function App() {
  const [view, setView] = useState<View>('tenant');
  const [propertyId, setPropertyId] = useState('1');
  const [message, setMessage] = useState('Please create a maintenance request. The kitchen sink is leaking and water is spreading across the floor.');
  const [approvals, setApprovals] = useState<Approval[]>([]);
  const [filter, setFilter] = useState<ApprovalFilter>('Pending');
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [listLoading, setListLoading] = useState(false);
  const [listError, setListError] = useState('');
  const [result, setResult] = useState<AgentResponse | null>(null);
  const [approval, setApproval] = useState<Approval | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [requestKey, setRequestKey] = useState<string | null>(null);

  async function submitIssue(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const id = Number(propertyId);
    if (!Number.isSafeInteger(id) || id <= 0) { setError('Enter a valid positive property ID.'); return; }
    if (!message.trim() || message.length > 4000) { setError('Enter a description of up to 4,000 characters.'); return; }
    setBusy(true); setError(''); setNotice(''); setResult(null); setApproval(null);
    // Keep the key on retry; create a new key when the user edits the form.
    const key = requestKey ?? crypto.randomUUID();
    setRequestKey(key);
    try {
      const data = await processMaintenance({ propertyId: id, message: message.trim(), idempotencyKey: key });
      setResult(data);
      setNotice(data.reused ? 'An existing request was found and reused.' : data.requiresApproval ? 'AI analysis completed. Manager approval is required.' : 'AI response received. No approval was created.');
    } catch (e) { setError(e instanceof Error ? e.message : 'Something went wrong.'); }
    finally { setBusy(false); }
  }

  function editProperty(value: string) { setPropertyId(value); setRequestKey(null); }
  function editMessage(value: string) { setMessage(value); setRequestKey(null); }

  const refreshApprovals = useCallback(async (activeFilter: ApprovalFilter, activePage: number) => {
    setListLoading(true);
    setListError('');
    try {
      const data = await listApprovals(activeFilter, activePage);
      setApprovals(data.items);
      setTotalCount(data.totalCount);
    } catch (e) {
      setListError(e instanceof Error ? e.message : 'Could not load approvals.');
    } finally {
      setListLoading(false);
    }
  }, []);

  useEffect(() => {
    if (view === 'manager') void refreshApprovals(filter, page);
  }, [view, filter, page, refreshApprovals]);

  async function selectApproval(id: number) {
    setBusy(true); setError(''); setNotice('');
    try {
      const current = await getApproval(id);
      setApproval(current);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load approval.');
    } finally { setBusy(false); }
  }

  async function managerAction(action: 'approve' | 'reject') {
    if (!approval || approval.status !== 'Pending') return;
    const id = approval.id;
    setBusy(true); setError(''); setNotice('');
    try {
      if (action === 'approve') await approveMaintenance(id);
      else await rejectMaintenance(id);
      const current = await getApproval(id);
      setApproval(current);
      setNotice(action === 'approve' ? 'Approved. The maintenance request has been created.' : 'Rejected. No maintenance request was created.');
      if (result?.approvalId === id) {
        setResult({ ...result, status: current.status, requiresApproval: false, executed: current.status === 'Approved' && current.maintenanceRequestId != null, maintenanceRequestId: current.maintenanceRequestId });
      }
      await refreshApprovals(filter, page);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Something went wrong.');
      // Refresh detail and list after a conflict (another reviewer may have processed it).
      try { setApproval(await getApproval(id)); } catch { /* Preserve original error. */ }
      await refreshApprovals(filter, page);
    } finally { setBusy(false); }
  }

  function openManager() { setView('manager'); setError(''); setNotice(''); }
  const canReview = approval !== null && approval.status === 'Pending';
  const totalPages = Math.max(1, Math.ceil(totalCount / 20));

  return <div className="app-shell">
    <aside className="sidebar">
      <div className="brand"><span className="brand-mark">M</span><div><strong>Maintain<span>AI</span></strong><small>PROPERTY OPERATIONS</small></div></div>
      <div className="sidebar-label">WORKSPACE</div>
      <nav aria-label="Main navigation">
        <button className={view === 'tenant' ? 'nav-link active' : 'nav-link'} onClick={() => { setView('tenant'); setError(''); setNotice(''); }}><span>▤</span> Report an issue</button>
        <button className={view === 'manager' ? 'nav-link active' : 'nav-link'} onClick={() => { setView('manager'); setError(''); setNotice(''); }}><span>✓</span> Manager approval</button>
      </nav>
      <div className="sidebar-bottom"><span className="online-dot"/> AI-assisted maintenance workflow<small>Demo application · Not affiliated with Keyhook</small></div>
    </aside>
    <main className="main-area">
      <header className="topbar"><span>Maintenance / {view === 'tenant' ? 'Report an issue' : 'Manager approval'}</span><div className="topbar-pill">AI + human review</div></header>
      <div className="page-content">
        <div className="eyebrow">SMART PROPERTY MANAGEMENT</div>
        <h1>{view === 'tenant' ? 'Report a maintenance issue' : 'Review maintenance requests'}</h1>
        <p className="lead">{view === 'tenant' ? 'Describe what needs fixing. Gemini can prepare a structured request for a manager to review.' : 'Only a pending approval can be approved or rejected. Approval creates the actual maintenance request.'}</p>
        {error && <div className="alert error" role="alert">{error}</div>}
        {notice && <div className="alert success" role="status">{notice}</div>}
        {view === 'tenant' ? <>
          <div className="content-grid">
            <section className="panel"><div className="panel-heading"><div className="step-icon">01</div><div><h2>Issue details</h2><p>Describe the problem at your property</p></div></div>
              <form onSubmit={submitIssue}>
                <label htmlFor="property">Property ID</label><input id="property" type="number" min="1" step="1" required value={propertyId} onChange={e => editProperty(e.target.value)}/>
                <label htmlFor="description">What's the problem?</label><textarea id="description" rows={6} maxLength={4000} required value={message} onChange={e => editMessage(e.target.value)} placeholder="Please create a maintenance request for..."/>
                <div className="helper">{message.length}/4000 characters · Explicitly ask to create a maintenance request for the demo.</div>
                <button className="primary-btn" disabled={busy || !message.trim()}>{busy ? 'Analysing…' : 'Submit with AI assistance →'}</button>
              </form>
            </section>
            <section className="panel aside-panel"><div className="panel-heading"><div className="step-icon teal">✦</div><div><h2>How it works</h2><p>AI-assisted, human-approved</p></div></div>
              <div className="timeline"><div><b>1</b><span><strong>Describe the issue</strong><small>Submit a maintenance problem in plain language.</small></span></div><div><b>2</b><span><strong>Gemini prepares a request</strong><small>AI proposes the category, priority and description.</small></span></div><div><b>3</b><span><strong>Manager reviews</strong><small>Approval creates a maintenance request in the database.</small></span></div></div>
              <div className="info-box">✧ AI prepares the request, but a person decides whether it is created.</div>
            </section>
          </div>
          {result && <section className="panel result-panel">
            <div className="result-header"><div><div className="eyebrow">GEMINI RESPONSE</div><h2>{result.approvalId != null ? 'Maintenance approval prepared' : 'AI response'}</h2></div><StatusBadge status={result.status}/></div>
            <p className="agent-message">{result.message}</p>
            {result.approvalId != null && <>
              <div className="approval-reference">Approval ID: <strong>#{result.approvalId}</strong>{result.reused && <span className="reused-note">Previously submitted</span>}</div>
              <DetailGrid propertyId={result.propertyId} description={result.description} category={result.category} priority={result.priority} requestId={result.maintenanceRequestId}/>
              {result.requiresApproval && <button className="primary-btn next-step" onClick={openManager}>Review as manager →</button>}
            </>}
            {result.approvalId == null && <p>No approval record was created. Try explicitly asking Gemini to create a maintenance request.</p>}
          </section>}
        </> : <section className="panel manager-panel">
          <div className="panel-heading"><div className="step-icon">02</div><div><h2>Approval centre</h2><p>Review incoming maintenance requests</p></div></div>
          <div className="list-toolbar">
            <label htmlFor="status-filter">Status <select id="status-filter" value={filter} onChange={e => { setFilter(e.target.value as ApprovalFilter); setPage(1); setApproval(null); }}>
              <option value="Pending">Pending</option><option value="All">All</option><option value="Approved">Approved</option><option value="Rejected">Rejected</option>
            </select></label>
            <button className="secondary-btn" disabled={listLoading} onClick={() => void refreshApprovals(filter, page)}>{listLoading ? 'Loading…' : '↻ Refresh list'}</button>
          </div>
          {listError && <div className="alert error" role="alert">{listError}</div>}
          <div className="list-summary">{totalCount} request{totalCount === 1 ? '' : 's'} · Page {page} of {totalPages}</div>
          <div className="approval-list" aria-label="Maintenance approval requests">
            {approvals.map(item => <button type="button" key={item.id} className={`approval-list-item ${approval?.id === item.id ? 'selected' : ''}`} disabled={busy} onClick={() => void selectApproval(item.id)}>
              <div className="list-item-top"><strong>Request #{item.id}</strong><StatusBadge status={item.status}/></div>
              <p>{item.description}</p>
              <div className="list-meta"><span>Property #{item.propertyId}</span><span>{item.category}</span><span>{item.priority} priority</span><span>{item.createdAt ? new Date(item.createdAt).toLocaleString() : ''}</span></div>
            </button>)}
            {!listLoading && approvals.length === 0 && !listError && <div className="empty-list">No {filter === 'All' ? '' : filter.toLowerCase()} requests found.</div>}
          </div>
          <div className="pagination"><button className="secondary-btn" disabled={page <= 1 || listLoading} onClick={() => setPage(v => v - 1)}>← Previous</button><span>{page} / {totalPages}</span><button className="secondary-btn" disabled={page >= totalPages || listLoading} onClick={() => setPage(v => v + 1)}>Next →</button></div>
          {approval && <div className="approval-card"><div className="result-header"><h3>Approval #{approval.id}</h3><StatusBadge status={approval.status}/></div><DetailGrid propertyId={approval.propertyId} description={approval.description} category={approval.category} priority={approval.priority} requestId={approval.maintenanceRequestId}/>
            <div className="action-row"><button className="primary-btn" disabled={busy || !canReview} onClick={() => void managerAction('approve')}>✓ Approve & create request</button><button className="danger-btn" disabled={busy || !canReview} onClick={() => void managerAction('reject')}>Reject</button></div>
            {approval.status !== 'Pending' && <p className="helper">This approval has already been processed. Further review actions are disabled.</p>}
          </div>}
        </section>}
        <footer>MaintainAI · Demonstration project · ASP.NET Core + PostgreSQL + Gemini</footer>
      </div>
    </main>
  </div>;
}
