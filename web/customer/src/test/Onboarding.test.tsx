import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Onboarding } from "../views/Onboarding";
import { json, stubApi } from "./render";

afterEach(() => vi.unstubAllGlobals());

describe("Abertura de conta", () => {
  it("se a conta falhar depois do cadastro, tentar de novo não cadastra outra vez", async () => {
    let accountCalls = 0;
    const fetchMock = stubApi({
      "POST /api/v1/customers": () => json(201, { id: "c1", name: "Carla Dev", document: "***", status: "active" }),
      "POST /api/v1/accounts": () => (++accountCalls === 1 ? json(503, { title: "ocupado", code: "contention" }) : json(201, { id: "a1" })),
    });
    const onDone = vi.fn();
    const user = userEvent.setup();
    render(<Onboarding customer={null} onDone={onDone} />);

    await user.type(screen.getByLabelText("Nome completo"), "Carla Dev");
    await user.type(screen.getByLabelText("CPF"), "52998224725");
    await user.click(screen.getByRole("button", { name: "Abrir conta" }));
    await screen.findByText(/O banco está ocupado agora/);
    await user.click(screen.getByRole("button", { name: "Abrir conta" }));

    await vi.waitFor(() => expect(onDone).toHaveBeenCalled());
    expect(fetchMock.mock.calls.filter(([path]) => path === "/api/v1/customers")).toHaveLength(1);
  });

  it("nome sem sobrenome e CPF curto param antes da API", async () => {
    const fetchMock = stubApi({});
    const user = userEvent.setup();
    render(<Onboarding customer={null} onDone={vi.fn()} />);

    await user.type(screen.getByLabelText("Nome completo"), "Carla");
    await user.click(screen.getByRole("button", { name: "Abrir conta" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Informe nome e sobrenome.");
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
