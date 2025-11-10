# A2I Frontend - Copilot Instructions

## Project Overview
This is a Next.js 16 frontend application using the App Router, TypeScript, and Tailwind CSS v4. Part of the larger `a2i_project` which appears to have a .NET backend component.

## Architecture & Structure

### Next.js App Router
- **App directory**: All routes, layouts, and pages live in `app/`
- **Root layout**: `app/layout.tsx` defines global fonts (Geist Sans & Mono) and metadata
- **Page components**: Use default exports (e.g., `export default function Home()`)
- **Path aliases**: Use `@/*` for imports from project root (configured in `tsconfig.json`)

### Styling Philosophy
- **Tailwind CSS v4**: Uses new `@tailwindcss/postcss` plugin (different from v3)
- **CSS imports**: Import Tailwind with `@import "tailwindcss"` in CSS files
- **Theme system**: Custom CSS variables in `globals.css` with `@theme inline` directive
- **Dark mode**: Automatic via `prefers-color-scheme` media query (see `globals.css`)
- **Typography**: Utility-first with extensive use of Tailwind classes; avoid custom CSS when possible

### Font Loading Pattern
```tsx
import { Geist, Geist_Mono } from "next/font/google";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});
```
Apply font variables to body with `className={`${geistSans.variable} ${geistMono.variable}`}`

## Development Workflow

### Package Manager
- **Required**: Use `pnpm` (evidenced by `pnpm-lock.yaml`)
- **Common commands**:
  - `pnpm dev` - Start development server at http://localhost:3000
  - `pnpm build` - Production build
  - `pnpm lint` - Run ESLint

### TypeScript Configuration
- **Strict mode enabled**: All new code must be type-safe
- **Module resolution**: Uses `bundler` (not `node` or `node16`)
- **JSX**: `react-jsx` runtime (no need to import React in components)

### ESLint Setup
- Uses flat config format (`eslint.config.mjs`) with Next.js presets
- Configured with both `eslint-config-next/core-web-vitals` and `eslint-config-next/typescript`
- Ignores: `.next/`, `out/`, `build/`, `next-env.d.ts`

## Key Conventions

### Component Patterns
- Server Components by default (Next.js 16 App Router)
- Add `"use client"` directive only when needed (state, effects, browser APIs)
- Export metadata constants for SEO: `export const metadata: Metadata = { ... }`

### Styling Conventions
- Responsive design with mobile-first breakpoints (`sm:`, `md:`, etc.)
- Dark mode variants using `dark:` prefix
- Example from `page.tsx`: `className="flex min-h-screen items-center justify-center bg-zinc-50 dark:bg-black"`

### Image Optimization
- Always use Next.js `Image` component from `next/image`
- Static assets go in `public/` directory (accessed as `/filename.ext`)
- Add `priority` prop for above-the-fold images

## Backend Integration Context
- Frontend is part of larger `dotnet/a2i_project` structure
- Likely communicates with .NET backend (consider API routes in `app/api/` when needed)
- No API configuration visible yet - backend connection will need environment variables

## Quality Standards
- TypeScript strict mode - no `any` types without justification
- ESLint must pass before commits
- Responsive design required (test mobile/desktop)
- Dark mode support for all new UI components
