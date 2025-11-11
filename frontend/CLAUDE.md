# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Next.js 16 frontend application using App Router, TypeScript, React 19, and Tailwind CSS v4. Part of the larger `a2i_project` with a .NET backend located in `../backend/`.

## Development Commands

**Package Manager**: Must use `pnpm` (not npm/yarn)

```bash
pnpm dev           # Start development server at http://localhost:3000
pnpm build         # Production build
pnpm start         # Run production build
pnpm lint          # Run ESLint
pnpm generate:api  # Generate API client from OpenAPI spec (v1.json)
```

## Architecture & Conventions

### Next.js App Router Structure

- **App directory**: All routes, layouts, and pages in `app/`
- **Server Components by default**: Only add `"use client"` when using state, effects, or browser APIs
- **Path aliases**: Use `@/*` for imports from project root (configured in tsconfig.json)
- **Metadata exports**: Export `metadata` constants for SEO in page/layout files

### Font Loading Pattern

Fonts are loaded using `next/font/google` and applied via CSS variables:

```tsx
import { Geist, Geist_Mono } from 'next/font/google';

const geistSans = Geist({
  variable: '--font-geist-sans',
  subsets: ['latin'],
});
```

Apply to body with `className={`${geistSans.variable} ${geistMono.variable}`}`

### Styling with Tailwind CSS v4

**Key differences from Tailwind v3:**

- Uses new `@tailwindcss/postcss` plugin (configured in postcss.config.mjs)
- Import with `@import "tailwindcss"` in CSS files (not the old @tailwind directives)
- Theme configuration via `@theme inline` directive in globals.css

**Design system:**

- Custom CSS variables in `globals.css` define `--background` and `--foreground`
- Automatic dark mode via `prefers-color-scheme` media query
- Use `dark:` prefix for dark mode variants
- Font variables: `--font-geist-sans` and `--font-geist-mono` available via `font-sans` and `font-mono` utilities

**Approach:**

- Utility-first: Use Tailwind classes directly; avoid custom CSS when possible
- Mobile-first: Use responsive breakpoints (`sm:`, `md:`, etc.)
- Dark mode support required for all UI components

### TypeScript Configuration

- **Strict mode enabled**: All code must be type-safe
- **Module resolution**: Uses `bundler` (not `node`)
- **JSX runtime**: `react-jsx` (no need to import React in components)
- **Target**: ES2017

### ESLint Setup

- Flat config format (eslint.config.mjs) with Next.js presets
- Includes both `eslint-config-next/core-web-vitals` and `eslint-config-next/typescript`
- Ignores: `.next/`, `out/`, `build/`, `next-env.d.ts`

### Image Optimization

- Always use `next/image` component for images
- Static assets in `public/` directory (accessed as `/filename.ext`)
- Add `priority` prop for above-the-fold images

## Backend Integration & API Client

### API Client Generation

The project uses **Orval** to generate a type-safe API client from the OpenAPI specification.

**Configuration:**

- OpenAPI spec: `v1.json` (downloaded from backend at `/openapi/v1.json`)
- Config file: `orval.config.ts`
- Generated client location: `lib/api/generated/`
- Custom fetch instance: `lib/api/custom-instance.ts`

**Key features:**

- Auto-generated React Query hooks for all endpoints
- Type-safe request/response models
- Organized by API tags (Auth, Customers, Subscriptions, Invoices, etc.)
- Custom instance handles authentication and error handling

**Regenerating the API client:**

```bash
# 1. Download latest OpenAPI spec from backend
curl http://localhost:5087/openapi/v1.json > v1.json

# 2. Generate client
pnpm generate:api
```

### Using the API Client

**Setup QueryProvider in your layout:**

```tsx
import { QueryProvider } from '@/lib/providers/query-provider';

export default function RootLayout({ children }) {
  return (
    <html>
      <body>
        <QueryProvider>{children}</QueryProvider>
      </body>
    </html>
  );
}
```

**Example: Using generated hooks**

```tsx
'use client';

import { useGetCustomerDetails } from '@/lib/api/generated/customers/customers';

export function CustomerProfile({ customerId }: { customerId: string }) {
  const { data, isLoading, error } = useGetCustomerDetails(customerId);

  if (isLoading) return <div>Loading...</div>;
  if (error) return <div>Error: {error.message}</div>;

  return <div>{data?.data?.email}</div>;
}
```

**Available API groups:**

- `lib/api/generated/auth/` - Authentication (login, register, refresh token)
- `lib/api/generated/account/` - User account management (profile, password, 2FA)
- `lib/api/generated/customers/` - Customer management
- `lib/api/generated/subscriptions/` - Stripe subscription operations
- `lib/api/generated/invoices/` - Invoice retrieval and PDF downloads
- `lib/api/generated/system/` - Health checks

### Authentication

The custom fetch instance (`lib/api/custom-instance.ts`) automatically:

- Adds `Authorization: Bearer <token>` header from localStorage
- Handles JSON serialization
- Throws errors for non-200 responses
- Configurable base URL via `NEXT_PUBLIC_API_BASE_URL` env variable

**Environment variables:**

```env
NEXT_PUBLIC_API_BASE_URL=http://localhost:5087
```

Backend API runs at `http://localhost:5087` by default (configurable via env).
