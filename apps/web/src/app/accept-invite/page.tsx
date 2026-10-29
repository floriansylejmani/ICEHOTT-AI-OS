import { Suspense } from "react";
import AcceptAction from "./AcceptAction";
type Props = { searchParams: Promise<{ token?: string }> };
async function Content({ searchParams }: Props) {
  const { token } = await searchParams;
  return <AcceptAction token={token ?? ""} />;
}
export default function AcceptInvitePage(props: Props) {
  return <Suspense fallback={<p className="p-8 text-slate-300">Loading invitation…</p>}>
    <Content {...props} />
  </Suspense>;
}
