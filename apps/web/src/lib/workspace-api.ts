/** Browser-safe typed facade over the same-origin Next.js API BFF.
 * Authorization is always enforced by the .NET backend and PostgreSQL. */
export type WorkspaceRole = 1 | 2 | 3 | 4;
export type ProjectStatus = 1 | 2 | 3 | 4 | 5;
export type Workspace = { id: string; slug: string; displayName: string; role: WorkspaceRole };
export type WorkspaceActivity = { action: string; happenedAtUtc: string };
export type WorkspaceOverview = {
  id: string; slug: string; name: string; role: WorkspaceRole;
  memberCount: number; projectCount: number; activeProjects: number;
  completedProjects: number; pendingInvitations: number; recentActivity: WorkspaceActivity[];
};
export type Project = {
  id: string; workspaceId: string; name: string; description: string;
  status: ProjectStatus; createdByUserId: string; createdAtUtc: string; updatedAtUtc: string;
};
export type ProjectInput = { name: string; description: string; status: ProjectStatus };

export const roleLabel: Record<WorkspaceRole, string> = {
  1: "Viewer", 2: "Member", 3: "Admin", 4: "Owner"
};
export const statusLabel: Record<ProjectStatus, string> = {
  1: "Planned", 2: "In progress", 3: "Blocked", 4: "Completed", 5: "Archived"
};

export class ApiError extends Error {
  constructor(public readonly status: number, message = "Request could not be completed") {
    super(message);
  }
}

async function send<T>(path: string, method: "GET" | "POST" | "PUT" | "DELETE" = "GET", body?: unknown): Promise<T> {
  const result = await fetch(path, {
    method, credentials: "same-origin", cache: "no-store",
    headers: body === undefined ? undefined : { "content-type": "application/json" },
    ...(body === undefined ? {} : { body: JSON.stringify(body) })
  });
  if (!result.ok) throw new ApiError(result.status,
    result.status === 401 ? "Session expired. Sign in again." :
      result.status === 403 ? "Your workspace role does not permit this action." :
      result.status === 404 ? "The requested record no longer exists." :
      result.status === 409 ? "Conflict. Refresh and try again." :
      result.status === 429 ? "Too many requests. Try again shortly." :
      "The request failed.");
  if (result.status === 204) return undefined as T;
  return result.json() as Promise<T>;
}

const prefix = (workspaceId: string) => `/api/workspaces/${encodeURIComponent(workspaceId)}`;
export const workspaceApi = {
  list: () => send<Workspace[]>("/api/workspaces"),
  overview: (id: string) => send<WorkspaceOverview>(`${prefix(id)}/overview`),
  projects: (id: string) => send<Project[]>(`${prefix(id)}/projects`),
  createProject: (id: string, project: ProjectInput) =>
    send<Project>(`${prefix(id)}/projects`, "POST", project),
  updateProject: (id: string, projectId: string, project: ProjectInput) =>
    send<Project>(`${prefix(id)}/projects/${encodeURIComponent(projectId)}`, "PUT", project),
  archiveProject: (id: string, projectId: string) =>
    send<void>(`${prefix(id)}/projects/${encodeURIComponent(projectId)}`, "DELETE"),
  rename: (id: string, name: string) =>
    send<Workspace>(`${prefix(id)}/settings`, "PUT", { name }),
};
