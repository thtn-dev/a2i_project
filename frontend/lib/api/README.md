# API Client

This directory contains the auto-generated API client using Orval and TanStack Query.

## Structure

```
lib/api/
├── custom-instance.ts          # Custom fetch wrapper with auth
├── generated/                  # Auto-generated (DO NOT EDIT MANUALLY)
│   ├── model/                 # TypeScript types/interfaces
│   ├── auth/                  # Authentication endpoints
│   ├── account/               # Account management endpoints
│   ├── customers/             # Customer endpoints
│   ├── subscriptions/         # Subscription endpoints
│   ├── invoices/              # Invoice endpoints
│   └── system/                # System/health check endpoints
└── README.md
```

## Usage Examples

### Queries (GET requests)

```tsx
"use client";

import { useGetCustomerDetails } from "@/lib/api/generated/customers/customers";

export function CustomerDetails({ customerId }: { customerId: string }) {
  const { data, isLoading, error, refetch } = useGetCustomerDetails(customerId);

  if (isLoading) return <div>Loading...</div>;
  if (error) return <div>Error: {error.message}</div>;

  return (
    <div>
      <h1>{data?.data?.fullName}</h1>
      <p>{data?.data?.email}</p>
      <button onClick={() => refetch()}>Refresh</button>
    </div>
  );
}
```

### Mutations (POST/PUT/DELETE requests)

```tsx
"use client";

import { useLoginUser } from "@/lib/api/generated/auth/auth";

export function LoginForm() {
  const { mutate: login, isPending, error } = useLoginUser();

  const handleSubmit = (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const formData = new FormData(e.currentTarget);

    login(
      {
        data: {
          username: formData.get("username") as string,
          password: formData.get("password") as string,
          rememberMe: false,
        },
      },
      {
        onSuccess: (response) => {
          // Store tokens
          localStorage.setItem("accessToken", response.data?.accessToken || "");
          localStorage.setItem("refreshToken", response.data?.refreshToken || "");
          // Redirect or update UI
        },
        onError: (error) => {
          console.error("Login failed:", error);
        },
      }
    );
  };

  return (
    <form onSubmit={handleSubmit}>
      <input name="username" type="text" required />
      <input name="password" type="password" required />
      <button type="submit" disabled={isPending}>
        {isPending ? "Logging in..." : "Login"}
      </button>
      {error && <p>Error: {error.message}</p>}
    </form>
  );
}
```

### With Query Options

```tsx
import { useGetCustomerInvoices } from "@/lib/api/generated/invoices/invoices";

export function InvoiceList({ customerId }: { customerId: string }) {
  const { data } = useGetCustomerInvoices(
    customerId,
    {
      Page: 1,
      PageSize: 10,
      Status: "paid",
    },
    {
      query: {
        staleTime: 5 * 60 * 1000, // 5 minutes
        refetchInterval: 30 * 1000, // Refetch every 30 seconds
      },
    }
  );

  return (
    <ul>
      {data?.data?.items?.map((invoice) => (
        <li key={invoice.id}>{invoice.invoiceNumber}</li>
      ))}
    </ul>
  );
}
```

## Regenerating the Client

When the backend API changes:

```bash
# 1. Download the latest OpenAPI spec
curl http://localhost:5087/openapi/v1.json > v1.json

# 2. Regenerate the client
pnpm generate:api
```

## Authentication

The custom instance automatically adds the Bearer token from localStorage to all requests. To set the token after login:

```ts
localStorage.setItem("accessToken", token);
```

To clear on logout:

```ts
localStorage.removeItem("accessToken");
localStorage.removeItem("refreshToken");
```

## Error Handling

All API errors are automatically thrown and can be caught in the `error` property of hooks:

```tsx
const { error } = useGetCustomerDetails(customerId);

if (error) {
  // error.message contains the error message
  // error.status contains the HTTP status code (if available)
}
```
