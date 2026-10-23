# Frontend (React + Vite)

This folder contains the tenant and manager UI for AI Maintenance Agent.

## Scope

- Tenant issue submission form.
- AI response display including approval metadata.
- Manager approval center:
  - list approvals
  - filter by status
  - pagination
  - approve or reject pending items

The frontend calls backend endpoints through a Vite dev proxy.

## Scripts

```bash
npm install
npm run dev
npm run build
npm run lint
npm run preview
```

## Local Development Notes

- Vite dev server proxies /api requests to http://localhost:5247.
- Ensure backend API is running before UI testing.

## API Client

Main API bindings are in src/api/maintenanceApi.ts.

Current frontend workflow uses:

- POST /api/agent/process
- GET /api/maintenance-approvals/{id}
- GET /api/maintenance-approvals
- POST /api/maintenance-approvals/{id}/approve
- POST /api/maintenance-approvals/{id}/reject

## Testing Status

- No automated frontend unit/integration tests yet.
- No E2E test automation yet.
- Current frontend verification is manual in local development.

For project-wide setup and backend details, see ../README.md.
