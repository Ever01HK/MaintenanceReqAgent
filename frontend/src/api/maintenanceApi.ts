export interface ProcessRequest {
  propertyId: number;
  message: string;
  idempotencyKey: string;
}

export interface AgentResponse {
  message: string;
  tool?: string;
  executed: boolean;
  requiresApproval: boolean;
  approvalId: number | null;
  propertyId?: number;
  description?: string;
  category?: string;
  priority?: string;
  status: string | null;
  maintenanceRequestId?: number | null;
  reused: boolean;
}

export interface Approval {
  id: number;
  propertyId: number;
  description: string;
  category: string;
  priority: string;
  status: string;
  maintenanceRequestId: number | null;
  createdAt?: string;
  reviewedAt?: string | null;
}

export interface ApprovalActionResponse {
  message?: string;
  approvalId: number;
  maintenanceRequestId?: number;
  status: 'Approved' | 'Rejected';
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  let response: Response;
  try {
    response = await fetch(path, {
      ...options,
      headers: { 'Content-Type': 'application/json', ...options.headers },
    });
  } catch {
    throw new Error('Cannot reach the API. Check that the ASP.NET Core server is running and the Vite proxy target is correct.');
  }

  const body: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    let detail = `Request failed (HTTP ${response.status}).`;
    if (body && typeof body === 'object') {
      const data = body as Record<string, unknown>;
      if (typeof data.error === 'string') detail = data.error;
      else if (typeof data.message === 'string') detail = data.message;
      else if (typeof data.title === 'string') detail = data.title;
    }
    throw new Error(detail);
  }
  return body as T;
}

export const processMaintenance = (payload: ProcessRequest) =>
  request<AgentResponse>('/api/agent/process', { method: 'POST', body: JSON.stringify(payload) });

export const getApproval = (id: number) =>
  request<Approval>(`/api/maintenance-approvals/${id}`);

export const approveMaintenance = (id: number) =>
  request<ApprovalActionResponse>(`/api/maintenance-approvals/${id}/approve`, { method: 'POST' });

export const rejectMaintenance = (id: number) =>
  request<ApprovalActionResponse>(`/api/maintenance-approvals/${id}/reject`, { method: 'POST' });

export interface ApprovalListResponse {
  items: Approval[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export type ApprovalFilter = 'All' | 'Pending' | 'Approved' | 'Rejected';

export const listApprovals = (filter: ApprovalFilter = 'Pending', page = 1, pageSize = 20) => {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (filter !== 'All') params.set('status', filter);
  return request<ApprovalListResponse>(`/api/maintenance-approvals?${params}`);
};
