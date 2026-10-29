import { Suspense } from "react";
import ResetAction from "./ResetAction";
type Props = { searchParams: Promise<{ token?: string }> };
async function Content({ searchParams }: Props) {
  const { token } = await searchParams;
  return <ResetAction token={token ?? ""} />;
}
export default function ResetPasswordPage(props: Props) {
  return <Suspense fallback={<p className="p-8 text-slate-300">Loading recovery…</p>}>
    <Content {...props} />
  </Suspense>;
}
