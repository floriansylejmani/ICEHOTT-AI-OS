import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import DashboardPage from "../app/dashboard/page";

const replace = vi.fn();
const api = vi.hoisted(() => ({
  list: vi.fn(),
  overview: vi.fn(),
  projects: vi.fn(),
  createProject: vi.fn(),
  updateProject: vi.fn(),
  archiveProject: vi.fn(),
  rename: vi.fn()
}));
vi.mock("next/navigation", () => ({ useRouter: () => ({ replace }) }));
vi.mock("../lib/workspace-api", async (importOriginal) => {
  const original = await importOriginal<typeof import("@/lib/workspace-api")>();
  return { ...original, workspaceApi: api };
});
const first = { id: "aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa", slug: "alpha", displayName: "Alpha", role: 1 as const };
const baseOverview = {
  id: first.id, slug: "alpha", name: "Alpha", role: 1 as const,
  memberCount: 4, projectCount: 1, activeProjects: 1, completedProjects: 0, pendingInvitations: 0,
  recentActivity: [{ action: "project.created", happenedAtUtc: "2026-10-29T00:00:00Z" }]
};
const project = {
  id: "bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb", workspaceId: first.id,
  name: "Product launch", description: "Launch work", status: 2 as const,
  createdByUserId: "cccccccc-cccc-4ccc-cccc-cccccccccccc",
  createdAtUtc: "2026-10-29T00:00:00Z", updatedAtUtc: "2026-10-29T00:00:00Z",
};

beforeEach(() => {
  vi.resetAllMocks();
  api.list.mockResolvedValue([first]);
  api.overview.mockResolvedValue({ ...baseOverview });
  api.projects.mockResolvedValue([project]);
  api.createProject.mockResolvedValue(project);
  api.updateProject.mockResolvedValue(project);
  api.archiveProject.mockResolvedValue(undefined);
  api.rename.mockResolvedValue(first);
});
afterEach(cleanup);

describe("Phase 2 role-gated workspace dashboard", () => {
  it("loads authoritative stats and audit and keeps Viewer read-only", async () => {
    render(<DashboardPage />);
    expect(await screen.findByRole("heading", { name: "Workspace overview" })).toBeTruthy();
    expect(await screen.findByText("Product launch")).toBeTruthy();
    expect(screen.getByText("project created")).toBeTruthy();
    expect(screen.getAllByTestId("stat-value")[0].textContent).toBe("1");
    fireEvent.click(screen.getByRole("button", { name: /^Projects$/ }));
    expect(screen.getByText("Your Viewer role can read projects but cannot change them.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Create project" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Archive" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Settings" }));
    expect(screen.getByText(/Admin or Owner access is required/)).toBeTruthy();
    expect((screen.getByRole("button", { name: "Save settings" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("lets authorized members create projects and owners update settings", async () => {
    api.list.mockResolvedValue([{ ...first, role: 4 }]);
    api.overview.mockResolvedValue({ ...baseOverview, role: 4 });
    render(<DashboardPage />);
    await screen.findByText("Product launch");
    fireEvent.click(screen.getByRole("button", { name: /^Projects$/ }));
    const name = screen.getByRole("textbox", { name: "Project name" });
    fireEvent.change(name, { target: { value: "Client console" } });
    fireEvent.click(screen.getByRole("button", { name: "Create project" }));
    await waitFor(() => expect(api.createProject).toHaveBeenCalledWith(first.id,
      { name: "Client console", description: "", status: 1 }));
    fireEvent.click(screen.getByRole("button", { name: "Settings" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Workspace name" }), { target: { value: "New Alpha" } });
    fireEvent.click(screen.getByRole("button", { name: "Save settings" }));
    await waitFor(() => expect(api.rename).toHaveBeenCalledWith(first.id, "New Alpha"));
  });

  it("switches workspaces without carrying prior tenant projects", async () => {
    const second = { id: "dddddddd-dddd-4ddd-dddd-dddddddddddd", slug: "beta", displayName: "Beta", role: 1 as const };
    api.list.mockResolvedValue([first, second]);
    api.overview.mockImplementation((id: string) => Promise.resolve({ ...baseOverview, id, name: id === first.id ? "Alpha" : "Beta" }));
    api.projects.mockImplementation((id: string) => Promise.resolve(id === first.id ? [project] : []));
    render(<DashboardPage />);
    await screen.findByText("Product launch");
    fireEvent.change(screen.getByRole("combobox", { name: "Choose workspace" }), { target: { value: second.id } });
    await waitFor(() => expect(api.projects).toHaveBeenCalledWith(second.id));
    await waitFor(() => expect(screen.queryByText("Product launch")).toBeNull());
    expect(screen.getByText("No projects yet. Add one from the Projects tab.")).toBeTruthy();
  });
});
