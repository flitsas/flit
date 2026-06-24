"use client";

/** Badge de estado — patrón CompanyListTable / DocumentTypeListTable. */
export interface OtStatusBadgeProps {
  label: string;
  tone?: "success" | "warning" | "danger" | "neutral";
}

const TONE_BG: Record<NonNullable<OtStatusBadgeProps["tone"]>, string> = {
  success: "#00DBD5",
  warning: "#557EFF",
  danger: "#FF4E00",
  neutral: "#162744",
};

export function OtStatusBadge({ label, tone = "neutral" }: OtStatusBadgeProps) {
  return (
    <span
      className="rounded-full px-2 py-0.5 text-[10px] font-semibold text-white"
      style={{ background: TONE_BG[tone] }}
    >
      {label}
    </span>
  );
}
