import { redirect } from "next/navigation";

export default function RbacRedirectPage() {
  redirect("/?m=rbac");
}
