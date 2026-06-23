// WI-1b — CSP violation report sink. Returns 204 per spec.

import { NextRequest, NextResponse } from "next/server";

export const dynamic = "force-dynamic";

export async function POST(req: NextRequest) {
  try {
    const report = await req.json().catch(() => null);
    if (report) {
      // eslint-disable-next-line no-console
      console.warn("[csp-report]", JSON.stringify(report).slice(0, 2000));
    }
  } catch {
    // Swallow — never let a malformed report break the sink.
  }
  return new NextResponse(null, { status: 204 });
}
