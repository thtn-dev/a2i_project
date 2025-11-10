/**
 * Custom fetch instance with authentication and error handling
 */

const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5087";

export type ErrorType<Error> = Error;

export const customInstance = async <T>(
  url: string,
  config?: RequestInit & { params?: Record<string, unknown> }
): Promise<T> => {
  const { params, ...rest } = config || {};

  // Build URL with query params
  const searchParams = new URLSearchParams();
  if (params) {
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null) {
        searchParams.append(key, String(value));
      }
    });
  }

  const queryString = searchParams.toString();
  const fullUrl = `${API_BASE_URL}${url}${queryString ? `?${queryString}` : ""}`;

  // Get auth token from localStorage (adjust based on your auth implementation)
  const token = typeof window !== "undefined" ? localStorage.getItem("accessToken") : null;

  const response = await fetch(fullUrl, {
    ...rest,
    headers: {
      "Content-Type": "application/json",
      ...(token && { Authorization: `Bearer ${token}` }),
      ...rest.headers,
    },
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({
      message: response.statusText,
      status: response.status,
    }));
    throw error;
  }

  // Handle empty responses (204 No Content)
  if (response.status === 204) {
    return {} as T;
  }

  return response.json();
};

export default customInstance;
