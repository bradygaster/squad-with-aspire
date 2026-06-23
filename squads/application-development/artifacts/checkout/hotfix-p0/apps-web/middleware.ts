// WI-1b — Next.js middleware registering CSP for /checkout/*.
// Drop into apps/web/middleware.ts (root). Re-exports the CSP middleware from
// apps/web/src/middleware/csp.ts (shipped by security-hardening-squad).

import type { NextRequest } from "next/server";
import { cspMiddleware } from "@/middleware/csp";

export function middleware(req: NextRequest) {
  return cspMiddleware(req);
}

export const config = {
  matcher: ["/checkout/:path*", "/csp-report"],
};
