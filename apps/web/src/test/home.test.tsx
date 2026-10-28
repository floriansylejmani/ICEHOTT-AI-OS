import React from "react";
import { afterEach, describe, expect, it } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import Home from "../app/page";

afterEach(cleanup);

describe("ICEHOTT AI OS foundation shell", () => {
  it("identifies the product and current development phase", () => {
    render(<Home />);
    const heading = screen.getByRole("heading", { level: 1 });
    expect(heading.textContent).toContain("One workspace");
    expect(heading.textContent).toContain("Unlimited potential");
    expect(screen.getByText("Phase 0 · Foundation")).toBeTruthy();
    expect(screen.getByText(/Development preview only/)).toBeTruthy();
  });

  it("labels future modules as planned rather than available", () => {
    render(<Home />);
    expect(screen.getAllByText("Planned")).toHaveLength(6);
    expect(screen.getByText("SaaS & Billing")).toBeTruthy();
  });

  it("has usable navigation anchors", () => {
    render(<Home />);
    const roadmap = screen.getByRole("link", { name: "Roadmap" });
    expect(roadmap.getAttribute("href")).toBe("#roadmap");
    expect(screen.getByRole("link", { name: "Explore the platform" })
      .getAttribute("href")).toBe("#platform");
  });
});
