import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { PledgeDocumentOverrideToggle } from "../PledgeDocumentOverrideToggle";

const fetchOtPrendaDocumentPoliciesForOffice = vi.fn();
const setOtPrendaDocumentPolicyForOffice = vi.fn();
const show = vi.fn();

vi.mock("@/lib/api/admin-ot-prenda-document-policies", () => ({
  fetchOtPrendaDocumentPoliciesForOffice: (...a: unknown[]) =>
    fetchOtPrendaDocumentPoliciesForOffice(...a),
  setOtPrendaDocumentPolicyForOffice: (...a: unknown[]) => setOtPrendaDocumentPolicyForOffice(...a),
}));

vi.mock("@/components/admin/Toast", () => ({
  useToast: () => ({ show }),
}));

const OT = "ot-1";

describe("PledgeDocumentOverrideToggle — prenda opcional por compañía", () => {
  beforeEach(() => {
    fetchOtPrendaDocumentPoliciesForOffice.mockReset();
    setOtPrendaDocumentPolicyForOffice.mockReset();
    show.mockReset();
    fetchOtPrendaDocumentPoliciesForOffice.mockResolvedValue([
      { tenantId: "t1", tenantName: "Gestora Uno", documentOptional: false },
    ]);
    setOtPrendaDocumentPolicyForOffice.mockResolvedValue(undefined);
  });

  it("carga compañías y permite activar prenda opcional", async () => {
    const user = userEvent.setup();
    render(<PledgeDocumentOverrideToggle transitOfficeId={OT} />);

    const toggle = await screen.findByRole("switch", { name: /gestora uno — prenda opcional/i });
    expect(toggle).not.toBeChecked();

    await user.click(toggle);
    await waitFor(() =>
      expect(setOtPrendaDocumentPolicyForOffice).toHaveBeenCalledWith(OT, "t1", true),
    );
  });

  it("muestra vacío si no hay compañías", async () => {
    fetchOtPrendaDocumentPoliciesForOffice.mockResolvedValue([]);
    render(<PledgeDocumentOverrideToggle transitOfficeId={OT} />);
    expect(
      await screen.findByText(/no hay compañías habilitadas/i),
    ).toBeInTheDocument();
  });
});
