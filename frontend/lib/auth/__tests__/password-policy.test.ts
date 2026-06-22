import { describe, expect, it } from "vitest";
import { isPasswordCompliant } from "../password-policy";

describe("isPasswordCompliant (HU #10171/#10173)", () => {
  it.each(["NewPass123", "Abcdef1g", "DemoPass1!"])("acepta %s", (pw) => {
    expect(isPasswordCompliant(pw)).toBe(true);
  });

  it.each(["Ab1", "abcdef12", "ABCDEF12", "Abcdefgh", ""])("rechaza %s", (pw) => {
    expect(isPasswordCompliant(pw)).toBe(false);
  });
});
