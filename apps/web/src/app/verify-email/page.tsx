import { Suspense } from "react";
import VerifyAction from "./VerifyAction";
type Props = { searchParams: Promise<{ token?: string }> };
async function Content({ searchParams }: Props) {
  const { token } = await searchParams;
  return <VerifyAction token={token ?? ""} />;
}
export default function VerifyEmailPage(props: Props) {
  return <Suspense fallback={<p className="p-8 text-slate-300">Loading verification…</p>}>
    <Content {...props} />
  </Suspense>;
}
